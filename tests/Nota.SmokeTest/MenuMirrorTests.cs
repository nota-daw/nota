// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Menu key's menu (#518, specs/093): the copy MenuMirror makes of the application menu
// matches its source entry for entry — order, separators, submenus, labels, shortcuts, check
// marks, enabled and visible state, tooltips — and a copy made after the source changed shows
// the change (the key's menu is rebuilt at every opening, never kept).

using Avalonia.Controls;
using Avalonia.Input;
using Nota.App;

internal static class MenuMirrorTests
{
    public static IEnumerable<(bool, string)> Run()
    {
        var snap = new NativeMenuItem("Snap to grid") { ToggleType = MenuItemToggleType.CheckBox, IsChecked = true };
        var recent = new NativeMenu();
        recent.Items.Add(new NativeMenuItem("No recent projects") { IsEnabled = false });
        var file = new NativeMenu();
        file.Items.Add(new NativeMenuItem("New") { Gesture = new KeyGesture(Key.N, KeyModifiers.Control) });
        file.Items.Add(new NativeMenuItem("Open recent") { Menu = recent });
        file.Items.Add(new NativeMenuItemSeparator());
        file.Items.Add(new NativeMenuItem("Exit") { Gesture = new KeyGesture(Key.Q, KeyModifiers.Control) });
        var grid = new NativeMenu();
        grid.Items.Add(snap);
        grid.Items.Add(new NativeMenuItem("Triplet grid") { ToggleType = MenuItemToggleType.CheckBox });
        var edit = new NativeMenu();
        edit.Items.Add(new NativeMenuItem("Undo") { Gesture = new KeyGesture(Key.Z, KeyModifiers.Control) });
        edit.Items.Add(new NativeMenuItem("Grid") { Menu = grid });
        var root = new NativeMenu();
        root.Items.Add(new NativeMenuItem("File") { Menu = file });
        root.Items.Add(new NativeMenuItem("Edit") { Menu = edit });
        root.Items.Add(new NativeMenuItem("Help"));

        string source = MenuMirror.Describe(root);
        var copy = MenuMirror.Build(root);
        string mirrored = MenuMirror.Describe(copy);
        int lines = source.Count(c => c == '\n');
        yield return (lines == 12 && source == mirrored,
            source == mirrored ? $"the Menu key's copy matches the menu ({lines} entries, submenus, separators, shortcuts, checks)"
                : $"the Menu key's copy differs:\n{source}\nvs\n{mirrored}");

        // Change the source as the app does between two openings: a check mark flips, the
        // recent list is rebuilt, an item is hidden.
        snap.IsChecked = false;
        recent.Items.Clear();
        recent.Items.Add(new NativeMenuItem("My__song") { ToolTip = "/music/My_song.nota" });
        ((NativeMenuItem)root.Items[2]).IsVisible = false;
        string after = MenuMirror.Describe(root);
        string remirrored = MenuMirror.Describe(MenuMirror.Build(root));
        yield return (after != source && after == remirrored,
            after == remirrored ? "a copy made after a change shows it (check mark, recent list, hidden item)"
                : $"the second copy is stale:\n{after}\nvs\n{remirrored}");

        // Picking a copied item runs the source item's click.
        int clicks = 0;
        var exit = (NativeMenuItem)file.Items[3];
        exit.Click += (_, _) => clicks++;
        var copyFile = (MenuItem)MenuMirror.Build(root)[0];
        var copyExit = copyFile.Items.OfType<MenuItem>().Last();
        copyExit.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        yield return (clicks == 1, $"picking a copied item runs the menu's own item (clicks: {clicks})");
    }
}
