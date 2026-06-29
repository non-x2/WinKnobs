using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.14「エクスプローラの起動先」用の純粋関数群（LaunchTo・shell.launchTo）。
/// I/O を持たず xUnit 対象。Command 型3択。HiddenFilesHelper と同型で「レジストリ値 ≠ 選択肢 index」を吸収する。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\LaunchTo（REG_DWORD）。
///   1=この PC / 2=ホーム（クイックアクセス）/ 3=ダウンロード。管理者不要（HKCU）。
///   ※ 値 0 も「ホーム」として扱われる（GUI は 2 を書くが 0 の環境もある）→ 読取時は 0/2 とも「ホーム」表示。
///   ※ 値 4（OneDrive/モバイル）等は本ツールの3択に含めず「管理外」＝センチネル表示にする（誤表示防止）。
///   反映は次回エクスプローラ起動から（RestartScope.Explorer・WinKnobs は自動再起動しない）。
///
/// plan-validator/cross-reviewer 反映（SearchBoxMode/HiddenFiles 踏襲）：
///   - ValueForLabel は必ず Choices の同一参照を辿って値化（リテラル直書き禁止）。
///   - 管理外（未設定/非数値/3択外）はセンチネル表示で no-op、Restore は DeleteValue。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲。
/// </summary>
public static class LaunchToHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ValueName = "LaunchTo";

    /// <summary>レジストリ値：この PC。</summary>
    public const int ValueThisPc = 1;
    /// <summary>レジストリ値：ホーム（クイックアクセス）。GUI は 2 を書く（0 も同義）。</summary>
    public const int ValueHome = 2;
    /// <summary>レジストリ値：ダウンロード。</summary>
    public const int ValueDownloads = 3;

    /// <summary>ComboBox 表示ラベル。index 0=ホーム / 1=この PC / 2=ダウンロード。
    /// ※ index はレジストリ値そのものではない（<see cref="ValueForIndex"/> で 2/1/3 へ変換）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "ホーム（クイックアクセス）",  // index 0 → レジストリ値 2
        "この PC",                     // index 1 → レジストリ値 1
        "ダウンロード"                 // index 2 → レジストリ値 3
    };

    /// <summary>レジストリ値 → Choices index（0/1/2）。0/2→ホーム(0)、1→このPC(1)、3→ダウンロード(2)。管理外は -1。</summary>
    public static int IndexForValue(int value) => value switch
    {
        0 or ValueHome => 0,   // 0 も「ホーム」
        ValueThisPc => 1,
        ValueDownloads => 2,
        _ => -1                // 4（OneDrive）等は管理外
    };

    /// <summary>Choices index（0/1/2）→ 書き込むレジストリ値（2/1/3）。範囲外は -1。</summary>
    public static int ValueForIndex(int index) => index switch
    {
        0 => ValueHome,        // ホーム＝2 を書く（GUI と同じ）
        1 => ValueThisPc,
        2 => ValueDownloads,
        _ => -1
    };

    /// <summary>
    /// ラベル → 書き込むレジストリ値（2/1/3）。Choices に無いラベル（センチネル/不正）は -1（＝書き込まない目印）。
    /// </summary>
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
    /// レジストリ生値が「WinKnobs が管理する起動先（0/1/2/3）」かを判定し、該当時に <paramref name="index"/>（0/1/2）を返す。
    /// 未設定（null）／非数値／3択外（4 等）は false（＝WinKnobs 管理外＝センチネル表示・Restore は削除）。
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
