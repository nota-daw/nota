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
// The Menu key (Windows / Linux, #518) is never in the order either: with the menu row hidden
// it is the only way to the menu by mouse. Nor is the ⋯ key at the end (#520): nothing shed is
// lost — the ⋯ menu lists every part the bar had no room for. The order itself lives in
// Nota.Presentation.ToolbarOverflow, so the menu and the smoke test read the same list.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public partial class MainWindow
{
    // Each step hides its controls together; steps go in ToolbarOverflow.ShedOrder and come
    // back in reverse. What a step hid is listed in the ⋯ menu (MainWindow.TransportOverflow.cs).
    private Control[][]? _transportShedOrder;
    private bool _transportFitPosted;

    /// <summary>How many steps of the shed order the last fit hid (0 = the whole bar shows).</summary>
    private int _shedSteps;

    private Control[][] TransportShedOrder => _transportShedOrder ??=
        ToolbarOverflow.ShedOrder.Select(ShedControls).ToArray();

    // The controls a part takes with it.
    private Control[] ShedControls(ToolbarPart part) => part switch
    {
        ToolbarPart.CpuBar => [CpuBar],                 // the DSP meter keeps its number
        ToolbarPart.RemoteLabel => [RemoteLabel],       // Remote keeps its glyph, count and dot
        ToolbarPart.Tap => [TapChip],                   // palette ▸ Tap Tempo; the BPM field stays
        ToolbarPart.Key => [KeyChip],                   // set once per project
        ToolbarPart.Remote => [RemoteBtn],              // View ▸ Connect Phone…
        ToolbarPart.Follow => [FollowBtn],              // View ▸ Follow playhead
        ToolbarPart.Automation => [AutomationToggle],   // ⌘⇧A
        ToolbarPart.Mixer => [MixerToggle],             // View ▸ Mixer, ⌘⇧M
        ToolbarPart.TimeSignature => [TimeSigCell],     // set once per project
        ToolbarPart.MidiLearn => [RemotePanel],         // MIDI learn (the panel goes with its last chip)
        ToolbarPart.Grid => [GridSep, SnapChip],        // the grid; snapping itself stays switchable
        ToolbarPart.Snap => [SnapToggle],               // hold Alt to drag freely
        ToolbarPart.LoopRecord => [LoopRecordCell],     // ⌘⇧R / Transport menu
        ToolbarPart.Loop => [LoopSep, LoopCell],        // ⌘L; the range stays visible on the ruler
        ToolbarPart.Master => [MasterWell],             // the master strip is in the mixer
        ToolbarPart.DspMeter => [CpuMeter, CpuSep],     // only when nothing else is left to give
        _ => [],
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

    /// <summary>Re-fit after something in the bar changed width (the Re-enable chip). Coalesced to one pass after the current layout.</summary>
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

        _shedSteps = hidden;
        OverflowDot.IsVisible = hidden > 0;
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
