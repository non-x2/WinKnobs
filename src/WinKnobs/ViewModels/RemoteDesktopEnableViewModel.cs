using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Services;

namespace WinKnobs.ViewModels;

/// <summary>
/// v0.1.19 Step3：リモートデスクトップ **有効化（ON）** の Red 専用 強確認ダイアログ ViewModel（🔴Red・本機能の心臓）。
///
/// 素の <see cref="DialogService.ConfirmDestructive"/>（MessageBox・チェック不可）では足りない要件
/// （明示同意チェック・ReadStatus コンテキスト提示）を満たすための専用 VM。構造の手本は
/// <see cref="WinKnobs.ViewModels.Startup.RestorePreviewViewModel"/>（Init 後注入＋DialogResult プロパティ方式）。
///
/// ■ 設計（plan-validator・設計ドラフト §4）：
///   - 判定ロジック（同意可否）と文言生成は**この VM の純粋プロパティ**に寄せ、Window（View）は表示だけにする
///     （DialogService のテスト可能性を保つ・xUnit で VM 単体検証）。I/O は一切持たない（<see cref="Init"/> に
///     <see cref="RdpStatus"/> を渡すだけ）。
///   - 明示同意ジェスチャ＝チェックボックス「リスクを理解しました」(<see cref="IsRiskUnderstood"/>) ON で初めて
///     [有効化] が活性（<see cref="CanEnable"/>）。Enter 連打・誤クリックを構造的に止める（問2-A）。
/// </summary>
public partial class RemoteDesktopEnableViewModel : ObservableObject
{
    private RdpStatus? _status;

    /// <summary>ヘッダ（危険度バッジは XAML 側の 🔴 と赤系配色で表現）。</summary>
    public string HeaderText => "リモートデスクトップを有効にする";

    /// <summary>何が起きるか（平易な日本語・§4-2）。</summary>
    public string WhatHappensText =>
        "外部からこのPCに接続できる入口（攻撃口）が1つ増えます。";

    // ---- 現在のコンテキスト（ReadStatus 由来・§4-3）----

    /// <summary>現在 ON/OFF・NLA の状態ラベル（Helper の純粋関数が生成）。</summary>
    public string CurrentStateText =>
        _status is null ? "状態：不明" : $"現在の状態：{_status.StateLabel}";

    /// <summary>対象ユーザー名。</summary>
    public string UserNameText =>
        _status is null || string.IsNullOrEmpty(_status.UserName)
            ? "対象ユーザー名：（取得できませんでした）"
            : $"対象ユーザー名：{_status.UserName}";

    /// <summary>このPCの LAN IP（複数なら列挙）。</summary>
    public string LanIpText
    {
        get
        {
            if (_status is null || _status.LanIPv4.Count == 0)
                return "このPCの LAN IP：（取得できませんでした）";
            return $"このPCの LAN IP：{string.Join(" / ", _status.LanIPv4)}";
        }
    }

    /// <summary>RDP ポート（既定 3389／変更時は強調文言を付ける）。</summary>
    public string PortText
    {
        get
        {
            if (_status is null) return "RDP ポート：不明";
            return _status.Port == 3389
                ? "RDP ポート：3389（既定）"
                : $"RDP ポート：{_status.Port}（⚠️ 既定 3389 から変更されています）";
        }
    }

    /// <summary>ポートが既定から変わっているか（XAML 強調表示用）。</summary>
    public bool IsPortChanged => _status is not null && _status.Port != 3389;

    /// <summary>あなたは今リモート接続中か（§4-3・true で強警告を出す）。</summary>
    public bool IsCurrentSessionRemote => _status?.IsCurrentSessionRemote ?? false;

    /// <summary>リモート接続中の警告文（NLA 必須化で古いクライアントが切れる可能性）。</summary>
    public string RemoteSessionWarningText =>
        "⚠️ あなたは今リモート接続中の可能性があります。NLA を必須にするため、" +
        "古いクライアント（未更新の接続元）から今後つながらなくなる場合があります。";

    /// <summary>推奨対策の同時提示（§4-4）。</summary>
    public string RecommendationText =>
        "・NLA（ネットワークレベル認証）を自動で必須にします（安全側）。\n" +
        "・接続アカウントには強いパスワードを必ず設定してください。\n" +
        "・同一LAN内でのみの利用を推奨します。公共Wi-Fi・共有PCでは有効化しないでください。\n" +
        "・インターネット（WAN）への露出はルーター設定（ポート開放/UPnP）次第で、WinKnobs では保証できません。";

    /// <summary>組織管理PCの注記表示（plan-validator #7・ポリシー管理時のみ）。</summary>
    public bool IsPolicyManaged => _status?.IsPolicyManaged ?? false;

    /// <summary>組織ポリシー注記（実装を増やさず文言で防御・plan-validator #7）。</summary>
    public string PolicyNoteText =>
        "※ 組織管理PCでは、この設定が組織ポリシー（GPO/MDM）で上書きされることがあります。";

    /// <summary>戻し方の保証（§4-7）。</summary>
    public string RestoreAssuranceText =>
        "適用前に現在の設定を退避します。WinKnobs の『無効にする』または『元に戻す』でいつでも戻せます。" +
        "（※ ON は『ぜんぶ戻す』では戻りません。『無効にする』か『元に戻す』を使ってください。）";

    /// <summary>
    /// 明示同意チェック「リスクを理解しました」。変更で <see cref="CanEnable"/> を更新する（問2-A）。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEnable))]
    private bool _isRiskUnderstood;

    /// <summary>[有効化] ボタン活性条件＝チェック ON のみ（誤確定防止）。</summary>
    public bool CanEnable => IsRiskUnderstood;

    /// <summary>ダイアログ結果（RestorePreviewViewModel と同方式）。true=同意して有効化 / false=キャンセル。</summary>
    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// 状態を後注入（RestorePreviewViewModel.Init と同じ流儀＝DataContext 設定の「前」に呼ぶ）。
    /// 設定済みプロパティは DataContext 割当時に WPF バインドが一括取得するため、個別 PropertyChanged は不要。
    /// </summary>
    public void Init(RdpStatus status)
    {
        _status = status;
    }

    [RelayCommand]
    private void Enable() => DialogResult = true;

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
