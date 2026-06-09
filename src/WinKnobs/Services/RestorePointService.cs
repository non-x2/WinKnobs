using System.Diagnostics;

namespace WinKnobs.Services;

/// <summary>
/// 復元ポイント作成結果。F-6（24時間制限）を区別するための enum 化。
/// </summary>
public enum RestorePointResult
{
    /// <summary>作成成功。</summary>
    Success,
    /// <summary>24時間制限に該当（同日内に既に作成済みの可能性）。</summary>
    FailedFrequency,
    /// <summary>管理者権限不足。</summary>
    FailedAccessDenied,
    /// <summary>システム保護が無効化されている。</summary>
    FailedProtectionDisabled,
    /// <summary>その他の失敗。</summary>
    FailedOther
}

/// <summary>
/// システムの復元ポイントを作成するサービス。
/// PowerShell の Checkpoint-Computer コマンドレットを内部で呼ぶ。
///
/// 修正履歴：
/// - F-6：戻り値を bool → RestorePointResult enum に拡張（24時間制限を UI に明示）
/// </summary>
public class RestorePointService
{
    /// <summary>
    /// 復元ポイントを作成（要管理者権限）。
    /// </summary>
    /// <param name="description">復元ポイントの説明</param>
    /// <param name="errorMessage">失敗時のエラーメッセージ（成功時は null）</param>
    /// <returns>作成結果</returns>
    public RestorePointResult Create(string description, out string? errorMessage)
    {
        errorMessage = null;
        var safeDesc = description.Replace("'", "''");

        var psi = new ProcessStartInfo("powershell.exe")
        {
            ArgumentList =
            {
                "-NoProfile",
                "-NonInteractive",
                "-ExecutionPolicy", "Bypass",
                "-Command",
                $"Checkpoint-Computer -Description '{safeDesc}' -RestorePointType MODIFY_SETTINGS"
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
                return RestorePointResult.FailedOther;
            }

            // 小出力（Checkpoint-Computer はメッセージ少）のため WaitForExit 後の読み取りで十分。
            // 注意：大量出力するコマンドでは stdout/stderr パイプバッファ充填でデッドロックしうる（本サービスは小出力前提・
            // SystemProtectionService と同注記）。
            if (!p.WaitForExit(60_000))
            {
                // タイムアウト時は powershell.exe を確実に終了させる（プロセスリーク防止・同型ハードニング 2026-06-09／
                // SystemProtectionService 軽微③と同手法）。using の Dispose ではプロセスは Kill されないため明示する。
                try { p.Kill(entireProcessTree: true); } catch { /* 既に終了済み等は無視（best-effort） */ }
                errorMessage = "復元ポイント作成がタイムアウトしました（60秒）。";
                return RestorePointResult.FailedOther;
            }

            if (p.ExitCode == 0)
                return RestorePointResult.Success;

            // 失敗時：標準エラー＋標準出力（Checkpoint-Computer は警告で stdout に出すケースあり）
            var stderr = p.StandardError.ReadToEnd();
            var stdout = p.StandardOutput.ReadToEnd();
            var combined = string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;
            errorMessage = combined;

            return Classify(combined);
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return RestorePointResult.FailedOther;
        }
    }

    /// <summary>下位互換用：単純な bool 戻り値の旧 API。</summary>
    public bool Create(string description) => Create(description, out _) == RestorePointResult.Success;

    private static RestorePointResult Classify(string stderr)
    {
        if (string.IsNullOrWhiteSpace(stderr)) return RestorePointResult.FailedOther;

        // 24時間制限関連の典型キーワード（英語・日本語）
        if (Contains(stderr, "frequency") ||
            Contains(stderr, "24") ||
            Contains(stderr, "1440") ||
            Contains(stderr, "頻度") ||
            Contains(stderr, "既に作成") ||
            Contains(stderr, "skipped"))
            return RestorePointResult.FailedFrequency;

        // 権限不足
        if ((Contains(stderr, "access") && Contains(stderr, "denied")) ||
            Contains(stderr, "アクセス") && Contains(stderr, "拒否"))
            return RestorePointResult.FailedAccessDenied;

        // システム保護無効
        if (Contains(stderr, "system protection") ||
            Contains(stderr, "システム保護") ||
            Contains(stderr, "disabled") ||
            Contains(stderr, "無効"))
            return RestorePointResult.FailedProtectionDisabled;

        return RestorePointResult.FailedOther;
    }

    private static bool Contains(string s, string keyword)
        => s.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0;
}
