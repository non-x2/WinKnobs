using System.Windows.Controls;

namespace WinKnobs.Views.Graphics;

/// <summary>
/// グラフィック・カテゴリ専用ペイン用 UserControl（v0.3.0）。
/// アプリ別GPU設定（列挙）＋全体トグル＋純正画面ランチャー。
/// </summary>
public partial class GraphicsListView : UserControl
{
    public GraphicsListView()
    {
        InitializeComponent();
    }
}
