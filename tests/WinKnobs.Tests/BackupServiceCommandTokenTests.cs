using System.IO;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// BackupService の Command 型戻しトークン永続化（v0.4.10・バックログ#7）の単体テスト。
///
/// StartupSnapshotIntegrityTests に倣い %APPDATA% を汚さないよう Path.GetTempPath() 配下に
/// 専用ディレクトリを作り、Dispose で確実に削除する。BackupService は既定で %APPDATA%\WinKnobs\backup\
/// を使うが、backupDirOverride コンストラクタ引数（xUnit 専用シーム）でテスト用ディレクトリを直接渡す
/// （実測：Environment.SetEnvironmentVariable("APPDATA", ...) は GetFolderPath に反映されないため）。
/// </summary>
public class BackupServiceCommandTokenTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _sut;

    public BackupServiceCommandTokenTests()
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
    public void SaveCommandTokenIfAbsent_Load往復_保存した値がそのまま読める()
    {
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "token-abc");

        var loaded = _sut.LoadCommandToken("privacy.advertisingIdDisabled");

        Assert.Equal("token-abc", loaded);
    }

    [Fact]
    public void LoadCommandToken_未保存キーはnull()
    {
        var loaded = _sut.LoadCommandToken("no.such.item");

        Assert.Null(loaded);
    }

    [Fact]
    public void SaveCommandTokenIfAbsent_first_touch_wins_2回目のSaveは上書きしない()
    {
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "first-token");
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "second-token");

        var loaded = _sut.LoadCommandToken("privacy.advertisingIdDisabled");

        Assert.Equal("first-token", loaded);
    }

    [Fact]
    public void RemoveCommandToken_削除後はLoadがnullになる()
    {
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "token-abc");

        _sut.RemoveCommandToken("privacy.advertisingIdDisabled");

        Assert.Null(_sut.LoadCommandToken("privacy.advertisingIdDisabled"));
    }

    [Fact]
    public void RemoveCommandToken_存在しないキーの削除は無害()
    {
        // ファイル自体が存在しない状態での Remove も例外を投げない
        var ex = Record.Exception(() => _sut.RemoveCommandToken("no.such.item"));

        Assert.Null(ex);
    }

    [Fact]
    public void LoadCommandToken_破損JSONは全件喪失としてnullを返す()
    {
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "token-abc");

        // ストアファイルへ直接不正な中身を書き込んで破損させる（既存 Network/Maintenance と同じ割り切り＝全件喪失）
        File.WriteAllText(_sut.CommandTokensPath, "{ this is not valid json");

        Assert.Null(_sut.LoadCommandToken("privacy.advertisingIdDisabled"));
    }

    [Fact]
    public void SaveCommandTokenIfAbsent_保存後は_tmpファイルが残らない()
    {
        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "token-abc");

        Assert.True(File.Exists(_sut.CommandTokensPath));
        Assert.False(File.Exists(_sut.CommandTokensPath + ".tmp"));
    }
}
