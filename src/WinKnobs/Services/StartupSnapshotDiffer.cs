using System.IO;
using System.Linq;
using System.Text;
using WinKnobs.Models;

namespace WinKnobs.Services;

/// <summary>
/// スタートアップ snapshot(.reg) と現在のレジストリ状態を比較し差分を算出する純粋ロジック（v0.1.6 機能 B）。
///
/// 設計改善（設計ドラフト v2 §B-3 からの変更）：
///   設計書案は BackupService.AnalyzeSnapshotDiff(snapshotRegPath) だったが、
///   それだと I/O 層（BackupService）が VM 層（StartupItemsProvider）へ逆依存する。
///   現在エントリを引数で受ける純粋関数にして層を分離し、ファイル無しで単体テスト可能にした。
///   StartupApprovedCodec と同じ static 純粋クラスの流儀に合わせる。
///
/// snapshot は読込のみ（reg import しない）＝副作用なし。
/// 突合キー・有効状態の判定は StartupItemsProvider.MergeApprovedState と一致させている
/// （RawName を OrdinalIgnoreCase 比較・StartupApproved\Run→HKLM_Run / Run32→Wow64_Run / HKCU→HKCU_Run・
///   RunOnce 系と未登録は有効扱い）。
/// </summary>
public static class StartupSnapshotDiffer
{
    /// <summary>snapshot 内の 1 エントリ（Run キーの値 + 突合した有効状態）。</summary>
    private sealed record SnapshotEntry(StartupHive Hive, string RawName, string Command, bool IsEnabled);

    // BackupStartupSnapshot がエクスポートする 9 キー（6 Run + 3 StartupApproved）→ 種別マップ。
    // StartupApproved は MergeApprovedState と同じ対応で Run ハイブに紐づける。
    private static readonly Dictionary<string, (bool IsApproved, StartupHive Hive)> SectionMap =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run"]                 = (false, StartupHive.HKLM_Run),
            [@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunOnce"]             = (false, StartupHive.HKLM_RunOnce),
            [@"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"]     = (false, StartupHive.HKLM_Wow64_Run),
            [@"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"] = (false, StartupHive.HKLM_Wow64_RunOnce),
            [@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run"]                  = (false, StartupHive.HKCU_Run),
            [@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce"]              = (false, StartupHive.HKCU_RunOnce),
            [@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"]   = (true, StartupHive.HKLM_Run),
            [@"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"] = (true, StartupHive.HKLM_Wow64_Run),
            [@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"]    = (true, StartupHive.HKCU_Run),
        };

    /// <summary>snapshot ファイルを読み込んで現在エントリと比較。UTF-16 LE BOM は自動判定。</summary>
    public static StartupDiffResult AnalyzeFile(string snapshotRegPath, IEnumerable<StartupEntryItem> currentEntries)
    {
        var text = File.ReadAllText(snapshotRegPath);
        return Analyze(text, currentEntries);
    }

    /// <summary>snapshot の .reg テキストを現在エントリと比較（テスト用・純粋）。</summary>
    public static StartupDiffResult Analyze(string regText, IEnumerable<StartupEntryItem> currentEntries)
    {
        var snapshot = ParseSnapshot(regText);

        var snapByKey = new Dictionary<string, SnapshotEntry>();
        foreach (var e in snapshot) snapByKey[Key(e.Hive, e.RawName)] = e;

        var curByKey = new Dictionary<string, StartupEntryItem>();
        foreach (var e in currentEntries) curByKey[Key(e.Hive, e.RawName)] = e;

        var added    = new List<string>();
        var removed  = new List<string>();
        var modified = new List<string>();

        // snapshot 視点：snapshot にあり / 現在なし → Added、両方あり → command/有効状態で Modified 判定
        foreach (var kv in snapByKey)
        {
            var snap = kv.Value;
            if (!curByKey.TryGetValue(kv.Key, out var cur))
            {
                added.Add(Format(snap.Hive, snap.RawName));
            }
            else
            {
                var curEnabled  = cur.IsEnabled ?? true;
                var commandDiff = !string.Equals(snap.Command, cur.Command, StringComparison.Ordinal);
                var enabledDiff = snap.IsEnabled != curEnabled;
                if (commandDiff || enabledDiff)
                {
                    // C（v0.1.6）：プレビュー UI の「✏変更予定」ペインで「何が変わるか」を見せるため、
                    // 名前だけでなく変更種別タグを付ける（名前のみだと機能 B の件数警告と情報量が変わらない）。
                    var tag = (commandDiff, enabledDiff) switch
                    {
                        (true, true)  => "コマンド・状態変更",
                        (true, false) => "コマンド変更",
                        (false, true) => snap.IsEnabled ? "無効→有効" : "有効→無効",
                        _             => "変更"
                    };
                    modified.Add(Format(snap.Hive, snap.RawName) + $"（{tag}）");
                }
            }
        }

        // 現在にあり / snapshot になし → Removed
        foreach (var kv in curByKey)
        {
            if (!snapByKey.ContainsKey(kv.Key))
                removed.Add(Format(kv.Value.Hive, kv.Value.RawName));
        }

        return new StartupDiffResult(added, removed, modified);
    }

    private static IReadOnlyList<SnapshotEntry> ParseSnapshot(string regText)
    {
        var runValues = new List<(StartupHive Hive, string RawName, string Command)>();
        var approved  = new Dictionary<string, bool>();  // Key(hive, rawName) → enabled

        (bool IsApproved, StartupHive Hive)? current = null;

        foreach (var logical in LogicalLines(regText))
        {
            var line = logical.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith(";", StringComparison.Ordinal)) continue;
            if (line.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase)) continue;

            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                // [path] か [-path]（キー削除マーカー）
                var inner = line.TrimStart('[').TrimEnd(']');
                if (inner.StartsWith("-", StringComparison.Ordinal))
                {
                    current = null;  // 削除マーカー＝キー不在＝エントリなし
                    continue;
                }
                current = SectionMap.TryGetValue(inner, out var sec) ? sec : ((bool, StartupHive)?)null;
                continue;
            }

            if (current == null) continue;
            if (!TryParseValueLine(line, out var rawName, out var valueToken)) continue;
            if (rawName.Length == 0) continue;  // "@"=default 等はスキップ

            if (current.Value.IsApproved)
            {
                var bytes = TryParseHexBinary(valueToken);
                var (enabled, _) = StartupApprovedCodec.Parse(bytes);
                approved[Key(current.Value.Hive, rawName)] = enabled;
            }
            else
            {
                var command = DecodeStringValue(valueToken);
                runValues.Add((current.Value.Hive, rawName, command));
            }
        }

        var result = new List<SnapshotEntry>();
        foreach (var (hive, rawName, command) in runValues)
        {
            bool enabled = true;  // RunOnce 系・StartupApproved 未登録は有効扱い（MergeApprovedState と整合）
            if (IsApprovedLinked(hive) && approved.TryGetValue(Key(hive, rawName), out var e))
                enabled = e;
            result.Add(new SnapshotEntry(hive, rawName, command, enabled));
        }
        return result;
    }

    private static bool IsApprovedLinked(StartupHive hive) =>
        hive is StartupHive.HKLM_Run or StartupHive.HKCU_Run or StartupHive.HKLM_Wow64_Run;

    /// <summary>行末 `\` で折り返された hex 行を 1 論理行に結合する。</summary>
    private static IEnumerable<string> LogicalLines(string regText)
    {
        var lines = regText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var sb = new StringBuilder();
        bool continuing = false;

        foreach (var raw in lines)
        {
            if (continuing)
            {
                sb.Append(raw.Trim());
            }
            else
            {
                if (sb.Length > 0) { yield return sb.ToString(); sb.Clear(); }
                sb.Append(raw);
            }

            var trimmedEnd = sb.ToString().TrimEnd();
            if (trimmedEnd.EndsWith("\\", StringComparison.Ordinal))
            {
                sb.Clear();
                sb.Append(trimmedEnd.Substring(0, trimmedEnd.Length - 1));
                continuing = true;
            }
            else
            {
                continuing = false;
            }
        }
        if (sb.Length > 0) yield return sb.ToString();
    }

    /// <summary>`"name"=value` を名前と value トークンに分解（エスケープ `\"` `\\` を考慮）。</summary>
    private static bool TryParseValueLine(string line, out string name, out string value)
    {
        name = ""; value = "";
        if (!line.StartsWith("\"", StringComparison.Ordinal)) return false;

        int i = 1;
        var sb = new StringBuilder();
        while (i < line.Length)
        {
            var c = line[i];
            if (c == '\\' && i + 1 < line.Length)
            {
                sb.Append(line[i + 1]);
                i += 2;
                continue;
            }
            if (c == '"') { i++; break; }
            sb.Append(c);
            i++;
        }
        if (i >= line.Length || line[i] != '=') return false;
        name  = sb.ToString();
        value = line.Substring(i + 1).Trim();
        return true;
    }

    /// <summary>value トークンを文字列コマンドにデコード（REG_SZ / REG_EXPAND_SZ / REG_MULTI_SZ）。</summary>
    private static string DecodeStringValue(string token)
    {
        if (token.StartsWith("\"", StringComparison.Ordinal))
        {
            var inner = token.Substring(1);
            if (inner.EndsWith("\"", StringComparison.Ordinal))
                inner = inner.Substring(0, inner.Length - 1);
            return Unescape(inner);
        }
        if (token.StartsWith("hex(2):", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = TryParseHexBytes(token.Substring("hex(2):".Length));
            return bytes == null ? "" : Encoding.Unicode.GetString(bytes).TrimEnd('\0');
        }
        if (token.StartsWith("hex(7):", StringComparison.OrdinalIgnoreCase))
        {
            var bytes = TryParseHexBytes(token.Substring("hex(7):".Length));
            return bytes == null ? "" : Encoding.Unicode.GetString(bytes).Replace('\0', ' ').Trim();
        }
        // dword: 等はそのまま比較トークンとして扱う
        return token;
    }

    private static string Unescape(string s) =>
        s.Replace("\\\\", "\\").Replace("\\\"", "\"");

    private static byte[]? TryParseHexBinary(string token)
    {
        var idx = token.IndexOf(':');
        var hexPart = idx >= 0 ? token.Substring(idx + 1) : token;
        return TryParseHexBytes(hexPart);
    }

    private static byte[]? TryParseHexBytes(string hex)
    {
        try
        {
            var parts = hex.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var bytes = new byte[parts.Length];
            for (int i = 0; i < parts.Length; i++)
                bytes[i] = Convert.ToByte(parts[i], 16);
            return bytes;
        }
        catch
        {
            return null;
        }
    }

    private static string Key(StartupHive hive, string rawName) =>
        ((int)hive).ToString() + " " + rawName.ToLowerInvariant();

    private static string Format(StartupHive hive, string rawName) =>
        HiveShort(hive) + "\\" + StripPrefix(rawName);

    private static string StripPrefix(string rawName)
    {
        int i = 0;
        while (i < rawName.Length && (rawName[i] == '!' || rawName[i] == '*')) i++;
        return rawName.Substring(i);
    }

    private static string HiveShort(StartupHive hive) => hive switch
    {
        StartupHive.HKLM_Run           => @"HKLM\Run",
        StartupHive.HKLM_RunOnce       => @"HKLM\RunOnce",
        StartupHive.HKCU_Run           => @"HKCU\Run",
        StartupHive.HKCU_RunOnce       => @"HKCU\RunOnce",
        StartupHive.HKLM_Wow64_Run     => @"HKLM\WOW64\Run",
        StartupHive.HKLM_Wow64_RunOnce => @"HKLM\WOW64\RunOnce",
        _ => hive.ToString()
    };
}
