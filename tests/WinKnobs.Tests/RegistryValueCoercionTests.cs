using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// RegistryValueCoercion（v0.1.12 集約・レジストリ生値→int 共通変換）の純粋関数テスト。
/// CursorSizeHelper / ScrollbarHelper / CaretWidthHelper の各 CoerceInt はここへ委譲する前方互換ラッパ
/// （それぞれの *HelperTests でも同一挙動を別途検証）。
/// </summary>
public class RegistryValueCoercionTests
{
    // ─── REG_DWORD（int）はそのまま通す ───

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(-3, -3)]
    [InlineData(int.MaxValue, int.MaxValue)]
    [InlineData(int.MinValue, int.MinValue)]
    public void CoerceInt_int_passthrough(int boxed, int expected)
        => Assert.Equal(expected, RegistryValueCoercion.CoerceInt(boxed, -1));

    // ─── REG_SZ（文字列）は int.TryParse で数値化 ───

    [Theory]
    [InlineData("0", 0)]
    [InlineData("5", 5)]
    [InlineData("20", 20)]
    [InlineData("-7", -7)]
    public void CoerceInt_string_parses(string s, int expected)
        => Assert.Equal(expected, RegistryValueCoercion.CoerceInt(s, -1));

    // ─── null・解釈不能・未対応型は fallback ───

    [Fact]
    public void CoerceInt_null_returns_fallback()
        => Assert.Equal(99, RegistryValueCoercion.CoerceInt(null, 99));

    [Theory]
    [InlineData("abc")]       // 非数値文字列
    [InlineData("3.5")]       // 小数（int ではない）
    [InlineData("")]          // 空文字
    [InlineData("01 02 03")]  // REG_BINARY を ReadValue<object> が16進文字列化した実機経路（int.TryParse 失敗→fallback）
    public void CoerceInt_unparsable_string_returns_fallback(string s)
        => Assert.Equal(7, RegistryValueCoercion.CoerceInt(s, 7));

    [Fact]
    public void CoerceInt_binary_blob_returns_fallback()
        // REG_BINARY（byte[]）は ToString() が "System.Byte[]" になり parse 失敗 → fallback
        => Assert.Equal(42, RegistryValueCoercion.CoerceInt(new byte[] { 1, 2, 3 }, 42));

    // ─── 既存 3 Helper のラッパが委譲先と一致する（リグレッション保険） ───

    [Theory]
    [InlineData(8)]
    [InlineData(-1)]
    public void Existing_helper_wrappers_match_shared(int boxed)
    {
        var shared = RegistryValueCoercion.CoerceInt(boxed, 0);
        Assert.Equal(shared, CursorSizeHelper.CoerceInt(boxed, 0));
        Assert.Equal(shared, ScrollbarHelper.CoerceInt(boxed, 0));
        Assert.Equal(shared, CaretWidthHelper.CoerceInt(boxed, 0));
    }

    // ─── TryCoerceInt（v0.3.8・Game DVR 退避の「値はあるが数値でない＝管理外」判定） ───

    [Fact]
    public void TryCoerceInt_int_succeeds()
    {
        Assert.True(RegistryValueCoercion.TryCoerceInt(1, out var v));
        Assert.Equal(1, v);
    }

    [Theory]
    [InlineData("0", 0)]
    [InlineData("1", 1)]
    [InlineData("135", 135)]
    public void TryCoerceInt_numeric_string_succeeds(string s, int expected)
    {
        Assert.True(RegistryValueCoercion.TryCoerceInt(s, out var v));
        Assert.Equal(expected, v);
    }

    [Fact]
    public void TryCoerceInt_null_fails_with_zero()
    {
        Assert.False(RegistryValueCoercion.TryCoerceInt(null, out var v));
        Assert.Equal(0, v);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("3.5")]
    [InlineData("")]
    public void TryCoerceInt_non_numeric_string_fails(string s)
        => Assert.False(RegistryValueCoercion.TryCoerceInt(s, out _));

    [Fact]
    public void TryCoerceInt_binary_blob_fails()
        => Assert.False(RegistryValueCoercion.TryCoerceInt(new byte[] { 0, 1, 2, 3 }, out _));

    [Fact]
    public void TryCoerceInt_extreme_ints_succeed()
    {
        // int.MinValue/MaxValue も「数値」として true（旧 two-fallback 実装の取りこぼし回帰防止）。
        Assert.True(RegistryValueCoercion.TryCoerceInt(int.MinValue, out var lo));
        Assert.Equal(int.MinValue, lo);
        Assert.True(RegistryValueCoercion.TryCoerceInt(int.MaxValue, out var hi));
        Assert.Equal(int.MaxValue, hi);
    }

    [Fact]
    public void CoerceInt_delegates_to_TryCoerceInt()
    {
        // CoerceInt は TryCoerceInt 委譲（成功時は値・失敗時は fallback）。
        Assert.Equal(5, RegistryValueCoercion.CoerceInt("5", -1));
        Assert.Equal(-1, RegistryValueCoercion.CoerceInt("x", -1));
    }
}
