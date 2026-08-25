// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// The detached Devices/Clip window (multi-window mode). Unlike the docked panel — which
// tabs between the clip editor and the device chain — this window shows both at once:
// the piano roll (clip editor) on top, the device chain on the bottom, with a draggable
// splitter between them. The two hosts receive the *same* control instances the main
// window uses (reparented, not rebuilt), so state and live updates carry over. On close
// it detaches them and asks the owner to re-dock.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class DetailWindow : NotaWindow
{
    public ContentControl ClipHost { get; }    // top — piano roll / clip editor
    public ContentControl ChainHost { get; }   // bottom — device chain
    public event Action? DockRequested;

    private readonly MainWindow _owner;

    public DetailWindow(MainWindow owner)
    {
        _owner = owner;

        // Route transport keys (Space/Return) in the tunnel phase so a focused control here
        // can't steal them, exactly like the main window does.
        AddHandler(KeyDownEvent, (_, e) => _owner.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);

        Title = "Devices / Clip";
        Width = 900;
        Height = 760;
        MinWidth = 520;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Application.Current?.FindResource("Brush.BgApp") is IBrush bg ? bg : Brushes.Black;

        ClipHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        ChainHost = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        // Piano roll fills the top; the device chain has its own fixed height, so the
        // bottom row just sizes to it (Auto) — no splitter needed.
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));   // clip (top)
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // device chain (bottom)

        Grid.SetRow(ClipHost, 0);
        Grid.SetRow(ChainHost, 1);
        grid.Children.Add(ClipHost);
        grid.Children.Add(ChainHost);
        SetBody(grid);
    }

    // Unhandled keys bubble up to here; forward them to the main window so its shortcuts
    // (undo/redo, R/M/A, clip copy-paste, note keys…) work while this window has focus.
    protected override void OnKeyDown(KeyEventArgs e)
    {
        _owner.HandleFloatingKeyDown(e);
        if (!e.Handled) base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        _owner.HandleKeyUp(e);
        if (!e.Handled) base.OnKeyUp(e);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (e.Cancel) return;
        // Detach the hosted controls before they go back to the main window (a control
        // can have only one parent).
        ClipHost.Content = null;
        ChainHost.Content = null;
        DockRequested?.Invoke();
    }
}
