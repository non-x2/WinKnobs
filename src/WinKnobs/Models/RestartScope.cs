namespace WinKnobs.Models;

/// <summary>
/// 設定変更後に反映するための再起動の粒度。
/// A3-2-c で導入：従来の bool RestartRequired を段階的に拡張し、
/// ユーザーに「どこまで再起動が必要か」を明示する。
/// </summary>
public enum RestartScope
{
    /// <summary>再起動不要・即時反映</summary>
    None,

    /// <summary>エクスプローラ再起動（taskkill /im explorer.exe → 再起動）で反映</summary>
    Explorer,

    /// <summary>ログオフ/再ログオンで反映（Control Panel\Desktop 系等）</summary>
    Logoff,

    /// <summary>PC 完全再起動で反映（HKLM 系のドライバ・カーネル系）</summary>
    System
}

/// <summary>
/// RestartScope の表示用ヘルパー。
/// </summary>
public static class RestartScopeExtensions
{
    /// <summary>UIで表示する日本語ラベル。</summary>
    public static string ToDisplay(this RestartScope scope) => scope switch
    {
        RestartScope.None => "再起動不要",
        RestartScope.Explorer => "エクスプローラ再起動で反映",
        RestartScope.Logoff => "ログオフで反映",
        RestartScope.System => "PC 再起動で反映",
        _ => ""
    };

    /// <summary>SettingDialog のフッターに表示する短い案内文。</summary>
    public static string ToHint(this RestartScope scope) => scope switch
    {
        RestartScope.None => "※ 変更は即時反映されます。",
        RestartScope.Explorer => "※ エクスプローラ再起動で反映されます。",
        RestartScope.Logoff => "※ 一度ログオフして再ログオンで反映されます。",
        RestartScope.System => "※ Windows を再起動すると反映されます。",
        _ => ""
    };
}
