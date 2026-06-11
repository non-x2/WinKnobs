using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.1 Step3 WslHelper（純粋関数）のテスト。
/// コマンド組み立て・State パース（英語固定 enum 値）・Classify の分類順
/// （FeatureNotFound を AccessDenied より先に判定＝Sysmain の ServiceNotFound 同流儀）。
/// </summary>
public class WslHelperTests
{
    // ---------- BuildCommand ----------

    [Fact]
    public void BuildEnableCommand_AllとNoRestartとErrorActionStopを含む()
    {
        var cmd = WslHelper.BuildEnableCommand(WslHelper.WslFeatureName);
        Assert.Contains("Enable-WindowsOptionalFeature", cmd);
        Assert.Contains("-FeatureName 'Microsoft-Windows-Subsystem-Linux'", cmd);
        Assert.Contains("-All", cmd);
        Assert.Contains("-NoRestart", cmd);
        Assert.Contains("-ErrorAction Stop", cmd);
        Assert.Contains("Out-Null", cmd);   // 大きめ出力の抑制（パイプバッファ対策）
    }

    [Fact]
    public void BuildDisableCommand_NoRestartを含みAllは含まない()
    {
        var cmd = WslHelper.BuildDisableCommand(WslHelper.WslFeatureName);
        Assert.Contains("Disable-WindowsOptionalFeature", cmd);
        Assert.Contains("-NoRestart", cmd);
        Assert.DoesNotContain("-All", cmd);
    }

    [Fact]
    public void BuildGetStateCommand_Stateだけを出力する形()
    {
        var cmd = WslHelper.BuildGetStateCommand(WslHelper.VmPlatformFeatureName);
        Assert.Contains("Get-WindowsOptionalFeature", cmd);
        Assert.Contains("'VirtualMachinePlatform'", cmd);
        Assert.Contains(".State", cmd);
    }

    // ---------- ParseFeatureState ----------

    [Theory]
    [InlineData("Enabled", WslFeatureState.Enabled)]
    [InlineData("Disabled", WslFeatureState.Disabled)]
    [InlineData("EnablePending", WslFeatureState.Pending)]
    [InlineData("DisablePending", WslFeatureState.Pending)]
    [InlineData("  Enabled  \r\n", WslFeatureState.Enabled)]   // 前後空白・改行
    [InlineData("", WslFeatureState.Unknown)]
    [InlineData(null, WslFeatureState.Unknown)]
    [InlineData("???", WslFeatureState.Unknown)]
    public void ParseFeatureState_各状態(string? output, WslFeatureState expected)
    {
        Assert.Equal(expected, WslHelper.ParseFeatureState(output));
    }

    // ---------- Classify ----------

    [Fact]
    public void Classify_ExitCode0はSuccess()
    {
        Assert.Equal(WslOutcome.Success, WslHelper.Classify(0, ""));
        Assert.Equal(WslOutcome.Success, WslHelper.Classify(0, "なにか出力があっても成功"));
    }

    [Theory]
    [InlineData("Error: 0x800f080c Feature name Microsoft-Windows-Subsystem-Linux is unknown.")]
    [InlineData("エラー: 0x800F080C")]   // HRESULT は大文字でも
    [InlineData("Feature name FooBar is unknown")]
    [InlineData("機能名 Microsoft-Windows-Subsystem-Linux は不明です")]
    public void Classify_機能不在はFeatureNotFound(string output)
    {
        Assert.Equal(WslOutcome.FeatureNotFound, WslHelper.Classify(1, output));
    }

    [Fact]
    public void Classify_機能不在はAccessDeniedより先に判定()
    {
        // 「アクセスが拒否」っぽい語と機能不在 HRESULT が同居しても FeatureNotFound 優先
        var output = "0x800f080c access is denied 風の文字列";
        Assert.Equal(WslOutcome.FeatureNotFound, WslHelper.Classify(1, output));
    }

    [Theory]
    [InlineData("Access is denied")]
    [InlineData("管理者特権が必要です")]
    public void Classify_権限不足はAccessDenied(string output)
    {
        Assert.Equal(WslOutcome.AccessDenied, WslHelper.Classify(1, output));
    }

    [Theory]
    [InlineData("なにか別のエラー")]
    [InlineData("")]
    [InlineData(null)]
    public void Classify_その他はFailed(string? output)
    {
        Assert.Equal(WslOutcome.Failed, WslHelper.Classify(1, output));
    }

    // ---------- LooksLikeFeatureNotFound ----------

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("unknown だけでは不十分", false)]   // "feature name" との組が必要
    [InlineData("feature name X is unknown", true)]
    [InlineData("0x800f080c", true)]
    public void LooksLikeFeatureNotFound_判定(string? output, bool expected)
    {
        Assert.Equal(expected, WslHelper.LooksLikeFeatureNotFound(output));
    }

    // ---------- 定数 ----------

    [Fact]
    public void 定数_機能名()
    {
        Assert.Equal("Microsoft-Windows-Subsystem-Linux", WslHelper.WslFeatureName);
        Assert.Equal("VirtualMachinePlatform", WslHelper.VmPlatformFeatureName);
    }
}
