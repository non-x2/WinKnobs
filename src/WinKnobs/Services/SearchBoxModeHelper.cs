using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.2「タスクバー検索ボックスの表示」用の純粋関数群（SearchboxTaskbarMode・desktop.taskbarSearch）。
/// I/O を持たず xUnit 対象。Command 型4択プリセット。**レジストリ DWORD 値がそのまま選択肢インデックス**。
/// TaskManagerHelper / MessageDurationHelper と同型。
///
/// レジストリ：HKCU\Software\Microsoft\Windows\CurrentVersion\Search\SearchboxTaskbarMode（REG_DWORD）。
///   0=表示しない / 1=検索アイコンのみ / 2=検索ボックス / 3=検索ボタン。管理者不要（HKCU）。
///   反映はエクスプローラ再起動（WinKnobs は自動再起動しない＝情報提示のみ・RestartScope.Explorer）。
///   ※ SearchboxTaskbarModeCache は新規プロファイル既定用。現ユーザー切替は本値が主＝こちらのみ書く。
///
/// plan-validator 反映（TaskManagerHelper 踏襲）：
///   - LabelForValue は必ず Choices[n] の同一参照を返す（リテラル直書き禁止＝黙って既定化を防ぐ）。
///   - 範囲外/未設定は丸めず null を返し、呼び出し側が「変更しない」センチネル表示にする（既定モードの誤表示を防ぐ）。
///   - CoerceInt は共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲（REG_SZ "2" 混在対応）。
/// </summary>
public static class SearchBoxModeHelper
{
    public const string RegistryPath = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Search";
    public const string ValueName = "SearchboxTaskbarMode";

    /// <summary>有効モード値の範囲（= Choices の index）。</summary>
    public const int MinMode = 0;
    public const int MaxMode = 3;

    /// <summary>ComboBox 表示ラベル。index = レジストリ DWORD 値（0..3）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "表示しない（非表示）",   // 0
        "検索アイコンのみ",       // 1
        "検索ボックス（広い）",   // 2
        "検索ボタン"              // 3
    };

    /// <summary>モード値が有効範囲（0..3）か。</summary>
    public static bool IsValidMode(int value) => value >= MinMode && value <= MaxMode;

    /// <summary>
    /// レジストリ値→表示ラベル（必ず Choices 内の同一参照）。
    /// null（未設定）・範囲外は **null**（呼び出し側が「変更しない」センチネル表示にする＝既定モードの誤表示を防ぐ）。
    /// </summary>
    public static string? LabelForValue(int? value)
        => value is int v && IsValidMode(v) ? Choices[v] : null;

    /// <summary>
    /// ラベル→書き込むレジストリ値（0..3）。Choices に無いラベル（センチネル/不正）は -1（＝書き込まない目印）。
    /// </summary>
    public static int ValueForLabel(string? label)
    {
        if (label == null) return -1;
        for (int i = 0; i < Choices.Count; i++)
        {
            if (ReferenceEquals(Choices[i], label) || Choices[i] == label) return i;
        }
        return -1;
    }

    /// <summary>
    /// レジストリ生値→int（共通 <see cref="RegistryValueCoercion.CoerceInt"/> へ委譲）。
    /// REG_SZ "2"/0x2 混在環境でも数値化。existed 判定（v != null）は呼び出し側の責務。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);

    /// <summary>
    /// レジストリ生値が「WinKnobs が管理する有効モード（0..3）」かを判定し、該当時に <paramref name="mode"/> を返す。
    /// 未設定（null）／非数値（REG_BINARY 等で coerce 不可）／範囲外（例 5）は false（＝WinKnobs 管理外）。
    ///
    /// cross-reviewer 反映（致命⑥/重要⑦/⑧）：Read と Backup の「管理対象判定」をここに一元化して xUnit 可能にする。
    /// これにより Backup が非管理値を誤って 0（＝非表示）として退避→Restore で 0 を誤書き込みする事故を防ぐ
    /// （非管理値は ValueExisted=false 扱い＝Restore は DeleteValue で WinKnobs の書込みを取り消し、OS 既定へ戻す）。
    /// v0.3.0 グラフィックの「型/範囲外は管理外＝読まない・触らない」方針と整合。
    /// </summary>
    public static bool TryGetManagedMode(object? raw, out int mode)
    {
        mode = 0;
        if (raw == null) return false;
        int coerced = CoerceInt(raw, -1);
        if (!IsValidMode(coerced)) return false;
        mode = coerced;
        return true;
    }
}
