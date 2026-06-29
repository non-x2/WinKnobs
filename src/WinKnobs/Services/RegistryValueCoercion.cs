namespace WinKnobs.Services;

/// <summary>
/// レジストリ生値（object）を int に変換する共通ユーティリティ（v0.1.12 集約）。
/// REG_DWORD は int そのまま、REG_SZ "3"/"0" 等は int.TryParse、未対応型（REG_BINARY 等）・解釈不能・null は fallback。
/// existed の判定（v != null）は呼び出し側の責務＝本メソッドは「数値化」のみ。
///
/// 集約の経緯（Rule of Three）：
///   CursorSizeHelper(v0.1.10) / ScrollbarHelper(v0.1.11) / CaretWidthHelper(v0.1.12) で
///   同一実装が4箇所目に達したため、単一実装へ集約した。
///   各 Helper の CoerceInt は既存呼び出し側・xUnit の公開 API を壊さないよう本メソッドへ委譲する
///   前方互換ラッパとして残している（挙動・シグネチャは不変）。
/// </summary>
public static class RegistryValueCoercion
{
    /// <summary>
    /// レジストリ生値→int。int はそのまま／文字列は int.TryParse／それ以外・null は fallback。
    /// </summary>
    public static int CoerceInt(object? v, int fallback)
        => TryCoerceInt(v, out var value) ? value : fallback;

    /// <summary>
    /// レジストリ生値→int を試行（v0.3.8）。int / 文字列数値なら true＋値、未対応型（REG_BINARY 等）・
    /// 解釈不能・null は false。「値はあるが数値でない（＝管理外）」を fallback と区別したい時に使う
    /// （CoerceInt は fallback 返却で両者を区別できないため）。
    /// </summary>
    public static bool TryCoerceInt(object? v, out int value)
    {
        if (v is int i) { value = i; return true; }
        if (v != null && int.TryParse(v.ToString(), out var p)) { value = p; return true; }
        value = 0;
        return false;
    }
}
