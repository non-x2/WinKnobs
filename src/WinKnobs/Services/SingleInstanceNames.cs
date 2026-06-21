namespace WinKnobs.Services;

/// <summary>
/// 単一インスタンス制御で使う名前付きカーネルオブジェクト名の決定的生成（純粋関数・v0.3.1）。
///
/// 設計：ロジックは Helper、副作用は Service（プロジェクト規約）。本クラスは「名前を作るだけ」で
/// I/O を持たないため xUnit で全面検証できる（実 Mutex/Event の往復は <see cref="SingleInstanceManager"/> 側）。
///
/// スコープは <c>Local\</c>（現在のログオンセッション内）＝観測バグ「同一セッションで 2 プロセス併存」を解決する範囲。
/// <c>Global\</c>（マシン全体・SeCreateGlobalPrivilege/ACL の別問題）とクロスセッション単一化は非対象（設計ドラフト §2）。
/// </summary>
public static class SingleInstanceNames
{
    /// <summary>WinKnobs 固有の安定 GUID。名前の一意性の核（他アプリと衝突しないため固定）。</summary>
    public const string AppGuid = "8F3A2C7E-4B19-4D8A-9C2E-7A1F6B05E3D4";

    /// <summary>
    /// 「管理者として再起動（runas）」を単一インスタンス化の例外にするためのコマンドライン引数。
    /// 環境変数は UAC 昇格時に AppInfo サービスが環境を作り直すため確実には届かない。
    /// コマンドライン引数なら ShellExecute "runas" でも子へ確実に渡るため、こちらを採用（plan-validator 致命1 反映）。
    /// </summary>
    public const string SanctionedRestartArg = "--winknobs-restarted";

    /// <summary>セッション内スコープ。Global は非対象（設計ドラフト §2）。</summary>
    private const string ScopePrefix = "Local";

    /// <summary>二重起動検出用の名前付き Mutex 名。</summary>
    public static string Mutex() => Compose("Mutex");

    /// <summary>既存インスタンスを起こす名前付き EventWaitHandle 名。</summary>
    public static string Activate() => Compose("Activate");

    internal static string Compose(string kind) => $@"{ScopePrefix}\WinKnobs_{AppGuid}_{kind}";
}
