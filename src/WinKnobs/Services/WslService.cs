using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace WinKnobs.Services;

/// <summary>
/// v0.2.1 Step3：WSL 2 機能（Microsoft-Windows-Subsystem-Linux ＋ VirtualMachinePlatform）の有効/無効。
/// PowerShell の Enable/Disable-WindowsOptionalFeature を内部で呼ぶ（**要管理者権限・再起動で確定**）。
///
/// SysmainService 同型のプロセス実行＋以下の差分：
///   - タイムアウト 300 秒（DISM 系は遅い・設計ドラフト §2-7）
///   - 有効化は2機能の C# done-stack（片方失敗で逆順ロールバック・RemoteDesktopService.Enable 同型・
///     ロールバックも失敗したら PartiallyEnabled＝中途半端な状態を正直に返す）
///   - 無効化は WSL 本体のみ（VirtualMachinePlatform は WSA 等の巻き添え防止で残す）
///   - first-touch：既に有効な機能は touch しない（ロールバック対象にも入れない）＝no-op 冪等
///
/// 文字列の組み立て・分類は <see cref="WslHelper"/>（純粋関数・xUnit 済）に委譲。
/// ディストリビューションのデータ（ext4.vhdx）は機能無効化では削除されない（再有効化で戻る）。
/// </summary>
public class WslService
{
    private const int TimeoutMs = 300_000;

    /// <summary>
    /// WSL（2機能）を有効化する（要管理者権限・再起動で確定）。
    /// </summary>
    public WslOutcome Enable(out string? errorMessage)
    {
        errorMessage = null;

        // 管理者前置チェック（cross-reviewer I-4）：Get-WindowsOptionalFeature の読取は非昇格でも
        // 通る環境があり、Enable 段階まで権限不足が遅延すると分類取りこぼしの経路がある。
        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            errorMessage = "管理者権限が必要です。";
            return WslOutcome.AccessDenied;
        }

        // 有効化順：実行基盤（VirtualMachinePlatform）→ WSL 本体。
        // ロールバックは逆順（後に有効化したものから無効化）＝done-stack。
        var features = new[] { WslHelper.VmPlatformFeatureName, WslHelper.WslFeatureName };
        var done = new Stack<string>();

        foreach (var feature in features)
        {
            // first-touch：既に有効（または再起動待ち）なら触らない＝no-op 冪等・ロールバック対象外
            var state = GetFeatureState(feature, out var stateErr);
            if (state == WslFeatureState.Enabled || state == WslFeatureState.Pending) continue;
            if (state == WslFeatureState.Unknown && stateErr != null)
            {
                // 状態が読めない（権限不足/機能不在等）。書き込み前に分類して中止＝安全側。
                errorMessage = stateErr.Value.Output;
                return WslHelper.Classify(stateErr.Value.ExitCode, stateErr.Value.Output);
            }

            var exit = RunPowerShell(WslHelper.BuildEnableCommand(feature), out var output);
            if (exit == 0)
            {
                done.Push(feature);
                continue;
            }

            // 失敗 → ここまで有効化した分を逆順ロールバック
            var outcome = WslHelper.Classify(exit, output);
            errorMessage = output;
            while (done.Count > 0)
            {
                var rollback = done.Pop();
                var rbExit = RunPowerShell(WslHelper.BuildDisableCommand(rollback), out var rbOutput);
                if (rbExit != 0)
                {
                    // ロールバックも失敗＝片方だけ有効の中途半端な状態（正直に返す・設計ドラフト 🔧反映6）
                    errorMessage = $"{output}\n（巻き戻しにも失敗：{rbOutput}）";
                    return WslOutcome.PartiallyEnabled;
                }
            }
            return outcome;
        }

        return WslOutcome.Success;
    }

    /// <summary>
    /// WSL 本体のみ無効化する（要管理者権限・再起動で確定）。
    /// VirtualMachinePlatform は残す（WSA / Windows サンドボックス等の巻き添え防止）。
    /// </summary>
    public WslOutcome Disable(out string? errorMessage)
    {
        errorMessage = null;

        // 管理者前置チェック（I-4・Enable と対称）
        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            errorMessage = "管理者権限が必要です。";
            return WslOutcome.AccessDenied;
        }

        var state = GetFeatureState(WslHelper.WslFeatureName, out var stateErr);
        if (state == WslFeatureState.Disabled) return WslOutcome.Success;   // no-op 冪等
        if (state == WslFeatureState.Unknown && stateErr != null)
        {
            errorMessage = stateErr.Value.Output;
            return WslHelper.Classify(stateErr.Value.ExitCode, stateErr.Value.Output);
        }

        var exit = RunPowerShell(WslHelper.BuildDisableCommand(WslHelper.WslFeatureName), out var output);
        if (exit == 0) return WslOutcome.Success;

        errorMessage = output;
        return WslHelper.Classify(exit, output);
    }

    /// <summary>機能の現在状態を取得（読めない時は err に ExitCode＋出力を返す）。</summary>
    private WslFeatureState GetFeatureState(string featureName, out (int ExitCode, string Output)? err)
    {
        err = null;
        var exit = RunPowerShell(WslHelper.BuildGetStateCommand(featureName), out var output);
        if (exit != 0)
        {
            err = (exit, output);
            return WslFeatureState.Unknown;
        }
        return WslHelper.ParseFeatureState(output);
    }

    /// <summary>
    /// PowerShell 1コマンド実行（SysmainService 同型・タイムアウト 300 秒）。
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

            // 小出力前提（Enable/Disable は Out-Null 済・State は1語）。大量出力はパイプデッドロック注意。
            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                combined = "WSL 機能の変更がタイムアウトしました（300秒）。途中まで進んだ可能性があります。「Windows の機能の有効化または無効化」（OptionalFeatures.exe）で状態を確認するか、PC を再起動して確定させてください。";
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
