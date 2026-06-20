using System.Text;

namespace WinKnobs.Services;

/// <summary>
/// グラフィック設定（アプリ別GPU・全体トグル）の純粋ロジック集約（v0.3.0・xUnit 対象）。
///
/// レジストリ仕様（HKCU・管理者不要）：
///   キー  ＝ HKCU\Software\Microsoft\DirectX\UserGpuPreferences
///   アプリ別 ＝ 値名=exe フルパス、データ=REG_SZ "GpuPreference=2;AutoHDREnable=1;"（; 区切り key=value）
///   全体    ＝ 値名 "DirectXUserGlobalSettings"、データ=REG_SZ "SwapEffectUpgradeEnable=1;AutoHDREnable=0;"
///
/// 設計原則（NightLightHelper 同思想）：「自分が知っているトークンだけ書き換え、未知トークンは原本の順序で保持」。
///   SpecificAdapter 等の高度な設定を壊さないため。壊れたトークンはスキップして頑健にパースする。
/// </summary>
public static class GpuPreferenceHelper
{
    /// <summary>アプリ別/全体トグルが入るキー（HKCU）。</summary>
    public const string KeyPath = @"HKCU\Software\Microsoft\DirectX\UserGpuPreferences";

    /// <summary>全体設定（既定の設定）の予約値名。アプリ別一覧からは除外する。</summary>
    public const string GlobalValueName = "DirectXUserGlobalSettings";

    /// <summary>
    /// グラフィック通知設定の予約値名（REG_DWORD）。アプリ別GPU設定ではない。
    /// v0.3.0 実機検証でアプリ別一覧への混入＋「ぜんぶ戻す」での REG_DWORD→REG_SZ 型変換を確認したため除外。
    /// </summary>
    public const string NotificationConfigValueName = "GraphicsFeaturesNotificationConfig";

    /// <summary>
    /// アプリ別GPU設定ではない Windows 予約値名（アプリ別一覧・退避から除外）。
    /// ※これは「名前」での除外。実際にはこれに加えて値の型が REG_SZ かどうかでも二重に弾く
    ///   （GraphicsService 側・将来の未知の非SZ予約値も型で除外できるようにするため）。
    /// </summary>
    public static readonly string[] ReservedValueNames = { GlobalValueName, NotificationConfigValueName };

    public const string TokenGpuPreference = "GpuPreference";
    public const string TokenAutoHdr = "AutoHDREnable";
    public const string TokenSwapEffect = "SwapEffectUpgradeEnable";

    /// <summary>GpuPreference の値：Windows に任せる。</summary>
    public const int GpuLetWindowsDecide = 0;
    /// <summary>GpuPreference の値：省電力 GPU（内蔵 GPU）。</summary>
    public const int GpuPowerSaving = 1;
    /// <summary>GpuPreference の値：高パフォーマンス GPU（外付け GPU）。</summary>
    public const int GpuHighPerformance = 2;

    /// <summary>exe パスの一致判定は常にこれ（重要4：Windows のレジストリ値名は大小無視）。</summary>
    public static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// "key=value;key=value;" を順序保持でパースする。未知トークンも保持（後で BuildValue で原本順に再構築）。
    /// 壊れたトークン（= を含まない・key が空）はスキップ。value 内に = があれば最初の = で分割。
    /// </summary>
    public static List<KeyValuePair<string, string>> ParseTokens(string? raw)
    {
        var list = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrEmpty(raw)) return list;

        foreach (var seg in raw.Split(';'))
        {
            var part = seg.Trim();
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            if (eq <= 0) continue;                       // = 無し or 先頭が = → 不正トークンとしてスキップ
            var key = part.Substring(0, eq).Trim();
            var value = part.Substring(eq + 1).Trim();
            if (key.Length == 0) continue;
            list.Add(new KeyValuePair<string, string>(key, value));
        }
        return list;
    }

    /// <summary>
    /// トークン列を "key=value;" 形式へ再構築（末尾は必ず ; 付き＝Windows 形式に揃える）。
    /// 空列なら空文字（呼び出し側は空文字なら値を削除する想定）。
    /// </summary>
    public static string BuildValue(IEnumerable<KeyValuePair<string, string>> tokens)
    {
        var sb = new StringBuilder();
        foreach (var kv in tokens)
        {
            if (string.IsNullOrEmpty(kv.Key)) continue;
            sb.Append(kv.Key).Append('=').Append(kv.Value).Append(';');
        }
        return sb.ToString();
    }

    /// <summary>指定トークンの値を取得（キーは大小無視）。無ければ null。</summary>
    public static string? GetToken(IReadOnlyList<KeyValuePair<string, string>> tokens, string key)
    {
        foreach (var kv in tokens)
            if (string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        return null;
    }

    /// <summary>
    /// 指定トークンを設定（存在すれば最初の1件だけ値を差替＝元のキー綴りを維持、無ければ末尾に追加）。
    /// 他トークンは順序ごと保持。新しいリストを返す（入力は変更しない）。
    /// </summary>
    public static List<KeyValuePair<string, string>> SetToken(
        IReadOnlyList<KeyValuePair<string, string>> tokens, string key, string value)
    {
        var result = new List<KeyValuePair<string, string>>(tokens.Count + 1);
        bool replaced = false;
        foreach (var kv in tokens)
        {
            if (!replaced && string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new KeyValuePair<string, string>(kv.Key, value)); // 元の綴り維持
                replaced = true;
            }
            else
            {
                result.Add(kv);
            }
        }
        if (!replaced) result.Add(new KeyValuePair<string, string>(key, value));
        return result;
    }

    /// <summary>指定トークンを全て除去（キーは大小無視）。新しいリストを返す。</summary>
    public static List<KeyValuePair<string, string>> RemoveToken(
        IReadOnlyList<KeyValuePair<string, string>> tokens, string key)
    {
        var result = new List<KeyValuePair<string, string>>(tokens.Count);
        foreach (var kv in tokens)
            if (!string.Equals(kv.Key, key, StringComparison.OrdinalIgnoreCase))
                result.Add(kv);
        return result;
    }

    /// <summary>GpuPreference を取得（0/1/2 のみ採用・範囲外や非数値は 0=自動扱い）。</summary>
    public static int GetGpuPreference(IReadOnlyList<KeyValuePair<string, string>> tokens)
    {
        var v = GetToken(tokens, TokenGpuPreference);
        if (int.TryParse(v, out var n) && IsValidGpuPreference(n)) return n;
        return GpuLetWindowsDecide;
    }

    /// <summary>AutoHDREnable トークンが "1" なら true。</summary>
    public static bool GetAutoHdr(IReadOnlyList<KeyValuePair<string, string>> tokens)
        => GetToken(tokens, TokenAutoHdr) == "1";

    /// <summary>SwapEffectUpgradeEnable（ウィンドウゲームの最適化）トークンが "1" なら true。</summary>
    public static bool GetSwapEffect(IReadOnlyList<KeyValuePair<string, string>> tokens)
        => GetToken(tokens, TokenSwapEffect) == "1";

    /// <summary>GpuPreference の値が 0/1/2 か。</summary>
    public static bool IsValidGpuPreference(int v) => v is >= 0 and <= 2;

    /// <summary>
    /// アプリ別GPU設定の値名（exe パス）として扱ってよいか。
    /// 除外：全体予約名 DirectXUserGlobalSettings／既定値（空名）／制御文字・NUL を含む／250 文字超（E1）。
    /// </summary>
    public static bool IsAppValueName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;                       // 既定値（空名）は除外
        if (name.Length > 250) return false;                                // 異常に長い値名（E1）
        foreach (var reserved in ReservedValueNames)                        // 全体設定・通知設定などの予約値（v0.3.0）
            if (string.Equals(name, reserved, StringComparison.OrdinalIgnoreCase)) return false;
        foreach (var c in name)
            if (char.IsControl(c)) return false;                            // 制御文字・NUL（E1）
        return true;
    }

    /// <summary>exe フルパスから表示名（ファイル名）を取り出す。失敗時は元パス。</summary>
    public static string DisplayNameOf(string fullPath)
    {
        if (string.IsNullOrEmpty(fullPath)) return fullPath;
        try
        {
            var name = System.IO.Path.GetFileName(fullPath);
            return string.IsNullOrEmpty(name) ? fullPath : name;
        }
        catch
        {
            return fullPath;
        }
    }

    /// <summary>GpuPreference の値 → 日本語ラベル（UI 表示・ドロップダウン用）。</summary>
    public static string GpuPreferenceLabel(int v) => v switch
    {
        GpuPowerSaving => "省電力 GPU（内蔵）",
        GpuHighPerformance => "高パフォーマンス GPU（外付け）",
        _ => "Windows に任せる（自動）",
    };
}
