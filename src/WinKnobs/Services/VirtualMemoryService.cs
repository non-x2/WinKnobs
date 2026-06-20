using System;
using System.Diagnostics;
using System.Text;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.0 仮想メモリ（ページファイル）の実行サービス（🔴Red・要管理者・**要再起動**）。
/// PowerShell（Set-CimInstance Win32_ComputerSystem ＋ レジストリ PagingFiles）を子プロセスで呼ぶ。
/// 文字列の組み立て・パース・分類・検証は <see cref="VirtualMemoryHelper"/>（純粋関数・xUnit）に委譲。
///
/// NicPowerService 同型（-EncodedCommand・stderr 非同期・タイムアウト・first-touch 退避）。
/// 退避は「WinKnobs で初めて触る前」の AutomaticManaged ＋ PagingFiles 全文（maintenance ストア・1 件）。
/// </summary>
public class VirtualMemoryService
{
    private const int TimeoutMs = 60_000;

    /// <summary>maintenance 退避ストアの機能キー。</summary>
    public const string FeatureKey = "virtualMemory";

    private readonly BackupService _backup;

    public VirtualMemoryService(BackupService backup)
    {
        _backup = backup;
    }

    /// <summary>退避（初めて触る前の状態）があるか＝「元に戻す」メニューの活性判定。</summary>
    public bool HasBackup() => _backup.HasMaintenanceOriginal(FeatureKey);

    /// <summary>現在状態を読む（読取のみ・非昇格でも概ね通る）。失敗・パース不能は null。</summary>
    public VirtualMemoryState? ReadState()
    {
        var exit = RunPowerShell(VirtualMemoryHelper.BuildReadCommand(), out var output);
        if (exit != 0) return null;
        return VirtualMemoryHelper.ParseState(output);
    }

    /// <summary>自動管理（Windows に任せる）に設定（推奨・安全）。要管理者・再起動で反映。</summary>
    public VmOutcome ApplyAutoManaged(out string? message)
        => Apply(VmApplyMode.AutoManaged, 0, 0, out message);

    /// <summary>システム管理サイズに設定（自動管理 OFF＋ C: を 0 0）。要管理者・再起動で反映。</summary>
    public VmOutcome ApplySystemManaged(out string? message)
        => Apply(VmApplyMode.SystemManaged, 0, 0, out message);

    /// <summary>手動サイズに設定（C: 初期/最大 MB）。検証は呼び出し側で済ませる前提。要管理者・再起動で反映。</summary>
    public VmOutcome ApplyManual(int initialMb, int maxMb, out string? message)
        => Apply(VmApplyMode.Manual, initialMb, maxMb, out message);

    private VmOutcome Apply(VmApplyMode mode, int initialMb, int maxMb, out string? message)
    {
        message = null;

        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            message = "管理者権限が必要です。";
            return VmOutcome.AccessDenied;
        }

        // ① 現在状態を読む（退避ペイロード）。読めなければ復元不能になるため中止。
        var state = ReadState();
        if (state == null)
        {
            message = "現在の仮想メモリ設定を読み取れなかったため、安全のため中止しました。";
            return VmOutcome.Failed;
        }

        // ② first-touch 退避（初回のみ）。失敗＝復元不能になるため中止。
        try
        {
            _backup.SaveMaintenanceOriginalIfAbsent(FeatureKey, VirtualMemoryHelper.SerializeBackup(state));
        }
        catch (Exception ex)
        {
            message = $"退避の保存に失敗したため、安全のため中止しました：{ex.Message}";
            return VmOutcome.Failed;
        }

        // ③ 適用
        var exit = RunPowerShell(VirtualMemoryHelper.BuildApplyCommand(mode, initialMb, maxMb), out var output);
        var outcome = VirtualMemoryHelper.Classify(exit, output);

        message = outcome switch
        {
            VmOutcome.Success => "仮想メモリ設定を変更しました。⚠️ 反映には再起動が必要です。",
            VmOutcome.AccessDenied => "管理者権限が必要です。",
            _ => "仮想メモリ設定の変更に失敗しました。\n" + Tail(output),
        };
        return outcome;
    }

    /// <summary>退避から原状復帰（「元に戻す」・要管理者・再起動で反映）。成功なら退避を削除。</summary>
    public VmOutcome Restore(out string? message)
    {
        message = null;

        if (!ElevationClassifier.IsRunningAsAdministrator())
        {
            message = "管理者権限が必要です。";
            return VmOutcome.AccessDenied;
        }

        if (!_backup.LoadMaintenanceOriginals().TryGetValue(FeatureKey, out var json))
        {
            message = "戻せる変更がありません。";
            return VmOutcome.Success;
        }

        var backup = VirtualMemoryHelper.ParseBackup(json);
        if (backup == null)
        {
            // E2：退避破損 → これ以上壊さない。純正へ誘導。
            message = "退避情報が破損していて読めませんでした。Windows 純正の仮想メモリ設定から戻してください（変更は行っていません）。";
            return VmOutcome.Failed;
        }

        var exit = RunPowerShell(VirtualMemoryHelper.BuildRestoreCommand(backup), out var output);
        var outcome = VirtualMemoryHelper.Classify(exit, output);

        if (outcome == VmOutcome.Success)
        {
            _backup.RemoveMaintenanceOriginal(FeatureKey);
            message = "適用前の状態に戻しました。⚠️ 反映には再起動が必要です。";
        }
        else
        {
            message = "元に戻す処理に失敗しました。退避は残してあります。\n" + Tail(output);
        }
        return outcome;
    }

    private static string Tail(string? output)
    {
        if (string.IsNullOrWhiteSpace(output)) return string.Empty;
        var s = output.Trim();
        return s.Length <= 300 ? s : "…" + s.Substring(s.Length - 300);
    }

    /// <summary>PowerShell を -EncodedCommand で実行（NicPowerService 同型・タイムアウト 60 秒）。</summary>
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

            var stderr = new StringBuilder();
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };
            p.BeginErrorReadLine();
            var stdout = p.StandardOutput.ReadToEnd();

            if (!p.WaitForExit(TimeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                combined = "仮想メモリ設定の変更がタイムアウトしました（60秒）。";
                return -1;
            }
            p.WaitForExit();

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
