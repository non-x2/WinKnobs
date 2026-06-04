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
    {
        if (v is int i) return i;
        if (v != null && int.TryParse(v.ToString(), out var p)) return p;
        return fallback;
    }
}
