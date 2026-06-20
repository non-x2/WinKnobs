using System.Collections.Generic;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// v0.3.0 VirtualMemoryHelper（純粋関数）のテスト。
/// CIM JSON のパース・手動サイズ検証（E5）・コマンド構築（複数 pagefile 封じ込め）・分類・退避。
/// </summary>
public class VirtualMemoryHelperTests
{
    // ---------- ParseState ----------

    [Fact]
    public void ParseState_自動管理ON()
    {
        var json = """
        {"AutomaticManaged":true,"TotalPhysicalBytes":68719476736,"Settings":[],"Usage":[{"Name":"C:\\pagefile.sys","AllocatedBaseSize":65536}],"PagingFiles":[]}
        """;
        var s = VirtualMemoryHelper.ParseState(json);
        Assert.NotNull(s);
        Assert.True(s!.AutomaticManaged);
        Assert.Equal(65536, s.RamMb);            // 68719476736 / 1MB = 65536
        Assert.False(s.PendingReboot);            // 自動管理は常に false
    }

    [Fact]
    public void ParseState_手動設定をパース()
    {
        var json = """
        {"AutomaticManaged":false,"TotalPhysicalBytes":17179869184,"Settings":[{"Name":"C:\\pagefile.sys","InitialSize":16384,"MaximumSize":32768}],"Usage":[{"Name":"C:\\pagefile.sys","AllocatedBaseSize":32768}],"PagingFiles":["C:\\pagefile.sys 16384 32768"]}
        """;
        var s = VirtualMemoryHelper.ParseState(json);
        Assert.NotNull(s);
        Assert.False(s!.AutomaticManaged);
        Assert.Single(s.Settings);
        Assert.Equal(16384, s.Settings[0].InitialSize);
        Assert.Equal(32768, s.Settings[0].MaxSize);
        Assert.Single(s.PagingFiles);
    }

    [Fact]
    public void ParseState_単一オブジェクトのSettingsも配列扱い()
    {
        // ConvertTo-Json は 1 要素を配列化しないことがある
        var json = """
        {"AutomaticManaged":false,"TotalPhysicalBytes":0,"Settings":{"Name":"C:\\pagefile.sys","InitialSize":100,"MaximumSize":200},"Usage":{"Name":"C:\\pagefile.sys","AllocatedBaseSize":200},"PagingFiles":"C:\\pagefile.sys 100 200"}
        """;
        var s = VirtualMemoryHelper.ParseState(json);
        Assert.NotNull(s);
        Assert.Single(s!.Settings);
        Assert.Single(s.PagingFiles);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{ broken")]
    public void ParseState_不正は_null(string? json)
    {
        Assert.Null(VirtualMemoryHelper.ParseState(json));
    }

    // ---------- ComputePendingReboot（重要6） ----------

    [Fact]
    public void PendingReboot_自動管理は常にfalse()
    {
        Assert.False(VirtualMemoryHelper.ComputePendingReboot(true, new[] { "C:\\pagefile.sys 9999 9999" }, 0));
    }

    [Fact]
    public void PendingReboot_システム管理0_0はfalse()
    {
        Assert.False(VirtualMemoryHelper.ComputePendingReboot(false, new[] { "C:\\pagefile.sys 0 0" }, 12345));
    }

    [Fact]
    public void PendingReboot_設定と稼働が乖離でtrue()
    {
        // 設定最大 32768 vs 稼働 4096 → 乖離大 → 再起動待ち
        Assert.True(VirtualMemoryHelper.ComputePendingReboot(false, new[] { "C:\\pagefile.sys 16384 32768" }, 4096));
    }

    [Fact]
    public void PendingReboot_設定と稼働が一致ならfalse()
    {
        Assert.False(VirtualMemoryHelper.ComputePendingReboot(false, new[] { "C:\\pagefile.sys 16384 32768" }, 32768));
    }

    // ---------- ValidateManual（E5） ----------

    [Fact]
    public void ValidateManual_0_0は弾く()
    {
        var (ok, err) = VirtualMemoryHelper.ValidateManual(0, 0);
        Assert.False(ok);
        Assert.NotNull(err);
    }

    [Theory]
    [InlineData(8, 100)]        // 初期 < 16
    [InlineData(0, 100)]        // 初期 < 16
    public void ValidateManual_初期が下限未満は弾く(int init, int max)
    {
        Assert.False(VirtualMemoryHelper.ValidateManual(init, max).Ok);
    }

    [Fact]
    public void ValidateManual_最大が初期未満は弾く()
    {
        Assert.False(VirtualMemoryHelper.ValidateManual(1000, 500).Ok);
    }

    [Fact]
    public void ValidateManual_上限超は弾く()
    {
        Assert.False(VirtualMemoryHelper.ValidateManual(16, 2_000_000).Ok);  // > 1TB
    }

    [Theory]
    [InlineData(16, 16)]
    [InlineData(16384, 65536)]
    [InlineData(1024, 1048576)]
    public void ValidateManual_妥当な値は通る(int init, int max)
    {
        Assert.True(VirtualMemoryHelper.ValidateManual(init, max).Ok);
    }

    // ---------- BuildApplyCommand（複数pagefile封じ込め） ----------

    [Fact]
    public void BuildApply_自動管理()
    {
        var cmd = VirtualMemoryHelper.BuildApplyCommand(VmApplyMode.AutoManaged, 0, 0);
        Assert.Contains("AutomaticManagedPagefile=$true", cmd);
        Assert.Contains(VirtualMemoryHelper.ResultMarker, cmd);
    }

    [Fact]
    public void BuildApply_システム管理は自動OFF_単一エントリ0_0()
    {
        var cmd = VirtualMemoryHelper.BuildApplyCommand(VmApplyMode.SystemManaged, 0, 0);
        Assert.Contains("AutomaticManagedPagefile=$false", cmd);
        Assert.Contains(@"C:\pagefile.sys 0 0", cmd);
        Assert.Contains("MultiString", cmd);   // 明示1エントリ＝複数pagefile封じ込め
    }

    [Fact]
    public void BuildApply_手動は自動OFF_指定サイズの単一エントリ()
    {
        var cmd = VirtualMemoryHelper.BuildApplyCommand(VmApplyMode.Manual, 16384, 65536);
        Assert.Contains("AutomaticManagedPagefile=$false", cmd);
        Assert.Contains(@"C:\pagefile.sys 16384 65536", cmd);
    }

    // ---------- BuildRestoreCommand ----------

    [Fact]
    public void BuildRestore_自動管理に戻すならPagingFilesは書かない()
    {
        var cmd = VirtualMemoryHelper.BuildRestoreCommand(new VmBackup(true, new[] { "C:\\pagefile.sys 1 2" }));
        Assert.Contains("AutomaticManagedPagefile=$true", cmd);
        Assert.DoesNotContain("Set-ItemProperty", cmd);   // 自動管理＝OS 任せ
    }

    [Fact]
    public void BuildRestore_手動に戻すなら退避PagingFilesを書く()
    {
        var cmd = VirtualMemoryHelper.BuildRestoreCommand(new VmBackup(false, new[] { "C:\\pagefile.sys 16384 65536" }));
        Assert.Contains("AutomaticManagedPagefile=$false", cmd);
        Assert.Contains(@"C:\pagefile.sys 16384 65536", cmd);
    }

    // ---------- Classify ----------

    [Fact]
    public void Classify_成功()
    {
        Assert.Equal(VmOutcome.Success, VirtualMemoryHelper.Classify(0, "...\n" + VirtualMemoryHelper.ResultMarker));
    }

    [Theory]
    [InlineData("Access is denied")]
    [InlineData("アクセスが拒否されました")]
    [InlineData("PrivilegeNotHeld")]
    public void Classify_権限不足(string output)
    {
        Assert.Equal(VmOutcome.AccessDenied, VirtualMemoryHelper.Classify(1, output));
    }

    [Fact]
    public void Classify_その他失敗()
    {
        Assert.Equal(VmOutcome.Failed, VirtualMemoryHelper.Classify(1, "something went wrong"));
    }

    [Fact]
    public void Classify_マーカー無し成功コードでも失敗扱い()
    {
        Assert.Equal(VmOutcome.Failed, VirtualMemoryHelper.Classify(0, "no marker here"));
    }

    [Fact]
    public void Classify_マーカーありでも非ゼロ終了は失敗()
    {
        // E-1：exitCode != 0 なら ResultMarker が出ていても成功にしない（AND ガード）
        Assert.Equal(VmOutcome.Failed, VirtualMemoryHelper.Classify(1, "x " + VirtualMemoryHelper.ResultMarker));
    }

    // ---------- IsSafePagingLine（S-1・PS 注入対策） ----------

    [Theory]
    [InlineData(@"C:\pagefile.sys 0 0")]
    [InlineData(@"C:\pagefile.sys 16384 65536")]
    [InlineData(@"D:\pagefile.sys 100 200")]
    public void IsSafePagingLine_正常形式は可(string line)
    {
        Assert.True(VirtualMemoryHelper.IsSafePagingLine(line));
    }

    [Theory]
    [InlineData(@"C:\pagefile.sys 0 0'; Remove-Item C:\ -Recurse")]  // シングルクォート注入
    [InlineData("C:\\pagefile.sys 0 0`nWrite-Host hi")]               // バッククォート/改行
    [InlineData("C:\\pagefile.sys $env 0")]                            // $ 変数
    [InlineData("garbage")]
    [InlineData(null)]
    [InlineData("")]
    public void IsSafePagingLine_危険または不正は拒否(string? line)
    {
        Assert.False(VirtualMemoryHelper.IsSafePagingLine(line));
    }

    [Fact]
    public void BuildRestore_危険なPagingFiles行は除外される()
    {
        var cmd = VirtualMemoryHelper.BuildRestoreCommand(
            new VmBackup(false, new[] { "C:\\pagefile.sys 0 0'; rm -r" }));
        Assert.DoesNotContain("rm -r", cmd);          // 危険行は注入されない
        Assert.DoesNotContain("Set-ItemProperty", cmd); // 安全行が無いので PagingFiles は書かない
    }

    // ---------- 退避シリアライズ ----------

    [Fact]
    public void Backup_ラウンドトリップ()
    {
        var state = new VirtualMemoryState(false, 65536,
            new List<VmPageFileSetting> { new("C:\\pagefile.sys", 16384, 65536) },
            new List<string> { "C:\\pagefile.sys 16384 65536" }, 65536, false);
        var token = VirtualMemoryHelper.SerializeBackup(state);
        var b = VirtualMemoryHelper.ParseBackup(token);
        Assert.NotNull(b);
        Assert.False(b!.AutomaticManaged);
        Assert.Single(b.PagingFiles);
        Assert.Equal("C:\\pagefile.sys 16384 65536", b.PagingFiles[0]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{ broken json")]
    public void ParseBackup_破損はnull(string? json)
    {
        Assert.Null(VirtualMemoryHelper.ParseBackup(json));
    }

    // ---------- 表示ラベル ----------

    [Fact]
    public void DescribeMenuStatusLabel_null時()
    {
        Assert.Contains("取得できませんでした", VirtualMemoryHelper.DescribeMenuStatusLabel(null));
    }

    [Fact]
    public void DescribeMenuStatusLabel_自動管理()
    {
        var s = new VirtualMemoryState(true, 65536, new List<VmPageFileSetting>(), new List<string>(), 65536, false);
        Assert.Contains("自動管理", VirtualMemoryHelper.DescribeMenuStatusLabel(s));
    }
}
