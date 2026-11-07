namespace Tamarin.Core.Hotkeys;

/// <summary>Values match the Win32 MOD_* flags used by RegisterHotKey.</summary>
[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x1,
    Control = 0x2,
    Shift = 0x4,
    Win = 0x8,
}

public sealed record Hotkey(HotkeyModifiers Modifiers, string Key, uint VirtualKey)
{
    /// <summary>Canonical text, for example "Ctrl+Shift+Space".</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }
}

public enum HotkeyProblem
{
    None,
    Empty,
    UnknownKey,
    NeedsModifier,
    AltGrConflict,
    /// <summary>Ctrl or Alt alone with a letter, digit or symbol (Ctrl+X, Alt+F...): would break copy/paste or menus in every program.</summary>
    TooCommon,
}

public readonly record struct HotkeyCheck(HotkeyProblem Problem, Hotkey? Hotkey)
{
    public bool IsValid => Problem == HotkeyProblem.None;
}

public static class HotkeyParser
{
    // Key names and their virtual-key codes. Letters and digits are handled separately below.
    private static readonly Dictionary<string, uint> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Space"] = 0x20, ["Enter"] = 0x0D, ["Tab"] = 0x09, ["Esc"] = 0x1B, ["Backspace"] = 0x08,
        ["Insert"] = 0x2D, ["Delete"] = 0x2E, ["Home"] = 0x24, ["End"] = 0x23,
        ["PageUp"] = 0x21, ["PageDown"] = 0x22,
        ["Left"] = 0x25, ["Up"] = 0x26, ["Right"] = 0x27, ["Down"] = 0x28,
        // Punctuation keys, named by what they are so the text does not depend on the keyboard layout.
        ["Semicolon"] = 0xBA, ["Equals"] = 0xBB, ["Comma"] = 0xBC, ["Minus"] = 0xBD, ["Period"] = 0xBE,
        ["Slash"] = 0xBF, ["Backquote"] = 0xC0, ["LBracket"] = 0xDB, ["Backslash"] = 0xDC,
        ["RBracket"] = 0xDD, ["Quote"] = 0xDE,
    };

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Escape"] = "Esc", ["Return"] = "Enter", ["Del"] = "Delete", ["Ins"] = "Insert",
        ["PgUp"] = "PageUp", ["PgDn"] = "PageDown", ["Spacebar"] = "Space",
    };

    private static readonly Dictionary<uint, string> NameByVk = BuildReverse();

    private static Dictionary<uint, string> BuildReverse()
    {
        var map = new Dictionary<uint, string>();
        foreach (var (name, vk) in Named) map[vk] = Canonical(name);
        return map;
    }

    private static string Canonical(string name) => Named.Keys.First(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Parses text like "ctrl+shift+space". Does not apply the "is this a good hotkey" rules; see <see cref="Validate"/>.</summary>
    public static bool TryParse(string? text, out Hotkey? hotkey)
    {
        hotkey = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var mods = HotkeyModifiers.None;
        string? keyName = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= HotkeyModifiers.Control; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win": case "windows": case "super": mods |= HotkeyModifiers.Win; break;
                default:
                    if (keyName is not null) return false; // two non-modifier keys
                    keyName = raw;
                    break;
            }
        }
        if (keyName is null) return false;
        if (!TryResolveKey(keyName, out var canonical, out var vk)) return false;

        hotkey = new Hotkey(mods, canonical, vk);
        return true;
    }

    private static bool TryResolveKey(string name, out string canonical, out uint vk)
    {
        canonical = "";
        vk = 0;
        if (Aliases.TryGetValue(name, out var aliased)) name = aliased;

        if (name.Length == 1 && char.IsAsciiLetter(name[0]))
        {
            canonical = name.ToUpperInvariant();
            vk = canonical[0]; // VK_A..VK_Z equal the ASCII capitals
            return true;
        }
        if (name.Length == 1 && char.IsAsciiDigit(name[0]))
        {
            canonical = name;
            vk = name[0]; // VK_0..VK_9 equal the ASCII digits
            return true;
        }
        if ((name[0] is 'F' or 'f') && int.TryParse(name.AsSpan(1), out var f) && f is >= 1 and <= 24)
        {
            canonical = "F" + f;
            vk = (uint)(0x70 + f - 1); // VK_F1 = 0x70
            return true;
        }
        if (Named.TryGetValue(name, out vk))
        {
            canonical = Canonical(name);
            return true;
        }
        return false;
    }

    /// <summary>Builds a hotkey from a virtual-key code, which is what the recorder control receives.</summary>
    public static bool TryFromVirtualKey(HotkeyModifiers mods, uint vk, out Hotkey? hotkey)
    {
        hotkey = null;
        string? name = null;
        if (vk is >= 0x41 and <= 0x5A) name = ((char)vk).ToString();
        else if (vk is >= 0x30 and <= 0x39) name = ((char)vk).ToString();
        else if (vk is >= 0x70 and <= 0x87) name = "F" + (vk - 0x70 + 1);
        else NameByVk.TryGetValue(vk, out name);
        if (name is null) return false;
        hotkey = new Hotkey(mods, name, vk);
        return true;
    }

    /// <summary>
    /// The rules for a hotkey that is safe to use as a global shortcut:
    /// it needs Ctrl, Alt or Win (Shift alone just types a capital letter), and Ctrl+Alt is refused
    /// because on keyboards with an AltGr key (ABNT2, German, Spanish...) Ctrl+Alt is how accents
    /// and symbols are typed, so the shortcut would break typing.
    /// </summary>
    public static HotkeyCheck Validate(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(HotkeyProblem.Empty, null);
        if (!TryParse(text, out var hk) || hk is null) return new(HotkeyProblem.UnknownKey, null);
        return Validate(hk);
    }

    public static HotkeyCheck Validate(Hotkey hk)
    {
        var m = hk.Modifiers;
        var hasMain = m.HasFlag(HotkeyModifiers.Control) || m.HasFlag(HotkeyModifiers.Alt) || m.HasFlag(HotkeyModifiers.Win);
        if (!hasMain) return new(HotkeyProblem.NeedsModifier, hk);
        if (m.HasFlag(HotkeyModifiers.Control) && m.HasFlag(HotkeyModifiers.Alt) && !m.HasFlag(HotkeyModifiers.Win))
            return new(HotkeyProblem.AltGrConflict, hk);
        // Ctrl alone (Ctrl+C, Ctrl+X, Ctrl+Z...) or Alt alone (menu letters) with a typing key is used by
        // every program. Taking it globally would break cut/copy/paste everywhere. Function keys,
        // Space, and any combination with Shift or Win are fine.
        var onlyCtrlOrAlt = !m.HasFlag(HotkeyModifiers.Shift) && !m.HasFlag(HotkeyModifiers.Win);
        if (onlyCtrlOrAlt && !IsFunctionKey(hk.VirtualKey) && hk.VirtualKey != 0x20)
            return new(HotkeyProblem.TooCommon, hk);
        return new(HotkeyProblem.None, hk);
    }

    private static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;
}
