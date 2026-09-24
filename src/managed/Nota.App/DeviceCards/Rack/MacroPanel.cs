// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the kit macros of the Drum Rack and Nota Rhythm, from the almanac's
// "1c Macro" cards. Eight knobs over the whole kit, as a 4 × 2 grid: the value in brass, the
// name, and in teal where it goes ("→ Reverb · Snare, Clap"). An unassigned macro is a dashed,
// empty cell. The selected macro's receivers — every mapping, with its range as a teal band and
// the value it sits at now as a brass tick — are listed by MacroDetail (the Drum Rack's right
// panel); the band's ends drag.
//
// Both instruments reach this through IMacroSurface: the rack's macros (RackCore) and the
// Rhythm's own. A mapping is a RackMacroMapping whose Chain is the pad chain or the voice.
// Map offers every target a slot has — a pad's own controls or a voice's params, the pad
// instrument's params, and each of its effects' params — so a macro can drive the kit's
// built-in effects directly.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

/// <summary>A kit instrument's macros, as the macro panel sees them.</summary>
internal interface IMacroSurface
{
    int TrackId { get; }
    /// <summary>The plugin-param device index and param index a macro rides (automation, MIDI learn).</summary>
    int AutomationDevice { get; }
    int MacroParamIndex(int macro);
    string MacroName(int macro);
    void SetMacroName(int macro, string name);
    /// <summary>What a slot is called in the plural ("pads", "voices").</summary>
    string SlotNoun { get; }
    /// <summary>The slots a macro can reach (pad chains / voices), in display order.</summary>
    IReadOnlyList<int> Slots();
    string SlotName(int slot);
    IBrush SlotHue(int slot);
    int MappingCount();
    bool TryGetMapping(int index, out RackMacroMapping m);
    int AddMapping(int macro, int slot, int device, int param, float lo, float hi);
    bool RemoveMapping(int index);
    bool SetMappingRange(int index, float lo, float hi);
    /// <summary>The target's short label ("Decay") and the device it sits on ("" for the slot's own).</summary>
    (string Param, string Device) TargetLabel(RackMacroMapping m);
    (float Min, float Max) TargetRange(RackMacroMapping m);
    float TargetValue(RackMacroMapping m);
    /// <summary>A target value as a number in its display unit, and that unit ("%", "st", "dB", "").</summary>
    string FormatTarget(RackMacroMapping m, float v);
    string TargetUnit(RackMacroMapping m);
    /// <summary>Fills the Map menu for <paramref name="macro"/>: every target, grouped by slot.</summary>
    void FillMapMenu(MenuFlyout menu, int macro, Action changed);
}

internal static class MacroPanel
{
    public const int Count = 8;

    /// <summary>The macro's value, 0..1.</summary>
    public static float Value(IAudioEngine e, IMacroSurface s, int m) => e.PluginParamGet(s.TrackId, s.AutomationDevice, s.MacroParamIndex(m));

    public static List<RackMacroMapping> Mappings(IMacroSurface s, int macro, List<int>? indices = null)
    {
        var list = new List<RackMacroMapping>();
        int n = s.MappingCount();
        for (int i = 0; i < n; i++)
            if (s.TryGetMapping(i, out var mm) && mm.Macro == macro) { list.Add(mm); indices?.Add(i); }
        return list;
    }

    public static int Assigned(IMacroSurface s)
    {
        int n = 0;
        for (int m = 0; m < Count; m++) if (Mappings(s, m).Count > 0) n++;
        return n;
    }

    /// <summary>Where a macro goes, in a few words: "→ Decay · 16 pads", "→ Reverb · Snare, Clap".</summary>
    public static string Receivers(IMacroSurface s, int macro, bool compact = false)
    {
        var maps = Mappings(s, macro);
        if (maps.Count == 0) return "";
        var labels = maps.Select(m => { var (p, d) = s.TargetLabel(m); return Short(d.Length > 0 ? d : p); }).Distinct().ToList();
        var slots = maps.Select(m => m.Chain).Distinct().ToList();
        string what = labels.Count <= 2 ? string.Join(" + ", labels) : $"{labels.Count} params";
        string where = slots.Count <= 2
            ? string.Join(", ", slots.Select(s.SlotName))
            : slots.Count == s.Slots().Count ? (compact ? "all" : $"all {slots.Count} {s.SlotNoun}") : $"{slots.Count} {s.SlotNoun}";
        return $"→ {what} · {where}";
    }

    /// <summary>A device name without the maker's prefix ("Nota Reverb" → "Reverb").</summary>
    public static string Short(string name) => name.StartsWith("Nota ", StringComparison.Ordinal) ? name[5..] : name;

    /// <summary>The status line: the selected macro and one other, and the free slots.</summary>
    public static string Status(IAudioEngine e, IMacroSurface s, int sel)
    {
        var parts = new List<string>();
        string One(int m)
        {
            int slots = Mappings(s, m).Select(x => x.Chain).Distinct().Count();
            return $"{s.MacroName(m)} {Pct(Value(e, s, m))} → {slots} {(slots == 1 ? s.SlotNoun.TrimEnd('s') : s.SlotNoun)}";
        }
        if (Mappings(s, sel).Count > 0) parts.Add(One(sel));
        for (int m = 0; m < Count && parts.Count < 2; m++) if (m != sel && Mappings(s, m).Count > 0) parts.Add(One(m));
        int free = Count - Assigned(s);
        parts.Add(free == 0 ? "every macro assigned" : $"{free} free");
        return string.Join(" · ", parts);
    }

    // ==================== the grid ====================

    /// <summary>The eight macros, 4 × 2. <paramref name="stacked"/>: the knob over its words (the
    /// Drum Rack's tall cells); otherwise the knob beside them (Rhythm's). A click selects.</summary>
    public static Control Grid(DeviceCardContext ctx, IMacroSurface s, int selected, Action<int> select, List<Action> tick, bool stacked,
                               bool receiversInMenu = false)
    {
        var g = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowSpacing = stacked ? 5 : 4, ColumnSpacing = stacked ? 5 : 4, Margin = stacked ? new Thickness(7, 5) : new Thickness(6, 5),
        };
        for (int m = 0; m < Count; m++)
        {
            var cell = Cell(ctx, s, m, m == selected, select, tick, stacked, receiversInMenu);
            Avalonia.Controls.Grid.SetRow(cell, m / 4); Avalonia.Controls.Grid.SetColumn(cell, m % 4);
            g.Children.Add(cell);
        }
        return g;
    }

    private static Control Cell(DeviceCardContext ctx, IMacroSurface s, int m, bool sel, Action<int> select, List<Action> tick, bool stacked,
                                bool receiversInMenu)
    {
        var e = ctx.Engine;
        int track = s.TrackId, di = s.AutomationDevice, pi = s.MacroParamIndex(m);
        bool assigned = Mappings(s, m).Count > 0;
        string pid = RhythmModel.MacroId(m);

        var knob = new Knob(Value(e, s, m), 1.0) { Accent = true, Default = 0.5, Width = Knob.SizeSecondary, Height = Knob.SizeSecondary, IsDim = !assigned };
        var value = new TextBlock { Text = Pct(Value(e, s, m)), FontSize = 7, FontFamily = NotaFonts.MonoFamily, Foreground = assigned ? AccentBright : TextDisabled, LineHeight = 9 };
        knob.ValueChanged += v =>
        {
            e.PluginParamSet(track, di, pi, (float)v);
            value.Text = Pct((float)v);
            ctx.InvokeRackParamRefreshers();   // mapped controls elsewhere on the card follow
        };
        knob.GestureBegin += () => e.BeginAutomationWrite(track, AutomationTarget.PluginParam, di, -1, pid);
        knob.GestureEnd += () =>
        {
            e.EndAutomationWrite(track, AutomationTarget.PluginParam, di, -1, pid);
            if (!sel) select(m);   // the macro you turned is the one whose receivers show (after the drag)
        };
        tick.Add(() => { if (!knob.Dragging) { float v = Value(e, s, m); if (Math.Abs(v - knob.Value) > 1e-4) { knob.Value = v; value.Text = Pct(v); } } });
        MidiLearn.Bind(knob, MidiTarget.PluginParam(track, di, pi), $"Macro {m + 1} · {s.MacroName(m)}");

        string name = s.MacroName(m);
        bool custom = name != $"Macro {m + 1}";
        var title = new TextBlock
        {
            Text = assigned || custom ? (stacked ? $"MACRO {m + 1} · {name.ToUpperInvariant()}" : $"{m + 1} · {name.ToUpperInvariant()}") : (stacked ? $"MACRO {m + 1}" : $"{m + 1}"),
            FontSize = 7, FontWeight = FontWeight.Bold, LetterSpacing = 0.56, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = !assigned ? TextDisabled : sel ? AccentBright : NotaPalette.TextStrong,
        };
        var recv = new TextBlock
        {
            Text = assigned ? Receivers(s, m, compact: !stacked) : "not assigned", FontSize = 7, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = assigned ? NotaPalette.TealBright : TextDisabled,
        };

        Control content;
        if (stacked)
        {
            title.HorizontalAlignment = recv.HorizontalAlignment = value.HorizontalAlignment = HorizontalAlignment.Center;
            var col = new StackPanel { Spacing = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { knob } };
            if (assigned) col.Children.Add(value);
            col.Children.Add(title); col.Children.Add(recv);
            knob.HorizontalAlignment = HorizontalAlignment.Center;
            content = col;
        }
        else
        {
            var words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { title } };
            if (assigned) words.Children.Add(value);
            words.Children.Add(recv);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 3, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(knob);
            Avalonia.Controls.Grid.SetColumn(words, 1); row.Children.Add(words);
            content = row;
        }

        var cell = new Border
        {
            CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(assigned ? 1 : 0), ClipToBounds = true,
            Padding = stacked ? new Thickness(3) : new Thickness(2, 3, 3, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Background = sel ? NotaPalette.AccentSubtle : assigned ? NotaPalette.SurfaceRaised : NotaPalette.BgSunken,
            BorderBrush = sel ? NotaPalette.BorderBrass : BorderDef, Child = content,
        };
        Control host = cell;
        if (!assigned)
        {
            // An empty macro: a dashed outline instead of the cell edge.
            var dash = new Rectangle
            {
                Stroke = sel ? NotaPalette.BorderBrass : BorderDef, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double>(3, 2),
                RadiusX = 4, RadiusY = 4, IsHitTestVisible = false,
            };
            host = new Panel { Children = { cell, dash } };
        }
        ToolTip.SetTip(host, assigned
            ? $"{name} — {Receivers(s, m)}\nDrag to turn · click to show its receivers · right-click to map, rename or clear"
            : $"Macro {m + 1} is free — right-click (or Map) to give it a target");
        host.PointerPressed += (_, ev) =>
        {
            var pt = ev.GetCurrentPoint(host).Properties;
            if (pt.IsRightButtonPressed) { ev.Handled = true; ShowMenu(ctx, s, host, m, receiversInMenu); return; }
            if (pt.IsLeftButtonPressed) { ev.Handled = true; if (!sel) select(m); }
        };
        return host;
    }

    // Right-click on a macro: map a target, rename, clear.
    public static void ShowMenu(DeviceCardContext ctx, IMacroSurface s, Control anchor, int m, bool receivers = false)
    {
        var f = new MenuFlyout();
        if (receivers)
        {
            // Where the card has no receivers panel (Rhythm), they open in a flyout.
            var rc = new MenuItem { Header = "Receivers and ranges…" };
            rc.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowReceivers(ctx, s, anchor, m));
            f.Items.Add(rc);
        }
        var map = new MenuFlyout();
        s.FillMapMenu(map, m, () => { ctx.NotifyChanged(); ctx.RequestRebuild(); });
        var mapItem = new MenuItem { Header = "Map to" };
        foreach (var it in map.Items.ToList()) { map.Items.Remove(it); mapItem.Items.Add(it); }
        f.Items.Add(mapItem);
        var rename = new MenuItem { Header = "Rename…" };
        rename.Click += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(() => PromptRename(ctx, s, anchor, m));
        f.Items.Add(rename);
        int n = Mappings(s, m).Count;
        if (n > 0)
        {
            f.Items.Add(new Separator());
            var clear = new MenuItem { Header = $"Clear {n} mapping{(n == 1 ? "" : "s")}" };
            clear.Click += (_, _) => { Clear(s, m); ctx.NotifyChanged(); ctx.RequestRebuild(); };
            f.Items.Add(clear);
        }
        f.ShowAt(anchor, showAtPointer: true);
    }

    public static void Clear(IMacroSurface s, int m)
    {
        for (int i = s.MappingCount() - 1; i >= 0; i--)
            if (s.TryGetMapping(i, out var mm) && mm.Macro == m) s.RemoveMapping(i);
    }

    public static void PromptRename(DeviceCardContext ctx, IMacroSurface s, Control anchor, int m)
    {
        var box = new TextBox { Text = s.MacroName(m), FontSize = 9, Width = 110, Height = 20, Padding = new Thickness(4, 0) };
        var fly = new Flyout { Content = box };
        box.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { s.SetMacroName(m, (box.Text ?? "").Trim()); ctx.NotifyChanged(); fly.Hide(); ctx.RequestRebuild(); }
            else if (e.Key == Key.Escape) fly.Hide();
        };
        fly.ShowAt(anchor);
        box.SelectAll();
        box.Focus();
    }

    /// <summary>Adds a mapping for <paramref name="macro"/>, replacing one of the same macro on the same target.</summary>
    public static void Map(IMacroSurface s, int macro, int slot, int device, int param, float lo, float hi)
    {
        for (int i = s.MappingCount() - 1; i >= 0; i--)
            if (s.TryGetMapping(i, out var mm) && mm.Macro == macro && mm.Chain == slot && mm.DeviceIndex == device && mm.ParamIndex == param)
                s.RemoveMapping(i);
        s.AddMapping(macro, slot, device, param, lo, hi);
    }

    // ==================== the selected macro's receivers ====================

    /// <summary>The selected macro: its name and value, every receiver with its range (drag the
    /// band's ends), and Map / Rename / Clear.</summary>
    public static Control Detail(DeviceCardContext ctx, IMacroSurface s, int m, List<Action> tick)
    {
        var e = ctx.Engine;
        var val = new TextBlock { FontSize = 7, FontFamily = NotaFonts.MonoFamily, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center };
        tick.Add(() => val.Text = Pct(Value(e, s, m)));
        var head = new Grid { Height = 20, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5, Margin = new Thickness(8, 0) };
        head.Children.Add(Cap($"MACRO {m + 1}"));
        var nm = new TextBlock { Text = s.MacroName(m), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        Avalonia.Controls.Grid.SetColumn(nm, 1); head.Children.Add(nm);
        Avalonia.Controls.Grid.SetColumn(val, 2); head.Children.Add(val);
        var headBar = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

        var idx = new List<int>();
        var maps = Mappings(s, m, idx);
        // By slot, then target, so a pad's receivers sit together.
        var order = Enumerable.Range(0, maps.Count).OrderBy(k => maps[k].Chain).ThenBy(k => maps[k].DeviceIndex).ThenBy(k => maps[k].ParamIndex).ToList();
        maps = order.Select(k => maps[k]).ToList();
        idx = order.Select(k => idx[k]).ToList();
        var rows = new StackPanel { Spacing = 6 };
        var capRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        capRow.Children.Add(Cap("RECEIVERS"));
        var mm2 = new TextBlock { Text = "min → max", FontSize = 7, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Controls.Grid.SetColumn(mm2, 1); capRow.Children.Add(mm2);
        rows.Children.Add(capRow);
        // One receiver per row. When a macro drives several params on each slot, the rows say which.
        bool manyParams = maps.Select(x => s.TargetLabel(x)).Distinct().Count() > 1;
        for (int k = 0; k < maps.Count; k++) rows.Children.Add(ReceiverRow(ctx, s, idx[k], maps[k], manyParams, tick));
        if (maps.Count == 0)
            rows.Children.Add(new TextBlock
            {
                Text = "Nothing mapped yet. Map gives this knob a pad control, an instrument param or any of the kit's effects.",
                FontSize = 8, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Border mapBtn = null!;
        mapBtn = Button("Map", teal: true, () => ShowMapMenu(ctx, s, mapBtn, m));
        ToolTip.SetTip(mapBtn, "Add a receiver: a pad control, the pad's instrument or one of its effects");
        Border renameBtn = null!;
        renameBtn = Button("Rename", teal: false, () => PromptRename(ctx, s, renameBtn, m));
        var clearBtn = Button("Clear", teal: false, () => { Clear(s, m); ctx.NotifyChanged(); ctx.RequestRebuild(); });
        ToolTip.SetTip(clearBtn, "Remove every receiver of this macro");
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 4 };
        buttons.Children.Add(mapBtn);
        Avalonia.Controls.Grid.SetColumn(renameBtn, 1); buttons.Children.Add(renameBtn);
        Avalonia.Controls.Grid.SetColumn(clearBtn, 2); buttons.Children.Add(clearBtn);
        var foot = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = buttons,
        };

        DockPanel.SetDock(foot, Dock.Bottom);
        var body = new DockPanel { LastChildFill = true, Margin = new Thickness(8, 6), Children = { foot, scroll } };
        DockPanel.SetDock(headBar, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { headBar, body } };
    }

    /// <summary>The selected macro's receivers panel in a flyout (Rhythm has no panel for it).</summary>
    public static void ShowReceivers(DeviceCardContext ctx, IMacroSurface s, Control anchor, int m)
    {
        var tick = new List<Action>();
        var box = new Border
        {
            Width = 240, Height = 190, Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = Detail(ctx, s, m, tick),
        };
        var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) => { foreach (var t in tick) t(); };
        var fly = new Flyout { Content = box, Placement = PlacementMode.BottomEdgeAlignedLeft };
        fly.Opened += (_, _) => timer.Start();
        fly.Closed += (_, _) => timer.Stop();
        fly.ShowAt(anchor);
    }

    /// <summary>The Map menu on its own (the Map buttons).</summary>
    public static void ShowMapMenu(DeviceCardContext ctx, IMacroSurface s, Control anchor, int m)
    {
        var f = new MenuFlyout();
        s.FillMapMenu(f, m, () => { ctx.NotifyChanged(); ctx.RequestRebuild(); });
        f.ShowAt(anchor);
    }

    private static Control ReceiverRow(DeviceCardContext ctx, IMacroSurface s, int index, RackMacroMapping mm, bool withParam, List<Action> tick)
    {
        var (pn, dev) = s.TargetLabel(mm);
        var (min, max) = s.TargetRange(mm);
        double span = Math.Max(1e-6, max - min);
        double fLo = Math.Clamp((mm.RangeMin - min) / span, 0, 1), fHi = Math.Clamp((mm.RangeMax - min) / span, 0, 1);

        var hue = new Border { Width = 3, Height = 8, CornerRadius = NotaRadius.Bar, Background = s.SlotHue(mm.Chain), VerticalAlignment = VerticalAlignment.Center };
        string label = withParam ? $"{s.SlotName(mm.Chain)} · {pn}" : s.SlotName(mm.Chain);
        var name = new TextBlock { Text = label, FontSize = 8, FontWeight = FontWeight.Medium, Foreground = TextPrimary, Width = withParam ? 74 : 50, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(name, dev.Length > 0 ? $"{s.SlotName(mm.Chain)} · {Short(dev)} · {pn}" : $"{s.SlotName(mm.Chain)} · {pn}");

        var band = new Border { Height = 3, CornerRadius = NotaRadius.Bar, Background = NotaPalette.Teal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var now = new Border { Width = 2, Height = 7, Background = AccentBright, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
        var track = new Panel
        {
            Height = 9, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast), VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Height = 3, CornerRadius = NotaRadius.Bar, Background = NotaPalette.BgSunken, VerticalAlignment = VerticalAlignment.Center }, band, now },
        };
        var vals = new TextBlock { FontSize = 7, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary, Width = 46, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        string unit = s.TargetUnit(mm);
        void Text() => vals.Text = $"{s.FormatTarget(mm, (float)(min + fLo * span))}–{s.FormatTarget(mm, (float)(min + fHi * span))}"
                                   + (unit.Length > 0 ? "\u2009" + unit : "");
        void Layout()
        {
            double w = track.Bounds.Width;
            double a = Math.Min(fLo, fHi), b = Math.Max(fLo, fHi);   // an inverted range still draws
            band.Margin = new Thickness(a * w, 0, 0, 0); band.Width = Math.Max(2, (b - a) * w);
            double cur = Math.Clamp((s.TargetValue(mm) - min) / span, 0, 1);
            now.Margin = new Thickness(Math.Clamp(cur * w - 1, 0, Math.Max(0, w - 2)), 0, 0, 0);
        }
        Text();
        tick.Add(Layout);
        track.SizeChanged += (_, _) => Layout();

        int drag = 0;   // 1 = the low end, 2 = the high end
        void Apply(double x)
        {
            double f = Math.Clamp(x / Math.Max(1, track.Bounds.Width), 0, 1);
            if (drag == 1) fLo = f; else fHi = f;
            s.SetMappingRange(index, (float)(min + fLo * span), (float)(min + fHi * span));
            Text(); Layout();
        }
        track.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(track).Properties.IsLeftButtonPressed) return;
            double x = e.GetPosition(track).X, w = track.Bounds.Width;
            drag = Math.Abs(x - fLo * w) <= Math.Abs(x - fHi * w) ? 1 : 2;
            e.Pointer.Capture(track); e.Handled = true; Apply(x);
        };
        track.PointerMoved += (_, e) => { if (drag != 0) Apply(e.GetPosition(track).X); };
        track.PointerReleased += (_, e) => { if (drag != 0) { drag = 0; e.Pointer.Capture(null); ctx.NotifyChanged(); } };
        ToolTip.SetTip(track, "The range the macro sweeps: drag either end · the brass tick is where it is now");

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 5 };
        g.Children.Add(hue);
        Avalonia.Controls.Grid.SetColumn(name, 1); g.Children.Add(name);
        Avalonia.Controls.Grid.SetColumn(track, 2); g.Children.Add(track);
        Avalonia.Controls.Grid.SetColumn(vals, 3); g.Children.Add(vals);
        g.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(g).Properties.IsRightButtonPressed) return;
            e.Handled = true;
            var f = new MenuFlyout();
            var rm = new MenuItem { Header = "Remove this receiver" };
            rm.Click += (_, _) => { s.RemoveMapping(index); ctx.NotifyChanged(); ctx.RequestRebuild(); };
            f.Items.Add(rm);
            f.ShowAt(g, showAtPointer: true);
        };
        return g;
    }

    // ---- small builders ------------------------------------------------------------------

    public static TextBlock Cap(string t) => new()
    {
        Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.KnobLabelTracking,
        Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A 16px action button; the teal one is Map (it adds modulation).</summary>
    public static Border Button(string text, bool teal, Action click)
    {
        var tb = new TextBlock
        {
            Text = text, FontSize = 7, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Foreground = teal ? NotaPalette.TealBright : NotaPalette.TextStrong,
        };
        IBrush rest = teal ? NotaPalette.Wash(NotaPalette.Teal, 0x33) : NotaPalette.SurfaceRaised;
        var b = new Border
        {
            Height = 16, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(6, 0),
            Background = rest, BorderBrush = teal ? NotaPalette.Teal : BorderDef, Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
        };
        b.PointerEntered += (_, _) => b.Background = teal ? NotaPalette.Wash(NotaPalette.Teal, 0x55) : NotaPalette.SurfaceHover;
        b.PointerExited += (_, _) => b.Background = rest;
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true; click();
        };
        return b;
    }

    /// <summary>A menu item that maps <paramref name="macro"/> to one target.</summary>
    public static MenuItem Target(IMacroSurface s, string header, int macro, int slot, int device, int param, float lo, float hi, Action changed, bool mapped = false)
    {
        var mi = new MenuItem { Header = header };
        if (mapped) mi.Icon = new Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Teal };
        mi.Click += (_, _) => { Map(s, macro, slot, device, param, lo, hi); changed(); };
        return mi;
    }

    /// <summary>A device param as the receivers show it: 0..1 params in percent, the rest as numbers.</summary>
    public static string FormatDeviceParam(float min, float max, float v)
        => min == 0f && max == 1f ? $"{Math.Round(v * 100):0}" : v.ToString(Math.Abs(max - min) >= 20 ? "0" : "0.##", NotaNum.Culture);
    public static string DeviceParamUnit(float min, float max) => min == 0f && max == 1f ? "%" : "";

    public static bool IsMapped(IMacroSurface s, int macro, int slot, int device, int param)
    {
        int n = s.MappingCount();
        for (int i = 0; i < n; i++)
            if (s.TryGetMapping(i, out var mm) && mm.Macro == macro && mm.Chain == slot && mm.DeviceIndex == device && mm.ParamIndex == param) return true;
        return false;
    }
}
