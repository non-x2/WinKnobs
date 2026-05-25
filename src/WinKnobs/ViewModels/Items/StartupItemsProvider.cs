using Microsoft.Win32;
using WinKnobs.Models;
using WinKnobs.Services;

namespace WinKnobs.ViewModels.Items;

/// <summary>
/// スタートアップエントリ（Run キー族）の読み込み・書き込みの純粋ロジック。
/// 設計書 4-1 / 6-1 参照。
///
/// 責務分担：
///   RegistryService は単体値の R/W のみ担当。
///   キー全体走査・StartupApproved 突合はこの Provider が担当（Microsoft.Win32.Registry を直接操作）。
/// </summary>
public class StartupItemsProvider
{
    // ─── Run キー族（6 ハイブ）の物理パス ───
    private const string RunSubKey       = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnceSubKey   = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Wow64RunSubKey     = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string Wow64RunOnceSubKey = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";

    // ─── StartupApproved（タスクマネージャー連動）───
    private const string ApprovedRun     = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32   = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedRunOnce = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\RunOnce";
    // ※ StartupApproved\StartupFolder はスコープ外（設計書 3-2・plan-validator #3）。
    //   ヘッダ注記に「スタートアップフォルダ（shell:startup）はタスクマネージャーで操作してください」を出している。

    /// <summary>
    /// 全 6 ハイブを走査して、StartupApproved と突合した結果を返す。
    /// 例外は基本的に投げない（壊れたハイブ・アクセス拒否は空扱いで続行）。
    /// </summary>
    public IList<StartupEntryItem> LoadAll()
    {
        var result = new List<StartupEntryItem>();

        result.AddRange(LoadHive(RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey,     StartupHive.HKLM_Run));
        result.AddRange(LoadHive(RegistryHive.LocalMachine, RegistryView.Registry64, RunOnceSubKey, StartupHive.HKLM_RunOnce));
        result.AddRange(LoadHive(RegistryHive.CurrentUser,  RegistryView.Default,    RunSubKey,     StartupHive.HKCU_Run));
        result.AddRange(LoadHive(RegistryHive.CurrentUser,  RegistryView.Default,    RunOnceSubKey, StartupHive.HKCU_RunOnce));

        // WOW6432Node は 64bit OS のみ存在（32bit OS では例外回避）
        if (Environment.Is64BitOperatingSystem)
        {
            result.AddRange(LoadHive(RegistryHive.LocalMachine, RegistryView.Registry32, Wow64RunSubKey,     StartupHive.HKLM_Wow64_Run));
            result.AddRange(LoadHive(RegistryHive.LocalMachine, RegistryView.Registry32, Wow64RunOnceSubKey, StartupHive.HKLM_Wow64_RunOnce));
        }

        // StartupApproved と突合（IsEnabled / StartupApprovedTrailing を埋める）
        MergeApprovedState(result);

        return result;
    }

    /// <summary>1 つのハイブを走査してエントリを生成。</summary>
    private static IEnumerable<StartupEntryItem> LoadHive(
        RegistryHive hive, RegistryView view, string subKeyPath, StartupHive hiveEnum)
    {
        var items = new List<StartupEntryItem>();
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (key == null) return items;

            foreach (var rawName in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(rawName)) continue;

                // DoNotExpandEnvironmentNames で REG_EXPAND_SZ の %ProgramFiles% 等を展開せず取得（書戻し用）
                var value = key.GetValue(rawName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                if (value == null) continue;

                var kind = key.GetValueKind(rawName);
                var commandText = value.ToString() ?? string.Empty;
                var displayName = StripRunOncePrefix(rawName);

                items.Add(new StartupEntryItem
                {
                    Hive = hiveEnum,
                    RawName = rawName,
                    Name = displayName,
                    Command = commandText,
                    ValueKind = kind,
                    IsEnabled = null,            // StartupApproved 突合は後段
                    StartupApprovedTrailing = null
                });
            }
        }
        catch
        {
            // ハイブが壊れている／アクセス拒否：空扱いで続行（設計書 9 リスク表）
        }

        return items;
    }

    /// <summary>
    /// RunOnce のプレフィックス `!`（失敗時も削除）・`*`（セーフモードでも実行）を除いた名前を返す。
    /// 設計書 3-3 エッジ#1。
    /// </summary>
    private static string StripRunOncePrefix(string rawName)
    {
        var name = rawName;
        while (name.Length > 0 && (name[0] == '!' || name[0] == '*'))
        {
            name = name.Substring(1);
        }
        return name;
    }

    /// <summary>
    /// StartupApproved の値を読んで、対応する Entry の IsEnabled / StartupApprovedTrailing を埋める。
    /// 突合キーは「RawName」（プレフィックス込）。
    /// </summary>
    private static void MergeApprovedState(IList<StartupEntryItem> entries)
    {
        var hklmApprovedRun   = ReadApprovedValues(RegistryHive.LocalMachine, RegistryView.Registry64, ApprovedRun);
        var hklmApprovedRun32 = ReadApprovedValues(RegistryHive.LocalMachine, RegistryView.Registry64, ApprovedRun32);
        var hkcuApprovedRun     = ReadApprovedValues(RegistryHive.CurrentUser, RegistryView.Default, ApprovedRun);
        var hkcuApprovedRunOnce = ReadApprovedValues(RegistryHive.CurrentUser, RegistryView.Default, ApprovedRunOnce);

        foreach (var e in entries)
        {
            // ハイブ → StartupApproved 辞書の対応関係
            Dictionary<string, byte[]>? source = e.Hive switch
            {
                StartupHive.HKLM_Run           => hklmApprovedRun,
                StartupHive.HKLM_RunOnce       => hklmApprovedRun,    // HKLM 側 RunOnce も Run と同居
                StartupHive.HKLM_Wow64_Run     => hklmApprovedRun32,
                StartupHive.HKLM_Wow64_RunOnce => hklmApprovedRun32,
                StartupHive.HKCU_Run           => hkcuApprovedRun,
                StartupHive.HKCU_RunOnce       => hkcuApprovedRunOnce,
                _ => null
            };

            if (source != null && source.TryGetValue(e.RawName, out var raw))
            {
                var (enabled, trailing) = StartupApprovedCodec.Parse(raw);
                e.IsEnabled = enabled;
                e.StartupApprovedTrailing = trailing;
            }
            else
            {
                // StartupApproved に登録なし = 有効扱い（Windows デフォルト挙動）
                e.IsEnabled = true;
                e.StartupApprovedTrailing = null;
            }
        }
    }

    /// <summary>
    /// 指定の StartupApproved キーから「値名 → 12 byte」の辞書を作る。
    /// キーが無ければ空辞書（StartupFolder は読まない＝ここに来ない）。
    /// </summary>
    private static Dictionary<string, byte[]> ReadApprovedValues(RegistryHive hive, RegistryView view, string subKeyPath)
    {
        var dict = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(subKeyPath, writable: false);
            if (key == null) return dict;

            foreach (var name in key.GetValueNames())
            {
                if (string.IsNullOrEmpty(name)) continue;
                if (key.GetValue(name) is byte[] bytes)
                {
                    dict[name] = bytes;
                }
            }
        }
        catch
        {
            // アクセス拒否等：空のまま返す
        }
        return dict;
    }

    // ─── R-2 以降で実装（R-1 では空メソッド） ───

    /// <summary>1 エントリを書込（Phase R-2 で実装）。</summary>
    public void Save(StartupEntryItem entry)
    {
        throw new NotImplementedException("Phase R-2 で実装予定");
    }

    /// <summary>Run と StartupApproved 両方から削除（Phase R-3 で実装）。</summary>
    public void Delete(StartupEntryItem entry)
    {
        throw new NotImplementedException("Phase R-3 で実装予定");
    }

    /// <summary>StartupApproved の有効/無効を書込（Phase R-2 で実装）。</summary>
    public void SetEnabled(StartupEntryItem entry, bool enabled)
    {
        throw new NotImplementedException("Phase R-2 で実装予定");
    }
}
