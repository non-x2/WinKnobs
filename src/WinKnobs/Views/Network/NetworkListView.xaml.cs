using System.Windows.Controls;

namespace WinKnobs.Views.Network;

/// <summary>
/// ネットワーク・カテゴリ専用ペイン用 UserControl（v0.1.7・設計ドラフト 5）。
/// 三層構造（かんたん設定 / Windows 純正ランチャー / ▸ くわしい設定）。
/// </summary>
public partial class NetworkListView : UserControl
{
    public NetworkListView()
    {
        InitializeComponent();
    }
}
