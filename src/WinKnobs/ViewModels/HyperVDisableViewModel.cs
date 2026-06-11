using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinKnobs.ViewModels;

/// <summary>
/// v0.2.1 Step5：Hyper-V（ハイパーバイザー起動）**無効化（OFF）** の Red 専用 強確認ダイアログ ViewModel（🔴Red）。
/// 構造は <see cref="RemoteDesktopEnableViewModel"/> と同じ（純粋プロパティ＋Init 後注入＋DialogResult 方式・I/O なし）。
///
/// ■ 設計（v0.2.1 設計ドラフト §2-6＋🔧反映2）：
///   - 連鎖影響の列挙：WSL2／Windows サンドボックス／コア分離／Credential Guard／Hyper-V VM がすべて停止。
///   - 🔴 依存ガイド（plan-validator 致命2）：コア分離（VBS）が ON のままだと off にしても効果なし
///     → 該当時のみ「先にコア分離を OFF に」を強調表示。
///   - 🔴 BitLocker 警告（plan-validator 致命2）：再起動後に回復キーを求められる場合あり → **無条件で常時表示**
///     （検知に頼らない＝検知失敗で警告が消える事故を構造的に防ぐ）。
///   - 明示同意チェック ON で初めて [無効化] が活性（Enter 連打・誤クリック防止）。
/// </summary>
public partial class HyperVDisableViewModel : ObservableObject
{
    private string _currentStateText = "現在の状態：不明";
    private bool _isCoreIsolationOn;

    /// <summary>ヘッダ。</summary>
    public string HeaderText => "Hyper-V（ハイパーバイザー）を無効にする";

    /// <summary>何が起きるか（平易な日本語）。</summary>
    public string WhatHappensText =>
        "Windows のハイパーバイザー（仮想化の土台）を次回起動から停止します。" +
        "LDPlayer 等の Android エミュレータを速く動かすための設定です。";

    /// <summary>現在の hypervisorlaunchtype（Helper の純粋関数が生成したラベルを Init で受ける）。</summary>
    public string CurrentStateText => _currentStateText;

    /// <summary>連鎖して止まる機能の列挙（設計ドラフト ダメ出し2）。</summary>
    public string AffectedFeaturesText =>
        "・WSL2（Linux 実行環境）\n" +
        "・Windows サンドボックス\n" +
        "・コア分離（メモリ整合性）などの仮想化ベースのセキュリティ\n" +
        "・Credential Guard\n" +
        "・Hyper-V の仮想マシンすべて\n" +
        "これらが次回起動からすべて使えなくなります（機能自体は残るので、元に戻せば復活します）。";

    /// <summary>コア分離が ON のままか（XAML の依存ガイド表示切替・plan-validator 致命2）。</summary>
    public bool IsCoreIsolationOn => _isCoreIsolationOn;

    /// <summary>依存ガイド（コア分離 ON 時のみ表示・🔴効かない経路を塞ぐ）。</summary>
    public string CoreIsolationGuideText =>
        "⚠️ コア分離（メモリ整合性）が現在【有効】です。\n" +
        "有効のままだと、この設定を OFF にして再起動してもハイパーバイザーは停止せず、効果が出ません。\n" +
        "先にツールメニューの「コア分離（メモリ整合性）を無効にする」を実行してください。";

    /// <summary>BitLocker 警告（無条件で常時表示・plan-validator 致命2）。</summary>
    public string BitLockerWarningText =>
        "⚠️ BitLocker（ドライブ暗号化）を使用している場合、起動構成の変更により" +
        "再起動後に回復キーの入力を求められることがあります。" +
        "実行前に回復キーを確認できる状態にしておいてください（Microsoft アカウントの「デバイス」ページ等）。";

    /// <summary>戻し方の保証。</summary>
    public string RestoreAssuranceText =>
        "適用前に現在の起動設定を退避します。ツールメニューの「Hyper-V を元に戻す」でいつでも戻せます" +
        "（反映には再起動が必要）。";

    /// <summary>
    /// 明示同意チェック「リスクを理解しました」。変更で <see cref="CanDisable"/> を更新する。
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDisable))]
    private bool _isRiskUnderstood;

    /// <summary>[無効化] ボタン活性条件＝チェック ON のみ（誤確定防止）。</summary>
    public bool CanDisable => IsRiskUnderstood;

    /// <summary>ダイアログ結果（RemoteDesktopEnableViewModel と同方式）。true=同意して無効化 / false=キャンセル。</summary>
    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// 状態を後注入（DataContext 設定の「前」に呼ぶ契約・RestorePreviewViewModel と同流儀）。
    /// </summary>
    /// <param name="currentStateLabel">HyperVHelper.DescribeMenuStatusLabel の生成ラベル。</param>
    /// <param name="isCoreIsolationOn">コア分離（HVCI）が有効か（不明時は false＝ガイド非表示。BitLocker 警告は常時表示なので最低限の防衛は維持）。</param>
    public void Init(string currentStateLabel, bool isCoreIsolationOn)
    {
        _currentStateText = currentStateLabel;
        _isCoreIsolationOn = isCoreIsolationOn;
    }

    [RelayCommand]
    private void Disable() => DialogResult = true;

    [RelayCommand]
    private void Cancel() => DialogResult = false;
}
