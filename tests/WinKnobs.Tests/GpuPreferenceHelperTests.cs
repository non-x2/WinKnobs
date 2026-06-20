using System.Collections.Generic;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.3.0 GpuPreferenceHelper（純粋関数）のテスト。
/// UserGpuPreferences の "key=value;" トークン列のパース・再構築・未知トークン保持・値名判定。
/// </summary>
public class GpuPreferenceHelperTests
{
    // ---------- ParseTokens ----------

    [Fact]
    public void ParseTokens_基本()
    {
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=2;AutoHDREnable=1;");
        Assert.Equal(2, t.Count);
        Assert.Equal("GpuPreference", t[0].Key);
        Assert.Equal("2", t[0].Value);
        Assert.Equal("AutoHDREnable", t[1].Key);
        Assert.Equal("1", t[1].Value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ParseTokens_空は空リスト(string? raw)
    {
        Assert.Empty(GpuPreferenceHelper.ParseTokens(raw));
    }

    [Fact]
    public void ParseTokens_末尾セミコロン無しでも読める()
    {
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=1");
        Assert.Single(t);
        Assert.Equal("1", t[0].Value);
    }

    [Fact]
    public void ParseTokens_壊れたトークンはスキップ()
    {
        // "=2"（key 空）と "garbage"（= 無し）はスキップ。正常な 2 件だけ残る。
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=2;=2;garbage;AutoHDREnable=0;");
        Assert.Equal(2, t.Count);
        Assert.Equal("GpuPreference", t[0].Key);
        Assert.Equal("AutoHDREnable", t[1].Key);
    }

    [Fact]
    public void ParseTokens_値に等号が含まれても最初の等号で分割()
    {
        var t = GpuPreferenceHelper.ParseTokens("SpecificAdapter=1002&6660&380F17AA;");
        Assert.Single(t);
        Assert.Equal("SpecificAdapter", t[0].Key);
        Assert.Equal("1002&6660&380F17AA", t[0].Value);
    }

    // ---------- BuildValue / ラウンドトリップ ----------

    [Fact]
    public void BuildValue_末尾セミコロン付きで再構築()
    {
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=2;AutoHDREnable=1;");
        Assert.Equal("GpuPreference=2;AutoHDREnable=1;", GpuPreferenceHelper.BuildValue(t));
    }

    [Fact]
    public void BuildValue_空リストは空文字()
    {
        Assert.Equal("", GpuPreferenceHelper.BuildValue(new List<KeyValuePair<string, string>>()));
    }

    [Fact]
    public void 未知トークンは保持される()
    {
        const string raw = "GpuPreference=1;SpecificAdapter=1002&6660&380F17AA;AutoHDREnable=0;";
        var t = GpuPreferenceHelper.ParseTokens(raw);
        // GpuPreference だけ 2 に変えても SpecificAdapter は順序ごと残る
        var changed = GpuPreferenceHelper.SetToken(t, GpuPreferenceHelper.TokenGpuPreference, "2");
        Assert.Equal("GpuPreference=2;SpecificAdapter=1002&6660&380F17AA;AutoHDREnable=0;",
            GpuPreferenceHelper.BuildValue(changed));
    }

    // ---------- SetToken ----------

    [Fact]
    public void SetToken_既存は最初の1件だけ差替え綴り維持()
    {
        var t = GpuPreferenceHelper.ParseTokens("gpupreference=0;AutoHDREnable=1;");
        var r = GpuPreferenceHelper.SetToken(t, "GpuPreference", "2");
        // 元の綴り "gpupreference" を維持しつつ値だけ 2 に
        Assert.Equal("gpupreference=2;AutoHDREnable=1;", GpuPreferenceHelper.BuildValue(r));
    }

    [Fact]
    public void SetToken_無ければ末尾に追加()
    {
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=2;");
        var r = GpuPreferenceHelper.SetToken(t, GpuPreferenceHelper.TokenAutoHdr, "1");
        Assert.Equal("GpuPreference=2;AutoHDREnable=1;", GpuPreferenceHelper.BuildValue(r));
    }

    [Fact]
    public void RemoveToken_キー大小無視で除去()
    {
        var t = GpuPreferenceHelper.ParseTokens("GpuPreference=2;autohdrenable=1;");
        var r = GpuPreferenceHelper.RemoveToken(t, "AutoHDREnable");
        Assert.Equal("GpuPreference=2;", GpuPreferenceHelper.BuildValue(r));
    }

    // ---------- Get 系 ----------

    [Theory]
    [InlineData("GpuPreference=0;", 0)]
    [InlineData("GpuPreference=1;", 1)]
    [InlineData("GpuPreference=2;", 2)]
    public void GetGpuPreference_有効値(string raw, int expected)
    {
        Assert.Equal(expected, GpuPreferenceHelper.GetGpuPreference(GpuPreferenceHelper.ParseTokens(raw)));
    }

    [Theory]
    [InlineData("GpuPreference=3;")]   // 範囲外
    [InlineData("GpuPreference=abc;")] // 非数値
    [InlineData("AutoHDREnable=1;")]   // 未設定
    [InlineData("")]
    public void GetGpuPreference_無効値は0自動扱い(string raw)
    {
        Assert.Equal(0, GpuPreferenceHelper.GetGpuPreference(GpuPreferenceHelper.ParseTokens(raw)));
    }

    [Fact]
    public void GetAutoHdr_1ならtrue()
    {
        Assert.True(GpuPreferenceHelper.GetAutoHdr(GpuPreferenceHelper.ParseTokens("AutoHDREnable=1;")));
        Assert.False(GpuPreferenceHelper.GetAutoHdr(GpuPreferenceHelper.ParseTokens("AutoHDREnable=0;")));
        Assert.False(GpuPreferenceHelper.GetAutoHdr(GpuPreferenceHelper.ParseTokens("GpuPreference=2;")));
    }

    [Fact]
    public void GetSwapEffect_1ならtrue()
    {
        Assert.True(GpuPreferenceHelper.GetSwapEffect(GpuPreferenceHelper.ParseTokens("SwapEffectUpgradeEnable=1;")));
        Assert.False(GpuPreferenceHelper.GetSwapEffect(GpuPreferenceHelper.ParseTokens("SwapEffectUpgradeEnable=0;")));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(-1, false)]
    [InlineData(3, false)]
    public void IsValidGpuPreference(int v, bool expected)
    {
        Assert.Equal(expected, GpuPreferenceHelper.IsValidGpuPreference(v));
    }

    // ---------- IsAppValueName（E1・重要4） ----------

    [Fact]
    public void IsAppValueName_通常のexeパスは可()
    {
        Assert.True(GpuPreferenceHelper.IsAppValueName(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
    }

    [Theory]
    [InlineData("DirectXUserGlobalSettings")]   // 全体予約名
    [InlineData("directxuserglobalsettings")]   // 大小無視で除外
    public void IsAppValueName_全体予約名は除外(string name)
    {
        Assert.False(GpuPreferenceHelper.IsAppValueName(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void IsAppValueName_空は除外(string? name)
    {
        Assert.False(GpuPreferenceHelper.IsAppValueName(name));
    }

    [Fact]
    public void IsAppValueName_制御文字を含む値名は除外()
    {
        // タブ・NUL は制御文字。これらを含む値名は除外する（E1）。スペースは制御文字でないので対象外。
        Assert.False(GpuPreferenceHelper.IsAppValueName("C:/app\t.exe"));
        Assert.False(GpuPreferenceHelper.IsAppValueName("C:/app\0.exe"));
    }

    [Fact]
    public void IsAppValueName_250文字超は除外()
    {
        Assert.False(GpuPreferenceHelper.IsAppValueName(new string('a', 251)));
    }

    [Theory]
    [InlineData("GraphicsFeaturesNotificationConfig")]   // 通知設定の予約名（REG_DWORD・v0.3.0 実機検証で混入発覚）
    [InlineData("graphicsfeaturesnotificationconfig")]   // 大小無視で除外
    public void IsAppValueName_通知設定予約名は除外(string name)
    {
        Assert.False(GpuPreferenceHelper.IsAppValueName(name));
    }

    [Theory]
    [InlineData("Claude_pzs8sxrjxfjjc!Claude")]              // UWP/ストアアプリ形式（PFN!AppId・パスでない）
    [InlineData("Microsoft.YourPhone_8wekyb3d8bbwe!App")]
    public void IsAppValueName_UWPストア形式は許可(string name)
    {
        // 値名がフルパスでない UWP 形式でも、予約名・空・制御文字でなければアプリ扱い（REG_SZ なので型フィルタでも残る）。
        Assert.True(GpuPreferenceHelper.IsAppValueName(name));
    }

    [Fact]
    public void ReservedValueNames_全体と通知設定の2種を含む()
    {
        Assert.Contains(GpuPreferenceHelper.GlobalValueName, GpuPreferenceHelper.ReservedValueNames);
        Assert.Contains(GpuPreferenceHelper.NotificationConfigValueName, GpuPreferenceHelper.ReservedValueNames);
    }

    // ---------- DisplayNameOf ----------

    [Fact]
    public void DisplayNameOf_ファイル名を取り出す()
    {
        Assert.Equal("chrome.exe",
            GpuPreferenceHelper.DisplayNameOf(@"C:\Program Files\Google\Chrome\Application\chrome.exe"));
    }

    // ---------- PathComparer（重要4） ----------

    [Fact]
    public void PathComparer_大小無視で同一視()
    {
        Assert.True(GpuPreferenceHelper.PathComparer.Equals(@"C:\App\Chrome.exe", @"c:\app\chrome.exe"));
    }
}
