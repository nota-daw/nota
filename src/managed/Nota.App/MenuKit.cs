// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Context-menu items with a drawn icon on the left and the matching shortcut on the right.
// The gesture is display-only (MenuItem.InputGesture doesn't bind anything): the real key
// handlers live in MainWindow.Input.cs / the native Edit menu / DeviceChainView, so only
// list a gesture here when one of those actually performs the same command.

using System;
using System.Collections.Generic;
using System.Text;
using Avalonia.Controls;
using Avalonia.Input;

namespace Nota.App;

internal static class MenuKit
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
    public static string Keys(string text) => OperatingSystem.IsMacOS() ? text : SpellKeys(text);

    internal static string SpellKeys(string text)
    {
        static bool IsMod(char c) => c is '⌘' or '⌃' or '⌥' or '⇧';
        var sb = new StringBuilder(text.Length + 16);
        for (int i = 0; i < text.Length;)
        {
            if (!IsMod(text[i]))
            {
                if (text[i] == '⌫') sb.Append("Del"); else sb.Append(text[i]);
                i++;
                continue;
            }
            bool ctrl = false, alt = false, shift = false;
            for (; i < text.Length && IsMod(text[i]); i++)
            {
                ctrl |= text[i] is '⌘' or '⌃';
                alt |= text[i] == '⌥';
                shift |= text[i] == '⇧';
            }
            var names = new List<string>(3);
            if (ctrl) names.Add("Ctrl");
            if (alt) names.Add("Alt");
            if (shift) names.Add("Shift");
            sb.Append(string.Join("+", names));
            // "⌘S" → "Ctrl+S", but "⌘ + wheel" / "⇧-click" keep their own joiner.
            if (i < text.Length && !char.IsWhiteSpace(text[i]) && text[i] != '-') sb.Append('+');
        }
        return sb.ToString();
    }

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
}
