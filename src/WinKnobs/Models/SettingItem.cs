namespace WinKnobs.Models;

/// <summary>
/// 設定項目の危険度
/// </summary>
public enum RiskLevel
{
    /// <summary>安全（見た目変更・すぐ戻せる）</summary>
    Green,
    /// <summary>注意（再起動必要・影響範囲大）</summary>
    Yellow,
    /// <summary>上級者向け（システム挙動に影響・要復元ポイント）</summary>
    Red
}

/// <summary>
/// レジストリ値の型（編集UIの切替に使用）
/// A2 で導入：UX-1（ON/OFF トグル）と RegistryValueKind 明示書込のため。
/// A3-2-c で追加：Binary（REG_BINARY） / Command（CommandAction 経由）。
/// </summary>
public enum SettingValueType
{
    /// <summary>真偽値（REG_DWORD 0/1 を ON/OFF として扱う）</summary>
    Boolean,
    /// <summary>32bit整数（REG_DWORD）</summary>
    DWord,
    /// <summary>文字列（REG_SZ）</summary>
    String,
    /// <summary>環境変数展開対応文字列（REG_EXPAND_SZ・%windir% 等）</summary>
    ExpandString,
    /// <summary>バイト列（REG_BINARY・16進文字列で編集）A3-2-c</summary>
    Binary,
    /// <summary>レジストリ非依存・CommandAction で動作 A3-2-c</summary>
    Command
}

/// <summary>
/// 設定カテゴリ種別
/// </summary>
public enum CategoryType
{
    Home,
    System,
    Startup,
    Shell,
    Network,
    Kernel,
    Login,
    Desktop,
    Privacy,
    KeyboardMouse,
    SearchResults,
    Favorites,
    /// <summary>
    /// アクセシビリティ（テキストサイズ等・v0.1.9）。
    /// plan-validator 反映：enum は末尾追加（既存値の数値シフト回避）。
    /// UI 表示順は InitializeCategories の Add 位置で制御する（enum 定義位置 ≠ 表示順）。
    /// </summary>
    Accessibility,

    /// <summary>
    /// システム・メンテナンス（タスクマネージャ/システム保護/メモリ/リモートデスクトップ等・v0.1.14〜）。
    /// enum は末尾追加（既存値の数値シフト回避）。Startup/Network 以外なので
    /// IsStandardCategorySelected=true となり標準 DataGrid に表示される（専用ペイン不要）。
    /// </summary>
    Maintenance,

    /// <summary>
    /// グラフィック（アプリ別GPU設定・全体トグル・v0.3.0）。専用ペイン GraphicsListView を表示する。
    /// enum は末尾追加（既存値の数値シフト回避）。Startup/Network と同じく専用ペインを持つため
    /// IsStandardCategorySelected の判定式に isGraphics を追加する（4者択一）。
    /// </summary>
    Graphics,

    /// <summary>
    /// サウンド（システム効果音 ON/OFF 等・v0.3.3）。専用ペインは持たず標準 DataGrid に表示する
    /// （Maintenance と同じ＝Startup/Network/Graphics 以外なので IsStandardCategorySelected=true）。
    /// enum は末尾追加（既存値の数値シフト回避）。表示順は InitializeCategories の Add 位置で制御。
    /// </summary>
    Sound,

    /// <summary>
    /// ゲーミング（Game Mode 等・v0.3.7〜・v0.4.0 で拡充予定）。専用ペインは持たず標準 DataGrid に表示する
    /// （Startup/Network/Graphics 以外なので IsStandardCategorySelected=true）。
    /// enum は末尾追加（既存値の数値シフト回避）。Graphics（グラフィック）とは別カテゴリ（アイコンも区別）。
    /// </summary>
    Gaming
}

/// <summary>
/// 1つの設定項目を表すモデル
/// </summary>
public class SettingItem
{
    /// <summary>項目ID（一意）</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>表示用タイトル</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>項目の説明（日本語・親切に）</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>所属カテゴリ</summary>
    public CategoryType Category { get; set; }

    /// <summary>危険度</summary>
    public RiskLevel Risk { get; set; } = RiskLevel.Green;

    /// <summary>
    /// A3-2-c：再起動の粒度。デフォルトは None（即時反映）。
    /// 旧 RestartRequired は computed プロパティでこの値から計算。
    /// </summary>
    public RestartScope RestartScope { get; set; } = RestartScope.None;

    /// <summary>
    /// A3-2-c：RestartScope != None で true。旧 bool プロパティとの後方互換。
    /// （バインディング `{Binding RestartRequired}` を維持するため computed で残す）
    /// </summary>
    public bool RestartRequired => RestartScope != RestartScope.None;

    /// <summary>レジストリパス（HKLM\... 形式）</summary>
    public string? RegistryPath { get; set; }

    /// <summary>レジストリ値の名前</summary>
    public string? RegistryValueName { get; set; }

    /// <summary>推奨値</summary>
    public object? RecommendedValue { get; set; }

    /// <summary>Windowsデフォルト値</summary>
    public object? DefaultValue { get; set; }

    /// <summary>値の型（A2 で導入。デフォルトは String で後方互換）</summary>
    public SettingValueType ValueType { get; set; } = SettingValueType.String;

    /// <summary>
    /// UI 表示とレジストリ値を反転（A3-2-a で導入・Boolean 専用）。
    /// true の場合：ユーザー視点 ON ↔ レジストリ値 0、ユーザー視点 OFF ↔ レジストリ値 1。
    /// 例：HideFileExt の値「拡張子を隠す」を Title「拡張子を表示」と一致させる。
    /// </summary>
    public bool InvertDisplay { get; set; } = false;

    /// <summary>
    /// A3-2-c：Command 型（および将来の Boolean 拡張用）の動作定義。
    /// ValueType == Command の場合は必須。
    /// </summary>
    public CommandAction? Command { get; set; }

    /// <summary>
    /// A3-2-c：Binary 型の固定バイト数バリデーション（plan-validator VE-2）。
    /// null なら 1〜256 バイトの可変長を許容、値があれば固定長強制。
    /// </summary>
    public int? ExpectedByteLength { get; set; }

    /// <summary>
    /// String 型で「数値文字列」を期待する場合の範囲（cross-reviewer U-3）。
    /// 例：MenuShowDelay は "0"〜"1000" のような整数文字列のみ許容。
    /// null なら任意文字列を許容。
    /// </summary>
    public (int Min, int Max)? NumericStringRange { get; set; }

    /// <summary>表示用：危険度マーク</summary>
    public string RiskMark => Risk switch
    {
        RiskLevel.Green => "🟢",
        RiskLevel.Yellow => "🟡",
        RiskLevel.Red => "🔴",
        _ => "⚪"
    };

    /// <summary>表示用：再起動マーク</summary>
    public string RestartMark => RestartRequired ? "⏯" : string.Empty;
}

/// <summary>
/// カテゴリ（ツリービューに表示）
/// </summary>
public class SettingCategory
{
    public string Name { get; set; }
    public CategoryType Type { get; set; }
    public string Icon { get; set; }

    public SettingCategory(string name, CategoryType type, string icon = "📁")
    {
        Name = name;
        Type = type;
        Icon = icon;
    }
}
