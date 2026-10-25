// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the kit macros of the Drum Rack and Nota Rhythm, from the almanac's
// "1c Macro" cards. Eight knobs over the whole kit, as a 4 × 2 grid of raised slots (like the
// Drum Rack's pads): the knob, its name and value, and in ink 3 where it goes ("→ Reverb · Snare,
// Clap"). An unassigned macro is a pressed-in slot in ink 4. The selected macro's receivers —
// every mapping, with its range as a soft orange band between two orange ends and the value it
// sits at now as a neutral tick — are listed by Detail (the Drum Rack's right panel); the band's
// ends drag.
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
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
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

    /// <summary>The eight macros, 4 × 2, each the knob beside its words. <paramref name="stacked"/>:
    /// the Drum Rack's tall slots, flush with the pad grid (the name over where it goes, on up to
    /// two lines); otherwise Rhythm's short ones ("1 · Tune" over where it goes). A click selects.</summary>
    public static Control Grid(DeviceCardContext ctx, IMacroSurface s, int selected, Action<int> select, List<Action> tick, bool stacked,
                               bool receiversInMenu = false)
    {
        var g = new Grid
        {
            RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("*,*,*,*"),
            RowSpacing = 4, ColumnSpacing = 4, Margin = stacked ? new Thickness(0) : new Thickness(6, 5),
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

        var knob = new Knob(Value(e, s, m), 1.0) { Accent = true, Default = 0.5, Width = Knob.Size, Height = Knob.Size, IsDim = !assigned, Format = v => Pct((float)v) };
        var value = new TextBlock
        {
            Text = Pct(Value(e, s, m)), FontSize = NotaType.KnobValue, FontFamily = NotaFonts.UiFamily, Foreground = TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = assigned,
        };
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
        // ⌥-click types, double-click resets; right-click stays the macro's own menu (the cell's).
        ValueEntry.Attach(knob, $"Macro {m + 1}", value, menu: false);

        string name = s.MacroName(m);
        bool custom = name != $"Macro {m + 1}";
        IBrush nameInk = sel ? AccentBright : assigned ? TextPrimary : TextDisabled;
        var recv = new TextBlock
        {
            Text = assigned ? Receivers(s, m, compact: true) : stacked ? "not assigned" : "free", FontSize = NotaType.RowLabel, TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = assigned ? TextTertiary : TextDisabled,
        };
        Control words;
        if (stacked)
        {
            // The value under the knob; beside it the name (or "Macro N" while it has none) over
            // where it goes, on up to two lines so a long target isn't cut to "Decay · 1…".
            var nameTb = custom
                ? new TextBlock { Text = name, FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = nameInk }
                : new TextBlock { Text = $"Macro {m + 1}", FontSize = NotaType.RowLabel, Foreground = sel ? AccentBright : assigned ? TextTertiary : TextDisabled, TextTrimming = TextTrimming.CharacterEllipsis };
            recv.TextWrapping = TextWrapping.Wrap;
            recv.MaxLines = 2;
            words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { nameTb, recv } };
        }
        else
        {
            // Two lines: "N · name" with the value at the right, where it goes.
            var title = new TextBlock
            {
                Text = assigned || custom ? $"{m + 1} · {name}" : $"{m + 1}", FontSize = NotaType.KnobValue, FontWeight = FontWeight.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Foreground = nameInk,
            };
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4, Children = { title } };
            Avalonia.Controls.Grid.SetColumn(value, 1); line.Children.Add(value);
            words = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center, Children = { line, recv } };
        }
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = stacked ? 8 : 4, VerticalAlignment = VerticalAlignment.Center };
        knob.VerticalAlignment = VerticalAlignment.Center;
        if (stacked)
        {
            knob.HorizontalAlignment = value.HorizontalAlignment = HorizontalAlignment.Center;
            content.Children.Add(new StackPanel { Spacing = 1, MinWidth = 34, VerticalAlignment = VerticalAlignment.Center, Children = { knob, value } });
        }
        else content.Children.Add(knob);
        Avalonia.Controls.Grid.SetColumn(words, 1); content.Children.Add(words);

        // An assigned macro is a raised slot (the soft tint when selected); a free one is pressed in.
        var cell = new Border
        {
            CornerRadius = NotaRadius.Field,
            Padding = stacked ? new Thickness(6, 3, 8, 3) : new Thickness(3, 3, 5, 3), Cursor = new Cursor(StandardCursorType.Hand),
            Background = sel ? NotaPalette.AccentSoft : assigned ? NotaPalette.SurfaceHover : NotaPalette.Groove, Child = content,
        };
        if (assigned) cell.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        Control host = cell;
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
        f.Items.Add(MenuKit.Header(s.MacroName(m), null, $"Macro · {m + 1}"));
        f.Items.Add(new Separator());
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
        f.Items.Add(MenuKit.Item("Rename…", GlyphKind.Edit, () => Avalonia.Threading.Dispatcher.UIThread.Post(() => PromptRename(ctx, s, anchor, m))));
        int n = Mappings(s, m).Count;
        if (n > 0)
        {
            f.Items.Add(new Separator());
            f.Items.Add(MenuKit.Danger($"Clear {n} mapping{(n == 1 ? "" : "s")}", GlyphKind.Trash,
                () => { Clear(s, m); ctx.NotifyChanged(); ctx.RequestRebuild(); }));
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
        var box = new TextBox { Text = s.MacroName(m), FontSize = NotaType.DeviceCaption, Width = 120, Height = 22, Padding = new Thickness(6, 0) };
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
        var val = new TextBlock { FontSize = NotaType.KnobValue, FontFamily = NotaFonts.UiFamily, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        tick.Add(() => val.Text = Pct(Value(e, s, m)));
        // Header (like the pad panel's): name · "Macro N" · the value at the right.
        var head = new Grid { Height = 16, ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6, Margin = new Thickness(0, 0, 0, 6) };
        var nm = new TextBlock { Text = s.MacroName(m), FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 100 };
        head.Children.Add(nm);
        var idxTb = Cap($"Macro {m + 1}");
        idxTb.FontWeight = FontWeight.Normal;
        idxTb.IsVisible = s.MacroName(m) != $"Macro {m + 1}";   // an unnamed macro would read "Macro 1  Macro 1"
        Avalonia.Controls.Grid.SetColumn(idxTb, 1); head.Children.Add(idxTb);
        val.HorizontalAlignment = HorizontalAlignment.Right;
        Avalonia.Controls.Grid.SetColumn(val, 2); head.Children.Add(val);
        var headBar = head;

        var idx = new List<int>();
        var maps = Mappings(s, m, idx);
        // By slot, then target, so a pad's receivers sit together.
        var order = Enumerable.Range(0, maps.Count).OrderBy(k => maps[k].Chain).ThenBy(k => maps[k].DeviceIndex).ThenBy(k => maps[k].ParamIndex).ToList();
        maps = order.Select(k => maps[k]).ToList();
        idx = order.Select(k => idx[k]).ToList();
        var rows = new StackPanel { Spacing = 6, Margin = new Thickness(0, 0, 6, 0) };
        var capRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        capRow.Children.Add(Cap("Receivers"));
        var mm2 = new TextBlock { Text = "min → max", FontSize = NotaType.RowLabel, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        Avalonia.Controls.Grid.SetColumn(mm2, 1); capRow.Children.Add(mm2);
        rows.Children.Add(capRow);
        // One receiver per row. When a macro drives several params on each slot, the rows say which.
        bool manyParams = maps.Select(x => s.TargetLabel(x)).Distinct().Count() > 1;
        for (int k = 0; k < maps.Count; k++) rows.Children.Add(ReceiverRow(ctx, s, idx[k], maps[k], manyParams, tick));
        if (maps.Count == 0)
            rows.Children.Add(new TextBlock
            {
                Text = "Nothing mapped yet. Map gives this knob a pad control, an instrument param or any of the kit's effects.",
                FontSize = NotaType.RowLabel, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0),
            });
        var scroll = new ScrollViewer { Content = rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };

        Border mapBtn = null!;
        mapBtn = Button("Map", () => ShowMapMenu(ctx, s, mapBtn, m));
        ToolTip.SetTip(mapBtn, "Add a receiver: a pad control, the pad's instrument or one of its effects");
        Border renameBtn = null!;
        renameBtn = Button("Rename", () => PromptRename(ctx, s, renameBtn, m));
        var clearBtn = Button("Clear", () => { Clear(s, m); ctx.NotifyChanged(); ctx.RequestRebuild(); });
        ToolTip.SetTip(clearBtn, "Remove every receiver of this macro");
        var buttons = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 6 };
        buttons.Children.Add(mapBtn);
        Avalonia.Controls.Grid.SetColumn(renameBtn, 1); buttons.Children.Add(renameBtn);
        Avalonia.Controls.Grid.SetColumn(clearBtn, 2); buttons.Children.Add(clearBtn);
        var foot = new Border
        {
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 6, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = buttons,
        };

        DockPanel.SetDock(foot, Dock.Bottom);
        var body = new DockPanel { LastChildFill = true, Children = { foot, scroll } };
        DockPanel.SetDock(headBar, Dock.Top);
        return new DockPanel { LastChildFill = true, Children = { headBar, body } };
    }

    /// <summary>The selected macro's receivers panel in a flyout (Rhythm has no panel for it).</summary>
    public static void ShowReceivers(DeviceCardContext ctx, IMacroSurface s, Control anchor, int m)
    {
        var tick = new List<Action>();
        var box = new Border
        {
            Width = 250, Height = 200, Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Group, ClipToBounds = true, Padding = new Thickness(10, 8), Child = Detail(ctx, s, m, tick),
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

        var hue = new Border { Width = 3, Height = 10, CornerRadius = NotaRadius.Fine, Background = s.SlotHue(mm.Chain), VerticalAlignment = VerticalAlignment.Center };
        string label = withParam ? $"{s.SlotName(mm.Chain)} · {pn}" : s.SlotName(mm.Chain);
        var name = new TextBlock { Text = label, FontSize = NotaType.RowLabel, Foreground = TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(name, dev.Length > 0 ? $"{s.SlotName(mm.Chain)} · {Short(dev)} · {pn}" : $"{s.SlotName(mm.Chain)} · {pn}");

        // The range: a groove with the soft orange tint between its two ends (orange lines, the
        // values), and a neutral tick where the target is now.
        var band = new Border { Background = NotaPalette.AccentSoft, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var endLo = new Border { Width = 2, Background = NotaPalette.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var endHi = new Border { Width = 2, Background = NotaPalette.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var now = new Border { Width = 1.5, Background = NotaPalette.VizInk, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Stretch };
        var groove = new Border { CornerRadius = NotaRadius.Fine, Background = NotaPalette.Groove, ClipToBounds = true, Child = new Panel { Children = { band, endLo, endHi, now } } };
        groove.BindResource(Border.BoxShadowProperty, "Shadow.Groove");
        var track = new Panel
        {
            Height = 12, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.SizeWestEast), VerticalAlignment = VerticalAlignment.Center,
            Children = { new Border { Height = 8, VerticalAlignment = VerticalAlignment.Center, Child = groove } },
        };
        var vals = new TextBlock { FontSize = NotaType.RowLabel, FontFamily = NotaFonts.UiFamily, Foreground = TextTertiary, TextAlignment = TextAlignment.Right, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        string unit = s.TargetUnit(mm);
        void Text() => vals.Text = $"{s.FormatTarget(mm, (float)(min + fLo * span))}–{s.FormatTarget(mm, (float)(min + fHi * span))}"
                                   + (unit.Length > 0 ? "\u2009" + unit : "");
        void Layout()
        {
            double w = track.Bounds.Width;
            double a = Math.Min(fLo, fHi), b = Math.Max(fLo, fHi);   // an inverted range still draws
            band.Margin = new Thickness(a * w, 0, 0, 0); band.Width = Math.Max(2, (b - a) * w);
            endLo.Margin = new Thickness(Math.Clamp(a * w - 1, 0, Math.Max(0, w - 2)), 0, 0, 0);
            endHi.Margin = new Thickness(Math.Clamp(b * w - 1, 0, Math.Max(0, w - 2)), 0, 0, 0);
            double cur = Math.Clamp((s.TargetValue(mm) - min) / span, 0, 1);
            now.Margin = new Thickness(Math.Clamp(cur * w - 0.75, 0, Math.Max(0, w - 1.5)), 0, 0, 0);
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
        track.PointerCaptureLost += (_, _) => { if (drag != 0) { drag = 0; ctx.NotifyChanged(); } };
        ToolTip.SetTip(track, "The range the macro sweeps: drag either end · the tick is where it is now");

        // Two lines: hue · pad (· param) · values, then the range under the name.
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 6, RowSpacing = 2, Background = Brushes.Transparent };
        g.Children.Add(hue);
        Avalonia.Controls.Grid.SetColumn(name, 1); g.Children.Add(name);
        Avalonia.Controls.Grid.SetColumn(vals, 2); g.Children.Add(vals);
        Avalonia.Controls.Grid.SetRow(track, 1); Avalonia.Controls.Grid.SetColumn(track, 1); Avalonia.Controls.Grid.SetColumnSpan(track, 2);
        g.Children.Add(track);
        g.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(g).Properties.IsRightButtonPressed) return;
            e.Handled = true;
            var f = new MenuFlyout();
            f.Items.Add(MenuKit.Danger("Remove this receiver", GlyphKind.Trash,
                () => { s.RemoveMapping(index); ctx.NotifyChanged(); ctx.RequestRebuild(); }));
            f.ShowAt(g, showAtPointer: true);
        };
        return g;
    }

    // ---- small builders ------------------------------------------------------------------

    public static TextBlock Cap(string t) => new()
    {
        Text = NotaText.Label(t), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium,
        Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
    };

    /// <summary>A raised 22px device button (the kit's TextButton, filling its column).</summary>
    public static Border Button(string text, Action click)
    {
        var b = TextButton(text);
        b.Height = 22; b.Padding = new Thickness(4, 0); b.HorizontalAlignment = HorizontalAlignment.Stretch;
        if (b.Child is TextBlock tb) { tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center; }
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
        // A mapped target is ticked like any chosen item (the menu theme's accent check).
        var mi = new MenuItem { Header = header, ToggleType = MenuItemToggleType.CheckBox, IsChecked = mapped };
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
