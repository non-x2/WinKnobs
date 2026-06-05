using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// MemoryCompressionHelper（v0.1.17 メモリ圧縮 ON/OFF / Enable-Disable-MMAgent -mc）の純粋関数テスト。
/// PowerShell 実行（MemoryCompressionService）は VM 実機検証の領域でここでは扱わない。
/// </summary>
public class MemoryCompressionHelperTests
{
    // ─── BuildCommand：PowerShell コマンド文字列 ───

    [Fact]
    public void BuildCommand_enable_uses_enable_cmdlet()
        => Assert.Equal("Enable-MMAgent -mc -ErrorAction Stop", MemoryCompressionHelper.BuildCommand(true));

    [Fact]
    public void BuildCommand_disable_uses_disable_cmdlet()
        => Assert.Equal("Disable-MMAgent -mc -ErrorAction Stop", MemoryCompressionHelper.BuildCommand(false));

    [Fact]
    public void BuildCommand_appends_erroraction_stop()
        => Assert.EndsWith("-ErrorAction Stop", MemoryCompressionHelper.BuildCommand(true));

    [Fact]
    public void BuildCommand_contains_mc_switch()
        => Assert.Contains("-mc", MemoryCompressionHelper.BuildCommand(false));

    // ─── Classify：ExitCode 0=成功／権限不足キーワード=AccessDenied／他=Failed ───

    [Theory]
    [InlineData(0, "")]
    [InlineData(0, "anything at all")]
    [InlineData(0, null)]
    public void Classify_exit0_is_success(int code, string? output)
        => Assert.Equal(MemoryCompressionOutcome.Success, MemoryCompressionHelper.Classify(code, output));

    [Theory]
    [InlineData("Access is denied.")]
    [InlineData("アクセスが拒否されました")]
    [InlineData("アクセスは拒否されました")]
    [InlineData("This command requires elevation.")]
    [InlineData("PrivilegeNotHeldException: 必要な特権が保持されていません")]
    [InlineData("管理者として実行してください")]
    [InlineData("UnauthorizedAccessException")]
    [InlineData("You must run as administrator")]
    public void Classify_access_denied_keywords(string output)
        => Assert.Equal(MemoryCompressionOutcome.AccessDenied, MemoryCompressionHelper.Classify(1, output));

    [Theory]
    [InlineData(1, "MMAgent service is not available")]
    [InlineData(1, "some generic failure")]
    [InlineData(1, "")]
    [InlineData(1, null)]
    [InlineData(2, "unexpected")]
    public void Classify_other_nonzero_is_failed(int code, string? output)
        => Assert.Equal(MemoryCompressionOutcome.Failed, MemoryCompressionHelper.Classify(code, output));

    // ExitCode 0 が最優先（出力に denied があっても成功なら Success）
    [Fact]
    public void Classify_exit0_wins_over_denied_text()
        => Assert.Equal(MemoryCompressionOutcome.Success, MemoryCompressionHelper.Classify(0, "access denied (但し成功)"));
}
