using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinKnobs.Services;
using WinKnobs.ViewModels;
using WinKnobs.ViewModels.Graphics;
using WinKnobs.ViewModels.Items;
using WinKnobs.ViewModels.Network;
using WinKnobs.ViewModels.Startup;
using WinKnobs.Views;
using WinKnobs.Views.Startup;

namespace WinKnobs;

/// <summary>
/// WinKnobs アプリケーション本体。
/// DI（依存性注入）コンテナを構築し、メインウィンドウを起動する。
///
/// 採用済みの plan-validator 指摘：
/// - F-3：OnExit で ThemeService.Dispose を明示呼び出し（SystemEvents 購読解除）
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>
    /// 単一インスタンス制御（v0.3.1）。DI には入れない（host 起動前に判定する必要があるため App の素フィールド）。
    /// </summary>
    private SingleInstanceManager? _single;

    /// <summary>
    /// DI サービス取得用。MainWindow.xaml.cs などコード側から WindowStateService 等を取得するため公開。
    /// 2026-05-20 追加（ウインドウ位置記憶機能対応）
    /// </summary>
    public IServiceProvider Services => _host.Services;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) =>
            {
                // サービス層
                services.AddSingleton<RegistryService>();
                services.AddSingleton<BackupService>();
                services.AddSingleton<RestorePointService>();
                services.AddSingleton<SystemProtectionService>();   // v0.1.15 システム・メンテナンス Phase 2
                services.AddSingleton<MemoryCompressionService>();   // v0.1.17 システム・メンテナンス Phase 3
                services.AddSingleton<SysmainService>();              // v0.2.0 Quick Win：Sysmain サービス 有効/無効
                services.AddSingleton<WslService>();                  // v0.2.1 Step3：WSL 2 機能 ON/OFF（2機能 done-stack・300秒）
                services.AddSingleton<CoreIsolationService>();        // v0.2.1 Step4：コア分離（HVCI）ON/OFF（🔴・レジストリ直・RegistryService 依存）
                services.AddSingleton<HyperVService>();               // v0.2.1 Step5：Hyper-V launchtype ON/OFF（🔴・bcdedit・BackupService 依存）
                services.AddSingleton<RemoteDesktopService>();        // v0.1.18 OFF / v0.1.19 ON＋状態読取＋退避（システム・メンテナンス Phase 4・🔴Red・BackupService 依存）
                services.AddSingleton<ThemeService>();
                services.AddSingleton<ExternalLauncherService>();
                services.AddSingleton<NetworkService>();      // v0.1.7：ネットワーク機能
                services.AddSingleton<NicPowerService>();     // v0.2.2 Step2：NIC省電力プリセット（PowerShell・要管理者・BackupService 依存）
                services.AddSingleton<GraphicsService>();     // v0.3.0：グラフィック（アプリ別GPU・全体トグル・HKCU・BackupService 依存）
                services.AddSingleton<VirtualMemoryService>();// v0.3.0：仮想メモリ（ページファイル・🔴・PowerShell/CIM・要管理者・BackupService 依存）
                services.AddSingleton<SystemSoundService>();  // v0.3.3：システム効果音 ON/OFF（HKCU・管理者不要・first-touch-wins 退避・RegistryService+BackupService 依存／Singleton 必須）
                services.AddSingleton<EnvironmentInfoService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<PrivilegeService>();   // v0.1.4 BUG-3 対応
                services.AddSingleton<WindowStateService>(); // 2026-05-20 ウィンドウ位置記憶

                // スタートアップ系（Phase R-1〜R-3）：状態保持＋シングルトン共有
                services.AddSingleton<StartupItemsProvider>();
                services.AddSingleton<StartupListViewModel>();

                // ネットワーク系（v0.1.7）：専用ペイン VM（状態保持＋シングルトン共有）
                services.AddSingleton<NetworkListViewModel>();

                // グラフィック系（v0.3.0）：専用ペイン VM（状態保持＋シングルトン共有）
                services.AddSingleton<GraphicsListViewModel>();

                // ViewModel
                services.AddTransient<MainViewModel>();
                services.AddTransient<SettingDialogViewModel>();
                services.AddTransient<StartupEntryEditViewModel>(); // Phase R-2
                services.AddTransient<RestorePreviewViewModel>();   // v0.1.6 機能 C
                services.AddTransient<RemoteDesktopEnableViewModel>(); // v0.1.19 Step3 Red ダイアログ
                services.AddTransient<HyperVDisableViewModel>();       // v0.2.1 Step5 Red ダイアログ
                services.AddTransient<VirtualMemoryViewModel>();        // v0.3.0 仮想メモリ Red ダイアログ

                // View
                services.AddTransient<MainWindow>();
                services.AddTransient<SettingDialog>();
                services.AddTransient<StartupEntryEditDialog>();    // Phase R-2
                services.AddTransient<RestorePreviewDialog>();      // v0.1.6 機能 C
                services.AddTransient<RemoteDesktopEnableDialog>();  // v0.1.19 Step3 Red ダイアログ
                services.AddTransient<HyperVDisableDialog>();        // v0.2.1 Step5 Red ダイアログ
                services.AddTransient<VirtualMemoryDialog>();        // v0.3.0 仮想メモリ Red ダイアログ
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        // ── 単一インスタンス化（v0.3.1）：2 個目は既存窓を起こして即終了 ──
        // 失敗時はフェイルオープン（プライマリ扱いで起動継続）＝単一化はアプリ起動を妨げない（重要4 反映）
        try
        {
            // 「管理者として再起動」した自プロセスは単一化の例外（旧プロセス解放を待って取得・自滅しない）。
            // ※将来 D&D/プロトコル等で引数を増やす時は、このフラグを先頭で消費してから他パーサーへ渡すこと（軽微1）。
            bool sanctionedRestart = Environment.GetCommandLineArgs()
                .Any(a => string.Equals(a, SingleInstanceNames.SanctionedRestartArg, StringComparison.OrdinalIgnoreCase));

            _single = new SingleInstanceManager();
            bool isPrimary = _single.TryAcquireOwnership(waitForRelease: sanctionedRestart);

            if (!isPrimary && !sanctionedRestart)
            {
                // 通常の 2 個目：既存（プライマリ）の窓を前面化させて自分は即終了
                _single.SignalPrimaryInstance();
                _single.Dispose();
                _single = null;
                // 窓 0 枚・ShutdownMode 非依存で確実終了（PrivilegeService と同じ保険パターン・致命2 反映）
                Environment.Exit(0);
                return;
            }

            if (isPrimary)
            {
                // 真のプライマリだけがリスナを張る（名前付きイベントの二重生成・シグナル横取りを防ぐ・致命1 反映）。
                // 起こされたら自分の窓を前面化。
                _single.StartListening(() => Dispatcher.Invoke(BringMainWindowToFront));
            }
            // else：sanctionedRestart=true でタイムアウト（旧プライマリが解放しなかった稀ケース）。
            //       リスナは張らず窓だけ出す（昇格再起動の目的＝窓を見せる を優先）。_single は OnExit で破棄。
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[SingleInstance] init failed (fail-open): {ex}");
            // フェイルオープン：中途半端な _single は確実に破棄し、単一化なしで通常起動を継続（重要3 反映）
            try { _single?.Dispose(); } catch { }
            _single = null;
        }

        await _host.StartAsync();

        // テーマ適用（settings.json から）
        var theme = _host.Services.GetRequiredService<ThemeService>();
        theme.ApplyOnStartup();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        MainWindow = window;   // 前面化（単一インスタンス）が Application.MainWindow を確実に参照できるよう明示
        window.Show();

        base.OnStartup(e);
    }

    /// <summary>
    /// 2 個目の起動で呼ばれ、既存（プライマリ）のメインウィンドウを前面化する（v0.3.1）。
    /// 自プロセスの自窓を Activate するだけ＝OS の前景ロック制限の対象外。のんさんの他作業の入力は奪わない。
    /// </summary>
    private void BringMainWindowToFront()
    {
        try
        {
            var w = MainWindow;
            if (w == null) return;

            if (w.WindowState == WindowState.Minimized)
            {
                w.WindowState = WindowState.Normal;
            }
            w.Show();   // Visibility=Collapsed 等で隠れている場合に備える（既定 SizeToContent=Manual なので位置記憶は侵さない・軽微3）
            w.Activate();

            // 子ダイアログ（設定/復元プレビュー/仮想メモリ等）が開いている時は Topmost ナッジを抑止。
            // ナッジは子窓を MainWindow の背後へ沈める既知事例があるため（重要1 反映）。
            if (Windows.Count <= 1)
            {
                w.Topmost = true;
                w.Topmost = false;
            }
            w.Focus();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"[SingleInstance] bring-to-front failed: {ex}");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        // 単一インスタンス制御の後片付け（Mutex 解放＋待機スレッド停止）。失敗してもアプリ終了は止めない。
        try
        {
            _single?.Dispose();
        }
        catch
        {
            // Dispose 失敗は致命的ではない
        }

        // F-3：ThemeService の SystemEvents 購読を明示解除（メモリリーク対策）
        try
        {
            _host.Services.GetRequiredService<ThemeService>().Dispose();
        }
        catch
        {
            // 失敗してもアプリ終了は止めない
        }

        await _host.StopAsync(TimeSpan.FromSeconds(5));
        _host.Dispose();
        base.OnExit(e);
    }
}
