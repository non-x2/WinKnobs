namespace WinKnobs.Services;

/// <summary>
/// メモリ圧縮（MemoryCompression / MMAgent）切り替えの結果。SystemProtectionOutcome と同流儀。
/// </summary>
public enum MemoryCompressionOutcome
{
    /// <summary>成功。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>
/// v0.1.17 システム・メンテナンス「メモリ圧縮 ON/OFF」（Phase 3）用の純粋関数群。
/// I/O を持たず xUnit 対象。PowerShell コマンド組み立てと、ExitCode＋出力からの結果分類。
///
/// 実行は <see cref="MemoryCompressionService"/>（PowerShell `Enable/Disable-MMAgent -mc`・要管理者）。
/// 本クラスは「文字列を作る／文字列を分類する」だけ＝完全にテスト可能。実行系の挙動は VM 実機検証の領域。
///
/// SystemProtectionHelper.Classify と同じ「ExitCode 0=成功・出力キーワードで権限不足を見分ける」方式。
/// メモリ圧縮は -Drive 等の引数が無く、有効化/無効化とも可逆（データ消失なし）。
/// </summary>
public static class MemoryCompressionHelper
{
    /// <summary>
    /// PowerShell -Command 文字列を組み立てる。
    /// enable=true → Enable-MMAgent -mc、false → Disable-MMAgent -mc。
    /// -ErrorAction Stop で非終了エラーも terminating 化し、powershell.exe の ExitCode!=0 を保証する
    /// （PowerShell 5.1/7 どちらでも権限不足を取りこぼさず、Classify が ExitCode を主判定にできる）。
    /// </summary>
    public static string BuildCommand(bool enable)
    {
        var cmdlet = enable ? "Enable-MMAgent" : "Disable-MMAgent";
        return $"{cmdlet} -mc -ErrorAction Stop";
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success。出力に権限不足キーワードがあれば AccessDenied。その他は Failed。
    /// 英語・日本語・PowerShell の典型エラー（PrivilegeNotHeld / requires elevation 等）に対応。
    /// SystemProtectionHelper.Classify と同じキーワード集。
    /// </summary>
    public static MemoryCompressionOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return MemoryCompressionOutcome.Success;

        // 権限不足キーワード集は ElevationClassifier に一元化（v0.1.17 で重複解消・SystemProtection と共有）。
        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return MemoryCompressionOutcome.AccessDenied;

        return MemoryCompressionOutcome.Failed;
    }
}
