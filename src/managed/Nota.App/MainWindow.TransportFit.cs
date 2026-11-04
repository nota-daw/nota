// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The transport bar on a narrow window: rather than clip the right end (master, CPU, Remote)
// or let the row run past the island's edge, it sheds parts in order of how little they are
// needed. First what is shown elsewhere or merely informative, then what has a menu entry or
// a shortcut, last the readouts you set while playing. Stop / play / record, the position,
// the tempo, the metronome and the view switch never go. The DSP meter is what you look at
// when the audio starts to crackle and isn't shown anywhere else, so it only folds to its
// number early on and goes last of all (#303).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application.Localization;

namespace Nota.App;

public partial class MainWindow
{
    // Each step hides its controls together; steps go in this order and come back in reverse.
    private Control[][]? _transportShedOrder;
    private bool _transportFitPosted;

    private Control[][] TransportShedOrder => _transportShedOrder ??= new[]
    {
        new Control[] { CpuBar },               // the DSP meter keeps its number
        new Control[] { ProjectNameText },      // moves to the title-bar caption
        new Control[] { RemoteLabel },          // Remote keeps its glyph, count and dot
        new Control[] { TapChip },              // palette ▸ Tap Tempo; the BPM field stays
        new Control[] { KeyChip },              // set once per project
        new Control[] { RemoteBtn },            // View ▸ Connect Phone…
        new Control[] { FollowBtn },            // View ▸ Follow playhead
        new Control[] { AutomationToggle },     // ⌘⇧A
        new Control[] { TimeSigCell },          // set once per project
        new Control[] { RemotePanel },          // MIDI learn (the panel goes with its last chip)
        new Control[] { SnapChip },             // the grid; snapping itself stays switchable
        new Control[] { SnapToggle },           // hold Alt to drag freely
        new Control[] { LoopRecordCell },       // ⌘⇧R / Transport menu
        new Control[] { LoopSep, LoopCell },    // ⌘L; the range stays visible on the ruler
        new Control[] { MasterWell },           // the master strip is in the mixer
        new Control[] { CpuMeter, CpuSep },     // only when nothing else is left to give
    };

    private void InitTransportFit()
    {
        TransportBar.SizeChanged += (_, _) => FitTransport();
        CpuText.AttachedToVisualTree += (_, _) => ReserveCpuTextWidth();
    }

    /// <summary>The DSP read-out grows a digit at 10% and 100%; without the bar beside it
    /// (folded first on a narrow bar) that nudged the master well, and could even flip the
    /// fit. Reserve the widest reading — "DSP 100%" in the current language — up front (#442).</summary>
    private void ReserveCpuTextWidth()
    {
        string shown = CpuText.Text ?? "";
        CpuText.MinWidth = 0;
        CpuText.Text = Loc.F("main.dsp_caps", 100);
        CpuText.Measure(Avalonia.Size.Infinity);
        double widest = CpuText.DesiredSize.Width;
        CpuText.Text = shown;
        CpuText.MinWidth = Math.Ceiling(widest);
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
        DocTitleText.Text = ProjectNameText.IsVisible ? Loc.T("common.nota") : ProjectDisplayName();
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
        // A control brought back keeps the size it had when it was last measured, parts it shed
        // meanwhile included (the Remote button came back 90 px too wide in French and was shed
        // again for nothing), so what is inside it is measured afresh.
        foreach (var inner in c.GetVisualDescendants()) (inner as Layoutable)?.InvalidateMeasure();
        // The visual parents, which include the template parts of a button; the logical chain stops there.
        for (var p = c.GetVisualParent() as Layoutable; p is not null; p = p.GetVisualParent() as Layoutable)
        {
            p.InvalidateMeasure();
            if (ReferenceEquals(p, TransportGrid)) break;
        }
    }
}
