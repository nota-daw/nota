// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The right-click menu of every text box — Cut, Copy, Paste — comes from Avalonia's theme, in
// English, and has no language setting. When Nota runs in another language this gives each text
// box a menu of its own with the same three commands, titled in that language and drawn by
// MenuKit like the rest of Nota's menus. In English nothing is replaced, so the theme's menu stays
// exactly as it was.

using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Nota.Application.Localization;

namespace Nota.App;

internal static class TextBoxMenu
{
    /// <summary>Call once at startup, after the language is installed.</summary>
    public static void Install()
    {
        if (Loc.Language == "en") return;
        Control.LoadedEvent.AddClassHandler<TextBox>(OnLoaded);
    }

    private static void OnLoaded(TextBox box, RoutedEventArgs e)
    {
        // No Nota text box has a flyout of its own (checked by grep), so the theme's is always replaced;
        // a box that comes to have one later is left alone through the marker below.
        if (box.ContextFlyout is MenuFlyout { } mine && Mine.TryGetValue(mine, out _)) return;
        var cut = MenuKit.Item(Loc.T("common.cut"), GlyphKind.Cut, box.Cut, MenuKit.CutKey);
        var copy = MenuKit.Item(Loc.T("common.copy"), GlyphKind.Copy, box.Copy, MenuKit.CopyKey);
        var paste = MenuKit.Item(Loc.T("common.paste"), GlyphKind.Paste, box.Paste, MenuKit.PasteKey);
        // The items exist before the menu is shown (a menu built in Opening came up empty); what
        // changes is only whether each is available.
        var flyout = new MenuFlyout { Items = { cut, copy, paste } };
        flyout.Opening += (_, _) => { cut.IsEnabled = box.CanCut; copy.IsEnabled = box.CanCopy; paste.IsEnabled = box.CanPaste; };
        Mine.Add(flyout, flyout);
        box.ContextFlyout = flyout;
    }

    // The flyouts made here, so a box that is loaded again does not get a second one.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<MenuFlyout, MenuFlyout> Mine = new();
}
