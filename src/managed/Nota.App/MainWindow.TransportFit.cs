// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar on a narrow window: rather than clip the right end (master, CPU, Remote)
// or let the row run past the island's edge, it sheds parts in order of how little they are
// needed. First what is shown elsewhere or merely informative, then what has a menu entry or
// a shortcut, last the readouts you set while playing. Stop / play / record, the position,
// the tempo, the metronome and the view switch never go.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace Nota.App;

public partial class MainWindow
{
    // Each step hides its controls together; steps go in this order and come back in reverse.
    private Control[][]? _transportShedOrder;
    private bool _transportFitPosted;

    private Control[][] TransportShedOrder => _transportShedOrder ??= new[]
    {
        new Control[] { CpuMeter, CpuSep },     // informative only
        new Control[] { ProjectNameText },      // moves to the title-bar caption
        new Control[] { RemoteLabel },          // Remote keeps its glyph, count and dot
        new Control[] { KeyChip },              // set once per project
        new Control[] { RemoteBtn },            // View ▸ Connect Phone…
        new Control[] { FollowBtn },            // View ▸ Follow playhead
        new Control[] { AutomationToggle },     // ⌘⇧A
        new Control[] { TimeSigCell },          // set once per project
        new Control[] { RemotePanel },          // MIDI learn (the panel goes with its last chip)
        new Control[] { SnapChip },             // the grid; snapping itself stays switchable
        new Control[] { SnapToggle },           // hold Alt to drag freely
        new Control[] { LoopSep, LoopCell },    // ⌘L; the range stays visible on the ruler
        new Control[] { MasterWell },           // the master strip is in the mixer
    };

    private void InitTransportFit()
    {
        TransportBar.SizeChanged += (_, _) => FitTransport();
    }

    /// <summary>Re-fit after something in the bar changed width (the project name, the
    /// Re-enable chip). Coalesced to one pass after the current layout.</summary>
    private void RequestTransportFit()
    {
        if (_transportFitPosted) return;
        _transportFitPosted = true;
        Dispatcher.UIThread.Post(() => { _transportFitPosted = false; FitTransport(); }, DispatcherPriority.Loaded);
    }

    /// <summary>Show everything, then hide steps until the row's natural width fits the bar.</summary>
    private void FitTransport()
    {
        double avail = TransportBar.Bounds.Width - TransportBar.Padding.Left - TransportBar.Padding.Right
                       - TransportBar.BorderThickness.Left - TransportBar.BorderThickness.Right;
        if (avail <= 0) return;

        var order = TransportShedOrder;
        foreach (var step in order)
            foreach (var c in step) SetShown(c, true);

        int hidden = 0;
        while (NaturalTransportWidth() > avail && hidden < order.Length)
        {
            foreach (var c in order[hidden]) SetShown(c, false);
            hidden++;
        }

        // The name leaves the bar for the caption, so the open project stays on screen.
        DocTitleText.Text = ProjectNameText.IsVisible ? "Nota" : ProjectDisplayName();
        TransportGrid.InvalidateMeasure();
    }

    // Measure unconstrained: the * spacer column collapses, so this is what the row needs.
    private double NaturalTransportWidth()
    {
        TransportGrid.Measure(Avalonia.Size.Infinity);
        return TransportGrid.DesiredSize.Width;
    }

    // Toggle and invalidate up to the grid, so the next unconstrained measure of the grid
    // re-measures the panels in between instead of returning their cached sizes.
    private void SetShown(Control c, bool on)
    {
        if (c.IsVisible == on) return;
        c.IsVisible = on;
        for (var p = c.Parent as Layoutable; p is not null; p = p.Parent as Layoutable)
        {
            p.InvalidateMeasure();
            if (ReferenceEquals(p, TransportGrid)) break;
        }
    }
}
