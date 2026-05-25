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

                var item = new StartupEntryItem
                {
                    Hive = hiveEnum,
                    RawName = rawName,
                    Name = displayName,
                    Command = commandText,
                    ValueKind = kind,
                    IsEnabled = null,            // StartupApproved 突合は後段
                    StartupApprovedTrailing = null
                };
                // R-2 cross-reviewer 致命15 対応：Load 直後に Original を確定
                item.SnapshotOriginal();
                items.Add(item);
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
        // R-2 plan/cross 致命1 対応：Wow64 側の StartupApproved は Registry32 ビュー + ApprovedRun で統一。
        // R-2 plan 致命2 対応：HKLM_RunOnce / HKLM_Wow64_RunOnce / HKCU_RunOnce は StartupApproved 連動なし（CanToggleEnabled=false）。
        // 読込時は IsEnabled=true（有効扱い）のまま StartupApproved 辞書突合をスキップする。
        var hklmApprovedRun       = ReadApprovedValues(RegistryHive.LocalMachine, RegistryView.Registry64, ApprovedRun);
        var hklmApprovedRunWow64  = ReadApprovedValues(RegistryHive.LocalMachine, RegistryView.Registry32, ApprovedRun);
        var hkcuApprovedRun       = ReadApprovedValues(RegistryHive.CurrentUser,  RegistryView.Default,    ApprovedRun);

        foreach (var e in entries)
        {
            // ハイブ → StartupApproved 辞書の対応関係（RunOnce 系は null → IsEnabled=true 扱い）
            Dictionary<string, byte[]>? source = e.Hive switch
            {
                StartupHive.HKLM_Run       => hklmApprovedRun,
                StartupHive.HKLM_Wow64_Run => hklmApprovedRunWow64,
                StartupHive.HKCU_Run       => hkcuApprovedRun,
                // RunOnce 系：StartupApproved 連動なし（タスクマネージャー実装と整合）
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

    // ─── R-2 実装：SetEnabled / Save ───

    /// <summary>
    /// StartupApproved の有効/無効を書込（Phase R-2 実装）。
    /// 既存値があれば byte[1..11]（タイムスタンプ等・MS未公開）を保持し、byte[0] のみ書換。
    /// 既存値がなければ Codec.Build(enabled, null) で新規 12 byte を生成。
    /// HKLM 系は管理者権限必須（呼び元で UnauthorizedAccessException 等を捕捉してフォールバック）。
    /// 設計書 3-2 / 6-1。
    ///
    /// R-2 plan-validator 致命2 対応：RunOnce 系は StartupApproved 連動非対応のため NotSupportedException。
    /// （タスクマネージャーが RunOnce を「スタートアップ」タブに表示しない実装と整合）
    /// </summary>
    public void SetEnabled(StartupEntryItem entry, bool enabled)
    {
        if (entry.Hive is StartupHive.HKLM_RunOnce
                       or StartupHive.HKCU_RunOnce
                       or StartupHive.HKLM_Wow64_RunOnce)
        {
            throw new NotSupportedException(
                $"RunOnce 系（{entry.HiveDisplay}）は StartupApproved 連動非対応です。" +
                "次回起動後に自動削除される仕様のため、有効/無効トグルは無効化されています。");
        }

        var (hive, view, approvedSubKey) = GetApprovedLocation(entry.Hive);
        var bytes = StartupApprovedCodec.Build(enabled, entry.StartupApprovedTrailing);

        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        // CreateSubKey：キーが無くても作成（StartupApproved\Run はユーザーが空のままの場合あり）
        using var key = baseKey.CreateSubKey(approvedSubKey, writable: true)
            ?? throw new InvalidOperationException($"StartupApproved キーを開けません：{approvedSubKey}");

        key.SetValue(entry.RawName, bytes, RegistryValueKind.Binary);
    }

    /// <summary>
    /// 1 エントリを Run キー側に書込（Phase R-2 実装）。
    /// R-2 では Command + ValueKind のみ編集対象（Name 変更は R-3 で削除→追加として実装）。
    ///
    /// 楽観ロック（plan-validator エッジ#3）：
    ///   forceOverwrite=false なら Save 直前にレジストリを再読込し、メモリ上 entry.Command と差分検証。
    ///   差分あれば SaveResult.ConflictDetected=true で返し、書込はしない。
    ///
    /// REG_EXPAND_SZ 厳守（plan-validator エッジ#4）：
    ///   entry.ValueKind を SetValue 第 4 引数に渡し、%ProgramFiles% 等の環境変数を維持。
    ///
    /// 設計書 3-3 / 6-1。
    /// </summary>
    public SaveResult Save(StartupEntryItem entry, bool forceOverwrite = false)
    {
        if (!forceOverwrite)
        {
            var (currentCommand, _) = ReadCurrentValue(entry);
            if (currentCommand != null && currentCommand != entry.Command)
            {
                return new SaveResult
                {
                    Success = false,
                    ConflictDetected = true,
                    CurrentCommandOnConflict = currentCommand
                };
            }
        }

        var (hive, view, runSubKey) = GetRunLocation(entry.Hive);
        using var baseKey = RegistryKey.OpenBaseKey(hive, view);
        using var key = baseKey.OpenSubKey(runSubKey, writable: true)
            ?? throw new InvalidOperationException($"Run キーを開けません：{runSubKey}");

        key.SetValue(entry.RawName, entry.Command, entry.ValueKind);
        return new SaveResult { Success = true };
    }

    /// <summary>Run と StartupApproved 両方から削除（Phase R-3 で実装）。</summary>
    public void Delete(StartupEntryItem entry)
    {
        throw new NotImplementedException("Phase R-3 で実装予定");
    }

    // ─── R-2 内部ヘルパー ───

    /// <summary>
    /// StartupApproved の書込先（HKLM/HKCU + キーパス）を返す。
    /// R-2 plan/cross 致命1 対応：HKLM_Wow64_Run は Registry32 ビュー + ApprovedRun（OS が WOW6432Node にリダイレクト）。
    /// RunOnce 系は SetEnabled の冒頭で NotSupportedException として弾かれる前提（ここに来たら設計ミス）。
    /// </summary>
    private static (RegistryHive hive, RegistryView view, string subKey) GetApprovedLocation(StartupHive entryHive)
    {
        return entryHive switch
        {
            StartupHive.HKLM_Run       => (RegistryHive.LocalMachine, RegistryView.Registry64, ApprovedRun),
            // Wow64 側：Run キー本体（GetRunLocation）と View を揃え Registry32 で開く。
            // タスクマネージャー実装も Wow64 側 StartupApproved は WOW6432Node 配下を見るため整合。
            StartupHive.HKLM_Wow64_Run => (RegistryHive.LocalMachine, RegistryView.Registry32, ApprovedRun),
            StartupHive.HKCU_Run       => (RegistryHive.CurrentUser,  RegistryView.Default,    ApprovedRun),
            // RunOnce 系は呼ばれない（SetEnabled で NotSupportedException）
            _ => throw new InvalidOperationException(
                $"StartupApproved 連動非対応のハイブです：{entryHive}（RunOnce 系は CanToggleEnabled=false で UI からブロック済）")
        };
    }

    /// <summary>Run キー側の書込先（HKLM/HKCU + キーパス + View）を返す。</summary>
    private static (RegistryHive hive, RegistryView view, string subKey) GetRunLocation(StartupHive entryHive)
    {
        return entryHive switch
        {
            StartupHive.HKLM_Run           => (RegistryHive.LocalMachine, RegistryView.Registry64, RunSubKey),
            StartupHive.HKLM_RunOnce       => (RegistryHive.LocalMachine, RegistryView.Registry64, RunOnceSubKey),
            StartupHive.HKLM_Wow64_Run     => (RegistryHive.LocalMachine, RegistryView.Registry32, Wow64RunSubKey),
            StartupHive.HKLM_Wow64_RunOnce => (RegistryHive.LocalMachine, RegistryView.Registry32, Wow64RunOnceSubKey),
            StartupHive.HKCU_Run           => (RegistryHive.CurrentUser,  RegistryView.Default,    RunSubKey),
            StartupHive.HKCU_RunOnce       => (RegistryHive.CurrentUser,  RegistryView.Default,    RunOnceSubKey),
            _ => throw new ArgumentOutOfRangeException(nameof(entryHive))
        };
    }

    /// <summary>Run キーから現在の値を読む（楽観ロック用）。</summary>
    private static (string? command, RegistryValueKind? kind) ReadCurrentValue(StartupEntryItem entry)
    {
        var (hive, view, runSubKey) = GetRunLocation(entry.Hive);
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            using var key = baseKey.OpenSubKey(runSubKey, writable: false);
            if (key == null) return (null, null);

            var value = key.GetValue(entry.RawName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null) return (null, null);

            return (value.ToString(), key.GetValueKind(entry.RawName));
        }
        catch
        {
            return (null, null);
        }
    }
}

/// <summary>
/// Save の結果を返す（楽観ロック対応）。
/// 設計書 3-3 エッジ#3。
/// </summary>
public class SaveResult
{
    /// <summary>書込成功。</summary>
    public bool Success { get; init; }

    /// <summary>並行編集を検出したため書込を中止した。</summary>
    public bool ConflictDetected { get; init; }

    /// <summary>Conflict 検出時、外部で書き換えられた現在の Command 値（ユーザーへの提示用）。</summary>
    public string? CurrentCommandOnConflict { get; init; }
}
