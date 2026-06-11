using System;
using System.Diagnostics;
using System.Text.Json;

namespace WinKnobs.Services;

/// <summary>
/// v0.2.1 Step5：Hyper-V（ハイパーバイザー起動・hypervisorlaunchtype）の有効/無効（🔴Red）。
/// PowerShell 経由で bcdedit を呼ぶ（**要管理者権限・再起動で確定**）。
///
/// SysmainService 同型のプロセス実行（60秒タイムアウト＋Kill(entireProcessTree)）＋以下の差分：
///   - 適用前に現在値を first-touch 退避（backup\maintenance\originals.json・RemoteDesktopService 同型）
///   - 「元に戻す」＝退避値の書き戻し（未設定だった場合は /deletevalue で要素ごと削除）
///   - no-op 冪等：現在値が目標と同じなら書き込まない（退避も作らない）
///
/// ⚠️ コア分離（VBS）が ON のままだと off にしても再起動後もハイパーバイザーが起動し続ける
///   （効果が出ない・設計ドラフト 🔧反映2）。依存ガイド・文言は呼び出し側（ダイアログ/結果表示）の責務。
/// 文字列の組み立て・パース・分類は <see cref="HyperVHelper"/>（純粋関数・xUnit 済）に委譲。
/// </summary>
public class HyperVService
{
    private const int TimeoutMs = 60_000;

    /// <summary>退避ストア（backup\maintenance\originals.json）内の機能キー。</summary>
    public const string FeatureKey = "hyperV";

    private readonly BackupService _backup;

    public HyperVService(BackupService backup)
    {
        _backup = backup;
    }

    /// <summary>
    /// 現在の hypervisorlaunchtype を読む（非昇格でも bcdedit /enum は通る環境が多いが、
    /// 失敗時は Unknown を返す＝呼び出し側はラベル「取得できませんでした」）。
    /// </summary>
    public (HyperVLaunchType Type, string? Raw) ReadLaunchType()
    {
        var exit = RunPowerShell(HyperVHelper.BuildReadCommand(), out var output);
        if (exit != 0) return (HyperVLaunchType.Unknown, null);
        return HyperVHelper.TryParseLaunchType(output);
    }

    /// <summary>
    /// hypervisorlaunchtype を auto（有効）/ off（無効）に設定する（要管理者・再起動で確定）。
    /// 適用前に first-touch 退避。退避保存に失敗したら安全側で中止（RemoteDesktopService.Enable 同型）。
    /// </summary>
    public HyperVOutcome SetLaunchType(bool autoOn, out string? errorMessage)
    {
        errorMessage = null;

        // ⓪ 管理者前置チェック（cross-reviewer I-4）：bcdedit /enum は非昇格でも通る環境があり、
        //    書込段階まで権限不足が遅延すると出力キーワード分類の取りこぼしで Failed になる経路がある。
        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            errorMessage = "管理者権限が必要です。";
            return HyperVOutcome.AccessDenied;
        }

        // ① 現在値読取（退避ペイロード用）。読めない時は退避が作れない＝安全側で中止。
        var readExit = RunPowerShell(HyperVHelper.BuildReadCommand(), out var readOutput);
        if (readExit != 0)
        {
            errorMessage = $"現在の起動設定を読み取れなかったため、安全のため変更を中止しました：{readOutput}";
            return HyperVHelper.Classify(readExit, readOutput);
        }
        var (current, raw) = HyperVHelper.TryParseLaunchType(readOutput);

        // ①' 現在値が Auto/Off/未設定 以外（手動設定の Other 値）＝退避しても書き戻せない
        //     （復元ホワイトリスト Auto/Off 外・cross-reviewer C-1）→ 安全側で変更を中止。
        if (current == HyperVLaunchType.Other)
        {
            errorMessage = $"現在の起動設定が想定外の値（{raw}）のため、安全のため変更を中止しました。\n" +
                           "コマンドプロンプト（管理者）で bcdedit /enum {current} を実行して確認してください。";
            return HyperVOutcome.Failed;
        }

        // ② no-op 冪等：既に目標と同じなら何もしない（退避も作らない＝WSL first-touch 同流儀）
        var target = autoOn ? HyperVLaunchType.Auto : HyperVLaunchType.Off;
        if (current == target) return HyperVOutcome.Success;

        // ③ first-touch 退避（初回のみ・既に退避済みなら据え置き）
        var backup = new HyperVHelper.HyperVBackup(
            LaunchTypeExisted: current != HyperVLaunchType.NotConfigured,
            LaunchTypeValue: raw ?? string.Empty);
        try
        {
            _backup.SaveMaintenanceOriginalIfAbsent(FeatureKey, JsonSerializer.Serialize(backup));
        }
        catch (Exception ex)
        {
            errorMessage = $"退避の保存に失敗したため、安全のため変更を中止しました：{ex.Message}";
            return HyperVOutcome.Failed;
        }

        // ④ 書込（単一ステップ＝done-stack 不要。失敗時は何も変わっていない）
        var exit = RunPowerShell(HyperVHelper.BuildSetLaunchTypeCommand(autoOn), out var output);
        if (exit == 0) return HyperVOutcome.Success;

        errorMessage = output;
        return HyperVHelper.Classify(exit, output);
    }

    /// <summary>
    /// 退避 JSON から hypervisorlaunchtype を原状復帰する（要管理者・再起動で確定）。
    /// 破損 JSON・実行失敗は例外送出（呼び出し側で「退避は残す＝再試行可能」のハンドリング・RDP Restore 同型）。
    /// </summary>
    public void RestoreHyperV(string json)
    {
        // 管理者前置チェック（I-4・SetLaunchType と対称）
        if (!ElevationClassifier.IsRunningAsAdministrator())
            throw new InvalidOperationException("管理者権限が必要です。WinKnobs を「管理者として実行」で起動し直してください。");

        var b = HyperVHelper.TryParseBackup(json);
        if (b == null)
            throw new InvalidOperationException("退避情報が破損していて読めませんでした（変更は行っていません）。");

        var command = b.LaunchTypeExisted
            ? HyperVHelper.BuildSetLaunchTypeRawCommand(b.LaunchTypeValue)
            : HyperVHelper.BuildDeleteLaunchTypeCommand();

        var exit = RunPowerShell(command, out var output);
        if (exit != 0)
            throw new InvalidOperationException($"起動設定の書き戻しに失敗しました：{output}");
    }

    /// <summary>
    /// PowerShell 1コマンド実行（SysmainService 同型・60秒タイムアウト・Kill(entireProcessTree)）。
    /// 戻り値は ExitCode（起動失敗/タイムアウトは -1）。combined は stderr 優先の結合出力。
    /// </summary>
    private static int RunPowerShell(string command, out string combined)
    {
        combined = string.Empty;
        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command",
                command
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

            // 小出力前提（/enum は数十行・/set /deletevalue は1行）。大量出力はパイプデッドロック注意。
            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                combined = "bcdedit の実行がタイムアウトしました（60秒）。コマンドプロンプト（管理者）で `bcdedit /enum {current}` を実行し、hypervisorlaunchtype の状態を確認してください。";
                return -1;
            }

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            return p.ExitCode;
        }
        catch (Exception ex)
        {
            combined = ex.Message;
            return -1;
        }
    }
}
