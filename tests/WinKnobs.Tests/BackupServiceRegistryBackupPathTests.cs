using System.IO;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// BackupService のレジストリ型 LastBackupPath 永続化（v0.4.11・バックログ#9）の単体テスト。
///
/// BackupServiceCommandTokenTests に倣い %APPDATA% を汚さないよう Path.GetTempPath() 配下に
/// 専用ディレクトリを作り、Dispose で確実に削除する。BackupService は既定で %APPDATA%\WinKnobs\backup\
/// を使うが、backupDirOverride コンストラクタ引数（xUnit 専用シーム）でテスト用ディレクトリを直接渡す。
/// </summary>
public class BackupServiceRegistryBackupPathTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _sut;

    public BackupServiceRegistryBackupPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinKnobsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _sut = new BackupService(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* 無視 */ }
    }

    [Fact]
    public void SaveRegistryBackupPath_Load往復_保存した値がそのまま読める()
    {
        var regPath = Path.Combine(_tempDir, "20260721_120000_privacy.telemetryMinimized.reg");
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", regPath);

        var loaded = _sut.LoadRegistryBackupPath("privacy.telemetryMinimized");

        Assert.Equal(regPath, loaded);
    }

    [Fact]
    public void LoadRegistryBackupPath_未保存キーはnull()
    {
        var loaded = _sut.LoadRegistryBackupPath("no.such.item");

        Assert.Null(loaded);
    }

    [Fact]
    public void SaveRegistryBackupPath_latest_wins_2回目のSaveで上書きされる()
    {
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", "first.reg");
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", "second.reg");

        var loaded = _sut.LoadRegistryBackupPath("privacy.telemetryMinimized");

        Assert.Equal("second.reg", loaded);
    }

    [Fact]
    public void RemoveRegistryBackupPath_削除後はLoadがnullになる()
    {
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", "some.reg");

        _sut.RemoveRegistryBackupPath("privacy.telemetryMinimized");

        Assert.Null(_sut.LoadRegistryBackupPath("privacy.telemetryMinimized"));
    }

    [Fact]
    public void RemoveRegistryBackupPath_存在しないキーの削除は無害()
    {
        // ファイル自体が存在しない状態での Remove も例外を投げない
        var ex = Record.Exception(() => _sut.RemoveRegistryBackupPath("no.such.item"));

        Assert.Null(ex);
    }

    [Fact]
    public void LoadRegistryBackupPath_破損JSONは全件喪失としてnullを返す()
    {
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", "some.reg");

        // ストアファイルへ直接不正な中身を書き込んで破損させる（既存3ストアと同じ割り切り＝全件喪失）
        File.WriteAllText(_sut.RegistryBackupPathsPath, "{ this is not valid json");

        Assert.Null(_sut.LoadRegistryBackupPath("privacy.telemetryMinimized"));
    }

    [Fact]
    public void SaveRegistryBackupPath_保存後は_tmpファイルが残らない()
    {
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", "some.reg");

        Assert.True(File.Exists(_sut.RegistryBackupPathsPath));
        Assert.False(File.Exists(_sut.RegistryBackupPathsPath + ".tmp"));
    }

    [Fact]
    public void LoadRegistryBackupPath_ローテーション押し出し相当_値は返るがFile存在はfalse()
    {
        // エントリが指す .reg を削除した状態を再現（ローテーションで押し出された想定）。
        // ViewModel 側の stale 掃除ロジックは「LoadRegistryBackupPath が非null」かつ
        // 「File.Exists が false」の組合せを前提にしているため、その前提をここで確認する。
        var regPath = Path.Combine(_tempDir, "rotated_out.reg");
        _sut.SaveRegistryBackupPath("privacy.telemetryMinimized", regPath);

        // regPath 自体を一度も作成していない（＝存在しない）ことを確認
        Assert.False(File.Exists(regPath));

        var loaded = _sut.LoadRegistryBackupPath("privacy.telemetryMinimized");

        Assert.Equal(regPath, loaded);
        Assert.False(File.Exists(loaded));
    }

    [Fact]
    public void CleanupOldBackups_参照中の_regはローテーションで保護され非参照の古いファイルは削除される()
    {
        // cross-reviewer 2026-07-21 should_fix①：CleanupOldBackups() は registry_backup_paths.json に
        // 現在参照されている .reg を削除対象から除外する。
        //
        // ExportKey は「キーが存在しない」場合は reg.exe を呼ばずローカルにマーカー .reg を書くだけ
        // （WriteMissingKeyMarker）なので、実レジストリに触れず・reg.exe プロセスも起動せず安全に
        // CleanupOldBackups() を発火できる。存在しないことが確実な GUID 付きキー名を使う。
        var referencedKey = @"HKCU\Software\WinKnobsTests_NonExistent_" + Guid.NewGuid().ToString("N");
        var referencedPath = _sut.ExportKey(referencedKey, "referenced_item");
        _sut.SaveRegistryBackupPath("referenced_item", referencedPath);

        // 参照中の .reg を「最も古い」扱いにして、素の LastWriteTime ローテーションなら
        // 真っ先に削除される位置に置く（保護が効いていることの検証を厳しくする）。
        File.SetLastWriteTime(referencedPath, DateTime.Now.AddMinutes(-1000));

        // 非参照の .reg を 11 件作る（MaxKeepCount=10 を超える数）。参照中ファイルより新しい時刻にする。
        var nonReferencedPaths = new List<string>();
        for (int i = 0; i < 11; i++)
        {
            var otherKey = @"HKCU\Software\WinKnobsTests_NonExistent_" + Guid.NewGuid().ToString("N");
            var path = _sut.ExportKey(otherKey, $"non_referenced_{i:D2}");
            File.SetLastWriteTime(path, DateTime.Now.AddMinutes(-100 + i));
            nonReferencedPaths.Add(path);
        }

        // もう一度 ExportKey を呼んで CleanupOldBackups() を発火させる（トリガー用の非参照ファイル）。
        var triggerKey = @"HKCU\Software\WinKnobsTests_NonExistent_" + Guid.NewGuid().ToString("N");
        _sut.ExportKey(triggerKey, "trigger_cleanup");

        // 参照中の .reg は最も古いタイムスタンプでも生存する（保護対象のため削除されない）。
        Assert.True(File.Exists(referencedPath));

        // 非参照ファイルは MaxKeepCount(10) 件までしか残らない（11件中1件以上は削除される）。
        var remainingNonReferenced = nonReferencedPaths.Count(File.Exists);
        Assert.True(remainingNonReferenced <= 10,
            $"非参照 .reg が MaxKeepCount(10) を超えて残っている: {remainingNonReferenced}");
    }
}
