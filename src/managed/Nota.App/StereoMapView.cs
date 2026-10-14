// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Stereo Map (specs/008-stereo-map): the Mixer's second view. Every track, return and the
// master is a handle at (pan, fader) — the control — with a cloud for where its sound really
// is (measured balance on the pan scale), how wide (1 − correlation) and how loud, plus the
// master correlation. One custom-drawn control, refreshed on every animation frame while it is
// visible: one native call per frame brings all measurements. Dragging a handle is one undo
// step over pan and volume (specs/009), with the automation rules of the strip controls;
// a click selects the track everywhere. Layout, tokens and texts: specs/008 contracts.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

/// <summary>The Mixer fader's value ↔ position mapping (linear gain, 0 … <see cref="Max"/>),
/// shared by the strip fader and the Stereo Map so a handle and its fader always agree.</summary>
internal static class FaderScale
{
    public const double Max = 1.5;
    public static double Frac(double gain) => Math.Clamp(gain / Max, 0, 1);
    public static double Gain(double frac) => Math.Clamp(frac, 0, 1) * Max;
}

public sealed class StereoMapView : Control
{
    // Panel geometry (contract): header and bottom rows, the well's inset and the plot padding.
    private const double HeaderH = 36, BottomH = 32, Inset = 8, Radius = 6;
    private const double PadTop = 14, PadRight = 18, PadBottom = 22, PadLeft = 40;
    private const double HandleR = 8, HitSlop = 4, CloudH = 22, LabelMaxW = 84;
    private const double OffPanNote = 0.15;
    private const int TooltipDelayMs = 300;
    // "Negative" correlation for the red outline: below this, not at 0 — unrelated material
    // hovers around 0 and would flicker red on every block.
    private const float PhaseAlarm = -0.05f;

    private readonly IAudioEngine _engine;

    /// <summary>The app's selected track id (the ring follows it).</summary>
    public Func<int>? SelectedTrackId { get; set; }
    /// <summary>Select a track everywhere (arrangement, device panel).</summary>
    public Action<int>? SelectTrack { get; set; }

    private struct Item
    {
        public int Id;
        public string Name;
        public IBrush Color;
        public float Pan, Volume;
        public bool Muted, Dimmed, Master;
        public StereoMeasure M;
        public bool Has;
        public double Level => Has ? M.LevelDb : -200;
    }

    private readonly List<Item> _items = new();
    private readonly List<int> _order = new();          // draw order: indices into _items
    private StereoMeasure[] _measures = new StereoMeasure[64];
    private readonly Dictionary<int, int> _measureAt = new();
    private readonly Dictionary<int, (string name, IBrush color)> _looks = new();
    private readonly Dictionary<int, (string text, IBrush brush, FormattedText ft)> _labels = new();
    private readonly List<Rect> _placedLabels = new();   // this frame's label boxes (no two overlap)
    // The last tooltip drawn: its rows and note titles (read by the UI checks).
    private readonly List<(string key, string value)> _tooltipRows = new();
    private readonly List<string> _tooltipNotes = new();
    private int _frame;
    private bool _frameRequested;

    // Interaction state.
    private int _dragId = -1;
    private MixGesture? _drag;
    private Point _dragStart;
    private float _dragPan0, _dragVol0;
    private bool _dragMoved;
    private int _hoverId = -1;
    private long _hoverSince;

    public StereoMapView(IAudioEngine engine)
    {
        _engine = engine;
        ClipToBounds = true;
        Focusable = false;
    }

    // ---- frame loop ---------------------------------------------------------------------

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        RequestFrame();
    }

    private void RequestFrame()
    {
        if (_frameRequested) return;
        if (TopLevel.GetTopLevel(this) is not { } top) return;
        _frameRequested = true;
        top.RequestAnimationFrame(_ =>
        {
            _frameRequested = false;
            if (TopLevel.GetTopLevel(this) is null) return;   // detached: the loop ends
            if (IsEffectivelyVisible) { Pull(); InvalidateVisual(); }
            RequestFrame();
        });
    }

    /// <summary>Re-reads tracks and measurements now (the frame loop does this every frame).</summary>
    public void Pull()
    {
        _frame++;
        bool refreshLooks = _frame % 30 == 1;   // names / colours: twice a second is plenty
        _items.Clear();
        int n = _engine.TrackCount;
        bool anySolo = false;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var t) && !t.IsGroup && t.Soloed != 0) { anySolo = true; break; }

        int rows = _engine.GetStereoMeasures(_measures);
        if (rows > _measures.Length) { _measures = new StereoMeasure[rows + 16]; rows = _engine.GetStereoMeasures(_measures); }
        _measureAt.Clear();
        for (int i = 0; i < Math.Min(rows, _measures.Length); i++) _measureAt[_measures[i].TrackId] = i;

        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var t) || t.IsGroup) continue;
            if (refreshLooks || !_looks.ContainsKey(t.Id))
                _looks[t.Id] = (TrackNames.Of(_engine, t), ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(_engine, t.Id)));
            var look = _looks[t.Id];
            var item = new Item
            {
                Id = t.Id, Name = look.name, Color = look.color, Pan = t.Pan, Volume = t.Volume,
                Muted = t.Muted != 0, Dimmed = anySolo && t.Soloed == 0 && !t.IsReturn,
            };
            if (_measureAt.TryGetValue(t.Id, out int mi)) { item.M = _measures[mi]; item.Has = true; }
            _items.Add(item);
        }
        int masterId = _engine.MasterTrackId;
        var master = new Item
        {
            Id = masterId, Name = _engine.GetTrackName(masterId) is { Length: > 0 } mn ? mn : "Master",
            Color = NotaPalette.Accent, Pan = 0, Volume = _engine.MasterVolume, Master = true,
        };
        if (_measureAt.TryGetValue(masterId, out int mm)) { master.M = _measures[mm]; master.Has = true; }
        _items.Add(master);

        // Draw order: the master first (under everything — it shares C / 0 dB with every new
        // track, and only tracks can be dragged), then quiet to loud, the selected handle last
        // (on top). Hit-testing walks it backwards, so the master is picked only when alone.
        int sel = SelectedTrackId?.Invoke() ?? -1;
        _order.Clear();
        for (int i = 0; i < _items.Count; i++) _order.Add(i);
        _order.Sort((a, b) =>
        {
            bool ma = _items[a].Master, mb = _items[b].Master;
            if (ma != mb) return ma ? -1 : 1;
            bool sa = _items[a].Id == sel, sb = _items[b].Id == sel;
            if (sa != sb) return sa ? 1 : -1;
            return _items[a].Level.CompareTo(_items[b].Level);
        });
    }

    // ---- geometry -----------------------------------------------------------------------

    private Rect Plot()
    {
        var b = Bounds;
        double left = Inset + PadLeft, top = HeaderH + Inset + PadTop;
        double right = b.Width - Inset - PadRight, bottom = b.Height - BottomH - Inset - PadBottom;
        return new Rect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    private static double PanX(Rect p, double pan) => p.Left + (Math.Clamp(pan, -1, 1) + 1) / 2 * p.Width;
    private static double VolY(Rect p, double gain) => p.Bottom - FaderScale.Frac(gain) * p.Height;
    private static Point HandleAt(Rect p, in Item it) => new(PanX(p, it.Pan), VolY(p, it.Volume));

    // ---- rendering ----------------------------------------------------------------------

    public override void Render(DrawingContext ctx)
    {
        var b = Bounds;
        var plot = Plot();
        // Panel, header, well.
        ctx.DrawRectangle(NotaPalette.SurfaceCard, new Pen(NotaPalette.BorderDefault, 1), new Rect(0.5, 0.5, b.Width - 1, b.Height - 1), Radius, Radius);
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, HeaderH), new Point(b.Width, HeaderH));
        DrawText(ctx, "STEREO MAP", NotaFonts.SansBold, 10, NotaPalette.TextPrimary, new Point(12, HeaderH / 2), vCenter: true, tracking: true);
        var well = new Rect(Inset, HeaderH + Inset, b.Width - 2 * Inset, b.Height - HeaderH - BottomH - 2 * Inset);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), well, 4, 4);
        DrawGrid(ctx, plot);

        // Clouds, then handles and labels, in draw order.
        using (ctx.PushClip(well))
        {
            foreach (int i in _order) DrawCloud(ctx, plot, _items[i]);
            int sel = SelectedTrackId?.Invoke() ?? -1;
            foreach (int i in _order) DrawHandle(ctx, plot, _items[i], _items[i].Id == sel);
            DrawLabels(ctx, plot, sel);
        }
        DrawBottom(ctx, b);
        DrawTooltip(ctx, plot, b);
    }

    private void DrawGrid(DrawingContext ctx, Rect p)
    {
        var strong = new Pen(NotaPalette.BorderDefault, 1);
        var weak = new Pen(NotaPalette.GridBeat, 1);
        foreach (var (pan, label) in new[] { (-1.0, "L100"), (-0.5, "L50"), (0.0, "C"), (0.5, "R50"), (1.0, "R100") })
        {
            double x = Math.Round(PanX(p, pan)) + 0.5;
            ctx.DrawLine(pan == 0 ? strong : weak, new Point(x, p.Top), new Point(x, p.Bottom));
            DrawText(ctx, label, NotaFonts.Mono, 8, NotaPalette.TextTertiary, new Point(x, p.Bottom + 6), hCenter: true);
        }
        foreach (var (db, label) in new[] { (20 * Math.Log10(FaderScale.Max), "+3.5"), (0.0, "0 dB"), (-6.0, "−6"), (-12.0, "−12"), (-24.0, "−24") })
        {
            double y = Math.Round(VolY(p, Math.Pow(10, db / 20))) + 0.5;
            ctx.DrawLine(db == 0 ? strong : weak, new Point(p.Left, y), new Point(p.Right, y));
            DrawText(ctx, label, NotaFonts.Mono, 8, NotaPalette.TextTertiary, new Point(p.Left - 6, y), vCenter: true, rightAlign: true);
        }
    }

    private static void DrawCloud(DrawingContext ctx, Rect p, in Item it)
    {
        if (!it.Has || it.M.Silent || it.Muted) return;
        if (it.Color is not SolidColorBrush tint) return;
        double width = Math.Max(0.016 * p.Width, it.M.Width * p.Width);   // ±width on the pan scale = width × plot width
        double cx = PanX(p, it.M.Balance), cy = VolY(p, it.Volume);
        double opacity = 0.10 + 0.32 * Math.Clamp((it.M.LevelDb + 60) / 54, 0, 1);
        if (it.Dimmed) opacity *= 0.35;
        byte alpha = (byte)(Math.Round(opacity * 255 / 8) * 8);         // quantized: a small Wash cache
        var outline = it.M.Correlation < PhaseAlarm ? NotaPalette.Danger : tint;
        ctx.DrawEllipse(NotaPalette.Wash(tint, alpha), new Pen(outline, 1), new Point(cx, cy), width / 2, CloudH / 2);
    }

    private static void DrawHandle(DrawingContext ctx, Rect p, in Item it, bool selected)
    {
        var c = HandleAt(p, it);
        bool dim = it.Muted || it.Dimmed;
        if (selected) ctx.DrawEllipse(null, new Pen(NotaPalette.Accent, 2), c, 11, 11);
        var ring = it.Has && !it.M.Silent && !dim && it.M.Correlation < PhaseAlarm ? NotaPalette.Danger : NotaPalette.BgSunken;
        ctx.DrawEllipse(dim ? NotaPalette.SurfaceHover : it.Color, new Pen(ring, 2), c, HandleR - 1, HandleR - 1);
    }

    private void DrawLabels(DrawingContext ctx, Rect p, int sel)
    {
        // Greedy: from the top of the draw order down, skip a label that would overlap one placed.
        var placed = _placedLabels;
        placed.Clear();
        for (int k = _order.Count - 1; k >= 0; k--)
        {
            var it = _items[_order[k]];
            var brush = it.Muted || it.Dimmed ? NotaPalette.TextDisabled : it.Id == sel ? NotaPalette.TextPrimary : NotaPalette.TextSecondary;
            var ft = Label(it.Id, it.Name, brush);
            var c = HandleAt(p, it);
            var r = new Rect(c.X + 13, c.Y - 7, Math.Min(LabelMaxW, ft.Width), ft.Height);
            bool hit = false;
            foreach (var q in placed) if (q.Intersects(r)) { hit = true; break; }
            if (hit) continue;
            placed.Add(r);
            using (ctx.PushClip(r)) ctx.DrawText(ft, r.TopLeft);
        }
    }

    private FormattedText Label(int id, string name, IBrush brush)
    {
        if (_labels.TryGetValue(id, out var l) && l.text == name && ReferenceEquals(l.brush, brush)) return l.ft;
        var ft = new FormattedText(name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansMedium, 10, brush)
        {
            MaxTextWidth = LabelMaxW, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis,
        };
        _labels[id] = (name, brush, ft);
        return ft;
    }

    private void DrawBottom(DrawingContext ctx, Rect b)
    {
        double y0 = b.Height - BottomH, cy = y0 + BottomH / 2;
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, y0), new Point(b.Width, y0));
        double x = 10;
        x += DrawText(ctx, "MASTER CORR", NotaFonts.SansBold, 8, NotaPalette.TextTertiary, new Point(x, cy), vCenter: true, tracking: true) + 10;
        x += DrawText(ctx, "−1", NotaFonts.Mono, 8, NotaPalette.TextTertiary, new Point(x, cy), vCenter: true) + 6;
        var bar = new Rect(x, cy - 3, 180, 6);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), bar, 2, 2);
        double mid = bar.Left + bar.Width / 2;
        float corr = 1f;
        foreach (var it in _items) if (it.Master && it.Has && !it.M.Silent) corr = it.M.Correlation;
        double w = Math.Abs(corr) * bar.Width / 2;
        var fill = corr < PhaseAlarm ? NotaPalette.Danger : NotaPalette.Success;
        ctx.DrawRectangle(fill, null, new Rect(corr < 0 ? mid - w : mid, bar.Top + 1, w, bar.Height - 2));
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1), new Point(mid, bar.Top - 2), new Point(mid, bar.Bottom + 2));
        x = bar.Right + 6;
        x += DrawText(ctx, "+1", NotaFonts.Mono, 8, NotaPalette.TextTertiary, new Point(x, cy), vCenter: true) + 10;
        DrawText(ctx, Signed(corr, 2), NotaFonts.Mono, 10, corr < PhaseAlarm ? NotaPalette.Danger : NotaPalette.TextSecondary, new Point(x, cy), vCenter: true);
        DrawText(ctx, "Drag: pan + volume · Shift locks axis · double-click centres pan · ⌥ double-click 0 dB",
                 NotaFonts.Sans, 10, NotaPalette.TextTertiary, new Point(b.Width - 10, cy), vCenter: true, rightAlign: true);
    }

    private void DrawTooltip(DrawingContext ctx, Rect p, Rect b)
    {
        if (_dragId >= 0 || _hoverId < 0 || Environment.TickCount64 - _hoverSince < TooltipDelayMs) return;
        int idx = _items.FindIndex(x => x.Id == _hoverId);
        if (idx < 0) return;
        var it = _items[idx];
        var rows = new List<(string k, string v, IBrush ink)>
        {
            ("PAN", it.Master ? "—" : PanText(it.Pan), NotaPalette.TextPrimary),
            ("VOLUME", DbText(it.Volume), NotaPalette.TextPrimary),
        };
        if (it.Has)
        {
            rows.Add(("BALANCE", Signed(it.M.Balance, 2), NotaPalette.TextSecondary));
            rows.Add(("WIDTH", it.M.Width.ToString("0.00", NotaNum.Culture), NotaPalette.TextSecondary));
            rows.Add(("CORRELATION", Signed(it.M.Correlation, 2), it.M.Correlation < PhaseAlarm ? NotaPalette.Danger : NotaPalette.TextSecondary));
            rows.Add(("LEVEL", it.M.Silent ? "below −60 dBFS" : Signed(it.M.LevelDb, 1) + NotaNum.Thin + "dBFS", NotaPalette.TextSecondary));
        }
        var notes = new List<(string title, string body, IBrush ink)>();
        if (it.Has && !it.M.Silent && !it.Muted && !it.Master && Math.Abs(it.M.Balance - it.Pan) > OffPanNote)
            notes.Add(("Image is off the pan", $"The pan is at {PanText(it.Pan)}, but the signal measures {PanText(it.M.Balance)}. The source itself is unbalanced.", NotaPalette.Accent));
        if (it.Muted) notes.Add(("Muted", "No cloud while the track is muted.", NotaPalette.TextSecondary));
        else if (!it.Has || it.M.Silent) notes.Add(("Silent", "Below −60 dBFS, so the cloud is hidden.", NotaPalette.TextSecondary));

        _tooltipRows.Clear();
        foreach (var (k, v, _) in rows) _tooltipRows.Add((k, v));
        _tooltipNotes.Clear();
        foreach (var n in notes) _tooltipNotes.Add(n.title);

        const double W = 188, Pad = 10, RowH = 13;
        var bodies = new List<FormattedText>();
        double h = 8 + 16 + rows.Count * RowH;
        foreach (var n in notes)
        {
            var ft = new FormattedText(n.body, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, 9, NotaPalette.TextTertiary) { MaxTextWidth = W - 2 * Pad };
            bodies.Add(ft);
            h += 6 + 13 + ft.Height;
        }
        h += 8;
        var c = HandleAt(p, it);
        double x = c.X + 16 + W > b.Width ? c.X - 16 - W : c.X + 16;
        double y = c.Y - 8 + h > b.Height ? c.Y + 8 - h : c.Y - 8;
        var r = new Rect(x, Math.Max(0, y), W, h);
        ctx.DrawRectangle(NotaPalette.TooltipBg, new Pen(NotaPalette.BorderStrong, 1), r, 4, 4);
        double ty = r.Top + 8;
        ctx.DrawEllipse(it.Color, null, new Point(r.Left + Pad + 4, ty + 7), 4, 4);
        DrawText(ctx, it.Name, NotaFonts.SansSemiBold, 11, NotaPalette.TextPrimary, new Point(r.Left + Pad + 14, ty + 7), vCenter: true);
        ty += 18;
        foreach (var (k, v, ink) in rows)
        {
            DrawText(ctx, k, NotaFonts.SansBold, 7, NotaPalette.TextTertiary, new Point(r.Left + Pad, ty + 6), vCenter: true, tracking: true);
            DrawText(ctx, v, NotaFonts.Mono, 9, ink, new Point(r.Left + Pad + 62, ty + 6), vCenter: true);
            ty += RowH;
        }
        for (int i = 0; i < notes.Count; i++)
        {
            ty += 4;
            ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(r.Left + Pad, ty), new Point(r.Right - Pad, ty));
            ty += 4;
            DrawText(ctx, notes[i].title, NotaFonts.SansSemiBold, 9, notes[i].ink, new Point(r.Left + Pad, ty + 6), vCenter: true);
            ty += 13;
            ctx.DrawText(bodies[i], new Point(r.Left + Pad, ty));
            ty += bodies[i].Height;
        }
    }

    // Draws `text` anchored at `at`; returns its width.
    private static double DrawText(DrawingContext ctx, string text, Typeface face, double size, IBrush brush, Point at,
        bool vCenter = false, bool hCenter = false, bool rightAlign = false, bool tracking = false)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush);
        double x = hCenter ? at.X - ft.Width / 2 : rightAlign ? at.X - ft.Width : at.X;
        double y = vCenter ? at.Y - ft.Height / 2 : at.Y;
        ctx.DrawText(ft, new Point(x, y));
        return ft.Width;
    }

    // ---- input --------------------------------------------------------------------------

    private int HitTest(Point pos)
    {
        var p = Plot();
        for (int k = _order.Count - 1; k >= 0; k--)   // topmost first
        {
            var it = _items[_order[k]];
            var c = HandleAt(p, it);
            double dx = pos.X - c.X, dy = pos.Y - c.Y;
            if (dx * dx + dy * dy <= (HandleR + HitSlop) * (HandleR + HitSlop)) return _order[k];
        }
        return -1;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int idx = HitTest(e.GetPosition(this));
        if (idx < 0) return;
        var it = _items[idx];
        e.Handled = true;
        if (it.Master) { SelectTrack?.Invoke(it.Id); return; }   // fixed: select only
        int id = it.Id;
        if (e.ClickCount == 2)   // reset: pan to C, ⌥ volume to 0 dB — one step each
        {
            if ((e.KeyModifiers & KeyModifiers.Alt) != 0) MixEdit.Step(_engine, () => _engine.SetTrackVolume(id, 1f), MixTarget.Volume(id));
            else MixEdit.Step(_engine, () => _engine.SetTrackPan(id, 0f), MixTarget.Pan(id));
            return;
        }
        _dragId = id;
        _dragStart = e.GetPosition(this);
        _dragPan0 = it.Pan;
        _dragVol0 = it.Volume;
        _dragMoved = false;
        // One undo step for the drag; opened before the automation writes so a recorded lane
        // and the values undo together (specs/009), closed after them.
        _drag = new MixGesture(_engine, MixTarget.Volume(id), MixTarget.Pan(id));
        _drag.Begin();
        _engine.BeginAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        _engine.BeginAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
        e.Pointer.Capture(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetPosition(this);
        if (_dragId < 0)
        {
            int idx = HitTest(pos);
            int id = idx >= 0 ? _items[idx].Id : -1;
            if (id != _hoverId) { _hoverId = id; _hoverSince = Environment.TickCount64; }
            Cursor = idx < 0 ? Cursor.Default : new Cursor(_items[idx].Master ? StandardCursorType.Hand : StandardCursorType.SizeAll);
            return;
        }
        var p = Plot();
        double dx = pos.X - _dragStart.X, dy = pos.Y - _dragStart.Y;
        if (!_dragMoved && Math.Abs(dx) + Math.Abs(dy) < 3) return;
        _dragMoved = true;
        if ((e.KeyModifiers & KeyModifiers.Shift) != 0)   // lock to the axis with the larger movement
        {
            if (Math.Abs(dx) * 2 / p.Width > Math.Abs(dy) / p.Height) dy = 0; else dx = 0;
        }
        float pan = (float)Math.Clamp(_dragPan0 + dx * 2 / p.Width, -1, 1);
        float vol = (float)FaderScale.Gain(FaderScale.Frac(_dragVol0) - dy / p.Height);
        _engine.SetTrackPan(_dragId, pan);
        _engine.SetTrackVolume(_dragId, vol);
        Pull();
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragId < 0) return;
        int id = _dragId;
        _engine.EndAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        _engine.EndAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
        _drag?.End();
        _drag = null;
        _dragId = -1;
        e.Pointer.Capture(null);
        if (!_dragMoved) SelectTrack?.Invoke(id);   // a click without a move selects
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hoverId = -1;
    }

    // ---- formatting ---------------------------------------------------------------------

    private static string PanText(double pan)
    {
        int n = (int)Math.Round(pan * 100);
        return n == 0 ? "C" : (n < 0 ? "L" : "R") + Math.Abs(n).ToString(NotaNum.Culture);
    }

    private static string DbText(double gain)
        => gain <= 1e-5 ? "−∞" : Signed(20 * Math.Log10(gain), 1) + " dB";

    private static string Signed(double v, int decimals)
    {
        double r = Math.Round(v, decimals);
        string s = Math.Abs(r).ToString("F" + decimals, NotaNum.Culture);
        return r < 0 ? "−" + s : r > 0 ? "+" + s : s;
    }
}
