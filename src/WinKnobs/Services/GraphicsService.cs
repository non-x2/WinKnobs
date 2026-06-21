using System.Text.Json;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>アプリ別GPU設定の1レコード（読取結果）。</summary>
public record GpuAppRecord(string Path, int GpuPreference, bool AutoHdr, string RawValue);

/// <summary>全体グラフィック設定（既定の設定）の状態。</summary>
public record GlobalGraphicsState(bool SwapEffectUpgrade, bool AutoHdr, string RawValue);

/// <summary>
/// グラフィック設定（HKCU・管理者不要）の読み書きサービス（v0.3.0）。
/// 全ロジックは <see cref="GpuPreferenceHelper"/>（純粋関数）に集約し、ここは I/O と退避だけを担う。
///
/// 退避方針（致命1 反映）：BackupService の maintenance ストア（FeatureKey="graphics"）に
///   「WinKnobs で初めて触る前」の REG_SZ 値（値名→値）を first-touch-wins で1度だけ JSON 退避。
///   Load（一覧の読み直し）では退避を上書きしない。RestoreAll 成功後は退避を消して次の first-touch を許す。
///
/// 型フィルタ（v0.3.0・実機検証で発覚した2バグの修正）：このキーには アプリ別GPU設定（REG_SZ）以外に
///   Windows 予約値（例 GraphicsFeaturesNotificationConfig＝REG_DWORD）が同居しうる。列挙・退避・復元は
///   すべて REG_SZ 値のみを対象とし、非SZ値は WinKnobs 管理外として「読まない・退避しない・復元時も触らない」
///   （温存）。よって「ぜんぶ元に戻す」が完全復元を保証する範囲は REG_SZ 値（アプリ別GPU・全体トグル）に限る。
///
/// 並行性：first-touch-wins は単一インスタンス前提。v0.3.1 で <see cref="SingleInstanceManager"/>（Local\ 名前付き Mutex）
///   により <b>同一セッション内は単一インスタンス強制済み</b>＝プロセス間の 2 窓同時操作は起こらない。
///   残る既知の制約は (a) クロスセッション（同一ユーザーの RDP+コンソール同時起動・非対象）と
///   (b) 同一プロセス内マルチスレッドの first-touch（本サービス側ロジックの領域）。
/// </summary>
public class GraphicsService
{
    /// <summary>maintenance 退避ストアの機能キー。</summary>
    public const string FeatureKey = "graphics";

    private readonly RegistryService _registry;
    private readonly BackupService _backup;

    public GraphicsService(RegistryService registry, BackupService backup)
    {
        _registry = registry;
        _backup = backup;
    }

    // =================== 読取 ===================

    /// <summary>アプリ別GPU設定の一覧を読む（全体予約名・既定値・異常値名は除外）。</summary>
    public IReadOnlyList<GpuAppRecord> ListApps()
    {
        var result = new List<GpuAppRecord>();
        foreach (var name in _registry.GetValueNames(GpuPreferenceHelper.KeyPath))
        {
            if (!GpuPreferenceHelper.IsAppValueName(name)) continue;
            if (!IsStringValue(name)) continue;   // v0.3.0：REG_SZ 以外（DWORD 予約値等）はアプリ扱いしない
            var raw = _registry.ReadValue<string>(GpuPreferenceHelper.KeyPath, name) ?? "";
            var tokens = GpuPreferenceHelper.ParseTokens(raw);
            result.Add(new GpuAppRecord(
                name,
                GpuPreferenceHelper.GetGpuPreference(tokens),
                GpuPreferenceHelper.GetAutoHdr(tokens),
                raw));
        }
        return result;
    }

    /// <summary>全体グラフィック設定（DirectXUserGlobalSettings）を読む。</summary>
    public GlobalGraphicsState ReadGlobal()
    {
        var raw = _registry.ReadValue<string>(GpuPreferenceHelper.KeyPath, GpuPreferenceHelper.GlobalValueName) ?? "";
        var tokens = GpuPreferenceHelper.ParseTokens(raw);
        return new GlobalGraphicsState(
            GpuPreferenceHelper.GetSwapEffect(tokens),
            GpuPreferenceHelper.GetAutoHdr(tokens),
            raw);
    }

    /// <summary>退避（WinKnobs で初めて触る前の状態）があるか＝「ぜんぶ戻す」ボタンの活性判定。</summary>
    public bool HasBackup() => _backup.HasMaintenanceOriginal(FeatureKey);

    // =================== 書込 ===================

    /// <summary>
    /// アプリ別GPU設定を書込（GpuPreference ＋ AutoHDR）。既存値があればその綴りの値名を使い、
    /// 未知トークンは保持。書込前に first-touch 退避（致命1）。REG_SZ 明示（kind=String）。
    /// </summary>
    public void SetAppPreference(string path, int gpuPreference, bool autoHdr)
    {
        if (!GpuPreferenceHelper.IsValidGpuPreference(gpuPreference))
            throw new ArgumentOutOfRangeException(nameof(gpuPreference));

        BackupAllFirstTouch();

        // 重要4：既存エントリがあればレジストリ格納形式の値名をそのまま使う（大小違いの二重エントリ防止）。
        var valueName = FindExistingValueName(path) ?? path;
        var raw = _registry.ReadValue<string>(GpuPreferenceHelper.KeyPath, valueName) ?? "";
        var tokens = GpuPreferenceHelper.ParseTokens(raw);

        tokens = GpuPreferenceHelper.SetToken(tokens, GpuPreferenceHelper.TokenGpuPreference, gpuPreference.ToString());
        tokens = autoHdr
            ? GpuPreferenceHelper.SetToken(tokens, GpuPreferenceHelper.TokenAutoHdr, "1")
            : GpuPreferenceHelper.RemoveToken(tokens, GpuPreferenceHelper.TokenAutoHdr);

        _registry.WriteValue(GpuPreferenceHelper.KeyPath, valueName, GpuPreferenceHelper.BuildValue(tokens), RegistryValueKind.String);
    }

    /// <summary>
    /// アプリ別GPU設定をリセット（＝その値を削除・Windows 公式の「リセット」と同義）。
    /// 致命3：DeleteValue はサイレント失敗するため削除後に存在を再確認し、残っていれば例外。
    /// </summary>
    public void ResetApp(string path)
    {
        BackupAllFirstTouch();

        var valueName = FindExistingValueName(path);
        if (valueName == null) return;   // 既に無い＝何もしない（冪等）

        _registry.DeleteValue(GpuPreferenceHelper.KeyPath, valueName);

        if (FindExistingValueName(path) != null)
            throw new InvalidOperationException(
                "リセットに失敗しました（他のアプリがレジストリキーを使用中の可能性があります）。");
    }

    /// <summary>全体グラフィック設定（ウィンドウゲーム最適化・Auto HDR）を書込。未知トークン保持・REG_SZ 明示。</summary>
    public void SetGlobal(bool swapEffectUpgrade, bool autoHdr)
    {
        BackupAllFirstTouch();

        var raw = _registry.ReadValue<string>(GpuPreferenceHelper.KeyPath, GpuPreferenceHelper.GlobalValueName) ?? "";
        var tokens = GpuPreferenceHelper.ParseTokens(raw);
        tokens = GpuPreferenceHelper.SetToken(tokens, GpuPreferenceHelper.TokenSwapEffect, swapEffectUpgrade ? "1" : "0");
        tokens = GpuPreferenceHelper.SetToken(tokens, GpuPreferenceHelper.TokenAutoHdr, autoHdr ? "1" : "0");

        _registry.WriteValue(GpuPreferenceHelper.KeyPath, GpuPreferenceHelper.GlobalValueName, GpuPreferenceHelper.BuildValue(tokens), RegistryValueKind.String);
    }

    // =================== 退避・復元 ===================

    /// <summary>キー全体を first-touch-wins で1度だけ JSON 退避（既に退避済みなら何もしない）。</summary>
    public void BackupAllFirstTouch()
    {
        if (_backup.HasMaintenanceOriginal(FeatureKey)) return;   // first-touch-wins

        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in _registry.GetValueNames(GpuPreferenceHelper.KeyPath))
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (!IsStringValue(name)) continue;   // v0.3.0：REG_SZ のみ退避（DWORD 等は管理外＝触らない・型変換させない）
            var v = _registry.ReadValue<string>(GpuPreferenceHelper.KeyPath, name);
            if (v != null) snapshot[name] = v;
        }
        _backup.SaveMaintenanceOriginalIfAbsent(
            FeatureKey, JsonSerializer.Serialize(snapshot));
    }

    /// <summary>
    /// グラフィック設定を「WinKnobs で初めて触る前」へ復元。退避に無い値は削除し、退避値を書き戻す。
    /// 退避破損（JsonException 等）は後始末だけして安全に抜ける（E2）。成功後は退避を削除（致命1）。
    /// </summary>
    public void RestoreAll()
    {
        if (!_backup.HasMaintenanceOriginal(FeatureKey)) return;

        var dict = _backup.LoadMaintenanceOriginals();
        if (!dict.TryGetValue(FeatureKey, out var json))
        {
            _backup.RemoveMaintenanceOriginal(FeatureKey);
            return;
        }

        Dictionary<string, string>? snapshot;
        try { snapshot = JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch (JsonException) { snapshot = null; }

        if (snapshot == null)
        {
            // 退避破損：これ以上壊さない。後始末のみ。
            _backup.RemoveMaintenanceOriginal(FeatureKey);
            return;
        }

        var keep = new HashSet<string>(snapshot.Keys, StringComparer.OrdinalIgnoreCase);

        // 退避に無い REG_SZ 値（退避後に増えたアプリ別GPU設定）だけ削除して退避時点へ戻す。
        // 型フィルタ（v0.3.0）：REG_DWORD 等の非SZ予約値（GraphicsFeaturesNotificationConfig 等）は退避もして
        //   いないので、ここでも一切削除しない＝温存する（「ぜんぶ戻す」が予約値を巻き添えにしない）。
        // DeleteValue は例外を握り潰して false を返す（過去事例 2026-06-10 のサイレント失敗）。失敗を集計し、
        //   主要復元（書戻し・退避クリア）を完了してから最後に明示報告する。
        int deleteFailed = 0;
        foreach (var name in _registry.GetValueNames(GpuPreferenceHelper.KeyPath))
        {
            if (string.IsNullOrEmpty(name)) continue;
            if (keep.Contains(name)) continue;
            if (!IsStringValue(name)) continue;   // 非SZ（DWORD 予約値等）は温存
            if (!_registry.DeleteValue(GpuPreferenceHelper.KeyPath, name)) deleteFailed++;
        }

        // 退避値を書き戻す（REG_SZ 明示）。万一 同名値が退避後に非SZ型へ化けていても、
        // ここで REG_SZ で上書きして退避時点の型・値へ戻す（型ごと復元する意図）。
        foreach (var kv in snapshot)
            _registry.WriteValue(GpuPreferenceHelper.KeyPath, kv.Key, kv.Value, RegistryValueKind.String);

        // 致命1：復元後は退避を消し、次操作で新規 first-touch を取れる状態にする。
        _backup.RemoveMaintenanceOriginal(FeatureKey);

        // サイレント失敗対策：主要復元の完了後に、削除できなかった件数を明示報告（呼び出し側が Error 表示）。
        if (deleteFailed > 0)
            throw new InvalidOperationException(
                $"{deleteFailed} 件のアプリ別GPU設定をリセットできませんでした（他のアプリがレジストリキーを使用中の可能性があります）。");
    }

    /// <summary>path に大小無視で一致する既存値名（レジストリ格納形式）を返す。無ければ null。</summary>
    private string? FindExistingValueName(string path)
    {
        foreach (var name in _registry.GetValueNames(GpuPreferenceHelper.KeyPath))
            if (GpuPreferenceHelper.IsAppValueName(name)
                && IsStringValue(name)   // v0.3.0：DWORD 予約値を「既存アプリ値」と誤認しない
                && string.Equals(name, path, StringComparison.OrdinalIgnoreCase))
                return name;
        return null;
    }

    /// <summary>
    /// 値が REG_SZ（アプリ別GPU設定の形式）か。DWORD 等の Windows 予約値を型でも除外するための判定（v0.3.0）。
    /// </summary>
    private bool IsStringValue(string valueName)
        => _registry.GetValueKind(GpuPreferenceHelper.KeyPath, valueName) == RegistryValueKind.String;
}
