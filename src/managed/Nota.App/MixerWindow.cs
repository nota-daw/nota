// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// The Mixer in its own window (opened from View → Mixer). It hosts the single MixerView
// instance the main window owns (reparented, not rebuilt), so meters/state carry over and
// the tick loop keeps updating it while the window is open. Closing detaches the mixer.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class MixerWindow : NotaWindow
{
    public ContentControl Host { get; }
    public event Action? WindowClosed;

    private readonly MainWindow _owner;

    public MixerWindow(MainWindow owner)
    {
        _owner = owner;

        // Route transport keys (Space/Return) in the tunnel phase so a focused control here
        // can't steal them, exactly like the main window does.
        AddHandler(KeyDownEvent, (_, e) => _owner.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);

        Title = "Mixer";
        Width = 1040;
        Height = 460;
        MinWidth = 560;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Avalonia.Application.Current?.FindResource("Brush.BgApp") is IBrush bg ? bg : Brushes.Black;

        Host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
        SetBody(Host);
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        Host.Content = null;   // detach the mixer so it can be re-hosted next time
        WindowClosed?.Invoke();
    }
}
