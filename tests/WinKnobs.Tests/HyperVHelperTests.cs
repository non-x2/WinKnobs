using System;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.2.1 Step5 HyperVHelper（純粋関数）のテスト。
/// コマンド組み立て（PowerShell 経由・raw トークン検証＝注入防止）・
/// bcdedit /enum 出力の行アンカーパース（ロケール非依存）・Classify・
/// 退避 JSON の厳密パース（破損は null）・メニュー状態ラベル。
/// </summary>
public class HyperVHelperTests
{
    // ---------- BuildCommand ----------

    [Fact]
    public void BuildReadCommand_currentをクォートしLASTEXITCODEを返す()
    {
        var cmd = HyperVHelper.BuildReadCommand();
        Assert.Contains("bcdedit /enum '{current}'", cmd);
        Assert.Contains("exit $LASTEXITCODE", cmd);
    }

    [Theory]
    [InlineData(true, "Auto")]
    [InlineData(false, "Off")]
    public void BuildSetLaunchTypeCommand_AutoOffを組み立てる(bool autoOn, string expected)
    {
        var cmd = HyperVHelper.BuildSetLaunchTypeCommand(autoOn);
        Assert.Contains($"bcdedit /set hypervisorlaunchtype {expected}", cmd);
        Assert.Contains("exit $LASTEXITCODE", cmd);
    }

    [Theory]
    [InlineData("Auto")]
    [InlineData("auto")]
    [InlineData("Off")]
    [InlineData("OFF")]
    public void BuildSetLaunchTypeRawCommand_ホワイトリストトークンは通る(string token)
    {
        var cmd = HyperVHelper.BuildSetLaunchTypeRawCommand(token);
        Assert.Contains($"hypervisorlaunchtype {token}", cmd);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("off; calc.exe")]       // コマンド注入
    [InlineData("Auto Limited")]        // 空白入り
    [InlineData("off`n")]
    [InlineData("123")]
    [InlineData("AutoLimited")]         // C-1：bcdedit が受理しない値＝復元永久失敗の元（ホワイトリスト外）
    public void BuildSetLaunchTypeRawCommand_不正トークンは例外(string token)
    {
        Assert.Throws<ArgumentException>(() => HyperVHelper.BuildSetLaunchTypeRawCommand(token));
    }

    [Fact]
    public void BuildDeleteLaunchTypeCommand_deletevalueを組み立てる()
    {
        var cmd = HyperVHelper.BuildDeleteLaunchTypeCommand();
        Assert.Contains("bcdedit /deletevalue hypervisorlaunchtype", cmd);
        Assert.Contains("exit $LASTEXITCODE", cmd);
    }

    // ---------- TryParseLaunchType ----------

    // bcdedit /enum '{current}' の代表出力（日本語環境でも要素名・値は英語固定）
    private const string EnumOutputAuto = @"
Windows ブート ローダー
-------------------------------
identifier              {current}
device                  partition=C:
path                    \WINDOWS\system32\winload.efi
description             Windows 11
hypervisorlaunchtype    Auto
";

    [Fact]
    public void TryParseLaunchType_Auto行を読む()
    {
        var (type, raw) = HyperVHelper.TryParseLaunchType(EnumOutputAuto);
        Assert.Equal(HyperVLaunchType.Auto, type);
        Assert.Equal("Auto", raw);
    }

    [Fact]
    public void TryParseLaunchType_Offは大文字小文字を問わない()
    {
        var (type, raw) = HyperVHelper.TryParseLaunchType("hypervisorlaunchtype    off\r\n");
        Assert.Equal(HyperVLaunchType.Off, type);
        Assert.Equal("off", raw);
    }

    [Fact]
    public void TryParseLaunchType_行が無ければNotConfigured()
    {
        var output = EnumOutputAuto.Replace("hypervisorlaunchtype    Auto\r\n", "")
                                   .Replace("hypervisorlaunchtype    Auto\n", "");
        var (type, raw) = HyperVHelper.TryParseLaunchType(output);
        Assert.Equal(HyperVLaunchType.NotConfigured, type);
        Assert.Null(raw);
    }

    [Fact]
    public void TryParseLaunchType_AutoOff以外はOtherでrawを保持()
    {
        var (type, raw) = HyperVHelper.TryParseLaunchType("hypervisorlaunchtype    AutoLimited");
        Assert.Equal(HyperVLaunchType.Other, type);
        Assert.Equal("AutoLimited", raw);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParseLaunchType_空はUnknown(string? output)
    {
        var (type, _) = HyperVHelper.TryParseLaunchType(output);
        Assert.Equal(HyperVLaunchType.Unknown, type);
    }

    [Fact]
    public void TryParseLaunchType_説明文中の単語には誤ヒットしない()
    {
        // 行アンカー（行頭＋要素名＋値のみ）を外れた文章は無視される
        var (type, _) = HyperVHelper.TryParseLaunchType(
            "description   この設定は hypervisorlaunchtype Auto を含む説明文です 余分");
        Assert.Equal(HyperVLaunchType.NotConfigured, type);
    }

    // ---------- Classify ----------

    [Fact]
    public void Classify_ExitCode0はSuccess()
        => Assert.Equal(HyperVOutcome.Success, HyperVHelper.Classify(0, "操作は正常に終了しました。"));

    [Theory]
    [InlineData("アクセスが拒否されました。")]
    [InlineData("Access is denied.")]
    [InlineData("この操作には管理者特権が必要です。")]
    public void Classify_権限不足はAccessDenied(string output)
        => Assert.Equal(HyperVOutcome.AccessDenied, HyperVHelper.Classify(1, output));

    [Fact]
    public void Classify_その他の非0はFailed()
        => Assert.Equal(HyperVOutcome.Failed, HyperVHelper.Classify(1, "要素データの設定中にエラーが発生しました。"));

    // ---------- DescribeMenuStatusLabel ----------

    [Theory]
    [InlineData(HyperVLaunchType.Auto, "Auto", "Auto（Hyper-V 有効）")]
    [InlineData(HyperVLaunchType.Off, "Off", "Off（無効・LDPlayer 等が使用可）")]
    [InlineData(HyperVLaunchType.NotConfigured, null, "未設定（OS 既定）")]
    [InlineData(HyperVLaunchType.Other, "AutoLimited", "AutoLimited（手動設定値）")]
    public void DescribeMenuStatusLabel_状態を表示する(HyperVLaunchType type, string? raw, string expectedPart)
    {
        var label = HyperVHelper.DescribeMenuStatusLabel(type, raw);
        Assert.StartsWith("現在の状態：", label);
        Assert.Contains(expectedPart, label);
    }

    [Fact]
    public void DescribeMenuStatusLabel_Unknownは取得不可表示()
        => Assert.Contains("取得できませんでした", HyperVHelper.DescribeMenuStatusLabel(HyperVLaunchType.Unknown, null));

    // ---------- TryParseBackup（厳密パース・E-1 同流儀） ----------

    [Fact]
    public void TryParseBackup_正常JSONを読める()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new HyperVHelper.HyperVBackup(true, "Auto"));
        var b = HyperVHelper.TryParseBackup(json);
        Assert.NotNull(b);
        Assert.True(b!.LaunchTypeExisted);
        Assert.Equal("Auto", b.LaunchTypeValue);
    }

    [Fact]
    public void TryParseBackup_未設定の退避も読める()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new HyperVHelper.HyperVBackup(false, ""));
        var b = HyperVHelper.TryParseBackup(json);
        Assert.NotNull(b);
        Assert.False(b!.LaunchTypeExisted);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]                                          // 全フィールド欠落
    [InlineData("{\"LaunchTypeExisted\":true}")]                // 値欠落
    [InlineData("{\"LaunchTypeValue\":\"Auto\"}")]              // 存在フラグ欠落
    [InlineData("not json")]
    public void TryParseBackup_破損はnull(string? json)
        => Assert.Null(HyperVHelper.TryParseBackup(json));

    [Theory]
    [InlineData("off; calc.exe")]   // 改ざん：コマンド注入
    [InlineData("AutoLimited")]     // C-1：ホワイトリスト（Auto/Off）外＝復元で bcdedit が受理しない
    public void TryParseBackup_存在ありなのに復元不可トークンは破損扱い(string value)
    {
        var json = $"{{\"LaunchTypeExisted\":true,\"LaunchTypeValue\":\"{value}\"}}";
        Assert.Null(HyperVHelper.TryParseBackup(json));
    }
}
