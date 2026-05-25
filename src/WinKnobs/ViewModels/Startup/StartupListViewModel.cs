using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Models;
using WinKnobs.ViewModels.Items;

namespace WinKnobs.ViewModels.Startup;

/// <summary>
/// スタートアップ系リストの ViewModel。
/// Phase R-1：閲覧のみ。
/// Phase R-2：トグル＋編集ダイアログ。
/// Phase R-3：追加・削除・バックアップ。
/// 設計書 4-2 参照。
/// </summary>
public partial class StartupListViewModel : ObservableObject
{
    private readonly StartupItemsProvider _provider;

    public ObservableCollection<StartupEntryItem> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }

    [ObservableProperty]
    private StartupEntryItem? _selectedEntry;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private string _statusMessage = "スタートアップ項目を読み込んでいません（カテゴリ選択で読込）。";

    public StartupListViewModel(StartupItemsProvider provider)
    {
        _provider = provider;
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = FilterEntry;
    }

    /// <summary>
    /// 全 6 ハイブから読込。
    /// R-1 では同期実装（I/O 軽量で UI スレッド OK）。R-2 以降で非同期化を検討。
    /// </summary>
    [RelayCommand]
    public void Load()
    {
        Entries.Clear();
        try
        {
            foreach (var e in _provider.LoadAll())
            {
                Entries.Add(e);
            }
            StatusMessage = $"📋 {Entries.Count} 件のスタートアップ項目を読み込みました。";
        }
        catch (Exception ex)
        {
            StatusMessage = $"⚠️ 読込エラー：{ex.Message}";
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredEntries?.Refresh();
    }

    private bool FilterEntry(object obj)
    {
        if (obj is not StartupEntryItem e) return false;
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        var q = SearchText.Trim();
        return e.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0
            || e.Command.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ─── R-2 / R-3 で実装（R-1 では何もしない） ───
    // R-2：ToggleEnabledCommand / EditCommand / SaveDirtyCommand
    // R-3：AddCommand / DeleteCommand / RestoreFromBackupCommand
}
