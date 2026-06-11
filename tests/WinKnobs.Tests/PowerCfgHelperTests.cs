using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.1 PowerCfgHelper（純粋関数）のテスト。
/// powercfg 出力のロケール非依存パース（日本語/英語の実出力サンプルでラウンドトリップ）。
/// </summary>
public class PowerCfgHelperTests
{
    // ---------- TryParseActiveSchemeGuid ----------

    [Fact]
    public void ActiveScheme_日本語出力からGUIDを取れる()
    {
        var output = "電源設定の GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (バランス)";
        Assert.True(PowerCfgHelper.TryParseActiveSchemeGuid(output, out var guid));
        Assert.Equal("381b4222-f694-41f0-9685-ff5bb260df2e", guid);
    }

    [Fact]
    public void ActiveScheme_英語出力からGUIDを取れる()
    {
        var output = "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)";
        Assert.True(PowerCfgHelper.TryParseActiveSchemeGuid(output, out var guid));
        Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", guid);
    }

    [Fact]
    public void ActiveScheme_大文字GUIDは小文字に正規化()
    {
        var output = "Power Scheme GUID: 8C5E7FDA-E8BF-4A96-9A85-A6E23A8C635C  (High performance)";
        Assert.True(PowerCfgHelper.TryParseActiveSchemeGuid(output, out var guid));
        Assert.Equal("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c", guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("GUID が含まれない出力")]
    public void ActiveScheme_GUIDなしはfalse(string? output)
    {
        Assert.False(PowerCfgHelper.TryParseActiveSchemeGuid(output, out _));
    }

    // ---------- TryParseAcDcIndexes ----------

    [Fact]
    public void AcDc_日本語の実出力形式からACとDCを取れる()
    {
        // powercfg /query <scheme> SUB_PROCESSOR PERFBOOSTMODE の出力骨格（日本語）
        var output = @"
電源設定の GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (バランス)
  サブグループの GUID: 54533251-82be-4824-96c1-47b60b740d00  (プロセッサの電源管理)
    電源設定の GUID: be337238-0d82-4146-a960-4f3749d470c7  (プロセッサ パフォーマンスの向上モード)
      利用可能な設定のインデックス: 000
      利用可能な設定のフレンドリ名: 無効
      利用可能な設定のインデックス: 001
      利用可能な設定のフレンドリ名: 有効
      現在の AC 電源設定のインデックス: 0x00000002
      現在の DC 電源設定のインデックス: 0x00000001";
        Assert.True(PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out var dc));
        Assert.Equal(2, ac);
        Assert.Equal(1, dc);
    }

    [Fact]
    public void AcDc_英語の実出力形式からACとDCを取れる()
    {
        var output = @"
Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced)
  Subgroup GUID: 54533251-82be-4824-96c1-47b60b740d00  (Processor power management)
    Power Setting GUID: be337238-0d82-4146-a960-4f3749d470c7  (Processor performance boost mode)
      Current AC Power Setting Index: 0x00000000
      Current DC Power Setting Index: 0x00000002";
        Assert.True(PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out var dc));
        Assert.Equal(0, ac);
        Assert.Equal(2, dc);
    }

    [Fact]
    public void AcDc_GUID内の16進はマッチしない()
    {
        // GUID は 0x プレフィックスが付かないため誤マッチしないことの確認
        var output = @"
Power Setting GUID: be337238-0d82-4146-a960-4f3749d470c7
      Current AC Power Setting Index: 0x00000001
      Current DC Power Setting Index: 0x00000001";
        Assert.True(PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out var dc));
        Assert.Equal(1, ac);
        Assert.Equal(1, dc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0x マッチが無い出力")]
    [InlineData("片方だけ: 0x00000002")]   // 2件未満は失敗（AC/DC が揃わない）
    public void AcDc_不正出力はfalse(string? output)
    {
        Assert.False(PowerCfgHelper.TryParseAcDcIndexes(output, out _, out _));
    }

    // ---------- 定数 ----------

    [Fact]
    public void 定数_GUID()
    {
        Assert.Equal("54533251-82be-4824-96c1-47b60b740d00", PowerCfgHelper.SubProcessorGuid);
        Assert.Equal("be337238-0d82-4146-a960-4f3749d470c7", PowerCfgHelper.PerfBoostModeGuid);
    }
}
