using Tamarin.Core.Hotkeys;
using Tamarin.Core.Settings;
using Xunit;

namespace Tamarin.Core.Tests;

public class SettingsTests : IDisposable
{
    private readonly TestDb _t = new();
    public void Dispose() => _t.Dispose();

    [Fact]
    public void Defaults_come_from_the_migration_and_nothing_uses_the_network()
    {
        Assert.Equal("Ctrl+Shift+Space", _t.Settings.Hotkey);
        Assert.False(_t.Settings.StartWithWindows);
        Assert.Equal("system", _t.Settings.Theme);
        Assert.Equal(14, _t.Settings.TextSize);
        Assert.Equal(30, _t.Settings.TrashRetentionDays);
        Assert.True(_t.Settings.BackupEnabled);
        Assert.Equal(7, _t.Settings.BackupKeep);
        Assert.False(_t.Settings.NetworkFeatures);   // the privacy promise
        Assert.False(_t.Settings.FirstRunDone);
    }

    [Fact]
    public void Values_round_trip_and_survive_a_restart()
    {
        _t.Settings.Hotkey = "Ctrl+Alt+Shift+K";
        _t.Settings.Theme = "dark";
        _t.Settings.StartWithWindows = true;
        _t.Settings.FirstRunDone = true;

        _t.Reopen();

        Assert.Equal("Ctrl+Alt+Shift+K", _t.Settings.Hotkey);
        Assert.Equal("dark", _t.Settings.Theme);
        Assert.True(_t.Settings.StartWithWindows);
        Assert.True(_t.Settings.FirstRunDone);
    }

    [Fact]
    public void Setting_the_same_key_twice_updates_instead_of_failing()
    {
        _t.Settings.Set("custom", "one");
        _t.Settings.Set("custom", "two");
        Assert.Equal("two", _t.Settings.Get("custom"));
        Assert.Equal(1L, Convert.ToInt64(_t.Db.Scalar("SELECT COUNT(*) FROM settings WHERE key = 'custom';")));
    }

    [Fact]
    public void Numbers_are_clamped_and_bad_values_fall_back()
    {
        _t.Settings.TextSize = 500;
        Assert.Equal(SettingsStore.MaxTextSize, _t.Settings.TextSize);
        _t.Settings.TextSize = 1;
        Assert.Equal(SettingsStore.MinTextSize, _t.Settings.TextSize);

        _t.Settings.Set("text_size", "not a number");
        Assert.Equal(14, _t.Settings.TextSize);

        _t.Settings.Theme = "purple";
        Assert.Equal("system", _t.Settings.Theme);
        _t.Settings.Set("theme", "garbage");
        Assert.Equal("system", _t.Settings.Theme);

        _t.Settings.TrashRetentionDays = 0;
        Assert.Equal(1, _t.Settings.TrashRetentionDays);
    }

    [Fact]
    public void Window_position_is_remembered_per_monitor()
    {
        var left = new WindowRect(100, 200, 440, 300);
        var right = new WindowRect(2100, 150, 600, 400);
        _t.Settings.SetWindowRect(@"\\.\DISPLAY1", left);
        _t.Settings.SetWindowRect(@"\\.\DISPLAY2", right);

        Assert.Equal(left, _t.Settings.GetWindowRect(@"\\.\DISPLAY1"));
        Assert.Equal(right, _t.Settings.GetWindowRect(@"\\.\DISPLAY2"));
        Assert.Null(_t.Settings.GetWindowRect(@"\\.\DISPLAY3"));
    }

    [Fact]
    public void WindowRect_rejects_garbage_and_detects_overlap()
    {
        Assert.False(WindowRect.TryParse("", out _));
        Assert.False(WindowRect.TryParse("1,2,3", out _));
        Assert.False(WindowRect.TryParse("a,b,c,d", out _));
        Assert.False(WindowRect.TryParse("0,0,10,10", out _));  // too small to be a real window

        var screen = new WindowRect(0, 0, 1920, 1080);
        Assert.True(new WindowRect(100, 100, 440, 300).Intersects(screen));
        Assert.False(new WindowRect(3000, 100, 440, 300).Intersects(screen));   // monitor was unplugged
        Assert.False(new WindowRect(-900, 0, 440, 300).Intersects(screen));
    }
}

public class HotkeyTests
{
    [Fact]
    public void The_default_hotkey_parses_and_prints_back_the_same()
    {
        Assert.True(HotkeyParser.TryParse(SettingsStore.DefaultHotkey, out var hk));
        Assert.Equal(HotkeyModifiers.Control | HotkeyModifiers.Shift, hk!.Modifiers);
        Assert.Equal("Space", hk.Key);
        Assert.Equal(0x20u, hk.VirtualKey);
        Assert.Equal("Ctrl+Shift+Space", hk.ToString());
        Assert.True(HotkeyParser.Validate(SettingsStore.DefaultHotkey).IsValid);
    }

    [Fact]
    public void Parsing_is_forgiving_about_case_spaces_and_names_but_prints_canonically()
    {
        Assert.True(HotkeyParser.TryParse(" control + SHIFT + space ", out var hk));
        Assert.Equal("Ctrl+Shift+Space", hk!.ToString());

        Assert.True(HotkeyParser.TryParse("shift+ctrl+q", out hk));
        Assert.Equal("Ctrl+Shift+Q", hk!.ToString());                 // modifier order is canonical

        Assert.True(HotkeyParser.TryParse("Windows+Escape", out hk));
        Assert.Equal("Win+Esc", hk!.ToString());
    }

    [Fact]
    public void Virtual_key_codes_match_the_win32_values()
    {
        uint Vk(string text) { HotkeyParser.TryParse(text, out var h); return h!.VirtualKey; }
        Assert.Equal(0x41u, Vk("Ctrl+A"));
        Assert.Equal(0x5Au, Vk("Ctrl+Z"));
        Assert.Equal(0x30u, Vk("Ctrl+0"));
        Assert.Equal(0x39u, Vk("Ctrl+9"));
        Assert.Equal(0x70u, Vk("Ctrl+F1"));
        Assert.Equal(0x7Bu, Vk("Ctrl+F12"));
        Assert.Equal(0x87u, Vk("Ctrl+F24"));
        Assert.Equal(0x0Du, Vk("Ctrl+Enter"));
        Assert.Equal(0xBAu, Vk("Ctrl+Semicolon"));
    }

    [Fact]
    public void Garbage_does_not_parse()
    {
        foreach (var bad in new[] { "", "   ", "Ctrl+", "Ctrl+Shift", "Ctrl+Banana", "Ctrl+A+B", "F25", "+", "Ctrl++" })
        {
            Assert.False(HotkeyParser.TryParse(bad, out var hk), $"'{bad}' should not parse");
            Assert.Null(hk);
        }
        Assert.Equal(HotkeyProblem.Empty, HotkeyParser.Validate("").Problem);
        Assert.Equal(HotkeyProblem.UnknownKey, HotkeyParser.Validate("Ctrl+Banana").Problem);
    }

    [Fact]
    public void A_global_hotkey_needs_ctrl_alt_or_win_because_shift_alone_just_types()
    {
        Assert.Equal(HotkeyProblem.NeedsModifier, HotkeyParser.Validate("Space").Problem);
        Assert.Equal(HotkeyProblem.NeedsModifier, HotkeyParser.Validate("A").Problem);
        Assert.Equal(HotkeyProblem.NeedsModifier, HotkeyParser.Validate("Shift+A").Problem);
        Assert.Equal(HotkeyProblem.TooCommon, HotkeyParser.Validate("Ctrl+A").Problem); // would take Select All from every program
        Assert.True(HotkeyParser.Validate("Alt+Space").IsValid);
        Assert.True(HotkeyParser.Validate("Win+N").IsValid);
        Assert.True(HotkeyParser.Validate("Shift+Alt+N").IsValid);
    }

    [Fact]
    public void Ctrl_plus_Alt_is_refused_because_it_is_AltGr_on_many_keyboards()
    {
        Assert.Equal(HotkeyProblem.AltGrConflict, HotkeyParser.Validate("Ctrl+Alt+N").Problem);
        Assert.Equal(HotkeyProblem.AltGrConflict, HotkeyParser.Validate("Ctrl+Alt+Shift+K").Problem);
        Assert.True(HotkeyParser.Validate("Ctrl+Alt+Win+N").IsValid); // Win makes it not an AltGr chord
    }

    [Fact]
    public void The_recorder_can_build_a_hotkey_from_a_virtual_key_code()
    {
        Assert.True(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x20, out var hk));
        Assert.Equal("Ctrl+Shift+Space", hk!.ToString());

        Assert.True(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Alt, 0x4B, out hk));
        Assert.Equal("Alt+K", hk!.ToString());

        Assert.True(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control, 0x71, out hk));
        Assert.Equal("Ctrl+F2", hk!.ToString());

        Assert.False(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control, 0xFF, out hk)); // not a key we can name
        Assert.False(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control, 0x11, out hk)); // Ctrl itself
    }

    [Fact]
    public void Every_hotkey_that_prints_also_parses_back_to_the_same_value()
    {
        foreach (var vk in new uint[] { 0x20, 0x41, 0x35, 0x70, 0x0D, 0xBA, 0x25, 0x2E })
        {
            Assert.True(HotkeyParser.TryFromVirtualKey(HotkeyModifiers.Control | HotkeyModifiers.Win, vk, out var made));
            Assert.True(HotkeyParser.TryParse(made!.ToString(), out var parsed));
            Assert.Equal(made, parsed);
        }
    }
}
