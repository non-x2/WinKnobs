using System;

namespace WinKnobs.Services;

/// <summary>
/// マウス加速（ポインターの精度を高める）の純粋関数ヘルパー（v0.2.1 Step1）。
/// レジストリ：HKCU\Control Panel\Mouse の MouseSpeed / MouseThreshold1 / MouseThreshold2（いずれも REG_SZ）。
///   OFF（加速なし・1:1）＝ "0","0","0" ／ 既定（加速あり）＝ "1","6","10"。
///
/// ⚠️ plan-validator 重要3：SystemParametersInfo(SPI_SETMOUSE) に渡す int[3] の並びは
///   { MouseThreshold1, MouseThreshold2, MouseSpeed(加速ON/OFF) } で、
///   レジストリのキー名の並び（Speed→T1→T2）と一致しない。変換はここに集約し xUnit で検証する。
/// </summary>
public static class MouseAccelHelper
{
    public const string MouseKeyPath = @"HKCU\Control Panel\Mouse";
    public const string SpeedValueName = "MouseSpeed";
    public const string Threshold1ValueName = "MouseThreshold1";
    public const string Threshold2ValueName = "MouseThreshold2";

    /// <summary>Windows 既定値（加速あり）。</summary>
    public const string DefaultSpeed = "1";
    public const string DefaultThreshold1 = "6";
    public const string DefaultThreshold2 = "10";

    /// <summary>加速 OFF の値。</summary>
    public const string OffValue = "0";

    /// <summary>
    /// レジストリ3値（REG_SZ）から「加速 OFF か」を判定する。
    /// MouseSpeed=0 が本体の OFF スイッチ（Threshold は XP 時代のカーブ残骸）だが、
    /// 公式 UI のチェック OFF は3値とも 0 にするため、表示判定は MouseSpeed 単独で行う
    /// （Threshold だけ残っていても加速は効かない＝OFF 表示が実挙動と一致）。
    /// null（値なし）は Windows 既定＝加速あり扱い。
    /// </summary>
    public static bool IsAccelerationOff(string? speed) => speed == "0";

    /// <summary>
    /// SPI_SETMOUSE 用の int[3] を組み立てる。並びは { threshold1, threshold2, accelOnOff }。
    /// 文字列が数値として読めない場合は Windows 既定値で補う（壊れた値で API に渡さない）。
    /// </summary>
    public static int[] ToSpiArray(string? speed, string? threshold1, string? threshold2)
    {
        return new[]
        {
            ParseOrDefault(threshold1, 6),
            ParseOrDefault(threshold2, 10),
            ParseOrDefault(speed, 1) == 0 ? 0 : 1
        };
    }

    private static int ParseOrDefault(string? s, int fallback)
        => int.TryParse(s, out var v) && v >= 0 ? v : fallback;
}
