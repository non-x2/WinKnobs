using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace WinKnobs.Services;

/// <summary>
/// v0.3.3「システム効果音 ON/OFF」用の純粋関数群（sound.systemSoundsScheme）。
/// I/O を持たず xUnit 対象。列挙・書込は <see cref="SystemSoundService"/> 側。
///
/// レジストリ構造（HKCU・管理者不要）：
///   HKCU\AppEvents\Schemes                         (Default)=選択中スキーム名（.Default/.None）※再生時は参照されない表示用
///   HKCU\AppEvents\Schemes\Apps\&lt;App&gt;\&lt;Event&gt;\.Current  (Default)=実効サウンド .wav（REG_SZ/REG_EXPAND_SZ）or 空
///   HKCU\AppEvents\Schemes\Apps\&lt;App&gt;\&lt;Event&gt;\.Default  (Default)=標準スキームの .wav（ON 復元の出所）
///
/// plan-validator 反映：
///   - LabelForScheme は必ず Choices の同一参照を返す（リテラル直書き禁止）。
///   - 退避エントリは existed/値/型(kind) を保持（"" 無音 と 値なし未設定 を区別＝Restore で誤書込み防止）。
///   - REG_EXPAND_SZ（%SystemRoot% 入り）の型を保存しないと復元時に展開されず無音バグ → 型を int で保存。
/// </summary>
public static class SystemSoundHelper
{
    public const string SchemesPath = @"HKCU\AppEvents\Schemes";
    public const string AppsPath = @"HKCU\AppEvents\Schemes\Apps";

    /// <summary>選択中スキーム名（トップ (Default)）。</summary>
    public const string SchemeDefault = ".Default";
    public const string SchemeNone = ".None";

    /// <summary>各イベント下のスキーム サブキー名。</summary>
    public const string CurrentSubKey = ".Current";
    public const string DefaultSubKey = ".Default";

    /// <summary>消音時に .Current\(Default) へ書く値（空文字＝No Sounds）。型は REG_SZ。</summary>
    public const string MutedValue = "";

    /// <summary>ComboBox 表示ラベル。index0=ON（標準）/ index1=OFF（消音）。</summary>
    public static IReadOnlyList<string> Choices { get; } = new[]
    {
        "効果音オン（標準）",   // 0
        "効果音オフ（消音）"    // 1
    };

    /// <summary>スキーム名が「消音（.None）」か。大小無視。</summary>
    public static bool IsMuteScheme(string? scheme)
        => string.Equals(scheme, SchemeNone, System.StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// スキーム名→表示ラベル（必ず Choices 内の同一参照）。
    /// .None→Choices[1]（オフ）／それ以外（.Default・カスタム・null）→Choices[0]（オン）。
    /// </summary>
    public static string LabelForScheme(string? scheme)
        => IsMuteScheme(scheme) ? Choices[1] : Choices[0];

    /// <summary>ラベルが「効果音オフ（消音）」か（Choices[1] と同一参照 or 文字一致）。</summary>
    public static bool IsMuteLabel(string? label)
        => label != null && (ReferenceEquals(label, Choices[1]) || label == Choices[1]);

    /// <summary>イベントの .Current キーのフルパス（HKCU\...\Apps\&lt;App&gt;\&lt;Event&gt;\.Current）。</summary>
    public static string BuildEventCurrentPath(string app, string ev)
        => $@"{AppsPath}\{app}\{ev}\{CurrentSubKey}";

    /// <summary>イベントの .Default キーのフルパス（ON 復元の参照元）。</summary>
    public static string BuildEventDefaultPath(string app, string ev)
        => $@"{AppsPath}\{app}\{ev}\{DefaultSubKey}";

    /// <summary>
    /// .Current パス→兄弟 .Default パス（末尾 \.Current を \.Default へ）。
    /// 末尾が \.Current でないパスは ArgumentException（cross-reviewer 指摘7：切り取り過ぎで別キーへ誤書込みを防ぐ）。
    /// </summary>
    public static string ToSiblingDefaultPath(string currentPath)
    {
        if (currentPath == null || !currentPath.EndsWith(CurrentSubKey, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"末尾が {CurrentSubKey} ではありません: {currentPath}", nameof(currentPath));
        return currentPath.Substring(0, currentPath.Length - CurrentSubKey.Length) + DefaultSubKey;
    }

    /// <summary>
    /// RegistryValueKind→退避用 int。サウンド値は REG_SZ/REG_EXPAND_SZ のみ想定。
    /// それ以外は String 相当(1) に丸める（復元は SZ で十分・予期せぬ型を持ち込まない）。
    /// </summary>
    public static int KindToInt(RegistryValueKind kind)
        => kind == RegistryValueKind.ExpandString ? 2 : 1;

    /// <summary>退避用 int→RegistryValueKind。2=ExpandString／他=String。</summary>
    public static RegistryValueKind IntToKind(int k)
        => k == 2 ? RegistryValueKind.ExpandString : RegistryValueKind.String;

    /// <summary>退避 JSON のシリアライズ（System.Text.Json）。</summary>
    public static string Serialize(SoundBackup backup)
        => JsonSerializer.Serialize(backup);

    /// <summary>退避 JSON のデシリアライズ。破損時は null（呼び出し側が後始末）。</summary>
    public static SoundBackup? TryDeserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<SoundBackup>(json); }
        catch (JsonException) { return null; }
    }
}

/// <summary>1 イベントの .Current\(Default) 退避（値の有無・値・型）。JSON キーは短縮（existed/v/k）。</summary>
public class SoundEventEntry
{
    /// <summary>退避時点で (Default) 値が存在したか（false＝値なし未設定→Restore は削除）。</summary>
    [JsonPropertyName("existed")] public bool Existed { get; set; }
    /// <summary>値（.wav パス or 空）。Existed=false の時は無意味。</summary>
    [JsonPropertyName("v")] public string Value { get; set; } = "";
    /// <summary>型（1=REG_SZ / 2=REG_EXPAND_SZ）。</summary>
    [JsonPropertyName("k")] public int Kind { get; set; } = 1;
}

/// <summary>効果音 ON/OFF の原状退避トークン（first-touch-wins）。</summary>
public class SoundBackup
{
    /// <summary>退避時点のトップスキーム名（.Default/.None/カスタム）。</summary>
    [JsonPropertyName("scheme")] public string Scheme { get; set; } = SystemSoundHelper.SchemeDefault;
    /// <summary>イベント .Current パス（HKCU\...\.Current）→ 退避エントリ。</summary>
    [JsonPropertyName("events")] public Dictionary<string, SoundEventEntry> Events { get; set; } = new();
}
