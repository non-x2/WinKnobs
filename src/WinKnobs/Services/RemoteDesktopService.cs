using System.Diagnostics;
using System.Runtime.InteropServices;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.18 リモートデスクトップ（RDP）を**無効化**するサービス（システム・メンテナンス Phase 4・🔴Red）。
/// MemoryCompressionService / SystemProtectionService と同型（powershell.exe 起動・60秒タイムアウト・ExitCode＋出力で分類）。
///
/// 文字列の組み立て・分類・エディション判定は <see cref="RemoteDesktopHelper"/>（純粋関数・xUnit 済）に委譲。
/// 本サービスは「実行」と、純粋関数で表現できない3つの判定（GPO 管理／自セッションがリモートか／ホスト対応エディションか）を担う。
///
/// ■ スコープ（v0.1.18）：**無効化（OFF）のみ**。有効化（ON）と退避/RevertAll は v0.1.19（ON 追加時）。
///   OFF は攻撃面を閉じるだけ＝安全側・Windows 設定からいつでも戻せるため、メモリ圧縮同様に退避を持たない。
/// ■ fail-closed：BuildDisableCommand が「FW を閉じる → fDeny=1」の順で、途中失敗しても開け直さない（RemoteDesktopHelper 参照）。
/// </summary>
public class RemoteDesktopService
{
    private readonly RegistryService _registry;
    private readonly EnvironmentInfoService _env;

    public RemoteDesktopService(RegistryService registry, EnvironmentInfoService env)
    {
        _registry = registry;
        _env = env;
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    /// <summary>このセッションがリモート（RDP 経由）か（MS 公式の判定法）。</summary>
    private const int SM_REMOTESESSION = 0x1000;

    /// <summary>
    /// 現在の WinKnobs 操作セッションが**リモートデスクトップ経由**か。
    /// GetSystemMetrics(SM_REMOTESESSION)（MS 公式：リモートセッション判定）。0以外＝リモート。
    /// 判定できない例外時は安全側で true（＝強警告を出す方向）に倒す。
    /// </summary>
    public bool IsCurrentSessionRemote()
    {
        try { return GetSystemMetrics(SM_REMOTESESSION) != 0; }
        catch { return true; } // 取れない＝危険側に倒す（締め出しガードを必ず出す）
    }

    /// <summary>
    /// RDP が GPO/MDM ポリシーで管理（強制）されているか。
    /// ポリシーキーに fDenyTSConnections 値が存在すれば、`Control\Terminal Server` を書いても効かない／戻されるため
    /// 本ツールでは変更不可。存在すれば true。
    /// </summary>
    public bool IsPolicyManaged()
    {
        // ReadValue<object> は値が無ければ null。DWORD があれば boxed int が返る。
        var v = _registry.ReadValue<object>(RemoteDesktopHelper.PolicyKeyPath, RemoteDesktopHelper.FDenyValueName);
        return v != null;
    }

    /// <summary>
    /// この PC が RDP ホスト（接続を受ける側）になれるエディションか。
    /// 既存 <see cref="EnvironmentInfoService"/> の EditionID を非昇格で読み、ホワイトリスト判定（Home は不可）。
    /// </summary>
    public bool IsHostCapableEdition()
        => RemoteDesktopHelper.IsRdpHostCapableEdition(_env.Collect().Edition);

    /// <summary>
    /// リモートデスクトップを**無効化**する（要管理者権限）。
    /// FW 規則 Disable → fDeny=1 の順（外→内・fail-closed）。冪等（既に無効でも成功）。
    /// </summary>
    /// <param name="errorMessage">失敗時のエラーメッセージ（成功時は null）</param>
    /// <returns>結果（Success / AccessDenied / Failed）</returns>
    public RemoteDesktopOutcome Disable(out string? errorMessage)
    {
        errorMessage = null;
        var command = RemoteDesktopHelper.BuildDisableCommand();

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
                return RemoteDesktopOutcome.Failed;
            }

            // 小出力前提（Disable-NetFirewallRule / Set-ItemProperty はメッセージ少）。
            if (!p.WaitForExit(60_000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
                errorMessage = "リモートデスクトップの無効化がタイムアウトしました（60秒）。";
                return RemoteDesktopOutcome.Failed;
            }

            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

            if (p.ExitCode != 0)
                errorMessage = combined;

            return RemoteDesktopHelper.Classify(p.ExitCode, combined);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return RemoteDesktopOutcome.Failed;
        }
    }
}
