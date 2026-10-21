// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Context-menu items with a drawn icon on the left and the matching shortcut on the right,
// plus the shared parts every menu is built from (spec 034): the value / switch column,
// danger items, the clipboard group and grouped separators. The inline rows (header,
// colour grid, segments, switch) live in MenuKit.Rows.cs.
// The gesture is display-only (MenuItem.InputGesture doesn't bind anything): the real key
// handlers live in MainWindow.Input.cs / the native Edit menu / DeviceChainView, so only
// list a gesture here when one of those actually performs the same command.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Nota.Application.Input;

namespace Nota.App;

internal static partial class MenuKit
{
    private static KeyModifiers Primary => OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>⌘ + key on macOS, Ctrl + key elsewhere.</summary>
    public static KeyGesture Cmd(Key key, bool shift = false)
        => new(key, Primary | (shift ? KeyModifiers.Shift : KeyModifiers.None));

    public static readonly KeyGesture CopyKey = Cmd(Key.C);
    public static readonly KeyGesture CutKey = Cmd(Key.X);
    public static readonly KeyGesture PasteKey = Cmd(Key.V);
    public static readonly KeyGesture PasteBouncedKey = Cmd(Key.V, shift: true);
    public static readonly KeyGesture DuplicateKey = Cmd(Key.D);
    // Arrangement: Copy to Session · Session: Copy to Arrangement.
    public static readonly KeyGesture CopyToOtherViewKey = Cmd(Key.C, shift: true);
    public static readonly KeyGesture SplitKey = Cmd(Key.E);
    public static readonly KeyGesture ConsolidateKey = Cmd(Key.J);
    // Arrangement time selection: Duplicate Time · Insert Silence.
    public static readonly KeyGesture DuplicateTimeKey = Cmd(Key.D, shift: true);
    public static readonly KeyGesture InsertSilenceKey = Cmd(Key.I, shift: true);
    public static readonly KeyGesture LoopKey = Cmd(Key.L);
    public static readonly KeyGesture GroupKey = Cmd(Key.G);
    public static readonly KeyGesture UngroupKey = Cmd(Key.G, shift: true);
    public static readonly KeyGesture ActivateKey = new(Key.D0);
    // Delete and Backspace both delete; show the key the platform calls "delete".
    public static readonly KeyGesture DeleteKey = new(OperatingSystem.IsMacOS() ? Key.Back : Key.Delete);

    /// <summary>Shortcut text written with the macOS glyphs ("⌘⇧Z", "⇧-click", "hold ⌘") as this
    /// platform spells it: unchanged on macOS, "Ctrl+Shift+Z" / "Shift-click" / "hold Ctrl"
    /// elsewhere.</summary>
    public static string Keys(string text) => Shortcut.SpellText(text);

    internal static string SpellKeys(string text) => Shortcut.SpellText(text, mac: false);

    /// <summary>A menu icon. Its ink inherits the item's foreground, so it follows hover and
    /// disabled like the label does.</summary>
    public static Control Icon(GlyphKind kind) => new Glyph(kind, 11);

    public static MenuItem Item(string header, GlyphKind? icon = null, Action? click = null,
                                KeyGesture? gesture = null, bool enabled = true)
    {
        var mi = new MenuItem { Header = header, IsEnabled = enabled, InputGesture = gesture };
        if (icon is { } k) mi.Icon = Icon(k);
        if (click is not null) mi.Click += (_, _) => click();
        return mi;
    }

    // ---- The right-hand column (spec 034) -----------------------------------------------
    // The Nota MenuItem theme (NotaTheme.axaml) draws one column between the label and the
    // submenu caret: the item's Trailing content (a current value such as "In 1–2", or a
    // switch) followed by its shortcut. Trailing is attached so call sites keep plain items.

    public static readonly AttachedProperty<object?> TrailingProperty =
        AvaloniaProperty.RegisterAttached<MenuItem, object?>("Trailing", typeof(MenuKit));

    public static object? GetTrailing(MenuItem mi) => mi.GetValue(TrailingProperty);
    public static void SetTrailing(MenuItem mi, object? value) => mi.SetValue(TrailingProperty, value);

    /// <summary>A submenu item that shows its current choice in muted ink before the caret
    /// ("Record input   In 1–2 ›"); the caller adds the choices to <c>Items</c>.</summary>
    public static MenuItem ValueItem(string header, GlyphKind? icon, string value)
    {
        var mi = Item(header, icon);
        SetTrailing(mi, value);
        return mi;
    }

    /// <summary>A destructive command (delete, remove, clear, discard): danger ink at rest,
    /// the usual accent fill while highlighted. It belongs in the menu's last group.</summary>
    public static MenuItem Danger(string header, GlyphKind? icon, Action click,
                                  KeyGesture? gesture = null, bool enabled = true)
    {
        var mi = Item(header, icon, click, gesture, enabled);
        mi.Classes.Add("danger");
        return mi;
    }

    /// <summary>The clipboard group every object menu shares, always in this order with these
    /// keys: Copy ⌘C · Cut ⌘X · Paste ⌘V · Duplicate ⌘D. A null action leaves its item out;
    /// Paste is disabled while there is nothing to paste.</summary>
    public static IEnumerable<MenuItem> ClipboardGroup(Action? copy, Action? cut, Action? paste,
                                                       Action? duplicate, bool canPaste = true)
    {
        if (copy is not null) yield return Item("Copy", GlyphKind.Copy, copy, CopyKey);
        if (cut is not null) yield return Item("Cut", GlyphKind.Cut, cut, CutKey);
        if (paste is not null) yield return Item("Paste", GlyphKind.Paste, paste, PasteKey, canPaste);
        if (duplicate is not null) yield return Item("Duplicate", GlyphKind.Duplicate, duplicate, DuplicateKey);
    }

    /// <summary>Adds groups to a menu with a rule between non-empty ones, so callers can pass
    /// groups that may come out empty without leaving doubled or trailing separators.</summary>
    public static void AddGroups(ItemCollection items, params IEnumerable<Control>?[] groups)
    {
        foreach (var g in groups)
        {
            if (g is null) continue;
            var list = g.ToList();
            if (list.Count == 0) continue;
            if (items.Count > 0 && items[^1] is not Separator) items.Add(new Separator());
            foreach (var c in list) items.Add(c);
        }
    }

    // ---- Shortcut spelling --------------------------------------------------------------

    /// <summary>A menu shortcut as this platform writes it: "⌘G", "⌘⇧G", "⌫" on macOS (no
    /// separators), "Ctrl+G", "Ctrl+Shift+G", "Del" elsewhere.</summary>
    public static string Spell(KeyGesture? gesture) => Spell(gesture, OperatingSystem.IsMacOS());

    internal static string Spell(KeyGesture? gesture, bool mac)
    {
        if (gesture is null) return "";
        var mods = ShortcutModifiers.None;
        var km = gesture.KeyModifiers;
        if (km.HasFlag(KeyModifiers.Meta)) mods |= ShortcutModifiers.Primary;
        if (km.HasFlag(KeyModifiers.Control)) mods |= mac ? ShortcutModifiers.Ctrl : ShortcutModifiers.Primary;
        if (km.HasFlag(KeyModifiers.Alt)) mods |= ShortcutModifiers.Alt;
        if (km.HasFlag(KeyModifiers.Shift)) mods |= ShortcutModifiers.Shift;
        return new Shortcut(mods, KeyName(gesture.Key, mac)).ToPlatformString(mac);
    }

    private static string KeyName(Key key, bool mac) => key switch
    {
        >= Key.A and <= Key.Z => key.ToString(),
        >= Key.D0 and <= Key.D9 => ((char)('0' + (key - Key.D0))).ToString(),
        >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (key - Key.NumPad0))).ToString(),
        >= Key.F1 and <= Key.F24 => key.ToString(),
        Key.Back => KeyGlyphs.Backspace.ToString(),
        Key.Delete => mac ? "⌦" : "Del",          // ⌦ forward delete
        Key.Enter => KeyGlyphs.Return.ToString(),
        Key.Escape => "Esc",
        Key.Space => "Space",
        Key.Tab => "Tab",
        Key.Left => "←", Key.Right => "→", Key.Up => "↑", Key.Down => "↓",
        Key.OemPlus or Key.Add => "+",
        Key.OemMinus or Key.Subtract => "−",
        Key.OemComma => ",", Key.OemPeriod => ".",
        Key.OemOpenBrackets => "[", Key.OemCloseBrackets => "]",
        _ => key.ToString(),
    };
}

/// <summary>Turns a MenuItem's InputGesture into the platform spelling for the Nota menu
/// theme (Avalonia's own converter writes "⌘+G").</summary>
internal sealed class MenuGestureConverter : IValueConverter
{
    public static readonly MenuGestureConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => MenuKit.Spell(value as KeyGesture);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
