using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// SystemProtectionHelper（v0.1.15 システムの保護 ON/OFF / Enable-Disable-ComputerRestore）の純粋関数テスト。
/// PowerShell 実行（SystemProtectionService）は VM 実機検証の領域でここでは扱わない。
/// </summary>
public class SystemProtectionHelperTests
{
    // ─── NormalizeDrive：→ "X:\" 形式 ───

    [Theory]
    [InlineData("C", "C:\\")]
    [InlineData("c", "C:\\")]
    [InlineData("C:", "C:\\")]
    [InlineData("C:\\", "C:\\")]
    [InlineData("d", "D:\\")]
    [InlineData("  D ", "D:\\")]
    [InlineData(null, "C:\\")]      // null → 既定 C
    [InlineData("", "C:\\")]        // 空 → 既定 C
    [InlineData("   ", "C:\\")]     // 空白 → 既定 C
    [InlineData("1", "C:\\")]       // 数字 → C にフォールバック
    [InlineData("@", "C:\\")]       // 記号 → C にフォールバック
    public void NormalizeDrive_to_x_colon_backslash(string? input, string expected)
        => Assert.Equal(expected, SystemProtectionHelper.NormalizeDrive(input));

    // ─── BuildCommand：PowerShell コマンド文字列 ───

    [Fact]
    public void BuildCommand_enable_uses_enable_cmdlet()
        => Assert.Equal("Enable-ComputerRestore -Drive 'C:\\' -ErrorAction Stop", SystemProtectionHelper.BuildCommand(true, "C"));

    [Fact]
    public void BuildCommand_disable_uses_disable_cmdlet()
        => Assert.Equal("Disable-ComputerRestore -Drive 'C:\\' -ErrorAction Stop", SystemProtectionHelper.BuildCommand(false, "C"));

    [Fact]
    public void BuildCommand_normalizes_drive()
        => Assert.Equal("Enable-ComputerRestore -Drive 'D:\\' -ErrorAction Stop", SystemProtectionHelper.BuildCommand(true, "d"));

    [Fact]
    public void BuildCommand_null_drive_defaults_to_c()
        => Assert.Equal("Disable-ComputerRestore -Drive 'C:\\' -ErrorAction Stop", SystemProtectionHelper.BuildCommand(false, null));

    [Fact]
    public void BuildCommand_appends_erroraction_stop()
        => Assert.EndsWith("-ErrorAction Stop", SystemProtectionHelper.BuildCommand(true, "C"));

    // ─── Classify：ExitCode 0=成功／権限不足キーワード=AccessDenied／他=Failed ───

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "anything at all")]
    [InlineData(0, null)]
    public void Classify_exit0_is_success(int code, string? output)
        => Assert.Equal(SystemProtectionOutcome.Success, SystemProtectionHelper.Classify(code, output));

    [Theory]
    [InlineData("Access is denied.")]
    [InlineData("アクセスが拒否されました")]
    [InlineData("アクセスは拒否されました")]   // ← 実機 Enable-ComputerRestore 非昇格の本物文言（"は"・2026-06-05 検証）
    [InlineData("This command requires elevation.")]
    [InlineData("PrivilegeNotHeldException: 必要な特権が保持されていません")]
    [InlineData("管理者として実行してください")]
    [InlineData("UnauthorizedAccessException")]
    [InlineData("You must run as administrator")]
    public void Classify_access_denied_keywords(string output)
        => Assert.Equal(SystemProtectionOutcome.AccessDenied, SystemProtectionHelper.Classify(1, output));

    [Theory]
    [InlineData(1, "VSS service is not running")]
    [InlineData(1, "some generic failure")]
    [InlineData(1, "")]
    [InlineData(1, null)]
    [InlineData(2, "unexpected")]
    public void Classify_other_nonzero_is_failed(int code, string? output)
        => Assert.Equal(SystemProtectionOutcome.Failed, SystemProtectionHelper.Classify(code, output));

    // ExitCode 0 が最優先（出力に denied があっても成功なら Success）
    [Fact]
    public void Classify_exit0_wins_over_denied_text()
        => Assert.Equal(SystemProtectionOutcome.Success, SystemProtectionHelper.Classify(0, "access denied (但し成功)"));
}
