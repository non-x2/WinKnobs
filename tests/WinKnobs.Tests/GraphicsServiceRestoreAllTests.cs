using System;
using System.IO;
using WinKnobs.Services;
using Xunit;

namespace WinKnobs.Tests;

/// <summary>
/// GraphicsService.RestoreAll の無音no-op解消（バックログ#11）の単体テスト。
/// SystemSoundService.RestoreAll と同型でthrow化した2分岐を検証する
/// （3つ目のTOCTOU限定分岐はplan-validator判断でテスト対象外）。
/// </summary>
public class GraphicsServiceRestoreAllTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _backup;
    private readonly GraphicsService _sut;

    public GraphicsServiceRestoreAllTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinKnobsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _backup = new BackupService(_tempDir);
        _sut = new GraphicsService(new RegistryService(), _backup);
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
    public void RestoreAll_退避なしはthrow()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => _sut.RestoreAll());
        Assert.Equal(GraphicsService.NoBackupDataMessage, ex.Message);
    }

    [Fact]
    public void RestoreAll_退避JSON破損はthrow_かつ後始末で退避が消える()
    {
        _backup.SaveMaintenanceOriginalIfAbsent(GraphicsService.FeatureKey, "{ not valid json");

        var ex = Assert.Throws<InvalidOperationException>(() => _sut.RestoreAll());

        Assert.Equal(GraphicsService.NoBackupDataMessage, ex.Message);
        Assert.False(_backup.HasMaintenanceOriginal(GraphicsService.FeatureKey));
    }
}
