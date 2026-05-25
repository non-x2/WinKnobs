namespace WinKnobs.Models;

/// <summary>
/// レジストリ単純書込以外の操作を SettingItem に紐付けるアクション群。
/// A3-2-c で導入：CLSID キー存在判定・fsutil・コマンド実行など。
///
/// plan-validator V-1 / V-4 対策：
///   reg.exe export がキー非存在で失敗するケース（例：W5 クラシック右クリック）に備え、
///   Backup/Restore を CommandAction 内に閉じ込める設計。
///   - Backup は復元用トークン（JSON 等の文字列）を返す
///   - Restore はそのトークンを受け取って元状態に戻す
///   - 両方 null の Command は「戻す」ボタンを無効化（UI 側で判断）
/// </summary>
public class CommandAction
{
    /// <summary>現在値を文字列で取得（"ON"/"OFF" 等の選択肢ラベルを返す）。</summary>
    public required Func<string> Read { get; init; }

    /// <summary>新しい値（Choices のいずれか）を適用。</summary>
    public required Action<string> Apply { get; init; }

    /// <summary>選択肢ラベル一覧（ComboBox 表示用）。最低 2 件。</summary>
    public required IReadOnlyList<string> Choices { get; init; }

    /// <summary>
    /// 適用前にバックアップ。戻り値は Restore に渡すトークン文字列。
    /// 例：CLSID キーが「存在しない状態」を JSON で保存。
    /// null なら戻し処理は走らない。
    /// </summary>
    public Func<string>? Backup { get; init; }

    /// <summary>Backup の戻り値（トークン）を受けて元状態に戻す。</summary>
    public Action<string>? Restore { get; init; }

    /// <summary>戻す機能が利用可能か（Backup + Restore が両方セットされている）。</summary>
    public bool CanRestore => Backup != null && Restore != null;
}
