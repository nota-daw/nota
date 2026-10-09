// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Command palette (CP-24): ties a native menu item to a registered command by id —
// `app:Commands.Id="file.save"` in MainWindow.axaml. The item takes its shortcut from the
// command catalog and runs the command, so the menu, the keyboard and the palette share one
// definition. Also turns the catalog's glyph shortcuts ("⌥⌘S") into Avalonia key gestures.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;

namespace Nota.App;

public sealed class Commands : AvaloniaObject
{
    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<Commands, NativeMenuItem, string?>("Id");

    public static string? GetId(NativeMenuItem item) => item.GetValue(IdProperty);
    public static void SetId(NativeMenuItem item, string? value) => item.SetValue(IdProperty, value);

    /// <summary>A catalog shortcut as a key gesture: ⌘ is Cmd on macOS and Ctrl elsewhere,
    /// ⌃ Ctrl, ⌥ Alt, ⇧ Shift; then a key ("S", ",", "Space", "F2", "0"). Null for "".</summary>
    public static KeyGesture? Parse(string glyphs)
    {
        if (string.IsNullOrEmpty(glyphs)) return null;
        var mods = KeyModifiers.None;
        int i = 0;
        for (; i < glyphs.Length; i++)
        {
            char c = glyphs[i];
            if (c == '⌘') mods |= OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;
            else if (c == '⌃') mods |= KeyModifiers.Control;
            else if (c == '⌥') mods |= KeyModifiers.Alt;
            else if (c == '⇧') mods |= KeyModifiers.Shift;
            else break;
        }
        string k = glyphs[i..];
        Key key = k switch
        {
            "," => Key.OemComma,
            "." => Key.OemPeriod,
            "Esc" => Key.Escape,
            _ when k.Length == 1 && char.IsDigit(k[0]) => Key.D0 + (k[0] - '0'),
            _ => Enum.TryParse<Key>(k, ignoreCase: true, out var parsed) ? parsed : Key.None,
        };
        return key == Key.None ? null : new KeyGesture(key, mods);
    }

    /// <summary>True for a shortcut with a modifier — the only kind a menu item may carry (a
    /// bare Space or 0 as a menu key equivalent would swallow typing).</summary>
    public static bool IsChord(string glyphs) => glyphs.IndexOfAny(['⌘', '⌃', '⌥']) >= 0;
}
