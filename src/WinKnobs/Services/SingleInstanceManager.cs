using System.Diagnostics;
using System.Threading;

namespace WinKnobs.Services;

/// <summary>
/// 単一インスタンス制御（v0.3.1）。名前付き Mutex で二重起動を検出し、名前付き EventWaitHandle で
/// 2 個目→1 個目（プライマリ）へ「窓を前面化して」シグナルを送る。
///
/// 設計（@plan-validator 反映済・設計ドラフト v0.3.1 §3）：
/// - 検出は <b>存在ベース</b>：<c>new Mutex(initiallyOwned:false, name, out createdNew)</c> の createdNew で判定し、
///   WaitOne で所有権を取らない＝AbandonedMutexException を構造的に回避。プライマリがクラッシュしても
///   ハンドル全閉で OS が名前を破棄→次回起動が createdNew=true で自己修復。
/// - <b>WPF 非依存</b>：本クラスは Application/Dispatcher に一切触れない。前面化は <c>StartListening(Action)</c> で
///   注入されたコールバックに委譲する（テストは素の Action を渡せる＝headless 検証可・致命/エッジ反映）。
/// - 待機スレッドは未捕捉例外でプロセスを落とさない（try/catch）。Dispose は stop イベント Set→Join→破棄の順で
///   「WaitAny 使用中のハンドル破棄」を回避（致命3 反映）。
///
/// テスト時はコンストラクタに一意名（<c>Local\WinKnobsTest_{Guid}</c>）を注入し、ホスト上で実 WinKnobs が
/// 稼働中でも衝突しないようにする。
/// </summary>
public sealed class SingleInstanceManager : IDisposable
{
    private readonly string _mutexName;
    private readonly string _eventName;

    private Mutex? _mutex;
    private EventWaitHandle? _activateEvent;
    private ManualResetEvent? _stopEvent;   // プロセス内のみ（名前なし）＝待機スレッド停止用
    private Thread? _listenerThread;
    private Action? _onActivate;
    private bool _disposed;

    /// <param name="mutexName">省略時は <see cref="SingleInstanceNames.Mutex"/>。テストは一意名を渡す。</param>
    /// <param name="eventName">省略時は <see cref="SingleInstanceNames.Activate"/>。テストは一意名を渡す。</param>
    public SingleInstanceManager(string? mutexName = null, string? eventName = null)
    {
        _mutexName = mutexName ?? SingleInstanceNames.Mutex();
        _eventName = eventName ?? SingleInstanceNames.Activate();
    }

    /// <summary>
    /// 名前付き Mutex を作成し、自分がプライマリ（最初の作成者）かを返す。
    /// </summary>
    /// <param name="waitForRelease">
    /// true の場合（＝許可付き昇格再起動）、既存インスタンスが Mutex を解放するまで <paramref name="waitMs"/> ミリ秒
    /// 小刻みにリトライしてから取得を試みる。取得できなくても呼び出し側は窓を出してよい（自滅させない＝致命1 反映）。
    /// </param>
    /// <returns>true＝プライマリ（このインスタンスが代表）／false＝既に別インスタンスが存在（セカンダリ）。</returns>
    public bool TryAcquireOwnership(bool waitForRelease = false, int waitMs = 3000)
    {
        _mutex = new Mutex(initiallyOwned: false, _mutexName, out bool createdNew);
        if (createdNew) return true;

        if (!waitForRelease) return false;

        // 許可付き再起動：旧インスタンスが解放するまで待つ。
        // 自分のハンドルを閉じてから再作成しないと（自分が掴んでいる間は）名前が破棄されないので、
        // 「Dispose→sleep→再作成」を繰り返す。
        long deadline = Environment.TickCount64 + waitMs;
        while (Environment.TickCount64 < deadline)
        {
            if (_disposed) return false;                  // Dispose と競合したら離脱（重要2 反映）
            try { _mutex?.Dispose(); } catch { /* 続行 */ }
            _mutex = null;
            Thread.Sleep(100);
            if (_disposed) return false;                  // Sleep 中に Dispose が来たケース（重要2 反映）
            _mutex = new Mutex(initiallyOwned: false, _mutexName, out createdNew);
            if (createdNew) return true;
        }
        return false;   // タイムアウト：呼び出し側が窓を出すか判断（単一化は妥協・起動は妨げない）
    }

    /// <summary>
    /// プライマリ専用：別インスタンスからのシグナルを待ち受け、受けたら <paramref name="onActivate"/> を呼ぶ。
    /// コールバックは待機（バックグラウンド）スレッド上で呼ばれるため、WPF を触る場合は呼び出し側で Dispatcher 経由にすること。
    /// </summary>
    public void StartListening(Action onActivate)
    {
        if (_listenerThread != null) return;   // 二重起動ガード

        _onActivate = onActivate;
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, _eventName, out _);
        _stopEvent = new ManualResetEvent(false);
        _listenerThread = new Thread(ListenLoop)
        {
            IsBackground = true,
            Name = "WinKnobs.SingleInstance.Listener"
        };
        _listenerThread.Start();
    }

    private void ListenLoop()
    {
        // WaitAny の配列は 2 要素固定（最大 64 要素の上限に注意）。idx==1 は停止シグナル。
        var handles = new WaitHandle[] { _activateEvent!, _stopEvent! };
        try
        {
            while (true)
            {
                int idx = WaitHandle.WaitAny(handles);
                if (idx == 1) break;   // stop
                try { _onActivate?.Invoke(); }
                catch (Exception ex) { Trace.WriteLine($"[SingleInstance] activate callback failed: {ex}"); }
            }
        }
        catch (ObjectDisposedException)
        {
            // Dispose と競合した正常停止
        }
        catch (Exception ex)
        {
            // 待機スレッドの例外でプロセスを落とさない（致命3 反映）
            Trace.WriteLine($"[SingleInstance] listener loop failed: {ex}");
        }
    }

    /// <summary>
    /// セカンダリ専用：既存プライマリの EventWaitHandle を開いてシグナルを送る（窓を前面化させる）。
    /// プライマリがまだ生成前／権限不足で開けない場合は false（例外は出さず握り潰して呼び出し側は終了する）。
    /// </summary>
    public bool SignalPrimaryInstance()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(_eventName, out EventWaitHandle? handle) && handle != null)
            {
                using (handle) { handle.Set(); }
                return true;
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // 権限不足で開けない（ACL）：診断のみ残す（エッジ3 反映）
            Trace.WriteLine($"[SingleInstance] open event denied: {ex}");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[SingleInstance] signal failed: {ex}");
        }
        return false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 停止シグナル → 待機スレッドが WaitAny を抜けるのを待ってからハンドル破棄（致命3 反映）
        try { _stopEvent?.Set(); } catch { }
        try
        {
            // タイムアウトしてもスレッドは IsBackground=true でプロセス終了を妨げず、万一まだ WaitAny 中なら
            // ListenLoop の ObjectDisposedException catch が正常停止扱いにする（重要4 反映・診断ログのみ）。
            if (_listenerThread != null && !_listenerThread.Join(1000))
            {
                Trace.WriteLine("[SingleInstance] listener thread did not exit within timeout");
            }
        }
        catch { }

        try { _activateEvent?.Dispose(); } catch { }
        try { _stopEvent?.Dispose(); } catch { }
        try { _mutex?.Dispose(); } catch { }
    }
}
