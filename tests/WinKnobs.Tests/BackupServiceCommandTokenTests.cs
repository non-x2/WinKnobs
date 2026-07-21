using System.IO;
using System.Text.Json;
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

    // =================== v0.4.12（バックログ#10）：鮮度表示・旧形式互換・孤児掃除 ===================

    [Fact]
    public void SaveCommandTokenIfAbsent_保存時刻が記録される()
    {
        var before = DateTimeOffset.Now;

        _sut.SaveCommandTokenIfAbsent("privacy.advertisingIdDisabled", "token-abc");

        var after = DateTimeOffset.Now;
        var savedAt = _sut.LoadCommandTokenSavedAt("privacy.advertisingIdDisabled");

        Assert.NotNull(savedAt);
        Assert.InRange(savedAt!.Value, before.AddSeconds(-1), after.AddSeconds(1));
    }

    [Fact]
    public void LoadCommandTokenSavedAt_未保存キーはnull()
    {
        Assert.Null(_sut.LoadCommandTokenSavedAt("no.such.item"));
    }

    [Fact]
    public void LoadCommandToken_旧形式JSON文字列のみでも読める()
    {
        // v0.4.10 形式（値はトークン文字列のみ）を手書きで再現。
        File.WriteAllText(_sut.CommandTokensPath, "{\"privacy.advertisingIdDisabled\":\"legacy-token\"}");

        var loaded = _sut.LoadCommandToken("privacy.advertisingIdDisabled");

        Assert.Equal("legacy-token", loaded);
    }

    [Fact]
    public void LoadCommandTokenSavedAt_旧形式JSONはnullを返す()
    {
        File.WriteAllText(_sut.CommandTokensPath, "{\"privacy.advertisingIdDisabled\":\"legacy-token\"}");

        Assert.Null(_sut.LoadCommandTokenSavedAt("privacy.advertisingIdDisabled"));
    }

    [Fact]
    public void SaveCommandTokenIfAbsent_旧形式ファイル上での新規保存後は新形式で保存される_遅延移行確認()
    {
        File.WriteAllText(_sut.CommandTokensPath, "{\"existing.item\":\"old-token\"}");

        _sut.SaveCommandTokenIfAbsent("new.item", "new-token");

        // 旧エントリも含めてファイル全体が新形式（オブジェクト値）で書き直されている。
        var raw = File.ReadAllText(_sut.CommandTokensPath);
        using var doc = JsonDocument.Parse(raw);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("existing.item").ValueKind);
        Assert.Equal(JsonValueKind.Object, doc.RootElement.GetProperty("new.item").ValueKind);

        // 互換ロード＋新規保存の往復も正しく機能する。
        Assert.Equal("old-token", _sut.LoadCommandToken("existing.item"));
        Assert.Equal("new-token", _sut.LoadCommandToken("new.item"));
    }

    [Fact]
    public void CleanupOrphanedCommandTokens_有効itemId以外は削除される()
    {
        _sut.SaveCommandTokenIfAbsent("keep.me", "token-1");
        _sut.SaveCommandTokenIfAbsent("orphan.me", "token-2");

        _sut.CleanupOrphanedCommandTokens(new[] { "keep.me" });

        Assert.Null(_sut.LoadCommandToken("orphan.me"));
    }

    [Fact]
    public void CleanupOrphanedCommandTokens_有効itemIdに含まれるものは残る()
    {
        _sut.SaveCommandTokenIfAbsent("keep.me", "token-1");
        _sut.SaveCommandTokenIfAbsent("orphan.me", "token-2");

        _sut.CleanupOrphanedCommandTokens(new[] { "keep.me" });

        Assert.Equal("token-1", _sut.LoadCommandToken("keep.me"));
    }

    [Fact]
    public void CleanupOrphanedCommandTokens_空集合を渡すと削除せず0件を返す_安全弁()
    {
        // plan-validator 指摘：validItemIds が空集合かつストアに1件以上ある場合、
        // カタログ構築の回帰バグ等で誤って全滅させる事故を防ぐため削除しない。
        _sut.SaveCommandTokenIfAbsent("keep.me", "token-1");

        var removed = _sut.CleanupOrphanedCommandTokens(Array.Empty<string>());

        Assert.Equal(0, removed);
        Assert.Equal("token-1", _sut.LoadCommandToken("keep.me"));
    }

    [Fact]
    public void CleanupOrphanedCommandTokens_削除件数が戻り値に一致する()
    {
        _sut.SaveCommandTokenIfAbsent("keep.me", "token-1");
        _sut.SaveCommandTokenIfAbsent("orphan.1", "token-2");
        _sut.SaveCommandTokenIfAbsent("orphan.2", "token-3");

        var removed = _sut.CleanupOrphanedCommandTokens(new[] { "keep.me" });

        Assert.Equal(2, removed);
    }

    [Fact]
    public void CleanupOrphanedCommandTokens_該当なしなら0件でファイルも変化しない()
    {
        _sut.SaveCommandTokenIfAbsent("keep.me", "token-1");
        var before = File.ReadAllText(_sut.CommandTokensPath);

        var removed = _sut.CleanupOrphanedCommandTokens(new[] { "keep.me", "not.present" });

        var after = File.ReadAllText(_sut.CommandTokensPath);
        Assert.Equal(0, removed);
        Assert.Equal(before, after);
    }
}
