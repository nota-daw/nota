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
using Nota.App.Input;
using Nota.Application.Input;

namespace Nota.App;

public sealed class Commands : AvaloniaObject
{
    public static readonly AttachedProperty<string?> IdProperty =
        AvaloniaProperty.RegisterAttached<Commands, NativeMenuItem, string?>("Id");

    public static string? GetId(NativeMenuItem item) => item.GetValue(IdProperty);
    public static void SetId(NativeMenuItem item, string? value) => item.SetValue(IdProperty, value);

    /// <summary>A catalog shortcut as a key gesture: ⌘ is Cmd on macOS and Ctrl elsewhere,
    /// ⌃ Ctrl, ⌥ Alt, ⇧ Shift; then a key ("S", ",", "Space", "F2", "0"). Null for "".</summary>
    public static KeyGesture? Parse(string glyphs) =>
        string.IsNullOrEmpty(glyphs) ? null : Shortcut.Parse(glyphs).ToGesture();

    /// <summary>True for a shortcut with a modifier (⌘S, ⌥⌘S). Bare letter/digit keys stay
    /// out of menus — a bare Space or 0 as a menu key equivalent would swallow typing.</summary>
    public static bool IsChord(string glyphs) =>
        !string.IsNullOrEmpty(glyphs) && Shortcut.Parse(glyphs).IsChord;

    /// <summary>True for a shortcut a menu item may carry: a chord, or a bare function key
    /// (F11) — function keys type nothing, so unlike a bare letter/digit they can't
    /// swallow text input.</summary>
    public static bool CanMenuGesture(string glyphs) =>
        !string.IsNullOrEmpty(glyphs) && Shortcut.Parse(glyphs).CanMenuGesture;
}
