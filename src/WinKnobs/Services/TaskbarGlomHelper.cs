using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.15「タスクバー ボタンの結合」用の純粋関数群（TaskbarGlomLevel・desktop.taskbarGlomLevel）。
/// I/O を持たず xUnit 対象。LaunchToHelper と同枠だが、本項目は **レジストリ値＝選択肢 index（0/1/2 が一致）** ＝
/// identity 変換（値≠index の吸収は不要）。それでも未設定/範囲外（管理外）の判定だけは Helper に閉じ込める。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced（管理者不要・HKCU）。
///   - TaskbarGlomLevel（REG_DWORD）：主タスクバー。
///   - MMTaskbarGlomLevel（REG_DWORD）：他モニタのタスクバー（24H2 で分割）。Apply は両方に同値を書く。
///   値：0=常に結合（ラベル非表示）/ 1=タスクバーに入りきらないとき / 2=結合しない（ラベル表示）。
///   **未設定時の OS 既定は 0（常に結合）** ＝ Read 未設定は choices[0] を返す（センチネルにしない＝実挙動と一致）。
///   反映はエクスプローラ再起動から（RestartScope.Explorer・WinKnobs は自動再起動しない）。
/// </summary>
public static class TaskbarGlomHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ValueName = "TaskbarGlomLevel";
    public const string MmValueName = "MMTaskbarGlomLevel";

    /// <summary>レジストリ値：常に結合（ラベル非表示）。OS 既定（未設定も同義）。</summary>
    public const int ValueAlways = 0;
    /// <summary>レジストリ値：タスクバーに入りきらないとき結合。</summary>
    public const int ValueWhenFull = 1;
    /// <summary>レジストリ値：結合しない（ラベル表示）。</summary>
    public const int ValueNever = 2;

    /// <summary>ComboBox 表示ラベル。index 0/1/2 がそのままレジストリ値 0/1/2 と一致（identity）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "常に結合（ラベル非表示）",            // index 0 → 値 0
        "タスクバーに入りきらないとき",         // index 1 → 値 1
        "結合しない（ラベル表示）"             // index 2 → 値 2
    };

    /// <summary>レジストリ値 → Choices index。0/1/2 はそのまま（identity）、それ以外（管理外）は -1。</summary>
    public static int IndexForValue(int value) => value switch
    {
        ValueAlways => 0,
        ValueWhenFull => 1,
        ValueNever => 2,
        _ => -1
    };

    /// <summary>Choices index → 書き込むレジストリ値。0/1/2 はそのまま（identity）、範囲外は -1。</summary>
    public static int ValueForIndex(int index) => index switch
    {
        0 => ValueAlways,
        1 => ValueWhenFull,
        2 => ValueNever,
        _ => -1
    };

    /// <summary>ラベル → 書き込むレジストリ値（0/1/2）。Choices に無いラベル（センチネル/不正/null）は -1。</summary>
    public static int ValueForLabel(string? label)
    {
        if (label == null) return -1;
        for (int i = 0; i < Choices.Count; i++)
        {
            if (ReferenceEquals(Choices[i], label) || Choices[i] == label)
                return ValueForIndex(i);
        }
        return -1;
    }

    /// <summary>レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。REG_SZ "1" 混在対応。</summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);

    /// <summary>
    /// レジストリ生値が「WinKnobs が管理する結合レベル（0/1/2）」かを判定し、該当時に <paramref name="index"/> を返す。
    /// 未設定（null）／非数値／範囲外（3 等）は false（＝管理外）。Read 側はこの false を choices[0]（常に結合＝OS 既定）に丸める。
    /// </summary>
    public static bool TryGetManagedIndex(object? raw, out int index)
    {
        index = 0;
        if (raw == null) return false;
        int coerced = CoerceInt(raw, -999);
        int idx = IndexForValue(coerced);
        if (idx < 0) return false;
        index = idx;
        return true;
    }
}
