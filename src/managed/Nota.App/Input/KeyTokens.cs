// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Avalonia keys ⇄ the key tokens bindings are stored in (specs/096, research R4): "A", "7",
// "F2", "Space", "Esc", "⌫", "←", "[" … Keypad digits read as the digit (⌘1 and keypad ⌘1 both
// narrow the grid, as before). A pressed key becomes a Shortcut here, with ⌘ = Cmd on macOS
// and Ctrl elsewhere — the one place that mapping lives.

using System;
using System.Collections.Generic;
using Avalonia.Input;
using Nota.Application.Input;

namespace Nota.App.Input;

public static class KeyTokens
{
    private static readonly Dictionary<Key, string> Named = new()
    {
        [Key.Space] = "Space", [Key.Return] = "Return", [Key.Tab] = "Tab", [Key.Escape] = "Esc",
        [Key.Back] = "⌫", [Key.Delete] = "⌦", [Key.Left] = "←", [Key.Right] = "→", [Key.Up] = "↑", [Key.Down] = "↓",
        [Key.PageUp] = "PgUp", [Key.PageDown] = "PgDn", [Key.Home] = "Home", [Key.End] = "End",
        [Key.OemComma] = ",", [Key.OemPeriod] = ".", [Key.OemQuestion] = "/", [Key.OemSemicolon] = ";",
        [Key.OemQuotes] = "'", [Key.OemOpenBrackets] = "[", [Key.OemCloseBrackets] = "]", [Key.OemPipe] = "\\",
        [Key.OemBackslash] = "\\", [Key.OemMinus] = "-", [Key.OemPlus] = "=", [Key.OemTilde] = "`",
    };

    private static readonly Dictionary<string, Key> Reverse = BuildReverse();

    private static Dictionary<string, Key> BuildReverse()
    {
        var r = new Dictionary<string, Key>(StringComparer.Ordinal);
        foreach (var (k, t) in Named) r.TryAdd(t, k);   // OemPipe wins over OemBackslash for "\"
        return r;
    }

    /// <summary>The token for a key, or null for a modifier or a key bindings don't use.</summary>
    public static string? Token(Key key)
    {
        if (key is >= Key.A and <= Key.Z) return ((char)('A' + (key - Key.A))).ToString();
        if (key is >= Key.D0 and <= Key.D9) return ((char)('0' + (key - Key.D0))).ToString();
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return ((char)('0' + (key - Key.NumPad0))).ToString();
        if (key is >= Key.F1 and <= Key.F24) return "F" + (1 + (key - Key.F1));
        return Named.GetValueOrDefault(key);
    }

    /// <summary>The Avalonia key for a token (the main-row digit for "0"–"9").</summary>
    public static Key ToKey(string token)
    {
        if (token.Length == 1 && token[0] is >= 'A' and <= 'Z') return Key.A + (token[0] - 'A');
        if (token.Length == 1 && token[0] is >= '0' and <= '9') return Key.D0 + (token[0] - '0');
        if (token.Length is 2 or 3 && token[0] == 'F' && int.TryParse(token[1..], out int f) && f is >= 1 and <= 24) return Key.F1 + (f - 1);
        if (Reverse.TryGetValue(token, out var k)) return k;
        if (token == "Delete") return Key.Back;   // older glyph text
        return Enum.TryParse<Key>(token, ignoreCase: true, out var parsed) ? parsed : Key.None;
    }

    public static bool IsModifierKey(Key k) => k is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
        or Key.LeftAlt or Key.RightAlt or Key.LWin or Key.RWin or Key.System or Key.CapsLock;

    /// <summary>The Win / Super key is held (Windows / Linux) — the system's, never Nota's.</summary>
    public static bool HasSystemKey(KeyModifiers m) => !OperatingSystem.IsMacOS() && (m & KeyModifiers.Meta) != 0;

    /// <summary>The pressed key as a binding chord, or null (a bare modifier, an unused key, or
    /// the Win / Super key held).</summary>
    public static Shortcut? Chord(KeyEventArgs e) => Chord(e.Key, e.KeyModifiers, OperatingSystem.IsMacOS());

    public static Shortcut? Chord(Key key, KeyModifiers m, bool mac)
    {
        if (IsModifierKey(key) || Token(key) is not { } token) return null;
        var mods = ShortcutModifiers.None;
        if (mac)
        {
            if ((m & KeyModifiers.Meta) != 0) mods |= ShortcutModifiers.Primary;
            if ((m & KeyModifiers.Control) != 0) mods |= ShortcutModifiers.Ctrl;
        }
        else
        {
            if ((m & KeyModifiers.Meta) != 0) return null;
            if ((m & KeyModifiers.Control) != 0) mods |= ShortcutModifiers.Primary;
        }
        if ((m & KeyModifiers.Alt) != 0) mods |= ShortcutModifiers.Alt;
        if ((m & KeyModifiers.Shift) != 0) mods |= ShortcutModifiers.Shift;
        return new Shortcut(mods, token);
    }
}
