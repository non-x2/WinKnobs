namespace WinKnobs.Services;

/// <summary>
/// システムの保護（システムの復元）切り替えの結果。RestorePointResult と同流儀。
/// </summary>
public enum SystemProtectionOutcome
{
    /// <summary>成功。</summary>
    Success,
    /// <summary>管理者権限不足（昇格が必要）。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>
/// v0.1.15 システム・メンテナンス「システムの保護 ON/OFF」（Phase 2）用の純粋関数群。
/// I/O を持たず xUnit 対象。PowerShell コマンド組み立てと、ExitCode＋出力からの結果分類。
///
/// 実行は <see cref="SystemProtectionService"/>（PowerShell `Enable/Disable-ComputerRestore`・要管理者）。
/// 本クラスは「文字列を作る／文字列を分類する」だけ＝完全にテスト可能。実行系の挙動は VM 実機検証の領域。
///
/// RestorePointService.Classify と同じ「ExitCode 0=成功・出力キーワードで権限不足を見分ける」方式。
/// </summary>
public static class SystemProtectionHelper
{
    /// <summary>既定ドライブ（システムドライブ）。</summary>
    public const string DefaultDrive = "C";

    /// <summary>
    /// ドライブ表記を "C:\" 形式に正規化。"c" / "C:" / "C:\" / null/空 → "C:\"。
    /// 先頭の英字1文字だけを採用（不正入力を既定 C に倒さず、与えられた文字を尊重）。
    /// </summary>
    public static string NormalizeDrive(string? drive)
    {
        var d = string.IsNullOrWhiteSpace(drive) ? DefaultDrive : drive.Trim();
        char letter = char.ToUpperInvariant(d[0]);
        if (letter < 'A' || letter > 'Z') letter = 'C';   // 記号等が来たら C に
        return $"{letter}:\\";
    }

    /// <summary>
    /// PowerShell -Command 文字列を組み立てる。
    /// enable=true → Enable-ComputerRestore、false → Disable-ComputerRestore。ドライブは単一引用符で安全に渡す。
    /// -ErrorAction Stop で非終了エラーも terminating 化し、powershell.exe の ExitCode!=0 を保証する
    /// （PowerShell 5.1/7 どちらでも権限不足を取りこぼさず、Classify が ExitCode を主判定にできる）。
    /// </summary>
    public static string BuildCommand(bool enable, string? drive)
    {
        var d = NormalizeDrive(drive);
        var cmdlet = enable ? "Enable-ComputerRestore" : "Disable-ComputerRestore";
        return $"{cmdlet} -Drive '{d}' -ErrorAction Stop";
    }

    /// <summary>
    /// 実行結果を分類。ExitCode 0=Success。出力に権限不足キーワードがあれば AccessDenied。その他は Failed。
    /// 英語・日本語・PowerShell の典型エラー（PrivilegeNotHeld / requires elevation 等）に対応。
    /// </summary>
    public static SystemProtectionOutcome Classify(int exitCode, string? output)
    {
        if (exitCode == 0) return SystemProtectionOutcome.Success;

        // 権限不足キーワード集は ElevationClassifier に一元化（v0.1.17 で重複解消・MemoryCompression と共有）。
        if (ElevationClassifier.LooksLikeAccessDenied(output))
            return SystemProtectionOutcome.AccessDenied;

        return SystemProtectionOutcome.Failed;
    }
}
