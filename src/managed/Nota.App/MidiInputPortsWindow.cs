// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// "Ports and channels…" for an instrument track's MIDI input (specs/115 US2): each MIDI port the
// track could listen to, with sixteen channel toggles — any set, not necessarily contiguous —
// so two controllers can play one instrument while the rest stay free for other tracks. A port
// the track lists that isn't connected keeps its row under its saved name, marked missing, and
// keeps its channels (FR-006). Edits apply live, like the other input pickers.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Application.Midi;

namespace Nota.App;

public sealed class MidiInputPortsWindow : NotaWindow
{
    private readonly IAudioEngine _engine;
    private readonly int _trackId;
    private readonly Action? _edited;
    private readonly StackPanel _rows = new() { Spacing = 6 };

    public MidiInputPortsWindow(IAudioEngine engine, int trackId, string trackName, Action? edited = null)
    {
        _engine = engine;
        _trackId = trackId;
        _edited = edited;
        Title = Loc.F("midi_input.ports.title", trackName);
        Width = 620;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        var all = new Button { Content = Loc.T("midi_input.all_inputs"), Classes = { "ghost" } };
        all.Click += (_, _) => { Apply(MidiInputList.Everything); Rebuild(); };
        var close = new Button { Content = Loc.T("common.close"), Classes = { "primary" } };
        close.Click += (_, _) => Close();

        SetBody(new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 14,
            Children =
            {
                new TextBlock { Text = Loc.T("midi_input.ports.caption"), Classes = { "Caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 420, Content = _rows },
                new DockPanel
                {
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { all } },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
                    },
                },
            },
        });
        Rebuild();
    }

    // Every enabled connected input, then a listed port that isn't connected (by its saved name).
    private List<(string Uid, string Name, RouteAvailability State)> Ports(MidiInputList cur)
    {
        var ports = _engine.MidiPorts();
        var rows = new List<(string, string, RouteAvailability)>();
        IReadOnlyList<MidiDevice> devices;
        try { devices = App.Services.GetService<IMidiDeviceService>()?.InputDevices() ?? Array.Empty<MidiDevice>(); }
        catch (InvalidOperationException) { devices = Array.Empty<MidiDevice>(); }
        foreach (var d in devices)
            if (_engine.IsMidiInputEnabled(d.Uid) || cur.Routes.Any(r => r.Uid == d.Uid))
                rows.Add((d.Uid, d.Name.Length > 0 ? d.Name : d.Uid, MidiRouteResolver.ResolveInput(ports, d.Uid)));
        foreach (var p in ports.Where(p => p.Uid.StartsWith("test:", StringComparison.Ordinal) && rows.All(r => r.Item1 != p.Uid)))
            rows.Add((p.Uid, p.Name, RouteAvailability.Available));
        foreach (var r in cur.Routes.Where(r => rows.All(x => x.Item1 != r.Uid)))
            rows.Add((r.Uid, r.Name.Length > 0 ? r.Name : r.Uid, RouteAvailability.Missing));
        return rows;
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var cur = _engine.GetTrackMidiInputs(_trackId);
        var ports = Ports(cur);
        if (ports.Count == 0)
        {
            _rows.Children.Add(new TextBlock { Text = Loc.T("midi_input.ports.none"), Foreground = NotaPalette.TextTertiary, FontSize = 10 });
            return;
        }
        foreach (var (uid, name, state) in ports) _rows.Children.Add(Row(cur, uid, name, state));
    }

    private Control Row(MidiInputList cur, string uid, string name, RouteAvailability state)
    {
        ushort mask = cur.All ? (ushort)0 : cur.Routes.FirstOrDefault(r => r.Uid == uid).Channels;
        string label = state switch
        {
            RouteAvailability.Missing => Loc.F("midi_input.not_connected", name),
            RouteAvailability.OverLimit => Loc.F("midi_input.ports.over_limit", name),
            RouteAvailability.Ambiguous => Loc.F("midi_input.ports.ambiguous", name),
            _ => name,
        };
        var title = new TextBlock
        {
            Text = label, FontSize = 11, Width = 150, VerticalAlignment = VerticalAlignment.Center,
            Foreground = state == RouteAvailability.Available ? NotaPalette.TextPrimary : NotaPalette.TextTertiary,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        var cells = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        for (int c = 1; c <= 16; c++)
        {
            int ch = c;
            var t = new ToggleButton
            {
                Content = ch.ToString(System.Globalization.CultureInfo.CurrentCulture), Classes = { "seg" },
                IsChecked = ((mask >> (ch - 1)) & 1) != 0, MinWidth = 24, Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            ToolTip.SetTip(t, Loc.F("midi_input.channel_n", ch));
            t.IsCheckedChanged += (_, _) => Toggle(uid, name, ch, t.IsChecked == true);
            cells.Children.Add(t);
        }
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { title, cells } };
    }

    // One channel of one port on or off. Leaving no channel anywhere goes back to All inputs.
    private void Toggle(string uid, string name, int channel, bool on)
    {
        var cur = _engine.GetTrackMidiInputs(_trackId);
        var routes = cur.All ? new List<MidiInputRoute>() : cur.Routes.ToList();
        int at = routes.FindIndex(r => r.Uid == uid);
        ushort bit = (ushort)(1 << (channel - 1));
        ushort m = (ushort)((at >= 0 ? routes[at].Channels : 0) & ~bit);
        if (on) m |= bit;
        if (at >= 0)
        {
            if (m == 0) routes.RemoveAt(at);
            else routes[at] = routes[at] with { Channels = m };
        }
        else if (m != 0) routes.Add(new MidiInputRoute(uid, m, name));
        Apply(routes.Count == 0 ? MidiInputList.Everything : new MidiInputList(false, MidiInputRoute.AllChannels, routes));
    }

    private void Apply(MidiInputList list)
    {
        _engine.SetTrackMidiInputs(_trackId, list);
        _edited?.Invoke();
    }
}
