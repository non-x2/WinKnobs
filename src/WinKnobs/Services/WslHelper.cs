namespace WinKnobs.Services;

/// <summary>
/// WSL 機能切り替えの結果（v0.2.1 Step3）。
/// SysmainOutcome 同流儀＋PartiallyEnabled（2機能 done-stack のロールバック失敗・plan-validator 重要指摘）。
/// </summary>
public enum WslOutcome
{
    /// <summary>成功（再起動で確定）。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>機能名がこの Windows に存在しない（エディション/ビルド差）。</summary>
    FeatureNotFound,
    /// <summary>2機能のうち片方だけ有効化された中途半端な状態（ロールバックも失敗）。</summary>
    PartiallyEnabled,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>機能の現在状態（Get-WindowsOptionalFeature の State パース結果）。</summary>
public enum WslFeatureState
{
    Enabled,
    Disabled,
    /// <summary>EnablePending / DisablePending（再起動待ち）。</summary>
    Pending,
    Unknown
}

/// <summary>
/// v0.2.1「WSL 2 機能 ON/OFF」用の純粋関数群。I/O を持たず xUnit 対象。
/// 実行は <see cref="WslService"/>（PowerShell Enable/Disable-WindowsOptionalFeature・要管理者）。
///
/// 設計（v0.2.1 設計ドラフト §2-7＋🔧反映6）：
///   - ON＝Microsoft-Windows-Subsystem-Linux ＋ VirtualMachinePlatform の2機能（WSL2 に両方必要）。
///     2機能は C# 側 done-stack で1つずつ有効化し、失敗時は逆順ロールバック（RemoteDesktopService.Enable 同型）。
///   - OFF＝WSL 本体のみ無効化。VirtualMachinePlatform は残す（WSA / Windows サンドボックス等の巻き添え防止）。
///   - `wsl --install` は不採用（ディストリビューションのダウンロードまで走る＝機能トグルとして過剰）。
///   - DISM 系は遅いためタイムアウトは 300 秒（Sysmain の 60 秒から個別延長）。
/// </summary>
public static class WslHelper
{
    /// <summary>WSL 本体の機能名。</summary>
    public const string WslFeatureName = "Microsoft-Windows-Subsystem-Linux";

    /// <summary>WSL2 実行基盤（軽量ユーティリティ VM）の機能名。</summary>
    public const string VmPlatformFeatureName = "VirtualMachinePlatform";

    /// <summary>
    /// 機能の現在状態を取得するコマンド。State だけを出力する（パースを単純に保つ）。
    /// </summary>
    public static string BuildGetStateCommand(string featureName)
        => $"(Get-WindowsOptionalFeature -Online -FeatureName '{featureName}' -ErrorAction Stop).State";

    /// <summary>
    /// 機能を有効化するコマンド。-All で依存機能も有効化・-NoRestart で再起動はユーザー判断に委ねる。
    /// Out-Null で RestartNeeded 表など大きめの出力を抑制（パイプバッファ対策）。
    /// </summary>
    public static string BuildEnableCommand(string featureName)
        => $"Enable-WindowsOptionalFeature -Online -FeatureName '{featureName}' -All -NoRestart -ErrorAction Stop | Out-Null";

    /// <summary>機能を無効化するコマンド。</summary>
    public static string BuildDisableCommand(string featureName)
        => $"Disable-WindowsOptionalFeature -Online -FeatureName '{featureName}' -NoRestart -ErrorAction Stop | Out-Null";

    /// <summary>
    /// Get-WindowsOptionalFeature の State 出力をパースする。
    /// State は enum 値（Enabled/Disabled/EnablePending/DisablePending 等）で英語固定＝ロケール非依存。
    /// </summary>
    public static WslFeatureState ParseFeatureState(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return WslFeatureState.Unknown;
        var t = output.Trim();

        // Pending を先に判定（"EnablePending" は "Enabled" を含まないが、前方一致の取り違え防止で明示順）
        if (t.Contains("EnablePending", System.StringComparison.OrdinalIgnoreCase) ||
            t.Contains("DisablePending", System.StringComparison.OrdinalIgnoreCase))
            return WslFeatureState.Pending;
        if (t.Contains("Enabled", System.StringComparison.OrdinalIgnoreCase))
            return WslFeatureState.Enabled;
        if (t.Contains("Disabled", System.StringComparison.OrdinalIgnoreCase))
            return WslFeatureState.Disabled;
        return WslFeatureState.Unknown;
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success。
    /// 機能不在キーワード → FeatureNotFound（AccessDenied より先＝誤分類防止・Sysmain の ServiceNotFound 同流儀）。
    /// 権限不足キーワード → AccessDenied（ElevationClassifier 一元化）。その他は Failed。
    /// </summary>
    public static WslOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return WslOutcome.Success;

        if (LooksLikeFeatureNotFound(output))
            return WslOutcome.FeatureNotFound;

        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return WslOutcome.AccessDenied;

        return WslOutcome.Failed;
    }

    /// <summary>
    /// 出力が「機能が見つからない」エラーらしいか（純粋関数）。
    /// 典型：英 "Feature name Microsoft-Windows-Subsystem-Linux is unknown."（DISM 0x800f080c）／
    /// 日 "機能名 ... は不明です"。HRESULT 0x800f080c はロケール非依存の強いシグナル。
    /// </summary>
    public static bool LooksLikeFeatureNotFound(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        var lower = output.ToLowerInvariant();

        if (lower.Contains("0x800f080c")) return true;
        if (lower.Contains("feature name") && lower.Contains("unknown")) return true;
        if (output.Contains("機能名") && output.Contains("不明")) return true;

        return false;
    }
}
