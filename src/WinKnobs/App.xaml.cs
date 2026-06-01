using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WinKnobs.Services;
using WinKnobs.ViewModels;
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
                services.AddSingleton<ThemeService>();
                services.AddSingleton<ExternalLauncherService>();
                services.AddSingleton<EnvironmentInfoService>();
                services.AddSingleton<DialogService>();
                services.AddSingleton<PrivilegeService>();   // v0.1.4 BUG-3 対応
                services.AddSingleton<WindowStateService>(); // 2026-05-20 ウィンドウ位置記憶

                // スタートアップ系（Phase R-1〜R-3）：状態保持＋シングルトン共有
                services.AddSingleton<StartupItemsProvider>();
                services.AddSingleton<StartupListViewModel>();

                // ネットワーク系（v0.1.7）：専用ペイン VM（状態保持＋シングルトン共有）
                services.AddSingleton<NetworkListViewModel>();

                // ViewModel
                services.AddTransient<MainViewModel>();
                services.AddTransient<SettingDialogViewModel>();
                services.AddTransient<StartupEntryEditViewModel>(); // Phase R-2
                services.AddTransient<RestorePreviewViewModel>();   // v0.1.6 機能 C

                // View
                services.AddTransient<MainWindow>();
                services.AddTransient<SettingDialog>();
                services.AddTransient<StartupEntryEditDialog>();    // Phase R-2
                services.AddTransient<RestorePreviewDialog>();      // v0.1.6 機能 C
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        await _host.StartAsync();

        // テーマ適用（settings.json から）
        var theme = _host.Services.GetRequiredService<ThemeService>();
        theme.ApplyOnStartup();

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.DataContext = _host.Services.GetRequiredService<MainViewModel>();
        window.Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(ExitEventArgs e)
    {
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
