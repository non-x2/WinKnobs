using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// 外部プロセス起動の責務を集約するサービス。
/// regedit / コントロールパネル / msconfig / GodMode / シェル再起動 / 電源系。
/// </summary>
public class ExternalLauncherService
{
    /// <summary>レジストリエディタを開く。</summary>
    public bool OpenRegistryEditor() => Launch("regedit.exe", null);

    /// <summary>コントロールパネルを開く。</summary>
    public bool OpenControlPanel() => Launch("control.exe", null);

    /// <summary>システム構成ユーティリティ（msconfig）を開く。</summary>
    public bool OpenMsConfig() => Launch("msconfig.exe", null);

    /// <summary>
    /// GodMode（全コントロールパネル項目を一画面で表示）を開く。
    /// </summary>
    public bool OpenGodMode()
        => Launch("explorer.exe", "shell:::{ED7BA470-8E54-465E-825C-99712043E01C}");

    /// <summary>
    /// レジストリエディタで指定キーを開く。
    /// regedit の LastKey に書き込んでから起動する仕組み。
    /// </summary>
    public bool OpenRegistryAtKey(string keyPath)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
            // regedit の LastKey は "Computer\HKLM\..." 形式を期待する
            key?.SetValue("LastKey", "Computer\\" + keyPath);
        }
        catch
        {
            // 書込失敗してもregeditは開く
        }
        return Launch("regedit.exe", null);
    }

    /// <summary>
    /// エクスプローラ（シェル）を再起動する。
    /// confirm コールバックで事前確認できる。
    /// </summary>
    /// <remarks>
    /// 2026-05-20 cross-reviewer P-3 対応：UI スレッドブロック回避のため async 化。
    /// 旧版は Thread.Sleep(500) で UI スレッドを止めていた → ボタン連打や応答停止の原因。
    /// </remarks>
    public async Task<bool> RestartExplorerAsync(Func<bool>? confirm = null)
    {
        if (confirm != null && !confirm()) return false;

        try
        {
            var psi = new ProcessStartInfo("taskkill")
            {
                ArgumentList = { "/f", "/im", "explorer.exe" },
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var kill = Process.Start(psi);
            if (kill != null)
            {
                // WaitForExit(int) は同期 → WaitForExitAsync で UI スレッドブロック回避
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await kill.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            }
        }
        catch
        {
            return false;
        }

        // 念のため少し待ってから起動（async）
        await Task.Delay(500).ConfigureAwait(false);
        return Launch("explorer.exe", null);
    }

    /// <summary>
    /// 後方互換のための同期版（呼び出し元の即時移行が困難な場合用）。
    /// 新規コードは <see cref="RestartExplorerAsync"/> を使うこと。
    /// </summary>
    [Obsolete("Use RestartExplorerAsync for non-blocking UI thread")]
    public bool RestartExplorer(Func<bool>? confirm = null)
    {
        // GetAwaiter().GetResult() でデッドロック回避（ConfigureAwait(false) 済のため安全）
        return RestartExplorerAsync(confirm).GetAwaiter().GetResult();
    }

    /// <summary>ログオフ。</summary>
    public bool Logoff() => Launch("shutdown.exe", "/l");

    /// <summary>30秒後に再起動（/a で取り消し可）。</summary>
    public bool Restart()
        => Launch("shutdown.exe", "/r /t 30 /c \"WinKnobs: restart in 30s\"");

    /// <summary>30秒後にシャットダウン（/a で取り消し可）。</summary>
    public bool Shutdown()
        => Launch("shutdown.exe", "/s /t 30 /c \"WinKnobs: shutdown in 30s\"");

    /// <summary>予約された再起動・シャットダウンを取り消す。</summary>
    public bool AbortShutdown() => Launch("shutdown.exe", "/a");

    /// <summary>任意の URL をブラウザで開く（GitHub 等）。</summary>
    public bool OpenUrl(string url) => Launch(url, null);

    /// <summary>
    /// v0.1.7 機能0：Windows 純正のネットワーク設定画面を開く（開くだけ・設定値は変更しない・管理者不要）。
    /// 設定アプリの ms-settings: URI、またはクラシックな control.exe / ncpa.cpl を UseShellExecute で起動する。
    /// 例：OpenNetworkSettings("ms-settings:network-status") / OpenNetworkSettings("control.exe", "ncpa.cpl")
    /// </summary>
    /// <returns>起動に成功したら true。URI 非対応など失敗時は false（呼び出し側でフォールバック誘導）。</returns>
    public bool OpenNetworkSettings(string fileName, string? args = null) => Launch(fileName, args);

    /// <summary>
    /// v0.3.0：Windows 純正のグラフィック設定画面を開く（開くだけ・管理者不要）。
    /// 既定 ms-settings:display-advancedgraphics。非対応なら呼び出し側で "ms-settings:display" へフォールバック。
    /// </summary>
    public bool OpenGraphicsSettings(string fileName = "ms-settings:display-advancedgraphics") => Launch(fileName, null);

    /// <summary>
    /// v0.3.0：Windows 純正のパフォーマンスオプション（→ 詳細設定 → 仮想メモリ）を開く（開くだけ）。
    /// SystemPropertiesPerformance.exe＝スクショの純正ダイアログ。仮想メモリの安全弁（純正へ逃がす）。
    /// </summary>
    public bool OpenPerformanceOptions() => Launch("SystemPropertiesPerformance.exe", null);

    /// <summary>
    /// A3-2-c 拡張：fsutil を実行して標準出力を取得（A3-2-c W7 復活・CommandAction 用）。
    /// 例：RunFsutil("behavior", "set", "disable8dot3", "1")
    /// </summary>
    /// <returns>stdout の文字列（trim 済み）。exit code != 0 なら例外。</returns>
    public string RunFsutil(params string[] args)
        => RunExe(Path.Combine(Environment.SystemDirectory, "fsutil.exe"), args);

    /// <summary>
    /// netsh を実行して標準出力を取得（TCP Auto-Tuning 等 CommandAction 用・2026-05-16 追加）。
    /// 例：RunNetsh("interface", "tcp", "set", "global", "autotuninglevel=normal")
    /// </summary>
    public string RunNetsh(params string[] args)
        => RunExe(Path.Combine(Environment.SystemDirectory, "netsh.exe"), args);

    /// <summary>
    /// ipconfig を実行して標準出力を取得（v0.1.7 機能2 軽いリセット用）。例：RunIpconfig("/flushdns")。
    /// </summary>
    public string RunIpconfig(params string[] args)
        => RunExe(Path.Combine(Environment.SystemDirectory, "ipconfig.exe"), args);

    /// <summary>
    /// powercfg を実行して標準出力を取得（v0.2.1 Boost モード用）。
    /// 例：RunPowerCfg("/getactivescheme") / RunPowerCfg("/setacvalueindex", guid, "SUB_PROCESSOR", "PERFBOOSTMODE", "2")
    /// </summary>
    public string RunPowerCfg(params string[] args)
        => RunExe(Path.Combine(Environment.SystemDirectory, "powercfg.exe"), args);

    /// <summary>
    /// 汎用 exe 実行ヘルパー（SystemDirectory のフルパス推奨）。
    /// </summary>
    private static string RunExe(string exePath, string[] args)
    {
        var psi = new ProcessStartInfo(exePath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"{Path.GetFileName(exePath)} 起動失敗");
        // cross-reviewer I-2（2026-06-11）：stdout/stderr を同期で順に ReadToEnd すると、
        // stderr 側のパイプバッファが先に埋まった場合にデッドロックする。stderr は非同期で吸い、
        // タイムアウト（60秒）＋Kill(entireProcessTree) でハング経路も閉じる（HyperV/WSL 系と同水準）。
        var stderrTask = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit(60_000))
        {
            try { p.Kill(entireProcessTree: true); } catch { /* best-effort */ }
            throw new InvalidOperationException(
                $"{Path.GetFileName(exePath)} がタイムアウトしました（60秒）。");
        }
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (p.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"{Path.GetFileName(exePath)} exit code {p.ExitCode}: {stderr.Trim()}");
        }
        return stdout.Trim();
    }

    private static bool Launch(string fileName, string? args)
    {
        try
        {
            var psi = new ProcessStartInfo(fileName)
            {
                UseShellExecute = true
            };
            if (!string.IsNullOrEmpty(args))
            {
                psi.Arguments = args;
            }
            Process.Start(psi);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
