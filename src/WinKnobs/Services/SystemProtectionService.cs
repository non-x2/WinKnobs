using System.Diagnostics;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.15 システムの保護（システムの復元）を有効/無効にするサービス（Phase 2）。
/// PowerShell の Enable-ComputerRestore / Disable-ComputerRestore を内部で呼ぶ（**要管理者権限**）。
/// RestorePointService と同型（PowerShell 起動・60秒タイムアウト・ExitCode＋出力で結果分類）。
///
/// 文字列の組み立て・分類は <see cref="SystemProtectionHelper"/>（純粋関数・xUnit 済）に委譲。
/// 本サービスのプロセス実行部分は VM 実機検証の領域（管理者操作）。
/// </summary>
public class SystemProtectionService
{
    /// <summary>
    /// システムの保護を切り替える（要管理者権限）。
    /// </summary>
    /// <param name="enable">true=有効化 / false=無効化</param>
    /// <param name="errorMessage">失敗時のエラーメッセージ（成功時は null）</param>
    /// <returns>結果（Success / AccessDenied / Failed）</returns>
    public SystemProtectionOutcome SetProtection(bool enable, out string? errorMessage)
    {
        errorMessage = null;
        var command = SystemProtectionHelper.BuildCommand(enable, SystemProtectionHelper.DefaultDrive);

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
                errorMessage = "powershell.exe を起動できませんでした。";
                return SystemProtectionOutcome.Failed;
            }

            // 小出力（ComputerRestore 系はメッセージ少）のため WaitForExit 後の読み取りで十分。
            // 注意：大量出力するコマンドでは stdout/stderr パイプバッファ充填でデッドロックしうる（本サービスは小出力前提）。
            if (!p.WaitForExit(60_000))
            {
                // タイムアウト時は powershell.exe を確実に終了させる（プロセスリーク防止・cross-reviewer 軽微③）。
                try { p.Kill(entireProcessTree: true); } catch { /* 既に終了済み等は無視（best-effort） */ }
                errorMessage = "システムの保護の切り替えがタイムアウトしました（60秒）。";
                return SystemProtectionOutcome.Failed;
            }

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            if (p.ExitCode != 0)
                errorMessage = combined;

            return SystemProtectionHelper.Classify(p.ExitCode, combined);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return SystemProtectionOutcome.Failed;
        }
    }
}
