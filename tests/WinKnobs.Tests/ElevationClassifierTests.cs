using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// ElevationClassifier（v0.1.17 で SystemProtection/MemoryCompression から一元化した
/// 「管理者権限不足」判定）の単体テスト。キーワード集の単一ソースをここで固定する。
/// </summary>
public class ElevationClassifierTests
{
    [Theory]
    [InlineData("Access is denied.")]
    [InlineData("ACCESS DENIED")]                                   // 大文字小文字非依存
    [InlineData("アクセスが拒否されました")]
    [InlineData("アクセスは拒否されました")]                        // 実機 Enable-ComputerRestore 非昇格の本物文言
    [InlineData("This command requires elevation.")]
    [InlineData("PrivilegeNotHeldException: 必要な特権が保持されていません")]
    [InlineData("管理者として実行してください")]
    [InlineData("UnauthorizedAccessException")]
    [InlineData("You must run as administrator")]
    public void LooksLikeAccessDenied_true_for_known_keywords(string output)
        => Assert.True(ElevationClassifier.LooksLikeAccessDenied(output));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VSS service is not running")]
    [InlineData("some generic failure")]
    [InlineData("access")]   // access 単独では不十分（denied 併用が条件）
    [InlineData("denied")]   // denied 単独でも不十分
    public void LooksLikeAccessDenied_false_for_non_permission_output(string? output)
        => Assert.False(ElevationClassifier.LooksLikeAccessDenied(output));
}
