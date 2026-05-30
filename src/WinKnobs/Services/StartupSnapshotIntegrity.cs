using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace WinKnobs.Services;

/// <summary>
/// スタートアップ snapshot(.reg) の改ざん検知（v0.1.6 機能 A：SHA256 + ACL）。
///
/// 設計の流儀（機能 B の StartupSnapshotDiffer / StartupApprovedCodec に倣う）：
///   I/O ロジックを純粋静的クラスに切り出し、BackupService からは委譲するだけにする。
///   ハッシュ計算は Stream オーバーロードを用意し、ファイル無しで単体テスト可能にしている。
///
/// しくみ：
///   - snapshot 生成時に、本体 .reg と同じ basename の `<snapshot>.sha256` サイドカーを作る。
///     形式は sha256sum 互換： "&lt;64桁小文字hex&gt;␣␣&lt;ファイル名&gt;\r\n"
///   - 復元の直前にサイドカーのハッシュと本体ファイルの実ハッシュを照合する。
///   - サイドカーには ACL を掛けて他ユーザー/他プロセスからの上書きを遮断する（多層防御）。
///
/// SHA256 の保証範囲：
///   「保存された snapshot ファイルが改ざんされていないか」だけを保証する。
///   「snapshot の内容が最新の操作意図と合っているか」は別問題（作成日時で判断する）。
///
/// 脅威モデル（設計ドラフト v2 §A-1）：
///   最も対策効果が高いのはシナリオ 2（他ユーザーアカウントからの AppData 上書き）。
///   同一ユーザー権限の PUA はサイドカーも書き換えられるため SHA256 単体では完全には防げない
///   （ACL も現ユーザー Modify を許す＝同一ユーザーには書換可能・これは設計上の限界）。
/// </summary>
public static class StartupSnapshotIntegrity
{
    /// <summary>サイドカーファイルの拡張子。</summary>
    private const string SidecarExtension = ".sha256";

    /// <summary>snapshot 本体パスから対応するサイドカーパス（同ディレクトリ・同 basename + .sha256）を返す。</summary>
    public static string GetSidecarPath(string snapshotRegPath)
        => Path.ChangeExtension(snapshotRegPath, SidecarExtension);

    /// <summary>
    /// ストリームの SHA256 を 64 桁小文字 hex で返す（純粋・テスト用）。
    /// </summary>
    public static string ComputeSha256(Stream stream)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// ファイルの SHA256 を 64 桁小文字 hex で返す。
    /// バイト列そのもの（UTF-16 LE BOM 込み）をハッシュ化する＝テキスト再エンコードでズレない。
    /// EA-5：FileShare.Read で他プロセスの読込ロックと共存。
    /// </summary>
    public static string ComputeFileSha256(string filePath)
    {
        using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        return ComputeSha256(fs);
    }

    /// <summary>サイドカー本文を組み立て（sha256sum 互換： "&lt;hash&gt;␣␣&lt;fileName&gt;\r\n"）。</summary>
    public static string BuildSidecarContent(string hash, string fileName)
        => $"{hash}  {fileName}\r\n";

    /// <summary>
    /// サイドカー本文から SHA256 hex（先頭トークン）を抽出する。
    /// ファイル名欄（2 番目以降）は無視する＝改名後でもハッシュ部分だけを照合する。
    /// 64 桁 hex でない（空・手編集・破損）場合は null を返す。
    /// </summary>
    public static string? ParseSidecarHash(string? sidecarContent)
    {
        if (string.IsNullOrWhiteSpace(sidecarContent)) return null;

        var firstToken = sidecarContent
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        if (firstToken == null) return null;

        return IsValidSha256Hex(firstToken) ? firstToken.ToLowerInvariant() : null;
    }

    /// <summary>64 桁の 16 進数文字列かどうか。</summary>
    private static bool IsValidSha256Hex(string s)
        => s.Length == 64 && s.All(Uri.IsHexDigit);

    /// <summary>
    /// snapshot 本体の SHA256 サイドカーを生成し、ACL 保護を掛ける。
    /// 呼び出し側（BackupService）で try-catch する想定だが、ここでも ACL 失敗は内部で握り潰す。
    /// </summary>
    public static void WriteSidecar(string snapshotRegPath)
    {
        var hash = ComputeFileSha256(snapshotRegPath);
        var sidecarPath = GetSidecarPath(snapshotRegPath);
        var content = BuildSidecarContent(hash, Path.GetFileName(snapshotRegPath));

        // ASCII 範囲（hex + ファイル名）なので UTF-8 BOM なしで保存。
        File.WriteAllText(sidecarPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        TryProtectAcl(sidecarPath);
    }

    /// <summary>
    /// snapshot の整合性を SHA256 で検証する。
    /// </summary>
    /// <returns>
    ///   IsValid: true=一致 / false=不一致 or サイドカー破損（改ざんの疑い）/ null=サイドカーなし（古い snapshot）。
    ///   ExpectedHash: サイドカーから読み取ったハッシュ（取得不可なら null）。
    ///   ActualHash: 本体ファイルから計算したハッシュ（取得不可なら null）。
    /// </returns>
    public static (bool? IsValid, string? ExpectedHash, string? ActualHash) Verify(string snapshotRegPath)
    {
        var sidecarPath = GetSidecarPath(snapshotRegPath);

        // EA-1 / EA-2：サイドカーなし＝v0.1.5 以前の古い snapshot → 警告なしで通過させる。
        if (!File.Exists(sidecarPath))
            return (null, null, null);

        string? expected;
        try
        {
            expected = ParseSidecarHash(File.ReadAllText(sidecarPath));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Integrity] サイドカー読込失敗（fail-safe で警告）: {ex.Message}");
            return (false, null, null);
        }

        // EA-3：サイドカーが破損・手編集で 64hex でない → fail-safe で「怪しい（警告）」扱い。
        if (expected == null)
            return (false, null, null);

        string actual;
        try
        {
            actual = ComputeFileSha256(snapshotRegPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Integrity] 本体ハッシュ計算失敗（fail-safe で警告）: {ex.Message}");
            return (false, expected, null);
        }

        var isValid = string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase);
        return (isValid, expected, actual);
    }

    /// <summary>
    /// サイドカーファイルに DACL を掛けて他ユーザー/他プロセスの上書きを遮断する（多層防御）。
    ///
    /// 方針（plan-validator 指摘 反映）：
    ///   - app.manifest は asInvoker（非管理者既定）なので Owner 付け替えはしない（失敗するため）。
    ///   - 現ユーザーは Modify を維持する＝WinKnobs 自身が次回 WriteSidecar / Cleanup で
    ///     書込・削除できる（自己ロックアウト回避）。
    ///   - 継承を切り、継承された Users 等のルールを引き継がない＝他ユーザーを遮断（シナリオ 2 対策）。
    ///   - SYSTEM / 管理者には FullControl を残す（OS・管理ツール・復旧経路の確保）。
    ///
    /// 失敗は致命的でない（SHA256 による改ざん検知自体は ACL なしでも機能する）→ ログのみ。
    /// </summary>
    private static void TryProtectAcl(string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var security = fileInfo.GetAccessControl();

            // 継承を切り、既存の継承ルールはコピーしない（他ユーザー/Everyone を遮断）。
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            // 現ユーザー：Modify（読み書き削除）＝自己ロックアウト回避。
            var userSid = WindowsIdentity.GetCurrent().User;
            if (userSid != null)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    userSid,
                    FileSystemRights.Modify | FileSystemRights.Synchronize,
                    AccessControlType.Allow));
            }

            // SYSTEM：FullControl。
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // 管理者グループ：FullControl（ACL 不調時の復旧経路）。
            security.AddAccessRule(new FileSystemAccessRule(
                new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
                FileSystemRights.FullControl,
                AccessControlType.Allow));

            // Owner は触らない（DACL のみ反映）。
            fileInfo.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Integrity] ACL 保護失敗（SHA256 検知は継続）: {ex.Message}");
        }
    }
}
