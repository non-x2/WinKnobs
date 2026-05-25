namespace WinKnobs.Models;

/// <summary>
/// ウィンドウ位置・サイズの永続化データ。
/// %APPDATA%\WinKnobs\window_state.json に JSON で保存される。
/// 作成日: 2026-05-20（起動位置・ウインドウサイズの記憶機能）
/// </summary>
public class WindowStateData
{
    /// <summary>画面左上 Y 座標（pixel）</summary>
    public double Top { get; set; }

    /// <summary>画面左上 X 座標（pixel）</summary>
    public double Left { get; set; }

    /// <summary>ウィンドウ幅（pixel）</summary>
    public double Width { get; set; }

    /// <summary>ウィンドウ高（pixel）</summary>
    public double Height { get; set; }

    /// <summary>WindowState 文字列（"Normal" / "Maximized"）。Minimized は復元しない（起動時に最小化される不自然挙動回避）</summary>
    public string WindowState { get; set; } = "Normal";

    /// <summary>このデータが有効か（不正値なら false）</summary>
    public bool IsValid =>
        !double.IsNaN(Top) && !double.IsNaN(Left) &&
        Width > 0 && Height > 0 &&
        (WindowState == "Normal" || WindowState == "Maximized");
}
