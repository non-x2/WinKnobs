using System;
using System.Runtime.InteropServices;

namespace WinKnobs.Services;

/// <summary>
/// マウスカーソルの即時反映（v0.1.10）。
/// レジストリ（CursorSize / CursorBaseSize）を書いた後に呼ぶと、Windows がカーソルを
/// レジストリから再読込してサイズ変更を即時反映する。best-effort（失敗しても再ログインで反映）。
///
/// plan-validator 反映：user32.dll に `SystemParametersInfo` という実体は無く `…W`/`…A` のみ。
///   EntryPoint を `SystemParametersInfoW` で明示し EntryPointNotFound を回避（GlobalMemoryStatusEx は
///   サフィックス無しの実エクスポートだったため前例とは状況が異なる）。
/// </summary>
public static class SystemCursorService
{
    private const uint SPI_SETCURSORS = 0x0057;
    private const uint SPIF_SENDCHANGE = 0x02;   // 変更を WM_SETTINGCHANGE でブロードキャスト

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, IntPtr pvParam, uint fWinIni);

    /// <summary>
    /// システムカーソルをレジストリから再読込し、サイズ変更を即時反映する（best-effort）。
    /// 戻り値は API の成否。失敗（false / 例外）でもサインアウト→再ログインで反映されるため呼び出し側は握り潰してよい。
    /// </summary>
    public static bool ReloadSystemCursors()
    {
        try
        {
            return SystemParametersInfo(SPI_SETCURSORS, 0, IntPtr.Zero, SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }
}
