// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Lens popped out into its own resizable window (specs/050): the same body as the card,
// built again over the same device, with the graph taking the extra room. Both edit the same
// device params, so the card and the window follow each other. It closes itself when its Lens
// (or its track) is gone, and when a project is opened or a new one started.

using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

internal sealed class LensWindow : NotaWindow
{
    private const int LensKind = 22, M_Instance = 21, kScope = 22;
    private static readonly Dictionary<(int Track, int Device), LensWindow> Open_ = new();

    private readonly IAudioEngine _engine;
    private readonly int _trackId, _device;
    private readonly List<Action> _ticks = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly float[] _scope = new float[kScope];
    private readonly float _instance;   // the Lens's serial: another Lens shifted to this index isn't it

    private LensWindow(DeviceCardContext card, int device)
    {
        _engine = card.Engine; _trackId = card.TrackId; _device = device;
        _instance = Instance();
        Title = $"Nota Lens · {TrackNames.Of(_engine, _trackId)}";
        Width = 1100; Height = 520; MinWidth = 640; MinHeight = 260;
        CanResize = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        // A context of its own: the window ticks its body; a sidechain change still reaches the
        // track headers through the card's context.
        var ctx = new DeviceCardContext(_engine, _trackId, _ticks.Add, card.NotifyChanged, (_, _, _, _) => { }, _ => { }, _ => { },
            () => 0, _ => { }, _ => { }, () => { }, () => { }, _ => { }, () => { }, card.Factory);
        SetBody(new LensDeviceBody(popout: true).Build(ctx, device));
        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => { _timer.Stop(); Open_.Remove((_trackId, _device)); };
    }

    /// <summary>Open (or bring forward) the window of the Lens at <paramref name="device"/> on
    /// the card's track.</summary>
    public static void Open(DeviceCardContext card, int device, Control anchor)
    {
        if (Open_.TryGetValue((card.TrackId, device), out var w)) { w.Activate(); return; }
        w = new LensWindow(card, device);
        Open_[(card.TrackId, device)] = w;
        // After the click has finished, owned by the window that holds the card.
        var owner = TopLevel.GetTopLevel(anchor) as Window;
        Dispatcher.UIThread.Post(() => { w.ShowOver(owner); w.Activate(); }, DispatcherPriority.Background);
    }

    /// <summary>A new or reopened project reuses track ids: close every Lens window.</summary>
    public static void CloseAll()
    {
        foreach (var w in new List<LensWindow>(Open_.Values)) w.Close();
        Open_.Clear();
    }

    private float Instance() =>
        _engine.TrackDeviceBuiltinKind(_trackId, _device) == LensKind && _engine.DeviceScope(_trackId, _device, _scope, kScope) >= kScope
            ? _scope[M_Instance] : -1f;

    private void Tick()
    {
        if (_instance < 0 || Instance() != _instance) { Close(); return; }   // deleted, moved, or another Lens took its place
        for (int i = 0; i < _ticks.Count; i++) _ticks[i]();
    }

    private void ShowOver(Window? owner)
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
