// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The items panel of every menu, flyout menu and submenu (NotaTheme.axaml). The Nota
// MenuItem theme lines its columns up through shared-size groups (icon · label · value ·
// shortcut · caret), and a Grid only learns its group's width after a layout pass ends
// (SharedSizeState updates on LayoutUpdated). The first pass therefore saw each item alone,
// and a menu whose widest label sits on an item without a shortcut or caret came out too
// narrow; the native popup window takes its size from that first pass and does not grow
// when the shared columns settle, so every row was centred in it and clipped on both
// sides. This panel reports the settled width already on the first pass: the widest
// "padding + label" of any item plus the widest of each shared column.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace Nota.App;

public sealed class MenuItemsPanel : StackPanel
{
    private const int Columns = 5;   // icon, label, trailing, shortcut, caret — NotaTheme.axaml

    protected override Size MeasureOverride(Size availableSize)
    {
        var size = base.MeasureOverride(availableSize);
        Span<double> shared = stackalloc double[Columns], cols = stackalloc double[Columns];
        double own = 0;
        foreach (var child in Children)
        {
            if (child is not MenuItem { IsVisible: true } mi || ItemGrid(mi) is not { } grid) continue;
            cols.Clear();
            foreach (var part in grid.Children)
            {
                int c = Grid.GetColumn(part);
                if (c >= 0 && c < Columns) cols[c] = Math.Max(cols[c], part.DesiredSize.Width);
            }
            for (int c = 0; c < Columns; c++) if (c != 1) shared[c] = Math.Max(shared[c], cols[c]);
            // The item's own share: its padding and frame around the grid, plus the label.
            own = Math.Max(own, mi.DesiredSize.Width - grid.DesiredSize.Width + cols[1]);
        }
        double width = own + shared[0] + shared[2] + shared[3] + shared[4];
        if (width > size.Width) size = size.WithWidth(Math.Min(width, availableSize.Width));
        return size;
    }

    // The column grid of an item in the Nota MenuItem template (an inline row has none).
    private static Grid? ItemGrid(MenuItem mi)
        => mi.GetVisualDescendants().OfType<ContentPresenter>().FirstOrDefault(p => p.Name == "PART_HeaderPresenter")?.Parent as Grid;
}
