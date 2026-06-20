using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Services;

namespace WinKnobs.ViewModels.Graphics;

/// <summary>
/// グラフィック・カテゴリ専用ペインの ViewModel（v0.3.0・HKCU・管理者不要）。
/// アプリ別GPU設定（列挙）＋全体トグル（ウィンドウゲーム最適化・Auto HDR）＋純正画面ランチャー。
/// 全ロジックは <see cref="GraphicsService"/>／<see cref="GpuPreferenceHelper"/> に委譲し、ここは UI 状態のみ。
/// </summary>
public partial class GraphicsListViewModel : ObservableObject
{
    private readonly GraphicsService _graphics;
    private readonly ExternalLauncherService _launcher;
    private readonly DialogService _dialogs;

    /// <summary>Load の世代カウンタ（fire-and-forget の古い結果上書き防止・見落とし事例 2026-06-09）。</summary>
    private int _loadGeneration;

    /// <summary>全体トグルの初期反映中は自動適用を抑制（ロード中に書き込まない）。</summary>
    private bool _suppressGlobalApply;

    [ObservableProperty]
    private string _statusMessage =
        "グラフィック設定。アプリごとに使う GPU を選べます。変更は対象アプリを次に起動したときに反映されます。";

    /// <summary>全体：ウィンドウゲームの最適化（DirectXUserGlobalSettings の SwapEffectUpgradeEnable）。</summary>
    [ObservableProperty] private bool _swapEffectGlobal;

    /// <summary>全体：Auto HDR（DirectXUserGlobalSettings の AutoHDREnable）。HDR 非対応ディスプレイでは無効。</summary>
    [ObservableProperty] private bool _autoHdrGlobal;

    /// <summary>退避（初めて変更する前の状態）があるか＝「↩ ぜんぶ元に戻す」ボタンの活性（E3）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreAllCommand))]
    private bool _hasGraphicsBackup;

    /// <summary>アプリ別GPU設定の行リスト。</summary>
    public ObservableCollection<GpuAppItem> Apps { get; } = new();

    public GraphicsListViewModel(
        GraphicsService graphics,
        ExternalLauncherService launcher,
        DialogService dialogs)
    {
        _graphics = graphics;
        _launcher = launcher;
        _dialogs = dialogs;
    }

    /// <summary>
    /// カテゴリ選択時に MainViewModel から呼ばれる。現在値を読み直す。
    /// E4：大量エントリでも UI を固めないよう async void＋Task.Run（読取のみ・安全）。
    /// </summary>
    public async void Load()
    {
        var gen = ++_loadGeneration;

        System.Collections.Generic.IReadOnlyList<GpuAppRecord> apps = Array.Empty<GpuAppRecord>();
        GlobalGraphicsState? global = null;
        bool hasBackup = false;
        try
        {
            await Task.Run(() =>
            {
                apps = _graphics.ListApps();
                global = _graphics.ReadGlobal();
                hasBackup = _graphics.HasBackup();
            });
        }
        catch
        {
            // 読取失敗は空表示（UI は壊さない）
        }

        if (gen != _loadGeneration) return;   // より新しい Load が走った → 破棄

        Apps.Clear();
        foreach (var a in apps)
            Apps.Add(new GpuAppItem(a.Path, a.GpuPreference, a.AutoHdr) { Applied = OnAppItemChanged });

        _suppressGlobalApply = true;
        SwapEffectGlobal = global?.SwapEffectUpgrade ?? false;
        AutoHdrGlobal = global?.AutoHdr ?? false;
        _suppressGlobalApply = false;

        HasGraphicsBackup = hasBackup;

        StatusMessage = Apps.Count == 0
            ? "アプリ別のGPU設定はまだありません。「➕ アプリを追加」から登録できます。"
            : $"アプリ別GPU設定 {Apps.Count} 件。変更は対象アプリを次に起動したときに反映されます。";
    }

    /// <summary>アプリ行の GPU/AutoHDR が変わった時の適用（GpuAppItem からコールバック）。</summary>
    private void OnAppItemChanged(GpuAppItem item)
    {
        try
        {
            _graphics.SetAppPreference(item.FullPath, item.SelectedGpuIndex, item.AutoHdr);
            HasGraphicsBackup = _graphics.HasBackup();
            StatusMessage =
                $"✅ {item.DisplayName}：{GpuPreferenceHelper.GpuPreferenceLabel(item.SelectedGpuIndex)}。" +
                "対象アプリを次に起動したときに反映されます。";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"GPU設定の変更に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    partial void OnSwapEffectGlobalChanged(bool value) { if (!_suppressGlobalApply) ApplyGlobal(); }
    partial void OnAutoHdrGlobalChanged(bool value)    { if (!_suppressGlobalApply) ApplyGlobal(); }

    private void ApplyGlobal()
    {
        try
        {
            _graphics.SetGlobal(SwapEffectGlobal, AutoHdrGlobal);
            HasGraphicsBackup = _graphics.HasBackup();
            StatusMessage = "✅ 全体のグラフィック設定を変更しました（一部はサインインし直すと反映されます）。";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"全体のグラフィック設定の変更に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    /// <summary>.exe を選んでアプリ別GPU設定の行を追加（GPU を選んだ時点で書き込み）。</summary>
    [RelayCommand]
    private void AddApp()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "GPU設定を追加するアプリ（.exe）を選択",
            Filter = "プログラム (*.exe)|*.exe|すべてのファイル (*.*)|*.*",
            CheckFileExists = true,
        };
        if (dlg.ShowDialog() != true) return;

        var path = dlg.FileName;
        var existing = Apps.FirstOrDefault(a => GpuPreferenceHelper.PathComparer.Equals(a.FullPath, path));
        if (existing != null)
        {
            StatusMessage = $"「{existing.DisplayName}」は既に一覧にあります。";
            return;
        }

        var item = new GpuAppItem(path, GpuPreferenceHelper.GpuLetWindowsDecide, false) { Applied = OnAppItemChanged };
        Apps.Add(item);
        // U-3：追加した時点で「Windows に任せる(0)」を即書込＝カテゴリを離れて戻っても消えない（サイレント消失防止）。
        OnAppItemChanged(item);
        StatusMessage =
            $"➕ {GpuPreferenceHelper.DisplayNameOf(path)} を追加しました（既定＝Windows に任せる）。GPU を選ぶと変更できます。";
    }

    /// <summary>アプリ別GPU設定をリセット（値削除＝Windows 既定へ）。</summary>
    [RelayCommand]
    private void ResetApp(GpuAppItem? item)
    {
        if (item == null) return;

        var ok = _dialogs.Confirm(
            $"「{item.DisplayName}」のGPU設定をリセットします（Windows の既定に戻します）。\n\n実行しますか？",
            "GPU設定をリセット");
        if (!ok) return;

        try
        {
            _graphics.ResetApp(item.FullPath);
            Apps.Remove(item);
            HasGraphicsBackup = _graphics.HasBackup();
            StatusMessage = $"🗑 {item.DisplayName} のGPU設定をリセットしました。";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"リセットに失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
    }

    /// <summary>WinKnobs で変更したグラフィック設定を「初めて触る前」へ復元。</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private void RestoreAll()
    {
        var ok = _dialogs.Confirm(
            "WinKnobs で変更したグラフィック設定（アプリ別GPU・全体トグル）を、すべて元の状態" +
            "（WinKnobs で初めて変更する前）に戻します。\n\n" +
            "※ WinKnobs で操作した後に、他のアプリや Windows の設定画面で追加した GPU 設定も削除されます。\n" +
            "※ Windows の予約値（グラフィック通知設定など）には触れません。\n\n実行しますか？",
            "グラフィック設定をぜんぶ元に戻す");
        if (!ok) return;

        try
        {
            _graphics.RestoreAll();
            StatusMessage = "↩ グラフィック設定（アプリ別GPU・全体トグル）を元の状態に戻しました。";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"元に戻す処理に失敗しました：\n{ex.Message}", "失敗");
            StatusMessage = $"❌ 失敗: {ex.Message}";
        }
        finally
        {
            Load();   // 退避クリア後の状態を UI に反映
        }
    }

    private bool CanRestoreAll() => HasGraphicsBackup;

    /// <summary>Windows 純正のグラフィック設定画面を開く（開くだけ・安全弁）。</summary>
    [RelayCommand]
    private void OpenWindowsGraphics()
    {
        if (_launcher.OpenGraphicsSettings())
        {
            StatusMessage = "🪟 Windows のグラフィック設定を開きました。";
            return;
        }
        if (_launcher.OpenGraphicsSettings("ms-settings:display"))
        {
            StatusMessage = "🪟 ディスプレイ設定を開きました（グラフィックの詳細設定はこの中にあります）。";
            return;
        }
        StatusMessage = "⚠️ グラフィック設定の画面を開けませんでした。";
    }
}

/// <summary>アプリ別GPU設定の1行（GPU 選択・Auto HDR トグル）。</summary>
public partial class GpuAppItem : ObservableObject
{
    /// <summary>exe フルパス（レジストリ値名）。</summary>
    public string FullPath { get; }

    /// <summary>表示名（ファイル名）。</summary>
    public string DisplayName { get; }

    /// <summary>GPU 選択肢（index 0/1/2 ＝ GpuPreference の値 0/1/2 に一致）。</summary>
    public System.Collections.Generic.IReadOnlyList<string> GpuChoices { get; } = new[]
    {
        "Windows に任せる（自動）",
        "省電力 GPU（内蔵）",
        "高パフォーマンス GPU（外付け）",
    };

    [ObservableProperty] private int _selectedGpuIndex;
    [ObservableProperty] private bool _autoHdr;

    /// <summary>true の間は変更時の自動適用を抑制（初期化中）。</summary>
    public bool SuppressApply { get; set; }

    /// <summary>ユーザーが GPU/AutoHDR を変えた時に呼ぶコールバック（VM が購読）。</summary>
    public Action<GpuAppItem>? Applied { get; set; }

    public GpuAppItem(string fullPath, int gpuPreference, bool autoHdr)
    {
        FullPath = fullPath;
        DisplayName = GpuPreferenceHelper.DisplayNameOf(fullPath);
        // バッキングフィールド直接代入＝OnChanged を発火させずに初期値を入れる（適用は走らない）。
        _selectedGpuIndex = GpuPreferenceHelper.IsValidGpuPreference(gpuPreference) ? gpuPreference : 0;
        _autoHdr = autoHdr;
    }

    partial void OnSelectedGpuIndexChanged(int value) { if (!SuppressApply) Applied?.Invoke(this); }
    partial void OnAutoHdrChanged(bool value)         { if (!SuppressApply) Applied?.Invoke(this); }
}
