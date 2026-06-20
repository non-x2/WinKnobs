using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WinKnobs.Services;

/// <summary>仮想メモリ（ページファイル）設定の適用モード。</summary>
public enum VmApplyMode
{
    /// <summary>自動管理（Windows に任せる・推奨）。</summary>
    AutoManaged,
    /// <summary>システム管理サイズ（自動管理 OFF＋ C: を 0 0）。</summary>
    SystemManaged,
    /// <summary>手動（初期/最大を MB 指定）。</summary>
    Manual
}

/// <summary>仮想メモリ操作の結果分類。</summary>
public enum VmOutcome
{
    /// <summary>成功（再起動後に反映）。</summary>
    Success,
    /// <summary>管理者権限不足。</summary>
    AccessDenied,
    /// <summary>その他の失敗。</summary>
    Failed
}

/// <summary>Win32_PageFileSetting 1 件（設定値・configured）。</summary>
public record VmPageFileSetting(string Name, int InitialSize, int MaxSize);

/// <summary>仮想メモリの現在状態（読取結果）。</summary>
public record VirtualMemoryState(
    bool AutomaticManaged,
    long RamMb,
    IReadOnlyList<VmPageFileSetting> Settings,
    IReadOnlyList<string> PagingFiles,
    long RunningTotalMb,
    bool PendingReboot);

/// <summary>仮想メモリ退避（原状復帰の素材）。AutomaticManaged ＋ レジストリ PagingFiles 全文。</summary>
public record VmBackup(bool AutomaticManaged, IReadOnlyList<string> PagingFiles);

/// <summary>
/// v0.3.0 仮想メモリ（ページファイル）の純粋関数群（I/O なし・xUnit 対象）。
/// 実行は <see cref="VirtualMemoryService"/>（PowerShell/CIM 経由・要管理者・要再起動・🔴Red）。
///
/// 設計（v0.3.0 設計ドラフト §2＋🔧 plan-validator 反映）：
///   - 読取は CIM（Win32_ComputerSystem/PageFileSetting/PageFileUsage）＋レジストリ PagingFiles を 1 回の PS で取得。
///   - 書込は **AutomaticManagedPagefile（CIM）と PagingFiles（レジストリ MULTI_SZ）を 1 コマンドで原子的に**。
///     手動/システム管理では PagingFiles を **明示 1 エントリ**で書く＝複数 pagefile 生成の MS 既知バグを構造的に封じる（§2-1）。
///   - 手動サイズ検証（E5）：0/0 はシステム管理化するので弾く・初期≥16MB・最大≥初期・最大≤上限。
///   - "無効化（なし）" プリセットは提供しない（ブート/ダンプ/休止リスク）。
///   - PendingReboot（重要6）：レジストリ設定値（will-apply）と稼働中サイズの乖離で「再起動待ち」を best-effort 判定。
/// </summary>
public static class VirtualMemoryHelper
{
    /// <summary>メモリ管理キー（PagingFiles・REG_MULTI_SZ）。</summary>
    public const string MemoryManagementKey =
        @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management";

    /// <summary>システムドライブのページファイルパス（既定）。</summary>
    public const string SystemPagefile = @"C:\pagefile.sys";

    /// <summary>手動初期サイズの下限 MB（これ未満は実質無効化＝弾く・E5）。</summary>
    public const int MinInitialMb = 16;

    /// <summary>手動最大サイズの上限 MB（1TB・タイプミスの暴走値を弾く）。</summary>
    public const int MaxSizeCapMb = 1_048_576;

    /// <summary>成功マーカー（適用 PS の末尾で echo）。</summary>
    public const string ResultMarker = "WINKNOBS_RESULT_OK";

    /// <summary>稼働値と設定値の乖離をこれ以上で「再起動待ち」とみなす閾値 MB。</summary>
    private const long PendingThresholdMb = 256;

    // ============ 読取コマンド ============

    /// <summary>CIM＋レジストリ PagingFiles を 1 つの JSON で出力する読取 PS（非昇格でも概ね通る）。</summary>
    public static string BuildReadCommand()
    {
        return
            "$ErrorActionPreference='SilentlyContinue';" +
            "$ProgressPreference='SilentlyContinue';" +
            "$cs=Get-CimInstance Win32_ComputerSystem;" +
            "$pf=@(Get-CimInstance Win32_PageFileSetting | Select-Object Name,InitialSize,MaximumSize);" +
            "$pu=@(Get-CimInstance Win32_PageFileUsage | Select-Object Name,AllocatedBaseSize);" +
            "$mm='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management';" +
            "$paging=@((Get-ItemProperty -Path $mm -Name PagingFiles -ErrorAction SilentlyContinue).PagingFiles);" +
            "[pscustomobject]@{AutomaticManaged=[bool]$cs.AutomaticManagedPagefile;" +
            "TotalPhysicalBytes=[int64]$cs.TotalPhysicalMemory;Settings=$pf;Usage=$pu;PagingFiles=$paging}" +
            " | ConvertTo-Json -Depth 4 -Compress";
    }

    // ============ 状態パース ============

    /// <summary>読取 PS の JSON 出力を状態へパース。失敗時は null。</summary>
    public static VirtualMemoryState? ParseState(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        var start = json.IndexOf('{');
        var end = json.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        try
        {
            using var doc = JsonDocument.Parse(json.Substring(start, end - start + 1));
            var root = doc.RootElement;

            bool auto = root.TryGetProperty("AutomaticManaged", out var a) && a.ValueKind == JsonValueKind.True;
            long ramMb = 0;
            if (root.TryGetProperty("TotalPhysicalBytes", out var tb) && tb.TryGetInt64(out var bytes))
                ramMb = bytes / (1024 * 1024);

            var settings = new List<VmPageFileSetting>();
            foreach (var e in EnumerateArray(root, "Settings"))
            {
                var name = GetString(e, "Name") ?? "";
                var init = GetInt(e, "InitialSize");
                var max = GetInt(e, "MaximumSize");
                if (!string.IsNullOrEmpty(name)) settings.Add(new VmPageFileSetting(name, init, max));
            }

            long runningTotal = 0;
            foreach (var e in EnumerateArray(root, "Usage"))
                runningTotal += GetInt(e, "AllocatedBaseSize");

            var paging = new List<string>();
            foreach (var e in EnumerateArray(root, "PagingFiles"))
                if (e.ValueKind == JsonValueKind.String)
                {
                    var s = e.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) paging.Add(s!);
                }

            var pending = ComputePendingReboot(auto, paging, runningTotal);
            return new VirtualMemoryState(auto, ramMb, settings, paging, runningTotal, pending);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 「再起動待ち」判定（best-effort・重要6）：自動管理なら false（OS 任せ）。
    /// 手動/システム管理は PagingFiles の設定最大サイズと稼働中サイズの差が閾値超なら true。
    /// 解釈できない時は false（安全側＝余計な警告を出さない）。
    /// </summary>
    public static bool ComputePendingReboot(bool autoManaged, IReadOnlyList<string> pagingFiles, long runningTotalMb)
    {
        if (autoManaged) return false;
        long configuredMax = 0;
        bool any = false;
        foreach (var line in pagingFiles)
        {
            // "C:\pagefile.sys <init> <max>"。"0 0" はシステム管理＝サイズ不定（判定対象外）。
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 3
                && int.TryParse(parts[^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var max)
                && int.TryParse(parts[^2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var init))
            {
                if (init == 0 && max == 0) return false;   // システム管理＝判定不能
                configuredMax += max;
                any = true;
            }
        }
        if (!any) return false;
        return Math.Abs(configuredMax - runningTotalMb) > PendingThresholdMb;
    }

    // ============ 手動サイズ検証（E5） ============

    /// <summary>手動サイズの検証。0/0 はシステム管理化するので弾く・初期≥16・最大≥初期・最大≤1TB。</summary>
    public static (bool Ok, string? Error) ValidateManual(int initialMb, int maxMb)
    {
        if (initialMb == 0 && maxMb == 0)
            return (false, "0 / 0 を入力すると「システム管理サイズ」と同じ意味になります。手動にする場合は初期サイズに 16MB 以上を入力してください。");
        if (initialMb < MinInitialMb)
            return (false, $"初期サイズは {MinInitialMb}MB 以上にしてください（小さすぎると不安定になります）。");
        if (maxMb < initialMb)
            return (false, "最大サイズは初期サイズ以上にしてください。");
        if (maxMb > MaxSizeCapMb)
            return (false, $"最大サイズが大きすぎます（上限 {MaxSizeCapMb} MB ＝ 1TB）。入力を確認してください。");
        return (true, null);
    }

    // ============ 適用コマンド ============

    /// <summary>
    /// 適用 PS を構築。AutomaticManagedPagefile（CIM）＋ PagingFiles（レジストリ MULTI_SZ・明示1エントリ）を原子的に。
    /// Manual/SystemManaged は PagingFiles を 1 行だけ書く＝複数 pagefile 生成バグの封じ込め（§2-1）。
    /// </summary>
    public static string BuildApplyCommand(VmApplyMode mode, int initialMb, int maxMb)
    {
        var head =
            "$ErrorActionPreference='Stop';" +
            "$ProgressPreference='SilentlyContinue';" +
            "$mm='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management';";

        switch (mode)
        {
            case VmApplyMode.AutoManaged:
                return head +
                    "Set-CimInstance -Query 'SELECT * FROM Win32_ComputerSystem' -Property @{AutomaticManagedPagefile=$true};" +
                    $"'{ResultMarker}'";

            case VmApplyMode.SystemManaged:
                return head +
                    "Set-CimInstance -Query 'SELECT * FROM Win32_ComputerSystem' -Property @{AutomaticManagedPagefile=$false};" +
                    $"Set-ItemProperty -Path $mm -Name 'PagingFiles' -Value @('{SystemPagefile} 0 0') -Type MultiString;" +
                    $"'{ResultMarker}'";

            case VmApplyMode.Manual:
                return head +
                    "Set-CimInstance -Query 'SELECT * FROM Win32_ComputerSystem' -Property @{AutomaticManagedPagefile=$false};" +
                    $"Set-ItemProperty -Path $mm -Name 'PagingFiles' -Value @('{SystemPagefile} {initialMb} {maxMb}') -Type MultiString;" +
                    $"'{ResultMarker}'";

            default:
                throw new ArgumentOutOfRangeException(nameof(mode));
        }
    }

    /// <summary>退避から原状復帰する PS を構築（AutomaticManaged ＋ PagingFiles 全文を書き戻す）。</summary>
    public static string BuildRestoreCommand(VmBackup backup)
    {
        var head =
            "$ErrorActionPreference='Stop';" +
            "$ProgressPreference='SilentlyContinue';" +
            "$mm='HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management';";

        var autoLiteral = backup.AutomaticManaged ? "$true" : "$false";
        var sb = new StringBuilder(head);
        sb.Append($"Set-CimInstance -Query 'SELECT * FROM Win32_ComputerSystem' -Property @{{AutomaticManagedPagefile={autoLiteral}}};");

        // 自動管理に戻す場合は PagingFiles は OS 管理に委ねる（明示書込しない）。
        // 手動/システム管理に戻す場合は退避した PagingFiles 全文をそのまま書き戻す。
        // S-1：退避 JSON は %APPDATA% にあり改ざんされ得る → 安全な形式の行だけをコマンドに通す（PS 注入対策）。
        var safeLines = backup.PagingFiles.Where(IsSafePagingLine).ToList();
        if (!backup.AutomaticManaged && safeLines.Count > 0)
        {
            var quoted = string.Join(",", safeLines.Select(l => "'" + EscapeSingle(l) + "'"));
            sb.Append($"Set-ItemProperty -Path $mm -Name 'PagingFiles' -Value @({quoted}) -Type MultiString;");
        }
        sb.Append($"'{ResultMarker}'");
        return sb.ToString();
    }

    private static string EscapeSingle(string s) => s.Replace("'", "''");

    /// <summary>
    /// PagingFiles 行が安全な形式か（退避 JSON 改ざん経由の PowerShell 注入対策・S-1）。
    /// 期待形式＝ "ドライブ:\パス &lt;初期&gt; &lt;最大&gt;"（例 "C:\pagefile.sys 0 0"）。
    /// 引用符・改行・制御文字・`$`・バッククォート・`;`・`%` を含む行は拒否（注入の足場を断つ）。
    /// </summary>
    public static bool IsSafePagingLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return false;
        if (line.IndexOfAny(new[] { '\'', '"', '\r', '\n', '`', '$', '%', ';' }) >= 0) return false;
        return Regex.IsMatch(line, @"^[A-Za-z]:\\[^\s]+\s+\d+\s+\d+$");
    }

    // ============ 分類 ============

    /// <summary>適用/復元の結果を分類（NicPowerHelper.Classify 同型）。</summary>
    public static VmOutcome Classify(int exitCode, string? output)
    {
        var o = output ?? string.Empty;
        if (exitCode == 0 && o.Contains(ResultMarker, StringComparison.Ordinal))
            return VmOutcome.Success;
        if (LooksLikeAccessDenied(o))
            return VmOutcome.AccessDenied;
        return VmOutcome.Failed;
    }

    private static bool LooksLikeAccessDenied(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return false;
        return msg.Contains("Access is denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Access denied", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("アクセスが拒否", StringComparison.Ordinal)
            || msg.Contains("PrivilegeNotHeld", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("Requires elevation", StringComparison.OrdinalIgnoreCase)
            || msg.Contains("特権", StringComparison.Ordinal)
            || msg.Contains("UnauthorizedAccess", StringComparison.OrdinalIgnoreCase);
    }

    // ============ 退避シリアライズ ============

    /// <summary>状態から退避トークン（JSON）を作る。</summary>
    public static string SerializeBackup(VirtualMemoryState state)
        => JsonSerializer.Serialize(new VmBackup(state.AutomaticManaged, state.PagingFiles.ToList()));

    /// <summary>退避トークンをパース（破損時は null・E2）。</summary>
    public static VmBackup? ParseBackup(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            var b = JsonSerializer.Deserialize<VmBackup>(json);
            if (b == null) return null;
            return new VmBackup(b.AutomaticManaged, b.PagingFiles ?? new List<string>());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ============ 表示ラベル ============

    /// <summary>ダイアログの「現在の状態」テキスト。</summary>
    public static string DescribeState(VirtualMemoryState? s)
    {
        if (s == null) return "現在の状態：取得できませんでした（管理者で起動すると確実に取得できます）。";

        var sb = new StringBuilder();
        sb.Append(s.AutomaticManaged
            ? "自動管理：ON（Windows に任せる）"
            : "自動管理：OFF（手動設定）");
        sb.Append($"\n搭載メモリ：約 {Math.Round(s.RamMb / 1024.0, 1)} GB");

        if (s.AutomaticManaged)
        {
            sb.Append("\nページファイル：Windows が自動で管理");
        }
        else if (s.PagingFiles.Count > 0)
        {
            sb.Append("\n設定値：" + string.Join(" / ", s.PagingFiles.Select(DescribePagingLine)));
        }
        else if (s.Settings.Count > 0)
        {
            sb.Append("\n設定値：" + string.Join(" / ",
                s.Settings.Select(x => $"{x.Name} {DescribeSize(x.InitialSize, x.MaxSize)}")));
        }

        if (s.PendingReboot)
            sb.Append("\n⏳ 再起動待ちの変更があります（次回起動で反映）。");

        return sb.ToString();
    }

    private static string DescribePagingLine(string line)
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 3
            && int.TryParse(parts[^2], out var init) && int.TryParse(parts[^1], out var max))
        {
            var name = string.Join(" ", parts.Take(parts.Length - 2));
            return $"{name} {DescribeSize(init, max)}";
        }
        return line;
    }

    private static string DescribeSize(int init, int max)
        => (init == 0 && max == 0) ? "システム管理" : $"初期 {init}MB / 最大 {max}MB";

    /// <summary>メニュー常設の状態ラベル（読み取り専用）。</summary>
    public static string DescribeMenuStatusLabel(VirtualMemoryState? s)
    {
        if (s == null) return "仮想メモリ：取得できませんでした（管理者起動で表示）";
        if (s.AutomaticManaged) return "仮想メモリ：自動管理（Windows に任せる）";
        if (s.PagingFiles.Count > 0)
            return "仮想メモリ：" + string.Join(" / ", s.PagingFiles.Select(DescribePagingLine))
                   + (s.PendingReboot ? "（⏳再起動待ち）" : "");
        return "仮想メモリ：手動設定";
    }

    // ============ JSON ヘルパ（単一/配列ゆれ吸収） ============

    private static IEnumerable<JsonElement> EnumerateArray(JsonElement root, string prop)
    {
        if (!root.TryGetProperty(prop, out var el)) yield break;
        if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var e in el.EnumerateArray()) yield return e;
        }
        else if (el.ValueKind != JsonValueKind.Null)
        {
            yield return el;   // 単一要素は ConvertTo-Json が配列化しない → 1 件として扱う
        }
    }

    private static string? GetString(JsonElement e, string prop)
        => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    private static int GetInt(JsonElement e, string prop)
    {
        if (e.ValueKind == JsonValueKind.Object && e.TryGetProperty(prop, out var v))
        {
            if (v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var n)) return n;
            if (v.ValueKind == JsonValueKind.String && int.TryParse(v.GetString(), out var m)) return m;
        }
        return 0;
    }
}
