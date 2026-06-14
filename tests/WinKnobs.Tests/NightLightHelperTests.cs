using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.2 Step3 NightLightHelper（純粋関数）のテスト。
/// 実機（Z370・2026-06-11）から採取した本物の 55 バイト blob でラウンドトリップを検証する。
/// 非公開フォーマットゆえ「実 blob で通ること」が最重要（設計ドラフト §3）。
/// </summary>
public class NightLightHelperTests
{
    // 実機採取サンプル（夜間モード ON・色温度 3850K 相当）。CF 28 94 3C が色温度。
    private static readonly byte[] RealBlob3850K =
    {
        0x43, 0x42, 0x01, 0x00, 0x0A, 0x02, 0x01, 0x00,
        0x2A, 0x06, 0xC0, 0xA3, 0xA5, 0xD1, 0x06, 0x2A,
        0x2B, 0x0E, 0x21, 0x43, 0x42, 0x01, 0x00, 0x02,
        0x01, 0xCA, 0x14, 0x0E, 0x15, 0x00, 0xCA, 0x1E,
        0x0E, 0x07, 0x00, 0xCF, 0x28, 0x94, 0x3C, 0xCA,
        0x32, 0x0E, 0x12, 0x2E, 0x36, 0x00, 0xCA, 0x3C,
        0x0E, 0x05, 0x00, 0x00, 0x00, 0x00, 0x00,
    };

    // ---------- エンコード式 ----------

    [Theory]
    [InlineData(3850, 0x94, 0x3C)]   // 実 blob の値
    [InlineData(6500, 0xC8, 0x65)]   // 6500/64=101=0x65, 6500%64=36 → 36*2+128=200=0xC8
    [InlineData(1216, 0x80, 0x13)]   // 1216/64=19=0x13, 1216%64=0 → 128=0x80
    public void KelvinToTempBytes_式どおり(int kelvin, int expLow, int expHigh)
    {
        var (low, high) = NightLightHelper.KelvinToTempBytes(kelvin);
        Assert.Equal((byte)expLow, low);
        Assert.Equal((byte)expHigh, high);
    }

    [Fact]
    public void TempBytes往復_実blobの値を復号できる()
    {
        Assert.True(NightLightHelper.TryTempBytesToKelvin(0x94, 0x3C, out var k));
        Assert.Equal(3850, k);
    }

    [Fact]
    public void TempBytesToKelvin_低位128未満は不正()
    {
        Assert.False(NightLightHelper.TryTempBytesToKelvin(0x7F, 0x3C, out _));
    }

    // ---------- TryParseSettings ----------

    [Fact]
    public void Parse_実blobから3850Kを取れる()
    {
        Assert.True(NightLightHelper.TryParseSettings(RealBlob3850K, out var k));
        Assert.Equal(3850, k);
    }

    [Fact]
    public void Parse_null空短すぎは失敗()
    {
        Assert.False(NightLightHelper.TryParseSettings(null, out _));
        Assert.False(NightLightHelper.TryParseSettings(System.Array.Empty<byte>(), out _));
        Assert.False(NightLightHelper.TryParseSettings(new byte[] { 0x43, 0x42 }, out _));
    }

    [Fact]
    public void Parse_マジック不一致は失敗()
    {
        var bad = (byte[])RealBlob3850K.Clone();
        bad[0] = 0x00;
        Assert.False(NightLightHelper.TryParseSettings(bad, out _));
    }

    [Fact]
    public void Parse_マーカー無しは失敗()
    {
        var bad = (byte[])RealBlob3850K.Clone();
        bad[35] = 0x00;   // CF を潰す
        Assert.False(NightLightHelper.TryParseSettings(bad, out _));
    }

    [Fact]
    public void Parse_マーカー複数は曖昧で失敗()
    {
        // 末尾近くにもう一つ CF 28 を仕込む
        var bad = (byte[])RealBlob3850K.Clone();
        bad[51] = 0xCF;
        bad[52] = 0x28;
        Assert.False(NightLightHelper.TryParseSettings(bad, out _));
    }

    // ---------- BuildPatchedSettings（核心ラウンドトリップ） ----------

    [Theory]
    [InlineData(6500)]
    [InlineData(5000)]
    [InlineData(4000)]
    [InlineData(2700)]
    [InlineData(1200)]
    public void Patch_各プリセットへ書換て再パースで一致(int kelvin)
    {
        var patched = NightLightHelper.BuildPatchedSettings(RealBlob3850K, kelvin, bumpTimestamp: false);
        Assert.NotNull(patched);
        Assert.True(NightLightHelper.TryParseSettings(patched!, out var k));
        Assert.Equal(
            NightLightHelper.NearestPresetLabel(kelvin),
            NightLightHelper.NearestPresetLabel(k));
    }

    [Fact]
    public void Patch_色温度2バイト以外は原本維持_bumpなし()
    {
        var patched = NightLightHelper.BuildPatchedSettings(RealBlob3850K, 2700, bumpTimestamp: false)!;
        Assert.Equal(RealBlob3850K.Length, patched.Length);
        for (int i = 0; i < patched.Length; i++)
        {
            // 色温度の2バイト（index 37,38）だけ変わり得る
            if (i == 37 || i == 38) continue;
            Assert.Equal(RealBlob3850K[i], patched[i]);
        }
    }

    [Fact]
    public void Patch_範囲外はクランプされる()
    {
        var hi = NightLightHelper.BuildPatchedSettings(RealBlob3850K, 99999, bumpTimestamp: false)!;
        Assert.True(NightLightHelper.TryParseSettings(hi, out var khi));
        Assert.Equal(NightLightHelper.NearestPresetLabel(NightLightHelper.MaxKelvin),
                     NightLightHelper.NearestPresetLabel(khi));

        var lo = NightLightHelper.BuildPatchedSettings(RealBlob3850K, 1, bumpTimestamp: false)!;
        Assert.True(NightLightHelper.TryParseSettings(lo, out var klo));
        Assert.Equal(NightLightHelper.NearestPresetLabel(NightLightHelper.MinKelvin),
                     NightLightHelper.NearestPresetLabel(klo));
    }

    [Fact]
    public void Patch_想定外blobには触らずnull()
    {
        var bad = (byte[])RealBlob3850K.Clone();
        bad[0] = 0x00;   // マジック破壊
        Assert.Null(NightLightHelper.BuildPatchedSettings(bad, 2700, bumpTimestamp: false));
    }

    // ---------- タイムスタンプ bump ----------

    [Fact]
    public void Patch_bumpありでタイムスタンプが1増え長さ不変()
    {
        var patched = NightLightHelper.BuildPatchedSettings(RealBlob3850K, 4000, bumpTimestamp: true)!;
        Assert.Equal(RealBlob3850K.Length, patched.Length);   // +1 秒は5バイトのまま

        // タイムスタンプ varint（index 10..14）は元 C0 A3 A5 D1 06 = 1781092800 → 1781092801
        // 1781092801 = ...0x01 増分なので末尾以外は同値、先頭バイトが C0→C1
        Assert.Equal(0xC1, patched[10]);
        Assert.Equal(RealBlob3850K[11], patched[11]);
        Assert.Equal(RealBlob3850K[12], patched[12]);
        Assert.Equal(RealBlob3850K[13], patched[13]);
        Assert.Equal(RealBlob3850K[14], patched[14]);
    }

    [Fact]
    public void Patch_bumpなしならタイムスタンプ不変()
    {
        var patched = NightLightHelper.BuildPatchedSettings(RealBlob3850K, 4000, bumpTimestamp: false)!;
        for (int i = 10; i <= 14; i++)
            Assert.Equal(RealBlob3850K[i], patched[i]);
    }

    // ---------- プリセット補助 ----------

    [Fact]
    public void NearestPreset_近い段階に丸める()
    {
        Assert.Equal("4000K（標準）", NightLightHelper.NearestPresetLabel(3850));
        Assert.Equal("6500K（ごく弱い）", NightLightHelper.NearestPresetLabel(6400));
        Assert.Equal("1200K（最も暖色）", NightLightHelper.NearestPresetLabel(1300));
    }

    [Fact]
    public void TryKelvinFromLabel_往復()
    {
        Assert.True(NightLightHelper.TryKelvinFromLabel("2700K（強い）", out var k));
        Assert.Equal(2700, k);
        Assert.False(NightLightHelper.TryKelvinFromLabel("存在しない", out _));
    }
}
