using System;
using System.Collections.Generic;

namespace WinKnobs.Services;

/// <summary>
/// 夜間モード「色温度（強度）」の CloudStore バイナリ blob を解析・パッチする純粋関数群
/// （v0.2.2 Step3・最難関）。I/O は一切持たない（レジストリ読み書きは NightLightService 側）。
///
/// ■ 対象 blob
///   HKCU\...\CloudStore\Store\DefaultAccount\Current\
///     default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings
///   の REG_BINARY 値 "Data"。**非公開フォーマット**（MS 非ドキュメント・コミュニティ解析）。
///
/// ■ 実機採取サンプル（Z370・2026-06-11・55バイト）で確定した構造
///   43 42 01 00            マジック "CB"+01 00
///   0A 02 01 00            （ヘッダ）
///   2A 06 C0 A3 A5 D1 06   タイムスタンプ：0x2A 0x06 の後に Unix 秒の protobuf varint（5バイト）
///                          C0 A3 A5 D1 06 → 1781092800 ≒ 2026-06（検算一致）
///   ... CF 28 94 3C ...    色温度：マーカー 0xCF 0x28 の直後 2バイト = [低位][高位]（リトルエンディアン）
///                          94 3C → 高位0x3C=60, 低位0x94=148 → 60*64 + (148-128)/2 = 3850K
///
/// ■ 色温度エンコード（コミュニティ確立式・実blobで検算済）
///   high = kelvin / 64（整数除算）
///   low  = (kelvin % 64) * 2 + 128
///   復号：kelvin = high * 64 + (low - 128) / 2
///
/// ■ 設計方針（壊さないことを最優先・設計ドラフト §3）
///   - **全再構築しない**：色温度2バイト（＋任意でタイムスタンプ5バイト）だけ差し替え、他バイトは原本維持。
///     OS ビルドによる形式揺れに強い。
///   - **1バイトでも想定外なら false**（TryParseSettings）。書込前は必ず TryParse 成功が前提。
///   - 範囲は公式 UI と同じ 1200K〜6500K にクランプ。
///   - 復元はフォーマット解釈不要（blob 丸ごと Base64 退避→書戻し）＝Service 側で常に安全。
///   - タイムスタンプ bump は **任意**（既定 false）。実機で「書いた値が CloudStore に戻される」場合のみ
///     有効化する。+1 秒なら varint 長は5バイトで不変＝blob 長を変えずに in-place 可能。
///     長さが変わる場合は **bump せず**（破損より「戻される」方が安全）。
/// </summary>
public static class NightLightHelper
{
    /// <summary>夜間モード設定 blob のレジストリキー（HKCU・$ はサブキー名の一部）。</summary>
    public const string SettingsKeyPath =
        @"HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.settings\windows.data.bluelightreduction.settings";

    public const string ValueName = "Data";

    /// <summary>色温度2バイトの直前に来るマーカー（0xCF 0x28）。</summary>
    public static readonly byte[] TempMarker = { 0xCF, 0x28 };

    /// <summary>blob 先頭マジック "CB"+01 00。</summary>
    public static readonly byte[] MagicHeader = { 0x43, 0x42, 0x01, 0x00 };

    public const int MinKelvin = 1200;
    public const int MaxKelvin = 6500;

    // パース時の妥当性窓（クランプ範囲より少し広く取り、想定外 blob を弾く）。
    private const int ParseMinKelvin = 1000;
    private const int ParseMaxKelvin = 7000;

    // タイムスタンプ varint の妥当性窓（Unix 秒・2017〜2049 年相当）。
    private const long UnixTsMin = 1_500_000_000L;
    private const long UnixTsMax = 2_500_000_000L;

    /// <summary>
    /// 色温度プリセット（ラベル, ケルビン）。弱い順（数値が大きいほど青白い＝弱い）。
    /// 6500K は実質ほぼ無効に近い。1200K が最も赤い（最強）。
    /// </summary>
    public static readonly IReadOnlyList<(string Label, int Kelvin)> Presets = new[]
    {
        ("6500K（ごく弱い）", 6500),
        ("5000K（弱い）", 5000),
        ("4000K（標準）", 4000),
        ("2700K（強い）", 2700),
        ("1200K（最も暖色）", 1200),
    };

    /// <summary>プリセットのラベル一覧（ComboBox 表示用）。</summary>
    public static IReadOnlyList<string> PresetLabels()
    {
        var list = new List<string>(Presets.Count);
        foreach (var p in Presets) list.Add(p.Label);
        return list;
    }

    /// <summary>ラベル→ケルビン。見つからなければ false。</summary>
    public static bool TryKelvinFromLabel(string label, out int kelvin)
    {
        foreach (var p in Presets)
        {
            if (p.Label == label) { kelvin = p.Kelvin; return true; }
        }
        kelvin = 0;
        return false;
    }

    /// <summary>ケルビン値に最も近いプリセットのラベルを返す。</summary>
    public static string NearestPresetLabel(int kelvin)
    {
        var best = Presets[0];
        var bestDiff = int.MaxValue;
        foreach (var p in Presets)
        {
            var d = Math.Abs(p.Kelvin - kelvin);
            if (d < bestDiff) { bestDiff = d; best = p; }
        }
        return best.Label;
    }

    /// <summary>ケルビン→[低位, 高位] の2バイト（クランプ込み）。</summary>
    public static (byte Low, byte High) KelvinToTempBytes(int kelvin)
    {
        var k = Math.Clamp(kelvin, MinKelvin, MaxKelvin);
        byte high = (byte)(k / 64);
        byte low = (byte)((k % 64) * 2 + 128);
        return (low, high);
    }

    /// <summary>[低位, 高位] → ケルビン。low &lt; 128 は不正。</summary>
    public static bool TryTempBytesToKelvin(byte low, byte high, out int kelvin)
    {
        kelvin = 0;
        if (low < 128) return false;
        kelvin = high * 64 + (low - 128) / 2;
        return true;
    }

    /// <summary>
    /// blob を構造検査しながら現在の色温度（ケルビン）を抽出。
    /// 想定外（マジック不一致・マーカー無し/複数・範囲外）なら false（＝書込しない）。
    /// </summary>
    public static bool TryParseSettings(byte[]? data, out int kelvin)
    {
        kelvin = 0;
        if (data == null || data.Length < MagicHeader.Length + TempMarker.Length + 2) return false;

        // マジックヘッダ検査
        for (int i = 0; i < MagicHeader.Length; i++)
            if (data[i] != MagicHeader[i]) return false;

        // 色温度マーカーを探す（**一意であることを要求**：複数あれば曖昧→フェイルセーフで false）
        if (!TryFindUniqueMarker(data, TempMarker, out int markerIndex)) return false;

        int lowIdx = markerIndex + TempMarker.Length;
        int highIdx = lowIdx + 1;
        if (highIdx >= data.Length) return false;

        if (!TryTempBytesToKelvin(data[lowIdx], data[highIdx], out int k)) return false;
        if (k < ParseMinKelvin || k > ParseMaxKelvin) return false;

        kelvin = k;
        return true;
    }

    /// <summary>
    /// 色温度フィールドだけ差し替えた新しい blob を返す（他バイトは原本維持）。
    /// original が TryParseSettings で通らない／結果が再パースで一致しない場合は null（＝書込中止）。
    /// bumpTimestamp=true かつタイムスタンプ varint を安全に +1 できる時のみ、その5バイトも更新。
    /// </summary>
    public static byte[]? BuildPatchedSettings(byte[]? original, int kelvin, bool bumpTimestamp)
    {
        if (original == null) return null;
        if (!TryParseSettings(original, out _)) return null;            // 想定外 blob には触らない
        if (!TryFindUniqueMarker(original, TempMarker, out int markerIndex)) return null;

        var clamped = Math.Clamp(kelvin, MinKelvin, MaxKelvin);
        var (low, high) = KelvinToTempBytes(clamped);

        var patched = (byte[])original.Clone();
        int lowIdx = markerIndex + TempMarker.Length;
        patched[lowIdx] = low;
        patched[lowIdx + 1] = high;

        if (bumpTimestamp) TryBumpTimestampInPlace(patched);

        // 自己検証：書いた値が読み戻せて、かつ意図したケルビンに最も近いプリセットへ復号できること
        if (!TryParseSettings(patched, out int readBack)) return null;
        if (NearestPresetLabel(readBack) != NearestPresetLabel(clamped)) return null;

        return patched;
    }

    /// <summary>
    /// タイムスタンプ varint（0x2A 0x06 の直後・Unix 秒・5バイト）を +1 する。
    /// 見つからない／+1 で varint 長が変わる場合は何もしない（破損回避）。
    /// </summary>
    private static void TryBumpTimestampInPlace(byte[] data)
    {
        if (!TryLocateTimestampVarint(data, out int start, out int len, out long value)) return;
        long bumped = value + 1;
        var encoded = EncodeVarint(bumped);
        if (encoded.Length != len) return;                 // 長さが変わるなら触らない
        Array.Copy(encoded, 0, data, start, len);
    }

    /// <summary>
    /// 0x2A 0x06 の直後から始まる Unix 秒 varint を探す。
    /// 妥当性窓（2017〜2049 年）に入る varint のみ採用。
    /// </summary>
    private static bool TryLocateTimestampVarint(byte[] data, out int start, out int len, out long value)
    {
        start = 0; len = 0; value = 0;
        for (int i = 0; i + 2 < data.Length; i++)
        {
            if (data[i] != 0x2A || data[i + 1] != 0x06) continue;
            int vStart = i + 2;
            if (!TryDecodeVarint(data, vStart, out long v, out int vLen)) continue;
            if (v < UnixTsMin || v > UnixTsMax) continue;
            start = vStart; len = vLen; value = v;
            return true;
        }
        return false;
    }

    /// <summary>protobuf 風 LEB128 varint を復号。</summary>
    private static bool TryDecodeVarint(byte[] data, int offset, out long value, out int byteLen)
    {
        value = 0; byteLen = 0;
        long result = 0;
        int shift = 0;
        for (int i = offset; i < data.Length && shift < 64; i++)
        {
            byte b = data[i];
            result |= (long)(b & 0x7F) << shift;
            byteLen++;
            if ((b & 0x80) == 0)
            {
                value = result;
                return true;
            }
            shift += 7;
        }
        byteLen = 0;
        return false;   // 終端ビット無し＝不正
    }

    /// <summary>long を LEB128 varint にエンコード。</summary>
    private static byte[] EncodeVarint(long value)
    {
        var bytes = new List<byte>(10);
        ulong v = (ulong)value;
        do
        {
            byte b = (byte)(v & 0x7F);
            v >>= 7;
            if (v != 0) b |= 0x80;
            bytes.Add(b);
        } while (v != 0);
        return bytes.ToArray();
    }

    /// <summary>パターンが data 中に「ちょうど1回」現れる時だけ true（複数/0 回は false）。</summary>
    private static bool TryFindUniqueMarker(byte[] data, byte[] pattern, out int index)
    {
        index = -1;
        int found = -1, count = 0;
        for (int i = 0; i + pattern.Length <= data.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (data[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) { count++; if (found < 0) found = i; }
        }
        if (count != 1) return false;
        index = found;
        return true;
    }
}
