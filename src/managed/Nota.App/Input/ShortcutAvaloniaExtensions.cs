// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Avalonia UI extension methods for Shortcut to KeyGesture conversions.

using System;
using Avalonia.Input;
using Nota.Application.Input;

namespace Nota.App.Input;

public static class ShortcutAvaloniaExtensions
{
    /// <summary>Converts a Shortcut value into an Avalonia KeyGesture for the running OS.</summary>
    public static KeyGesture? ToGesture(this Shortcut s) => ToGesture(s, OperatingSystem.IsMacOS());

    /// <summary>Converts a Shortcut value into an Avalonia KeyGesture for the specified OS.</summary>
    public static KeyGesture? ToGesture(this Shortcut s, bool mac)
    {
        if (string.IsNullOrEmpty(s.Key)) return null;

        var mods = KeyModifiers.None;
        if ((s.Modifiers & ShortcutModifiers.Primary) != 0)
            mods |= mac ? KeyModifiers.Meta : KeyModifiers.Control;
        if ((s.Modifiers & ShortcutModifiers.Alt) != 0)
            mods |= KeyModifiers.Alt;
        if ((s.Modifiers & ShortcutModifiers.Shift) != 0)
            mods |= KeyModifiers.Shift;
        if ((s.Modifiers & ShortcutModifiers.Ctrl) != 0)
            mods |= KeyModifiers.Control;

        Key key = s.Key switch
        {
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "Esc" => Key.Escape,
            _ when s.Key.Length == 1 && char.IsDigit(s.Key[0]) => Key.D0 + (s.Key[0] - '0'),
            _ => Enum.TryParse<Key>(s.Key, ignoreCase: true, out var parsed) ? parsed : Key.None,
        };

        return key == Key.None ? null : new KeyGesture(key, mods);
    }
}
