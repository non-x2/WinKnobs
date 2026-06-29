using System.Collections.Generic;
using Microsoft.Win32;
using WinKnobs.Services;

namespace WinKnobs.Tests;

/// <summary>
/// SystemSoundHelper（v0.3.3 システム効果音 ON/OFF・sound.systemSoundsScheme）の純粋関数テスト。
/// レジストリ I/O（列挙・書込）は SystemSoundService の領域で VM/実機検証に委ねる（Helper は純粋部分のみ）。
/// </summary>
public class SystemSoundHelperTests
{
    [Fact]
    public void Choices_has_two_entries_on_then_off()
    {
        Assert.Equal(2, SystemSoundHelper.Choices.Count);
        Assert.Contains("オン", SystemSoundHelper.Choices[0]);
        Assert.Contains("オフ", SystemSoundHelper.Choices[1]);
    }

    // ─── IsMuteScheme：.None のみ true（大小無視） ───
    [Theory]
    [InlineData(".None", true)]
    [InlineData(".none", true)]
    [InlineData(".NONE", true)]
    [InlineData(".Default", false)]
    [InlineData("MyCustomScheme", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsMuteScheme_true_only_for_none(string? scheme, bool expected)
        => Assert.Equal(expected, SystemSoundHelper.IsMuteScheme(scheme));

    // ─── LabelForScheme：必ず Choices の同一参照 ───
    [Fact]
    public void LabelForScheme_none_returns_off_same_reference()
        => Assert.Same(SystemSoundHelper.Choices[1], SystemSoundHelper.LabelForScheme(".None"));

    [Theory]
    [InlineData(".Default")]
    [InlineData("custom")]
    [InlineData(null)]
    public void LabelForScheme_non_none_returns_on_same_reference(string? scheme)
        => Assert.Same(SystemSoundHelper.Choices[0], SystemSoundHelper.LabelForScheme(scheme));

    // ─── IsMuteLabel：Choices[1]（消音）だけ true ───
    [Fact]
    public void IsMuteLabel_true_for_off_choice()
        => Assert.True(SystemSoundHelper.IsMuteLabel(SystemSoundHelper.Choices[1]));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("でたらめ")]
    public void IsMuteLabel_false_for_unknown(string? label)
        => Assert.False(SystemSoundHelper.IsMuteLabel(label));

    [Fact]
    public void IsMuteLabel_false_for_on_choice()
        => Assert.False(SystemSoundHelper.IsMuteLabel(SystemSoundHelper.Choices[0]));

    // ─── パス構築 ───
    [Fact]
    public void BuildEventCurrentPath_formats_expected()
        => Assert.Equal(
            @"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current",
            SystemSoundHelper.BuildEventCurrentPath(".Default", "SystemAsterisk"));

    [Fact]
    public void BuildEventDefaultPath_formats_expected()
        => Assert.Equal(
            @"HKCU\AppEvents\Schemes\Apps\Explorer\Navigating\.Default",
            SystemSoundHelper.BuildEventDefaultPath("Explorer", "Navigating"));

    // ─── ToSiblingDefaultPath：末尾 \.Current → \.Default／不正末尾は例外（cross-reviewer 指摘7/9） ───
    [Fact]
    public void ToSiblingDefaultPath_converts_current_to_default()
        => Assert.Equal(
            @"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Default",
            SystemSoundHelper.ToSiblingDefaultPath(
                @"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current"));

    [Fact]
    public void ToSiblingDefaultPath_roundtrips_with_build()
    {
        var current = SystemSoundHelper.BuildEventCurrentPath("Explorer", "Navigating");
        Assert.Equal(SystemSoundHelper.BuildEventDefaultPath("Explorer", "Navigating"),
            SystemSoundHelper.ToSiblingDefaultPath(current));
    }

    [Theory]
    [InlineData(@"HKCU\AppEvents\Schemes\Apps\X\Y\.Default")]   // 末尾が .Current でない
    [InlineData(@"HKCU\AppEvents\Schemes")]
    [InlineData("")]
    public void ToSiblingDefaultPath_rejects_non_current_tail(string path)
        => Assert.Throws<System.ArgumentException>(() => SystemSoundHelper.ToSiblingDefaultPath(path));

    // ─── 型 int↔Kind：2=ExpandString／他=String ───
    [Fact]
    public void KindToInt_maps_expand_to_2_else_1()
    {
        Assert.Equal(2, SystemSoundHelper.KindToInt(RegistryValueKind.ExpandString));
        Assert.Equal(1, SystemSoundHelper.KindToInt(RegistryValueKind.String));
        Assert.Equal(1, SystemSoundHelper.KindToInt(RegistryValueKind.DWord));   // 想定外型は SZ に丸め
    }

    [Fact]
    public void IntToKind_maps_2_to_expand_else_string()
    {
        Assert.Equal(RegistryValueKind.ExpandString, SystemSoundHelper.IntToKind(2));
        Assert.Equal(RegistryValueKind.String, SystemSoundHelper.IntToKind(1));
        Assert.Equal(RegistryValueKind.String, SystemSoundHelper.IntToKind(99));
    }

    [Theory]
    [InlineData(RegistryValueKind.String)]
    [InlineData(RegistryValueKind.ExpandString)]
    public void Kind_int_kind_roundtrip(RegistryValueKind kind)
        => Assert.Equal(kind, SystemSoundHelper.IntToKind(SystemSoundHelper.KindToInt(kind)));

    // ─── 退避 JSON 往復：existed/値/型（REG_EXPAND_SZ 含む）を保持 ───
    [Fact]
    public void Backup_roundtrip_preserves_entries()
    {
        var backup = new SoundBackup { Scheme = ".Default" };
        backup.Events[@"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current"] =
            new SoundEventEntry { Existed = true, Value = @"%SystemRoot%\media\Windows Foreground.wav", Kind = 2 };
        backup.Events[@"HKCU\AppEvents\Schemes\Apps\.Default\Notification.Default\.Current"] =
            new SoundEventEntry { Existed = true, Value = @"C:\Windows\media\notify.wav", Kind = 1 };
        backup.Events[@"HKCU\AppEvents\Schemes\Apps\.Default\EmptyEvent\.Current"] =
            new SoundEventEntry { Existed = false, Value = "", Kind = 1 };

        var json = SystemSoundHelper.Serialize(backup);
        Assert.Contains("\"existed\"", json);   // JSON キーは短縮形を維持
        var back = SystemSoundHelper.TryDeserialize(json);

        Assert.NotNull(back);
        Assert.Equal(".Default", back!.Scheme);
        Assert.Equal(3, back.Events.Count);

        var expand = back.Events[@"HKCU\AppEvents\Schemes\Apps\.Default\SystemAsterisk\.Current"];
        Assert.True(expand.Existed);
        Assert.Equal(@"%SystemRoot%\media\Windows Foreground.wav", expand.Value);
        Assert.Equal(2, expand.Kind);   // REG_EXPAND_SZ 保持

        var none = back.Events[@"HKCU\AppEvents\Schemes\Apps\.Default\EmptyEvent\.Current"];
        Assert.False(none.Existed);   // 値なし未設定（"" 無音との区別）
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{ this is not json")]
    public void TryDeserialize_invalid_returns_null(string? json)
        => Assert.Null(SystemSoundHelper.TryDeserialize(json));

    // ─── 多数イベント（~1000件）退避往復の健全性（plan-validator エッジ2） ───
    [Fact]
    public void Backup_roundtrip_handles_many_events()
    {
        var backup = new SoundBackup { Scheme = ".None" };
        for (int i = 0; i < 1000; i++)
        {
            var path = SystemSoundHelper.BuildEventCurrentPath(".Default", $"Event{i}");
            backup.Events[path] = new SoundEventEntry
            {
                Existed = i % 3 != 0,                 // 一部 Existed=false
                Value = i % 2 == 0 ? "" : $@"%SystemRoot%\media\e{i}.wav",
                Kind = i % 2 == 0 ? 1 : 2
            };
        }

        var back = SystemSoundHelper.TryDeserialize(SystemSoundHelper.Serialize(backup));

        Assert.NotNull(back);
        Assert.Equal(".None", back!.Scheme);
        Assert.Equal(1000, back.Events.Count);

        // Event997：i=997 は奇数（Value=.wav・Kind=2）かつ 997%3=1≠0（Existed=true）。
        var sample = back.Events[SystemSoundHelper.BuildEventCurrentPath(".Default", "Event997")];
        Assert.True(sample.Existed);
        Assert.Equal(@"%SystemRoot%\media\e997.wav", sample.Value);
        Assert.Equal(2, sample.Kind);
    }
}
