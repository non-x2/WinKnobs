using System;
using System.Runtime.InteropServices;

namespace WinKnobs.Services;

/// <summary>
/// マウス加速設定の即時反映（v0.2.1 Step1）。
/// レジストリ（HKCU\Control Panel\Mouse）を書いた後に呼ぶと再ログオン無しで反映される。
/// best-effort：失敗しても再ログオンで反映されるため呼び出し側は握り潰してよい（SystemCursorService 同型）。
///
/// SPI_SETMOUSE の pvParam は int[3]＝{ MouseThreshold1, MouseThreshold2, MouseSpeed(加速ON/OFF) }。
/// レジストリのキー名の並びと違う点に注意（変換は MouseAccelHelper.ToSpiArray に集約）。
/// </summary>
public static class MouseParamsService
{
    private const uint SPI_SETMOUSE = 0x0004;
    private const uint SPIF_UPDATEINIFILE = 0x01;
    private const uint SPIF_SENDCHANGE = 0x02;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint uiAction, uint uiParam, [In] int[] pvParam, uint fWinIni);

    /// <summary>
    /// SPI_SETMOUSE で加速設定を即時反映する（best-effort）。
    /// </summary>
    /// <param name="spiArray">MouseAccelHelper.ToSpiArray の戻り値（長さ3）。</param>
    /// <returns>API の成否。false でも再ログオンで反映されるため致命ではない。</returns>
    public static bool TryApply(int[] spiArray)
    {
        if (spiArray is not { Length: 3 }) return false;
        try
        {
            return SystemParametersInfo(SPI_SETMOUSE, 0, spiArray, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
        }
        catch
        {
            return false;
        }
    }
}
