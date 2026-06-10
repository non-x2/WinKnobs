using System.Diagnostics;

namespace WinKnobs.Services;

/// <summary>
/// v0.2.0 Sysmain（旧 Superfetch）サービスを有効/無効にするサービス。
/// PowerShell の Set-Service / Stop-Service / Start-Service を内部で呼ぶ（**要管理者権限**）。
/// MemoryCompressionService と同型（PowerShell 起動・60秒タイムアウト・ExitCode＋出力で結果分類）。
///
/// 文字列の組み立て・分類は <see cref="SysmainHelper"/>（純粋関数・xUnit 済）に委譲。
/// 本サービスのプロセス実行部分は VM 実機検証の領域（管理者操作）。
///
/// タイムアウト時：powershell.exe は Kill するが、サービス自体の強制停止はしない
/// （Stop-Service 60秒超を無理に殺すと中途半端なサービス状態が残るため・案内は「再起動で確定」）。
/// </summary>
public class SysmainService
{
    /// <summary>
    /// Sysmain サービスを切り替える（要管理者権限）。
    /// </summary>
    /// <param name="enable">true=有効化（Automatic＋Start） / false=無効化（Stop＋Disabled）</param>
    /// <param name="errorMessage">失敗時のエラーメッセージ（成功時は null）</param>
    /// <returns>結果（Success / AccessDenied / ServiceNotFound / Failed）</returns>
    public SysmainOutcome SetSysmain(bool enable, out string? errorMessage)
    {
        errorMessage = null;
        var command = SysmainHelper.BuildCommand(enable);

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
                return SysmainOutcome.Failed;
            }

            // 小出力（Set-Service/Stop-Service はメッセージ少）のため WaitForExit 後の読み取りで十分。
            // 注意：大量出力するコマンドでは stdout/stderr パイプバッファ充填でデッドロックしうる（本サービスは小出力前提）。
            if (!p.WaitForExit(60_000))
            {
                // powershell.exe のリークは防ぐが、サービスの強制 Kill はしない（中途半端な状態を残すより OS/再起動に任せる）。
                try { p.Kill(entireProcessTree: true); } catch { /* 既に終了済み等は無視（best-effort） */ }
                errorMessage = "Sysmain の切り替えがタイムアウトしました（60秒）。途中まで進んだ可能性があります。現在の状態はサービス画面（services.msc）で確認するか、PC を再起動して確定させてください。";
                return SysmainOutcome.Failed;
            }

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            if (p.ExitCode != 0)
                errorMessage = combined;

            return SysmainHelper.Classify(p.ExitCode, combined);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return SysmainOutcome.Failed;
        }
    }
}
