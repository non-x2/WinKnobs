using System;
using System.Diagnostics;
using System.Text;

namespace WinKnobs.Services;

/// <summary>
/// v0.2.2 Step2：NIC省電力プリセットの実行サービス（要管理者・即時反映＝適用時に数秒の通信断）。
/// PowerShell（Set-NetAdapterAdvancedProperty / Set-NetAdapterPowerManagement）を子プロセスで呼ぶ。
///
/// HyperVService / WslService 同型の差分：
///   - タイムアウト 120 秒（NIC 再初期化が遅い個体差を考慮・設計ドラフト §2-2）
///   - コマンドは **-EncodedCommand（Base64 UTF-16LE）** で渡す（🔧反映4：日本語アダプタ名・
///     コンソールコードページ CP932 依存の文字化けを構造的に排除）
///   - アダプタは GUID で再解決（適用直前 TOCTOU・🔧反映エッジ1）。退避は GUID 単位 first-touch。
///   - 自動ロールバックはしない（🔧反映エッジ4：NIC は値単位ゆえ誤発火の二次破壊を避け、
///     失敗時は「元に戻す」ボタンへ誘導する＝呼び出し側の責務）。
///
/// 文字列の組み立て・パース・分類は <see cref="NicPowerHelper"/>（純粋関数・xUnit 済）に委譲。
/// </summary>
public class NicPowerService
{
    private const int TimeoutMs = 120_000;

    private readonly BackupService _backup;

    public NicPowerService(BackupService backup)
    {
        _backup = backup;
    }

    /// <summary>退避ストア（backup\maintenance\originals.json）の機能キー（GUID 単位・🔧反映4）。</summary>
    public static string FeatureKeyFor(string interfaceGuid) => "nicPower_" + interfaceGuid;

    /// <summary>このアダプタに退避（適用済み＝復元可能な状態）があるか。</summary>
    public bool HasBackup(string interfaceGuid)
        => _backup.HasMaintenanceOriginal(FeatureKeyFor(interfaceGuid));

    /// <summary>
    /// 現在の省電力スナップショットを読む（読取のみ・安全・非昇格でも概ね通る）。
    /// 失敗・パース不能は null（呼び出し側はラベル「取得できませんでした」）。
    /// </summary>
    public NicPowerHelper.NicPowerSnapshot? ReadSnapshot(string interfaceGuid)
    {
        var exit = RunPowerShell(NicPowerHelper.BuildReadCommand(interfaceGuid), out var output);
        if (exit != 0) return null;
        return NicPowerHelper.TryParseSnapshot(output);
    }

    /// <summary>
    /// 省電力 OFF プリセットを適用する（要管理者・即時反映＝数秒の通信断）。
    /// 適用前に GUID 単位 first-touch 退避。退避保存に失敗したら安全側で中止。
    /// 「変える項目が無い（全て既に 0・電源管理も Disabled）」なら no-op で Success。
    /// </summary>
    public NicPowerOutcome ApplyPowerSavingOff(string interfaceGuid, out string? message)
    {
        message = null;

        // ⓪ 管理者前置チェック（cross-reviewer I-4 と同流儀）
        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            message = "管理者権限が必要です。";
            return NicPowerOutcome.AccessDenied;
        }

        // ① 現在状態を読む（退避ペイロード＋適用対象の決定素材）
        var snapshot = ReadSnapshot(interfaceGuid);
        if (snapshot == null)
        {
            message = "アダプタの現在設定を読み取れなかったため、安全のため中止しました。";
            return NicPowerOutcome.Failed;
        }

        var keywords = NicPowerHelper.SelectKeywordsToApply(snapshot);
        var lockPm = snapshot.AllowComputerToTurnOffDevice == "Enabled";

        // ② no-op：変える項目が無い（既に省電力 OFF 済み）
        if (keywords.Count == 0 && !lockPm)
        {
            message = "このアダプタは既に省電力が OFF（または該当設定なし）です。変更はありません。";
            return NicPowerOutcome.Success;
        }

        // ③ first-touch 退避（初回のみ・GUID 単位）。失敗＝復元不能になるため中止。
        try
        {
            _backup.SaveMaintenanceOriginalIfAbsent(
                FeatureKeyFor(interfaceGuid), NicPowerHelper.SerializeBackup(snapshot));
        }
        catch (Exception ex)
        {
            message = $"退避の保存に失敗したため、安全のため中止しました：{ex.Message}";
            return NicPowerOutcome.Failed;
        }

        // ④ 適用（GUID 再解決・個別集計）
        var exit = RunPowerShell(
            NicPowerHelper.BuildApplyCommand(interfaceGuid, keywords, lockPm), out var output);
        var outcome = NicPowerHelper.Classify(exit, output);

        message = outcome switch
        {
            NicPowerOutcome.Success => "省電力を OFF にしました（即時反映・一瞬通信が途切れた場合があります）。",
            NicPowerOutcome.PartiallyApplied =>
                "一部の項目だけ適用できました。「元に戻す」で適用前の状態に戻せます。\n" + Tail(output),
            NicPowerOutcome.AdapterNotFound =>
                "適用の直前にアダプタが見つからなくなりました（取り外し・切替の可能性）。もう一度お試しください。",
            NicPowerOutcome.AccessDenied => "管理者権限が必要です。",
            _ => "省電力 OFF の適用に失敗しました。\n" + Tail(output),
        };
        return outcome;
    }

    /// <summary>
    /// このアダプタの退避から原状を書き戻す（「元に戻す」・要管理者）。
    /// 成功（または部分でも fail=0）なら退避を削除して後始末。
    /// </summary>
    public NicPowerOutcome RestoreOriginal(string interfaceGuid, out string? message)
    {
        message = null;

        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            message = "管理者権限が必要です。";
            return NicPowerOutcome.AccessDenied;
        }

        var key = FeatureKeyFor(interfaceGuid);
        if (!_backup.LoadMaintenanceOriginals().TryGetValue(key, out var json))
        {
            message = "このアダプタに戻せる変更がありません。";
            return NicPowerOutcome.Success;
        }

        var snapshot = NicPowerHelper.TryParseBackup(json);
        if (snapshot == null)
        {
            message = "退避情報が破損していて読めませんでした（変更は行っていません）。";
            return NicPowerOutcome.Failed;
        }

        var exit = RunPowerShell(NicPowerHelper.BuildRestoreCommand(snapshot), out var output);
        var outcome = NicPowerHelper.Classify(exit, output);

        if (outcome == NicPowerOutcome.Success)
        {
            _backup.RemoveMaintenanceOriginal(key);   // 完全復元できたので後始末
            message = "適用前の状態に戻しました。";
        }
        else if (outcome == NicPowerOutcome.PartiallyApplied)
        {
            // 退避は残す＝再試行可能（一部だけ戻った）。
            message = "一部の項目だけ戻せました。退避は残してあるので、もう一度「元に戻す」を試せます。\n" + Tail(output);
        }
        else
        {
            message = "元に戻す処理に失敗しました。退避は残してあります。\n" + Tail(output);
        }
        return outcome;
    }

    /// <summary>出力の末尾だけ（エラー詳細・長すぎる出力をダイアログに貼らない）。</summary>
    private static string Tail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;
        var s = output.Trim();
        return s.Length <= 300 ? s : "…" + s.Substring(s.Length - 300);
    }

    /// <summary>
    /// PowerShell を -EncodedCommand で実行（HyperVService 同型・タイムアウト 120 秒）。
    /// Base64(UTF-16LE) で渡す＝日本語アダプタ名の CP932 化けを排除（🔧反映4）。
    /// 戻り値は ExitCode（起動失敗/タイムアウトは -1）。combined は stdout＋stderr 結合。
    /// </summary>
    private static int RunPowerShell(string command, out string combined)
    {
        combined = string.Empty;
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-EncodedCommand", encoded
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        try
        {
            using var p = Process.Start(psi);
            if (p == null)
            {
                combined = "powershell.exe を起動できませんでした。";
                return -1;
            }

            // 出力は JSON＋結果行で中量。デッドロック回避に stderr を非同期で受ける（v0.2.1 I-2 同流儀）。
            var stderr = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            var stdout = p.StandardOutput.ReadToEnd();

            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                combined = "ネットワーク設定の変更がタイムアウトしました（120秒）。デバイスマネージャーでアダプタの状態を確認してください。";
                return -1;
            }
            p.WaitForExit();   // 非同期 stderr の取りこぼし防止

            var err = stderr.ToString();
            combined = string.IsNullOrWhiteSpace(err) ? stdout : stdout + "\n" + err;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            combined = ex.Message;
            return -1;
        }
    }
}
