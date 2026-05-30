using System.ComponentModel;
using WinKnobs.Models;
using WinKnobs.ViewModels.Startup;

namespace WinKnobs.Tests;

/// <summary>
/// RestorePreviewViewModel（v0.1.6 機能 C：復元前プレビュー）の単体テスト。
/// UI を起動せず VM のロジック（差分反映・チェック連動の OK enable・OK/Cancel）を検証する。
/// </summary>
public class RestorePreviewViewModelTests
{
    private static StartupDiffResult MakeDiff() =>
        new(
            AddedEntries:    new[] { @"HKCU\Run\Add1" },
            RemovedEntries:  new[] { @"HKCU\Run\Rem1", @"HKLM\Run\Rem2" },
            ModifiedEntries: new[] { @"HKCU\Run\Mod1（コマンド変更）" });

    private static RestorePreviewViewModel MakeVm()
    {
        var vm = new RestorePreviewViewModel();
        vm.Init(MakeDiff(), "startup_full_20260531_120000_123.reg", new DateTime(2026, 5, 31, 12, 0, 0));
        return vm;
    }

    [Fact]
    public void Init_差分とファイル情報を反映する()
    {
        var vm = MakeVm();

        Assert.Equal(1, vm.AddedCount);
        Assert.Equal(2, vm.RemovedCount);
        Assert.Equal(1, vm.ModifiedCount);
        Assert.Equal(4, vm.TotalCount);
        Assert.Equal("startup_full_20260531_120000_123.reg", vm.FileName);
        Assert.Equal("2026-05-31 12:00:00", vm.CreatedAtText);
    }

    [Fact]
    public void Has系フラグ_件数に連動する()
    {
        var vm = MakeVm();
        Assert.True(vm.HasAdded);
        Assert.True(vm.HasRemoved);
        Assert.True(vm.HasModified);

        // 空ペイン（削除のみ）
        var only = new RestorePreviewViewModel();
        only.Init(
            new StartupDiffResult(Array.Empty<string>(), new[] { @"HKCU\Run\X" }, Array.Empty<string>()),
            "f.reg", new DateTime(2026, 5, 31, 0, 0, 0));
        Assert.False(only.HasAdded);
        Assert.True(only.HasRemoved);
        Assert.False(only.HasModified);
    }

    [Fact]
    public void 未チェックなら復元不可_チェックで復元可()
    {
        var vm = MakeVm();

        Assert.False(vm.IsConfirmed);
        Assert.False(vm.CanRestore);

        vm.IsConfirmed = true;
        Assert.True(vm.CanRestore);
    }

    [Fact]
    public void IsConfirmed変更でCanRestoreの変更通知が飛ぶ()
    {
        var vm = MakeVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.IsConfirmed = true;

        // NotifyPropertyChangedFor により CanRestore の通知も飛ぶ（XAML の IsEnabled バインド更新の根拠）
        Assert.Contains(nameof(RestorePreviewViewModel.IsConfirmed), raised);
        Assert.Contains(nameof(RestorePreviewViewModel.CanRestore), raised);
    }

    [Fact]
    public void Ok_でDialogResultがtrue()
    {
        var vm = MakeVm();
        vm.IsConfirmed = true;

        vm.OkCommand.Execute(null);

        Assert.True(vm.DialogResult);
    }

    [Fact]
    public void Cancel_でDialogResultがfalse()
    {
        var vm = MakeVm();

        vm.CancelCommand.Execute(null);

        Assert.False(vm.DialogResult);
    }

    [Fact]
    public void 初期状態_DialogResultはnull()
    {
        var vm = MakeVm();
        Assert.Null(vm.DialogResult);
    }
}
