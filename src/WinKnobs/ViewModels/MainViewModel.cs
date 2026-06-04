using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Models;
using WinKnobs.Services;
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
        RegistryService registry,
        StartupListViewModel startupListVM,
        NetworkListViewModel networkListVM)
    {
        _dialogs = dialogs;
        _theme = theme;
        _launcher = launcher;
        _envInfo = envInfo;
        _backup = backup;
        _restore = restore;
        _registry = registry;
        _launcherForCommands = launcher;
        StartupListVM = startupListVM;
        NetworkListVM = networkListVM;

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
        IsStartupCategorySelected = isStartup;
        IsNetworkCategorySelected = isNetwork;
        // 既存 DataGrid は「Startup でも Network でもない」時だけ表示する（3 者択一）
        IsStandardCategorySelected = !(isStartup || isNetwork);

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

    [RelayCommand]
    private void CreateRestorePoint()
    {
        var ok = _dialogs.Confirm(
            "システムの復元ポイントを作成します。\n（管理者権限が必要・24時間に1回まで）\n\n続行しますか？",
            "復元ポイント");
        if (!ok) return;

        var result = _restore.Create("WinKnobs Manual Checkpoint", out var err);
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
                _dialogs.Error($"作成失敗: {err}", "失敗");
                StatusMessage = $"❌ {err}";
                break;
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
            "🪟 WinKnobs v0.1.14\n" +
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

    /// <summary>W5 用バックアップトークン。</summary>
    private class ClassicMenuBackup
    {
        public bool KeyExisted { get; set; }
    }
}
