using System.IO;
using Microsoft.Extensions.DependencyInjection;
using WinKnobs.Models;
using WinKnobs.Services;
using WinKnobs.ViewModels;

namespace WinKnobs.Tests;

/// <summary>
/// SettingDialogViewModel の鮮度表示（v0.4.12・バックログ#10）の単体テスト。
///
/// BackupServiceCommandTokenTests と同じく backupDirOverride（xUnit 専用シーム）で %APPDATA% を汚さない。
/// Load() は Command 型かつ CanRestore==true の分岐では RegistryService/DialogService/Launcher/Privilege
/// のいずれにも触れない（ReadAndSyncCurrentValue が item.Command.Read() だけを呼ぶ経路）ため、
/// IServiceProvider は空の ServiceCollection で足りる。
/// </summary>
public class SettingDialogViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly BackupService _backup;
    private readonly SettingDialogViewModel _sut;

    public SettingDialogViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "WinKnobsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _backup = new BackupService(_tempDir);

        var services = new ServiceCollection().BuildServiceProvider();
        _sut = new SettingDialogViewModel(new RegistryService(), _backup, new RestorePointService(), services);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* 無視 */ }
    }

    private static SettingItem MakeCommandItem(string id = "test.command.item") => new()
    {
        Id = id,
        Title = "テスト項目",
        Description = "テスト用 Command 型項目",
        ValueType = SettingValueType.Command,
        Command = new CommandAction
        {
            Read = () => "OFF",
            Apply = _ => { },
            Choices = new[] { "OFF", "ON" },
            Backup = () => "backup-token",
            Restore = _ => { },
        },
    };

    [Fact]
    public void Load_Command型_時刻ありの場合StatusMessageに日付が含まれる()
    {
        var item = MakeCommandItem();
        _backup.SaveCommandTokenIfAbsent(item.Id, "saved-token");
        var savedAt = _backup.LoadCommandTokenSavedAt(item.Id)!.Value;

        _sut.Load(item);

        var expectedDatePart = savedAt.LocalDateTime.ToString("yyyy年M月d日 HH:mm");
        Assert.Contains(expectedDatePart, _sut.StatusMessage);
        Assert.Contains("適用時の状態に戻せます", _sut.StatusMessage);
    }

    [Fact]
    public void Load_Command型_旧形式_SavedAt無しの場合_適用日時は記録されていません文言になる()
    {
        var item = MakeCommandItem();
        // v0.4.10 旧形式（値はトークン文字列のみ）を手書きで再現。
        File.WriteAllText(_backup.CommandTokensPath, $"{{\"{item.Id}\":\"legacy-token\"}}");

        _sut.Load(item);

        Assert.Contains("適用日時は記録されていません", _sut.StatusMessage);
    }
}
