// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI routes (specs/115 US3): a side panel beside the Mixer's strips that lists every instrument
// track's MIDI routes in one place — what it listens to (every input, or ports with their
// channels), the track it takes MIDI from, and where an External MIDI track sends — with every
// port, role or track that isn't there marked. Filter by a source or a destination; click a row
// to pick its track. Read-only (FR-021): opening, filtering or closing it changes nothing.
// A flat panel of the shell (DESIGN.md): rows on the panel, split by rules, no depth.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Application.Midi;

namespace Nota.App;

public sealed class MidiRoutesPanel : UserControl
{
    private readonly IAudioEngine _engine;
    private readonly StackPanel _rows = new();
    private readonly ComboBox _source = new() { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _dest = new() { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly List<string?> _sourceKeys = new(), _destKeys = new();
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };
    private string _shown = "";
    private bool _filling;

    /// <summary>A row was clicked: pick its track.</summary>
    public event Action<int>? TrackPicked;

    public MidiRoutesPanel(IAudioEngine engine)
    {
        _engine = engine;
        Width = 320;
        var title = new TextBlock
        {
            Text = Loc.T("midi_routes.title"), FontSize = NotaType.Body, FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary, Margin = new Thickness(12, 10, 12, 6),
        };
        _source.SelectionChanged += (_, _) => { if (!_filling) Refresh(force: true); };
        _dest.SelectionChanged += (_, _) => { if (!_filling) Refresh(force: true); };
        var filters = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, Margin = new Thickness(12, 0, 12, 8),
            Children = { _source, _dest },
        };
        Grid.SetColumn(_dest, 1);
        var head = new StackPanel { Children = { title, filters } };
        var list = new ScrollViewer { Content = _rows, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        var dock = new DockPanel();
        DockPanel.SetDock(head, Dock.Top);
        dock.Children.Add(head);
        dock.Children.Add(list);
        Content = new Border
        {
            Background = NotaPalette.Panel, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1, 0, 0, 0),
            Child = dock,
        };
        _tick.Tick += (_, _) => Refresh();
        AttachedToVisualTree += (_, _) => { Refresh(force: true); _tick.Start(); };
        DetachedFromVisualTree += (_, _) => _tick.Stop();
    }

    /// <summary>Re-reads the routes; redraws only when something changed (names, ports, routes).</summary>
    public void Refresh(bool force = false)
    {
        FillFilters();
        var rows = MidiRoutesQuery.Rows(_engine, Key(_source, _sourceKeys), Key(_dest, _destKeys));
        string sig = string.Join("|", rows.Select(r => $"{r.TrackId}:{r.TrackName}:{r.TrackColorIndex}:{r.AllInputs}:{r.AllInputsChannels}:" +
            string.Join(",", r.Inputs.Select(i => $"{i.Key}/{i.Name}/{i.Detail}/{i.Availability}")) + $":{r.MidiFrom}:{r.InstrumentName}:{r.Destination}"));
        if (!force && sig == _shown) return;
        _shown = sig;
        _rows.Children.Clear();
        if (rows.Count == 0)
        {
            _rows.Children.Add(new TextBlock
            {
                Text = Loc.T("midi_routes.empty"), FontSize = NotaType.Caption, Foreground = NotaPalette.TextTertiary,
                HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(12, 16),
            });
            return;
        }
        foreach (var r in rows) _rows.Children.Add(Row(r));
    }

    private static string? Key(ComboBox c, List<string?> keys) => c.SelectedIndex > 0 && c.SelectedIndex < keys.Count ? keys[c.SelectedIndex] : null;

    // Sources: every port any track lists, then every track another takes MIDI from. Destinations:
    // the outputs and roles External MIDI tracks send to.
    private void FillFilters()
    {
        var all = MidiRoutesQuery.Rows(_engine);
        var sources = new List<(string Key, string Name)>();
        foreach (var r in all)
        {
            foreach (var i in r.Inputs) if (sources.All(s => s.Key != i.Key)) sources.Add((i.Key, i.Name));
            if (r.MidiFrom is { } f && sources.All(s => s.Key != f.Key)) sources.Add((f.Key, f.Name.Length > 0 ? f.Name : Loc.T("midi_input.from_removed_track")));
        }
        var dests = new List<(string Key, string Name)>();
        foreach (var r in all)
            if (r.Destination is { Key.Length: > 0 } d && dests.All(x => x.Key != d.Key)) dests.Add((d.Key, d.Name));
        Fill(_source, _sourceKeys, Loc.T("midi_routes.any_source"), sources);
        Fill(_dest, _destKeys, Loc.T("midi_routes.any_destination"), dests);
    }

    private void Fill(ComboBox c, List<string?> keys, string any, List<(string Key, string Name)> items)
    {
        var want = new List<string?> { null };
        want.AddRange(items.Select(i => (string?)i.Key));
        if (want.SequenceEqual(keys)) return;
        string? cur = Key(c, keys);
        _filling = true;
        keys.Clear();
        keys.AddRange(want);
        c.Items.Clear();
        c.Items.Add(any);
        foreach (var i in items) c.Items.Add(i.Name);
        int at = cur is null ? 0 : keys.IndexOf(cur);
        c.SelectedIndex = at < 0 ? 0 : at;
        _filling = false;
    }

    private Control Row(MidiRouteRow r)
    {
        var chip = new Border { Width = 4, CornerRadius = NotaRadius.Fine, Background = ArrangementView.TrackBrush(r.TrackColorIndex), Margin = new Thickness(0, 2, 8, 2) };
        var name = new TextBlock
        {
            Text = r.TrackName, FontSize = NotaType.Body, Foreground = NotaPalette.TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var lines = new StackPanel { Spacing = 2, Children = { name } };

        // In: every input, or each port with its channels.
        if (r.AllInputs)
            lines.Children.Add(Line(Loc.T("midi_routes.in"), r.AllInputsChannels.Length > 0
                ? Loc.F("midi_routes.all_inputs_channels", r.AllInputsChannels) : Loc.T("midi_input.all_inputs"), RouteAvailability.Available));
        foreach (var i in r.Inputs)
            lines.Children.Add(Line(Loc.T("midi_routes.in"), i.Detail.Length > 0 ? Loc.F("midi_input.ports.device_channels", i.Name, i.Detail) : i.Name, i.Availability));
        if (r.MidiFrom is { } f)
            lines.Children.Add(Line(Loc.T("midi_routes.from"), f.Name.Length > 0 ? f.Name : Loc.T("midi_input.from_removed_track"), f.Availability));
        // Out: the track's own instrument, or External MIDI's port or role and channel.
        if (r.Destination is { } d)
        {
            string ch = d.Detail == "keep" ? Loc.T("device.external_midi.keep_channels") : Loc.F("device.external_midi.channel_n", d.Detail);
            string to = d.Name.Length > 0 ? Loc.F("midi_routes.port_channels", d.Name, ch) : Loc.T("device.external_midi.no_output");
            lines.Children.Add(Line(Loc.T("midi_routes.out"), to, d.Availability));
        }
        else lines.Children.Add(Line(Loc.T("midi_routes.out"), r.InstrumentName, RouteAvailability.Available));

        var grid = new DockPanel { Children = { chip, lines } };
        DockPanel.SetDock(chip, Dock.Left);
        var row = new Border
        {
            Padding = new Thickness(12, 6), BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(0, 0, 0, 1),
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = grid,
        };
        row.PointerEntered += (_, _) => row.Background = NotaPalette.Well;
        row.PointerExited += (_, _) => row.Background = Brushes.Transparent;
        row.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            TrackPicked?.Invoke(r.TrackId);
        };
        return row;
    }

    // "In  Keystep · Ch 3" — a missing endpoint in ink 3 with its state after it.
    private static Control Line(string label, string text, RouteAvailability a)
    {
        string state = a switch
        {
            RouteAvailability.Missing => Loc.T("midi_routes.missing"),
            RouteAvailability.Ambiguous => Loc.T("midi_routes.ambiguous"),
            RouteAvailability.Unbound => Loc.T("midi_routes.unbound"),
            RouteAvailability.OverLimit => Loc.T("midi_routes.over_limit"),
            _ => "",
        };
        var l = new TextBlock { Text = label, FontSize = NotaType.Caption, Foreground = NotaPalette.TextTertiary, Width = 34 };
        var v = new TextBlock
        {
            Text = state.Length > 0 ? Loc.F("midi_routes.with_state", text, state) : text, FontSize = NotaType.Caption,
            Foreground = a == RouteAvailability.Available ? NotaPalette.TextSecondary : NotaPalette.TextTertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var p = new DockPanel { Children = { l, v } };
        DockPanel.SetDock(l, Dock.Left);
        return p;
    }
}
