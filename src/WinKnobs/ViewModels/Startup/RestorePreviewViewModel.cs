using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Models;

namespace WinKnobs.ViewModels.Startup;

/// <summary>
/// v0.1.6 機能 C：snapshot 復元前プレビューダイアログの ViewModel。
///
/// 機能 B の <see cref="StartupDiffResult"/> を再利用し、3 ペイン（🗑削除 / ✏変更 / ➕追加）で
/// 「復元すると何が起きるか」を見せる。「内容を確認しました」チェック後のみ復元ボタンを有効化する。
///
/// 既存ダイアログ（StartupEntryEditViewModel）と同じ DialogResult プロパティ方式・Load 後注入方式に揃える。
/// 差分 0 件は呼び出し側（StartupListViewModel）で弾く前提なので、CanRestore に件数条件は持たせない。
/// </summary>
public partial class RestorePreviewViewModel : ObservableObject
{
    /// <summary>削除予定（snapshot になし / 現在あり → 復元で消える）。</summary>
    public IReadOnlyList<string> RemovedEntries { get; private set; } = Array.Empty<string>();

    /// <summary>変更予定（両方あり・コマンド or 有効状態が異なる）。種別タグ付き。</summary>
    public IReadOnlyList<string> ModifiedEntries { get; private set; } = Array.Empty<string>();

    /// <summary>追加予定（snapshot にあり / 現在なし → 復元で増える）。</summary>
    public IReadOnlyList<string> AddedEntries { get; private set; } = Array.Empty<string>();

    public int RemovedCount  => RemovedEntries.Count;
    public int ModifiedCount => ModifiedEntries.Count;
    public int AddedCount    => AddedEntries.Count;
    public int TotalCount    => RemovedCount + ModifiedCount + AddedCount;

    // 空ペインに「（なし）」を出すためのフラグ（plan-validator エッジ1）。
    public bool HasRemoved  => RemovedCount > 0;
    public bool HasModified => ModifiedCount > 0;
    public bool HasAdded    => AddedCount > 0;

    /// <summary>snapshot ファイル名（ヘッダ表示）。</summary>
    public string FileName { get; private set; } = string.Empty;

    /// <summary>snapshot 作成日時（ヘッダ表示）。</summary>
    public string CreatedAtText { get; private set; } = string.Empty;

    /// <summary>「内容を確認しました」チェック。変更で CanRestore（OK enable）を更新する。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    private bool _isConfirmed;

    /// <summary>復元ボタン enable 条件（0 件は呼び出し側で弾くのでチェックのみ）。</summary>
    public bool CanRestore => IsConfirmed;

    /// <summary>ダイアログ結果（既存ダイアログと同方式）。true=復元 / false=キャンセル。</summary>
    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// 差分とファイル情報を後注入（既存の Load/LoadNew と同じ流儀）。
    ///
    /// 【契約】このメソッドは DataContext を設定する「前」に呼ぶこと
    /// （DialogService.OpenRestorePreview が Init→DataContext 設定の順を遵守）。
    /// 設定済みプロパティは DataContext 割当時に WPF のバインドが一括取得するため、
    /// ここで個別 PropertyChanged を発火する必要はない（cross-reviewer 重要2：空振り発火の除去）。
    /// </summary>
    public void Init(StartupDiffResult diff, string fileName, DateTime createdAt)
    {
        RemovedEntries  = diff.RemovedEntries;
        ModifiedEntries = diff.ModifiedEntries;
        AddedEntries    = diff.AddedEntries;
        FileName        = fileName;
        CreatedAtText   = createdAt.ToString("yyyy-MM-dd HH:mm:ss");
    }

    [RelayCommand]
    private void Ok() => DialogResult = true;

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
