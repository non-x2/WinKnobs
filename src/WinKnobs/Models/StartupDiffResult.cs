namespace WinKnobs.Models;

/// <summary>
/// スタートアップ snapshot と現在のレジストリ状態の差分（v0.1.6 機能 B：世代またぎ復元の差分件数警告）。
///
/// snapshot 視点を「正（＝復元後の状態）」とする（設計ドラフト v2 §B-4）：
///   Added    = snapshot にあり / 現在なし   → 復元すると追加される
///   Removed  = snapshot になし / 現在あり   → 復元すると削除される
///   Modified = 両方にあり・コマンド or 有効状態が異なる
///
/// 件数のみ使う B では Count 系プロパティ、エントリ内訳（*Entries）は将来の C（プレビュー UI）で使う。
/// </summary>
public sealed record StartupDiffResult(
    IReadOnlyList<string> AddedEntries,
    IReadOnlyList<string> RemovedEntries,
    IReadOnlyList<string> ModifiedEntries)
{
    public int AddedCount    => AddedEntries.Count;
    public int RemovedCount  => RemovedEntries.Count;
    public int ModifiedCount => ModifiedEntries.Count;
    public int TotalCount    => AddedCount + RemovedCount + ModifiedCount;

    /// <summary>差分算出不能時のフォールバック（全 0 件）。</summary>
    public static StartupDiffResult Empty { get; } =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
}
