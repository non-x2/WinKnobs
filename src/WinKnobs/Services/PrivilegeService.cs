using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Windows;

namespace WinKnobs.Services;

/// <summary>
/// 管理者権限の判定と「管理者として再起動」を担当するサービス。
/// v0.1.4 BUG-3 対応で新設。
///
/// 採用済みの plan-validator 指摘：
/// - #4：UI 無効化フラグ（IsShutdownPending）で二重実行ガード
/// - エッジケース1：runas 起動失敗時にエラーが伝わるよう例外を区別
/// </summary>
public class PrivilegeService
{
    /// <summary>
    /// 管理者として再起動 → Shutdown が予約されたら true。
    /// DoApply 冒頭で参照して二重実行を防ぐ。
    /// </summary>
    public bool IsShutdownPending { get; private set; }

    public bool IsRunningAsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 自己プロセスを runas で再起動する。
    /// 成功（UAC 同意）した場合：true。呼び出し元は ShutdownCurrentProcess を呼んで現プロセスを終了する。
    /// 失敗（UAC 拒否・起動エラー）：false。errorMessage に詳細が入る。
    /// </summary>
    public bool TryRestartAsAdministrator(out string errorMessage)
    {
        errorMessage = string.Empty;
        try
        {
            var exePath = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(exePath))
            {
                errorMessage = "実行ファイルのパスを取得できませんでした。";
                return false;
            }

            var psi = new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                Verb = "runas",
                // v0.3.1：昇格再起動を単一インスタンス化の例外にする（旧プロセスの Mutex 解放を待って取得・自滅防止）。
                // 環境変数は UAC 昇格で届かないためコマンドライン引数で渡す（SingleInstanceNames 参照）。
                Arguments = SingleInstanceNames.SanctionedRestartArg,
                WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty
            };
            Process.Start(psi);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED：UAC で「いいえ」を選んだ
            errorMessage = "管理者起動がキャンセルされました（UAC ダイアログで「いいえ」を選択）。";
            return false;
        }
        catch (Win32Exception ex)
        {
            errorMessage = $"管理者起動に失敗しました（Win32 エラー {ex.NativeErrorCode}）：{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = $"管理者起動に失敗しました：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 現プロセスを正常終了。
    /// IsShutdownPending を立てて、再起動完了までの間に DoApply 等が二重実行されないようガード。
    ///
    /// 2026-05-20 追加（VM-3 検証で発覚した旧プロセス残存バグ修正）：
    /// Application.Current.Shutdown() だけだと _host.StopAsync(5sec) や残存スレッドで詰まって
    /// プロセスが終わらないケースがある（plan-validator #4 / cross-reviewer E-3 顕在化）。
    /// → 1500ms 経っても終わってなければ Environment.Exit(0) で確実に強制終了。
    /// </summary>
    public void ShutdownCurrentProcess()
    {
        IsShutdownPending = true;

        // 旧ウィンドウを即座に無効化（ユーザーが他のボタンを誤って押さないように）
        try
        {
            if (Application.Current?.MainWindow is { } main)
            {
                main.IsEnabled = false;
                main.Title += "（管理者として再起動中）";
            }
        }
        catch
        {
            // UI 無効化失敗は致命的ではない
        }

        // ① Graceful Shutdown 試行（WPF の Application 終了処理を走らせる）
        try { Application.Current?.Shutdown(); } catch { }

        // ② 保険：1500ms 経って終わってなければ強制終了（旧プロセス確実消滅）
        //    _host.StopAsync(5sec) で詰まる・残存スレッドが居る等の対策
        System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(1500);
            try
            {
                Environment.Exit(0);
            }
            catch
            {
                try { Process.GetCurrentProcess().Kill(); } catch { }
            }
        });
    }
}
