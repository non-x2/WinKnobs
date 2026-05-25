using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using WinKnobs.Models;

namespace WinKnobs.Services;

/// <summary>
/// ウィンドウ位置・サイズの永続化サービス。
/// 作成日: 2026-05-20（ウインドウ状態の永続化サービス）
///
/// 設計ポイント：
/// - %APPDATA%\WinKnobs\window_state.json に JSON 保存
/// - マルチモニタ環境で画面外座標になる場合は既定値にフォールバック
/// - WindowState=Minimized で起動するのは不自然なので Normal/Maximized のみ保存
/// </summary>
public class WindowStateService
{
    private readonly string _filePath;

    public WindowStateService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "WinKnobs");
        if (!Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        _filePath = Path.Combine(dir, "window_state.json");
    }

    /// <summary>保存ファイルパス（テスト/デバッグ用）</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// 保存された状態を読み込む。
    /// ファイル不在/不正値/画面外座標の場合は null を返す（呼び出し側で既定値）。
    /// </summary>
    public WindowStateData? Load()
    {
        if (!File.Exists(_filePath)) return null;

        try
        {
            var json = File.ReadAllText(_filePath);
            var data = JsonSerializer.Deserialize<WindowStateData>(json);
            if (data == null || !data.IsValid) return null;

            // マルチモニタ対応：保存座標が現在の全モニタ領域から外れていれば null（既定値復元）
            if (!IsRectVisible(data.Left, data.Top, data.Width, data.Height))
            {
                return null;
            }

            return data;
        }
        catch
        {
            // JSON 破損等で読み込めない場合は null（既定値）
            return null;
        }
    }

    /// <summary>
    /// 現在のウィンドウ状態を保存する。
    /// WindowState=Minimized の場合は RestoreBounds から取得（Normal 扱い）。
    /// </summary>
    public void Save(Window window)
    {
        if (window == null) return;

        try
        {
            WindowStateData data;
            if (window.WindowState == System.Windows.WindowState.Minimized)
            {
                // Minimized は RestoreBounds（直前の Normal 状態）を保存
                var r = window.RestoreBounds;
                data = new WindowStateData
                {
                    Top = r.Top,
                    Left = r.Left,
                    Width = r.Width,
                    Height = r.Height,
                    WindowState = "Normal"
                };
            }
            else if (window.WindowState == System.Windows.WindowState.Maximized)
            {
                // Maximized は RestoreBounds に元の Normal サイズ・位置が入る
                var r = window.RestoreBounds;
                data = new WindowStateData
                {
                    Top = r.Top,
                    Left = r.Left,
                    Width = r.Width,
                    Height = r.Height,
                    WindowState = "Maximized"
                };
            }
            else
            {
                // Normal：現在の値そのまま
                data = new WindowStateData
                {
                    Top = window.Top,
                    Left = window.Left,
                    Width = window.Width,
                    Height = window.Height,
                    WindowState = "Normal"
                };
            }

            if (!data.IsValid) return;  // 不正値（NaN 等）は保存しない

            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
            {
                WriteIndented = true
            });
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // 保存失敗してもアプリ動作は止めない
        }
    }

    /// <summary>
    /// 矩形が仮想スクリーン領域（全モニタ統合）に十分含まれるか。
    /// WPF 標準の SystemParameters.VirtualScreen* を使用（System.Windows.Forms 依存なし）。
    /// タイトルバー中央付近（中央 ±100px、上 32px）が見えていれば OK とする。
    /// </summary>
    private static bool IsRectVisible(double left, double top, double width, double height)
    {
        // 全モニタを統合した仮想スクリーン領域
        var vLeft   = SystemParameters.VirtualScreenLeft;
        var vTop    = SystemParameters.VirtualScreenTop;
        var vRight  = vLeft + SystemParameters.VirtualScreenWidth;
        var vBottom = vTop  + SystemParameters.VirtualScreenHeight;

        // ウィンドウのタイトルバー中央付近の座標
        var titleCenterX = left + width / 2;
        var titleY       = top;

        return
            titleCenterX >= vLeft &&
            titleCenterX <= vRight &&
            titleY       >= vTop - 8 &&         // 上方向にちょい食み出しは許容
            titleY       <= vBottom - 32;
    }
}
