namespace WinKnobs.Services;

/// <summary>
/// Sysmain サービス切り替えの結果。MemoryCompressionOutcome と同流儀＋ServiceNotFound（plan-validator 指摘5）。
/// </summary>
public enum SysmainOutcome
{
    /// <summary>成功。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>SysMain サービスがこの PC に存在しない（エディション/構成差）。</summary>
    ServiceNotFound,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>
/// v0.2.0「Sysmain サービス 有効/無効」用の純粋関数群。
/// I/O を持たず xUnit 対象。PowerShell コマンド組み立てと、ExitCode＋出力からの結果分類。
///
/// 実行は <see cref="SysmainService"/>（PowerShell Set-Service / Stop-Service・要管理者）。
/// レジストリ直叩き（Services\SysMain\Start=4）は不採用：再起動まで実状態と乖離するため SCM 経由が正道。
///
/// MemoryCompressionHelper と同じ「ExitCode 0=成功・出力キーワードで分類」方式。
/// 追加で「サービス不在」を AccessDenied と誤分類しないよう ServiceNotFound を先に判定する。
/// </summary>
public static class SysmainHelper
{
    /// <summary>
    /// PowerShell -Command 文字列を組み立てる。
    /// 有効化：スタートアップ種別を Automatic にしてから Start（先に種別＝Disabled のままだと Start が失敗するため）。
    /// 無効化：先に Stop してから Disabled（動いたまま Disabled にしても次回起動まで残るため）。
    /// -Force は SysMain に依存するサービスも止める（通常は無し・念のため）。
    /// -ErrorAction Stop で非終了エラーも terminating 化し、powershell.exe の ExitCode!=0 を保証する。
    /// </summary>
    public static string BuildCommand(bool enable)
    {
        return enable
            ? "Set-Service -Name SysMain -StartupType Automatic -ErrorAction Stop; Start-Service -Name SysMain -ErrorAction Stop"
            : "Stop-Service -Name SysMain -Force -ErrorAction Stop; Set-Service -Name SysMain -StartupType Disabled -ErrorAction Stop";
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success。
    /// サービス不在キーワード → ServiceNotFound（AccessDenied 判定より先＝誤分類防止・plan-validator 指摘5）。
    /// 権限不足キーワード → AccessDenied（ElevationClassifier 一元化）。その他は Failed。
    /// </summary>
    public static SysmainOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return SysmainOutcome.Success;

        if (LooksLikeServiceNotFound(output))
            return SysmainOutcome.ServiceNotFound;

        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return SysmainOutcome.AccessDenied;

        return SysmainOutcome.Failed;
    }

    /// <summary>
    /// 出力が「サービスが見つからない」エラーらしいか（純粋関数）。
    /// PowerShell の典型：英 "Cannot find any service with service name 'SysMain'"／
    /// 日 "サービス名 'SysMain' のサービスが見つかりません"／例外型 ServiceCommandException・ObjectNotFound。
    /// </summary>
    public static bool LooksLikeServiceNotFound(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return false;
        var lower = output.ToLowerInvariant();

        // 英語：Cannot find any service / NoServiceFoundForGivenName
        if (lower.Contains("cannot find any service")) return true;
        if (lower.Contains("noservicefoundforgivenname")) return true;
        // ErrorCategory：ObjectNotFound ＋ サービス名（単独の ObjectNotFound はパス系でも出るため SysMain と組で判定）
        if (lower.Contains("objectnotfound") && lower.Contains("sysmain")) return true;
        // 日本語：〜のサービスが見つかりません
        if (output.Contains("サービスが見つかりません")) return true;

        return false;
    }
}
