using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// レジストリ変更前のバックアップサービス。
/// reg.exe export / import を内部で実行して .reg ファイルとして保存・復元する。
///
/// 修正履歴：
/// - F-5：保存先を %APPDATA%\WinKnobs\backup\ に変更（ThemeService と統一）
/// - F-1：ExportKey 後にファイルサイズ＋ヘッダ検証
/// - E-4：reg.exe を Environment.SystemDirectory のフルパス指定で起動
///
/// 最大10件保持・古いものは自動削除（開発ガイドライン準拠）。
/// </summary>
public class BackupService
{
    private const int MaxKeepCount = 10;
    private readonly string _backupDir;
    private readonly string _regExePath;

    public BackupService()
    {
        // F-5：%APPDATA%\WinKnobs\backup\ に変更
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _backupDir = Path.Combine(appData, "WinKnobs", "backup");
        Directory.CreateDirectory(_backupDir);

        // E-4：reg.exe はフルパス指定
        _regExePath = Path.Combine(Environment.SystemDirectory, "reg.exe");
    }

    /// <summary>バックアップ保存先ディレクトリ。</summary>
    public string BackupDirectory => _backupDir;

    /// <summary>
    /// 指定したレジストリキーを .reg ファイルにエクスポート。
    /// </summary>
    /// <param name="keyPath">"HKLM\Software\..." 形式</param>
    /// <param name="label">ファイル名に含めるラベル（項目名等）</param>
    /// <returns>保存した .reg ファイルのパス</returns>
    public string ExportKey(string keyPath, string label)
    {
        if (string.IsNullOrWhiteSpace(keyPath))
            throw new ArgumentException("keyPath is empty", nameof(keyPath));

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var safeLabel = SanitizeFileName(label);
        var filename = $"{timestamp}_{safeLabel}.reg";
        var filepath = Path.Combine(_backupDir, filename);

        // reg.exe にフルパスで渡す。HKLM 等の短縮形を展開
        var fullKeyPath = ExpandHive(keyPath);

        // BUG-1 修正（v0.1.2-dev → v0.1.3）：キーが存在しない場合の対応。
        // 例：PR1 Cortana の HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search は
        //     新規環境では存在せず、reg export がエラー終了する。
        //     その場合「キー削除マーカー .reg」を生成して保存する。
        //     [-FullKeyPath] 記法を reg import 時に解釈してキーごと削除 → 「適用前」状態に正確復元。
        if (!RegistryKeyExists(keyPath))
        {
            WriteMissingKeyMarker(filepath, fullKeyPath);
            ValidateBackupFile(filepath);
            CleanupOldBackups();
            return filepath;
        }

        var psi = new ProcessStartInfo(_regExePath)
        {
            ArgumentList = { "export", fullKeyPath, filepath, "/y" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start reg.exe");
        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"reg export failed: {err}");
        }

        // F-1：ファイルサイズ＋ヘッダ検証
        ValidateBackupFile(filepath);

        CleanupOldBackups();
        return filepath;
    }

    /// <summary>
    /// BUG-1 対応：指定キーが存在しない場合の「キー削除マーカー」.reg を生成。
    /// reg import 時に [-FullKeyPath] 記法でキーを削除してくれる仕様を利用。
    /// reg export と同じ UTF-16 LE BOM 形式で出力（ValidateBackupFile を通すため）。
    /// </summary>
    private static void WriteMissingKeyMarker(string filepath, string fullKeyPath)
    {
        var content =
            "Windows Registry Editor Version 5.00\r\n" +
            "\r\n" +
            "; WinKnobs backup marker: key did not exist before apply (BUG-1 対応)\r\n" +
            "; Generated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
            "; KeyPath=" + fullKeyPath + "\r\n" +
            "\r\n" +
            "[-" + fullKeyPath + "]\r\n";
        // reg.exe は UTF-16 LE BOM 付きを期待する
        File.WriteAllText(filepath, content, new System.Text.UnicodeEncoding(false, true));
    }

    /// <summary>
    /// BUG-1 対応：レジストリキー存在チェック。
    /// </summary>
    private static bool RegistryKeyExists(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        var hive = hiveName switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            "HKCR" => RegistryHive.ClassesRoot,
            "HKU" => RegistryHive.Users,
            "HKCC" => RegistryHive.CurrentConfig,
            _ => RegistryHive.LocalMachine
        };

        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Default);
            if (string.IsNullOrEmpty(subKey)) return true;
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// .reg ファイルからインポートして復元。
    /// </summary>
    public void ImportKey(string regFilePath)
    {
        if (!File.Exists(regFilePath))
            throw new FileNotFoundException($"Backup file not found: {regFilePath}");

        // F-1：インポート前にも検証
        ValidateBackupFile(regFilePath);

        var psi = new ProcessStartInfo(_regExePath)
        {
            ArgumentList = { "import", regFilePath },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start reg.exe");
        p.WaitForExit();

        if (p.ExitCode != 0)
        {
            var err = p.StandardError.ReadToEnd();
            throw new InvalidOperationException($"reg import failed: {err}");
        }
    }

    /// <summary>
    /// バックアップ一覧を新しい順で取得。
    /// </summary>
    public IReadOnlyList<FileInfo> ListBackups()
    {
        return Directory.GetFiles(_backupDir, "*.reg")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();
    }

    /// <summary>
    /// F-1：reg export の出力が「正常な .reg ファイル」かを検証。
    /// 終了コードが 0 でも、アクセス拒否サブキーで空ファイルや破損ファイルが生成されるケースに対応。
    /// </summary>
    private static void ValidateBackupFile(string filepath)
    {
        var fi = new FileInfo(filepath);
        if (!fi.Exists)
            throw new InvalidOperationException($"backup file not created: {filepath}");

        // 最低 30 バイト（"Windows Registry Editor Version 5.00" は約 40 バイト）
        if (fi.Length < 30)
            throw new InvalidOperationException(
                $"backup file is empty or too small ({fi.Length} bytes): {filepath}");

        // ヘッダ確認（UTF-16 LE BOM の可能性もあるので両対応）
        using var stream = File.OpenRead(filepath);
        var buf = new byte[64];
        var read = stream.Read(buf, 0, buf.Length);
        if (read < 30)
            throw new InvalidOperationException(
                $"backup file unreadable: {filepath}");

        // reg export は UTF-16 LE BOM 付きで出力する
        string header;
        if (read >= 2 && buf[0] == 0xFF && buf[1] == 0xFE)
        {
            // UTF-16 LE
            header = System.Text.Encoding.Unicode.GetString(buf, 2, read - 2);
        }
        else
        {
            // フォールバック：ASCII / UTF-8
            header = System.Text.Encoding.UTF8.GetString(buf, 0, read);
        }

        if (!header.StartsWith("Windows Registry Editor", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"backup file header invalid (not a .reg file): {filepath}");
    }

    private void CleanupOldBackups()
    {
        var files = Directory.GetFiles(_backupDir, "*.reg")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.LastWriteTime)
            .ToList();

        foreach (var old in files.Skip(MaxKeepCount))
        {
            try { old.Delete(); }
            catch { /* 削除失敗は無視 */ }
        }
    }

    private static string SanitizeFileName(string label)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = string.Concat(label.Where(c => !invalid.Contains(c)));
        return string.IsNullOrWhiteSpace(safe) ? "backup" : safe;
    }

    /// <summary>
    /// HKLM 等の短縮形を HKEY_LOCAL_MACHINE 等のフル名に展開。
    /// </summary>
    private static string ExpandHive(string keyPath)
    {
        var parts = keyPath.Split('\\', 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        var fullHive = hiveName switch
        {
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKCU" => "HKEY_CURRENT_USER",
            "HKCR" => "HKEY_CLASSES_ROOT",
            "HKU" => "HKEY_USERS",
            "HKCC" => "HKEY_CURRENT_CONFIG",
            _ => hiveName
        };

        return string.IsNullOrEmpty(subKey) ? fullHive : $"{fullHive}\\{subKey}";
    }
}
