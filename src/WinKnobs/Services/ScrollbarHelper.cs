using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.11 アクセシビリティ「スクロールバーを常に表示」用の純粋関数群。
/// I/O を持たず xUnit 対象。Command 型2択（常に表示 / 使う時だけ表示）のラベル⇔値変換。
///
/// レジストリ：HKCU\Control Panel\Accessibility\DynamicScrollbars（REG_DWORD）。
///   0 = 常に表示 ／ 1（または未設定）= 使う時だけ表示（Windows 既定）。
///
/// plan-validator 反映：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝1バイトのズレで
///     SettingDialogViewModel の Choices.IndexOf が -1→黙って既定化する事故を防ぐ）。
///   - CoerceInt：DynamicScrollbars が REG_SZ "0"/"1" で格納された環境でも型に依らず数値化
///     （REG_SZ "0"＝常に表示 を取りこぼさない・v0.1.10 CursorBaseSize の REG_SZ 混在の教訓）。
/// </summary>
public static class ScrollbarHelper
{
    public const string RegistryPath = @"HKCU\Control Panel\Accessibility";
    public const string ValueName = "DynamicScrollbars";

    /// <summary>常に表示（レジストリ 0）。</summary>
    public const int ValueAlwaysShow = 0;
    /// <summary>使う時だけ表示＝Windows 既定（レジストリ 1・未設定も同挙動）。</summary>
    public const int ValueAutoHide = 1;

    /// <summary>ComboBox 表示ラベル（先頭＝既定）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "使う時だけ表示（既定）", "常に表示する"
    };

    /// <summary>既定（使う時だけ表示）ラベル＝Choices[0] の同一参照。</summary>
    public static string AutoHideLabel => Choices[0];
    /// <summary>常に表示ラベル＝Choices[1] の同一参照。</summary>
    public static string AlwaysShowLabel => Choices[1];

    /// <summary>レジストリ値→「常に表示か」。0=true／それ以外・未設定(null)=false。</summary>
    public static bool IsAlwaysShow(int? value) => value == ValueAlwaysShow;

    /// <summary>ラベル→書き込むレジストリ値。「常に表示する」→0／それ以外→1。</summary>
    public static int ValueForLabel(string? label)
        => label == AlwaysShowLabel ? ValueAlwaysShow : ValueAutoHide;

    /// <summary>レジストリ値→表示ラベル（必ず Choices 内の同一参照を返す）。</summary>
    public static string LabelForValue(int? value)
        => IsAlwaysShow(value) ? AlwaysShowLabel : AutoHideLabel;

    /// <summary>
    /// レジストリ生値（object）を int に変換。REG_DWORD は int、REG_SZ "0"/"1" は parse、
    /// それ以外/解釈不能は fallback。Read/Backup で型混在環境（REG_SZ）でも値を保つ。
    /// existed の判定（v != null）は呼び出し側で行う＝本メソッドは「値の数値化」のみ。
    /// ※ CursorSizeHelper.CoerceInt と同一コード（3箇所目）。4箇所目が出たら共通ユーティリティへ集約検討（Rule of Three）。
    /// </summary>
    public static int CoerceInt(object? v, int fallback)
    {
        if (v is int i) return i;
        if (v != null && int.TryParse(v.ToString(), out var p)) return p;
        return fallback;
    }
}
