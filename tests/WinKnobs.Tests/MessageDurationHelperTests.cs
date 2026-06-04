using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// MessageDurationHelper（v0.1.13 アクセシビリティ「通知を表示する時間」）の純粋関数テスト。
/// レジストリ I/O は VM 実機検証の領域でここでは扱わない。
/// ラベル⇔値は IndexOf 明示マッピング（"1分"=60秒）。CoerceInt は委譲先（RegistryValueCoercionTests）でも検証。
/// </summary>
public class MessageDurationHelperTests
{
    // ─── プリセットとラベルの対応 ───

    [Fact]
    public void Presets_and_choices_are_same_length()
    {
        Assert.Equal(MessageDurationHelper.PresetSeconds.Count, MessageDurationHelper.Choices.Count);
        Assert.Equal(6, MessageDurationHelper.Choices.Count);
    }

    // plan-validator 反映：端値一致はラベル非依存（PresetSeconds で直接照合）。
    // TryParseLabel を使わない設計（"1分"問題）なので CaretWidth の端値テスト型は流用しない。
    [Fact]
    public void Presets_endpoints_match_min_and_max()
    {
        Assert.Equal(MessageDurationHelper.MinSeconds, MessageDurationHelper.PresetSeconds[0]);
        Assert.Equal(MessageDurationHelper.MaxSeconds, MessageDurationHelper.PresetSeconds[MessageDurationHelper.PresetSeconds.Count - 1]);
    }

    // ─── ClampToRange：[5, 300] に丸める ───

    [Theory]
    [InlineData(4, 5)]
    [InlineData(5, 5)]
    [InlineData(60, 60)]
    [InlineData(300, 300)]
    [InlineData(301, 300)]
    [InlineData(0, 5)]
    [InlineData(-50, 5)]
    [InlineData(int.MaxValue, 300)]
    [InlineData(int.MinValue, 5)]
    public void ClampToRange_clamps_to_5_300(int input, int expected)
        => Assert.Equal(expected, MessageDurationHelper.ClampToRange(input));

    // ─── SecondsForLabel：ラベル→秒数（明示マッピング・"1分"=60／"5分"=300） ───

    [Theory]
    [InlineData("5秒（既定）", 5)]
    [InlineData("7秒", 7)]
    [InlineData("15秒", 15)]
    [InlineData("30秒", 30)]
    [InlineData("1分", 60)]    // ← 先頭数字方式なら誤って1になる。明示マッピングで60
    [InlineData("5分", 300)]   // ← 先頭数字方式なら誤って5になる。明示マッピングで300
    public void SecondsForLabel_maps_label_to_seconds(string label, int expected)
        => Assert.Equal(expected, MessageDurationHelper.SecondsForLabel(label));

    [Theory]
    [InlineData("99分")]       // Choices に無い
    [InlineData("")]
    [InlineData("でたらめ")]
    public void SecondsForLabel_unknown_returns_default(string label)
        => Assert.Equal(MessageDurationHelper.PresetSeconds[0], MessageDurationHelper.SecondsForLabel(label));

    [Fact]
    public void SecondsForLabel_null_returns_default()
        => Assert.Equal(MessageDurationHelper.PresetSeconds[0], MessageDurationHelper.SecondsForLabel(null));

    // ─── NearestPresetLabel：最も近いプリセットのラベル ───
    // プリセット秒 {5,7,15,30,60,300}。同距離は小さい方（先に出現）を採用。

    [Theory]
    [InlineData(5, "5秒（既定）")]
    [InlineData(7, "7秒")]
    [InlineData(6, "5秒（既定）")]   // |6-5|=1 < |6-7|=1 → 同距離は先頭(5)
    [InlineData(11, "7秒")]          // |11-7|=4 = |11-15|=4 → 同距離は小さい方(7)
    [InlineData(20, "15秒")]         // |20-15|=5 < |20-30|=10
    [InlineData(45, "30秒")]         // |45-30|=15 = |45-60|=15 → 同距離は小さい方(30)
    [InlineData(120, "1分")]         // |120-60|=60 < |120-300|=180
    [InlineData(180, "1分")]         // |180-60|=120 = |180-300|=120 → 同距離は小さい方(60=1分)
    [InlineData(300, "5分")]
    [InlineData(1000, "5分")]        // 範囲外（上）→最大プリセット
    [InlineData(0, "5秒（既定）")]
    [InlineData(-5, "5秒（既定）")]
    public void NearestPresetLabel_maps_to_closest(int seconds, string expected)
        => Assert.Equal(expected, MessageDurationHelper.NearestPresetLabel(seconds));

    // ─── plan-validator 致命対応：戻り値は必ず Choices に含まれる（ComboBox 整合 invariant） ───

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(45)]
    [InlineData(180)]
    [InlineData(300)]
    [InlineData(99999)]
    [InlineData(int.MaxValue)]
    public void NearestPresetLabel_always_returns_a_valid_choice(int seconds)
        => Assert.Contains(MessageDurationHelper.NearestPresetLabel(seconds), MessageDurationHelper.Choices);

    // ─── ラウンドトリップ（両方向）：ラベル→秒→ラベル / 各プリセット秒→ラベル→秒 ───

    [Fact]
    public void EachChoice_roundtrips_label_to_seconds()
    {
        for (int idx = 0; idx < MessageDurationHelper.Choices.Count; idx++)
        {
            var label = MessageDurationHelper.Choices[idx];
            Assert.Equal(MessageDurationHelper.PresetSeconds[idx], MessageDurationHelper.SecondsForLabel(label));
        }
    }

    [Fact]
    public void EachPresetSecond_roundtrips_to_its_own_label()
    {
        for (int idx = 0; idx < MessageDurationHelper.PresetSeconds.Count; idx++)
        {
            // 各プリセット秒は自分自身のラベルに最近接（距離0）
            Assert.Same(MessageDurationHelper.Choices[idx],
                MessageDurationHelper.NearestPresetLabel(MessageDurationHelper.PresetSeconds[idx]));
        }
    }

    // ─── CoerceInt は委譲先と同一挙動（前方互換ラッパの担保） ───

    [Fact]
    public void CoerceInt_delegates_to_shared_coercion()
    {
        Assert.Equal(30, MessageDurationHelper.CoerceInt(30, -1));       // int passthrough
        Assert.Equal(15, MessageDurationHelper.CoerceInt("15", -1));     // REG_SZ
        Assert.Equal(5, MessageDurationHelper.CoerceInt(null, 5));       // 未設定→fallback
        Assert.Equal(MessageDurationHelper.MinSeconds,
            MessageDurationHelper.CoerceInt(new byte[] { 1, 2 }, MessageDurationHelper.MinSeconds)); // 異常型→fallback
    }
}
