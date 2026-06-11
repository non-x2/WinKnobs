using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.1 MouseAccelHelper（純粋関数）のテスト。
/// 核心は ToSpiArray の並び＝{ Threshold1, Threshold2, Speed(加速ON/OFF) }
/// （レジストリのキー名の並び Speed→T1→T2 と一致しない・plan-validator 重要3）。
/// </summary>
public class MouseAccelHelperTests
{
    // ---------- IsAccelerationOff ----------

    [Theory]
    [InlineData("0", true)]
    [InlineData("1", false)]
    [InlineData("2", false)]
    [InlineData(null, false)]   // 値なし＝Windows 既定（加速あり）
    [InlineData("", false)]
    [InlineData("abc", false)]
    public void IsAccelerationOff_MouseSpeed単独で判定(string? speed, bool expected)
    {
        Assert.Equal(expected, MouseAccelHelper.IsAccelerationOff(speed));
    }

    // ---------- ToSpiArray（並びの検証が本丸） ----------

    [Fact]
    public void ToSpiArray_既定値は並びがT1_T2_Speedになる()
    {
        var arr = MouseAccelHelper.ToSpiArray("1", "6", "10");
        Assert.Equal(new[] { 6, 10, 1 }, arr);   // {T1, T2, Speed} の順（キー名順ではない）
    }

    [Fact]
    public void ToSpiArray_OFFは全要素ゼロ()
    {
        var arr = MouseAccelHelper.ToSpiArray("0", "0", "0");
        Assert.Equal(new[] { 0, 0, 0 }, arr);
    }

    [Fact]
    public void ToSpiArray_Speedは0以外を1に正規化()
    {
        // SPI_SETMOUSE の第3要素は加速 ON/OFF フラグ（0/1）。レジストリに 2 が入っていても 1 に丸める。
        var arr = MouseAccelHelper.ToSpiArray("2", "6", "10");
        Assert.Equal(1, arr[2]);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("abc", "def", "ghi")]
    [InlineData("", "", "")]
    [InlineData("-1", "-5", "-10")]   // 負数は不正値扱い
    public void ToSpiArray_不正値はWindows既定値で補う(string? speed, string? t1, string? t2)
    {
        var arr = MouseAccelHelper.ToSpiArray(speed, t1, t2);
        Assert.Equal(new[] { 6, 10, 1 }, arr);
    }

    [Fact]
    public void ToSpiArray_長さは常に3()
    {
        Assert.Equal(3, MouseAccelHelper.ToSpiArray(null, null, null).Length);
    }

    // ---------- 定数（レジストリ名のタイポ防止） ----------

    [Fact]
    public void 定数_レジストリパスと値名()
    {
        Assert.Equal(@"HKCU\Control Panel\Mouse", MouseAccelHelper.MouseKeyPath);
        Assert.Equal("MouseSpeed", MouseAccelHelper.SpeedValueName);
        Assert.Equal("MouseThreshold1", MouseAccelHelper.Threshold1ValueName);
        Assert.Equal("MouseThreshold2", MouseAccelHelper.Threshold2ValueName);
        Assert.Equal("1", MouseAccelHelper.DefaultSpeed);
        Assert.Equal("6", MouseAccelHelper.DefaultThreshold1);
        Assert.Equal("10", MouseAccelHelper.DefaultThreshold2);
    }
}
