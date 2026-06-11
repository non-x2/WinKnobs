using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.1 Step4 CoreIsolationHelper（純粋関数）のテスト。
/// 定数（レジストリパスのタイポ防止）と状態表示・書込値変換。
/// </summary>
public class CoreIsolationHelperTests
{
    [Fact]
    public void 定数_レジストリパスと値名()
    {
        Assert.Equal(
            @"HKLM\SYSTEM\CurrentControlSet\Control\DeviceGuard\Scenarios\HypervisorEnforcedCodeIntegrity",
            CoreIsolationHelper.HvciKeyPath);
        Assert.Equal("Enabled", CoreIsolationHelper.EnabledValueName);
        Assert.Equal(
            @"HKLM\SOFTWARE\Policies\Microsoft\Windows\DeviceGuard",
            CoreIsolationHelper.PolicyKeyPath);
        Assert.Equal("HypervisorEnforcedCodeIntegrity", CoreIsolationHelper.PolicyHvciValueName);
    }

    [Theory]
    [InlineData(1, "有効")]
    [InlineData(0, "無効")]
    [InlineData(null, "無効（未設定）")]
    [InlineData(2, "不明（2）")]   // 想定外の値は不明として正直表示
    public void DescribeState_各状態(int? value, string expected)
    {
        Assert.Equal(expected, CoreIsolationHelper.DescribeState(value));
    }

    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ToRegistryValue_変換(bool enable, int expected)
    {
        Assert.Equal(expected, CoreIsolationHelper.ToRegistryValue(enable));
    }
}
