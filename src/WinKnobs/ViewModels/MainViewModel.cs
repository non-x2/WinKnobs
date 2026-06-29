using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Models;
using WinKnobs.Services;
using WinKnobs.ViewModels.Graphics;
using WinKnobs.ViewModels.Network;
using WinKnobs.ViewModels.Startup;

// .NET 10 で WPF に System.Windows.ThemeMode が追加されたため別名で衝突回避
using ThemeMode = WinKnobs.Services.ThemeMode;

namespace WinKnobs.ViewModels;

/// <summary>
/// メインウィンドウの ViewModel。
/// カテゴリフィルタ・検索・全メニューコマンドを実装。
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly DialogService _dialogs;
    private readonly ThemeService _theme;
    private readonly ExternalLauncherService _launcher;
    private readonly EnvironmentInfoService _envInfo;
    private readonly BackupService _backup;
    private readonly RestorePointService _restore;
    private readonly SystemProtectionService _systemProtection;
    private readonly MemoryCompressionService _memoryCompression;
    private readonly RemoteDesktopService _remoteDesktop;
    private readonly SysmainService _sysmain;
    private readonly WslService _wsl;
    private readonly CoreIsolationService _coreIsolation;
    private readonly HyperVService _hyperV;
    private readonly VirtualMemoryService _virtualMemory;
    private readonly SystemSoundService _systemSound;
    private readonly RegistryService _registry;
    private readonly ExternalLauncherService _launcherForCommands;

    [ObservableProperty]
    private string _statusMessage = "WinKnobs を起動しました。左のカテゴリから設定項目を選んでください。";

    [ObservableProperty]
    private SettingCategory? _selectedCategory;

    [ObservableProperty]
    private SettingItem? _selectedItem;

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>表示メニュー「ライト/ダーク/システム」のチェックマーク表示用（2026-05-16）。</summary>
    [ObservableProperty]
    private ThemeMode _currentThemeMode;

    /// <summary>
    /// スタートアップ系カテゴリ（Run キー族）が選択中か。
    /// MainWindow.xaml で右ペインを既存 DataGrid と StartupListView で Visibility 切替するために使用。
    /// 設計書 5-1（plan-validator #1 反映）。
    /// </summary>
    [ObservableProperty]
    private bool _isStartupCategorySelected;

    /// <summary>
    /// ネットワーク・カテゴリが選択中か（v0.1.7）。専用ペイン NetworkListView の表示切替に使用。
    /// </summary>
    [ObservableProperty]
    private bool _isNetworkCategorySelected;

    /// <summary>
    /// グラフィック・カテゴリが選択中か（v0.3.0）。専用ペイン GraphicsListView の表示切替に使用。
    /// </summary>
    [ObservableProperty]
    private bool _isGraphicsCategorySelected;

    /// <summary>
    /// 標準カテゴリ（Startup でも Network でもない）が選択中か。既存 DataGrid(ItemsGrid) の表示切替に使用。
    /// 専用ペイン追加で右ペインが「3 者択一」になったため、単一 bool（IsStartupCategorySelected の否定）では
    /// 表現できず新設（plan-validator 致命2 反映）。既定 true＝起動直後（未選択）は従来通り DataGrid を表示。
    /// </summary>
    [ObservableProperty]
    private bool _isStandardCategorySelected = true;

    public ObservableCollection<SettingCategory> Categories { get; } = new();
    public ObservableCollection<SettingItem> Items { get; } = new();
    public ICollectionView FilteredItems { get; }

    /// <summary>
    /// スタートアップ系リスト用 ViewModel（DI で取得）。
    /// MainWindow.xaml の StartupListView の DataContext にバインドされる。
    /// </summary>
    public StartupListViewModel StartupListVM { get; }

    /// <summary>
    /// ネットワーク・カテゴリ専用ペイン用 ViewModel（DI 取得・v0.1.7）。
    /// MainWindow.xaml の NetworkListView の DataContext にバインドされる。
    /// </summary>
    public NetworkListViewModel NetworkListVM { get; }

    /// <summary>
    /// グラフィック・カテゴリ専用ペイン用 ViewModel（DI 取得・v0.3.0）。
    /// MainWindow.xaml の GraphicsListView の DataContext にバインドされる。
    /// </summary>
    public GraphicsListViewModel GraphicsListVM { get; }

    /// <summary>
    /// ネットワーク・カテゴリの既存詳細項目（HTTP 同時接続数 / TCP Auto-Tuning 等）を
    /// 専用ペインの「▸ くわしい設定」に表示するための独立ビュー。
    /// FilteredItems とは別の CollectionView（フィルタを共有しない）。
    /// </summary>
    public ICollectionView NetworkAdvancedItems { get; }

    public MainViewModel(
        DialogService dialogs,
        ThemeService theme,
        ExternalLauncherService launcher,
        EnvironmentInfoService envInfo,
        BackupService backup,
        RestorePointService restore,
        SystemProtectionService systemProtection,
        MemoryCompressionService memoryCompression,
        RemoteDesktopService remoteDesktop,
        SysmainService sysmain,
        WslService wsl,
        CoreIsolationService coreIsolation,
        HyperVService hyperV,
        VirtualMemoryService virtualMemory,
        SystemSoundService systemSound,
        RegistryService registry,
        StartupListViewModel startupListVM,
        NetworkListViewModel networkListVM,
        GraphicsListViewModel graphicsListVM)
    {
        _dialogs = dialogs;
        _theme = theme;
        _launcher = launcher;
        _envInfo = envInfo;
        _backup = backup;
        _restore = restore;
        _systemProtection = systemProtection;
        _memoryCompression = memoryCompression;
        _remoteDesktop = remoteDesktop;
        _sysmain = sysmain;
        _wsl = wsl;
        _coreIsolation = coreIsolation;
        _hyperV = hyperV;
        _virtualMemory = virtualMemory;
        _systemSound = systemSound;
        _registry = registry;
        _launcherForCommands = launcher;
        StartupListVM = startupListVM;
        NetworkListVM = networkListVM;
        GraphicsListVM = graphicsListVM;

        // v0.1.19：ON 退避の有無を起動時に 1 回読む（「元に戻す」メニューの初期活性判定）。
        _hasRdpBackup = _backup.HasMaintenanceOriginal(RemoteDesktopService.FeatureKey);

        // v0.1.20：メニュー常設の「現在の状態」ラベルを起動時に非同期で1回更新（ReadStatus は FW 読取で
        // powershell.exe を起動するため UI スレッドをブロックしない・コンストラクタは UI スレッド＝await 後 UI へ戻る）。
        _ = RefreshRemoteDesktopStatusLabelAsync();

        // v0.2.1 Step5：Hyper-V 退避有無＋状態ラベル（RDP と同パターン）。
        _hasHyperVBackup = _backup.HasMaintenanceOriginal(HyperVService.FeatureKey);
        _ = RefreshHyperVStatusLabelAsync();

        // v0.3.0：仮想メモリ退避有無＋状態ラベル（HyperV と同パターン）。
        _hasVmBackup = _backup.HasMaintenanceOriginal(VirtualMemoryService.FeatureKey);
        _ = RefreshVirtualMemoryStatusLabelAsync();

        // テーマ初期化＋変更追従（2026-05-16：MenuItem IsChecked 連動）
        _currentThemeMode = _theme.CurrentMode;
        _theme.ThemeChanged += mode => CurrentThemeMode = mode;

        InitializeCategories();
        InitializeSettingItems();

        FilteredItems = CollectionViewSource.GetDefaultView(Items);
        FilteredItems.Filter = FilterItem;

        // 専用ペイン「▸ くわしい設定」用：ネットワーク・カテゴリの既存項目だけを表示する独立ビュー。
        // FilteredItems（GetDefaultView）と共有すると互いのフィルタが干渉するため、別 CollectionView を作る。
        NetworkAdvancedItems = new CollectionViewSource { Source = Items }.View;
        NetworkAdvancedItems.Filter = o => o is SettingItem si && si.Category == CategoryType.Network;
    }

    private bool FilterItem(object obj)
    {
        if (obj is not SettingItem item) return false;

        // カテゴリフィルタ（ホーム・お気に入り・検索結果は全件表示）
        if (SelectedCategory != null
            && SelectedCategory.Type != CategoryType.Home
            && SelectedCategory.Type != CategoryType.Favorites
            && SelectedCategory.Type != CategoryType.SearchResults
            && item.Category != SelectedCategory.Type)
        {
            return false;
        }

        // 検索フィルタ（タイトル＋説明 部分一致）
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText.Trim();
            if (item.Title.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0
                && item.Description.IndexOf(q, StringComparison.OrdinalIgnoreCase) < 0)
                return false;
        }

        return true;
    }

    partial void OnSelectedCategoryChanged(SettingCategory? value)
    {
        FilteredItems?.Refresh();

        // スタートアップ系／ネットワーク・カテゴリへの切替検出（設計書 5-1 / v0.1.7 致命2 反映）
        var isStartup = value?.Type == CategoryType.Startup;
        var isNetwork = value?.Type == CategoryType.Network;
        var isGraphics = value?.Type == CategoryType.Graphics;
        IsStartupCategorySelected = isStartup;
        IsNetworkCategorySelected = isNetwork;
        IsGraphicsCategorySelected = isGraphics;
        // 既存 DataGrid は「Startup でも Network でも Graphics でもない」時だけ表示する（4 者択一・v0.3.0）。
        IsStandardCategorySelected = !(isStartup || isNetwork || isGraphics);

        if (isStartup)
        {
            // R-2 plan-validator 依存関係指摘：IsDirty 編集が残っている時は Load しない
            //   問答無用 Load だと「カテゴリ切替で編集消失」の致命 UX バグになる
            if (StartupListVM.HasDirtyEntries)
            {
                StatusMessage = $"カテゴリ「{value!.Name}」を選択中。⚠️ 編集中のエントリがあります（💾保存 または ↺戻す で確定してください）。";
                return;
            }
            // Startup に切り替わるたびに読み直し（regedit 外部変更が反映されるように）
            StartupListVM.Load();
        }
        else if (isNetwork)
        {
            // ネットワークペインの状態表示を更新（Phase 3/4 で実値読込）
            NetworkListVM.Load();
        }
        else if (isGraphics)
        {
            // グラフィックペインの状態表示を更新（v0.3.0・async void＋Task.Run）
            GraphicsListVM.Load();
        }

        StatusMessage = value == null
            ? "カテゴリを選択してください。"
            : $"カテゴリ「{value.Name}」を選択中。";
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredItems?.Refresh();

        // Startup ペイン時は StartupListVM の検索にも転送（設計書 9 リスク表 #7）
        if (IsStartupCategorySelected)
        {
            StartupListVM.SearchText = value;
        }
    }

    partial void OnSelectedItemChanged(SettingItem? value)
    {
        if (value == null) return;
        StatusMessage = $"{value.RiskMark} {value.Title} を選択中。";
    }

    // ====================== コマンド ======================

    /// <summary>DataGrid ダブルクリック・Enter で発火。</summary>
    [RelayCommand]
    private void OpenSelectedItem()
    {
        if (SelectedItem == null) return;
        _dialogs.OpenSettingItem(SelectedItem);
        FilteredItems?.Refresh();
    }

    // -------- ファイルメニュー --------
    [RelayCommand]
    private void ExitApp()
    {
        Application.Current.Shutdown();
    }

    // -------- 表示メニュー（テーマ） --------
    [RelayCommand] private void ApplyLight()        { _theme.Apply(ThemeMode.Light);  CurrentThemeMode = ThemeMode.Light;  StatusMessage = "🌞 ライトテーマに切替"; }
    [RelayCommand] private void ApplyDark()         { _theme.Apply(ThemeMode.Dark);   CurrentThemeMode = ThemeMode.Dark;   StatusMessage = "🌙 ダークテーマに切替"; }
    [RelayCommand] private void ApplySystemTheme()  { _theme.Apply(ThemeMode.System); CurrentThemeMode = ThemeMode.System; StatusMessage = "🖥️ システム連動テーマ"; }

    [RelayCommand]
    private void Refresh()
    {
        // Startup ペイン時はスタートアップ項目を再読込（設計書 9 リスク表 #8）
        if (IsStartupCategorySelected)
        {
            // R-2 plan-validator 依存関係指摘：F5 でも IsDirty 編集を破棄しない
            if (StartupListVM.HasDirtyEntries)
            {
                var ok = _dialogs.Confirm(
                    "編集中のエントリがあります。\n再読込すると編集内容が失われます。\n\nそれでも再読込しますか？",
                    "編集破棄の確認");
                if (!ok)
                {
                    StatusMessage = "🔄 再読込をキャンセルしました。";
                    return;
                }
            }
            StartupListVM.Load();
            StatusMessage = "🔄 スタートアップ項目を最新の状態に更新しました。";
            return;
        }

        FilteredItems?.Refresh();
        StatusMessage = "🔄 最新の状態に更新しました。";
    }

    // -------- ツールメニュー（外部起動） --------
    [RelayCommand] private void OpenRegistryEditor() { _launcher.OpenRegistryEditor(); StatusMessage = "regedit を起動しました。"; }
    [RelayCommand] private void OpenControlPanel()   { _launcher.OpenControlPanel();   StatusMessage = "コントロールパネルを起動しました。"; }
    [RelayCommand] private void OpenMsConfig()       { _launcher.OpenMsConfig();       StatusMessage = "msconfig を起動しました。"; }
    [RelayCommand] private void OpenGodMode()        { _launcher.OpenGodMode();        StatusMessage = "GodMode を開きました。"; }

    /// <summary>
    /// 2026-05-20 cross-reviewer P-3：UI スレッドブロック回避のため async 化。
    /// RelayCommand は async Task をサポート（CommunityToolkit.Mvvm）。
    /// </summary>
    [RelayCommand]
    private async Task RestartExplorer()
    {
        await _launcher.RestartExplorerAsync(() => _dialogs.Confirm(
            "エクスプローラを再起動します。\n開いているフォルダウィンドウは閉じてください。\n\n続行しますか？",
            "シェル再起動"));
        StatusMessage = "✅ エクスプローラ再起動を実行。";
    }

    [RelayCommand]
    private void Logoff()
    {
        if (!_dialogs.Confirm("ログオフします。続行しますか？", "ログオフ")) return;
        _launcher.Logoff();
    }

    [RelayCommand]
    private void Restart()
    {
        if (!_dialogs.Confirm(
            "30秒後にWindowsを再起動します。続行しますか？\n（取り消しは「ファイル」→「シャットダウン取消」または shutdown /a）",
            "再起動")) return;
        _launcher.Restart();
        StatusMessage = "⏳ 30秒後に再起動予定";
    }

    [RelayCommand]
    private void Shutdown()
    {
        if (!_dialogs.Confirm(
            "30秒後にWindowsをシャットダウンします。続行しますか？\n（取り消しは shutdown /a）",
            "シャットダウン")) return;
        _launcher.Shutdown();
        StatusMessage = "⏳ 30秒後にシャットダウン予定";
    }

    // -------- ツールメニュー（バックアップ・復元ポイント） --------
    [RelayCommand]
    private void OpenBackupFolder()
    {
        _launcher.OpenUrl(_backup.BackupDirectory);
        StatusMessage = $"📦 バックアップフォルダ: {_backup.BackupDirectory}";
    }

    /// <summary>
    /// 復元ポイント作成が実行中か（同型ハードニング 2026-06-09・SystemProtection 重要②と同手法）。
    /// true の間は作成コマンドを CanExecute=false で封じ、PowerShell（Checkpoint-Computer）の二重起動を防ぐ。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateRestorePointCommand))]
    private bool _isCreatingRestorePoint;

    private bool CanCreateRestorePoint() => !IsCreatingRestorePoint;

    [RelayCommand(CanExecute = nameof(CanCreateRestorePoint))]
    private async Task CreateRestorePoint()
    {
        var ok = _dialogs.Confirm(
            "システムの復元ポイントを作成します。\n（管理者権限が必要・24時間に1回まで）\n\n続行しますか？",
            "復元ポイント");
        if (!ok) return;

        IsCreatingRestorePoint = true;
        StatusMessage = "⏳ 復元ポイントを作成しています…";
        try
        {
            // UI スレッドを最大 60 秒ブロックしないよう別スレッドで実行（同型ハードニング・SystemProtection 重要②と同手法）。
            // out 引数はラムダ内ローカルで受け、タプルで返す。
            var (result, err) = await Task.Run(() =>
            {
                var r = _restore.Create("WinKnobs Manual Checkpoint", out var e);
                return (r, e);
            });

            switch (result)
            {
                case RestorePointResult.Success:
                    _dialogs.Info("✅ 復元ポイントを作成しました。", "成功");
                    StatusMessage = "✅ 復元ポイント作成完了";
                    break;
                case RestorePointResult.FailedFrequency:
                    _dialogs.Warn(
                        "24時間以内に既に復元ポイントが作成されています。\n（Windowsの仕様で同日内に1回のみ）",
                        "24時間制限");
                    StatusMessage = "⚠️ 24時間制限のためスキップ";
                    break;
                case RestorePointResult.FailedAccessDenied:
                    _dialogs.Warn("管理者権限が必要です。", "権限不足");
                    StatusMessage = "❌ 権限不足";
                    break;
                case RestorePointResult.FailedProtectionDisabled:
                    _dialogs.Warn(
                        "システム保護が無効化されています。\nコントロールパネル → 回復 → システムの復元の構成 で有効化できます。",
                        "システム保護無効");
                    StatusMessage = "❌ システム保護無効";
                    break;
                default:
                    // err が空（無音で異常終了）でも「作成失敗: 」だけにならないようフォールバック（SystemProtection 軽微④と同手法）。
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"作成失敗: {detail}", "失敗");
                    StatusMessage = $"❌ {detail}";
                    break;
            }
        }
        finally
        {
            IsCreatingRestorePoint = false;
        }
    }

    // -------- システムの保護（システムの復元）ON/OFF：v0.1.15 システム・メンテナンス Phase 2 --------
    // 既存「復元ポイントを作成」(CreateRestorePoint) と同じ作法（PowerShell 管理者・結果ダイアログ）。
    // 実行は SystemProtectionService（Enable/Disable-ComputerRestore）。最終確認は VM 実機検証（管理者操作）。

    /// <summary>
    /// システムの保護の切替が実行中か（v0.1.16 cross-reviewer 重要②）。
    /// true の間は Enable/Disable 両コマンドを CanExecute=false で封じ、PowerShell の二重起動を防ぐ。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableSystemProtectionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableSystemProtectionCommand))]
    private bool _isSystemProtectionBusy;

    private bool CanToggleSystemProtection() => !IsSystemProtectionBusy;

    [RelayCommand(CanExecute = nameof(CanToggleSystemProtection))]
    private Task EnableSystemProtection() => ToggleSystemProtection(true);

    [RelayCommand(CanExecute = nameof(CanToggleSystemProtection))]
    private Task DisableSystemProtection() => ToggleSystemProtection(false);

    private async Task ToggleSystemProtection(bool enable)
    {
        var verb = enable ? "有効" : "無効";
        var msg = enable
            ? "システムの保護（システムの復元）を C ドライブで有効にします。\n（管理者権限が必要）\n\n有効にすると復元ポイントを作成できるようになります。\n続行しますか？"
            : "システムの保護（システムの復元）を C ドライブで無効にします。\n（管理者権限が必要）\n\n⚠️ 無効にすると、既存の復元ポイントが削除され、新しい復元ポイントを作成できなくなります。\n続行しますか？";
        // 無効化＝既存の復元ポイント全削除（不可逆）。既定ボタン「いいえ」の ConfirmDestructive で
        // Enter 連打等の誤操作による誤爆を防ぐ（v0.1.16 cross-reviewer 重要①・設計ドラフト v0.1.14 の要件）。
        var ok = enable
            ? _dialogs.Confirm(msg, $"システムの保護を{verb}化")
            : _dialogs.ConfirmDestructive(msg, $"システムの保護を{verb}化");
        if (!ok) return;

        IsSystemProtectionBusy = true;
        StatusMessage = $"⏳ システムの保護を{verb}化しています…";
        try
        {
            // UI スレッドを最大 60 秒ブロックしないよう別スレッドで実行（v0.1.16 cross-reviewer 重要②）。
            // out 引数はラムダ内ローカルで受け、タプルで返す。
            var (result, err) = await Task.Run(() =>
            {
                var r = _systemProtection.SetProtection(enable, out var e);
                return (r, e);
            });

            switch (result)
            {
                case SystemProtectionOutcome.Success:
                    _dialogs.Info($"✅ システムの保護を{verb}にしました。", "成功");
                    StatusMessage = $"✅ システムの保護を{verb}化";
                    break;
                case SystemProtectionOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    // err が空（無音で異常終了）でも「失敗しました：」だけにならないようフォールバック（cross-reviewer 軽微④）。
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"システムの保護の{verb}化に失敗しました：\n{detail}", "失敗");
                    StatusMessage = $"❌ {verb}化失敗";
                    break;
            }
        }
        finally
        {
            IsSystemProtectionBusy = false;
        }
    }

    // -------- メモリ圧縮 ON/OFF：v0.1.17 システム・メンテナンス Phase 3 --------
    // システムの保護(ToggleSystemProtection) と同型（PowerShell 管理者・別スレッド・結果ダイアログ）。
    // 実行は MemoryCompressionService（Enable/Disable-MMAgent -mc）。最終確認は VM 実機検証（管理者操作）。
    // メモリ圧縮は有効化/無効化とも可逆（データ消失なし）＝両方とも通常 Confirm（ConfirmDestructive 不要）。

    /// <summary>
    /// メモリ圧縮の切替が実行中か。true の間は Enable/Disable 両コマンドを CanExecute=false で封じ、
    /// PowerShell の二重起動を防ぐ（SystemProtection 同様）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableMemoryCompressionCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableMemoryCompressionCommand))]
    private bool _isMemoryCompressionBusy;

    private bool CanToggleMemoryCompression() => !IsMemoryCompressionBusy;

    [RelayCommand(CanExecute = nameof(CanToggleMemoryCompression))]
    private Task EnableMemoryCompression() => ToggleMemoryCompression(true);

    [RelayCommand(CanExecute = nameof(CanToggleMemoryCompression))]
    private Task DisableMemoryCompression() => ToggleMemoryCompression(false);

    private async Task ToggleMemoryCompression(bool enable)
    {
        var verb = enable ? "有効" : "無効";
        // 中立説明（一律推奨しない・feedback_no_text_scaling 同様の中立方針）。
        var msg = enable
            ? "メモリ圧縮を有効にします。\n（管理者権限が必要）\n\nメモリを圧縮して空きを増やす機能です。\n続行しますか？"
            : "メモリ圧縮を無効にします。\n（管理者権限が必要）\n\nRAM の大きい PC では切ると CPU 負荷が減ることがあります。\nいつでも有効に戻せます。\n続行しますか？";
        // 有効化/無効化とも可逆（再トグルで戻せる・データ消失なし）＝通常 Confirm でよい。
        var ok = _dialogs.Confirm(msg, $"メモリ圧縮を{verb}化");
        if (!ok) return;

        IsMemoryCompressionBusy = true;
        StatusMessage = $"⏳ メモリ圧縮を{verb}化しています…";
        try
        {
            // UI スレッドを最大 60 秒ブロックしないよう別スレッドで実行（SystemProtection 同様）。
            var (result, err) = await Task.Run(() =>
            {
                var r = _memoryCompression.SetCompression(enable, out var e);
                return (r, e);
            });

            switch (result)
            {
                case MemoryCompressionOutcome.Success:
                    _dialogs.Info($"✅ メモリ圧縮を{verb}にしました。", "成功");
                    StatusMessage = $"✅ メモリ圧縮を{verb}化";
                    break;
                case MemoryCompressionOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"メモリ圧縮の{verb}化に失敗しました：\n{detail}", "失敗");
                    StatusMessage = $"❌ {verb}化失敗";
                    break;
            }
        }
        finally
        {
            IsMemoryCompressionBusy = false;
        }
    }

    // -------- Sysmain サービス ON/OFF：v0.2.0 Quick Win --------
    // メモリ圧縮(ToggleMemoryCompression) と同型（PowerShell 管理者・別スレッド・結果ダイアログ）。
    // 実行は SysmainService（Set-Service / Stop-Service / Start-Service）。最終確認は VM 実機検証（管理者操作）。
    // 有効化/無効化とも可逆（再トグルで戻る・データ消失なし）＝両方とも通常 Confirm。
    // 第4分類 ServiceNotFound（SysMain 不在エディション）を AccessDenied と区別して案内（plan-validator 指摘5）。

    /// <summary>
    /// Sysmain 切替が実行中か。true の間は Enable/Disable 両コマンドを CanExecute=false で封じ、
    /// PowerShell の二重起動を防ぐ（SystemProtection / MemoryCompression 同様）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableSysmainCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableSysmainCommand))]
    private bool _isSysmainBusy;

    private bool CanToggleSysmain() => !IsSysmainBusy;

    [RelayCommand(CanExecute = nameof(CanToggleSysmain))]
    private Task EnableSysmain() => ToggleSysmain(true);

    [RelayCommand(CanExecute = nameof(CanToggleSysmain))]
    private Task DisableSysmain() => ToggleSysmain(false);

    private async Task ToggleSysmain(bool enable)
    {
        var verb = enable ? "有効" : "無効";
        // 中立説明（一律推奨しない）。HDD では無効化でカクつき軽減の報告・SSD では既定 ON のままが無難。
        var msg = enable
            ? "Sysmain サービスを有効にします。\n（管理者権限が必要）\n\nよく使うアプリを先読みして起動を速くする機能です（旧 Superfetch）。\n続行しますか？"
            : "Sysmain サービスを無効にします。\n（管理者権限が必要）\n\nHDD ではディスク負荷が下がりカクつきが軽くなる報告があります。\nSSD では既定の有効のままが無難です。いつでも有効に戻せます。\n続行しますか？";
        var ok = _dialogs.Confirm(msg, $"Sysmain を{verb}化");
        if (!ok) return;

        IsSysmainBusy = true;
        StatusMessage = $"⏳ Sysmain を{verb}化しています…";
        try
        {
            // UI スレッドを最大 60 秒ブロックしないよう別スレッドで実行（SystemProtection / MemoryCompression 同様）。
            var (result, err) = await Task.Run(() =>
            {
                var r = _sysmain.SetSysmain(enable, out var e);
                return (r, e);
            });

            switch (result)
            {
                case SysmainOutcome.Success:
                    _dialogs.Info($"✅ Sysmain を{verb}にしました。", "成功");
                    StatusMessage = $"✅ Sysmain を{verb}化";
                    break;
                case SysmainOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                case SysmainOutcome.ServiceNotFound:
                    _dialogs.Warn(
                        "この PC には SysMain サービスが見つかりませんでした。\nWindows のエディションや構成によっては存在しません。変更は行われていません。",
                        "サービスなし");
                    StatusMessage = "❌ SysMain サービスが見つかりません";
                    break;
                default:
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"Sysmain の{verb}化に失敗しました：\n{detail}", "失敗");
                    StatusMessage = $"❌ {verb}化失敗";
                    break;
            }
        }
        finally
        {
            IsSysmainBusy = false;
        }
    }

    // -------- WSL 2 機能 ON/OFF：v0.2.1 Step3 --------
    // Sysmain(ToggleSysmain) と同型（PowerShell 管理者・別スレッド・結果ダイアログ）＋以下の差分：
    //   - 有効化は2機能（VirtualMachinePlatform→WSL本体）の done-stack（WslService 内・片方失敗で逆順ロールバック）
    //   - 無効化は WSL 本体のみ（VirtualMachinePlatform は WSA 等の巻き添え防止で残す）
    //   - 反映は再起動後（-NoRestart・再起動はユーザー判断）
    //   - Busy は共通の IsSystemFeatureBusy（plan-validator エッジ3＝DISM グローバル mutex 衝突防止。
    //     Step4 コア分離 / Step5 Hyper-V も同じ Busy に乗せて再起動系メンテを直列化する）

    /// <summary>
    /// 再起動系メンテ機能（WSL / コア分離 / Hyper-V）の共通 Busy。
    /// DISM・bcdedit 等のグローバルロック衝突を防ぐため、これら全コマンドを1本で直列化する。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(EnableWslCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableWslCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableCoreIsolationCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableCoreIsolationCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableHyperVCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisableHyperVCommand))]
    [NotifyCanExecuteChangedFor(nameof(RestoreHyperVCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenVirtualMemoryCommand))]      // v0.3.0：仮想メモリも同じ直列化（致命2）
    [NotifyCanExecuteChangedFor(nameof(RestoreVirtualMemoryCommand))]
    private bool _isSystemFeatureBusy;

    private bool CanToggleSystemFeature() => !IsSystemFeatureBusy;

    [RelayCommand(CanExecute = nameof(CanToggleSystemFeature))]
    private Task EnableWsl() => ToggleWsl(true);

    [RelayCommand(CanExecute = nameof(CanToggleSystemFeature))]
    private Task DisableWsl() => ToggleWsl(false);

    private async Task ToggleWsl(bool enable)
    {
        var verb = enable ? "有効" : "無効";
        // 中立説明（一律推奨しない）。無効化でもディストリビューションのデータは消えない（再有効化で戻る）。
        var msg = enable
            ? "WSL（Windows Subsystem for Linux）を有効にします。\n（管理者権限が必要・反映には再起動が必要）\n\nWSL 本体と実行基盤（仮想マシン プラットフォーム）の2つの Windows 機能を有効化します。\n処理に数分かかる場合があります。\n続行しますか？"
            : "WSL（Windows Subsystem for Linux）を無効にします。\n（管理者権限が必要・反映には再起動が必要）\n\nLinux ディストリビューションのデータ（ext4.vhdx）は削除されません。再有効化すれば戻ります。\n仮想マシン プラットフォームは他機能（WSA 等）が使うため残します。\n続行しますか？";
        var ok = _dialogs.Confirm(msg, $"WSL を{verb}化");
        if (!ok) return;

        IsSystemFeatureBusy = true;
        StatusMessage = $"⏳ WSL を{verb}化しています…（数分かかる場合があります）";
        try
        {
            // DISM 系は最大 300 秒 × 複数ステップ。UI スレッドをブロックしないよう別スレッドで実行。
            var (result, err) = await Task.Run(() =>
            {
                string? e;
                var r = enable ? _wsl.Enable(out e) : _wsl.Disable(out e);
                return (r, e);
            });

            switch (result)
            {
                case WslOutcome.Success:
                    _dialogs.Info($"✅ WSL を{verb}にしました。\n反映には PC の再起動が必要です（他の変更とまとめて1回の再起動で OK）。", "成功");
                    StatusMessage = $"✅ WSL を{verb}化（再起動で反映）";
                    break;
                case WslOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                case WslOutcome.FeatureNotFound:
                    _dialogs.Warn(
                        "この Windows には WSL の機能が見つかりませんでした。\nエディションやビルドによっては利用できません。変更は行われていません。",
                        "機能なし");
                    StatusMessage = "❌ WSL 機能が見つかりません";
                    break;
                case WslOutcome.PartiallyEnabled:
                    _dialogs.Error(
                        "⚠️ 2つの機能のうち片方だけが有効化された状態で止まりました（巻き戻しにも失敗）。\n" +
                        "「Windows の機能の有効化または無効化」（OptionalFeatures.exe）で\n" +
                        "『Linux 用 Windows サブシステム』と『仮想マシン プラットフォーム』の状態を確認してください。\n\n詳細：\n" +
                        (string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err),
                        "中途半端な状態");
                    StatusMessage = "⚠️ WSL が中途半端な状態（要手動確認）";
                    break;
                default:
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"WSL の{verb}化に失敗しました：\n{detail}", "失敗");
                    StatusMessage = $"❌ {verb}化失敗";
                    break;
            }
        }
        finally
        {
            IsSystemFeatureBusy = false;
        }
    }

    // -------- コア分離（メモリ整合性・HVCI）ON/OFF：v0.2.1 Step4（🔴Red） --------
    // レジストリ DWORD 直接書込（CoreIsolationService・要管理者・再起動で確定）。
    //   - OFF＝セキュリティ後退方向（ドライバ悪用攻撃への防壁を外す）→ ConfirmDestructive（既定いいえ）。
    //   - ON も互換性リスクあり（非対応ドライバで機器の誤動作・まれに起動不能）→ 通常 Confirm に注意書き。
    //   - GPO/MDM 管理下はグレーアウト（RDP CanEnableRemoteDesktop 同型・起動時1回キャッシュ）。
    //   - UEFI ロック環境では書いても戻る場合あり → 成功と言い切らず「再起動後に Windows セキュリティで確認」。
    //   - Busy は WSL と共通の IsSystemFeatureBusy（再起動系メンテ直列化・plan-validator エッジ3）。

    /// <summary>HVCI が GPO/MDM 管理下か（起動時1回キャッシュ・RdpHostCapable 同パターン）。</summary>
    private bool? _coreIsolationPolicyManagedCache;

    private bool CoreIsolationPolicyManaged()
    {
        _coreIsolationPolicyManagedCache ??= _coreIsolation.IsPolicyManaged();
        return _coreIsolationPolicyManagedCache.Value;
    }

    private bool CanToggleCoreIsolation() => !IsSystemFeatureBusy && !CoreIsolationPolicyManaged();

    [RelayCommand(CanExecute = nameof(CanToggleCoreIsolation))]
    private Task EnableCoreIsolation() => ToggleCoreIsolation(true);

    [RelayCommand(CanExecute = nameof(CanToggleCoreIsolation))]
    private Task DisableCoreIsolation() => ToggleCoreIsolation(false);

    private async Task ToggleCoreIsolation(bool enable)
    {
        var verb = enable ? "有効" : "無効";
        var current = _coreIsolation.ReadEnabled();
        var currentLabel = CoreIsolationHelper.DescribeState(current);

        bool ok;
        if (enable)
        {
            // ON 方向：セキュリティ強化だが互換性リスクあり（非対応ドライバ）→ 通常 Confirm＋注意書き
            ok = _dialogs.Confirm(
                $"コア分離（メモリ整合性）を有効にします。\n（管理者権限が必要・反映には再起動が必要）\n\n現在の状態：{currentLabel}\n\n" +
                "悪意あるドライバからシステムを守るセキュリティ機能です。\n" +
                "⚠️ 古い周辺機器のドライバが非対応だと、その機器が動かなくなる場合があります。\n続行しますか？",
                "コア分離を有効化");
        }
        else
        {
            // OFF 方向：防壁を外す（🔴）→ ConfirmDestructive（既定いいえ）
            ok = _dialogs.ConfirmDestructive(
                $"🔴 コア分離（メモリ整合性）を無効にします。\n（管理者権限が必要・反映には再起動が必要）\n\n現在の状態：{currentLabel}\n\n" +
                "ドライバの悪用攻撃からシステムを守る防壁を外します。\n" +
                "ゲームの互換性・性能目的の一時的な OFF を想定した機能です。用が済んだら有効に戻すことを強くおすすめします。\n\n本当に無効にしますか？",
                "⚠️ コア分離を無効化");
        }
        if (!ok) return;

        IsSystemFeatureBusy = true;
        StatusMessage = $"⏳ コア分離を{verb}化しています…";
        try
        {
            // レジストリ書込は速いが、Busy 直列化（WSL/Hyper-V と同枠）に乗せて再起動系メンテの同時実行を防ぐ。
            var (result, err) = await Task.Run(() =>
            {
                var r = _coreIsolation.SetEnabled(enable, out var e);
                return (r, e);
            });

            switch (result)
            {
                case CoreIsolationOutcome.Success:
                    // UEFI ロック環境では戻る場合がある＝「設定しました」止まりで言い切らない（設計ドラフト §2-5）
                    _dialogs.Info(
                        $"コア分離（メモリ整合性）を{verb}に設定しました。\n反映には PC の再起動が必要です（他の変更とまとめて1回の再起動で OK）。\n\n" +
                        "再起動後に「Windows セキュリティ > デバイス セキュリティ > コア分離」で実際の状態を確認してください\n（UEFI で保護された PC では設定が元に戻ることがあります）。",
                        "設定完了（再起動で反映）");
                    StatusMessage = $"✅ コア分離を{verb}化（再起動で反映）";
                    break;
                case CoreIsolationOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error($"コア分離の{verb}化に失敗しました：\n{detail}", "失敗");
                    StatusMessage = $"❌ {verb}化失敗";
                    break;
            }
        }
        finally
        {
            IsSystemFeatureBusy = false;
        }
    }

    // -------- Hyper-V（ハイパーバイザー起動）ON/OFF：v0.2.1 Step5（🔴Red） --------
    // bcdedit /set hypervisorlaunchtype off|auto（HyperVService・PowerShell 経由・要管理者・再起動で確定）。
    //   - OFF＝🔴 専用 Red 強確認ダイアログ（連鎖影響列挙＋コア分離依存ガイド＋BitLocker 警告＝plan-validator 致命2）。
    //   - ON（auto）＝復旧方向 → 通常 Confirm。
    //   - 適用前 first-touch 退避（maintenance ストア）＋「元に戻す」メニュー（RDP Restore 同型）。
    //   - メニュー常設の状態ラベル（v0.1.20 世代カウンタパターン流用）。
    //   - Busy は WSL/コア分離と共通の IsSystemFeatureBusy（再起動系メンテ直列化・plan-validator エッジ3）。

    /// <summary>メニュー常設の「現在の状態」ラベル（hypervisorlaunchtype・読み取り専用 MenuItem に表示）。</summary>
    [ObservableProperty]
    private string _hyperVStatusLabel = "現在の状態：確認中…";

    /// <summary>ラベル更新の世代番号（RDP _rdpStatusLabelGeneration と同じレース対策・最新だけが勝つ）。</summary>
    private int _hyperVStatusLabelGeneration;

    /// <summary>
    /// 現在の hypervisorlaunchtype を読み直してメニュー常設ラベルへ反映する。
    /// ReadLaunchType は powershell.exe（bcdedit）を起動する＝別スレッド実行で UI を止めない。
    /// </summary>
    private async Task RefreshHyperVStatusLabelAsync()
    {
        var gen = ++_hyperVStatusLabelGeneration; // UI スレッドで採番（呼び出し元は全て UI スレッド）
        string label;
        try
        {
            var (type, raw) = await Task.Run(() => _hyperV.ReadLaunchType());
            label = HyperVHelper.DescribeMenuStatusLabel(type, raw);
        }
        catch
        {
            label = HyperVHelper.DescribeMenuStatusLabel(HyperVLaunchType.Unknown, null);
        }

        if (gen == _hyperVStatusLabelGeneration)
            HyperVStatusLabel = label;
    }

    /// <summary>Hyper-V 退避（first-touch）が存在するか＝「元に戻す」の活性（HasRdpBackup 同パターン）。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreHyperVCommand))]
    private bool _hasHyperVBackup;

    /// <summary>退避の有無を 1 回だけ読み直して <see cref="HasHyperVBackup"/> に反映（ユーザー操作ごと＝ホットパス外）。</summary>
    private void RefreshHyperVBackupState()
    {
        try { HasHyperVBackup = _backup.HasMaintenanceOriginal(HyperVService.FeatureKey); }
        catch { /* 読めない時は据え置き */ }
    }

    /// <summary>
    /// Hyper-V を有効に戻す（hypervisorlaunchtype auto・復旧方向＝通常 Confirm）。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleSystemFeature))]
    private async Task EnableHyperV()
    {
        var ok = _dialogs.Confirm(
            "Hyper-V（ハイパーバイザー）を有効に戻します。\n（管理者権限が必要・反映には再起動が必要）\n\n" +
            "WSL2・Windows サンドボックス・Hyper-V 仮想マシンなどが再び使えるようになります。\n" +
            "※ LDPlayer 等のエミュレータは再び遅くなる/動かなくなる場合があります。\n続行しますか？",
            "Hyper-V を有効化");
        if (!ok) return;

        await ApplyHyperV(autoOn: true);
    }

    /// <summary>
    /// Hyper-V を無効にする（hypervisorlaunchtype off・🔴 専用 Red 強確認ダイアログ）。
    /// スレッド順序は RDP Enable と同じ（plan-validator #3）：
    ///   ① UI で状態読取 → ② UI で Red ダイアログ ShowDialog → ③ 同意時のみ Task.Run(適用) → ④ UI で結果。
    ///   Busy はダイアログ表示中から立てる。
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanToggleSystemFeature))]
    private async Task DisableHyperV()
    {
        IsSystemFeatureBusy = true;
        try
        {
            // ① 現在状態（launchtype＋コア分離）を読む。launchtype の読取は子プロセス＝別スレッド。
            string currentLabel;
            try
            {
                var (type, raw) = await Task.Run(() => _hyperV.ReadLaunchType());
                currentLabel = HyperVHelper.DescribeMenuStatusLabel(type, raw);

                // 既に Off なら no-op を案内して終了（ダイアログまで進めない）
                if (type == HyperVLaunchType.Off)
                {
                    _dialogs.Info("ハイパーバイザー起動は既に Off に設定されています。\n（反映には再起動が必要です）", "Hyper-V");
                    return;
                }
            }
            catch (Exception ex)
            {
                _dialogs.Error($"現在の起動設定を読み取れませんでした：\n{ex.Message}", "Hyper-V");
                StatusMessage = "❌ 状態読取失敗";
                return;
            }

            // コア分離（HVCI）が ON のままだと off が効かない（plan-validator 致命2）→ ダイアログの依存ガイドへ
            var coreIsolationOn = _coreIsolation.ReadEnabled() == 1;

            // ② UI スレッドで Red 専用 強確認ダイアログ
            var consented = _dialogs.ConfirmHyperVDisable(currentLabel, coreIsolationOn);
            if (!consented)
            {
                StatusMessage = "Hyper-V の無効化をキャンセルしました。";
                return;
            }

            // ③ 同意時のみ適用
            await ApplyHyperVCore(autoOn: false);
        }
        finally
        {
            IsSystemFeatureBusy = false;
            RefreshHyperVBackupState();
            _ = RefreshHyperVStatusLabelAsync();
        }
    }

    /// <summary>有効化（auto）側の共通適用（Busy 確保＋後始末込み）。</summary>
    private async Task ApplyHyperV(bool autoOn)
    {
        IsSystemFeatureBusy = true;
        try
        {
            await ApplyHyperVCore(autoOn);
        }
        finally
        {
            IsSystemFeatureBusy = false;
            RefreshHyperVBackupState();
            _ = RefreshHyperVStatusLabelAsync();
        }
    }

    /// <summary>
    /// bcdedit 適用＋結果表示（Busy 管理は呼び出し側）。
    /// 成功文言は言い切らない（🔧反映2＝VBS 残存時は off が効かない／Secured-core では書いても戻る場合あり）。
    /// </summary>
    private async Task ApplyHyperVCore(bool autoOn)
    {
        var verb = autoOn ? "有効" : "無効";
        StatusMessage = $"⏳ Hyper-V を{verb}化しています…";

        var (result, err) = await Task.Run(() =>
        {
            var r = _hyperV.SetLaunchType(autoOn, out var e);
            return (r, e);
        });

        switch (result)
        {
            case HyperVOutcome.Success:
                var offNote = autoOn
                    ? ""
                    : "\n\n⚠️ コア分離（メモリ整合性）など仮想化ベースのセキュリティが有効なままの場合、\n" +
                      "再起動してもハイパーバイザーは停止しません（効果が出ません）。\n" +
                      "再起動後にツールメニューの状態表示と LDPlayer 等の動作で確認してください。";
                _dialogs.Info(
                    $"ハイパーバイザーの起動設定を変更しました（{(autoOn ? "auto" : "off")}）。\n" +
                    "反映には PC の再起動が必要です（他の変更とまとめて1回の再起動で OK）。" + offNote,
                    "設定完了（再起動で反映）");
                StatusMessage = $"✅ Hyper-V を{verb}化（再起動で反映）";
                break;
            case HyperVOutcome.AccessDenied:
                _dialogs.Warn(
                    "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                    "権限不足");
                StatusMessage = "❌ 権限不足（管理者が必要）";
                break;
            default:
                var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                _dialogs.Error($"Hyper-V の{verb}化に失敗しました：\n{detail}", "失敗");
                StatusMessage = $"❌ {verb}化失敗";
                break;
        }
    }

    private bool CanRestoreHyperV() => !IsSystemFeatureBusy && HasHyperVBackup;

    /// <summary>Hyper-V の起動設定を first-touch 退避へ復元する（RDP Restore 同型）。</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreHyperV))]
    private async Task RestoreHyperV()
    {
        var ok = _dialogs.Confirm(
            "ハイパーバイザーの起動設定を、WinKnobs で初めて変更する前の状態に戻します。\n（管理者権限が必要・反映には再起動が必要）\n\n続行しますか？",
            "Hyper-V を元に戻す");
        if (!ok) return;

        IsSystemFeatureBusy = true;
        StatusMessage = "⏳ Hyper-V の設定を元に戻しています…";
        try
        {
            var dict = _backup.LoadMaintenanceOriginals();
            if (!dict.TryGetValue(HyperVService.FeatureKey, out var json))
            {
                _dialogs.Info("戻せる退避情報が見つかりませんでした。", "元に戻す");
                StatusMessage = "退避情報なし";
                return;
            }

            string? err = null;
            var restored = await Task.Run(() =>
            {
                try { _hyperV.RestoreHyperV(json); return true; }
                catch (Exception ex) { err = ex.Message; return false; }
            });

            if (restored)
            {
                // 完全成功時のみ退避を後始末（失敗時は残して再試行可能に）。
                _backup.RemoveMaintenanceOriginal(HyperVService.FeatureKey);
                _dialogs.Info("✅ ハイパーバイザーの起動設定を、変更前の状態に戻しました。\n反映には PC の再起動が必要です。", "成功");
                StatusMessage = "✅ Hyper-V を元に戻しました（再起動で反映）";
            }
            else
            {
                var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                _dialogs.Error(
                    $"元に戻す処理に失敗しました：\n{detail}\n\n退避は残してあります。管理者で起動し直して、もう一度お試しください。",
                    "失敗");
                StatusMessage = "❌ 元に戻す失敗（退避は保持）";
            }
        }
        finally
        {
            IsSystemFeatureBusy = false;
            RefreshHyperVBackupState();
            _ = RefreshHyperVStatusLabelAsync();
        }
    }

    // -------- 仮想メモリ（ページファイル）：v0.3.0（🔴Red・要管理者・要再起動） --------
    // HyperV/RDP と同流儀：① UI で状態読取 → ② UI で Red ダイアログ → ③ 同意時のみ Task.Run(適用) → ④ UI で結果。
    // Busy は共通 IsSystemFeatureBusy（再起動系メンテ直列化）。

    /// <summary>メニュー常設の「現在の状態」ラベル（読み取り専用 MenuItem に表示）。</summary>
    [ObservableProperty]
    private string _virtualMemoryStatusLabel = "仮想メモリ：確認中…";

    private int _vmStatusLabelGeneration;

    /// <summary>仮想メモリの現在状態を読み直してメニュー常設ラベルへ反映（PowerShell 起動＝別スレッド）。</summary>
    private async Task RefreshVirtualMemoryStatusLabelAsync()
    {
        var gen = ++_vmStatusLabelGeneration;
        string label;
        try
        {
            var state = await Task.Run(() => _virtualMemory.ReadState());
            label = VirtualMemoryHelper.DescribeMenuStatusLabel(state);
        }
        catch
        {
            label = VirtualMemoryHelper.DescribeMenuStatusLabel(null);
        }
        if (gen == _vmStatusLabelGeneration)
            VirtualMemoryStatusLabel = label;
    }

    /// <summary>仮想メモリ退避（first-touch）が存在するか＝「元に戻す」の活性。</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreVirtualMemoryCommand))]
    private bool _hasVmBackup;

    private void RefreshVmBackupState()
    {
        try { HasVmBackup = _backup.HasMaintenanceOriginal(VirtualMemoryService.FeatureKey); }
        catch { /* 読めない時は据え置き */ }
    }

    /// <summary>Windows 純正の仮想メモリ設定（パフォーマンスオプション）を開く（開くだけ・安全弁）。</summary>
    [RelayCommand]
    private void OpenWindowsVirtualMemory()
    {
        if (_launcher.OpenPerformanceOptions())
            StatusMessage = "🪟 Windows のパフォーマンスオプションを開きました（詳細設定 → 仮想メモリ）。";
        else
            StatusMessage = "⚠️ パフォーマンスオプションを開けませんでした。";
    }

    /// <summary>仮想メモリ設定ダイアログ（🔴Red）を開いて適用する。</summary>
    [RelayCommand(CanExecute = nameof(CanToggleSystemFeature))]
    private async Task OpenVirtualMemory()
    {
        IsSystemFeatureBusy = true;
        try
        {
            // ① 現在状態を読む（PowerShell＝別スレッド）。
            VirtualMemoryState? state;
            try { state = await Task.Run(() => _virtualMemory.ReadState()); }
            catch { state = null; }

            // ② UI スレッドで Red ダイアログ（モード選択＋手動サイズ＋理解チェック）。
            var choice = _dialogs.ShowVirtualMemory(state);
            if (choice == null)
            {
                StatusMessage = "仮想メモリ設定の変更をキャンセルしました。";
                return;
            }

            // ③ 同意時のみ適用（モード別）。
            var mode = choice.SelectedMode;
            int init = 0, max = 0;
            if (mode == VmApplyMode.Manual && !choice.TryGetManualSizes(out init, out max))
            {
                _dialogs.Error("手動サイズの読み取りに失敗しました。", "失敗");
                return;
            }

            StatusMessage = "⏳ 仮想メモリ設定を変更しています…";
            var (outcome, msg) = await Task.Run(() =>
            {
                string? m;
                VmOutcome o = mode switch
                {
                    VmApplyMode.AutoManaged => _virtualMemory.ApplyAutoManaged(out m),
                    VmApplyMode.SystemManaged => _virtualMemory.ApplySystemManaged(out m),
                    _ => _virtualMemory.ApplyManual(init, max, out m),
                };
                return (o, m);
            });

            switch (outcome)
            {
                case VmOutcome.Success:
                    _dialogs.Info(
                        (msg ?? "変更しました。") + "\n\n他の変更とまとめて 1 回の再起動で反映できます。",
                        "設定完了（再起動で反映）");
                    StatusMessage = "✅ 仮想メモリ設定を変更（再起動で反映）";
                    break;
                case VmOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    _dialogs.Error(msg ?? "仮想メモリ設定の変更に失敗しました。", "失敗");
                    StatusMessage = "❌ 仮想メモリ変更失敗";
                    break;
            }
        }
        finally
        {
            IsSystemFeatureBusy = false;
            RefreshVmBackupState();
            _ = RefreshVirtualMemoryStatusLabelAsync();
        }
    }

    private bool CanRestoreVirtualMemory() => !IsSystemFeatureBusy && HasVmBackup;

    /// <summary>仮想メモリ設定を first-touch 退避へ復元する。</summary>
    [RelayCommand(CanExecute = nameof(CanRestoreVirtualMemory))]
    private async Task RestoreVirtualMemory()
    {
        var ok = _dialogs.Confirm(
            "仮想メモリ設定を、WinKnobs で初めて変更する前の状態に戻します。\n（管理者権限が必要・反映には再起動が必要）\n\n続行しますか？",
            "仮想メモリ設定を元に戻す");
        if (!ok) return;

        IsSystemFeatureBusy = true;
        StatusMessage = "⏳ 仮想メモリ設定を元に戻しています…";
        try
        {
            var (outcome, msg) = await Task.Run(() =>
            {
                var o = _virtualMemory.Restore(out var m);
                return (o, m);
            });

            switch (outcome)
            {
                case VmOutcome.Success:
                    _dialogs.Info(msg ?? "元に戻しました。", "成功");
                    StatusMessage = "✅ 仮想メモリ設定を元に戻しました（再起動で反映）";
                    break;
                case VmOutcome.AccessDenied:
                    _dialogs.Warn("管理者権限が必要です。", "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    _dialogs.Error(msg ?? "元に戻す処理に失敗しました。", "失敗");
                    StatusMessage = "❌ 元に戻す失敗（退避は保持）";
                    break;
            }
        }
        finally
        {
            IsSystemFeatureBusy = false;
            RefreshVmBackupState();
            _ = RefreshVirtualMemoryStatusLabelAsync();
        }
    }

    // -------- リモートデスクトップ 無効化：v0.1.18 システム・メンテナンス Phase 4（🔴Red） --------
    // メモリ圧縮(ToggleMemoryCompression) と同型（powershell 管理者・別スレッド・結果ダイアログ）。
    // 実行は RemoteDesktopService（FW 無効化＋fDeny=1・外→内・fail-closed）。
    // v0.1.18 は「無効化(OFF)」のみ＝攻撃面を閉じる安全方向。有効化(ON)は v0.1.19（強警告UI＋接続往復VM検証込み）。
    // 🔴 唯一の本物の締め出し経路＝「自分がリモート接続中に無効化」。必ず自己セッション判定で強警告（ConfirmDestructive 既定いいえ）。

    /// <summary>
    /// リモートデスクトップ無効化が実行中か。true の間は Disable コマンドを CanExecute=false で封じ、
    /// powershell の二重起動を防ぐ（SystemProtection / MemoryCompression 同様）。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisableRemoteDesktopCommand))]
    [NotifyCanExecuteChangedFor(nameof(EnableRemoteDesktopCommand))]   // v0.1.19：ON も同じ排他で封じる
    [NotifyCanExecuteChangedFor(nameof(RestoreRemoteDesktopCommand))]  // v0.1.19：元に戻すも実行中は封じる
    private bool _isRemoteDesktopBusy;

    // v0.1.20：Disable も Enable と同条件でグレーアウト（Home 非対応／GPO 管理中は押せない＝常設ラベルで理由を提示）。
    // 旧 v0.1.18 は「押せるが no-op＋説明」だったが、状態が常時見える今は「押せない＋理由表示」が一貫した UX。
    // DisableRemoteDesktop 内の Home/GPO 分岐は defense-in-depth として残す（CanExecute をすり抜けても安全側）。
    // ※ cross-reviewer 軽微③（2026-06-09）：Enable と活性条件が完全同一のため <see cref="CanEnableRemoteDesktop"/>
    //   へ委譲して単一ソース化（片方だけ条件を変えて Enable/Disable の活性がズレる事故を防ぐ）。
    private bool CanToggleRemoteDesktop() => CanEnableRemoteDesktop();

    /// <summary>
    /// v0.1.20：メニュー常設の「現在の状態」ラベル（読み取り専用 MenuItem に表示）。
    /// 起動時＋各 RDP 操作完了時に <see cref="RefreshRemoteDesktopStatusLabelAsync"/> で更新する。
    /// </summary>
    [ObservableProperty]
    private string _remoteDesktopStatusLabel = "現在の状態：確認中…";

    /// <summary>
    /// ラベル更新の世代番号（cross-reviewer 重要②・2026-06-09）。`Refresh...` を呼ぶたびに UI スレッドで +1 し、
    /// await から戻った時に自分の世代が最新でなければ書き込みを破棄する。連続操作で複数の `Task.Run(ReadStatus)` が
    /// 走り、**完了順が前後して古い状態でラベルを上書きする**レースを防ぐ（呼び出しは全て UI スレッド＝lock 不要）。
    /// </summary>
    private int _rdpStatusLabelGeneration;

    /// <summary>
    /// 現在の RDP 状態を読み直してメニュー常設ラベルへ反映する（v0.1.20）。
    /// ReadStatus は FW 読取で powershell.exe を起動し得る＝別スレッド実行で UI を止めない。
    /// await 後の継続は UI スレッド（呼び出しは UI 同期コンテキスト）でプロパティ設定する。
    /// </summary>
    private async Task RefreshRemoteDesktopStatusLabelAsync()
    {
        var gen = ++_rdpStatusLabelGeneration; // UI スレッドで採番（呼び出し元は全て UI スレッド）
        string label;
        try
        {
            var status = await Task.Run(() => _remoteDesktop.ReadStatus());
            label = RemoteDesktopHelper.DescribeMenuStatusLabel(
                status.IsHostCapable, status.IsPolicyManaged,
                status.FDeny, status.FwTcpEnabled, status.Nla, status.Port);
        }
        catch
        {
            label = "現在の状態：取得できませんでした";
        }

        // 自分より後に発行された更新があれば、古い結果でラベルを上書きしない（最新だけが勝つ）。
        if (gen == _rdpStatusLabelGeneration)
            RemoteDesktopStatusLabel = label;
    }

    /// <summary>
    /// v0.1.19：ON 用の退避（first-touch）が存在するか＝「リモートデスクトップを元に戻す」が使えるか。
    /// CommandManager の頻繁な再評価でディスクを叩かないようキャッシュし、各コマンド完了時に <see cref="RefreshRdpBackupState"/> で更新する。
    /// </summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RestoreRemoteDesktopCommand))]
    private bool _hasRdpBackup;

    /// <summary>
    /// v0.1.19：ホスト対応エディションかのキャッシュ（plan-validator #6）。
    /// <see cref="EnvironmentInfoService.Collect"/> はフル収集で重く、CanExecute は頻繁に再評価されるため
    /// エディション（実行中に変わらない）を 1 回だけ取得して使い回す。
    /// </summary>
    private bool? _rdpHostCapableCache;
    private bool RdpHostCapable() => _rdpHostCapableCache ??= _remoteDesktop.IsHostCapableEdition();

    /// <summary>退避の有無を 1 回だけ読み直して <see cref="HasRdpBackup"/> に反映（ユーザー操作ごと＝ホットパス外）。</summary>
    private void RefreshRdpBackupState()
    {
        try { HasRdpBackup = _backup.HasMaintenanceOriginal(RemoteDesktopService.FeatureKey); }
        catch { /* 読めない時は据え置き */ }
    }

    [RelayCommand(CanExecute = nameof(CanToggleRemoteDesktop))]
    private async Task DisableRemoteDesktop()
    {
        // 1) ホスト非対応エディション（Home 等）＝RDP を受けられない。安全に no-op＋説明（押せるが害なし）。
        if (!_remoteDesktop.IsHostCapableEdition())
        {
            _dialogs.Info(
                "このエディションの Windows は、リモートデスクトップの「受け側（ホスト）」になれません（Pro 以上が必要）。\n" +
                "そのため無効化の操作は不要です。",
                "リモートデスクトップ");
            return;
        }

        // 2) GPO/MDM ポリシー管理中＝このツールでは変更できない（書いても効かない/戻される）。
        if (_remoteDesktop.IsPolicyManaged())
        {
            _dialogs.Warn(
                "リモートデスクトップは、グループポリシー（GPO/MDM）で管理されています。\n" +
                "WinKnobs からは変更できません。組織の管理者にご確認ください。",
                "ポリシー管理中");
            return;
        }

        // 3) 自己セッション判定。リモート接続中なら強警告（既定いいえ）。これが v0.1.18 唯一の締め出しガード。
        bool ok;
        if (_remoteDesktop.IsCurrentSessionRemote())
        {
            ok = _dialogs.ConfirmDestructive(
                "【重要】あなたは今このPCに『リモート接続』して操作しています。\n\n" +
                "リモートデスクトップを無効にすると、この接続が切断され、\n" +
                "このPCの前で直接操作しないと元に戻せなくなる可能性があります。\n\n" +
                "それでも続行しますか？（推奨：いいえ）",
                "⚠️ あなたは今リモート接続中です");
        }
        else
        {
            ok = _dialogs.ConfirmDestructive(
                "リモートデスクトップを無効にします。（管理者権限が必要）\n\n" +
                "外部からこのPCへの接続を受け付けなくなります（防御的な操作です）。\n" +
                "ファイアウォールのリモートデスクトップ規則も同時に閉じます。\n" +
                "※ Windows Defender ファイアウォールの該当規則のみが対象です。\n\n" +
                "再び有効にしたい場合は Windows の「設定 > システム > リモート デスクトップ」から行えます。\n\n" +
                "続行しますか？",
                "リモートデスクトップを無効化");
        }
        if (!ok) return;

        IsRemoteDesktopBusy = true;
        StatusMessage = "⏳ リモートデスクトップを無効化しています…";
        try
        {
            // UI スレッドを最大 60 秒ブロックしないよう別スレッドで実行（SystemProtection / MemoryCompression 同様）。
            var (result, err) = await Task.Run(() =>
            {
                var r = _remoteDesktop.Disable(out var e);
                return (r, e);
            });

            switch (result)
            {
                case RemoteDesktopOutcome.Success:
                    _dialogs.Info(
                        "✅ リモートデスクトップを無効にしました。\n外部からの接続を受け付けません。",
                        "成功");
                    StatusMessage = "✅ リモートデスクトップを無効化";
                    break;
                case RemoteDesktopOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;
                default:
                    // fail-closed：途中失敗ではロールバックしない（防御方向）。ただし FW 規則の無効化自体が
                    // 失敗（規則名不在等）した場合は FW が閉じていないこともあるため「閉じた前提」とは断定しない。
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error(
                        $"リモートデスクトップの無効化に失敗しました：\n{detail}\n\n" +
                        "（ファイアウォール規則や OS の拒否設定が、一部または全部 適用されていない可能性があります。\n" +
                        "Windows の設定でリモートデスクトップの状態を確認してください）",
                        "失敗");
                    StatusMessage = "❌ 無効化失敗";
                    break;
            }
        }
        finally
        {
            IsRemoteDesktopBusy = false;
            _ = RefreshRemoteDesktopStatusLabelAsync(); // v0.1.20：状態が変わった可能性 → 常設ラベル更新
        }
    }

    // -------- リモートデスクトップ 有効化（ON）：v0.1.19 システム・メンテナンス Phase 4（🔴Red 最終弾） --------
    // plan-validator #3（スレッド順序の固定）：
    //   ① UI スレッドで ReadStatus → ② UI スレッドで Red ダイアログ ShowDialog（Task.Run 内での UI 生成は即例外）
    //   → ③ 同意時のみ await Task.Run(Enable) → ④ await 後 UI スレッドで結果表示。
    //   IsRemoteDesktopBusy は**ダイアログ表示中から**立てる（表示中の再 Invoke も封じる）。
    // CanExecute（問3-A）：Home/ポリシー管理時は自動グレー（RdpHostCapable キャッシュ＋IsPolicyManaged）。

    private bool CanEnableRemoteDesktop()
        => !IsRemoteDesktopBusy && RdpHostCapable() && !_remoteDesktop.IsPolicyManaged();

    [RelayCommand(CanExecute = nameof(CanEnableRemoteDesktop))]
    private async Task EnableRemoteDesktop()
    {
        // defense-in-depth（通常は CanExecute でグレーだが、念のため UI スレッドで再ガード）。
        if (!RdpHostCapable())
        {
            _dialogs.Info(
                "このエディションの Windows は、リモートデスクトップの「受け側（ホスト）」になれません（Pro 以上が必要）。",
                "リモートデスクトップ");
            return;
        }
        if (_remoteDesktop.IsPolicyManaged())
        {
            _dialogs.Warn(
                "リモートデスクトップは、グループポリシー（GPO/MDM）で管理されています。\n" +
                "WinKnobs からは変更できません。組織の管理者にご確認ください。",
                "ポリシー管理中");
            return;
        }

        // ダイアログ表示中から排他（plan-validator #3）。
        IsRemoteDesktopBusy = true;
        try
        {
            // ① UI スレッドで現在状態を読む（ダイアログのコンテキスト提示用）。
            RdpStatus status;
            try
            {
                status = _remoteDesktop.ReadStatus();
            }
            catch (Exception ex)
            {
                _dialogs.Error($"現在の状態を読み取れませんでした：\n{ex.Message}", "リモートデスクトップ");
                StatusMessage = "❌ 状態読取失敗";
                return;
            }

            // ② UI スレッドで Red 専用 強確認ダイアログ（ShowDialog は UI スレッド必須）。
            var consented = _dialogs.ConfirmRemoteDesktopEnable(status);
            if (!consented)
            {
                StatusMessage = "リモートデスクトップの有効化をキャンセルしました。";
                return;
            }

            // ③ 同意時のみ別スレッドで適用（UI を最大 60 秒ブロックしない）。
            StatusMessage = "⏳ リモートデスクトップを有効化しています…";
            var (result, err) = await Task.Run(() =>
            {
                var r = _remoteDesktop.Enable(out var e);
                return (r, e);
            });

            // ④ await 後は UI スレッド。4 分岐 switch（plan-validator #1）。
            switch (result)
            {
                case RemoteDesktopOutcome.Success:
                    var lanText = status.LanIPv4.Count > 0
                        ? string.Join(" / ", status.LanIPv4)
                        : "（取得できませんでした）";
                    var note = string.IsNullOrWhiteSpace(err) ? "" : $"\n\n{err}"; // UDP best-effort 警告等
                    _dialogs.Info(
                        "✅ リモートデスクトップを有効にしました。\n\n" +
                        $"・ユーザー：{status.UserName}\n" +
                        $"・LAN IP：{lanText}\n" +
                        $"・ポート：{status.Port}\n" +
                        "・NLA（ネットワークレベル認証）を必須にしました（安全側）。\n\n" +
                        "※ NLA を必須にしたので、古いクライアント（未更新の mstsc・一部 Mac/thin client）は\n" +
                        "　つながらない場合があります。その時はクライアント側を更新してください。" + note,
                        "成功");
                    StatusMessage = "✅ リモートデスクトップを有効化";
                    break;

                case RemoteDesktopOutcome.AccessDenied:
                    _dialogs.Warn(
                        "管理者権限が必要です。\nWinKnobs を「管理者として実行」で起動し直してから、もう一度お試しください。",
                        "権限不足");
                    StatusMessage = "❌ 権限不足（管理者が必要）";
                    break;

                case RemoteDesktopOutcome.LeftExposed:
                    // 🔴 最重要：FW を開けた後に失敗し、巻き戻しでも閉じ直せなかった＝攻撃口が開いたまま。
                    var leDetail = string.IsNullOrWhiteSpace(err) ? "" : $"\n\n{err}";
                    _dialogs.Error(
                        "🔴 外部接続を受け付ける状態のまま、有効化に失敗しました。\n\n" +
                        "今すぐ『リモートデスクトップを無効にする』を管理者で実行するか、\n" +
                        "Windows の「設定 > システム > リモート デスクトップ」で状態を確認してください。" + leDetail,
                        "🔴 攻撃口が開いたままです");
                    StatusMessage = "🔴 攻撃口が開いたまま（今すぐ無効化を）";
                    break;

                default: // Failed：途中失敗は元へ巻き戻し済み＝攻撃口は開いていない。
                    var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                    _dialogs.Error(
                        $"リモートデスクトップの有効化に失敗しました：\n{detail}\n\n" +
                        "変更は元の状態に巻き戻しました（攻撃口は開いていません）。",
                        "失敗");
                    StatusMessage = "❌ 有効化失敗（巻き戻し済み）";
                    break;
            }
        }
        finally
        {
            IsRemoteDesktopBusy = false;
            RefreshRdpBackupState(); // 退避が増減した可能性 → 「元に戻す」活性を更新
            _ = RefreshRemoteDesktopStatusLabelAsync(); // v0.1.20：状態が変わった可能性 → 常設ラベル更新
        }
    }

    // -------- リモートデスクトップ 元に戻す（first-touch 復元）：v0.1.19 --------
    // 専用「元に戻す」で first-touch 退避へワンクリック復帰（plan-validator #2b）。
    // ※ 横断「ぜんぶ戻す」への統合は Maintenance RevertAll を作る v0.2.x まで待つ（問4-A）。

    private bool CanRestoreRemoteDesktop() => !IsRemoteDesktopBusy && HasRdpBackup;

    [RelayCommand(CanExecute = nameof(CanRestoreRemoteDesktop))]
    private async Task RestoreRemoteDesktop()
    {
        var ok = _dialogs.Confirm(
            "リモートデスクトップの設定を、WinKnobs で初めて変更する前の状態に戻します。\n（管理者権限が必要）\n\n続行しますか？",
            "リモートデスクトップを元に戻す");
        if (!ok) return;

        IsRemoteDesktopBusy = true;
        StatusMessage = "⏳ リモートデスクトップを元に戻しています…";
        try
        {
            var dict = _backup.LoadMaintenanceOriginals();
            if (!dict.TryGetValue(RemoteDesktopService.FeatureKey, out var json))
            {
                _dialogs.Info("戻せる退避情報が見つかりませんでした。", "元に戻す");
                StatusMessage = "退避情報なし";
                return;
            }

            string? err = null;
            var restored = await Task.Run(() =>
            {
                try { _remoteDesktop.RestoreRemoteDesktop(json); return true; }
                catch (Exception ex) { err = ex.Message; return false; }
            });

            if (restored)
            {
                // 完全成功時のみ退避を後始末（部分失敗時は残して再試行可能に）。
                _backup.RemoveMaintenanceOriginal(RemoteDesktopService.FeatureKey);
                _dialogs.Info("✅ リモートデスクトップを、変更前の状態に戻しました。", "成功");
                StatusMessage = "✅ リモートデスクトップを元に戻しました";
            }
            else
            {
                // RestoreRemoteDesktop は失敗時に安全側へ寄せて例外送出（退避は残す＝管理者で再試行可能）。
                var detail = string.IsNullOrWhiteSpace(err) ? "（詳細不明）" : err;
                _dialogs.Error(
                    $"元に戻す処理に失敗しました：\n{detail}\n\n" +
                    "安全のため『無効』寄りに留めています。管理者で起動し直して、もう一度お試しください。",
                    "失敗");
                StatusMessage = "❌ 元に戻す失敗（安全側に留置）";
            }
        }
        finally
        {
            IsRemoteDesktopBusy = false;
            RefreshRdpBackupState();
            _ = RefreshRemoteDesktopStatusLabelAsync(); // v0.1.20：状態が変わった可能性 → 常設ラベル更新
        }
    }

    // -------- ヘルプメニュー --------
    [RelayCommand]
    private void CopyEnvironmentInfo()
    {
        try
        {
            var text = _envInfo.BuildClipboardText();
            Clipboard.SetText(text);
            _dialogs.Info(
                "環境情報をクリップボードにコピーしました。\nIssues 報告などに貼り付けてください。",
                "コピー完了");
            StatusMessage = "📋 環境情報コピー済み";
        }
        catch (Exception ex)
        {
            _dialogs.Error($"コピー失敗: {ex.Message}", "失敗");
        }
    }

    [RelayCommand] private void OpenGitHubRepo()     { _launcher.OpenUrl("https://github.com/"); }
    [RelayCommand] private void OpenGitHubIssues()   { _launcher.OpenUrl("https://github.com/"); }
    [RelayCommand] private void OpenGitHubSponsors() { _launcher.OpenUrl("https://github.com/sponsors/"); }

    /// <summary>
    /// 2026-05-20 充実：ビルド情報・実装機能サマリ・実行環境を一括表示。
    /// </summary>
    [RelayCommand]
    private void ShowVersion()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly();
        var asmName = asm.GetName();
        var location = string.IsNullOrEmpty(asm.Location) ? "(SingleFile / Self-contained)" : asm.Location;

        var os = Environment.OSVersion.VersionString;
        var clr = Environment.Version;
        var arch = Environment.Is64BitProcess ? "x64" : "x86";
        var isAdmin = new System.Security.Principal.WindowsPrincipal(
            System.Security.Principal.WindowsIdentity.GetCurrent())
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);

        var msg =
            "🪟 WinKnobs v0.3.0\n" +
            "© WinKnobs Project / MIT License\n" +
            "\n" +
            "──── 実装済みカテゴリ・項目 ────\n" +
            $"カテゴリ数：{Categories.Count}  項目数：{Items.Count}\n" +
            "\n" +
            "──── 主な機能 ────\n" +
            "・レジストリ／fsutil／netsh／実行系の各種設定を 1 ダイアログから\n" +
            "・適用前に自動バックアップ（.reg）→ 失敗時に戻すボタンで復元\n" +
            "・HKLM 系は管理者再起動 UX で安全に書込（BUG-3 対応）\n" +
            "・ライト／ダーク／システム連動の 3 テーマ\n" +
            "・ウィンドウ位置・サイズの永続化（2026-05-20）\n" +
            "・UI Automation 対応（Phase B）：smoke/dialog/apply 系を全自動テスト\n" +
            "\n" +
            "──── 実行環境 ────\n" +
            $"OS：{os}\n" +
            $".NET：{clr}（{arch}）\n" +
            $"権限：{(isAdmin ? "管理者" : "標準ユーザー")}\n" +
            $"アセンブリ：{asmName.Name} {asmName.Version}\n" +
            $"場所：{location}\n" +
            "\n" +
            "──── 由来 ────\n" +
            "「いじくるツール」（高橋忍さん作・2018年で開発停止）にインスパイアされた\n" +
            "Windows 11 時代の独自実装。安全・分かりやすさ重視。";

        _dialogs.Info(msg, "バージョン情報");
    }

    // ====================== 初期化 ======================
    private void InitializeCategories()
    {
        Categories.Add(new SettingCategory("ホーム", CategoryType.Home, "🏠"));
        Categories.Add(new SettingCategory("システム", CategoryType.System, "🖥️"));
        Categories.Add(new SettingCategory("システム・メンテナンス", CategoryType.Maintenance, "🛠"));
        Categories.Add(new SettingCategory("起動・スタートアップ", CategoryType.Startup, "🚀"));
        Categories.Add(new SettingCategory("エクスプローラ・シェル", CategoryType.Shell, "📂"));
        Categories.Add(new SettingCategory("ネットワーク", CategoryType.Network, "🌐"));
        Categories.Add(new SettingCategory("カーネル", CategoryType.Kernel, "🧠"));
        Categories.Add(new SettingCategory("ログイン", CategoryType.Login, "🔐"));
        Categories.Add(new SettingCategory("デスクトップ・UI", CategoryType.Desktop, "🎨"));
        Categories.Add(new SettingCategory("グラフィック", CategoryType.Graphics, "🎮"));
        Categories.Add(new SettingCategory("サウンド", CategoryType.Sound, "🔊"));   // v0.3.3：システム効果音 ON/OFF
        Categories.Add(new SettingCategory("ゲーミング", CategoryType.Gaming, "🕹️"));   // v0.3.7：ゲームモード等
        Categories.Add(new SettingCategory("アクセシビリティ", CategoryType.Accessibility, "♿"));
        Categories.Add(new SettingCategory("プライバシー", CategoryType.Privacy, "🔒"));
        Categories.Add(new SettingCategory("キーボード・マウス", CategoryType.KeyboardMouse, "⌨️"));
        Categories.Add(new SettingCategory("検索結果", CategoryType.SearchResults, "🔍"));
        Categories.Add(new SettingCategory("お気に入り", CategoryType.Favorites, "⭐"));
    }

    /// <summary>
    /// 設定項目を登録（Phase 2 で「Dummy」から本番運用名に変更：cross-reviewer M-2）。
    /// A3-2-c：RestartScope migration ＋ Win11 価値項目 W1-W8 追加。
    /// </summary>
    private void InitializeSettingItems()
    {
        // ====================== 既存項目（A3-2-c migration） ======================

        // BUG-1 修正済み：HKLM\...\Explorer → HKCU\...\Explorer\Advanced
        Items.Add(new SettingItem
        {
            Id = "shell.iconCacheSize",
            Title = "アイコンキャッシュサイズ",
            Description = "エクスプローラのアイコン表示用キャッシュ容量を増やします。アイコン表示が早くなります。推奨：4096〜8192。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Logoff,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "Max Cached Icons",
            ValueType = SettingValueType.String,
            NumericStringRange = (32, 65535),    // cross-reviewer U-3
            RecommendedValue = "4096",
            DefaultValue = "1024"
        });

        Items.Add(new SettingItem
        {
            Id = "shell.shortcutArrow",
            Title = "ショートカット矢印を非表示",
            Description = "ショートカットアイコンの左下にある矢印アイコンを消します。デスクトップが見た目すっきり。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Shell Icons",
            RegistryValueName = "29",
            ValueType = SettingValueType.ExpandString,
            RecommendedValue = @"%windir%\System32\imageres.dll,-1015"
        });

        Items.Add(new SettingItem
        {
            Id = "shell.separateProcess",
            Title = "エクスプローラを別プロセスで起動",
            Description = "1つのエクスプローラがフリーズしても他に影響しなくなります。安定性UP・メモリは少し増えます。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "SeparateProcess",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,
            DefaultValue = false
        });

        Items.Add(new SettingItem
        {
            Id = "shell.iconsOnly",
            Title = "縮小表示を無効化（アイコンのみ表示）",
            Description = "エクスプローラの縮小表示（画像・動画のサムネイル）を無効化し、ファイル固有のアイコンのみ表示します。動作軽量化・古いPC向け。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "IconsOnly",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = false,
            DefaultValue = false
        });

        Items.Add(new SettingItem
        {
            Id = "shell.taskbarLeftAlignment",
            Title = "タスクバーを左寄せにする（Windows 11）",
            Description = "Windows 11 のタスクバーアイコンを中央配置から左配置に変更します。OFF（=中央寄せ）が Windows 11 デフォルト。即効・タスクバー自動更新（PC 再起動不要）。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "TaskbarAl",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,
            RecommendedValue = true,
            DefaultValue = false
        });

        Items.Add(new SettingItem
        {
            Id = "shell.showSuperHidden",
            Title = "システム保護ファイルを表示（上級者向け）",
            Description = "hiberfil.sys、pagefile.sys 等のシステム保護ファイルをエクスプローラに表示します。⚠️ 誤削除すると Windows が起動不能になる可能性あり。普段は OFF を強く推奨。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Red,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "ShowSuperHidden",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = false,
            DefaultValue = false
        });

        Items.Add(new SettingItem
        {
            Id = "shell.hideFileExt",
            Title = "ファイルの拡張子を表示",
            Description = ".txt / .exe / .docx などのファイル拡張子をエクスプローラに表示します。偽装ファイル（例：document.pdf.exe）の発見にも有効。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "HideFileExt",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,
            RecommendedValue = true,
            DefaultValue = false
        });

        Items.Add(new SettingItem
        {
            Id = "network.maxConnections",
            Title = "HTTP同時接続数の上限を上げる",
            Description = "ブラウザの同時ダウンロード数を増やします。複数ファイルダウンロード時に効果あり。",
            Category = CategoryType.Network,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings",
            RegistryValueName = "MaxConnectionsPerServer",
            ValueType = SettingValueType.DWord,
            RecommendedValue = 16,
            DefaultValue = 6
        });

        Items.Add(new SettingItem
        {
            Id = "kernel.disablePagingExecutive",
            Title = "カーネルを物理メモリに常駐",
            Description = "カーネルとドライバを物理メモリに固定し、ディスクへページアウトさせません。レスポンス向上。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Red,
            RestartScope = RestartScope.System,
            RegistryPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management",
            RegistryValueName = "DisablePagingExecutive",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,
            DefaultValue = false
        });

        // ====================== Win11 価値項目（A3-2-c W1-W6, W8） ======================

        // W1：アプリのダークモード（HKCU\...\Personalize → AppsUseLightTheme）
        // 値=0：ダーク・値=1：ライト → InvertDisplay でユーザー視点「ダーク ON / ライト OFF」に
        Items.Add(new SettingItem
        {
            Id = "desktop.darkModeApps",
            Title = "アプリのダークモード（Win11）",
            Description = "Windows 11 のアプリ表示をダークモードに切り替えます。Microsoft Store アプリ・設定アプリ等が黒基調に。即時反映。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            RegistryValueName = "AppsUseLightTheme",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,        // ON（ダーク）= レジストリ 0
            RecommendedValue = false,    // 推奨：ライト（ユーザー視点 OFF）
            DefaultValue = false         // Windows デフォルト：ライト
        });

        // W2：システムのダークモード（HKCU\...\Personalize → SystemUsesLightTheme）
        Items.Add(new SettingItem
        {
            Id = "desktop.darkModeSystem",
            Title = "システムのダークモード（Win11）",
            Description = "Windows 11 のシステム UI（タスクバー・スタートメニュー等）をダークモードに。即時反映。アプリのダークと併用推奨。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            RegistryValueName = "SystemUsesLightTheme",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,
            RecommendedValue = false,
            DefaultValue = false
        });

        // W3：メニュー表示遅延（HKCU\Control Panel\Desktop → MenuShowDelay）
        // String 型・数値文字列。推奨 0-100ms（既定 400ms）
        // plan-validator V-8：Logoff Scope だが Explorer 再起動でも反映する報告あり → Explorer に確定
        // cross-reviewer U-3：NumericStringRange で 0-10000 制約
        Items.Add(new SettingItem
        {
            Id = "desktop.menuShowDelay",
            Title = "メニュー表示遅延を短縮（Win11 サクサク化）",
            Description = "右クリックメニュー・サブメニューが開くまでの待ち時間（ミリ秒・0〜10000）。既定 400ms → 0〜100ms で体感速度向上。0 だと意図せず展開することがあるため 50〜100 推奨。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Control Panel\Desktop",
            RegistryValueName = "MenuShowDelay",
            ValueType = SettingValueType.String,
            NumericStringRange = (0, 10000),
            RecommendedValue = "50",
            DefaultValue = "400"
        });

        // W4：タスクバーアイコンサイズ（HKCU\...\Advanced → TaskbarSi）
        // plan-validator V-5/VE-5 修正：DWord 0/1/2 を Command 型の Choices に変換
        // ⚠️ Win11 24H2 で効かない報告あり → Description に明記
        Items.Add(BuildTaskbarSizeItem());

        // v0.1.9：アクセシビリティ「テキストのサイズ」（HKCU\...\Accessibility → TextScaleFactor）
        Items.Add(BuildTextScaleItem());

        // v0.1.10：アクセシビリティ「マウスポインタのサイズ」（CursorSize + CursorBaseSize の2値）
        Items.Add(BuildPointerSizeItem());

        // v0.1.11：アクセシビリティ「スクロールバーを常に表示」（DynamicScrollbars・Command型2択）
        Items.Add(BuildScrollbarItem());

        // v0.1.12：アクセシビリティ「テキストカーソルの太さ」（CaretWidth・REG_DWORD・1-20px）
        Items.Add(BuildCaretWidthItem());

        // v0.1.13：アクセシビリティ「通知を表示する時間」（MessageDuration・REG_DWORD・秒・5-300）
        Items.Add(BuildMessageDurationItem());

        // v0.1.14：システム・メンテナンス「タスクマネージャを使えるようにする」（DisableTaskMgr・HKCU・管理者不要・Phase 1）
        Items.Add(BuildTaskManagerItem());

        // v0.3.2：デスクトップ・UI「タスクバーの検索ボックスの表示」（SearchboxTaskbarMode・HKCU・管理者不要・4択）
        Items.Add(BuildTaskbarSearchItem());

        // v0.3.12：シェル「隠しファイル・フォルダーを表示」（Hidden・HKCU・管理者不要・2択・1=表示/2=非表示）
        Items.Add(BuildHiddenFilesItem());

        // v0.3.3：サウンド「システム効果音 ON/OFF」（AppEvents\Schemes・HKCU・管理者不要・2択・Scope B 実消音）
        Items.Add(BuildSystemSoundItem());

        // v0.3.4：サウンド「Windows の起動音」（HKLM\…\LogonUI\BootAnimation\DisableStartupSound・要管理者・次回起動で反映）
        // 値=0：起動音オン（鳴る）/ 値=1：オフ（鳴らない）→ InvertDisplay でユーザー視点「鳴らす ON / 鳴らさない OFF」に。
        // darkModeApps と同型の宣言的 HKLM Boolean（専用 service/helper 不要・汎用書込＋退避＋UAC 誘導フローを再利用）。
        Items.Add(new SettingItem
        {
            Id = "sound.startupSound",
            Title = "Windows の起動音（起動時のサウンド）",
            Description = "Windows のサインイン/起動時に鳴る「起動音」のオン/オフを切り替えます"
                + "（設定 > 個人用設定 > テーマ > サウンド、または「サウンド」コントロールパネルの『Windows スタートアップのサウンドを再生する』と同じ）。"
                + "オンで起動音が鳴り、オフで無音になります。"
                + "⚠️ システム全体の設定のため管理者権限が必要です（HKLM）。⚠️ 反映は次回の起動（再起動）からです。",
            Category = CategoryType.Sound,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            RegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Authentication\LogonUI\BootAnimation",
            RegistryValueName = "DisableStartupSound",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,        // ON（鳴らす）= レジストリ 0（DisableStartupSound=0）
            RecommendedValue = true,     // 中立：Windows 既定（起動音オン）を推奨表示＝変更を押し付けない
            DefaultValue = true          // Windows デフォルト：起動音オン
        });

        // v0.3.5：デスクトップ・UI「スナップ時の候補表示（スナップアシスト）」
        // HKCU\…\Explorer\Advanced\SnapAssist（DWORD・1=ON/0=OFF・管理者不要・エクスプローラ再起動で反映）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。darkMode と同型の宣言的 HKCU Boolean。
        Items.Add(new SettingItem
        {
            Id = "desktop.snapAssist",
            Title = "スナップ時の候補表示（スナップアシスト）",
            Description = "ウィンドウを画面端にスナップした時に「残りの場所に何を並べるか」の候補（他ウィンドウのサムネイル）を表示する機能のオン/オフです"
                + "（設定 > システム > マルチタスク >「ウィンドウをスナップする」内の候補表示に相当）。"
                + "オフにすると、スナップ後に候補ポップアップが出なくなります（スナップ操作自体は無効になりません）。"
                + "管理者権限は不要です（自分のユーザー設定）。⚠️ 反映にはエクスプローラの再起動（またはサインアウト→再ログイン）が必要な場合があります。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "SnapAssist",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,     // 中立：Windows 既定（候補表示オン）
            DefaultValue = true          // Windows デフォルト：オン
        });

        // v0.3.6：システム「ストレージセンサー（自動ディスク クリーンアップ）」
        // HKCU\…\StorageSense\Parameters\StoragePolicy 値名 "01"（DWORD・1=ON/0=OFF・管理者不要・即時）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。宣言的 HKCU Boolean（汎用書込＋退避を再利用）。
        Items.Add(new SettingItem
        {
            Id = "system.storageSenseAuto",
            Title = "ストレージセンサー（自動ディスク クリーンアップ）",
            Description = "ディスクの空き容量が少ない時などに、一時ファイルやごみ箱の中身を自動で削除してディスクを整理する機能のオン/オフです"
                + "（設定 > システム > ストレージ > ストレージセンサー と同じ）。"
                + "オフにすると自動クリーンアップが行われなくなります（手動のディスク クリーンアップは引き続き使えます）。"
                + "管理者権限は不要です（自分のユーザー設定）。設定はすぐ反映されます。"
                + "⚠️ 設定値の名前はレジストリ上「01」です（Windows の仕様）。",
            Category = CategoryType.System,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\StorageSense\Parameters\StoragePolicy",
            RegistryValueName = "01",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,     // 中立：自動整理オンを既定表示（押し付けない）
            DefaultValue = true          // Windows 11 既定はおおむねオン
        });

        // v0.3.7：ゲーミング「ゲームモード（Game Mode）」
        // HKCU\Software\Microsoft\GameBar\AutoGameModeEnabled（DWORD・1=ON/0=OFF・管理者不要・即時）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。宣言的 HKCU Boolean（汎用書込＋退避を再利用）。
        Items.Add(new SettingItem
        {
            Id = "gaming.gameMode",
            Title = "ゲームモード（Game Mode）",
            Description = "ゲームのプレイ中に Windows の更新や通知などのバックグラウンド動作を抑えて、ゲームを優先する機能のオン/オフです"
                + "（設定 > ゲーム > ゲームモード と同じ）。"
                + "オンにするとゲーム中のカクつき軽減が期待できます（効果は環境により異なります）。"
                + "管理者権限は不要です（自分のユーザー設定）。設定はすぐ反映されます。",
            Category = CategoryType.Gaming,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\GameBar",
            RegistryValueName = "AutoGameModeEnabled",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,     // 中立：Windows 既定（オン）
            DefaultValue = true          // Windows デフォルト：オン
        });

        // v0.3.8：ゲーミング「Game DVR（ゲームの録画機能）」（2値同時 Command・HKCU・管理者不要）
        Items.Add(BuildGameDvrItem());

        // v0.3.9：デスクトップ・UI「タスクバー時計に秒を表示」
        // HKCU\…\Explorer\Advanced\ShowSecondsInSystemClock（DWORD・1=表示/0=非表示・管理者不要・エクスプローラ再起動で反映）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。宣言的 HKCU Boolean（汎用書込＋退避を再利用）。
        Items.Add(new SettingItem
        {
            Id = "desktop.taskbarClockSeconds",
            Title = "タスクバー時計に秒を表示",
            Description = "タスクバー右下の時計に「秒」まで表示します（設定 > 個人用設定 > タスクバー > タスクバーの動作 >「システム トレイの時計で秒を表示する」と同じ）。"
                + "オンにすると時刻が hh:mm:ss 形式になります（その分わずかに CPU を使います）。"
                + "管理者権限は不要です（自分のユーザー設定）。⚠️ 反映にはエクスプローラの再起動が必要な場合があります。"
                + "⚠️ Windows 11 build 22621.1776 以降が必要です（古いバージョンでは効果がありません）。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "ShowSecondsInSystemClock",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = false,    // 中立：Windows 既定（非表示）
            DefaultValue = false         // Windows デフォルト：非表示
        });

        // v0.3.10：デスクトップ・UI「タスクバーのボタン表示」3種（いずれも HKCU\…\Explorer\Advanced・DWORD 1=表示/0=非表示・管理者不要）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。タスクバーをすっきりさせる定番（いじくるツール代替）。
        Items.Add(new SettingItem
        {
            Id = "desktop.taskbarTaskView",
            Title = "タスクバーに「タスクビュー」ボタンを表示",
            Description = "タスクバーの「タスクビュー」ボタン（仮想デスクトップ切替）の表示/非表示を切り替えます"
                + "（設定 > 個人用設定 > タスクバー >「タスク ビュー」と同じ）。"
                + "非表示にするとタスクバーがすっきりします（Win + Tab のショートカットは引き続き使えます）。"
                + "管理者権限は不要です。⚠️ エクスプローラの再起動で反映する場合があります。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "ShowTaskViewButton",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,     // 中立：Windows 既定（表示）
            DefaultValue = true
        });
        // 注：「ウィジェット」ボタン（TaskbarDa）は Win11 24H2/25H2（build 26200）で値単位の ACL 保護により
        //     HKCU 書込が「unauthorized」になる（自動検証 2026-06-29 で実証・同キーの他値は書込可）。
        //     偽トグルを出さない方針で本バージョンでは非提供（将来は HKLM ポリシー AllowNewsAndInterests＝要管理者で別途検討）。
        Items.Add(new SettingItem
        {
            Id = "desktop.taskbarChat",
            Title = "タスクバーに「チャット」ボタンを表示",
            Description = "タスクバーの「チャット」ボタン（Microsoft Teams 個人用）の表示/非表示を切り替えます"
                + "（設定 > 個人用設定 > タスクバー >「チャット」と同じ）。"
                + "非表示にするとタスクバーがすっきりします。"
                + "管理者権限は不要です。⚠️ エクスプローラの再起動で反映する場合があります。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "TaskbarMn",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = true,     // 中立：Windows 既定（表示）
            DefaultValue = true
        });

        // v0.3.13：シェル「項目のチェックボックスを表示」（AutoCheckSelect・HKCU\…\Explorer\Advanced・DWORD 1=表示/0=非表示・管理者不要）。
        // 値 1=ON がユーザー視点 ON と一致＝InvertDisplay 不要。宣言的 HKCU Boolean（秒表示/タスクバーボタン同型）。File Explorer 挙動なので Shell カテゴリ。
        Items.Add(new SettingItem
        {
            Id = "shell.autoCheckSelect",
            Title = "項目のチェックボックスを表示",
            Description = "エクスプローラでファイル/フォルダーにマウスを重ねるとチェックボックスが出て、クリックで複数選択できるようにします"
                + "（エクスプローラの「表示 > 表示 > 項目チェック ボックス」と同じ）。"
                + "タッチ操作や、Ctrl を押さずに複数選択したいときに便利です。オフにすると従来どおりチェックボックスは出ません。"
                + "管理者権限は不要です（自分のユーザー設定）。"
                + "⚠️ 反映にはエクスプローラの再起動（または F5 で更新）が必要な場合があります（WinKnobs は自動では再起動しません）。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
            RegistryValueName = "AutoCheckSelect",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = false,    // 中立：Windows 既定（非表示）
            DefaultValue = false         // Windows デフォルト：非表示
        });

        // W5：クラシック右クリックメニュー（CommandAction で CLSID キー存在判定）
        // plan-validator V-1/V-4 反映：Backup/Restore で JSON トークン保持
        Items.Add(BuildClassicContextMenuItem());

        // W6：Bing 検索無効化（HKCU\...\Search → BingSearchEnabled）
        // InvertDisplay：ユーザー視点「無効化 ON」= レジストリ 0
        Items.Add(new SettingItem
        {
            Id = "privacy.bingSearchDisabled",
            Title = "Bing 検索を無効化（Win11 スタート検索）",
            Description = "Windows 11 のスタートメニュー検索から Bing ウェブ検索結果を除去します。プライバシー保護＋ローカル検索結果が見やすく。",
            Category = CategoryType.Privacy,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search",
            RegistryValueName = "BingSearchEnabled",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,        // ON（無効化 ON）= レジストリ 0
            RecommendedValue = true,     // 推奨：無効化 ON
            DefaultValue = false         // Windows デフォルト：Bing 有効（無効化 OFF）
        });

        // W8：ショートカット名「- ショートカット」省略（Binary 4バイト）
        // Link 値を 00 00 00 00 にすると省略 ON、未設定または既定で OFF
        Items.Add(new SettingItem
        {
            Id = "shell.shortcutSuffixOmit",
            Title = "ショートカット名「- ショートカット」を省略",
            Description = "新規ショートカット作成時に末尾に付く「- ショートカット」表示を省略します。Binary 値（4バイト固定）。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer",
            RegistryValueName = "Link",
            ValueType = SettingValueType.Binary,
            ExpectedByteLength = 4,
            RecommendedValue = "00 00 00 00",
            DefaultValue = "1E 00 00 00"   // Windows デフォルト（参考値）
        });

        // ====================== A3-2-c W7復活＋拡張：v0.1.1 追加項目（L1/L2/K1/K2/P1/P2） ======================

        // L1：Fast Startup（HiberbootEnabled）
        // HKLM\...\Session Manager\Power → HiberbootEnabled (DWord 0/1)
        Items.Add(new SettingItem
        {
            Id = "login.fastStartup",
            Title = "高速スタートアップ（Fast Startup）",
            Description = "Windows シャットダウン時に一部をハイバネートして起動を高速化します。⚠️ デュアルブート環境や OS 更新後の不具合の原因になることあり。SSD なら無効化推奨。PC 再起動で反映。",
            Category = CategoryType.Login,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            RegistryPath = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Power",
            RegistryValueName = "HiberbootEnabled",
            ValueType = SettingValueType.Boolean,
            RecommendedValue = false,    // SSD 主流の現代では無効推奨
            DefaultValue = true          // Windows デフォルトは有効
        });

        // L2：Prefetcher 動作モード
        // HKLM\...\Memory Management\PrefetchParameters → EnablePrefetcher (DWord 0-3)
        Items.Add(BuildPrefetcherItem());

        // K1：NTFS 8.3 名生成無効化（fsutil 経由 CommandAction）
        Items.Add(BuildNtfs8dot3Item());

        // K2：NTFS LastAccess 更新（fsutil 経由 CommandAction）
        //   ★W7 復活：uint オーバーフロー（0x80000003）を fsutil で回避
        Items.Add(BuildNtfsLastAccessItem());

        // P1：Win32PrioritySeparation（CPU スケジューラ優先度）
        // HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl → Win32PrioritySeparation (DWord)
        Items.Add(BuildWin32PrioritySeparationItem());

        // P2：System Responsiveness（マルチメディア優先度）
        // HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile → SystemResponsiveness (DWord)
        Items.Add(new SettingItem
        {
            Id = "kernel.systemResponsiveness",
            Title = "システムレスポンシブネス（マルチメディア優先度）",
            Description = "マルチメディアアプリの CPU 予約割合（％）。既定 20% → 10% で他アプリへの CPU を増やし応答性向上（ゲーミング向け）。範囲 0〜100。即時反映。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
            RegistryValueName = "SystemResponsiveness",
            ValueType = SettingValueType.DWord,
            RecommendedValue = 10,
            DefaultValue = 20
        });

        // ====================== v0.1.2：プライバシー 3 + ネット 1 + シェル 2 ======================

        // PR1：Cortana 無効化
        Items.Add(new SettingItem
        {
            Id = "privacy.cortanaDisabled",
            Title = "Cortana を無効化（Win11）",
            Description = "Microsoft の音声アシスタント Cortana を無効化します。ポリシーレジストリ経由。管理者権限必須。サインインし直しで反映。",
            Category = CategoryType.Privacy,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Logoff,
            RegistryPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\Windows Search",
            RegistryValueName = "AllowCortana",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,           // ON（無効化 ON）= レジストリ 0
            RecommendedValue = true,        // 推奨：無効化 ON
            DefaultValue = false
        });

        // PR2：Activity History 無効化
        Items.Add(new SettingItem
        {
            Id = "privacy.activityHistoryDisabled",
            Title = "アクティビティ履歴を無効化",
            Description = "Windows がアプリの利用履歴を「タイムライン」に記録するのを無効化します。プライバシー保護。管理者権限必須。サインインし直しで反映。",
            Category = CategoryType.Privacy,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Logoff,
            RegistryPath = @"HKLM\SOFTWARE\Policies\Microsoft\Windows\System",
            RegistryValueName = "EnableActivityFeed",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = true,
            RecommendedValue = true,
            DefaultValue = false
        });

        // PR3：SmartScreen 動作モード（Command 3択・REG_SZ）
        Items.Add(BuildSmartScreenItem());

        // N1：TCP Auto-Tuning（netsh 経由 Command）
        Items.Add(BuildTcpAutoTuningItem());

        // S1：アイコンキャッシュクリア（CommandAction・実行系）
        Items.Add(BuildIconCacheClearItem());

        // S2：BagMRU クリア（CommandAction・実行系）
        Items.Add(BuildBagMRUClearItem());

        // ====================== v0.2.0 追加項目（2026-05-25 AI代行会社） ======================

        // V1：透明効果 ON/OFF（夜暗室向け・GPU 負荷軽減・既存ダーク UI のメリハリ向上）
        // HKCU・Boolean・1=ON / 0=OFF（InvertDisplay なし＝ユーザー視点とレジストリ一致）
        Items.Add(new SettingItem
        {
            Id = "desktop.transparencyEffects",
            Title = "透明効果（タスクバー・スタートメニュー）",
            Description = "Windows の透明エフェクトの有効/無効。OFF にすると暗室で UI のコントラストが上がり、GPU 負荷も軽減します。即時反映。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
            RegistryValueName = "EnableTransparency",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = false,        // 1=ON（透明）・0=OFF
            RecommendedValue = false,     // 暗室向け推奨：OFF
            DefaultValue = true           // Windows デフォルト：ON
        });

        // V2：クリップボード履歴 ON/OFF（Win+V で履歴呼び出し）
        // HKCU・Boolean・1=履歴有効 / 0=無効
        Items.Add(new SettingItem
        {
            Id = "system.clipboardHistory",
            Title = "クリップボード履歴（Win+V）",
            Description = "Windows のクリップボード履歴機能を有効にします。Win+V で過去にコピーした内容を一覧から選んで貼り付けできます。即時反映。",
            Category = CategoryType.System,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            RegistryPath = @"HKCU\Software\Microsoft\Clipboard",
            RegistryValueName = "EnableClipboardHistory",
            ValueType = SettingValueType.Boolean,
            InvertDisplay = false,        // 1=有効・0=無効
            RecommendedValue = true,      // 推奨：ON
            DefaultValue = false          // Windows デフォルト：OFF
        });

        // ====================== v0.2.0 追加項目（2026-06-10 Quick Win） ======================

        // Power Throttling 無効化（kernel.powerThrottlingOff）
        Items.Add(BuildPowerThrottlingItem());

        // ====================== v0.2.1 追加項目（2026-06-11 Step1-2） ======================

        // マウス加速 OFF（gaming.mouseAcceleration）
        Items.Add(BuildMouseAccelerationItem());
        // Network Throttling Index（network.throttlingIndex）
        Items.Add(BuildNetworkThrottlingItem());
        // プロセッサー Boost モード（system.processorBoost）
        Items.Add(BuildProcessorBoostItem());

        // 最大のプロセッサの状態（system.processorPowerState・v0.3.11・PROCTHROTTLEMAX %）
        Items.Add(BuildProcessorMaxStateItem());

        // ====================== v0.2.2 追加項目（2026-06-11 Step3） ======================

        // 夜間モード 色温度（desktop.nightLightStrength・CloudStore バイナリ blob・最難関）
        Items.Add(BuildNightLightItem());
    }

    // ====================== v0.2.0 Power Throttling（Command 2択・値単位復元） ======================
    //
    // ⚠️ Boolean 型は不採用（plan-validator 致命1・2）：Boolean の復元は .reg キー削除マーカー経由＝
    // 「PowerThrottling キーごと削除」になり、同居する他の値（将来 MS が置く既定値等）を巻き添えにする。
    // Command 型＝StringBackup（ValueExisted）＋DeleteValue の「値単位復元」で安全側に（SmartScreen 同型）。
    // 書込時のキー自動作成は RegistryService.WriteValue（CreateSubKey）が行うため CreateKey 不要。

    private SettingItem BuildPowerThrottlingItem()
    {
        const string path = @"HKLM\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling";
        const string valueName = "PowerThrottlingOff";
        // 「既定」は値そのものを置かない（=削除）。0 を書く流儀もあるが、原状＝値なしに合わせる。
        var choices = new[] { "既定（スロットリングする・省電力）", "無効化（スロットリングしない・性能優先）" };

        // DeleteValue は例外を握りつぶして false を返すため（RegistryService.DeleteValue）、そのままだと
        // HKLM 非昇格時に HandleElevationNeeded の catch（UnauthorizedAccessException）をすり抜けて
        // 「既定に戻したつもりが何も変わっていない」サイレント失敗になる（@cross-reviewer 致命1）。
        // → 削除失敗かつ値が残存している時は明示的に投げて昇格ダイアログ経路に乗せる。
        void DeleteOrThrow()
        {
            if (_registry.DeleteValue(path, valueName)) return;
            // DeleteValue false＝キー無し or 例外（権限不足等）。「値の不在」を正の確認で示せた時だけ
            // 成功扱いにする（cross-reviewer I-3：ReadValue は例外を null に倒すため、
            // 「読めない（権限）」と「値が無い」を旧条件では区別できずサイレント成功し得た）。
            if (!_registry.KeyExists(path)) return;   // キーごと無い＝値も無い（正の確認）
            if (_registry.ReadValue<string>(path, valueName) == null) return;   // キーは読めて値が無い（正の確認）
            throw new UnauthorizedAccessException(
                $"レジストリ値 {path}\\{valueName} を削除できませんでした（管理者権限が必要な可能性）。");
        }

        return new SettingItem
        {
            Id = "kernel.powerThrottlingOff",
            Title = "Power Throttling（バックグラウンドCPU抑制）",
            Description = "Windows がバックグラウンドのアプリの CPU クロックを下げて省電力する機能。無効化すると裏で動くアプリ（エンコード・AI 処理等）が遅くならない代わりに消費電力が増えます（ノート PC ではバッテリー持ちに影響）。非公式設定のため将来の Windows Update で無視される可能性あり。個別アプリだけ除外したい場合は「設定 > システム > 電源」の公式 UI を推奨。管理者権限必須。再起動で確実に反映。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[0],   // 中立：既定のまま（一律推奨しない）
            DefaultValue = choices[0],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    // DWORD でも ReadValue<string> なら「値なし=null」を判別できる（SmartScreen 同手法）。
                    var v = _registry.ReadValue<string>(path, valueName);
                    return v == "1" ? choices[1] : choices[0];
                },
                Apply = label =>
                {
                    if (label == choices[1])
                        _registry.WriteValue(path, valueName, 1, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        DeleteOrThrow();   // 既定＝値を置かない（0 書込でなく原状の形へ・失敗は昇格経路へ）
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<string>(path, valueName);
                    return JsonSerializer.Serialize(new StringBackup { ValueExisted = v != null, Value = v ?? "" });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<StringBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted && int.TryParse(bk.Value, out var dw))
                        _registry.WriteValue(path, valueName, dw, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        DeleteOrThrow();   // 値単位削除（キーは消さない＝同居値を守る・失敗は昇格経路へ）
                }
            }
        };
    }

    // ====================== v0.2.1 マウス加速（Command 2択・3値連動＋SPI 即時反映） ======================
    //
    // HKCU\Control Panel\Mouse の MouseSpeed/MouseThreshold1/MouseThreshold2（REG_SZ・3値連動）。
    // レジストリ書込だけでは再ログオンまで反映されないため SPI_SETMOUSE で即時反映（best-effort・
    // 失敗してもレジストリは書けている＝再ログオンで反映＝フェイルソフト）。
    // ⚠️ SPI の int[3] の並びはキー名の並びと違う（MouseAccelHelper.ToSpiArray に集約・plan-validator 重要3）。

    private class MouseAccelBackup
    {
        public string Speed { get; set; } = MouseAccelHelper.DefaultSpeed;
        public string Threshold1 { get; set; } = MouseAccelHelper.DefaultThreshold1;
        public string Threshold2 { get; set; } = MouseAccelHelper.DefaultThreshold2;
    }

    private SettingItem BuildMouseAccelerationItem()
    {
        var choices = new[] { "既定（加速あり・ポインター精度を高める）", "OFF（加速なし・1:1 ゲーマー向け）" };

        // 3値を一括書込 → SPI で即時反映（共通ローカル関数）
        void WriteMouseValues(string speed, string t1, string t2)
        {
            _registry.WriteValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.SpeedValueName, speed, Microsoft.Win32.RegistryValueKind.String);
            _registry.WriteValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.Threshold1ValueName, t1, Microsoft.Win32.RegistryValueKind.String);
            _registry.WriteValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.Threshold2ValueName, t2, Microsoft.Win32.RegistryValueKind.String);
            MouseParamsService.TryApply(MouseAccelHelper.ToSpiArray(speed, t1, t2));
        }

        return new SettingItem
        {
            Id = "gaming.mouseAcceleration",
            Title = "マウス加速（ポインターの精度を高める）",
            Description = "マウスを速く動かすとカーソルが余計に進む Windows の補正。OFF にすると物理移動量とカーソル移動量が 1:1 になり、FPS ゲーム等で照準が安定します（デスクトップ作業ではカーソル移動が重く感じる場合あり）。即時反映（まれに再ログオンが必要）。",
            Category = CategoryType.KeyboardMouse,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[0],   // 中立：既定のまま（一律推奨しない）
            DefaultValue = choices[0],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var speed = _registry.ReadValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.SpeedValueName);
                    return MouseAccelHelper.IsAccelerationOff(speed) ? choices[1] : choices[0];
                },
                Apply = label =>
                {
                    if (label == choices[1])
                        WriteMouseValues(MouseAccelHelper.OffValue, MouseAccelHelper.OffValue, MouseAccelHelper.OffValue);
                    else
                        WriteMouseValues(MouseAccelHelper.DefaultSpeed, MouseAccelHelper.DefaultThreshold1, MouseAccelHelper.DefaultThreshold2);
                },
                Backup = () =>
                {
                    // 値なし（通常あり得ない）は Windows 既定で補完して退避＝Restore は常に「書く」だけで安全
                    return JsonSerializer.Serialize(new MouseAccelBackup
                    {
                        Speed = _registry.ReadValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.SpeedValueName) ?? MouseAccelHelper.DefaultSpeed,
                        Threshold1 = _registry.ReadValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.Threshold1ValueName) ?? MouseAccelHelper.DefaultThreshold1,
                        Threshold2 = _registry.ReadValue<string>(MouseAccelHelper.MouseKeyPath, MouseAccelHelper.Threshold2ValueName) ?? MouseAccelHelper.DefaultThreshold2
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<MouseAccelBackup>(token);
                    if (bk == null) return;
                    WriteMouseValues(bk.Speed, bk.Threshold1, bk.Threshold2);
                }
            }
        };
    }

    // ====================== v0.2.1 Network Throttling Index（Command 2択・値上書き復元） ======================
    //
    // HKLM\...\Multimedia\SystemProfile の NetworkThrottlingIndex（DWORD）。
    // 既定=10（0xa・マルチメディア再生保護のため非メディア系ネット処理を 10 packets/ms に制限）／
    // 無効=0xffffffff（制限なし）。OS 既定はこの値が「存在する」ため、既定側は値削除でなく 0xa を書く
    // （Power Throttling と逆・plan-validator 重要4）。値が元々無かった環境の復元のみ DeleteOrThrow 型。

    private SettingItem BuildNetworkThrottlingItem()
    {
        const string path = @"HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
        const string valueName = "NetworkThrottlingIndex";
        const string disabledDecimal = "4294967295";   // 0xffffffff の 10 進表現（ReadValue<string> 比較用）
        var choices = new[] { "既定（10・マルチメディア再生を保護）", "無効（制限なし・スループット優先）" };

        void DeleteOrThrow()
        {
            if (_registry.DeleteValue(path, valueName)) return;
            // DeleteValue false＝キー無し or 例外（権限不足等）。「値の不在」を正の確認で示せた時だけ
            // 成功扱いにする（cross-reviewer I-3：ReadValue は例外を null に倒すため、
            // 「読めない（権限）」と「値が無い」を旧条件では区別できずサイレント成功し得た）。
            if (!_registry.KeyExists(path)) return;   // キーごと無い＝値も無い（正の確認）
            if (_registry.ReadValue<string>(path, valueName) == null) return;   // キーは読めて値が無い（正の確認）
            throw new UnauthorizedAccessException(
                $"レジストリ値 {path}\\{valueName} を削除できませんでした（管理者権限が必要な可能性）。");
        }

        return new SettingItem
        {
            Id = "network.throttlingIndex",
            Title = "Network Throttling Index（非メディア系ネット制限）",
            Description = "音楽・動画再生中の音飛び防止のため、Windows がメディア系以外のネットワーク処理を毎ミリ秒 10 パケットに制限する機能。無効にするとゲーム等のスループットが上がる場合がありますが、配信や DAW（音楽制作）併用時は既定のままが安定することもあります。管理者権限必須。再起動で確実に反映。",
            Category = CategoryType.Network,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[0],   // 中立：既定のまま（一律推奨しない）
            DefaultValue = choices[0],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<string>(path, valueName);
                    return v == disabledDecimal ? choices[1] : choices[0];
                },
                Apply = label =>
                {
                    if (label == choices[1])
                        _registry.WriteValue(path, valueName, unchecked((int)0xffffffff), Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.WriteValue(path, valueName, 10, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<string>(path, valueName);
                    return JsonSerializer.Serialize(new StringBackup { ValueExisted = v != null, Value = v ?? "" });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<StringBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted && long.TryParse(bk.Value, out var dw))
                        _registry.WriteValue(path, valueName, unchecked((int)(uint)dw), Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        DeleteOrThrow();   // 元々値が無かった環境のみ（値単位削除・キーは消さない）
                }
            }
        };
    }

    // ====================== v0.2.1 プロセッサー Boost モード（Command 3択・powercfg 実GUID固定） ======================
    //
    // powercfg SUB_PROCESSOR PERFBOOSTMODE。⚠️ plan-validator 致命1：scheme_current 文字列は使わず、
    // 操作のたびに /getactivescheme で実 GUID を1回解決し Read/Write/Backup を同一 GUID に固定
    // （カスタム/OEM プラン・TOCTOU 対策）。GUID 解決失敗時は Apply 中止（例外→既存エラー経路）。
    // 5択でなく 3択（4/5=効率的系はハイブリッド CPU 専用＝非対応環境で「効かない選択肢」を出さない）。

    private class BoostBackup
    {
        public string SchemeGuid { get; set; } = "";
        public int Ac { get; set; } = 2;
        public int Dc { get; set; } = 2;
    }

    private string ResolveActiveSchemeGuidOrThrow()
    {
        var output = _launcherForCommands.RunPowerCfg("/getactivescheme");
        if (!PowerCfgHelper.TryParseActiveSchemeGuid(output, out var guid))
            throw new InvalidOperationException("アクティブな電源プランの GUID を取得できませんでした（powercfg /getactivescheme）。");
        return guid;
    }

    /// <summary>
    /// Boost：直前の Backup() が解決した GUID（cross-reviewer I-1・2026-06-11）。
    /// Backup と Apply が別々に /getactivescheme を解決すると、その間のプラン切替で
    /// 「退避した場所」と「書き込む場所」が食い違う（致命1 TOCTOU の部分再導入）。
    /// Backup→Apply は同一操作内で連続実行されるため、Backup の解決結果を Apply が使い回す。
    /// Apply 単独呼び出し（Backup なし経路）では null のため従来どおり自前解決する。
    /// </summary>
    private string? _boostSchemeGuidFromBackup;

    private SettingItem BuildProcessorBoostItem()
    {
        var choices = new[] { "無効（ブーストしない・発熱/静音優先）", "有効（控えめにブースト）", "アグレッシブ（既定・最大ブースト）" };
        // choices のインデックス＝PERFBOOSTMODE の index（0/1/2）に一致させている

        return new SettingItem
        {
            Id = "system.processorBoost",
            Title = "プロセッサー Boost モード（Turbo Boost 制御）",
            Description = "CPU が定格クロックを超えてブーストする挙動の制御。無効にすると発熱・ファン騒音・消費電力が下がる代わりに重い処理が遅くなります（AI ローカル実行や夏場の熱対策に）。現在アクティブな電源プランの AC/DC 両方に書き込みます。プランを切り替えると設定は引き継がれません。Windows 11 の「電源モード」スライダーが『推奨』以外だと反映されない場合があります。管理者権限必須。即時反映。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[2],   // 中立：Windows 既定（アグレッシブ）
            DefaultValue = choices[2],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    try
                    {
                        var scheme = ResolveActiveSchemeGuidOrThrow();
                        // ⚠️ /query でなく /qh（隠し設定込み）：PERFBOOSTMODE は既定で hidden 属性のため
                        // /query では出力されない（VM 実機検証 2026-06-11 で発見・ホストでも再現）。
                        var output = _launcherForCommands.RunPowerCfg(
                            "/qh", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid);
                        if (PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out _) && ac >= 0 && ac < choices.Length)
                            return choices[ac];
                    }
                    catch { /* 読取失敗は既定表示（SmartScreen 同パターン） */ }
                    return choices[2];   // 不明・3択外（4/5 等）は既定表示
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 2;
                    // I-1：Backup が解決した GUID を優先（退避先と書込先を同一プランに固定）。
                    var scheme = _boostSchemeGuidFromBackup ?? ResolveActiveSchemeGuidOrThrow();   // 解決失敗は throw＝Apply 中止
                    _boostSchemeGuidFromBackup = null;   // 使い捨て（次回操作に古い GUID を持ち越さない）
                    _launcherForCommands.RunPowerCfg("/setacvalueindex", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid, idx.ToString());
                    _launcherForCommands.RunPowerCfg("/setdcvalueindex", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid, idx.ToString());
                    _launcherForCommands.RunPowerCfg("/setactive", scheme);   // 再適用で即時反映
                },
                Backup = () =>
                {
                    var scheme = ResolveActiveSchemeGuidOrThrow();
                    // /qh（隠し設定込み）：PERFBOOSTMODE は hidden のため /query では空（Read と同じ）。
                    var output = _launcherForCommands.RunPowerCfg(
                        "/qh", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid);
                    if (!PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out var dc))
                        throw new InvalidOperationException("Boost モードの現在値を読み取れなかったため、適用を中止しました（復元不能な変更を避けるため）。");
                    _boostSchemeGuidFromBackup = scheme;   // I-1：直後の Apply が同一 GUID を使う
                    return JsonSerializer.Serialize(new BoostBackup { SchemeGuid = scheme, Ac = ac, Dc = dc });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<BoostBackup>(token);
                    if (bk == null || string.IsNullOrEmpty(bk.SchemeGuid)) return;
                    // 退避時の GUID に書き戻す（プランが切り替わっていても「退避した場所」へ正しく戻る）
                    _launcherForCommands.RunPowerCfg("/setacvalueindex", bk.SchemeGuid, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid, bk.Ac.ToString());
                    _launcherForCommands.RunPowerCfg("/setdcvalueindex", bk.SchemeGuid, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.PerfBoostModeGuid, bk.Dc.ToString());
                    _launcherForCommands.RunPowerCfg("/setactive", bk.SchemeGuid);
                }
            }
        };
    }

    // ====================== v0.3.11 最大のプロセッサの状態（Command プリセット・powercfg PROCTHROTTLEMAX %） ======================
    //
    // processorBoost と同型（powercfg・/getactivescheme で実 GUID 解決し Read/Backup/Apply を同一 GUID に固定＝TOCTOU 対策）。
    // 違い：PROCTHROTTLEMAX は列挙インデックスでなく **パーセント値**（TryParseAcDcIndexes は末尾2つの 0x を AC/DC として拾う＝
    //   範囲設定でも「現在の AC/DC」が末尾に来るので流用可）。プリセット外/読取不可はセンチネル表示（誤丸め防止）。

    private class ProcMaxStateBackup
    {
        public string SchemeGuid { get; set; } = "";
        public int Ac { get; set; } = 100;
        public int Dc { get; set; } = 100;
    }

    /// <summary>processorBoost の _boostSchemeGuidFromBackup と同じ用途（Backup→Apply 間の GUID 固定）。</summary>
    private string? _procMaxSchemeGuidFromBackup;

    private SettingItem BuildProcessorMaxStateItem()
    {
        const string keepLabel = "（変更しない・現在のまま）";
        var choices = new List<string> { keepLabel };
        choices.AddRange(ProcMaxStateHelper.Choices);

        return new SettingItem
        {
            Id = "system.processorPowerState",
            Title = "最大のプロセッサの状態（CPU 上限クロック %）",
            Description = "CPU が使う最大クロックの割合（％）を制限します。100% で制限なし。下げると発熱・ファン騒音・消費電力が下がる代わりに、"
                + "重い処理は遅くなります（夏場の熱対策・AI ローカル実行の温度/電力抑制・8700K 等の発熱機向け）。"
                + "現在アクティブな電源プランの AC/DC 両方に書き込みます。プランを切り替えると設定は引き継がれません。"
                + "現在値を読み取れない・プリセット外の場合は「（変更しない・現在のまま）」と表示し、その時は何も書き換えません。"
                + "⚠️ Windows 11 の「電源モード」スライダーが『推奨（バランス）』以外だと反映されない場合があります。管理者権限必須。即時反映。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            RecommendedValue = keepLabel,   // 中立：一律に制限を勧めない（現在値の表示が主目的）
            DefaultValue = keepLabel,
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    try
                    {
                        var scheme = ResolveActiveSchemeGuidOrThrow();
                        var output = _launcherForCommands.RunPowerCfg(
                            "/qh", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid);
                        if (PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out _))
                            return ProcMaxStateHelper.LabelForPercent(ac) ?? keepLabel;   // プリセット外はセンチネル
                    }
                    catch { /* 読取失敗はセンチネル表示 */ }
                    return keepLabel;
                },
                Apply = label =>
                {
                    var percent = ProcMaxStateHelper.PercentForLabel(label);
                    if (percent < 0) return;   // センチネル/不正＝書き込まない（no-op）
                    var scheme = _procMaxSchemeGuidFromBackup ?? ResolveActiveSchemeGuidOrThrow();   // 解決失敗は throw＝Apply 中止
                    _procMaxSchemeGuidFromBackup = null;   // 使い捨て
                    _launcherForCommands.RunPowerCfg("/setacvalueindex", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid, percent.ToString());
                    _launcherForCommands.RunPowerCfg("/setdcvalueindex", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid, percent.ToString());
                    _launcherForCommands.RunPowerCfg("/setactive", scheme);   // 再適用で即時反映
                },
                Backup = () =>
                {
                    var scheme = ResolveActiveSchemeGuidOrThrow();
                    var output = _launcherForCommands.RunPowerCfg(
                        "/qh", scheme, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid);
                    if (!PowerCfgHelper.TryParseAcDcIndexes(output, out var ac, out var dc))
                        throw new InvalidOperationException("最大プロセッサ状態の現在値を読み取れなかったため、適用を中止しました（復元不能な変更を避けるため）。");
                    _procMaxSchemeGuidFromBackup = scheme;   // 直後の Apply が同一 GUID を使う
                    return JsonSerializer.Serialize(new ProcMaxStateBackup { SchemeGuid = scheme, Ac = ac, Dc = dc });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<ProcMaxStateBackup>(token);
                    if (bk == null || string.IsNullOrEmpty(bk.SchemeGuid)) return;
                    _launcherForCommands.RunPowerCfg("/setacvalueindex", bk.SchemeGuid, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid, bk.Ac.ToString());
                    _launcherForCommands.RunPowerCfg("/setdcvalueindex", bk.SchemeGuid, PowerCfgHelper.SubProcessorGuid, PowerCfgHelper.ProcThrottleMaxGuid, bk.Dc.ToString());
                    _launcherForCommands.RunPowerCfg("/setactive", bk.SchemeGuid);
                }
            }
        };
    }

    // ====================== v0.2.2 夜間モード 色温度（Command プリセット・CloudStore blob） ======================
    //
    // ⚠️ 最難関：非公開バイナリ blob（HKCU CloudStore）。**全ロジックは NightLightHelper（純粋関数）に集約**し、
    //   ここは「生 blob 読取 → パッチ → 書込／Base64 退避→書戻し」の I/O だけを担う。
    // ・色温度2バイトだけ差し替え（他バイトは原本維持）＝形式揺れに強い。1バイトでも想定外なら Apply 中止。
    // ・復元は blob 丸ごと書戻し（Base64・フォーマット解釈不要＝常に安全）。
    // ・HKCU のため昇格不要。ON/OFF（別 blob）には触らない＝強度のみ。
    //
    // ⚠️ 即時反映しない（2026-06-11 ホスト Z370 実機検証で確定）：
    //   夜間モードサービスは設定 blob をライブ監視していない。色温度2バイトの書込は成功・永続し
    //   （CloudStore による巻き戻しも無し）、トグルしても値は保持されるが、**画面への反映には
    //   夜間モードを一度 OFF→ON するか再ログオンが必要**。タイムスタンプを現在時刻に進めても
    //   ライブ反映はしなかった＝bump は反映トリガーにならない。よって RestartScope=Logoff。
    // ⚠️ タイムスタンプ bump は無効のまま（反映に寄与せず・誤エンコードは blob 破損リスクのため触らない）。
    private const bool BumpTimestampOnApply = false;

    private SettingItem BuildNightLightItem()
    {
        var choices = NightLightHelper.PresetLabels();
        const string unreadable = "（読取不可・公式設定で変更してください）";

        return new SettingItem
        {
            Id = "desktop.nightLightStrength",
            Title = "夜間モード 色温度（ブルーライト軽減の強さ）",
            Description = "Windows の夜間モードの「強さ（色温度）」をプリセットから選びます。数値が小さいほど暖色（赤み）が強くなります。" +
                "夜間モードの ON/OFF やスケジュールは変更しません（強度のみ）。設定は Windows 非公開のバイナリ形式のため、" +
                "WinKnobs は色温度の部分だけを安全に書き換え、元の値は丸ごと退避します。読み取れない／形式が想定外の場合は書き込みを中止します。" +
                "⚠️ 画面にすぐ反映はされません：適用後に夜間モードを一度 OFF→ON する（アクションセンターの「夜間モード」タイル、または ms-settings:nightlight）か、" +
                "再ログオンすると反映されます。管理者権限は不要。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            RecommendedValue = "4000K（標準）",   // 中立（一律に変更を勧めない・現在値の表示が主目的）
            DefaultValue = "4000K（標準）",
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var blob = _registry.ReadBinary(NightLightHelper.SettingsKeyPath, NightLightHelper.ValueName);
                    if (blob != null && NightLightHelper.TryParseSettings(blob, out var kelvin))
                        return NightLightHelper.NearestPresetLabel(kelvin);
                    return unreadable;   // 3択外＝SyncEditableFromOriginal で先頭に丸められる（クラッシュしない）
                },
                Apply = label =>
                {
                    if (!NightLightHelper.TryKelvinFromLabel(label, out var kelvin))
                        throw new InvalidOperationException($"不明なプリセット「{label}」のため適用を中止しました。");

                    var blob = _registry.ReadBinary(NightLightHelper.SettingsKeyPath, NightLightHelper.ValueName);
                    var patched = NightLightHelper.BuildPatchedSettings(blob, kelvin, BumpTimestampOnApply);
                    if (patched == null)
                        throw new InvalidOperationException(
                            "夜間モードの設定データが読み取れない／想定外の形式のため、安全のため書き込みを中止しました。" +
                            "「設定 > システム > ディスプレイ > 夜間モード」（ms-settings:nightlight）から変更してください。");

                    _registry.WriteBinary(NightLightHelper.SettingsKeyPath, NightLightHelper.ValueName, patched);
                },
                Backup = () =>
                {
                    // blob 丸ごとを Base64 で退避（無ければ空文字＝Restore は no-op）。
                    var blob = _registry.ReadBinary(NightLightHelper.SettingsKeyPath, NightLightHelper.ValueName);
                    return blob == null ? "" : Convert.ToBase64String(blob);
                },
                Restore = token =>
                {
                    if (string.IsNullOrEmpty(token)) return;   // 退避時に blob が無かった＝戻すものが無い
                    // 🔴致命①候補（クロスレビュー 2026-06-14）：退避後に夜間モードのリセット／プロファイル再作成で
                    // blob キー自体が消えている場合、WriteBinary（CreateSubKey）が非公開 CloudStore キーを
                    // "復活生成" してしまう＝当方が構造を保証できないキーを外部から作る＝グレーアウト事故の温床。
                    // キーが現存する時だけ書き戻す（消えていれば復元はスキップ＝壊さない最優先・OS の再生成に委ねる）。
                    if (!_registry.KeyExists(NightLightHelper.SettingsKeyPath)) return;
                    var blob = Convert.FromBase64String(token);
                    _registry.WriteBinary(NightLightHelper.SettingsKeyPath, NightLightHelper.ValueName, blob);
                }
            }
        };
    }

    // ====================== PR3 SmartScreen（Command 3択） ======================

    private SettingItem BuildSmartScreenItem()
    {
        const string path = @"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer";
        const string valueName = "SmartScreenEnabled";
        var choices = new[] { "Off（無効）", "Prompt（既定・確認のみ）", "RequireAdmin（管理者承認必須）" };
        var values = new[] { "Off", "Prompt", "RequireAdmin" };

        return new SettingItem
        {
            Id = "privacy.smartScreen",
            Title = "SmartScreen 動作モード",
            Description = "Web からダウンロードしたファイルの実行時 SmartScreen 警告の動作。Off=警告なし / Prompt=既定 / RequireAdmin=管理者承認必須。管理者権限必須。",
            Category = CategoryType.Privacy,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[1],
            DefaultValue = choices[1],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<string>(path, valueName);
                    if (v == null) return choices[1];
                    var idx = Array.IndexOf(values, v);
                    return idx >= 0 ? choices[idx] : choices[1];
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 1;
                    _registry.WriteValue<string>(path, valueName, values[idx], Microsoft.Win32.RegistryValueKind.String);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<string>(path, valueName);
                    return JsonSerializer.Serialize(new StringBackup { ValueExisted = v != null, Value = v ?? "" });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<StringBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<string>(path, valueName, bk.Value, Microsoft.Win32.RegistryValueKind.String);
                    else
                        _registry.DeleteValue(path, valueName);
                }
            }
        };
    }

    // ====================== N1 TCP Auto-Tuning（netsh 経由） ======================

    private SettingItem BuildTcpAutoTuningItem()
    {
        var choices = new[] {
            "disabled（無効）",
            "highlyrestricted（高制限）",
            "restricted（制限）",
            "normal（既定・推奨）",
            "experimental（実験的）"
        };
        var values = new[] { "disabled", "highlyrestricted", "restricted", "normal", "experimental" };

        return new SettingItem
        {
            Id = "network.tcpAutoTuning",
            Title = "TCP 受信ウィンドウ自動調整（Auto-Tuning Level）",
            Description = "TCP 受信ウィンドウの自動最適化レベル。normal が既定でほとんどの環境で最適。古いルーターで通信不安定の場合は restricted や disabled に。netsh 経由。管理者権限必須。即時反映。",
            Category = CategoryType.Network,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[3],
            DefaultValue = choices[3],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () => ParseAutoTuningFromNetsh(values) is { } v ? choices[Array.IndexOf(values, v)] : choices[3],
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 3;
                    _launcherForCommands.RunNetsh("interface", "tcp", "set", "global", $"autotuninglevel={values[idx]}");
                },
                Backup = () =>
                {
                    var v = ParseAutoTuningFromNetsh(values) ?? "normal";
                    return JsonSerializer.Serialize(new StringBackup { ValueExisted = true, Value = v });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<StringBackup>(token);
                    if (bk == null) return;
                    _launcherForCommands.RunNetsh("interface", "tcp", "set", "global", $"autotuninglevel={bk.Value}");
                }
            }
        };
    }

    // 他項目（ECN Capability 等）の disabled を誤マッチしないよう、"Auto-Tuning" 行に絞ってからコロン後を読む
    private string? ParseAutoTuningFromNetsh(string[] validValues)
    {
        try
        {
            var output = _launcherForCommands.RunNetsh("interface", "tcp", "show", "global");
            foreach (var rawLine in output.Split('\n'))
            {
                var line = rawLine.TrimEnd('\r');
                if (!line.Contains("Auto-Tuning", StringComparison.OrdinalIgnoreCase)) continue;
                var colonIdx = line.IndexOf(':');
                if (colonIdx < 0) continue;
                var value = line.Substring(colonIdx + 1).Trim();
                return Array.IndexOf(validValues, value) >= 0 ? value : null;
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ====================== S1 アイコンキャッシュクリア（実行系 Command） ======================

    private SettingItem BuildIconCacheClearItem()
    {
        var choices = new[] { "未実行", "クリア実行" };

        return new SettingItem
        {
            Id = "shell.iconCacheClear",
            Title = "アイコンキャッシュをクリア",
            Description = "%LOCALAPPDATA%\\IconCache.db および ExplorerSTART\\IconCache_*.db を削除しエクスプローラを再起動します。アイコン表示が崩れたときの定番修復。クリック→「クリア実行」を選んで「適用」。⚠️ クリアは元に戻せません（戻すボタン無効）。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[1],
            DefaultValue = choices[0],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () => choices[0],   // 常に「未実行」表示
                Apply = label =>
                {
                    if (label != choices[1]) return;
                    var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    // 古い場所
                    var oldCache = System.IO.Path.Combine(local, "IconCache.db");
                    if (System.IO.File.Exists(oldCache))
                    {
                        try { System.IO.File.Delete(oldCache); } catch { }
                    }
                    // 新しい場所（Win10/11）
                    var explorerCache = System.IO.Path.Combine(local, "Microsoft", "Windows", "Explorer");
                    if (System.IO.Directory.Exists(explorerCache))
                    {
                        foreach (var f in System.IO.Directory.GetFiles(explorerCache, "iconcache_*.db"))
                        {
                            try { System.IO.File.Delete(f); } catch { }
                        }
                        foreach (var f in System.IO.Directory.GetFiles(explorerCache, "thumbcache_*.db"))
                        {
                            try { System.IO.File.Delete(f); } catch { }
                        }
                    }
                    // explorer 再起動
                    // 2026-05-20 cross-reviewer P-3：fire-and-forget で async 起動・UI スレッドブロック回避
                    _ = _launcherForCommands.RestartExplorerAsync(() => true);
                }
                // Backup/Restore なし → CanRestore = false → 「戻す」ボタン無効化
            }
        };
    }

    // ====================== S2 BagMRU クリア（実行系 Command） ======================

    private SettingItem BuildBagMRUClearItem()
    {
        const string bagMRU = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\BagMRU";
        const string bags = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags";
        var choices = new[] { "未実行", "クリア実行" };

        return new SettingItem
        {
            Id = "shell.bagMRUClear",
            Title = "エクスプローラのフォルダ表示設定をリセット（BagMRU クリア）",
            Description = "全フォルダの表示形式（詳細/大アイコン等）・列幅・ソート設定をリセットします。エクスプローラが「フォルダごとに表示方式を忘れる」「変な並び順になる」現象の修復。⚠️ クリアは元に戻せません。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[1],
            DefaultValue = choices[0],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () => choices[0],
                Apply = label =>
                {
                    if (label != choices[1]) return;
                    _registry.DeleteKeyTree(bagMRU);
                    _registry.DeleteKeyTree(bags);
                    // 2026-05-20 cross-reviewer P-3：fire-and-forget で async 起動・UI スレッドブロック回避
                    _ = _launcherForCommands.RestartExplorerAsync(() => true);
                }
            }
        };
    }

    /// <summary>PR3 / N1 用：String 値のバックアップトークン。</summary>
    private class StringBackup
    {
        public bool ValueExisted { get; set; }
        public string Value { get; set; } = "";
    }

    // ====================== L2 Prefetcher（Command 4択） ======================

    private SettingItem BuildPrefetcherItem()
    {
        const string path = @"HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters";
        const string valueName = "EnablePrefetcher";
        var choices = new[] {
            "無効 (0)",
            "アプリ起動のみ (1)",
            "ブート時のみ (2)",
            "両方有効 (3・既定)"
        };

        return new SettingItem
        {
            Id = "login.prefetcher",
            Title = "Prefetcher 動作モード（アプリ・ブート起動高速化）",
            Description = "Windows の Prefetcher（先読みキャッシュ）動作モード。0=無効 / 1=アプリのみ / 2=ブートのみ / 3=両方（既定）。SSD でも有効推奨（MS 公式）。HKLM 系で管理者権限必須。PC 再起動で反映。",
            Category = CategoryType.Login,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = "両方有効 (3・既定)",
            DefaultValue = "両方有効 (3・既定)",
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    return v switch
                    {
                        int i when i >= 0 && i < choices.Length => choices[i],
                        _ => choices[3]
                    };
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 3;
                    _registry.WriteValue<int>(path, valueName, idx, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    var idx = v is int i ? i : -1;
                    return JsonSerializer.Serialize(new IntBackup { ValueExisted = idx >= 0, Value = idx });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<IntBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, valueName, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, valueName);
                }
            }
        };
    }

    // ====================== K1 NTFS 8.3 名（fsutil 経由 Command） ======================
    // BUG-2 修正（v0.1.2-dev → v0.1.3）：fsutil behavior set disable8dot3 は 0/1/2/3 の 4 状態を取る
    //   旧実装は 0/1 の 2 択しか想定せず、Win11 既定の「2（ボリューム単位）」状態を Restore できなかった。
    //   K2 と同じ IntBackup ベースに統一して 4 択に拡張。

    private SettingItem BuildNtfs8dot3Item()
    {
        var choices = new[] {
            "全ボリューム有効 (0)",
            "全ボリューム無効 (1・推奨)",
            "ボリューム単位 (2・既定)",
            "各ボリューム個別 (3)"
        };

        return new SettingItem
        {
            Id = "system.ntfs8dot3",
            Title = "NTFS 8.3 名生成（fsutil 経由）",
            Description = "NTFS の MS-DOS 互換 8.3 名（例：PROGRA~1）を生成する/しないを 4 段階で切替。0=全ボリューム生成・1=全ボリューム停止（高速化）・2=ボリューム単位（Win11 既定）・3=各ボリューム個別設定。⚠️ 1 にすると一部レガシーソフト動作不可。管理者権限必須。PC 再起動で反映。",
            Category = CategoryType.System,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[1],
            DefaultValue = choices[2],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    try
                    {
                        // 出力例（英）: "The registry state is 2 (Per volume setting, the default)."
                        // 出力例（日）: "レジストリの状態は 2 です (ボリューム単位で設定します - 既定値)"
                        var output = _launcherForCommands.RunFsutil("behavior", "query", "disable8dot3");
                        var idx = ParseDisable8dot3Index(output);
                        return choices[idx];
                    }
                    catch
                    {
                        return choices[2];
                    }
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 2;
                    _launcherForCommands.RunFsutil("behavior", "set", "disable8dot3", idx.ToString());
                },
                Backup = () =>
                {
                    try
                    {
                        var output = _launcherForCommands.RunFsutil("behavior", "query", "disable8dot3");
                        var idx = ParseDisable8dot3Index(output);
                        return JsonSerializer.Serialize(new IntBackup { ValueExisted = true, Value = idx });
                    }
                    catch
                    {
                        return JsonSerializer.Serialize(new IntBackup { ValueExisted = true, Value = 2 });
                    }
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<IntBackup>(token);
                    if (bk == null) return;
                    _launcherForCommands.RunFsutil("behavior", "set", "disable8dot3", bk.Value.ToString());
                }
            }
        };
    }

    /// <summary>fsutil behavior query disable8dot3 の出力（英/日）から 0-3 の状態を抽出。</summary>
    private static int ParseDisable8dot3Index(string output)
    {
        if (string.IsNullOrWhiteSpace(output)) return 2;
        // 行毎に分割して disable8dot3 含む行 or 数字を抽出
        foreach (var line in output.Split('\n'))
        {
            // 「is X」「= X」「は X です」のパターン
            var m = System.Text.RegularExpressions.Regex.Match(line, @"(?:is|=|は)\s*([0-3])\b");
            if (m.Success) return int.Parse(m.Groups[1].Value);
        }
        // フォールバック：単独の 0-3 数字
        var fb = System.Text.RegularExpressions.Regex.Match(output, @"\b([0-3])\b");
        return fb.Success ? int.Parse(fb.Groups[1].Value) : 2;
    }

    // ====================== K2 NTFS LastAccess（fsutil 経由 Command・W7復活） ======================

    private SettingItem BuildNtfsLastAccessItem()
    {
        // fsutil behavior set disablelastaccess の 4 値（0/1/2/3）
        var choices = new[] {
            "ユーザー設定・更新する (0)",
            "ユーザー設定・更新しない (1)",
            "システム設定・更新する (2)",
            "システム設定・更新しない (3・既定)"
        };

        return new SettingItem
        {
            Id = "system.ntfsLastAccess",
            Title = "NTFS 最終アクセス時刻の更新",
            Description = "ファイル読込時に「最終アクセス時刻」を更新するか。無効化でファイル読込が高速化。Windows 既定（3）は「システムが SSD/HDD で自動判定して無効化」。⚠️ バックアップ・アンチウイルスソフトが時刻参照する場合は注意。管理者権限必須。PC 再起動で反映。",
            Category = CategoryType.System,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = "システム設定・更新しない (3・既定)",
            DefaultValue = "システム設定・更新しない (3・既定)",
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    try
                    {
                        // fsutil behavior query disablelastaccess → 「DisableLastAccess = 3」のような出力
                        var output = _launcherForCommands.RunFsutil("behavior", "query", "disablelastaccess");
                        for (int i = 0; i < choices.Length; i++)
                        {
                            if (output.EndsWith(i.ToString()) || output.Contains($"= {i}"))
                                return choices[i];
                        }
                        return choices[3];
                    }
                    catch
                    {
                        return choices[3];
                    }
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 3;
                    _launcherForCommands.RunFsutil("behavior", "set", "disablelastaccess", idx.ToString());
                },
                Backup = () =>
                {
                    try
                    {
                        var output = _launcherForCommands.RunFsutil("behavior", "query", "disablelastaccess");
                        for (int i = 0; i < choices.Length; i++)
                        {
                            if (output.EndsWith(i.ToString()) || output.Contains($"= {i}"))
                                return JsonSerializer.Serialize(new IntBackup { ValueExisted = true, Value = i });
                        }
                        return JsonSerializer.Serialize(new IntBackup { ValueExisted = true, Value = 3 });
                    }
                    catch
                    {
                        return JsonSerializer.Serialize(new IntBackup { ValueExisted = true, Value = 3 });
                    }
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<IntBackup>(token);
                    if (bk == null) return;
                    _launcherForCommands.RunFsutil("behavior", "set", "disablelastaccess", bk.Value.ToString());
                }
            }
        };
    }

    // ====================== P1 Win32PrioritySeparation（Command 3択） ======================

    private SettingItem BuildWin32PrioritySeparationItem()
    {
        const string path = @"HKLM\SYSTEM\CurrentControlSet\Control\PriorityControl";
        const string valueName = "Win32PrioritySeparation";
        // 値とラベルの組（10進数値）
        var choices = new[] {
            "バックグラウンド優先（18・サーバー向け）",
            "標準フォアグラウンド（2・既定）",
            "高フォアグラウンド（26・ゲーミング最適）"
        };
        var values = new[] { 18, 2, 26 };

        return new SettingItem
        {
            Id = "kernel.win32PrioritySeparation",
            Title = "CPU スケジューラ優先度（Win32 Priority Separation）",
            Description = "前景アプリ vs バックグラウンドの CPU 配分を制御。既定 2 → 26（ゲーミング最適）でフォアグラウンドに 3 倍 CPU。HKLM 系で管理者権限必須。PC 再起動で反映。",
            Category = CategoryType.Kernel,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.System,
            ValueType = SettingValueType.Command,
            RecommendedValue = choices[2],
            DefaultValue = choices[1],
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    if (v is int i)
                    {
                        var idx = Array.IndexOf(values, i);
                        if (idx >= 0) return choices[idx];
                    }
                    return choices[1];   // 既定
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 1;
                    _registry.WriteValue<int>(path, valueName, values[idx], Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    var existed = v is int;
                    var val = v is int i ? i : 2;
                    return JsonSerializer.Serialize(new IntBackup { ValueExisted = existed, Value = val });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<IntBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, valueName, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, valueName);
                }
            }
        };
    }

    /// <summary>L2 / P1 共通：DWord 整数のバックアップ用トークン。</summary>
    private class IntBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>K1 用：bool バックアップトークン。</summary>
    private class BoolBackup
    {
        public bool Value { get; set; }
    }

    // ====================== A3-2-c CommandAction 構築ヘルパー ======================

    /// <summary>
    /// W4：TaskbarSi（タスクバーアイコンサイズ）を Command 型で構築。
    /// DWord 0/1/2 を選択肢ラベル ["小", "中（既定）", "大"] にマップ。
    /// </summary>
    private SettingItem BuildTaskbarSizeItem()
    {
        const string path = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
        const string valueName = "TaskbarSi";
        var choices = new[] { "小", "中（既定）", "大" };

        return new SettingItem
        {
            Id = "desktop.taskbarSize",
            Title = "タスクバーアイコンサイズ（Win11）",
            Description = "Windows 11 のタスクバーアイコンサイズを小・中・大から選択。⚠️ Win11 24H2 以降では効かない場合あり（Microsoft の仕様変更）。エクスプローラ再起動で反映。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            RecommendedValue = "中（既定）",
            DefaultValue = "中（既定）",
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    return v switch
                    {
                        int i when i >= 0 && i < choices.Length => choices[i],
                        _ => choices[1]   // 既定：中
                    };
                },
                Apply = label =>
                {
                    var idx = Array.IndexOf(choices, label);
                    if (idx < 0) idx = 1;
                    _registry.WriteValue<int>(path, valueName, idx, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    // 現在の値を JSON トークンとして保存
                    var v = _registry.ReadValue<object>(path, valueName);
                    var idx = v is int i ? i : -1;   // -1 は「値なし」
                    return JsonSerializer.Serialize(new TaskbarSiBackup { ValueExisted = idx >= 0, Index = idx });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<TaskbarSiBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                    {
                        _registry.WriteValue<int>(path, valueName, bk.Index, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    else
                    {
                        _registry.DeleteValue(path, valueName);
                    }
                }
            }
        };
    }

    /// <summary>
    /// W5：クラシック右クリックメニュー（Win11 → Win10 風）を Command 型で構築。
    /// HKCU\Software\Classes\CLSID\{86ca1aa0-...}\InprocServer32 キーの存在で ON/OFF。
    /// plan-validator V-4：reg export では非存在状態を保存できないため、自前で JSON トークン保存。
    /// </summary>
    private SettingItem BuildClassicContextMenuItem()
    {
        const string clsidKey = @"HKCU\Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}";
        const string innerKey = clsidKey + @"\InprocServer32";
        var choices = new[] { "OFF（Win11 既定の新メニュー）", "ON（クラシック右クリック）" };

        return new SettingItem
        {
            Id = "shell.classicContextMenu",
            Title = "クラシック右クリックメニュー（Win10 風）",
            Description = "Windows 11 のシンプル右クリックメニューを Windows 10 までの「フル表示」に戻します。「その他のオプションを表示」を経由せず全項目即表示。エクスプローラ再起動で反映。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            RecommendedValue = "ON（クラシック右クリック）",
            DefaultValue = "OFF（Win11 既定の新メニュー）",
            Command = new CommandAction
            {
                Choices = choices,
                Read = () => _registry.KeyExists(innerKey) ? choices[1] : choices[0],
                Apply = label =>
                {
                    var on = label == choices[1];
                    if (on)
                    {
                        _registry.CreateKey(innerKey);
                    }
                    else
                    {
                        // CLSID キーごと削除
                        _registry.DeleteKeyTree(clsidKey);
                    }
                },
                Backup = () =>
                {
                    var on = _registry.KeyExists(innerKey);
                    return JsonSerializer.Serialize(new ClassicMenuBackup { KeyExisted = on });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<ClassicMenuBackup>(token);
                    if (bk == null) return;
                    if (bk.KeyExisted)
                    {
                        _registry.CreateKey(innerKey);
                    }
                    else
                    {
                        _registry.DeleteKeyTree(clsidKey);
                    }
                }
            }
        };
    }

    /// <summary>
    /// v0.1.9：アクセシビリティ「テキストのサイズ」を Command 型で構築。
    /// HKCU\Software\Microsoft\Accessibility\TextScaleFactor（REG_DWORD・100〜225）。
    /// 管理者不要・危険度 Green。完全反映はサインアウト→再ログイン（RestartScope.Logoff）。
    /// plan-validator 反映：
    ///   - Read は NearestPresetLabel（必ず Choices 内文字列＝ComboBox 整合）。
    ///   - Read/Backup とも `v is int i` で TaskbarSi と同型（ReadValue&lt;object&gt; 型変換の曖昧さ排除）。
    ///   - Backup は生値を保存（復元忠実性）。適用前が未設定なら Restore で DeleteValue＝未設定に戻す。
    /// </summary>
    private SettingItem BuildTextScaleItem()
    {
        var path = TextScaleHelper.RegistryPath;
        var valueName = TextScaleHelper.ValueName;

        return new SettingItem
        {
            Id = "accessibility.textScaleFactor",
            Title = "テキストのサイズ（文字を大きく）",
            Description = "Windows の文字サイズを変更します（設定 > アクセシビリティ > テキストのサイズ と同じ）。"
                + "4K など高解像度で文字が小さく感じる時に。100%（既定）〜225% から選べます。"
                + "画面全体の拡大率（DPI）とは別で、文字だけを大きくします。"
                + "⚠️ 完全に反映するにはサインアウト→再ログインが必要です（一部アプリは即時反映）。",
            Category = CategoryType.Accessibility,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            // 中立表示：拡大率を一律推奨しない（既定 100%）。プリセットに 135% は含めるが推奨マークは付けない。
            RecommendedValue = TextScaleHelper.Choices[0],
            DefaultValue = TextScaleHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = TextScaleHelper.Choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    // 値あり（int）→最近接プリセットラベル（必ず Choices 内）／値なし→既定
                    return v is int i
                        ? TextScaleHelper.NearestPresetLabel(i)
                        : TextScaleHelper.Choices[0];
                },
                Apply = label =>
                {
                    if (!TextScaleHelper.TryParseLabel(label, out var pct)) return;
                    var value = TextScaleHelper.ClampToRange(pct);
                    _registry.WriteValue<int>(path, valueName, value, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, valueName);
                    // 生値を保存（最近接ラベルではなく実値）＝忠実に戻すため
                    return JsonSerializer.Serialize(new TextScaleBackup
                    {
                        ValueExisted = v is int,
                        Value = v is int i ? i : 0
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<TextScaleBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                    {
                        _registry.WriteValue<int>(path, valueName, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    else
                    {
                        _registry.DeleteValue(path, valueName);   // 元が未設定なら値ごと削除して未設定へ
                    }
                }
            }
        };
    }

    /// <summary>
    /// v0.1.10：アクセシビリティ第2項目「マウスポインタのサイズ」を Command 型で構築。
    /// 2値書込：CursorSize(HKCU\…\Accessibility・1-15) ＋ CursorBaseSize(HKCU\Control Panel\Cursors・32-256)。
    /// 対応式 CursorBaseSize=32+(CursorSize-1)*16。書込後に SystemParametersInfo(SPI_SETCURSORS) で即時反映（best-effort）。
    /// plan-validator 反映：
    ///   - Backup の existed は `v != null`（REG_SZ 等でも existed=true・Restore でのデータ消失を防ぐ）。
    ///     値は CoerceInt で数値化し DWORD に正規化して保存/復元（＝Windows 設定アプリと同じく DWORD 化を意図的に採用）。
    ///   - Read は NearestPresetLabel（必ず Choices 内＝ComboBox 整合）。CursorSize 基準。
    /// </summary>
    private SettingItem BuildPointerSizeItem()
    {
        var accPath = CursorSizeHelper.AccessibilityPath;
        var accName = CursorSizeHelper.CursorSizeValue;
        var curPath = CursorSizeHelper.CursorsPath;
        var curName = CursorSizeHelper.CursorBaseSizeValue;

        return new SettingItem
        {
            Id = "accessibility.pointerSize",
            Title = "マウスポインタのサイズ（大きく）",
            Description = "マウスポインタ（矢印）を大きくします（設定 > アクセシビリティ > マウスポインターとタッチ と同じ）。"
                + "ポインタを見失いやすい・高解像度で小さく感じる時に。標準（1）〜最大（15）。"
                + "適用後、マウスを動かすと反映します（反映しない場合はサインアウト→再ログインで確実に反映）。"
                + "⚠️ Windows の「設定」アプリを開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Accessibility,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            // 中立表示：サイズを一律推奨しない（既定＝標準）。
            RecommendedValue = CursorSizeHelper.Choices[0],
            DefaultValue = CursorSizeHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = CursorSizeHelper.Choices,
                Read = () =>
                {
                    // CursorSize（設定アプリのスライダー値）基準。値あり→最近接プリセット／なし→標準
                    var v = _registry.ReadValue<object>(accPath, accName);
                    return v is int i
                        ? CursorSizeHelper.NearestPresetLabel(i)
                        : CursorSizeHelper.Choices[0];
                },
                Apply = label =>
                {
                    if (!CursorSizeHelper.TryParseLabel(label, out var size)) return;
                    size = CursorSizeHelper.ClampSize(size);
                    var baseSize = CursorSizeHelper.ToBaseSize(size);
                    // 2値とも書く（設定アプリのスライダー表示と実描画を一致させる）
                    _registry.WriteValue<int>(accPath, accName, size, Microsoft.Win32.RegistryValueKind.DWord);
                    _registry.WriteValue<int>(curPath, curName, baseSize, Microsoft.Win32.RegistryValueKind.DWord);
                    SystemCursorService.ReloadSystemCursors();   // best-effort 即時反映
                },
                Backup = () =>
                {
                    var acc = _registry.ReadValue<object>(accPath, accName);
                    var cur = _registry.ReadValue<object>(curPath, curName);
                    // existed は v != null（REG_SZ でも true）。値は数値化（DWORD 正規化で復元）
                    return JsonSerializer.Serialize(new PointerSizeBackup
                    {
                        CursorSizeExisted = acc != null,
                        CursorSize = CursorSizeHelper.CoerceInt(acc, CursorSizeHelper.MinSize),
                        BaseSizeExisted = cur != null,
                        BaseSize = CursorSizeHelper.CoerceInt(cur, CursorSizeHelper.BaseSizeForMin)
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<PointerSizeBackup>(token);
                    if (bk == null) return;
                    if (bk.CursorSizeExisted)
                        _registry.WriteValue<int>(accPath, accName, bk.CursorSize, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(accPath, accName);

                    if (bk.BaseSizeExisted)
                        _registry.WriteValue<int>(curPath, curName, bk.BaseSize, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(curPath, curName);

                    SystemCursorService.ReloadSystemCursors();
                }
            }
        };
    }

    /// <summary>
    /// v0.1.11：アクセシビリティ第3項目「スクロールバーを常に表示」を Command 型2択で構築。
    /// HKCU\Control Panel\Accessibility\DynamicScrollbars（DWORD・0=常に表示 / 1 or 未設定=既定）。
    /// 宣言的 Boolean ではなく Command＋JSON 退避にする理由：DynamicScrollbars は既定で未設定のことが多く、
    /// .reg エクスポート退避だと「新規作成→戻す」で値が消えず戻せない（TaskbarSi と同じ理由）。
    /// plan-validator 反映：
    ///   - Read/ラベルは ScrollbarHelper の Choices 同一参照のみ（リテラル直書き禁止＝黙って既定化を防ぐ）。
    ///   - Read/Backup とも CoerceInt で REG_SZ "0"/"1" 環境にも対応。existed は v != null。
    /// </summary>
    private SettingItem BuildScrollbarItem()
    {
        var path = ScrollbarHelper.RegistryPath;
        var name = ScrollbarHelper.ValueName;

        return new SettingItem
        {
            Id = "accessibility.alwaysShowScrollbars",
            Title = "スクロールバーを常に表示",
            Description = "スクロールバーを常に表示します（設定 > アクセシビリティ > 視覚効果 と同じ）。"
                + "Windows 11 既定では、使う時だけ表示され普段は細い線に隠れます。常に表示すると今どこを見ているかが分かりやすくなります。"
                + "⚠️ 設定アプリやストアアプリには効きますが、エクスプローラなど一部の従来型アプリには反映されません。"
                + "完全反映はサインアウト→再ログイン（または対象アプリの再起動）。"
                + "⚠️ Windows の「設定」を開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Accessibility,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            // 中立表示：一律「常に表示」を推さない（既定＝使う時だけ表示）。
            RecommendedValue = ScrollbarHelper.Choices[0],
            DefaultValue = ScrollbarHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = ScrollbarHelper.Choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 未設定→null（既定ラベル）／値あり→CoerceInt（REG_SZ "0" も拾う）→ラベル（必ず Choices 内）
                    int? iv = v == null ? (int?)null : ScrollbarHelper.CoerceInt(v, ScrollbarHelper.ValueAutoHide);
                    return ScrollbarHelper.LabelForValue(iv);
                },
                Apply = label =>
                {
                    _registry.WriteValue<int>(path, name,
                        ScrollbarHelper.ValueForLabel(label), Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    return JsonSerializer.Serialize(new ScrollbarBackup
                    {
                        ValueExisted = v != null,
                        Value = ScrollbarHelper.CoerceInt(v, ScrollbarHelper.ValueAutoHide)
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<ScrollbarBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, name);   // 元が未設定なら削除して未設定へ戻す
                }
            }
        };
    }

    /// <summary>
    /// v0.1.12：アクセシビリティ第4項目「テキストカーソルの太さ」を Command 型で構築。
    /// HKCU\Control Panel\Desktop\CaretWidth（REG_DWORD・1〜20px）。テキストカーソル（点滅する縦棒｜）の太さ。
    /// 管理者不要・危険度 Green・完全反映はサインアウト→再ログイン（RestartScope.Logoff）。
    /// プリセット範囲型は TextScaleFactor(v0.1.9) と同型。Read の REG_SZ 対応・CoerceInt は
    /// Scrollbar(v0.1.11)/Pointer(v0.1.10) を踏襲（手本2系統のいいとこ取り＝TextScale より Read が頑健）。
    /// plan-validator 反映：
    ///   - Read は NearestPresetLabel（必ず Choices 内＝ComboBox 整合）。CoerceInt で REG_SZ 混在も拾う。
    ///   - Backup の existed は v != null。値は CoerceInt で数値化し DWORD 正規化（異常型/未設定は MinWidth に寄る）。
    ///   - live-apply（SystemParametersInfo SPI_SETCARETWIDTH）は未検証ゆえ見送り（v0.1.13 検証候補・「API無し」とは断定しない）。
    /// </summary>
    private SettingItem BuildCaretWidthItem()
    {
        var path = CaretWidthHelper.RegistryPath;
        var name = CaretWidthHelper.ValueName;

        return new SettingItem
        {
            Id = "accessibility.caretWidth",
            Title = "テキストカーソルの太さ",
            Description = "文字入力時に点滅する縦棒（テキストカーソル｜）の太さを変更します（設定 > アクセシビリティ > テキスト カーソル > テキスト カーソルの太さ と同じ）。"
                + "入力位置を見失いやすい時に。1px（既定・最も細い）〜20px から選べます。"
                + "⚠️ 点滅の速さ（点滅速度）ではなく、縦棒の「太さ」を変える設定です。"
                + "⚠️ 完全に反映するにはサインアウト→再ログインが必要です。"
                + "⚠️ Windows の「設定」を開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Accessibility,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            // 中立表示：太さを一律推奨しない（既定 1px・推奨マークなし）。
            RecommendedValue = CaretWidthHelper.Choices[0],
            DefaultValue = CaretWidthHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = CaretWidthHelper.Choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 未設定→既定ラベル／値あり→CoerceInt（REG_SZ も拾う）→最近接プリセット（必ず Choices 内）
                    return v == null
                        ? CaretWidthHelper.Choices[0]
                        : CaretWidthHelper.NearestPresetLabel(CaretWidthHelper.CoerceInt(v, CaretWidthHelper.MinWidth));
                },
                Apply = label =>
                {
                    if (!CaretWidthHelper.TryParseLabel(label, out var px)) return;
                    _registry.WriteValue<int>(path, name,
                        CaretWidthHelper.ClampToRange(px), Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 生値を CoerceInt で数値化して保存（未設定なら existed=false）。DWORD 正規化。
                    return JsonSerializer.Serialize(new CaretWidthBackup
                    {
                        ValueExisted = v != null,
                        Value = CaretWidthHelper.CoerceInt(v, CaretWidthHelper.MinWidth)
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<CaretWidthBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, name);   // 元が未設定なら削除して未設定へ戻す
                }
            }
        };
    }

    /// <summary>
    /// v0.1.13：アクセシビリティ第5項目「通知を表示する時間」を Command 型で構築。
    /// HKCU\Control Panel\Accessibility\MessageDuration（REG_DWORD・秒・既定5）。管理者不要・危険度 Green。
    /// ラベルに「1分」「5分」が混じるため、ラベル⇔値は MessageDurationHelper の明示マッピング（SecondsForLabel）を使う。
    /// plan-validator 反映：
    ///   - RestartScope=Logoff（None の「即時反映」断定を避ける。完全反映は再ログイン・CaretWidth と一貫）。
    ///   - Read は NearestPresetLabel（必ず Choices 内＝ComboBox 整合）。CoerceInt で REG_SZ 混在も拾う。
    ///   - Backup は生値（非プリセット値45秒等も忠実保存）。Restore は Clamp せず元の値を戻す（バックアップの契約）。
    ///     ※数値化できない異常型（REG_BINARY 等）は CoerceInt の fallback＝既定5秒に正規化される（実害極小・CaretWidth と同方針）。
    /// </summary>
    private SettingItem BuildMessageDurationItem()
    {
        var path = MessageDurationHelper.RegistryPath;
        var name = MessageDurationHelper.ValueName;

        return new SettingItem
        {
            Id = "accessibility.messageDuration",
            Title = "通知を表示する時間",
            Description = "画面に出る通知（トースト）を自動で消すまでの時間を変更します（設定 > アクセシビリティ > 視覚効果 >「通知を表示する時間」と同じ）。"
                + "通知を読み切れずに消えてしまう時に長くできます。5秒（既定）〜5分から選べます。"
                + "次に表示される通知から反映されます（完全反映はサインアウト→再ログイン）。"
                + "⚠️ Windows の「設定」を開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Accessibility,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Logoff,
            ValueType = SettingValueType.Command,
            // 中立表示：時間を一律推奨しない（既定 5秒・推奨マークなし）。
            RecommendedValue = MessageDurationHelper.Choices[0],
            DefaultValue = MessageDurationHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = MessageDurationHelper.Choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 未設定→既定ラベル／値あり→CoerceInt（REG_SZ も拾う）→最近接プリセット（必ず Choices 内）
                    return v == null
                        ? MessageDurationHelper.Choices[0]
                        : MessageDurationHelper.NearestPresetLabel(MessageDurationHelper.CoerceInt(v, MessageDurationHelper.MinSeconds));
                },
                Apply = label =>
                {
                    // ラベルは明示マッピングで秒数化（"1分"→60）。先頭数字方式は使わない。
                    _registry.WriteValue<int>(path, name,
                        MessageDurationHelper.SecondsForLabel(label), Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 生値を CoerceInt で数値化して保存（非プリセット値45秒等も忠実）。未設定なら existed=false。
                    return JsonSerializer.Serialize(new MessageDurationBackup
                    {
                        ValueExisted = v != null,
                        Value = MessageDurationHelper.CoerceInt(v, MessageDurationHelper.MinSeconds)
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<MessageDurationBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, name);   // 元が未設定なら削除して未設定へ戻す
                }
            }
        };
    }

    /// <summary>
    /// v0.1.14（システム・メンテナンス Phase 1）：「タスクマネージャを使えるようにする」を Command 型2択で構築。
    /// HKCU\…\Policies\System\DisableTaskMgr（DWORD・0/未設定=使える / 1=無効）。**管理者不要**（HKCU ポリシー）。
    /// 危険度 Yellow（自分をロックアウトしうるが WinKnobs でいつでも戻せる）。ScrollbarHelper 同型（Command＋JSON 退避）。
    /// plan-validator 反映：Read/ラベルは TaskManagerHelper の Choices 同一参照のみ。CoerceInt で REG_SZ 混在対応。未設定は Restore で DeleteValue。
    /// </summary>
    private SettingItem BuildTaskManagerItem()
    {
        var path = TaskManagerHelper.RegistryPath;
        var name = TaskManagerHelper.ValueName;

        return new SettingItem
        {
            Id = "maintenance.taskManager",
            Title = "タスクマネージャを使えるようにする",
            Description = "タスクマネージャ（Ctrl+Shift+Esc）を使えるようにするか、無効にするかを切り替えます"
                + "（グループポリシー『タスク マネージャーの削除』と同じ・HKCU）。"
                + "⚠️『無効にする』を選ぶと、Ctrl+Shift+Esc でもタスクバー右クリックメニューからもタスクマネージャを開けなくなります（WinKnobs からはいつでも『使える』に戻せます）。"
                + "管理者権限は不要です（自分のユーザー設定）。通常はすぐ反映（反映しない場合は再ログイン）。"
                + "⚠️ グループポリシーエディタ（gpedit.msc）等のポリシー編集ツールを開いている場合は閉じてから適用してください。",
            Category = CategoryType.Maintenance,
            Risk = RiskLevel.Yellow,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            // 中立表示：既定＝使える（Choices[0]）。無効化を推奨しない。
            RecommendedValue = TaskManagerHelper.Choices[0],
            DefaultValue = TaskManagerHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = TaskManagerHelper.Choices,
                Read = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    // 未設定→null（使える）／値あり→CoerceInt（REG_SZ "1" も拾う）→ラベル（必ず Choices 内）
                    int? iv = v == null ? (int?)null : TaskManagerHelper.CoerceInt(v, TaskManagerHelper.ValueEnabled);
                    return TaskManagerHelper.LabelForValue(iv);
                },
                Apply = label =>
                {
                    _registry.WriteValue<int>(path, name,
                        TaskManagerHelper.ValueForLabel(label), Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    var v = _registry.ReadValue<object>(path, name);
                    return JsonSerializer.Serialize(new TaskManagerBackup
                    {
                        ValueExisted = v != null,
                        Value = TaskManagerHelper.CoerceInt(v, TaskManagerHelper.ValueEnabled)
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<TaskManagerBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    else
                        _registry.DeleteValue(path, name);   // 元が未設定なら削除して未設定（使える）へ戻す
                }
            }
        };
    }

    /// <summary>
    /// v0.3.2（デスクトップ・UI）：「タスクバーの検索ボックスの表示」を Command 型4択で構築。
    /// HKCU\…\CurrentVersion\Search\SearchboxTaskbarMode（REG_DWORD・0=非表示/1=アイコン/2=ボックス/3=ボタン）。**管理者不要**（HKCU）。
    /// 値＝選択肢インデックスの直対応。MessageDuration/TaskManager 同型（Command＋JSON 退避）。
    /// 未設定・範囲外は「変更しない」センチネル表示（既定モードを誤表示しない）。Restore は未設定なら DeleteValue。
    /// </summary>
    private SettingItem BuildTaskbarSearchItem()
    {
        var path = SearchBoxModeHelper.RegistryPath;
        var name = SearchBoxModeHelper.ValueName;
        // 非適用センチネル（現状維持）＋実選択肢4件。Read がこれを返しても ComboBox に存在するので安全。
        const string keepLabel = "（変更しない・現在のまま）";
        var choices = new List<string> { keepLabel };
        choices.AddRange(SearchBoxModeHelper.Choices);

        return new SettingItem
        {
            Id = "desktop.taskbarSearch",
            Title = "タスクバーの検索ボックスの表示",
            Description = "タスクバーの検索の見せ方を切り替えます（設定 > 個人用設定 > タスクバー >「検索」と同じ）。"
                + "「表示しない」でタスクバーの幅を広く使えます。表示しない / 検索アイコンのみ / 検索ボックス（広い）/ 検索ボタン から選べます。"
                + "現在の値を読み取れない・未設定の場合は「（変更しない・現在のまま）」と表示し、その時は何も書き換えません。"
                + "管理者権限は不要です（自分のユーザー設定）。"
                + "⚠️ 反映にはエクスプローラの再起動（またはサインアウト→再ログイン）が必要な場合があります（WinKnobs は自動では再起動しません）。"
                + "⚠️ お使いの Windows のバージョンによっては「検索ボタン」が無く「検索ボックス」と同じ見た目になることがあります。"
                + "⚠️ Windows の「設定」を開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Desktop,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            // 中立表示：特定モードを推奨しない（センチネル＝変更しない）。
            RecommendedValue = keepLabel,
            DefaultValue = keepLabel,
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    // 管理対象（0..3）のみラベル化／未設定・範囲外・非数値はセンチネル（既定モードを誤表示しない）。
                    var v = _registry.ReadValue<object>(path, name);
                    return SearchBoxModeHelper.TryGetManagedMode(v, out var m)
                        ? SearchBoxModeHelper.Choices[m]
                        : keepLabel;
                },
                Apply = label =>
                {
                    var val = SearchBoxModeHelper.ValueForLabel(label);
                    if (val < 0) return;   // センチネル/不正＝書き込まない（no-op）
                    _registry.WriteValue<int>(path, name, val, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    // 管理対象（0..3）だけ ValueExisted=true で退避。非管理値（未設定/範囲外/非数値）は false 扱い＝
                    // Restore で誤って 0 を書かず DeleteValue で WinKnobs の書込みを取り消す（cross-reviewer 致命⑥/重要⑦ 反映）。
                    var v = _registry.ReadValue<object>(path, name);
                    bool managed = SearchBoxModeHelper.TryGetManagedMode(v, out var m);
                    return JsonSerializer.Serialize(new SearchBoxModeBackup { ValueExisted = managed, Value = m });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<SearchBoxModeBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                    {
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    else if (!_registry.DeleteValue(path, name))
                    {
                        // DeleteValue は失敗時 false（例外を投げない）。サイレント失敗を握り潰さず明示報告（cross-reviewer 致命⑥）。
                        throw new InvalidOperationException("タスクバー検索の設定を元に戻せませんでした（値の削除に失敗）。");
                    }
                }
            }
        };
    }

    /// <summary>
    /// v0.3.12（シェル）：「隠しファイル・フォルダーを表示」を Command 型2択で構築。
    /// HKCU\…\Explorer\Advanced\Hidden（REG_DWORD・1=表示/2=非表示）。**管理者不要**（HKCU）。
    /// SearchboxTaskbarMode 同型（Command＋JSON 退避）だが「レジストリ値 ≠ index」（1/2）を HiddenFilesHelper が吸収。
    /// 未設定・1/2 以外は「変更しない」センチネル表示（既定を誤表示しない）。Restore は管理外なら DeleteValue。
    /// </summary>
    private SettingItem BuildHiddenFilesItem()
    {
        var path = HiddenFilesHelper.RegistryPath;
        var name = HiddenFilesHelper.ValueName;
        // 非適用センチネル（現状維持）＋実選択肢2件。Read がこれを返しても ComboBox に存在するので安全。
        const string keepLabel = "（変更しない・現在のまま）";
        var choices = new List<string> { keepLabel };
        choices.AddRange(HiddenFilesHelper.Choices);

        return new SettingItem
        {
            Id = "shell.hiddenFiles",
            Title = "隠しファイル・フォルダーを表示",
            Description = "隠し属性が付いたファイル・フォルダーをエクスプローラに表示します"
                + "（エクスプローラの「表示 > 表示 > 隠しファイル」と同じ）。"
                + "「表示する」で隠しファイルが見え、「表示しない（既定）」で Windows 標準の隠れた状態に戻ります。"
                + "現在の値を読み取れない・未設定の場合は「（変更しない・現在のまま）」と表示し、その時は何も書き換えません。"
                + "管理者権限は不要です（自分のユーザー設定）。"
                + "⚠️ これは通常の隠しファイルのみです。hiberfil.sys 等のシステム保護ファイルは別項目「システム保護ファイルを表示（上級者向け）」で切り替えます。"
                + "⚠️ 反映にはエクスプローラの再起動（またはサインアウト→再ログイン）が必要な場合があります（WinKnobs は自動では再起動しません）。"
                + "⚠️ エクスプローラの「表示」メニューを開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Shell,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.Explorer,
            ValueType = SettingValueType.Command,
            // 中立表示：特定状態を推奨しない（センチネル＝変更しない）。
            RecommendedValue = keepLabel,
            DefaultValue = keepLabel,
            Command = new CommandAction
            {
                Choices = choices,
                Read = () =>
                {
                    // 管理対象（1/2）のみラベル化／未設定・範囲外・非数値はセンチネル（既定を誤表示しない）。
                    var v = _registry.ReadValue<object>(path, name);
                    return HiddenFilesHelper.TryGetManagedIndex(v, out var idx)
                        ? HiddenFilesHelper.Choices[idx]
                        : keepLabel;
                },
                Apply = label =>
                {
                    var val = HiddenFilesHelper.ValueForLabel(label);
                    if (val < 0) return;   // センチネル/不正＝書き込まない（no-op）
                    _registry.WriteValue<int>(path, name, val, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    // 管理対象（1/2）だけ ValueExisted=true で生のレジストリ値を退避。非管理値（未設定/範囲外/非数値）は
                    // false 扱い＝Restore で誤って 1/2 を書かず DeleteValue で WinKnobs の書込みを取り消す（OS 既定へ）。
                    var v = _registry.ReadValue<object>(path, name);
                    bool managed = HiddenFilesHelper.TryGetManagedIndex(v, out var idx);
                    return JsonSerializer.Serialize(new HiddenFilesBackup
                    {
                        ValueExisted = managed,
                        Value = managed ? HiddenFilesHelper.ValueForIndex(idx) : 0
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<HiddenFilesBackup>(token);
                    if (bk == null) return;
                    if (bk.ValueExisted)
                    {
                        _registry.WriteValue<int>(path, name, bk.Value, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                    else if (!_registry.DeleteValue(path, name))
                    {
                        // DeleteValue は失敗時 false（例外を投げない）。サイレント失敗を握り潰さず明示報告（SearchBoxMode 致命⑥ 踏襲）。
                        throw new InvalidOperationException("隠しファイル表示の設定を元に戻せませんでした（値の削除に失敗）。");
                    }
                }
            }
        };
    }

    /// <summary>
    /// v0.3.3（サウンド）：「システム効果音 ON/OFF」を Command 型2択で構築。
    /// HKCU\AppEvents\Schemes（管理者不要）。Scope B＝各イベントの .Current を直接書き換えて実際に消音する。
    /// 退避は SystemSoundService の first-touch-wins（maintenance ストア）に委譲＝Backup() はマーカーを返すだけ、
    /// Restore() は RestoreAll() に委譲（GraphicsService と同型・SearchBoxMode の Command 配線と同じ枠）。
    /// </summary>
    private SettingItem BuildSystemSoundItem()
    {
        return new SettingItem
        {
            Id = "sound.systemSoundsScheme",
            Title = "システム効果音 オン/オフ",
            Description = "Windows の効果音（通知音・警告音などのイベントサウンド）をまとめてオン/オフします"
                + "（設定 > システム > サウンド、または「サウンド」コントロールパネルの『サウンドなし』スキームと同じ効果）。"
                + "「効果音オフ（消音）」は各イベントのサウンドを空にして無音化します。"
                + "「効果音オン（標準）」は Windows 標準のサウンドに戻します。"
                + "（一部のアプリは標準のサウンドが登録されておらず、そのイベントの音は変更されません。）"
                + "⚠️『オン（標準）』を適用するとカスタム音は Windows 標準音に置き換わります。カスタム音へ戻すには『↩ 戻す』を使ってください。"
                + "個別の「↩ 戻す」で、WinKnobs が変更する前のサウンド設定（あなたのカスタム設定を含む）に正確に復元できます。"
                + "管理者権限は不要です（自分のユーザー設定）。"
                + "⚠️ この表示は選択中のサウンドスキーム基準のため、外部ツールで個別に変更した場合は実際の鳴り方と一致しないことがあります。"
                + "⚠️ 一部の常駐アプリは再起動するまで古い音が鳴ることがあります。"
                + "⚠️ Windows の「サウンド」設定／コントロールパネルを開いている場合は閉じてから適用してください（値が競合します）。",
            Category = CategoryType.Sound,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,   // 各 .Current を直接書くため以後の再生に即時反映（再ログイン不要）
            ValueType = SettingValueType.Command,
            // 中立表示：オン/オフどちらも推奨しない。
            RecommendedValue = SystemSoundHelper.Choices[0],
            DefaultValue = SystemSoundHelper.Choices[0],
            Command = new CommandAction
            {
                Choices = SystemSoundHelper.Choices.ToList(),
                Read = () => _systemSound.CurrentLabel(),
                Apply = label => _systemSound.SetMuted(SystemSoundHelper.IsMuteLabel(label)),
                // framework は初回 Apply 前に Backup を1回呼ぶ（SettingDialogViewModel S-1）。
                // ここで first-touch 退避を確定し、非null マーカーを返して CanRestore を有効化する。
                Backup = () =>
                {
                    _systemSound.BackupFirstTouch();
                    return SystemSoundService.FeatureKey;
                },
                Restore = _ => _systemSound.RestoreAll()
            }
        };
    }

    /// <summary>
    /// v0.3.8（ゲーミング）：「Game DVR（ゲームの録画機能）」を Command 型2択で構築。
    /// Game DVR は2つの HKCU 値で制御するため一括 ON/OFF（どちらも管理者不要）：
    ///   ① HKCU\System\GameConfigStore\GameDVR_Enabled（広域スイッチ）
    ///   ② HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR\AppCaptureEnabled（キャプチャ）
    /// 値 1=オン/0=オフ。Read は①基準（明示 0 のみ OFF）。Backup/Restore は両値を existed+値で退避し delete-or-throw。
    /// </summary>
    private SettingItem BuildGameDvrItem()
    {
        const string path1 = @"HKCU\System\GameConfigStore";
        const string name1 = "GameDVR_Enabled";
        const string path2 = @"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR";
        const string name2 = "AppCaptureEnabled";
        const string onLabel = "オン（録画機能を有効）";
        const string offLabel = "オフ（録画機能を無効）";

        return new SettingItem
        {
            Id = "gaming.gameDvr",
            Title = "Game DVR（ゲームの録画機能）",
            Description = "Xbox Game Bar のゲーム録画機能（Game DVR）のオン/オフをまとめて切り替えます"
                + "（設定 > ゲーム > キャプチャ に相当）。"
                + "オフにすると録画関連のバックグラウンド動作が止まり、低スペック機ではゲームが軽くなることがあります。"
                + "GameConfigStore の GameDVR_Enabled と GameDVR の AppCaptureEnabled の2つを同時に切り替えます。"
                + "片方だけ無効になっている場合も「オン」を選んで適用すれば両方そろいます。"
                + "管理者権限は不要です（自分のユーザー設定）。⚠️ 反映は次回のゲーム起動からです。",
            Category = CategoryType.Gaming,
            Risk = RiskLevel.Green,
            RestartScope = RestartScope.None,
            ValueType = SettingValueType.Command,
            RecommendedValue = onLabel,   // 中立（Windows 既定＝オン）
            DefaultValue = onLabel,
            Command = new CommandAction
            {
                Choices = new List<string> { onLabel, offLabel },
                Read = () =>
                {
                    // ①基準：明示的に 0 のときだけ OFF、それ以外（1/未設定）は ON。
                    var raw = _registry.ReadValue<object>(path1, name1);
                    int v = RegistryValueCoercion.CoerceInt(raw, 1);
                    return v == 0 ? offLabel : onLabel;
                },
                Apply = label =>
                {
                    int val = label == offLabel ? 0 : 1;
                    _registry.WriteValue<int>(path1, name1, val, Microsoft.Win32.RegistryValueKind.DWord);
                    _registry.WriteValue<int>(path2, name2, val, Microsoft.Win32.RegistryValueKind.DWord);
                },
                Backup = () =>
                {
                    // M-2：非数値（REG_BINARY 等で coerce 不可）は existed=false 扱い＝Restore で誤った型変換を防ぐ。
                    var (e1, v1) = ReadDwordManaged(path1, name1);
                    var (e2, v2) = ReadDwordManaged(path2, name2);
                    return JsonSerializer.Serialize(new GameDvrBackup
                    {
                        V1Existed = e1, V1 = v1,
                        V2Existed = e2, V2 = v2
                    });
                },
                Restore = token =>
                {
                    var bk = JsonSerializer.Deserialize<GameDvrBackup>(token);
                    if (bk == null) return;
                    // F-1：片方が失敗しても両方必ず試す（2値の不整合を防ぐ）＝集計して最後に throw。
                    var failures = new List<string>();
                    TryRestoreOne(path1, name1, bk.V1Existed, bk.V1, failures);
                    TryRestoreOne(path2, name2, bk.V2Existed, bk.V2, failures);
                    if (failures.Count > 0)
                        throw new InvalidOperationException(
                            $"Game DVR の設定を元に戻せませんでした（{string.Join("、", failures)}）。");
                }
            }
        };
    }

    /// <summary>
    /// 退避 DWORD を1つ書き戻す／未設定なら削除。失敗は failures に積む（F-1：片方の例外で他方を止めない）。
    /// 削除は「消えたか」を再読込で確認（F-2：キー/値が元から無い環境で DeleteValue=false を偽の失敗にしない）。
    /// </summary>
    private void TryRestoreOne(string path, string name, bool existed, int value, List<string> failures)
    {
        try
        {
            if (existed)
            {
                _registry.WriteValue<int>(path, name, value, Microsoft.Win32.RegistryValueKind.DWord);
            }
            else
            {
                _registry.DeleteValue(path, name);
                // DeleteValue はキー/値が無いときも false を返す。実際に値が残っているときだけ失敗扱い。
                if (_registry.ReadValue<object>(path, name) != null)
                    failures.Add($"{name} の削除に失敗");
            }
        }
        catch
        {
            failures.Add($"{name} の書き込みに失敗");
        }
    }

    /// <summary>
    /// HKCU の DWORD を「値の有無・int 値」で頑健に読む。非数値（REG_BINARY 等で coerce 不可）は
    /// existed=false 扱い＝退避時に「管理外」とし、Restore で誤った型変換（DWord 0 上書き）を防ぐ（M-2）。
    /// </summary>
    private (bool existed, int value) ReadDwordManaged(string path, string name)
    {
        var raw = _registry.ReadValue<object>(path, name);
        return RegistryValueCoercion.TryCoerceInt(raw, out var v) ? (true, v) : (false, 0);
    }

    /// <summary>v0.3.8 Game DVR 用バックアップトークン（2値・existed は値の有無）。</summary>
    private class GameDvrBackup
    {
        public bool V1Existed { get; set; }
        public int V1 { get; set; }
        public bool V2Existed { get; set; }
        public int V2 { get; set; }
    }

    /// <summary>W4 用バックアップトークン。</summary>
    private class TaskbarSiBackup
    {
        public bool ValueExisted { get; set; }
        public int Index { get; set; }
    }

    /// <summary>v0.1.9 テキストサイズ用バックアップトークン（生値保存）。</summary>
    private class TextScaleBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.1.10 マウスポインタサイズ用バックアップトークン（2値・existed は値の有無）。</summary>
    private class PointerSizeBackup
    {
        public bool CursorSizeExisted { get; set; }
        public int CursorSize { get; set; }
        public bool BaseSizeExisted { get; set; }
        public int BaseSize { get; set; }
    }

    /// <summary>v0.1.11 スクロールバー常時表示用バックアップトークン（existed は値の有無）。</summary>
    private class ScrollbarBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.1.12 テキストカーソルの太さ用バックアップトークン（existed は値の有無・生値保存）。</summary>
    private class CaretWidthBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.1.13 通知の表示時間用バックアップトークン（existed は値の有無・生値保存）。</summary>
    private class MessageDurationBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.1.14 タスクマネージャ（DisableTaskMgr）用バックアップトークン（existed は値の有無）。</summary>
    private class TaskManagerBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.3.2 タスクバー検索ボックス表示（SearchboxTaskbarMode）用バックアップトークン（existed は値の有無・生値保存）。</summary>
    private class SearchBoxModeBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>v0.3.12 隠しファイル表示（Hidden）用バックアップトークン（existed は管理値の有無・生のレジストリ値 1/2 を保存）。</summary>
    private class HiddenFilesBackup
    {
        public bool ValueExisted { get; set; }
        public int Value { get; set; }
    }

    /// <summary>W5 用バックアップトークン。</summary>
    private class ClassicMenuBackup
    {
        public bool KeyExisted { get; set; }
    }
}
