using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinKnobs.Services;

namespace WinKnobs.ViewModels;

/// <summary>
/// v0.3.0 仮想メモリ（ページファイル）設定の 🔴Red 専用ダイアログ ViewModel。
/// HyperVDisableViewModel と同流儀（Init 後注入＋DialogResult 方式・I/O なし）だが、
/// 選択肢（自動管理／システム管理／手動）＋手動サイズ入力を持つ点が異なる。
///
/// 安全設計（設計ドラフト §2-2）：推奨＝自動管理／"無効化(なし)" は出さない／手動は検証（E5）／
///   理解チェック ON で初めて [適用] 活性／適用は再起動で反映。
/// </summary>
public partial class VirtualMemoryViewModel : ObservableObject
{
    private readonly ExternalLauncherService _launcher;

    public VirtualMemoryViewModel(ExternalLauncherService launcher)
    {
        _launcher = launcher;
    }

    private string _currentStateText = "現在の状態：不明";

    public string HeaderText => "仮想メモリ（ページファイル）設定";

    public string WhatHappensText =>
        "Windows のページファイル（仮想メモリ）の管理方法を変更します。" +
        "⚠️ 変更は再起動後に反映されます。値を誤ると起動が不安定になることがあります。";

    public string CurrentStateText => _currentStateText;

    public string WarningText =>
        "⚠️ この変更は再起動後に反映されます。\n" +
        "・適用前の状態は自動で退避し、「元に戻す」でいつでも戻せます。\n" +
        "・「ページファイルなし（無効化）」はこのツールでは選べません（起動・休止・クラッシュ情報に影響するため）。\n" +
        "・迷ったら「自動管理（Windows に任せる）」が安全です。";

    public string RestoreAssuranceText =>
        "適用前に現在の設定を退避します。ツールメニューの「仮想メモリ設定を元に戻す」でいつでも戻せます" +
        "（反映には再起動が必要）。心配なら先に「復元ポイントを作成」しておくと安心です。";

    // ── モード選択（RadioButton 3 択・GroupName で排他） ──
    [ObservableProperty] private bool _isAutoMode = true;     // 既定＝推奨
    [ObservableProperty] private bool _isSystemMode;
    [ObservableProperty] private bool _isManualMode;

    // ── 手動サイズ（MB・文字列入力） ──
    [ObservableProperty] private string _initialSizeText = "";
    [ObservableProperty] private string _maxSizeText = "";

    /// <summary>検証メッセージ（手動サイズが不正な理由を表示）。</summary>
    [ObservableProperty] private string _validationMessage = "";

    /// <summary>明示同意チェック「リスクを理解しました」。</summary>
    [ObservableProperty] private bool _isRiskUnderstood;

    /// <summary>ダイアログ結果。true=適用 / false=キャンセル。</summary>
    [ObservableProperty] private bool? _dialogResult;

    /// <summary>選択中のモード。</summary>
    public VmApplyMode SelectedMode =>
        IsManualMode ? VmApplyMode.Manual :
        IsSystemMode ? VmApplyMode.SystemManaged :
        VmApplyMode.AutoManaged;

    /// <summary>[適用] 活性条件＝理解チェック ON ＋（手動なら）サイズが妥当。</summary>
    public bool CanApply
    {
        get
        {
            if (!IsRiskUnderstood) return false;
            if (!IsManualMode) return true;
            if (!TryGetManualSizes(out var init, out var max)) return false;
            return VirtualMemoryHelper.ValidateManual(init, max).Ok;
        }
    }

    /// <summary>手動サイズ（初期/最大 MB）を取得。両方が整数として読めれば true。</summary>
    public bool TryGetManualSizes(out int initialMb, out int maxMb)
    {
        maxMb = 0;
        return int.TryParse(InitialSizeText, out initialMb) && int.TryParse(MaxSizeText, out maxMb);
    }

    /// <summary>状態を後注入（DataContext 設定の「前」に呼ぶ契約）。</summary>
    public void Init(VirtualMemoryState? state)
    {
        _currentStateText = VirtualMemoryHelper.DescribeState(state);

        // 現在が手動設定ならその値を初期表示（編集の足場）。Init は DataContext 設定前に呼ぶ契約なので
        // 生成プロパティ経由で代入しても UI バインドはまだ無い（OnChanged の NotifyApplyState は無害）。
        if (state is { AutomaticManaged: false, Settings.Count: > 0 })
        {
            var s = state.Settings[0];
            if (!(s.InitialSize == 0 && s.MaxSize == 0))
            {
                InitialSizeText = s.InitialSize.ToString();
                MaxSizeText = s.MaxSize.ToString();
            }
        }
    }

    partial void OnIsAutoModeChanged(bool value) { NotifyApplyState(); }
    partial void OnIsSystemModeChanged(bool value) { NotifyApplyState(); }
    partial void OnIsManualModeChanged(bool value) { NotifyApplyState(); }
    partial void OnInitialSizeTextChanged(string value) { NotifyApplyState(); }
    partial void OnMaxSizeTextChanged(string value) { NotifyApplyState(); }
    partial void OnIsRiskUnderstoodChanged(bool value)
    {
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();   // U-2：CanExecute も同期（IsEnabled だけに頼らない安全ゲート）
    }

    private void NotifyApplyState()
    {
        // 手動の検証メッセージを更新
        if (IsManualMode)
        {
            if (!TryGetManualSizes(out var init, out var max))
                ValidationMessage = "初期サイズ・最大サイズに数値（MB）を入力してください。";
            else
            {
                var (ok, err) = VirtualMemoryHelper.ValidateManual(init, max);
                ValidationMessage = ok ? "" : err ?? "";
            }
        }
        else
        {
            ValidationMessage = "";
        }
        OnPropertyChanged(nameof(IsManualMode));
        OnPropertyChanged(nameof(CanApply));
        ApplyCommand.NotifyCanExecuteChanged();   // U-2：CanExecute も同期
    }

    /// <summary>U-2：理解チェック＋（手動なら）サイズ妥当時のみ実行可（IsEnabled すり抜け防止）。</summary>
    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => DialogResult = true;

    [RelayCommand]
    private void Cancel() => DialogResult = false;

    /// <summary>U-1：Windows 純正の仮想メモリ設定（パフォーマンスオプション）を開く（開くだけ・安全弁・判断の場で逃がす）。</summary>
    [RelayCommand]
    private void OpenNativeSettings() => _launcher.OpenPerformanceOptions();
}
