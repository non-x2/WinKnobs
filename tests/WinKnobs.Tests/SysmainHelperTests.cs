using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.0 SysmainHelper（純粋関数）のテスト。
/// BuildCommand のコマンド組み立てと、Classify の4分類（Success/AccessDenied/ServiceNotFound/Failed）。
/// MemoryCompressionHelperTests と同流儀。
/// </summary>
public class SysmainHelperTests
{
    // ---------- BuildCommand ----------

    [Fact]
    public void BuildCommand_有効化は種別Automaticが先でStartが後()
    {
        var cmd = SysmainHelper.BuildCommand(true);
        Assert.Contains("Set-Service -Name SysMain -StartupType Automatic", cmd);
        Assert.Contains("Start-Service -Name SysMain", cmd);
        // 順序：Disabled のままでは Start が失敗するため、種別変更が先。
        Assert.True(cmd.IndexOf("Set-Service") < cmd.IndexOf("Start-Service"));
        Assert.DoesNotContain("Stop-Service", cmd);
    }

    [Fact]
    public void BuildCommand_無効化はStopが先でDisabledが後()
    {
        var cmd = SysmainHelper.BuildCommand(false);
        Assert.Contains("Stop-Service -Name SysMain -Force", cmd);
        Assert.Contains("Set-Service -Name SysMain -StartupType Disabled", cmd);
        // 順序：先に止める（動いたまま Disabled だけだと次回起動まで残る）。
        Assert.True(cmd.IndexOf("Stop-Service") < cmd.IndexOf("Set-Service"));
        Assert.DoesNotContain("Start-Service", cmd);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void BuildCommand_ErrorActionStopで非終了エラーをterminating化(bool enable)
    {
        // ExitCode!=0 を保証し Classify の主判定を ExitCode にできる（既存3サービスと同方針）。
        var cmd = SysmainHelper.BuildCommand(enable);
        Assert.Contains("-ErrorAction Stop", cmd);
    }

    // ---------- Classify ----------

    [Theory]
    [InlineData(0, null)]
    [InlineData(0, "")]
    [InlineData(0, "WARNING: something")]   // ExitCode 0 なら出力に関わらず成功
    public void Classify_ExitCode0は常にSuccess(int exitCode, string? output)
        => Assert.Equal(SysmainOutcome.Success, SysmainHelper.Classify(exitCode, output));

    [Theory]
    [InlineData("Service 'SysMain (SysMain)' cannot be stopped due to the following error: Cannot open SysMain service on computer '.'. Access is denied")]
    [InlineData("PermissionDenied: アクセスが拒否されました。")]
    [InlineData("This operation requires elevation.")]
    [InlineData("管理者として実行してください")]
    public void Classify_権限不足キーワードはAccessDenied(string output)
        => Assert.Equal(SysmainOutcome.AccessDenied, SysmainHelper.Classify(1, output));

    [Theory]
    [InlineData("Stop-Service : Cannot find any service with service name 'SysMain'.")]
    [InlineData("Set-Service : サービス名 'SysMain' のサービスが見つかりません。")]
    [InlineData("CategoryInfo : ObjectNotFound: (SysMain:String) [Stop-Service], ServiceCommandException")]
    [InlineData("FullyQualifiedErrorId : NoServiceFoundForGivenName,Microsoft.PowerShell.Commands.StopServiceCommand")]
    public void Classify_サービス不在はServiceNotFound(string output)
        => Assert.Equal(SysmainOutcome.ServiceNotFound, SysmainHelper.Classify(1, output));

    [Fact]
    public void Classify_サービス不在かつ権限風キーワード混在でもServiceNotFound優先()
    {
        // 「Cannot find any service」と access 系単語が同居しても誤って AccessDenied にしない（判定順の保証）。
        var output = "Cannot find any service with service name 'SysMain'. Check access rights.";
        Assert.Equal(SysmainOutcome.ServiceNotFound, SysmainHelper.Classify(1, output));
    }

    [Theory]
    [InlineData(1, null)]
    [InlineData(1, "")]
    [InlineData(1, "Some unrelated error occurred")]
    [InlineData(2, "timeout waiting for service")]
    public void Classify_その他の失敗はFailed(int exitCode, string? output)
        => Assert.Equal(SysmainOutcome.Failed, SysmainHelper.Classify(exitCode, output));

    // ---------- LooksLikeServiceNotFound ----------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ObjectNotFound: path 'C:\\foo' not found")]   // ObjectNotFound 単独（SysMain なし）は不在扱いしない
    [InlineData("Access is denied")]
    public void LooksLikeServiceNotFound_非該当はfalse(string? output)
        => Assert.False(SysmainHelper.LooksLikeServiceNotFound(output));

    [Theory]
    [InlineData("CANNOT FIND ANY SERVICE with name 'SysMain'")]   // 大文字小文字非依存
    [InlineData("noservicefoundforgivenname")]
    [InlineData("ObjectNotFound (sysmain)")]
    [InlineData("指定されたサービスが見つかりません（サービスが見つかりません）")]
    public void LooksLikeServiceNotFound_該当はtrue(string output)
        => Assert.True(SysmainHelper.LooksLikeServiceNotFound(output));
}
