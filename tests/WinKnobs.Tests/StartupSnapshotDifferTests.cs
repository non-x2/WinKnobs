using System.Text;
using Microsoft.Win32;
using WinKnobs.Models;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// StartupSnapshotDiffer（v0.1.6 機能 B：差分件数警告）の単体テスト。
/// snapshot(.reg テキスト) × 現在エントリ → 差分件数 を検証する。
/// </summary>
public class StartupSnapshotDifferTests
{
    // ─── レジストリキーのフルパス（BackupStartupSnapshot がエクスポートする 9 キーのうち主要なもの）───
    private const string HkcuRun       = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string HkcuApproved  = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string HklmRun       = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run";

    // ─── .reg テキスト組み立てヘルパー ───

    private static string Reg(params string[] sections) =>
        "Windows Registry Editor Version 5.00\r\n\r\n" + string.Concat(sections);

    private static string Sec(string path, params string[] lines) =>
        $"[{path}]\r\n" + (lines.Length > 0 ? string.Join("\r\n", lines) + "\r\n" : "") + "\r\n";

    /// <summary>REG_SZ 値行：reg export と同じ `\` `"` エスケープを施す。</summary>
    private static string Sz(string name, string command) =>
        $"\"{name}\"=\"{command.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";

    /// <summary>REG_EXPAND_SZ 値行：hex(2): + UTF-16 LE バイト列（終端 00 00 付き）。</summary>
    private static string Expand(string name, string command)
    {
        var body = Encoding.Unicode.GetBytes(command);
        var all = new byte[body.Length + 2];
        Array.Copy(body, all, body.Length);
        return $"\"{name}\"=hex(2):" + string.Join(",", all.Select(b => b.ToString("x2")));
    }

    /// <summary>StartupApproved 値行：12 byte REG_BINARY（byte[0]=0x02 有効 / 0x03 無効）。</summary>
    private static string Approved(string name, bool enabled)
    {
        var b = new byte[12];
        b[0] = enabled ? (byte)0x02 : (byte)0x03;
        return $"\"{name}\"=hex:" + string.Join(",", b.Select(x => x.ToString("x2")));
    }

    /// <summary>現在エントリ（StartupItemsProvider.LoadAll 相当）の 1 件を作る。</summary>
    private static StartupEntryItem Cur(StartupHive hive, string rawName, string command, bool enabled = true) =>
        new()
        {
            Hive = hive,
            RawName = rawName,
            Name = rawName,
            Command = command,
            ValueKind = RegistryValueKind.String,
            IsEnabled = enabled,
        };

    // ─── テストケース ───

    [Fact]
    public void 差分0_完全一致なら全ゼロ()
    {
        var reg = Reg(
            Sec(HkcuRun, Sz("App1", @"C:\Apps\app1.exe")),
            Sec(HkcuApproved, Approved("App1", enabled: true)));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App1", @"C:\Apps\app1.exe", enabled: true) };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(0, diff.TotalCount);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(0, diff.ModifiedCount);
    }

    [Fact]
    public void 追加_snapshotにあり現在になし_はAdded()
    {
        var reg = Reg(Sec(HkcuRun,
            Sz("App1", @"C:\Apps\app1.exe"),
            Sz("App2", @"C:\Apps\app2.exe")));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App1", @"C:\Apps\app1.exe") };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.TotalCount);
    }

    [Fact]
    public void 削除_現在にありsnapshotになし_はRemoved()
    {
        var reg = Reg(Sec(HkcuRun, Sz("App1", @"C:\Apps\app1.exe")));
        var current = new[]
        {
            Cur(StartupHive.HKCU_Run, "App1", @"C:\Apps\app1.exe"),
            Cur(StartupHive.HKCU_Run, "App3", @"C:\Apps\app3.exe"),
        };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(1, diff.TotalCount);
    }

    [Fact]
    public void 変更_コマンドが異なる_はModified()
    {
        var reg = Reg(Sec(HkcuRun, Sz("App1", @"C:\Apps\app1_NEW.exe")));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App1", @"C:\Apps\app1_OLD.exe") };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(0, diff.AddedCount);
        Assert.Equal(0, diff.RemovedCount);
        Assert.Equal(1, diff.TotalCount);
    }

    [Fact]
    public void 変更_有効無効が異なる_はModified()
    {
        // snapshot=無効(0x03) / 現在=有効 → コマンド同じでも Modified
        var reg = Reg(
            Sec(HkcuRun, Sz("App1", @"C:\Apps\app1.exe")),
            Sec(HkcuApproved, Approved("App1", enabled: false)));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App1", @"C:\Apps\app1.exe", enabled: true) };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(1, diff.TotalCount);
    }

    [Fact]
    public void 複合_追加2削除2変更1_で計5件()
    {
        var reg = Reg(Sec(HkcuRun,
            Sz("A1", @"C:\new.exe"),   // current と command 違い → Modified
            Sz("B1", @"C:\b1.exe"),    // snapshot のみ → Added
            Sz("B2", @"C:\b2.exe")));  // snapshot のみ → Added
        var current = new[]
        {
            Cur(StartupHive.HKCU_Run, "A1", @"C:\old.exe"),  // Modified
            Cur(StartupHive.HKCU_Run, "C1", @"C:\c1.exe"),   // current のみ → Removed
            Cur(StartupHive.HKCU_Run, "C2", @"C:\c2.exe"),   // current のみ → Removed
        };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(2, diff.AddedCount);
        Assert.Equal(2, diff.RemovedCount);
        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(5, diff.TotalCount);
    }

    [Fact]
    public void 大規模_11件追加_で計10件以上()
    {
        var lines = Enumerable.Range(0, 11).Select(i => Sz($"Item{i:D2}", $@"C:\Apps\item{i}.exe")).ToArray();
        var reg = Reg(Sec(HkcuRun, lines));
        var current = Array.Empty<StartupEntryItem>();

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(11, diff.AddedCount);
        Assert.True(diff.TotalCount >= 10);
    }

    [Fact]
    public void エッジ_REG_EXPAND_SZのhex2が未展開文字列と一致して差分0()
    {
        const string cmd = @"%ProgramFiles%\App\app.exe";
        var reg = Reg(Sec(HkcuRun, Expand("App", cmd)));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App", cmd) };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(0, diff.TotalCount);
    }

    [Fact]
    public void エッジ_hex行末バックスラッシュ折り返しを結合できる()
    {
        const string cmd = @"%ProgramFiles%\App\app.exe";
        var expand = Expand("App", cmd);            // "App"=hex(2):xx,xx,...
        // カンマ直後で reg export 風に折り返す（行末 \ + 次行先頭インデント）
        int comma = expand.IndexOf(',', expand.IndexOf(':') + 1);
        var folded = expand[..(comma + 1)] + "\\\r\n  " + expand[(comma + 1)..];

        var reg = Reg(Sec(HkcuRun, folded));
        var current = new[] { Cur(StartupHive.HKCU_Run, "App", cmd) };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(0, diff.TotalCount);
    }

    [Fact]
    public void エッジ_キー削除マーカーは不在扱いで現在側がRemoved()
    {
        // HKLM\Run が削除マーカー（snapshot 取得時に不在）→ 現在の X は snapshot になし＝Removed
        var reg = Reg(Sec($"-{HklmRun}"));
        var current = new[] { Cur(StartupHive.HKLM_Run, "X", @"C:\x.exe") };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(0, diff.AddedCount);
    }

    [Fact]
    public void エッジ_空またはゴミregでも例外を投げず現在側を全Removed()
    {
        var current = new[]
        {
            Cur(StartupHive.HKCU_Run, "App1", @"C:\app1.exe"),
            Cur(StartupHive.HKCU_Run, "App2", @"C:\app2.exe"),
        };

        var empty = StartupSnapshotDiffer.Analyze("", current);
        Assert.Equal(2, empty.RemovedCount);

        var garbage = StartupSnapshotDiffer.Analyze("これは reg ではありません\r\nゴミ行", current);
        Assert.Equal(2, garbage.RemovedCount);
    }

    // ─── cross-reviewer 重要2 反映：プレフィックス / Wow64 / 二重空 の突合カバレッジ追加 ───

    private const string HkcuRunOnce = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string HklmWow64Run     = @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string HklmApprovedRun32 = @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";

    [Fact]
    public void エッジ_RunOnceプレフィックス込みで突合して差分0()
    {
        // snapshot と現在で "!App"（! 込）が一致 → 差分なし
        var reg = Reg(Sec(HkcuRunOnce, Sz("!App", @"C:\once.exe")));
        var current = new[] { Cur(StartupHive.HKCU_RunOnce, "!App", @"C:\once.exe") };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(0, diff.TotalCount);
    }

    [Fact]
    public void エッジ_プレフィックス有無は別エントリとして突合()
    {
        // snapshot="!App"（!込） / 現在="App"（!なし）→ 別キー → Added 1 + Removed 1
        var reg = Reg(Sec(HkcuRunOnce, Sz("!App", @"C:\once.exe")));
        var current = new[] { Cur(StartupHive.HKCU_RunOnce, "App", @"C:\once.exe") };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.AddedCount);
        Assert.Equal(1, diff.RemovedCount);
        Assert.Equal(2, diff.TotalCount);
    }

    [Fact]
    public void エッジ_Wow64のRun32が現在のWow64_Runと突合して有効状態差をModified()
    {
        // snapshot: WOW6432Node\Run に App、StartupApproved\Run32 で無効(0x03)
        // 現在: HKLM_Wow64_Run の App 有効 → command 同じでも enable 差で Modified
        var reg = Reg(
            Sec(HklmWow64Run, Sz("App", @"C:\w.exe")),
            Sec(HklmApprovedRun32, Approved("App", enabled: false)));
        var current = new[] { Cur(StartupHive.HKLM_Wow64_Run, "App", @"C:\w.exe", enabled: true) };

        var diff = StartupSnapshotDiffer.Analyze(reg, current);

        Assert.Equal(1, diff.ModifiedCount);
        Assert.Equal(1, diff.TotalCount);
    }

    [Fact]
    public void エッジ_両方空なら差分ゼロでEmptyと一致()
    {
        var diff = StartupSnapshotDiffer.Analyze("", Array.Empty<StartupEntryItem>());

        Assert.Equal(0, diff.TotalCount);
        Assert.Equal(StartupDiffResult.Empty.TotalCount, diff.TotalCount);
    }
}
