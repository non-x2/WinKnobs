using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.12「隠しファイル・フォルダーを表示」用の純粋関数群（Hidden・shell.hiddenFiles）。
/// I/O を持たず xUnit 対象。Command 型2択。**SearchBoxModeHelper と同型だが「レジストリ値 ≠ 選択肢 index」**
/// が最大の差分（Hidden は 1=表示 / 2=非表示 で 0/1 ではない）ため、value↔index の対応を明示的に持つ。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced\Hidden（REG_DWORD）。
///   1=隠しファイルを表示 / 2=表示しない（Windows 既定）。管理者不要（HKCU）。
///   反映はエクスプローラ再起動（WinKnobs は自動再起動しない＝情報提示のみ・RestartScope.Explorer）。
///   ※ システム保護ファイル（hiberfil.sys 等）は別値 ShowSuperHidden（既存 shell.showSuperHidden）。本値は通常の隠し属性のみ。
///   ※ 0 やその他の値は Windows 標準でない（設定アプリは 1/2 のみ書く）ため「管理外」とみなしセンチネル表示にする。
///
/// plan-validator/cross-reviewer 反映（SearchBoxModeHelper 踏襲）：
///   - ValueForLabel は必ず Choices の同一参照を辿って値化（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - 未設定・非数値・1/2 以外は管理外＝呼び出し側が「変更しない」センチネル表示にする（既定モードの誤表示を防ぐ）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲（REG_SZ "1" 混在対応）。
/// </summary>
public static class HiddenFilesHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    public const string ValueName = "Hidden";

    /// <summary>レジストリ値：隠しファイルを表示。</summary>
    public const int ValueShow = 1;
    /// <summary>レジストリ値：表示しない（Windows 既定）。</summary>
    public const int ValueHide = 2;

    /// <summary>ComboBox 表示ラベル。index 0=表示する / index 1=表示しない。
    /// ※ index はレジストリ値そのものではない（<see cref="ValueForIndex"/> で 1/2 へ変換）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "表示する",            // index 0 → レジストリ値 1
        "表示しない（既定）"   // index 1 → レジストリ値 2
    };

    /// <summary>レジストリ値（1/2）→ Choices index（0/1）。管理外の値は -1。</summary>
    public static int IndexForValue(int value) => value switch
    {
        ValueShow => 0,
        ValueHide => 1,
        _ => -1
    };

    /// <summary>Choices index（0/1）→ レジストリ値（1/2）。範囲外は -1。</summary>
    public static int ValueForIndex(int index) => index switch
    {
        0 => ValueShow,
        1 => ValueHide,
        _ => -1
    };

    /// <summary>
    /// ラベル → 書き込むレジストリ値（1/2）。Choices に無いラベル（センチネル/不正）は -1（＝書き込まない目印）。
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

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "1"/0x1 混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);

    /// <summary>
    /// レジストリ生値が「WinKnobs が管理する有効値（1=表示 / 2=非表示）」かを判定し、該当時に
    /// <paramref name="index"/>（0/1）を返す。未設定（null）／非数値（REG_BINARY 等で coerce 不可）／
    /// 1・2 以外（例 0）は false（＝WinKnobs 管理外）。
    ///
    /// cross-reviewer 反映（SearchBoxMode 致命⑥/重要⑦/⑧ 踏襲）：Read と Backup の「管理対象判定」をここに一元化して
    /// xUnit 可能にする。これにより Backup が非管理値を誤って退避→Restore で誤書き込みする事故を防ぐ
    /// （非管理値は ValueExisted=false 扱い＝Restore は DeleteValue で WinKnobs の書込みを取り消し、OS 既定へ戻す）。
    /// </summary>
    public static bool TryGetManagedIndex(object? raw, out int index)
    {
        index = 0;
        if (raw == null) return false;
        int coerced = CoerceInt(raw, -1);
        int idx = IndexForValue(coerced);
        if (idx < 0) return false;
        index = idx;
        return true;
    }
}
