using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// TelemetryMinimizeHelper（v0.4.9a プライバシー「診断データ送信を最小化」/ AllowTelemetry）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class TelemetryMinimizeHelperTests
{
    [Fact]
    public void Choices_has_two_entries()
        => Assert.Equal(2, TelemetryMinimizeHelper.Choices.Count);

    [Fact]
    public void Labels_are_same_reference_as_choices()
    {
        Assert.Same(TelemetryMinimizeHelper.Choices[0], TelemetryMinimizeHelper.MinimizedLabel);
        Assert.Same(TelemetryMinimizeHelper.Choices[1], TelemetryMinimizeHelper.DefaultLabel);
    }

    // ─── IsMinimized：厳密に 1 の時のみ true（境界値対応・0/2/3 を誤認しない） ───

    [Theory]
    [InlineData(0, false)]   // Security（Enterprise/Education限定）
    [InlineData(1, true)]    // Basic（最小化）
    [InlineData(2, false)]   // Enhanced（旧）
    [InlineData(3, false)]   // Full（旧）
    [InlineData(-1, false)]
    [InlineData(int.MaxValue, false)]
    public void IsMinimized_true_only_for_1(int value, bool expected)
        => Assert.Equal(expected, TelemetryMinimizeHelper.IsMinimized(value));

    [Fact]
    public void IsMinimized_null_is_false()   // 未設定＝既定に戻す
        => Assert.False(TelemetryMinimizeHelper.IsMinimized(null));

    // ─── LabelForValue：値→ラベル（必ず Choices 内・同一参照・境界値） ───

    [Fact]
    public void LabelForValue_1_returns_minimized_same_ref()
        => Assert.Same(TelemetryMinimizeHelper.Choices[0], TelemetryMinimizeHelper.LabelForValue(1));

    [Theory]
    [InlineData(0)]
    [InlineData(2)]     // Enhanced を「最小化済み」と誤認しない
    [InlineData(3)]     // Full を「最小化済み」と誤認しない
    [InlineData(-5)]
    public void LabelForValue_non1_returns_default_same_ref(int value)
        => Assert.Same(TelemetryMinimizeHelper.Choices[1], TelemetryMinimizeHelper.LabelForValue(value));

    [Fact]
    public void LabelForValue_null_returns_default()   // 未設定＝既定に戻す
        => Assert.Same(TelemetryMinimizeHelper.Choices[1], TelemetryMinimizeHelper.LabelForValue(null));

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    public void LabelForValue_always_in_choices(int? value)
        => Assert.Contains(TelemetryMinimizeHelper.LabelForValue(value), TelemetryMinimizeHelper.Choices);

    // ─── CoerceInt は委譲先と同一挙動 ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(1, TelemetryMinimizeHelper.CoerceInt(1, -1));
        Assert.Equal(1, TelemetryMinimizeHelper.CoerceInt("1", -1));
        Assert.Equal(0, TelemetryMinimizeHelper.CoerceInt(null, 0));
        Assert.Equal(0, TelemetryMinimizeHelper.CoerceInt(new byte[] { 9 }, 0));
    }
}
