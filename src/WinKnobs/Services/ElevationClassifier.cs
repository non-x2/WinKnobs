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
}
