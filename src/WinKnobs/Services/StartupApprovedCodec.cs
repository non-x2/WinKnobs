namespace WinKnobs.Services;

/// <summary>
/// StartupApproved レジストリ値（12 byte REG_BINARY）のエンコード/デコード。
///
/// 仕様（コミュニティ知見・MS未公開）：
///   byte[0]    : 0x02=有効 / 0x03=無効
///   byte[1..11]: タイムスタンプ等（MS未公開・touch しない）
///
/// 設計書 3-2 / 3-3 参照。純粋関数のみ（DI 不要・static で良い）。
/// </summary>
public static class StartupApprovedCodec
{
    /// <summary>StartupApproved 値のバイト数（12 byte 固定）。</summary>
    public const int ExpectedLength = 12;

    /// <summary>有効マーカー。byte[0] = 0x02。</summary>
    public const byte EnabledMarker = 0x02;

    /// <summary>無効マーカー。byte[0] = 0x03。</summary>
    public const byte DisabledMarker = 0x03;

    /// <summary>
    /// 12 byte の値を解析。
    /// - 12 byte 未満（壊れた値・古い形式）：新規扱いにフォールバック（isEnabled=true・trailing=null）。例外は投げない。
    /// - 12 byte 超（OS拡張）：先頭 12 byte のみ採用・残りは破棄。
    /// 設計書 3-3 エッジ#2。
    /// </summary>
    public static (bool isEnabled, byte[]? trailing) Parse(byte[]? raw)
    {
        if (raw == null || raw.Length < ExpectedLength)
        {
            // 壊れた値・未登録：有効扱い（タスクマネージャー互換）
            return (true, null);
        }

        var isEnabled = raw[0] == EnabledMarker;

        var trailing = new byte[ExpectedLength - 1];
        Array.Copy(raw, 1, trailing, 0, ExpectedLength - 1);
        return (isEnabled, trailing);
    }

    /// <summary>
    /// 12 byte の値を構築。
    /// - trailingToPreserve がある：byte[1..11] を保持して byte[0] のみ書換（タスクマネージャー互換）。
    /// - trailingToPreserve が null：新規 12 byte を生成（byte[0] のみ意味あり・他は 0x00）。
    /// </summary>
    public static byte[] Build(bool isEnabled, byte[]? trailingToPreserve)
    {
        var result = new byte[ExpectedLength];
        result[0] = isEnabled ? EnabledMarker : DisabledMarker;

        if (trailingToPreserve != null && trailingToPreserve.Length >= ExpectedLength - 1)
        {
            Array.Copy(trailingToPreserve, 0, result, 1, ExpectedLength - 1);
        }
        // trailing が無い／短い場合は残り 11 byte を 0x00 のまま（result はデフォルト 0 初期化済）

        return result;
    }
}
