// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Modular Scope node (specs/047): a CV monitor whose trace can be resized by the grip
// under it or popped out into its own window, with a time window (ms, or beats when
// synced), a vertical zoom, a rising-edge trigger and a freeze. The window, sync, zoom,
// trigger and trace height are modulator fields saved with the project; freeze is a view
// state shared by the node and its window. The maths lives in ScopeTrace.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;

namespace Nota.App;

public sealed partial class ModularView
{
    // A frozen Scope keeps the history it had when Freeze went on, so the node and its window
    // (rebuilt on every chip change) both draw the held trace.
    private readonly Dictionary<(int Track, int Mod), ScopeSnapshot> _scopeFrozen = new();
    internal sealed record ScopeSnapshot(float[] Values, float[] Dts, int Count);
    private readonly Dictionary<(int Track, int Mod), ScopeWindow> _scopeWindows = new();
    private bool _rebuildQueued;

    internal bool ScopeFrozen(int trackId, int modId) => _scopeFrozen.ContainsKey((trackId, modId));
    internal ScopeSnapshot? ScopeHeld(int trackId, int modId) => _scopeFrozen.GetValueOrDefault((trackId, modId));

    private void SetScopeFrozen(int trackId, int modId, bool on)
    {
        if (!on) { _scopeFrozen.Remove((trackId, modId)); return; }
        var v = new float[ScopeTrace.HistoryLength]; var dt = new float[ScopeTrace.HistoryLength];
        int n = _engine.ModulatorScopeTimed(trackId, modId, v, dt);
        _scopeFrozen[(trackId, modId)] = new ScopeSnapshot(v, dt, Math.Max(0, n));
    }

    // Rebuild on the next dispatcher turn: safe from inside a live-follow tick, which
    // Rebuild would otherwise clear while it runs.
    private void QueueRebuild()
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.UIThread.Post(() => { _rebuildQueued = false; Rebuild(); });
    }

    // Scope: a CV monitor — one CV input (patch a cable in) drives an oscilloscope; the
    // incoming CV also passes straight through to CV-out.
    private void BuildScopeView(GNode n, int modId)
    {
        var col = new Grid { RowDefinitions = new RowDefinitions("3,32,Auto,Auto,Auto") };
        col.Children.Add(Row(new Border { Background = n.Stripe, CornerRadius = NotaRadius.Top(NotaRadius.TileValue) }, 0));
        var headBorder = NodeHeader(n);
        col.Children.Add(Row(headBorder, 1));

        int t = _trackId; var e = _engine;
        var viz = new ScopeViz(e, t, modId, () => ScopeHeld(t, modId));
        _liveKnobs.Add(viz.Update);
        // Unwired, the scope says what it can watch: a modulator's CV, patched into its input. It
        // doesn't tap audio — a first-time user tried to hang it off a filter's output (#261). The hint
        // stands in for the trace, which would only draw a flat line through it.
        Control body = viz;
        if ((int)Math.Round(e.ModulatorGet(t, modId, 10)) < 0)
        {
            body = new TextBlock
            {
                Text = "Watches CV. Drag a modulator's CV out onto this node. For audio, put Lens on the chain.",
                FontSize = 9, Foreground = Text3, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0),
            };
        }
        double h0 = Math.Clamp(e.ModulatorGet(t, modId, ScopeTrace.FieldHeight), ScopeTrace.MinHeight, ScopeTrace.MaxHeight);
        var vizBox = new Border { Height = h0, Margin = new Thickness(9, 6, 9, 0), Child = body };
        col.Children.Add(Row(new StackPanel { Children = { vizBox, ScopeGrip(vizBox, t, modId) } }, 2));
        col.Children.Add(Row(ScopeControls(t, modId, _liveKnobs.Add, QueueRebuild), 3));
        col.Children.Add(Row(PortsBlock(n), 4));
        WrapFrame(n, col, headBorder);
    }

    // The bar under the trace: drag it to make the trace taller or shorter (76–320 px).
    private Control ScopeGrip(Border box, int t, int modId)
    {
        var line = new Border
        {
            Width = 28, Height = 2, CornerRadius = NotaRadius.Badge, Background = NotaPalette.Seam,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        var grip = new Border { Height = 9, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Child = line };
        ToolTip.SetTip(grip, "Drag to resize the trace");
        bool drag = false; double y0 = 0, h0 = 0;
        grip.PointerEntered += (_, _) => line.Background = Text3;
        grip.PointerExited += (_, _) => { if (!drag) line.Background = NotaPalette.Seam; };
        grip.PointerPressed += (_, ev) =>
        {
            drag = true; y0 = ev.GetPosition(this).Y; h0 = box.Height;
            ev.Pointer.Capture(grip); ev.Handled = true;
        };
        grip.PointerMoved += (_, ev) =>
        {
            if (!drag) return;
            double dy = (ev.GetPosition(this).Y - y0) / Math.Max(0.05, _scale);   // screen px → node px
            box.Height = Math.Round(Math.Clamp(h0 + dy, ScopeTrace.MinHeight, ScopeTrace.MaxHeight));
            ev.Handled = true;
        };
        grip.PointerReleased += (_, ev) =>
        {
            if (!drag) return;
            drag = false; ev.Pointer.Capture(null); line.Background = NotaPalette.Seam;
            _engine.ModulatorSet(t, modId, ScopeTrace.FieldHeight, (float)box.Height);
            Changed?.Invoke();
            ev.Handled = true;
        };
        return grip;
    }

    // Window + zoom knobs, then the trigger and freeze chips. Shared by the node and its
    // window: `addTick` registers the live follow, `refresh` rebuilds the host when a
    // chip changes (or the other copy changed one).
    internal Control ScopeControls(int t, int modId, Action<Action> addTick, Action refresh)
    {
        var e = _engine;
        bool sync = e.ModulatorGet(t, modId, ScopeTrace.FieldSync) > 0.5f;
        bool trig = e.ModulatorGet(t, modId, ScopeTrace.FieldTrigger) > 0.5f;
        bool frozen = ScopeFrozen(t, modId);
        int lastBeat = ScopeTrace.BeatSteps.Length - 1;

        // Window: milliseconds on a log knob, or musical steps when synced.
        int wField = sync ? ScopeTrace.FieldWindowBeats : ScopeTrace.FieldWindowMs;
        double ToFrac(float v) => sync ? ScopeTrace.BeatIndex(v) / (double)lastBeat : ScopeTrace.MsToKnob(v);
        float FromFrac(double f) => sync ? ScopeTrace.BeatSteps[(int)Math.Round(Math.Clamp(f, 0, 1) * lastBeat)] : (float)ScopeTrace.KnobToMs(f);
        string Fmt(float v) => sync ? ScopeTrace.BeatsLabel(v) : ScopeTrace.MsLabel(v);
        float w0 = e.ModulatorGet(t, modId, wField);
        var wValue = MonoValue(Fmt(w0));
        // Full travel either way; double-click goes back to 1 s (or 4 beats).
        var wKnob = new Knob(ToFrac(w0))
        {
            Accent = true, Width = 30, Height = 30,
            Default = sync ? ToFrac((float)ScopeTrace.DefaultWindowBeats) : ScopeTrace.MsToKnob(ScopeTrace.DefaultWindowMs),
        };
        wKnob.Format = f => Fmt(FromFrac(f));
        wKnob.ValueChanged += f => { float v = FromFrac(f); e.ModulatorSet(t, modId, wField, v); wValue.Text = Fmt(v); };
        ValueEntry.Attach(wKnob, "Window", wValue);
        var syncChip = ToggleLabel(sync ? "Sync" : "ms", sync ? "The window is in beats at the tempo. Click for milliseconds"
                                                             : "The window is in milliseconds. Click to sync it to the beat",
            () => { e.ModulatorSet(t, modId, ScopeTrace.FieldSync, sync ? 0f : 1f); Changed?.Invoke(); refresh(); });
        var windowCell = new StackPanel
        {
            Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Children = { syncChip, wKnob, wValue },
        };

        // Zoom: 1× / 2× / 4× / 8× on a stepped knob.
        int last = ScopeTrace.Zooms.Length - 1;
        int ZoomAt(double f) => ScopeTrace.Zooms[(int)Math.Round(Math.Clamp(f, 0, 1) * last)];
        float z0 = e.ModulatorGet(t, modId, ScopeTrace.FieldZoom);
        var zValue = MonoValue(ScopeTrace.ZoomLabel(ScopeTrace.Zooms[ScopeTrace.ZoomIndex(z0)]));
        var zKnob = new Knob(ScopeTrace.ZoomIndex(z0) / (double)last, 0) { Accent = true };
        zKnob.Format = f => ScopeTrace.ZoomLabel(ZoomAt(f));
        zKnob.ValueChanged += f => { int z = ZoomAt(f); e.ModulatorSet(t, modId, ScopeTrace.FieldZoom, z); zValue.Text = ScopeTrace.ZoomLabel(z); };
        ValueEntry.Attach(zKnob, "Zoom", zValue);
        var zoomCell = KnobCell("Zoom", zKnob, zValue);

        addTick(() =>
        {
            // Another copy (node ↔ window) switched a chip: rebuild to match.
            if ((e.ModulatorGet(t, modId, ScopeTrace.FieldSync) > 0.5f) != sync
                || (e.ModulatorGet(t, modId, ScopeTrace.FieldTrigger) > 0.5f) != trig
                || ScopeFrozen(t, modId) != frozen) { refresh(); return; }
            if (!wKnob.Dragging)
            {
                float cur = e.ModulatorGet(t, modId, wField); double f = ToFrac(cur);
                if (Math.Abs(f - wKnob.Value) > 1e-3) { wKnob.Value = f; wValue.Text = Fmt(cur); }
            }
            if (!zKnob.Dragging)
            {
                float cur = e.ModulatorGet(t, modId, ScopeTrace.FieldZoom); double f = ScopeTrace.ZoomIndex(cur) / (double)last;
                if (Math.Abs(f - zKnob.Value) > 1e-3) { zKnob.Value = f; zValue.Text = ScopeTrace.ZoomLabel(ScopeTrace.Zooms[ScopeTrace.ZoomIndex(cur)]); }
            }
        });

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(9, 0), VerticalAlignment = VerticalAlignment.Center };
        void SetTrig(bool on) { e.ModulatorSet(t, modId, ScopeTrace.FieldTrigger, on ? 1f : 0f); Changed?.Invoke(); refresh(); }
        var free = SegChip("Free", !trig, () => SetTrig(false));
        ToolTip.SetTip(free, "Free running: the newest value is at the right");
        var rise = SegChip("Rise", trig, () => SetTrig(true));
        ToolTip.SetTip(rise, "Trigger: start the trace where the input rises through 0, so a repeating shape holds still");
        var freeze = SegChip("Freeze", frozen, () => { SetScopeFrozen(t, modId, !frozen); refresh(); });
        ToolTip.SetTip(freeze, frozen ? "Back to the live trace" : "Hold the trace while the input keeps running");
        freeze.Margin = new Thickness(8, 0, 0, 0);
        chips.Children.Add(free); chips.Children.Add(rise); chips.Children.Add(freeze);

        return new StackPanel
        {
            Margin = new Thickness(0, 4, 0, 0),
            Children =
            {
                KnobGrid(windowCell, zoomCell),
                new Border { Height = 34, Child = chips, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0) },
            },
        };
    }

    // Open (or bring forward) the Scope's own window.
    private void OpenScopeWindow(int t, int modId, Control anchor)
    {
        if (_scopeWindows.TryGetValue((t, modId), out var open)) { open.Activate(); return; }
        var w = new ScopeWindow(this, _engine, t, modId, $"Scope {modId} · {TrackName(t)}");
        _scopeWindows[(t, modId)] = w;
        w.Closed += (_, _) => _scopeWindows.Remove((t, modId));
        // Show once the press is over: shown inside it, the release lands on the main window
        // and macOS raises that over the new one.
        var owner = TopLevel.GetTopLevel(anchor) as Window;
        Dispatcher.UIThread.Post(() => { w.ShowOver(owner); w.Activate(); });
    }

    internal sealed class ScopeViz : Control
    {
        private readonly IAudioEngine _e;
        private readonly int _t, _mod;
        private readonly Func<ScopeSnapshot?> _held;
        private readonly float[] _v = new float[ScopeTrace.HistoryLength], _dt = new float[ScopeTrace.HistoryLength];
        private readonly double[] _xs = new double[ScopeTrace.HistoryLength];
        private readonly float[] _ys = new float[ScopeTrace.HistoryLength];
        private int _n, _zoom = 1;
        private bool _triggered;

        public ScopeViz(IAudioEngine e, int trackId, int modId, Func<ScopeSnapshot?> held)
        {
            _e = e; _t = trackId; _mod = modId; _held = held;
            IsHitTestVisible = false; ClipToBounds = true;
        }

        public void Update()
        {
            _zoom = ScopeTrace.Zooms[ScopeTrace.ZoomIndex(_e.ModulatorGet(_t, _mod, ScopeTrace.FieldZoom))];
            // Frozen: lay out the held history, so window, zoom and trigger still apply to it.
            var held = _held();
            float[] v = held?.Values ?? _v, dt = held?.Dts ?? _dt;
            int m = held?.Count ?? _e.ModulatorScopeTimed(_t, _mod, _v, _dt);
            double win = ScopeTrace.WindowSeconds(_e.ModulatorGet(_t, _mod, ScopeTrace.FieldSync) > 0.5f,
                _e.ModulatorGet(_t, _mod, ScopeTrace.FieldWindowMs), _e.ModulatorGet(_t, _mod, ScopeTrace.FieldWindowBeats), _e.Bpm);
            bool trig = _e.ModulatorGet(_t, _mod, ScopeTrace.FieldTrigger) > 0.5f;
            _n = ScopeTrace.Layout(v.AsSpan(0, m), dt.AsSpan(0, m), win, trig, _xs, _ys, out _triggered);
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h <= 0) return;
            ctx.DrawRectangle(ScopeBg, null, new Rect(0, 0, w, h), 4, 4);
            double mid = h / 2;
            var grid = new Pen(BorderInner, 1);
            for (int i = 1; i < 4; i++) { double x = Math.Round(w * i / 4) + 0.5; ctx.DrawLine(grid, new Point(x, 2), new Point(x, h - 2)); }
            ctx.DrawLine(grid, new Point(0, mid), new Point(w, mid));
            if (_triggered) ctx.DrawRectangle(Brass, null, new Rect(0, mid - 4, 2, 8));   // the trigger point
            if (_n < 2) return;
            double scale = _zoom / ScopeTrace.Range;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                for (int i = 0; i < _n; i++)
                {
                    double v = Math.Clamp(_ys[i] * scale, -1, 1);
                    var p = new Point(_xs[i] * w, mid - v * (mid - 2));
                    if (i == 0) g.BeginFigure(p, false); else g.LineTo(p);
                }
            }
            ctx.DrawGeometry(null, new Pen(Brass, 1.5), geo);
        }
    }
}
