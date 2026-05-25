using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// テーマモード（ライト / ダーク / システム連動）
/// </summary>
public enum ThemeMode
{
    Light,
    Dark,
    System
}

/// <summary>
/// アプリ設定（settings.json で永続化）
/// </summary>
public class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
}

/// <summary>
/// テーマ切替・設定永続化サービス。
/// 致命度高（F-3）対策：IDisposable で SystemEvents.UserPreferenceChanged 購読解除。
/// エッジケース（E-1）対策：settings.json 破損時は try-catch で既定値フォールバック。
/// 致命度中（F-5）対策：保存先は %APPDATA%\WinKnobs\settings.json で BackupService と統一。
/// </summary>
public class ThemeService : IDisposable
{
    private readonly string _settingsPath;
    private AppSettings _settings = new();
    private bool _disposed;

    public event Action<ThemeMode>? ThemeChanged;

    public ThemeService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WinKnobs");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "settings.json");

        LoadSettings();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    /// <summary>
    /// 現在のテーマモード設定。
    /// </summary>
    public ThemeMode CurrentMode => _settings.Theme;

    /// <summary>
    /// テーマモードを切り替えて保存し、リソースを反映する。
    /// </summary>
    public void Apply(ThemeMode mode)
    {
        _settings.Theme = mode;
        SaveSettings();
        ApplyResources(ResolveActualTheme(mode));
        ThemeChanged?.Invoke(mode);
    }

    /// <summary>
    /// 起動時に現在の設定をリソースに反映する。
    /// </summary>
    public void ApplyOnStartup()
    {
        ApplyResources(ResolveActualTheme(_settings.Theme));
    }

    private bool ResolveActualTheme(ThemeMode mode) => mode switch
    {
        ThemeMode.Light => true,
        ThemeMode.Dark => false,
        ThemeMode.System => IsSystemLightTheme(),
        _ => true
    };

    private static bool IsSystemLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var v = key?.GetValue("AppsUseLightTheme");
            return v == null || Convert.ToInt32(v) != 0;
        }
        catch
        {
            return true; // 既定はライト
        }
    }

    private void ApplyResources(bool isLight)
    {
        var app = Application.Current;
        if (app == null) return;

        var resourceUri = new Uri(
            isLight ? "Themes/Light.xaml" : "Themes/Dark.xaml",
            UriKind.Relative);
        var newDict = new ResourceDictionary { Source = resourceUri };

        var mds = app.Resources.MergedDictionaries;

        // 既存テーマ辞書を除去（Themes/ で始まる Source を持つもの）
        for (int i = mds.Count - 1; i >= 0; i--)
        {
            var src = mds[i].Source?.OriginalString ?? "";
            if (src.IndexOf("Themes/", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mds.RemoveAt(i);
            }
        }

        mds.Add(newDict);
    }

    private void OnUserPreferenceChanged(object? sender, UserPreferenceChangedEventArgs e)
    {
        // システム連動モードの時だけ追従
        if (_settings.Theme != ThemeMode.System) return;

        if (e.Category == UserPreferenceCategory.General ||
            e.Category == UserPreferenceCategory.Color)
        {
            // UIスレッドへマーシャル
            Application.Current?.Dispatcher?.Invoke(() =>
                ApplyResources(IsSystemLightTheme()));
        }
    }

    private void LoadSettings()
    {
        if (!File.Exists(_settingsPath))
        {
            SaveSettings();
            return;
        }

        try
        {
            var json = File.ReadAllText(_settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json);
            if (loaded != null) _settings = loaded;
        }
        catch (JsonException)
        {
            // 破損していたら既定値で再生成（E-1 対策）
            _settings = new AppSettings();
            SaveSettings();
        }
        catch
        {
            // I/O エラー等はサイレントに既定値
            _settings = new AppSettings();
        }
    }

    private void SaveSettings()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                _settings,
                new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_settingsPath, json);
        }
        catch
        {
            // 保存失敗は無視（次回再試行）
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // F-3 対策：static イベント購読解除
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
    }
}
