// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// A Modular Scope node popped out into its own resizable window (specs/047): the same live
// trace and the same controls as the node, the trace filling the window. It closes itself
// when its Scope (or its track) is gone. Settings changed here or on the node follow each
// other through the engine fields; freeze is shared through the Modular view.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

internal sealed class ScopeWindow : NotaWindow
{
    private readonly ModularView _view;
    private readonly IAudioEngine _engine;
    private readonly int _trackId, _modId;
    private readonly List<Action> _ticks = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private bool _refillQueued;

    public ScopeWindow(ModularView view, IAudioEngine engine, int trackId, int modId, string title)
    {
        _view = view; _engine = engine; _trackId = trackId; _modId = modId;
        Title = title;
        Width = 720; Height = 340; MinWidth = 420; MinHeight = 220;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refill();
    }

    private void Refill()
    {
        _ticks.Clear();
        var viz = new ModularView.ScopeViz(_engine, _trackId, _modId, () => _view.ScopeHeld(_trackId, _modId));
        _ticks.Add(viz.Update);
        var controls = _view.ScopeControls(_trackId, _modId, _ticks.Add, QueueRefill);
        var side = new Border { Width = 214, Child = controls, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        DockPanel.SetDock(side, Dock.Right);
        var dock = new DockPanel { Margin = new Thickness(12) };
        dock.Children.Add(side);
        dock.Children.Add(new Border { Margin = new Thickness(0, 0, 12, 0), Child = viz });
        SetBody(dock);
    }

    private void QueueRefill()
    {
        if (_refillQueued) return;
        _refillQueued = true;
        Dispatcher.UIThread.Post(() => { _refillQueued = false; Refill(); });
    }

    private void Tick()
    {
        if (_engine.ModulatorKind(_trackId, _modId) != ScopeTrace.Kind) { Close(); return; }   // deleted
        for (int i = 0; i < _ticks.Count; i++) _ticks[i]();
    }

    /// <summary>Show over <paramref name="owner"/> (the window that holds the node; the main
    /// window when that is gone), so it stays in front of it; over the main window, forward
    /// its keys (transport, undo …) as the device window does.</summary>
    public void ShowOver(Window? owner)
    {
        owner ??= (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        if (owner is null) { Show(); return; }
        if (owner is MainWindow mw)
        {
            AddHandler(KeyDownEvent, (_, e) => mw.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);
            KeyDown += (_, e) => { if (!e.Handled) mw.HandleFloatingKeyDown(e); };
            KeyUp += (_, e) => { if (!e.Handled) mw.HandleKeyUp(e); };
        }
        Show(owner);
    }
}
