// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Typing an exact value into any parameter control (HANDOFF §4: "all numerics editable by
// drag (vertical) and double-click-type"; specs/035-typed-value-entry). One gesture scheme
// for every knob, fader, slider, graph handle and matrix cell:
//
//   double-click   on the control restores the default (or clears a route)
//   ⌥/Alt-click    (and a double-click on the read-out) turns the read-out itself editable —
//                  a borderless text box in its place (an adorner, so it works whatever panel
//                  the read-out sits in) with a 1px accent line under it and the number
//                  selected; Enter or a click away commits, Escape cancels, Tab / Shift+Tab
//                  commit and move to the next / previous value on the same card (the nearest
//                  Scope), wrapping
//   right-click    a parameter menu — Type value…, Reset to default / Clear — unless the
//                  control has its own menu, which then adds MenuItems itself
//
// The pointer handlers run in the tunnel phase, so the control's own press handling (a drag)
// never sees these clicks. A control hands over a ValueField (one value) or is an
// IValueEntryHost (several handles, asked for the fields under the pointer; null lets the
// event through). The typed text is read by ValueText against the field's own read-out,
// and a commit is one Begin / Set / End — one undo step and one automation gesture, like a
// drag. Return outside the box keeps its transport meaning (Stop), so a control does not
// open entry on Enter.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace Nota.App;

internal static partial class ValueEntry
{
    [System.Text.RegularExpressions.GeneratedRegex(@"[+\-−]?(?:\d+(?:[.,]\d*)?|[.,]\d+|∞)")]
    private static partial System.Text.RegularExpressions.Regex Number();

    // ---- attaching -----------------------------------------------------------------------

    // What Tab needs to find a control's values again: the fields and where each one's box goes.
    private sealed class Entry(Control owner, Func<IReadOnlyList<ValueField>> fields, Func<ValueField, Anchor> anchor)
    {
        public readonly WeakReference<Control> Owner = new(owner);
        public readonly Func<IReadOnlyList<ValueField>> Fields = fields;
        public readonly Func<ValueField, Anchor> AnchorOf = anchor;
    }

    /// <summary>Where a box sits: a rectangle in <c>Visual</c>'s coordinates; <c>Font</c> is the read-out
    /// it replaces (its font and alignment), null over a control without one.</summary>
    private readonly record struct Anchor(Visual Visual, Rect Rect, TextBlock? Font);

    private static readonly ConditionalWeakTable<Control, Entry> Entries = new();

    /// <summary>Marks a Tab-chain root: a device card, a mixer strip, an editor panel.</summary>
    public static readonly AttachedProperty<bool> ScopeProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("Scope", typeof(ValueEntry));
    public static bool GetScope(Control c) => c.GetValue(ScopeProperty);
    public static void SetScope(Control c, bool v) => c.SetValue(ScopeProperty, v);

    /// <summary>Make <paramref name="control"/> typeable (and its <paramref name="readout"/>, where the
    /// box then sits). <paramref name="menu"/> false: the control shows its own right-click menu
    /// and adds <see cref="MenuItems"/> to it.</summary>
    public static void Attach(Control control, ValueField field, TextBlock? readout = null, bool menu = true)
    {
        Anchor AnchorOf(ValueField _) => readout is { IsEffectivelyVisible: true }
            ? new Anchor(readout, new Rect(readout.Bounds.Size), readout)
            : new Anchor(control, new Rect(control.Bounds.Size), null);
        var fields = new[] { field };
        if (control is TextBlock tb) tb.Background ??= Brushes.Transparent;
        Entries.AddOrUpdate(control, new Entry(control, () => fields, AnchorOf));
        void Press(object? s, PointerPressedEventArgs e)
        {
            bool onReadout = control is TextBlock || readout is not null && ReferenceEquals(s, readout);
            if (Pressed(e, s as Visual ?? control, field, onReadout: onReadout)) Open(control, field, AnchorOf(field));
        }
        control.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        if (readout is not null && !ReferenceEquals(readout, control))
        {
            readout.Background ??= Brushes.Transparent;   // the whole column takes the click, not just the glyphs
            readout.AddHandler(InputElement.PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        }
        if (!menu) return;
        void Context(object? s, ContextRequestedEventArgs e)
        {
            if (e.Handled) return;
            e.Handled = true;
            ShowMenu(control, field, () => Open(control, field, AnchorOf(field)));
        }
        control.AddHandler(Control.ContextRequestedEvent, Context);
        if (readout is not null && !ReferenceEquals(readout, control)) readout.AddHandler(Control.ContextRequestedEvent, Context);
    }

    /// <summary>A knob and its value line: typed through its Format, or — without one — by reading
    /// <paramref name="readout"/> back after each probe. Nothing without either.</summary>
    public static void Attach(Knob knob, string name, TextBlock? readout = null, bool menu = true)
    {
        if ((knob.EntryField(name) ?? (readout is null ? null : knob.ProbingField(name, () => readout.Text ?? ""))) is { } f)
            Attach(knob, f, readout, menu);
    }

    /// <summary>A knob whose value line is refreshed elsewhere: <paramref name="text"/> formats the
    /// current value (read back after each probe when the knob has no Format).</summary>
    public static void Attach(Knob knob, string name, TextBlock readout, Func<string> text, bool menu = true)
        => Attach(knob, knob.EntryField(name) ?? knob.ProbingField(name, text), readout, menu);

    /// <summary>Make the handles of <paramref name="host"/> typeable.</summary>
    public static void AttachHost<T>(T host, bool menu = true) where T : Control, IValueEntryHost
    {
        Anchor AnchorOf(ValueField f) => new(host, host.AnchorOf(f), null);
        Entries.AddOrUpdate(host, new Entry(host, () => host.AllFields, AnchorOf));
        host.AddHandler(InputElement.PointerPressedEvent, (_, e) =>
        {
            if (host.FieldsAt(e.GetPosition(host)) is not { Count: > 0 } fs) return;
            if (Pressed(e, host, fs[0], fs)) Open(host, fs[0], AnchorOf(fs[0]));
        }, RoutingStrategies.Tunnel);
        if (!menu) return;
        host.AddHandler(Control.ContextRequestedEvent, (_, e) =>
        {
            if (e.Handled || !e.TryGetPosition(host, out var p) || host.FieldsAt(p) is not { Count: > 0 } fs) return;
            e.Handled = true;
            ShowMenu(host, fs[0], () => Open(host, fs[0], AnchorOf(fs[0])), fs);
        });
    }

    /// <summary>A slider row's read-out, typed by probing the row's own setter (<paramref name="setNorm"/>
    /// then <paramref name="text"/>). <paramref name="control"/> (its slider) takes the same gestures;
    /// <paramref name="reset"/> — by default the slider's own Reset — is what a double-click and the menu run.</summary>
    public static void Attach(TextBlock readout, Func<double> norm, Action<double> setNorm, Func<string> text,
        Action? begin = null, Action? end = null, Action? after = null, Action? reset = null, Control? control = null, string name = "Value")
        => Attach(control ?? readout, Probing(name, norm, setNorm, text, begin, end, after, reset ?? (control as SliderTrack)?.Reset), readout);

    /// <summary>A field typed by probing <paramref name="setNorm"/> + <paramref name="text"/>.</summary>
    public static ValueField Probing(string name, Func<double> norm, Action<double> setNorm, Func<string> text,
        Action? begin = null, Action? end = null, Action? after = null, Action? reset = null)
        => new(name, norm, n => { setNorm(n); return text(); }, setNorm, begin, end)
        { Probes = true, After = after, Reset = reset };

    // A left press: ⌥ opens the box; a double-click resets (every field of the handle) — or,
    // on the read-out, opens the box too.
    private static bool Pressed(PointerPressedEventArgs e, Visual at, ValueField field, IReadOnlyList<ValueField>? all = null,
        bool onReadout = false)
    {
        if (!e.GetCurrentPoint(at).Properties.IsLeftButtonPressed) return false;
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0 || onReadout && e.ClickCount == 2)
        {
            e.Handled = true;
            return true;
        }
        if (e.ClickCount != 2) return false;
        if (field.Clear is null && !(all ?? [field]).Any(f => f.CanReset)) return false;   // the control keeps its own double-click
        e.Handled = true;
        if (field.Clear is not null) Clear(field);
        else foreach (var f in all ?? [field]) Reset(f);
        return false;
    }

    // ---- reset / clear / commit ----------------------------------------------------------

    /// <summary>Back to the default as one gesture; nothing without a default or when already there.</summary>
    public static void Reset(ValueField f)
    {
        if (f.Reset is { } act) { f.Begin?.Invoke(); act(); f.End?.Invoke(); f.After?.Invoke(); return; }
        if (f.Default is not { } d) return;
        d = Math.Clamp(d, 0, 1);
        if (Math.Abs(f.Get() - d) < 1e-9) return;
        f.Begin?.Invoke(); f.Set(d); f.End?.Invoke(); f.After?.Invoke();
    }

    private static void Clear(ValueField f) { f.Clear?.Invoke(); f.After?.Invoke(); }

    private static bool AtDefault(ValueField f) => f.Reset is null && f.Default is { } d && Math.Abs(f.Get() - Math.Clamp(d, 0, 1)) < 1e-9;

    /// <summary>Apply typed text: the position whose read-out shows it, written as one gesture.
    /// Unreadable text or a value the read-out already shows changes nothing.</summary>
    internal static void Commit(ValueField f, string typed)
    {
        if (string.IsNullOrWhiteSpace(typed)) return;
        double before = f.Get();
        string was = f.TextAt(before);
        double? to = ValueText.Solve(typed, was, f.TextAt);
        string? now = to is { } n ? f.TextAt(n) : null;
        if (f.Probes) f.Set(before);   // the probes moved it: the gesture must open on the old value (undo, specs/016)
        if (to is null || now == was) { f.After?.Invoke(); return; }
        f.Begin?.Invoke();
        f.Set(to.Value);
        f.End?.Invoke();
        f.After?.Invoke();
    }

    // ---- the parameter menu --------------------------------------------------------------

    /// <summary>Type value… and Reset to default (Clear for a route), for a control that builds
    /// its own right-click menu.</summary>
    public static IEnumerable<Control> MenuItems(Control control, ValueField field)
    {
        Anchor a = Entries.TryGetValue(control, out var en) ? en.AnchorOf(field) : new Anchor(control, new Rect(control.Bounds.Size), null);
        return MenuItems(field, () => Open(control, field, a));
    }

    /// <summary>The same items for an attached control's (first) value; none when it is not attached.</summary>
    public static IEnumerable<Control> MenuItems(Control control)
        => Entries.TryGetValue(control, out var en) && en.Fields() is { Count: > 0 } fs ? MenuItems(control, fs[0]) : [];

    private static IEnumerable<Control> MenuItems(ValueField field, Action open, IReadOnlyList<ValueField>? all = null)
    {
        yield return MenuKit.Item("Type value…", GlyphKind.Edit, open);
        var fs = all ?? [field];
        if (field.Clear is not null) yield return MenuKit.Danger("Clear", GlyphKind.Trash, () => Clear(field));
        else yield return MenuKit.Item("Reset to default", GlyphKind.Cycle, () => { foreach (var f in fs) Reset(f); },
            enabled: fs.Any(f => f.CanReset && !AtDefault(f)));
    }

    private static void ShowMenu(Control control, ValueField field, Action open, IReadOnlyList<ValueField>? all = null)
    {
        var fly = new MenuFlyout();
        fly.Items.Add(MenuKit.Header(field.Name, null, "Parameter"));
        fly.Items.Add(new Separator());
        foreach (var c in MenuItems(field, open, all)) fly.Items.Add(c);
        fly.ShowAt(control, showAtPointer: true);
    }

    // ---- the box -------------------------------------------------------------------------

    private static Action<bool>? _close;   // the open box (one at a time): close it, committing or not

    /// <summary>Open the box on <paramref name="field"/> of <paramref name="control"/> (the menu, Tab).</summary>
    public static void Open(Control control, ValueField field)
    {
        if (Entries.TryGetValue(control, out var en)) Open(control, field, en.AnchorOf(field));
    }

    private static void Open(Control owner, ValueField field, Anchor at)
    {
        _close?.Invoke(true);
        if (AdornerLayer.GetAdornerLayer(at.Visual) is null) return;
        TopLevel? top = TopLevel.GetTopLevel(at.Visual);
        var font = at.Font;
        Rect r = at.Rect;
        double fontSize = font is { FontSize: > 0 } ? font.FontSize : 9;
        // The value line itself turns editable (TextBox.value): same font, size and alignment
        // as the read-out, a 1px accent line under it, the read-out's own ink hidden meanwhile.
        // Without a read-out (a fader, a bar) a value line is laid over the control instead.
        var align = font is null ? HorizontalAlignment.Center
            : font.TextAlignment == TextAlignment.Center || font.HorizontalAlignment == HorizontalAlignment.Center && double.IsNaN(font.Width)
                ? HorizontalAlignment.Center
            : font.TextAlignment is TextAlignment.Right or TextAlignment.End ? HorizontalAlignment.Right
            : HorizontalAlignment.Left;
        double w = Math.Max(r.Width, font is null ? 40 : 28) + 2, h = font is null ? fontSize + 6 : r.Height + 2;
        double x = align switch
        {
            HorizontalAlignment.Right => r.Right + 1 - w,
            HorizontalAlignment.Left => r.X - 1,
            _ => r.X + (r.Width - w) / 2,
        };
        double y = font is null ? r.Y + (r.Height - h) / 2 : r.Y;
        // Keep the box inside the window.
        if (top is not null && at.Visual.TranslatePoint(new Point(x, y), top) is { } p)
        {
            var size = top.ClientSize;
            if (p.X < 2) x += 2 - p.X; else if (p.X + w > size.Width - 2) x -= p.X + w - (size.Width - 2);
            if (p.Y < 2) y += 2 - p.Y; else if (p.Y + h > size.Height - 2) y -= p.Y + h - (size.Height - 2);
        }
        string was = font?.Text is { Length: > 0 } t ? t : field.TextAt(field.Get());
        var box = new TextBox
        {
            Classes = { "value" },
            Text = was, FontFamily = font?.FontFamily ?? NotaFonts.MonoFamily, FontSize = fontSize,
            FontWeight = font?.FontWeight ?? FontWeight.Normal,
            Height = h, Width = w,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
            HorizontalContentAlignment = align,
            Margin = new Thickness(x, y, 0, 0),
        };
        if (font is null) box.Classes.Add("solid");
        IBrush? ink = font?.Foreground;
        if (font is not null) font.Foreground = Brushes.Transparent;   // the box draws the value now
        bool done = false;
        EventHandler<PointerPressedEventArgs>? outside = null;
        EventHandler<VisualTreeAttachmentEventArgs>? gone = null;
        void Close(bool apply)
        {
            if (done) return;
            done = true;
            if (_close == Close) _close = null;
            if (outside is not null) top?.RemoveHandler(InputElement.PointerPressedEvent, outside);
            at.Visual.DetachedFromVisualTree -= gone;
            AdornerLayer.SetAdorner(at.Visual, null);
            if (font is not null && ReferenceEquals(font.Foreground, Brushes.Transparent)) font.Foreground = ink;
            if (apply) Commit(field, box.Text ?? "");
        }
        _close = Close;
        box.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Return) { Close(true); e.Handled = true; }
            else if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
            else if (e.Key == Key.Tab)
            {
                e.Handled = true;
                Close(true);
                Step(owner, field, (e.KeyModifiers & KeyModifiers.Shift) != 0 ? -1 : 1);
            }
        }, RoutingStrategies.Tunnel);
        box.LostFocus += (_, _) => Close(true);
        gone = (_, _) => Close(false);
        at.Visual.DetachedFromVisualTree += gone;
        // A click on something that doesn't take focus (a canvas, a slider track) still ends the edit.
        outside = (_, e) => { if (e.Source is not Visual v || !box.IsVisualAncestorOf(v) && v != box) Close(true); };
        top?.AddHandler(InputElement.PointerPressedEvent, outside, RoutingStrategies.Tunnel, handledEventsToo: true);

        AdornerLayer.SetIsClipEnabled(box, false);   // it overhangs the read-out
        AdornerLayer.SetAdorner(at.Visual, box);
        Dispatcher.UIThread.Post(() =>
        {
            if (done) return;
            box.Focus();
            // Select the number and leave its unit: typing "12" into "10.0 ms" reads 12 ms.
            if (Number().Match(was) is { Success: true } m) { box.SelectionStart = m.Index; box.SelectionEnd = m.Index + m.Length; }
            else box.SelectAll();
        });
    }

    // ---- Tab -----------------------------------------------------------------------------

    // Every typeable value under the nearest Scope, in reading order: rows by top edge (within
    // half the smaller height), then left edge; a host's fields in its own order. Wraps.
    private static void Step(Control owner, ValueField from, int dir)
    {
        if (owner.GetVisualAncestors().OfType<Control>().FirstOrDefault(GetScope) is not { } scope) return;
        var items = new List<(Control owner, ValueField field, Rect at, int sub)>();
        foreach (var c in scope.GetVisualDescendants().OfType<Control>())
        {
            if (!Entries.TryGetValue(c, out var en) || !c.IsEffectivelyVisible || !c.IsEffectivelyEnabled) continue;
            if (c.TranslatePoint(default, scope) is not { } p) continue;
            var at = new Rect(p, c.Bounds.Size);
            var fs = en.Fields();
            for (int i = 0; i < fs.Count; i++) items.Add((c, fs[i], at, i));
        }
        if (items.Count == 0) return;
        var rows = new List<List<(Control owner, ValueField field, Rect at, int sub)>>();
        foreach (var it in items.OrderBy(i => i.at.Top))
        {
            var row = rows.LastOrDefault();
            if (row is not null && it.at.Top - row[0].at.Top <= Math.Min(row[0].at.Height, it.at.Height) / 2) row.Add(it);
            else rows.Add([it]);
        }
        var order = rows.SelectMany(r => r.OrderBy(i => i.at.Left).ThenBy(i => i.sub)).ToList();
        int k = order.FindIndex(i => i.owner == owner && ReferenceEquals(i.field, from));
        if (k < 0) k = order.FindIndex(i => i.owner == owner);
        var next = order[((k < 0 ? 0 : k) + dir + order.Count) % order.Count];
        next.owner.BringIntoView();
        Dispatcher.UIThread.Post(() => Open(next.owner, next.field), DispatcherPriority.Background);
    }
}
