// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What a hardware controller is on, drawn over the device cards and the mixer (specs/051).
// FR-017: the eight controls of the focused device's current page get their knob number and an
// orange underline, and the first of them carries the page ("2 / 3 · Filter"). FR-020: a control
// whose hardware knob or fader has not picked up yet shows where the hardware is — on a knob a
// hollow dot on its own arc (the knob's pointer shows the value), on a fader or slider a rail
// beside it with the hardware and the value — so it is clear which way to turn. specs/107: the
// focused device's card itself carries an orange top line and a "Controller" tag.
//
// Like the MIDI Learn glass it finds controls by their MidiLearn binding, so no card needs code
// of its own: a control a knob drives is one MIDI Learn could map.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

public sealed class ControllerFocusOverlay : Control
{
    private ControllerService? _service;
    private readonly DispatcherTimer _tick;   // follows cards that rebuild, scroll or reflow
    private readonly Dictionary<MidiTarget, int> _knobOf = new();
    private string _pageText = "";
    private DeviceAddress? _focusCard;   // the top-level card the knobs are on (specs/107)

    /// <summary>Which top-level device a Devices-panel card is (set by the chain on each card it
    /// builds): the overlay marks the focused one, so no card needs code of its own (specs/107 FR-004).</summary>
    public static readonly AttachedProperty<DeviceAddress?> CardAddressProperty =
        AvaloniaProperty.RegisterAttached<ControllerFocusOverlay, Control, DeviceAddress?>("CardAddress");
    public static void SetCardAddress(Control c, DeviceAddress? a) => c.SetValue(CardAddressProperty, a);
    public static DeviceAddress? GetCardAddress(Control c) => c.GetValue(CardAddressProperty);

    /// <summary>Where to look for bound controls (the Devices panel, the Mixer).</summary>
    public Func<IEnumerable<Visual?>>? Roots { get; set; }
    /// <summary>A target's value 0..1 (the pickup hint's orange tick).</summary>
    public Func<MidiTarget, double?>? ValueOf { get; set; }

    public ControllerFocusOverlay()
    {
        IsHitTestVisible = false;
        _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _tick.Tick += (_, _) => InvalidateVisual();
    }

    public ControllerService? Service
    {
        get => _service;
        set
        {
            if (_service is { } old && _subscribed) { old.FocusChanged -= Changed; old.PickupChanged -= Changed; old.ControllersChanged -= Changed; }
            _subscribed = false;
            _service = value;
            if (_service is { } s) { s.FocusChanged += Changed; s.PickupChanged += Changed; s.ControllersChanged += Changed; _subscribed = true; }
            Changed();
        }
    }

    // A floating window's overlay leaves with its window: no timer or service events after that.
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (_service is { } s && !_subscribed) { s.FocusChanged += Changed; s.PickupChanged += Changed; s.ControllersChanged += Changed; _subscribed = true; }
        Changed();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _tick.Stop();
        if (_service is { } s && _subscribed) { s.FocusChanged -= Changed; s.PickupChanged -= Changed; s.ControllersChanged -= Changed; _subscribed = false; }
    }

    private bool _subscribed;

    private void Changed()
    {
        _knobOf.Clear();
        _pageText = "";
        _focusCard = null;
        if (_service?.KnobFocus() is { } kf && _service.Focused is { } f)
        {
            var a = f.Address;
            _focusCard = new DeviceAddress(a.TrackId, a.Section, a.Section == FocusSection.Instrument ? -1 : a.Index);   // a rack's inner device: its rack's card
            for (int k = 0; k < kf.Knobs.Count; k++)
                if (kf.Knobs[k].Target is { } t) _knobOf.TryAdd(t, k);
            string name = f.PageName(kf.Page);
            _pageText = name.Length > 0
                ? Loc.F("controllers.page_named", kf.Page + 1, Math.Max(1, f.PageCount), name)
                : Loc.F("controllers.page", kf.Page + 1, Math.Max(1, f.PageCount));
        }
        bool any = (_knobOf.Count > 0 || _focusCard is not null || _service?.PickupGhosts.Count > 0) && this.IsAttachedToVisualTree();
        if (any) { if (!_tick.IsEnabled) _tick.Start(); }
        else if (_tick.IsEnabled) _tick.Stop();
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        if (_service is null || Roots is null) return;
        var ghosts = _service.PickupGhosts;
        if (_knobOf.Count == 0 && ghosts.Count == 0 && _focusCard is null) return;

        Rect? first = null;
        foreach (var root in Roots())
        {
            if (root is null || !root.IsEffectivelyVisible) continue;
            foreach (var v in root.GetVisualDescendants())
            {
                if (v is not Control c || !c.IsEffectivelyVisible || c.Bounds.Width <= 0) continue;
                if (_focusCard is not null && GetCardAddress(c) is { } ca && ca == _focusCard
                    && c.TranslatePoint(new Point(0, 0), this) is { } ctl)
                    DrawCardMark(ctx, new Rect(ctl, c.Bounds.Size));
                if (MidiLearn.GetBinding(c) is not { } b) continue;
                bool knob = _knobOf.TryGetValue(b.Target, out int k);
                bool ghost = ghosts.TryGetValue(b.Target, out double physical);
                if (!knob && !ghost) continue;
                if (c.TranslatePoint(new Point(0, 0), this) is not { } tl) continue;
                var r = new Rect(tl, c.Bounds.Size);
                // A knob draws a circle centred in a cell that may be wider or taller than it:
                // mark the circle, not the cell, or the number drifts towards the next knob.
                bool round = c is Knob or PanKnob;
                var dial = c switch
                {
                    Knob => Centred(r, Math.Min(r.Width, r.Height)),
                    PanKnob => Centred(r, Math.Min(Math.Min(r.Width, r.Height), 24)),
                    _ => r,
                };
                if (knob)
                {
                    DrawKnob(ctx, dial, k, round, underline: !ghost);
                    if (k == 0 || first is null) first = dial;   // the page sits on knob 1 (else the first found)
                }
                if (ghost)
                {
                    if (c is Knob) DrawArcGhost(ctx, dial, physical);
                    else DrawGhost(ctx, dial, physical, ValueOf?.Invoke(b.Target));
                }
            }
        }
        if (first is { } fr && _pageText.Length > 0) DrawPage(ctx, fr);
    }

    private static Rect Centred(Rect r, double d) => new(r.Center.X - d / 2, r.Center.Y - d / 2, d, d);

    // The number tucks into the dial's top-left corner — outside the circle, against it — so it
    // reads as that knob's; a fader or button gets it just left of its top. The underline gives
    // way to the pickup hint when one is shown.
    private void DrawKnob(DrawingContext ctx, Rect r, int knob, bool round, bool underline)
    {
        if (underline) ctx.DrawLine(new Pen(NotaPalette.Accent, 1.5), new Point(r.Left + 2, r.Bottom + 1), new Point(r.Right - 2, r.Bottom + 1));
        var ft = Text((knob + 1).ToString(CultureInfo.CurrentCulture), NotaPalette.AccentBright);
        double bw = ft.Width + 6, bh = ft.Height + 2;
        var box = round
            ? new Rect(Math.Max(0, r.Left + r.Width * 0.12 - bw), Math.Max(0, r.Top + r.Height * 0.12 - bh), bw, bh)
            : new Rect(Math.Max(0, r.Left - bw - 2), r.Top - 1, bw, bh);
        ctx.DrawRectangle(NotaPalette.Panel, new Pen(NotaPalette.AccentEdge, 1), box, 3, 3);
        ctx.DrawText(ft, new Point(box.Left + 3, box.Top + 1));
    }

    // The focused card (specs/107 FR-004): an orange line along its top edge and a small tag at its
    // right end — the orange-lines strength the knob numbers use, so it reads as "the knobs are here".
    private void DrawCardMark(DrawingContext ctx, Rect card)
    {
        ctx.DrawLine(new Pen(NotaPalette.Accent, 2), new Point(card.Left + 4, card.Top + 1), new Point(card.Right - 4, card.Top + 1));
        var ft = Text(Loc.T("controllers.card_tag"), NotaPalette.AccentBright);
        var box = new Rect(Math.Max(card.Left + 4, card.Right - ft.Width - 18), card.Top - ft.Height / 2 - 1, ft.Width + 10, ft.Height + 2);
        double rad = NotaSkin.MapRadius(3);   // the skin's roundness (DESIGN.md)
        ctx.DrawRectangle(NotaPalette.Panel, new Pen(NotaPalette.AccentEdge, 1), box, rad, rad);
        ctx.DrawText(ft, new Point(box.Left + 5, box.Top + 1));
    }

    private void DrawPage(DrawingContext ctx, Rect r)
    {
        // Above the row of knob numbers (they sit in the dials' top-left corners), so it covers none.
        var ft = Text(_pageText, NotaPalette.TextSecondary);
        double numbers = r.Top + r.Height * 0.12 - ft.Height - 2;
        double top = Math.Max(0, Math.Min(r.Top, numbers) - ft.Height - 7);
        var box = new Rect(r.Left - 1, top, ft.Width + 10, ft.Height + 4);
        ctx.DrawRectangle(NotaPalette.Panel, new Pen(NotaPalette.AccentEdge, 1), box, 3, 3);
        ctx.DrawText(ft, new Point(box.Left + 5, box.Top + 2));
    }

    // The pickup hint on a knob: a hollow dot on its arc where the hardware knob is. The knob's
    // own pointer is the value, so the gap between them says which way to turn.
    private static void DrawArcGhost(DrawingContext ctx, Rect dial, double physical)
    {
        double rr = dial.Width / 2 - 3;   // the knob's track radius
        double a = (Knob.StartDeg + Math.Clamp(physical, 0, 1) * Knob.SweepDeg) * Math.PI / 180.0;
        var p = new Point(dial.Center.X + Math.Cos(a) * rr, dial.Center.Y + Math.Sin(a) * rr);
        ctx.DrawEllipse(NotaPalette.Panel, new Pen(NotaPalette.TextPrimary, 1.5), p, 3, 3);
    }

    // The pickup hint elsewhere: a rail along the control (beside a vertical fader, under anything
    // else) with the value in orange and the hardware as a hollow dot.
    private static void DrawGhost(DrawingContext ctx, Rect r, double physical, double? value)
    {
        bool vertical = r.Height > r.Width * 1.5;
        Point At(double t) => vertical
            ? new Point(r.Right + 4, r.Bottom - 2 - Math.Clamp(t, 0, 1) * Math.Max(8, r.Height - 4))
            : new Point(r.Left + 2 + Math.Clamp(t, 0, 1) * Math.Max(8, r.Width - 4), r.Bottom + 4);
        ctx.DrawLine(new Pen(NotaPalette.TextTertiary, 1), At(0), At(1));
        if (value is { } v)
        {
            var vp = At(v);
            var d = vertical ? new Vector(3, 0) : new Vector(0, 3);
            ctx.DrawLine(new Pen(NotaPalette.Accent, 2), vp - d, vp + d);
        }
        ctx.DrawEllipse(NotaPalette.Panel, new Pen(NotaPalette.TextPrimary, 1.5), At(physical), 3, 3);
    }

    private FormattedText Text(string s, IBrush brush)
        => new NotaFormattedText(s, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, NotaFonts.SansMedium, NotaType.KnobLabel, brush);
}
