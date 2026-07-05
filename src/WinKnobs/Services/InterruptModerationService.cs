using System;
using System.Diagnostics;
using System.Text;

namespace WinKnobs.Services;

/// <summary>
/// v0.4.6「割り込み加減」の PowerShell 実行サービス（要管理者・全物理アダプタ一括・即時反映＝数秒断あり）。
/// NicPowerService と同じ -EncodedCommand（Base64 UTF-16LE）方式だが、対象は NDIS 標準の単一プロパティ
/// (0/1 固定) のみで GUID 単位スナップショットストアは不要＝退避は呼び出し元の SettingItem 標準
/// Backup/Restore（アプリ共通の「↩戻す」）に委譲する。タイムアウト 60 秒（単一プロパティのみで
/// NicPowerService の 120 秒より短め）。
/// </summary>
public class InterruptModerationService
{
    private const int TimeoutMs = 60_000;

    /// <summary>PowerShell を -EncodedCommand で実行。戻り値は ExitCode（起動失敗/タイムアウトは -1）。</summary>
    public int Run(string command, out string combined)
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
                combined = "割り込み加減の設定変更がタイムアウトしました（60秒）。デバイスマネージャーでアダプタの状態を確認してください。";
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
