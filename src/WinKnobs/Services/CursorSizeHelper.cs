using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// v0.1.10 アクセシビリティ「マウスポインタのサイズ」用の純粋関数群。
/// I/O を持たず xUnit 対象。プリセット選択式（Command 型）のためのラベル⇔値変換・範囲丸め・
/// CursorSize(1-15) ⇔ CursorBaseSize(32-256) のマッピングを提供する。
///
/// レジストリ（2値とも書く＝Windows 設定アプリと同挙動）：
///   - CursorSize     : HKCU\Software\Microsoft\Accessibility（DWORD・1-15・設定アプリのスライダー位置）
///   - CursorBaseSize : HKCU\Control Panel\Cursors（DWORD・32-256・実際の描画サイズ）
///   対応式：CursorBaseSize = 32 + (CursorSize - 1) * 16（1→32 / 2→48 / 15→256）
///
/// plan-validator 反映：
///   - NearestPresetLabel は Choices 配列から直接引く（戻り値は必ず Choices 内＝ComboBox 整合）。
///   - 距離計算は long で行い極端値（int.MinValue 等）のオーバーフローを回避。
///   - CoerceInt：レジストリ値が REG_SZ で格納された環境（古い Windows・3rd-party ツール）でも
///     型に関わらず int を取り出す（Backup の existed 誤判定＝データ消失を防ぐ）。
/// </summary>
public static class CursorSizeHelper
{
    /// <summary>CursorSize（設定アプリのスライダー値・1-15）。</summary>
    public const string AccessibilityPath = @"HKCU\Software\Microsoft\Accessibility";
    public const string CursorSizeValue = "CursorSize";

    /// <summary>CursorBaseSize（実描画サイズ・32-256）。</summary>
    public const string CursorsPath = @"HKCU\Control Panel\Cursors";
    public const string CursorBaseSizeValue = "CursorBaseSize";

    public const int MinSize = 1;
    public const int MaxSize = 15;
    public const int BaseSizeForMin = 32;   // CursorSize=1 → CursorBaseSize=32
    public const int BaseSizeStep = 16;     // 1 段ごとに +16

    /// <summary>CursorSize 実値のプリセット（Choices と同順・同数）。</summary>
    public static IReadOnlyList<int> PresetSizes { get; } = new[] { 1, 2, 3, 4, 5, 8, 11, 15 };

    /// <summary>ComboBox 表示ラベル（数字先頭＝TryParseLabel が効くように。先頭＝標準）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "1（標準）", "2", "3", "4", "5", "8", "11", "15"
    };

    /// <summary>CursorSize を有効範囲 [1, 15] に丸める。</summary>
    public static int ClampSize(int size)
        => size < MinSize ? MinSize : (size > MaxSize ? MaxSize : size);

    /// <summary>
    /// CursorSize(1-15) → CursorBaseSize(32-256)。式：32 + (size-1)*16。
    /// 範囲外入力は ClampSize 経由で必ず 32〜256 に収まる。
    /// </summary>
    public static int ToBaseSize(int cursorSize)
    {
        var s = ClampSize(cursorSize);
        return BaseSizeForMin + (s - 1) * BaseSizeStep;
    }

    /// <summary>
    /// ラベル先頭の整数を取り出す。"1（標準）"→1 / "11"→11 / "15"→15。
    /// 先頭に数字が無い・空なら false。
    /// </summary>
    public static bool TryParseLabel(string? label, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(label)) return false;

        int i = 0;
        while (i < label.Length && char.IsWhiteSpace(label[i])) i++;
        int start = i;
        while (i < label.Length && char.IsDigit(label[i])) i++;
        if (i == start) return false;

        return int.TryParse(label.Substring(start, i - start), out value);
    }

    /// <summary>
    /// 任意の CursorSize 値に最も近いプリセットのラベルを返す（必ず Choices 内）。
    /// Read 専用：ComboBox の SelectedIndex 整合のため。同距離なら小さい方を採用。
    /// </summary>
    public static string NearestPresetLabel(int cursorSize)
    {
        int bestIdx = 0;
        long bestDist = long.MaxValue;
        for (int idx = 0; idx < PresetSizes.Count; idx++)
        {
            // long 化で (1 - int.MinValue) 等のオーバーフローを回避
            long dist = System.Math.Abs((long)PresetSizes[idx] - cursorSize);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx = idx;
            }
        }
        return Choices[bestIdx];
    }

    /// <summary>
    /// レジストリ生値（object）を int に変換。Backup の値保存に使う（型混在環境 REG_SZ "128" 等でも値を保つ）。
    /// existed の判定（v != null）は呼び出し側で別途行う＝本メソッドは「値の数値化」のみ。
    /// ※ v0.1.12 で <see cref="RegistryValueCoercion.CoerceInt"/> へ集約済（Rule of Three・4箇所目）。
    ///   本メソッドは既存呼び出し側・xUnit との後方互換を保つ前方互換ラッパ（挙動・シグネチャ不変）。
    /// </summary>
    public static int CoerceInt(object? v, int fallback) => RegistryValueCoercion.CoerceInt(v, fallback);
}
