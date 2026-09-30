// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the hosted plug-in card (VST3 / AU), for an instrument (deviceIndex -1)
// and an insert effect alike. The shell header names the plug-in; two sizes share it:
//   • S 260 × 260 — who it is (name, vendor, format, kind, params, latency) over the two
//     actions: open the native editor, save a preset.
//   • L 700 × 260 — the same column on the left, then every automatable parameter as a
//     knob grid (search + pages), and, for a plug-in with a sidechain bus, its source.
// The size is editor state kept by the chain view (a hosted plug-in has no "view" param).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

/// <summary>What the catalog knows about a hosted plug-in.</summary>
internal readonly record struct PluginInfo(string Name, string Vendor, string Format, bool IsInstrument)
{
    /// <summary>Looks the device's plug-in up in the scanned catalog; falls back to the
    /// engine's name and the identifier's format prefix when it is not installed.</summary>
    public static PluginInfo Of(IAudioEngine engine, int track, int deviceIndex)
    {
        string id = deviceIndex < 0 ? engine.TrackInstrumentPluginId(track) : engine.TrackDevicePluginId(track, deviceIndex);
        string name = engine.DeviceName(track, deviceIndex), vendor = "", format = "";
        if (App.Services?.GetService(typeof(IPluginCatalog)) is IPluginCatalog cat && id.Length > 0
            && cat.IndexOfId(id) is var ci and >= 0 && cat.Description(ci) is { } desc)
        {
            // "Name | Format | inst|fx | Manufacturer"
            var parts = desc.Split('|');
            if (parts.Length > 0 && parts[0].Trim().Length > 0) name = parts[0].Trim();
            if (parts.Length > 1) format = parts[1].Trim();
            if (parts.Length > 3) vendor = parts[3].Trim();
        }
        if (format.Length == 0 && id.IndexOf('-') is var dash and > 0) format = id[..dash];
        if (format == "AudioUnit") format = "AU";
        if (string.IsNullOrWhiteSpace(name)) name = deviceIndex < 0 ? "Plug-in instrument" : "Plug-in";
        return new PluginInfo(name, vendor, format, deviceIndex < 0);
    }
}

internal static class PluginCard
{
    public const double MiniWidth = 260, FullWidth = 700;
    private const double InfoW = 196, ScW = 128, CellW = 58, CellH = 53;

    /// <summary>Per-card view state the chain view keeps across rebuilds.</summary>
    internal sealed class ViewState { public bool Mini = true; public int Page; public string Filter = ""; }

    public static Control Build(DeviceCardContext ctx, int di, PluginInfo info, ViewState state)
    {
        if (state.Mini) return Section(InfoColumn(ctx, di, info, mini: true));

        var e = ctx.Engine; int t = ctx.TrackId;
        bool sidechain = di >= 0 && e.DeviceAcceptsSidechain(t, di);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions(sidechain ? $"{InfoW},*,{ScW}" : $"{InfoW},*"), ColumnSpacing = 5 };
        grid.Children.Add(Section(InfoColumn(ctx, di, info, mini: false)));
        var pars = Section(Params(ctx, di, state)); Grid.SetColumn(pars, 1); grid.Children.Add(pars);
        if (sidechain)
        {
            var sc = Section(DeviceParamControls.SidechainSelector(ctx, di), pad: 6);
            Grid.SetColumn(sc, 2); grid.Children.Add(sc);
        }
        return grid;
    }

    // ---- who it is + the two actions ------------------------------------------

    private static Control InfoColumn(DeviceCardContext ctx, int di, PluginInfo info, bool mini)
    {
        var e = ctx.Engine; int t = ctx.TrackId;

        var name = new TextBlock
        {
            Text = info.Name, FontSize = NotaType.Heading, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTip.SetTip(name, info.Name);
        var vendor = new TextBlock
        {
            Text = info.Vendor.Length > 0 ? info.Vendor : "Unknown vendor", FontSize = NotaType.Body,
            Foreground = info.Vendor.Length > 0 ? TextSecondary : TextTertiary, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var tags = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(0, 4, 0, 0) };
        if (info.Format.Length > 0) tags.Children.Add(Tag(info.Format));
        tags.Children.Add(Tag(info.IsInstrument ? "INSTRUMENT" : "EFFECT"));

        int pc = e.PluginParamCount(t, di);
        double ms = e.SampleRate > 0 ? e.TrackLatencySamples(t) / e.SampleRate * 1000.0 : 0;
        var facts = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 3, Margin = new Thickness(0, 8, 0, 0) };
        Fact(facts, 0, "PARAMS", pc.ToString(NotaNum.Culture));
        Fact(facts, 1, "LATENCY", $"{ms.ToString("0.0", NotaNum.Culture)}\u2009ms");

        var open = ActionButton("Open editor", GlyphKind.PopOut, primary: true);
        ToolTip.SetTip(open, "Open the plug-in's own window");
        open.Click += (_, _) => { try { e.OpenPluginEditor(t, di); } catch { /* no editor */ } };
        var save = ActionButton("Save preset", GlyphKind.Save, primary: false);
        ToolTip.SetTip(save, "Save the plug-in's state as a preset");
        save.Click += (_, _) => ctx.RequestPresetSave(di);

        var top = new StackPanel { Spacing = 1, Children = { name, vendor, tags, facts } };
        var actions = new StackPanel { Spacing = 5, Children = { open, save } };
        if (mini && di >= 0 && e.DeviceAcceptsSidechain(t, di)) actions.Children.Insert(0, SidechainLine(e, t, di));
        var col = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(actions, Dock.Bottom);
        col.Children.Add(actions);
        col.Children.Add(top);
        return col;
    }

    // S has no room for the sidechain controls: it names the source; L edits it.
    private static Control SidechainLine(IAudioEngine e, int t, int di)
    {
        int src = e.DeviceSidechainSource(t, di);
        string label = "None";
        for (int i = 0, n = e.TrackCount; i < n && src >= 0; i++)
            if (e.TryGetTrackInfo(i, out var ti) && ti.Id == src) { label = TrackNames.Of(e, ti); break; }
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(0, 0, 0, 3) };
        Fact(g, 0, "SIDECHAIN", label);
        return g;
    }

    private static void Fact(Grid g, int row, string label, string value)
    {
        if (g.RowDefinitions.Count <= row) g.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        var l = new TextBlock
        {
            Text = label, FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking,
            Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
        };
        var v = new TextBlock
        {
            Text = value, FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.Value, Foreground = TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Grid.SetRow(l, row); Grid.SetRow(v, row); Grid.SetColumn(v, 1);
        g.Children.Add(l); g.Children.Add(v);
    }

    private static Border Tag(string text) => new()
    {
        BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
        Padding = new Thickness(4, 1), VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock
        {
            Text = text.ToUpperInvariant(), FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.KnobLabel,
            FontWeight = FontWeight.Medium, LetterSpacing = 0.8, Foreground = TextSecondary,
        },
    };

    private static Button ActionButton(string text, GlyphKind glyph, bool primary)
    {
        var b = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Center,
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 6,
                Children = { new Glyph(glyph, 10) { VerticalAlignment = VerticalAlignment.Center }, new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } },
            },
        };
        if (primary) b.Classes.Add("primary");
        return b;
    }

    // ---- parameters: search + pages of knobs ------------------------------------

    private static Control Params(DeviceCardContext ctx, int di, ViewState state)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        int total = e.PluginParamCount(t, di);
        var names = new string[total];
        for (int i = 0; i < total; i++) names[i] = e.PluginParamName(t, di, i);

        var title = new TextBlock
        {
            Text = "PARAMETERS", FontSize = NotaType.DeviceSection, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.DeviceSectionTracking, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center,
        };
        var count = new TextBlock { FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.KnobValue, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };

        var search = new TextBox { Watermark = "Filter", Text = state.Filter, FontSize = 9, Classes = { "search" }, VerticalAlignment = VerticalAlignment.Center };
        var searchWrap = new Border
        {
            Width = 116, Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(6, 0), Child = search,
        };
        search.GotFocus += (_, _) => searchWrap.BorderBrush = NotaPalette.Accent;
        search.LostFocus += (_, _) => searchWrap.BorderBrush = BorderDef;

        var pageText = new TextBlock { FontFamily = NotaFonts.MonoFamily, FontSize = NotaType.KnobValue, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30, TextAlignment = TextAlignment.Center };
        var host = new ContentControl();
        int perPage = 1, gen = 0;
        var matches = new List<int>();

        void Render()
        {
            gen++;   // stale knobs stop following once their page is gone
            matches.Clear();
            string f = state.Filter.Trim();
            for (int i = 0; i < total; i++)
                if (f.Length == 0 || names[i].Contains(f, StringComparison.OrdinalIgnoreCase)) matches.Add(i);
            int pages = Math.Max(1, (matches.Count + perPage - 1) / perPage);
            state.Page = Math.Clamp(state.Page, 0, pages - 1);
            pageText.Text = $"{state.Page + 1}/{pages}";
            count.Text = f.Length == 0 ? total.ToString(NotaNum.Culture) : $"{matches.Count}/{total}";

            if (matches.Count == 0)
            {
                host.Content = new TextBlock
                {
                    Text = total == 0 ? "This plug-in exposes no parameters" : "No parameter matches",
                    FontSize = 9, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                return;
            }
            var wrap = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, ItemHeight = CellH + 6 };
            int myGen = gen;
            for (int k = state.Page * perPage; k < Math.Min(matches.Count, (state.Page + 1) * perPage); k++)
                wrap.Children.Add(ParamKnob(ctx, di, matches[k], names[matches[k]], () => myGen == gen));
            host.Content = wrap;
        }
        void Step(int dir)
        {
            int pages = Math.Max(1, (matches.Count + perPage - 1) / perPage);
            int next = Math.Clamp(state.Page + dir, 0, pages - 1);
            if (next != state.Page) { state.Page = next; Render(); }
        }

        search.TextChanged += (_, _) => { if (state.Filter == search.Text) return; state.Filter = search.Text ?? ""; state.Page = 0; Render(); };
        var prev = Glyph(GlyphKind.ChevronLeft, true, () => Step(-1));
        var next = Glyph(GlyphKind.ChevronRight, true, () => Step(+1));
        ToolTip.SetTip(prev, "Previous page"); ToolTip.SetTip(next, "Next page");

        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), Height = 22 };
        head.Children.Add(title);
        Grid.SetColumn(count, 1); head.Children.Add(count);
        Grid.SetColumn(searchWrap, 2); searchWrap.Margin = new Thickness(0, 0, 8, 0); head.Children.Add(searchWrap);
        Grid.SetColumn(prev, 3); head.Children.Add(prev);
        Grid.SetColumn(pageText, 4); head.Children.Add(pageText);
        Grid.SetColumn(next, 5); head.Children.Add(next);

        // The page holds as many knobs as the section fits; wheel over it turns the page.
        var body = new Border { Background = Brushes.Transparent, Padding = new Thickness(0, 4, 0, 0), Child = host };
        body.PointerWheelChanged += (_, ev) => { Step(ev.Delta.Y < 0 ? +1 : -1); ev.Handled = true; };
        body.SizeChanged += (_, ev) =>
        {
            int cols = Math.Max(1, (int)(ev.NewSize.Width / CellW));
            int rows = Math.Max(1, (int)((ev.NewSize.Height - 4) / (CellH + 6)));
            int fit = cols * rows;
            if (fit != perPage) { perPage = fit; state.Page = Math.Min(state.Page, Math.Max(0, (matches.Count - 1) / perPage)); Render(); }
        };

        var col = new DockPanel { LastChildFill = true };
        var headStrip = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };
        DockPanel.SetDock(headStrip, Dock.Top);
        col.Children.Add(headStrip);
        col.Children.Add(body);
        Render();
        return col;
    }

    // One plug-in parameter as the shared gauge knob: the plug-in's own value text, its
    // default on double-click, automation-write gestures, MIDI Learn and live follow.
    private static Control ParamKnob(DeviceCardContext ctx, int di, int p, string name, Func<bool> live)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        string pid = e.PluginParamId(t, di, p);
        string Text(float v) { string s = e.PluginParamText(t, di, p); return s.Length > 0 ? s : Pct(v); }

        float v0 = e.PluginParamGet(t, di, p);
        var value = new TextBlock { Text = Text(v0), Foreground = TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = CellW - 2 };
        var knob = new Knob(v0, 1.0) { Accent = true, Default = e.PluginParamDefault(t, di, p), Width = Knob.SizeSecondary, Height = Knob.SizeSecondary };
        knob.ValueChanged += v => { e.PluginParamSet(t, di, p, (float)v); value.Text = Text((float)v); ctx.NotifyChanged(); };
        knob.GestureBegin += () => e.BeginAutomationWrite(t, AutomationTarget.PluginParam, di, -1, pid);
        knob.GestureEnd += () => e.EndAutomationWrite(t, AutomationTarget.PluginParam, di, -1, pid);
        MidiLearn.Bind(knob, MidiTarget.PluginParam(t, di, p), name);
        ctx.AddDeviceRefresher(() =>
        {
            if (!live() || knob.Dragging) return;
            float v = e.PluginParamGet(t, di, p);
            if (Math.Abs(v - knob.Value) > 1e-4) { knob.Value = v; value.Text = Text(v); }
        });
        var cell = KnobCell(name, knob, value, CellW);
        ToolTip.SetTip(cell, name);
        return cell;
    }

    // A device section: card ground, hairline, radius 6.
    private static Border Section(Control child, double pad = 8) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Panel, Padding = new Thickness(pad, 6, pad, pad), ClipToBounds = true, Child = child,
    };
}
