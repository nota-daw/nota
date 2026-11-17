// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Key combinations the operating system keeps for itself (specs/096, FR-008): the Shortcuts
// editor refuses them with a reason. Not exhaustive — other apps' global shortcuts can't be
// detected, and the editor says so.

using System;
using System.Collections.Generic;

namespace Nota.Application.Input;

public enum KeyPlatform { Mac, Windows, Linux }

public static class ReservedKeys
{
    public static KeyPlatform Current =>
        OperatingSystem.IsMacOS() ? KeyPlatform.Mac : OperatingSystem.IsWindows() ? KeyPlatform.Windows : KeyPlatform.Linux;

    private const ShortcutModifiers P = ShortcutModifiers.Primary, S = ShortcutModifiers.Shift,
        A = ShortcutModifiers.Alt, C = ShortcutModifiers.Ctrl;

    private static readonly HashSet<Shortcut> Mac = new()
    {
        new(P, "Q"), new(P, "H"), new(P | A, "H"), new(P, "Tab"), new(P | S, "Tab"), new(P, "Space"),
        new(C, "Space"), new(P, "`"), new(P | S, "3"), new(P | S, "4"), new(P | S, "5"),
        new(C | P, "Q"), new(P | A, "Esc"),
    };

    // ⌘ (Primary) is Ctrl on Windows / Linux, so Ctrl+Esc is written with P there.
    private static readonly HashSet<Shortcut> Windows = new()
    {
        new(A, "F4"), new(A, "Tab"), new(A | S, "Tab"), new(A, "Esc"), new(A, "Space"), new(P, "Esc"),
        new(P | S, "Esc"), new(P | A, "⌦"),
    };

    private static readonly HashSet<Shortcut> Linux = new()
    {
        new(A, "F4"), new(A, "Tab"), new(A | S, "Tab"), new(P | A, "⌦"),
    };

    /// <summary>Why the system keeps this combination, or null when Nota may use it.</summary>
    public static string? Reason(Shortcut s, KeyPlatform platform)
    {
        var set = platform switch { KeyPlatform.Mac => Mac, KeyPlatform.Windows => Windows, _ => Linux };
        if (set.Contains(s)) return platform switch
        {
            KeyPlatform.Mac => "macOS",
            KeyPlatform.Windows => "Windows",
            _ => "Linux",
        };
        // Ctrl+Alt+F1…F12 switch virtual terminals on Linux.
        if (platform == KeyPlatform.Linux && s.Modifiers == (P | A) && s.Key.Length is 2 or 3 && s.Key[0] == 'F'
            && int.TryParse(s.Key[1..], out int f) && f is >= 1 and <= 12) return "Linux";
        return null;
    }
}
