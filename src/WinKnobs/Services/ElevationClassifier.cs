namespace WinKnobs.Services;

/// <summary>
/// PowerShell 等の外部コマンド出力から「管理者権限不足（昇格が必要）」を判定する共通ヘルパー。
///
/// v0.1.17 時点で <see cref="SystemProtectionHelper"/> と <see cref="MemoryCompressionHelper"/> の
/// 2クラスに同一の権限不足キーワード集がコピー存在していた（Rule of Three 到達・@cross-reviewer 監視指摘）。
/// 「片方だけキーワードを追加して齟齬が出る」リスクを断つため、キーワード集をここへ一元化する。
///
/// 注意：本クラスは「出力が権限不足を示すか」の bool 判定のみ。ExitCode との合成や enum へのマッピングは
/// 各 Helper.Classify が担う（返す enum 型が Helper ごとに異なるため）。
///
/// <see cref="RestorePointService"/> は独自の狭い判定（access/denied・アクセス/拒否のみ＋FailedFrequency 等の
/// 分岐）を内蔵しており、挙動互換のため本クラスへは寄せていない（必要になれば別途検討）。
/// </summary>
public static class ElevationClassifier
{
    /// <summary>
    /// 出力文字列が「管理者権限不足（昇格が必要）」を示すか。
    /// 英語・日本語・PowerShell の典型エラー（PrivilegeNotHeld / requires elevation 等）に対応。
    /// null/空は false。
    /// </summary>
    public static bool LooksLikeAccessDenied(string? output)
    {
        var s = output ?? string.Empty;
        return (Contains(s, "access") && Contains(s, "denied")) ||
               (Contains(s, "アクセス") && Contains(s, "拒否")) ||
               Contains(s, "administrator") ||
               Contains(s, "管理者") ||
               Contains(s, "elevat") ||                 // requires elevation / elevated
               Contains(s, "PrivilegeNotHeld") ||
               Contains(s, "UnauthorizedAccess");
    }

    private static bool Contains(string s, string keyword)
        => s.IndexOf(keyword, System.StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// 現在のプロセスが管理者として実行中か（v0.2.1 cross-reviewer I-4）。
    /// bcdedit /enum や Get-WindowsOptionalFeature は**読取が非昇格でも通る**ため、書込段階まで
    /// 権限不足が遅延し、出力キーワード分類の取りこぼしで Failed 扱いになる経路があった。
    /// 書込系サービスは実行前にこれで早期 AccessDenied を返す（PrivilegeService と同実装・
    /// サービス層から DI なしで使えるよう static で提供）。
    /// </summary>
    public static bool IsRunningAsAdministrator()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;   // 判定不能は安全側（非管理者扱い）。実書込の失敗分類が最後の防衛線
        }
    }
}
