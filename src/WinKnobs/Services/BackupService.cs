using System.Diagnostics;
using System.IO;
using System.Text.Json;
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
/// - v0.4.10（バックログ#7）：Command 型戻しトークンを command_tokens.json に永続化。
///   xUnit 分離用にコンストラクタへ backupDirOverride 引数を追加（既定 null＝従来通り %APPDATA%）
///
/// 最大10件保持・古いものは自動削除（開発ガイドライン準拠）。
/// </summary>
public class BackupService
{
    private const int MaxKeepCount = 10;

    // R-3 plan-validator 致命高1 対応：スタートアップ snapshot 専用カウンター（既存個別バックアップと独立）
    private const int MaxStartupSnapshotCount = 10;

    private readonly string _backupDir;
    private readonly string _startupBackupDir;
    private readonly string _networkBackupDir;       // v0.1.7：ネットワーク退避ストア
    private readonly string _networkOriginalsPath;   // v0.1.7：原状 JSON（first-touch-wins）
    private readonly string _maintenanceBackupDir;       // v0.1.19：システム・メンテナンス退避ストア
    private readonly string _maintenanceOriginalsPath;   // v0.1.19：原状 JSON（first-touch-wins）
    private readonly string _commandTokensPath;           // v0.4.10：Command 型戻しトークン永続化ストア（バックログ#7）
    private readonly string _regExePath;

    /// <summary>
    /// backupDirOverride は xUnit 専用シーム（バックログ#7）：既定は %APPDATA%\WinKnobs\backup\ を使う。
    /// 実測（2026-07-21）：Environment.SetEnvironmentVariable("APPDATA", ...) は
    /// Environment.GetFolderPath(SpecialFolder.ApplicationData) の戻り値に反映されない
    /// （Windows 版 .NET は SHGetKnownFolderPath でユーザープロファイルから解決するため）。
    /// そのためテスト分離は環境変数差し替えでなくこのコンストラクタ引数で行う。
    /// AddSingleton&lt;BackupService&gt;() からは DI が string の既定値を解決できず parameterless 相当で呼ばれる。
    /// </summary>
    public BackupService(string? backupDirOverride = null)
    {
        // F-5：%APPDATA%\WinKnobs\backup\ に変更
        _backupDir = backupDirOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinKnobs", "backup");
        Directory.CreateDirectory(_backupDir);

        // R-3 plan-validator 致命高1 対応：スタートアップ snapshot 専用サブディレクトリ。
        // 既存の個別バックアップ（PR1 等）と保存先を分離し、ローテーション 10 件枠を独立確保。
        _startupBackupDir = Path.Combine(_backupDir, "startup");
        Directory.CreateDirectory(_startupBackupDir);

        // v0.1.7：ネットワーク退避ストア（plan-validator #4：ExportKey でなく JSON 原状を集約）。
        _networkBackupDir = Path.Combine(_backupDir, "network");
        Directory.CreateDirectory(_networkBackupDir);
        _networkOriginalsPath = Path.Combine(_networkBackupDir, "originals.json");

        // v0.1.19（plan-validator #2）：システム・メンテナンス退避ストアは network と**別系統**に分離。
        // 理由：network/originals.json に同居させると NetworkService.RevertAll（固定3キー配列）の対象外なのに
        //   同一ファイルを共有し、DoH 退避の道連れ破損／「ぜんぶ戻す」で戻らない孤児 UX 事故を招く。
        //   構造で「これは Maintenance 専用＝NetworkService.RevertAll は触らない」を明示する。
        _maintenanceBackupDir = Path.Combine(_backupDir, "maintenance");
        Directory.CreateDirectory(_maintenanceBackupDir);
        _maintenanceOriginalsPath = Path.Combine(_maintenanceBackupDir, "originals.json");

        // v0.4.10（plan-validator 2026-07-21・バックログ#7）：Command 型「戻す」トークンの永続化ストア。
        // network/maintenance と同じ理由（道連れ破損防止）で別ファイルに分離。単一ファイルのみなので
        // 専用サブディレクトリは持たず backup 直下に置く。
        _commandTokensPath = Path.Combine(_backupDir, "command_tokens.json");

        // E-4：reg.exe はフルパス指定
        _regExePath = Path.Combine(Environment.SystemDirectory, "reg.exe");
    }

    /// <summary>バックアップ保存先ディレクトリ（個別バックアップ用）。</summary>
    public string BackupDirectory => _backupDir;

    /// <summary>スタートアップ snapshot 専用ディレクトリ。</summary>
    public string StartupBackupDirectory => _startupBackupDir;

    /// <summary>ネットワーク退避ストア専用ディレクトリ（v0.1.7）。</summary>
    public string NetworkBackupDirectory => _networkBackupDir;

    /// <summary>システム・メンテナンス退避ストア専用ディレクトリ（v0.1.19）。</summary>
    public string MaintenanceBackupDirectory => _maintenanceBackupDir;

    /// <summary>Command 型戻しトークンストアのファイルパス（v0.4.10・テストからの直接検証用）。</summary>
    public string CommandTokensPath => _commandTokensPath;

    // =================== v0.1.7：ネットワーク原状退避（JSON・first-touch-wins） ===================
    // plan-validator #4 反映：配信最適化 .reg(ExportKey) を使わず、各機能の原状を JSON 集約。
    // 「戻す」の意味は「WinKnobs が触る前の原状へ復帰」なので、機能キー単位で初回のみ退避（first-touch-wins）。
    // 再適用しても原状は上書きしない。エッジ3（admin/非admin 並存中の破損）は原子的置換で緩和。

    /// <summary>機能キーの原状を JSON で退避（既に退避済みなら何もしない＝first-touch-wins）。</summary>
    public void SaveNetworkOriginalIfAbsent(string featureKey, string jsonPayload)
    {
        var dict = LoadNetworkOriginals();
        if (dict.ContainsKey(featureKey)) return;
        dict[featureKey] = jsonPayload;
        SaveNetworkOriginals(dict);
    }

    /// <summary>
    /// 機能キーの原状を JSON で退避（無条件上書き・v0.1.7 詳細層）。
    /// アダプタ個別指定の read-modify-write 用：機能キー単位の first-touch-wins では
    /// 「アダプタAだけ適用→後でBだけ適用」で B が退避できない（キー "dns" が既存のため skip）。
    /// 呼び出し側で「既存退避＋新規対象の未収録分」をマージした完全な原状を組んで、ここで上書きする
    /// （first-touch-wins の単位を機能キーからアダプタへ細分化）。必ずマージ後の完全な原状を渡すこと。
    /// </summary>
    public void SaveNetworkOriginal(string featureKey, string jsonPayload)
    {
        var dict = LoadNetworkOriginals();
        dict[featureKey] = jsonPayload;
        SaveNetworkOriginals(dict);
    }

    /// <summary>退避済みの全原状（機能キー→JSON ペイロード）を読む。</summary>
    public Dictionary<string, string> LoadNetworkOriginals()
    {
        try
        {
            if (!File.Exists(_networkOriginalsPath)) return new();
            var json = File.ReadAllText(_networkOriginalsPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>退避済みの原状が 1 件以上あるか（「ぜんぶ戻す」ボタンの有効化判定）。</summary>
    public bool HasNetworkOriginals() => LoadNetworkOriginals().Count > 0;

    /// <summary>指定機能キーの原状退避を削除（個別復元完了後の後始末）。</summary>
    public void RemoveNetworkOriginal(string featureKey)
    {
        var dict = LoadNetworkOriginals();
        if (dict.Remove(featureKey)) SaveNetworkOriginals(dict);
    }

    /// <summary>ネットワーク原状退避をすべて消去（「ぜんぶ戻す」完了後）。</summary>
    public void ClearNetworkOriginals()
    {
        try { if (File.Exists(_networkOriginalsPath)) File.Delete(_networkOriginalsPath); }
        catch { /* 後始末失敗は無害 */ }
    }

    private void SaveNetworkOriginals(Dictionary<string, string> dict)
    {
        // 原子的置換（temp 書込 → コピー上書き → temp 削除）。admin/非admin 並存中の破損を緩和（エッジ3）。
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        var tmp = _networkOriginalsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, _networkOriginalsPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* 無害 */ }
    }

    // =================== v0.1.19：システム・メンテナンス原状退避（JSON・first-touch-wins） ===================
    // plan-validator #2（2026-06-06）：network 版の薄いコピー。保存先のみ backup\maintenance\originals.json に分離し、
    // NetworkService.RevertAll（固定3キー配列）の対象外であることを構造で保証する（孤児 UX 事故・道連れ破損を断つ）。
    // 「戻す」の意味は「WinKnobs が触る前の原状へ復帰」なので機能キー単位で初回のみ退避（first-touch-wins）。

    /// <summary>機能キーの原状を JSON で退避（既に退避済みなら何もしない＝first-touch-wins）。</summary>
    public void SaveMaintenanceOriginalIfAbsent(string featureKey, string jsonPayload)
    {
        var dict = LoadMaintenanceOriginals();
        if (dict.ContainsKey(featureKey)) return;
        dict[featureKey] = jsonPayload;
        SaveMaintenanceOriginals(dict);
    }

    /// <summary>退避済みの全原状（機能キー→JSON ペイロード）を読む。</summary>
    public Dictionary<string, string> LoadMaintenanceOriginals()
    {
        try
        {
            if (!File.Exists(_maintenanceOriginalsPath)) return new();
            var json = File.ReadAllText(_maintenanceOriginalsPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    /// <summary>指定機能キーの原状退避があるか（個別「元に戻す」メニューの有効化判定）。</summary>
    public bool HasMaintenanceOriginal(string featureKey) => LoadMaintenanceOriginals().ContainsKey(featureKey);

    /// <summary>指定機能キーの原状退避を削除（個別復元の完全成功後の後始末）。</summary>
    public void RemoveMaintenanceOriginal(string featureKey)
    {
        var dict = LoadMaintenanceOriginals();
        if (dict.Remove(featureKey)) SaveMaintenanceOriginals(dict);
    }

    private void SaveMaintenanceOriginals(Dictionary<string, string> dict)
    {
        // network 版と同じ原子的置換（temp 書込 → コピー上書き → temp 削除）。
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        var tmp = _maintenanceOriginalsPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, _maintenanceOriginalsPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* 無害 */ }
    }

    // =================== v0.4.10：Command 型戻しトークン永続化（JSON・first-touch-wins） ===================
    // plan-validator 2026-07-21（バックログ#7）：設定画面を閉じると CommandAction.Backup が返すトークンが
    // ViewModel のフィールド（_lastCommandToken）にしか残らず消える → 再度開くと「戻す」が使えない不具合の解消。
    // itemId 単位で JSON 永続化し、Load 時に読み戻す。network/maintenance 版と同じ first-touch-wins・原子的置換。
    //
    // 既知の弱点（plan-validator 指摘・意識的な割り切り）：
    // 1. admin/非admin 並存中の read-modify-write は Lost Update しうる（既存 network/maintenance 2 ストアと
    //    同じ弱点をそのまま継承。プロセス間ロックは持たない）。
    // 2. 委譲型 Command（トークンが「マーカー」のみで実データは Maintenance ストア側にある設計・例：
    //    SaveMaintenanceOriginalIfAbsent と組み合わせる Command）では、このストアのマーカーと Maintenance
    //    側の実データがズレると「戻す」が例外を出さず無音で no-op になりうる（信頼境界。呼び出し元の
    //    CommandAction.Restore 実装が両ストアの整合を保証する責務を持つ）。

    /// <summary>Command 型項目の復元用トークンを itemId 単位で退避（既に退避済みなら何もしない＝first-touch-wins）。</summary>
    public void SaveCommandTokenIfAbsent(string itemId, string token)
    {
        var dict = LoadCommandTokensDict();
        if (dict.ContainsKey(itemId)) return;
        dict[itemId] = token;
        SaveCommandTokensDict(dict);
    }

    /// <summary>itemId の復元用トークンを読む（未保存 or 破損時は null。破損時は全件喪失＝既存 Network/Maintenance と同じ割り切り）。</summary>
    public string? LoadCommandToken(string itemId)
    {
        var dict = LoadCommandTokensDict();
        return dict.TryGetValue(itemId, out var token) ? token : null;
    }

    /// <summary>itemId のトークンを削除（戻し完了後の後始末）。削除失敗（ファイルロック等）は無害として握り潰す（ClearNetworkOriginals 同型）。</summary>
    public void RemoveCommandToken(string itemId)
    {
        try
        {
            var dict = LoadCommandTokensDict();
            if (dict.Remove(itemId)) SaveCommandTokensDict(dict);
        }
        catch { /* 後始末失敗は無害 */ }
    }

    private Dictionary<string, string> LoadCommandTokensDict()
    {
        try
        {
            if (!File.Exists(_commandTokensPath)) return new();
            var json = File.ReadAllText(_commandTokensPath);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private void SaveCommandTokensDict(Dictionary<string, string> dict)
    {
        // network/maintenance 版と同じ原子的置換（temp 書込 → コピー上書き → temp 削除）。
        var json = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true });
        var tmp = _commandTokensPath + ".tmp";
        File.WriteAllText(tmp, json);
        File.Copy(tmp, _commandTokensPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* 無害 */ }
    }

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

    // ─── R-3 実装：スタートアップ snapshot ───

    /// <summary>
    /// R-3：スタートアップ系の全 9 キー（6 Run + 3 StartupApproved）を 1 つの統合 .reg にエクスポート。
    ///
    /// 対象キー：
    ///   - HKLM/HKCU の Run / RunOnce / WOW6432Node\Run / WOW6432Node\RunOnce
    ///   - HKLM/HKCU の Explorer\StartupApproved\Run / Run32
    ///
    /// 出力先：%APPDATA%\WinKnobs\backup\startup\startup_full_YYYYMMDD_HHmmss.reg
    /// ローテーション：MaxStartupSnapshotCount（10 件）を超えたら古い順に削除（独自カウンター）。
    ///
    /// R-3 plan-validator 致命高1 対応：既存 ExportKey（共通 10 件ローテ）とは保存先を分離。
    /// </summary>
    /// <returns>統合 .reg ファイルのフルパス</returns>
    public string BackupStartupSnapshot()
    {
        // ミリ秒まで含める（plan-validator 指摘：秒精度だと管理者版/非管理者版が 1 秒以内に
        // 同時取得した際に同名衝突 → .reg と .sha256 が別プロセスの組合せになり Verify が常に false になる）。
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
        var finalPath = Path.Combine(_startupBackupDir, $"startup_full_{timestamp}.reg");

        // 9 キーをフルパスで列挙（reg.exe 互換の HKEY_* 形式）
        var keys = new[]
        {
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_LOCAL_MACHINE\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\RunOnce",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run",
            @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32",
            @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
        };

        // 一時ディレクトリで各キーをエクスポート → 統合（途中失敗時は中間ファイル削除）
        var tempDir = Path.Combine(_startupBackupDir, $".tmp_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var sections = new List<string>();

            for (int i = 0; i < keys.Length; i++)
            {
                var keyPath = keys[i];
                var tempFile = Path.Combine(tempDir, $"part_{i:D2}.reg");

                if (!RegistryKeyExistsFullPath(keyPath))
                {
                    // キー不在：[-FullKeyPath] 削除マーカーセクションを生成（個別 ExportKey と同じ仕様）
                    sections.Add(BuildMissingKeyMarkerSection(keyPath));
                    continue;
                }

                // reg.exe export で一時ファイルに保存
                var psi = new ProcessStartInfo(_regExePath)
                {
                    ArgumentList = { "export", keyPath, tempFile, "/y" },
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
                    throw new InvalidOperationException($"reg export failed for {keyPath}: {err}");
                }

                // 一時ファイルからヘッダ除いた本文を取得（2 つ目以降はヘッダ重複させない）
                var body = ReadRegBodyExceptHeader(tempFile);

                // R-3 cross-reviewer 致命高2 対応：空 section / 無効 section を統合 .reg に混入させない。
                // 正常 section は `[HKEY_...]` か `[-HKEY_...]` を含むはず。
                if (string.IsNullOrWhiteSpace(body))
                {
                    throw new InvalidOperationException(
                        $"reg.exe export が空の本文を返しました（key={keyPath}）。" +
                        "snapshot 整合性を保つため中止します。");
                }
                if (!body.Contains("[HKEY_", StringComparison.OrdinalIgnoreCase)
                    && !body.Contains("[-HKEY_", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"reg.exe export の本文に有効な reg セクションが含まれません（key={keyPath}）。");
                }
                sections.Add(body);
            }

            // R-3 cross-reviewer 致命高2 対応：section 数の最終検証（途中 break や break loop の検知）
            if (sections.Count != keys.Length)
            {
                throw new InvalidOperationException(
                    $"snapshot section 数の不整合（期待 {keys.Length} / 実際 {sections.Count}）。");
            }

            // 統合 .reg を書き出し（UTF-16 LE BOM + 共通ヘッダ + 各セクション）
            var sb = new System.Text.StringBuilder();
            sb.Append("Windows Registry Editor Version 5.00\r\n\r\n");
            sb.Append("; WinKnobs Startup snapshot (R-3)\r\n");
            sb.Append($"; Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}\r\n");
            sb.Append($"; Keys: {keys.Length} (6 Run + 3 StartupApproved)\r\n\r\n");
            foreach (var sec in sections)
            {
                sb.Append(sec);
                if (!sec.EndsWith("\r\n\r\n", StringComparison.Ordinal))
                    sb.Append("\r\n");
            }

            File.WriteAllText(finalPath, sb.ToString(), new System.Text.UnicodeEncoding(false, true));
            ValidateBackupFile(finalPath);

            // A（v0.1.6）：改ざん検知用の SHA256 サイドカー（+ ACL 保護）を生成。
            // 失敗してもメインの snapshot 作成は止めない（EnsureSnapshot の「Modify を止めない」思想に合わせる）。
            try
            {
                StartupSnapshotIntegrity.WriteSidecar(finalPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BackupService] SHA256 サイドカー生成失敗（snapshot 自体は有効）: {ex.Message}");
            }

            CleanupOldStartupSnapshots();
            return finalPath;
        }
        catch
        {
            // 失敗時：中間ファイル削除（部分バックアップ残存回避）
            try
            {
                if (File.Exists(finalPath)) File.Delete(finalPath);
            }
            catch { /* 無視 */ }
            throw;
        }
        finally
        {
            // 一時ディレクトリは必ず掃除
            try
            {
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
            }
            catch { /* 無視 */ }
        }
    }

    /// <summary>
    /// スタートアップ snapshot 一覧を新しい順で取得。
    /// 並びキーは LastWriteTime ではなくファイル名（cross-reviewer 軽微3）。
    /// ファイル名は startup_full_yyyyMMdd_HHmmss_fff（ミリ秒まで一意）なので、
    /// 序数降順＝時系列降順で決定的。手動コピー等で LastWriteTime が変わっても順序が安定する。
    /// </summary>
    public IReadOnlyList<FileInfo> ListStartupSnapshots()
    {
        return Directory.GetFiles(_startupBackupDir, "startup_full_*.reg")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>最新のスタートアップ snapshot ファイルのパス（無ければ null）。</summary>
    public string? GetLatestStartupSnapshotPath()
    {
        return ListStartupSnapshots().FirstOrDefault()?.FullName;
    }

    /// <summary>
    /// A（v0.1.6）：スタートアップ snapshot の SHA256 整合性を検証（StartupSnapshotIntegrity に委譲）。
    /// </summary>
    /// <returns>
    ///   IsValid: true=一致 / false=不一致 or サイドカー破損（改ざんの疑い）/ null=サイドカーなし（v0.1.5 以前の古い snapshot）。
    ///   ExpectedHash / ActualHash: 照合に使った各ハッシュ（取得不可なら null）。
    /// </returns>
    public (bool? IsValid, string? ExpectedHash, string? ActualHash) VerifyStartupSnapshotIntegrity(string snapshotRegPath)
        => StartupSnapshotIntegrity.Verify(snapshotRegPath);

    /// <summary>
    /// スタートアップ snapshot 専用の独自ローテーション（10 件まで保持）。
    /// 並びキーはファイル名（ListStartupSnapshots と統一・cross-reviewer 軽微3）。
    /// 注意（cross-reviewer 重要1）：.sha256 は ACL で継承遮断しているため、ローミングプロファイルや
    /// 別マシンへのコピー等で SID が食い違うと削除に失敗し得る（管理者なら可）。その場合は孤児 .sha256 が
    /// 残るが、削除失敗は握り潰す設計（snapshot 運用自体は継続）。
    /// </summary>
    private void CleanupOldStartupSnapshots()
    {
        var files = Directory.GetFiles(_startupBackupDir, "startup_full_*.reg")
            .Select(f => new FileInfo(f))
            .OrderByDescending(f => f.Name, StringComparer.Ordinal)
            .ToList();

        foreach (var old in files.Skip(MaxStartupSnapshotCount))
        {
            // A（v0.1.6）：.reg 本体と対の .sha256 サイドカーを「個別に」try-catch で削除。
            // 個別にするのは、どちらか片方の削除に失敗しても他方を確実に試して孤児化を防ぐため
            // （.reg だけ消えて .sha256 が残る／その逆 の両方を回避）。
            try { old.Delete(); }
            catch { /* 削除失敗は無視 */ }

            try
            {
                var sidecar = StartupSnapshotIntegrity.GetSidecarPath(old.FullName);
                if (File.Exists(sidecar)) File.Delete(sidecar);
            }
            catch { /* 削除失敗は無視 */ }
        }
    }

    /// <summary>フルパス（HKEY_LOCAL_MACHINE\... 形式）でキー存在チェック。</summary>
    private static bool RegistryKeyExistsFullPath(string fullKeyPath)
    {
        var parts = fullKeyPath.Split('\\', 2);
        var hiveName = parts[0].ToUpperInvariant();
        var subKey = parts.Length > 1 ? parts[1] : string.Empty;

        var hive = hiveName switch
        {
            "HKEY_LOCAL_MACHINE" => RegistryHive.LocalMachine,
            "HKEY_CURRENT_USER" => RegistryHive.CurrentUser,
            "HKEY_CLASSES_ROOT" => RegistryHive.ClassesRoot,
            "HKEY_USERS" => RegistryHive.Users,
            "HKEY_CURRENT_CONFIG" => RegistryHive.CurrentConfig,
            _ => RegistryHive.LocalMachine
        };

        try
        {
            // WOW6432Node を含むパスは Registry32 ビューを使う（64bit OS でも 32bit ハイブ）
            var view = subKey.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase)
                ? RegistryView.Registry32
                : RegistryView.Registry64;

            using var baseKey = RegistryKey.OpenBaseKey(hive, view);
            if (string.IsNullOrEmpty(subKey)) return true;
            using var key = baseKey.OpenSubKey(subKey, writable: false);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>不在キーの削除マーカーセクションを生成（reg import で [-FullKeyPath] 解釈）。</summary>
    private static string BuildMissingKeyMarkerSection(string fullKeyPath)
    {
        return
            $"; --- key not present at snapshot time ---\r\n" +
            $"; KeyPath={fullKeyPath}\r\n" +
            $"[-{fullKeyPath}]\r\n\r\n";
    }

    /// <summary>
    /// 一時 .reg ファイルからヘッダ行（"Windows Registry Editor Version 5.00" + 直後の空行）を除いた本文を返す。
    /// reg.exe は UTF-16 LE BOM 付きで出力するため、UTF-16 として読み込み。
    /// </summary>
    private static string ReadRegBodyExceptHeader(string regFilePath)
    {
        var fullText = File.ReadAllText(regFilePath, System.Text.Encoding.Unicode);
        const string headerLine = "Windows Registry Editor Version 5.00";
        var idx = fullText.IndexOf(headerLine, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            // ヘッダなし：そのまま返す（reg.exe 出力形式が変わった場合のフォールバック）
            return fullText.TrimStart('﻿');
        }

        // ヘッダ後の最初の改行までを飛ばし、その後の空行も飛ばす
        var startSearch = idx + headerLine.Length;
        var newlineIdx = fullText.IndexOf('\n', startSearch);
        if (newlineIdx < 0) return string.Empty;

        var body = fullText.Substring(newlineIdx + 1).TrimStart('\r', '\n');
        return body;
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
