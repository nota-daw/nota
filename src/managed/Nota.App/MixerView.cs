// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Mixer (mockup 1f): a dedicated strip mixer as a third Detail mode. One strip
// per track (+ returns + master): name with a 2px colour top, I/O rows, A/B
// sends, a pan knob, a 190px vertical fader with a stereo meter, a dB read-out
// and M/S/Arm. All strips ride the existing engine ops (vol/pan/mute/solo/send/
// meter); meters are pushed each UI tick by MainWindow. Crossfader is M7+.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class MixerView : UserControl
{
    private static readonly IBrush Card = NotaPalette.SurfaceCard;
    private static readonly IBrush MasterBg = NotaPalette.SurfaceRaised;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderInner = NotaPalette.SurfaceRaised;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush Danger = NotaPalette.Danger;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush TextDisabled = NotaPalette.TextDisabled;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;

    private readonly IAudioEngine _engine;
    private readonly StackPanel _row = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10) };
    private readonly List<(int trackId, MeterBar meter)> _meters = new();
    // Sends / I/O sections are collapsible across every strip (mockup 1f header
    // toggles); we keep the section borders so a toggle can hide them all at once.
    private readonly List<Control> _sendSections = new();
    private readonly List<Control> _ioSections = new();
    private bool _sendsVisible = true, _ioVisible = true;
    private float _masterVolume = 1.0f;

    public MixerView(IAudioEngine engine)
    {
        _engine = engine;
        var scroller = new ScrollViewer
        {
            Content = _row,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var dock = new DockPanel();
        var footer = Footer();
        DockPanel.SetDock(footer, Dock.Bottom);
        dock.Children.Add(footer);
        dock.Children.Add(scroller);
        Content = dock;
    }

    public void Refresh()
    {
        _row.Children.Clear();
        _meters.Clear();
        _sendSections.Clear();
        _ioSections.Clear();
        int returns = _engine.ReturnTrackCount;
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
        {
            if (!_engine.TryGetTrackInfo(i, out var ti)) continue;
            Color c = ArrangementView.TrackColorForIndex(ArrangementView.EffectiveColorIndex(_engine, ti.Id));
            _row.Children.Add(Strip(ti, c, returns));
        }
        _row.Children.Add(MasterStrip());
    }

    /// <summary>Pushes fresh meter readings to every strip (called at the UI clock).</summary>
    public void UpdateMeters()
    {
        foreach (var (id, meter) in _meters)
        {
            if (id < 0) meter.Push(_engine.MasterMeter());
            else if (_engine.TryGetTrackMeter(id, out var m)) meter.Push(m);
        }
    }

    // ---- strips -----------------------------------------------------------

    private Control Strip(NotaTrackInfo ti, Color color, int returns)
    {
        int id = ti.Id;
        bool isReturn = ti.IsReturn;
        var col = new StackPanel();

        col.Children.Add(Header(NameFor(ti), color, Card));

        string io1 = isReturn ? "Return in" : ti.IsInstrument ? "MIDI in" : "In 1";
        string io2 = isReturn ? "→ Master" : ti.IsInstrument ? "Monitor Auto" : "Monitor In";
        // Instrument tracks get a live "MIDI To" selector in place of the static I/O text.
        var ioRow = ti.IsInstrument ? MidiIoRow(id) : IoRow(io1, io2);
        ioRow.IsVisible = _ioVisible;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        if (!isReturn)
        {
            var sends = SendsRow(id, returns);
            sends.IsVisible = _sendsVisible;
            _sendSections.Add(sends);
            col.Children.Add(sends);
        }

        col.Children.Add(PanRow(id, ti.Pan));
        col.Children.Add(FaderRow(id, ti.Volume, color, out var db, out var meter));
        _meters.Add((id, meter));
        col.Children.Add(db);
        col.Children.Add(ButtonsRow(id, ti, isReturn));

        return new Border
        {
            Width = 154, Background = Card, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = col,
        };
    }

    private Control MasterStrip()
    {
        var col = new StackPanel();
        col.Children.Add(Header("Master", NotaPalette.AccentColor, MasterBg));
        var ioRow = IoRow("Sum bus", "→ Output");
        ioRow.IsVisible = _ioVisible;
        _ioSections.Add(ioRow);
        col.Children.Add(ioRow);

        Control db;
        var faderRow = FaderRow(-1, _masterVolume, NotaPalette.AccentColor, out db, out var meter);
        _meters.Add((-1, meter));
        col.Children.Add(faderRow);
        col.Children.Add(db);
        // Master has no sends / pan / arm; keep the strip visually aligned with a
        // spacer where the buttons row sits.
        col.Children.Add(new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0) });

        return new Border
        {
            Width = 154, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), ClipToBounds = true, Child = col,
        };
    }

    // ---- footer: section toggles + crossfader placeholder ----------------

    private Control Footer()
    {
        var sendsToggle = SectionToggle("Sends", _sendsVisible, on => { _sendsVisible = on; foreach (var s in _sendSections) s.IsVisible = on; });
        var ioToggle = SectionToggle("I/O", _ioVisible, on => { _ioVisible = on; foreach (var s in _ioSections) s.IsVisible = on; });
        var left = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { new TextBlock { Text = "Show", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center }, sendsToggle, ioToggle },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        grid.Children.Add(left);
        var xf = Crossfader();
        Grid.SetColumn(xf, 2);
        grid.Children.Add(xf);
        return new Border { Height = 34, Background = MasterBg, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(12, 0), Child = grid };
    }

    private Border SectionToggle(string label, bool initial, Action<bool> set)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 10, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Height = 20, Padding = new Thickness(8, 0), CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Child = t };
        void Paint() { b.Background = on ? Brass : MasterBg; b.BorderBrush = on ? Brass : BorderStrong; t.Foreground = on ? OnAccent : TextSecondary; }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    // Crossfader is not in the engine yet — a disabled A/B track with an M7+ badge.
    private Control Crossfader()
    {
        var track = new Border { Width = 120, Height = 4, Background = Sunken, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center };
        var knob = new Border { Width = 12, Height = 14, Background = NotaPalette.SurfaceHover, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var host = new Panel { Width = 120, Children = { track, knob } };
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, IsEnabled = false,
            Children =
            {
                new TextBlock { Text = "A", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
                host,
                new TextBlock { Text = "B", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        return new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
            Children = { row, new NaBadge { Kind = NaBadgeKind.Future } },
        };
    }

    private string NameFor(NotaTrackInfo ti)
        => ti.IsReturn ? $"Return {_engine.TrackReturnIndex(ti.Id) + 1}"
                       : (ti.IsInstrument ? "Inst " : "Audio ") + ti.Id;

    private Control Header(string name, Color topColor, IBrush bg)
    {
        var grid = new Grid { RowDefinitions = new RowDefinitions("2,*") };
        grid.Children.Add(new Border { Background = new SolidColorBrush(topColor), Height = 2, VerticalAlignment = VerticalAlignment.Top });
        var label = new TextBlock { Text = name, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 7, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetRow(label, 1);
        grid.Children.Add(label);
        return new Border { Height = 24, Background = bg, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = grid };
    }

    private Control IoRow(string io1, string io2) => new Border
    {
        Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
        Child = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = io1, FontSize = 8, Foreground = TextTertiary },
                new TextBlock { Text = io2, FontSize = 8, Foreground = TextTertiary },
            },
        },
    };

    // Instrument-track I/O row: a "MIDI In" selector (None, or another instrument track whose
    // MIDI this one receives) over the label. Writes/reads via the engine.
    private Control MidiIoRow(int id)
    {
        var cb = new ComboBox
        {
            FontSize = 8, Height = 16, MinHeight = 0, Padding = new Thickness(5, 0),
            HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
        };
        var sources = new List<int>();
        void Add(string label, int src)
        {
            cb.Items.Add(new ComboBoxItem { Content = new TextBlock { Text = label, FontSize = 8, TextTrimming = TextTrimming.CharacterEllipsis }, Padding = new Thickness(5, 1), MinHeight = 0 });
            sources.Add(src);
        }
        Add("None", -1);
        int n = _engine.TrackCount;
        for (int i = 0; i < n; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsInstrument && ti.Id != id)
                Add(_engine.GetTrackName(ti.Id) is { Length: > 0 } nm ? nm : $"Inst {ti.Id}", ti.Id);
        int cur = _engine.GetTrackMidiSource(id);
        int sel = sources.IndexOf(cur);
        cb.SelectedIndex = sel >= 0 ? sel : 0;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex >= 0 && cb.SelectedIndex < sources.Count)
                _engine.SetTrackMidiSource(id, sources[cb.SelectedIndex]);
        };
        return new Border
        {
            Height = 34, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 4),
            Child = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    new TextBlock { Text = "MIDI in", FontSize = 8, Foreground = TextTertiary },
                    cb,
                },
            },
        };
    }

    private Control SendsRow(int id, int returns)
    {
        var body = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        for (int bus = 0; bus < 2; bus++)
        {
            char letter = (char)('A' + bus);
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
            grid.Children.Add(new TextBlock { Text = letter.ToString(), FontSize = 8, Foreground = TextTertiary, Width = 8, VerticalAlignment = VerticalAlignment.Center });
            if (bus < returns)
            {
                var f = new MiniFader(_engine.GetTrackSend(id, bus), 1.0) { VerticalAlignment = VerticalAlignment.Center };
                int b = bus;
                f.ValueChanged += v => _engine.SetTrackSend(id, b, (float)v);
                Grid.SetColumn(f, 1); grid.Children.Add(f);
            }
            else
            {
                var flat = new Border { Height = 3, Background = Sunken, CornerRadius = new CornerRadius(2), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.5 };
                Grid.SetColumn(flat, 1); grid.Children.Add(flat);
            }
            body.Children.Add(grid);
        }
        return new Border { Height = 42, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(7, 5), Child = body };
    }

    private Control PanRow(int id, float pan)
    {
        var knob = new PanKnob(pan);
        knob.PanChanged += p => _engine.SetTrackPan(id, (float)p);
        if (id > 0)   // M9-C: record pan moves (master pan isn't an automation target)
        {
            knob.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            knob.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Pan, -1, -1, "");
            MidiLearn.Bind(knob, MidiTarget.TrackPan(id), "Track Pan");
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { knob, Mono("PAN", TextTertiary) } };
        return new Border { Height = 38, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 0, 0, 1), Child = row };
    }

    // Fader travel is 0..1.5 linear (VFader.Max), i.e. up to ~+3.5 dB; the dB
    // read-out is drag-editable and double-click-typeable (HANDOFF §4), kept in
    // two-way sync with the fader.
    private const double MaxLin = 1.5;
    private static readonly double MaxDb = AudioMath.LinToDb(MaxLin);
    private const double MinDb = -60.0;
    private static double LinToDb(double v) => v <= 1e-4 ? MinDb : Math.Clamp(AudioMath.LinToDb(v), MinDb, MaxDb);
    private static double DbToLin(double db) => db <= MinDb + 1e-6 ? 0.0 : Math.Clamp(AudioMath.DbToLin(db), 0, MaxLin);

    private Control FaderRow(int id, float vol, Color color, out Control db, out MeterBar meter)
    {
        var fader = new VFader(vol, color);
        var dbNum = new DragNumber(LinToDb(vol), MinDb, MaxDb, 0.3, "+0.0;-0.0;0.0", 8)
        {
            Width = 52, HorizontalAlignment = HorizontalAlignment.Center,
        };
        ToolTip.SetTip(dbNum, "Drag or double-click to set gain (dB)");
        void SetVolume(double v) { if (id < 0) { _masterVolume = (float)v; _engine.SetMasterVolume(_masterVolume); } else _engine.SetTrackVolume(id, (float)v); }
        fader.ValueChanged += v => { SetVolume(v); dbNum.Value = LinToDb(v); };
        dbNum.ValueChanged += d => { double v = DbToLin(d); SetVolume(v); fader.SetValueExternal(v); };
        if (id > 0)   // M9-C: record volume moves (master volume isn't a track target)
        {
            fader.GestureBegin += () => _engine.BeginAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
            fader.GestureEnd   += () => _engine.EndAutomationWrite(id, AutomationTarget.Volume, -1, -1, "");
        }
        MidiLearn.Bind(fader, id > 0 ? MidiTarget.TrackVolume(id) : MidiTarget.MasterVolume, id > 0 ? "Track Volume" : "Master Volume");
        db = dbNum;

        meter = new MeterBar { Width = 12 };
        var scaleGrid = new Grid { Width = 16, RowDefinitions = new RowDefinitions("*,*,*,*") };
        string[] marks = { "+6", "0", "-12", "-48" };
        for (int i = 0; i < 4; i++)
        {
            var t = new TextBlock { Text = marks[i], FontSize = 7, Foreground = TextDisabled, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = i == 0 ? VerticalAlignment.Top : i == 3 ? VerticalAlignment.Bottom : VerticalAlignment.Center };
            t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
            Grid.SetRow(t, i);
            scaleGrid.Children.Add(t);
        }

        var grid = new Grid { Height = 190, ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(7, 8) };
        grid.Children.Add(scaleGrid);
        Grid.SetColumn(fader, 1); grid.Children.Add(fader);
        Grid.SetColumn(meter, 2); meter.Margin = new Thickness(4, 0, 0, 0); grid.Children.Add(meter);
        return grid;
    }

    private Control ButtonsRow(int id, NotaTrackInfo ti, bool isReturn)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var mute = Toggle("M", false, ti.Muted != 0, v => _engine.SetTrackMute(id, v));
        var solo = Toggle("S", false, ti.Soloed != 0, v => _engine.SetTrackSolo(id, v));
        MidiLearn.Bind(mute, MidiTarget.TrackMute(id), "Mute");
        MidiLearn.Bind(solo, MidiTarget.TrackSolo(id), "Solo");
        row.Children.Add(mute);
        row.Children.Add(solo);
        if (!isReturn) row.Children.Add(Toggle("●", true, ti.Armed != 0, v => _engine.SetTrackArmed(id, v)));
        return new Border { Height = 28, BorderBrush = BorderInner, BorderThickness = new Thickness(0, 1, 0, 0), Child = row };
    }

    private Border Toggle(string label, bool danger, bool initial, Action<bool> set)
    {
        bool on = initial;
        var t = new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 20, Height = 17, CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = t };
        void Paint()
        {
            var accent = danger ? Danger : Brass;
            b.Background = on ? accent : MasterBg;
            b.BorderBrush = on ? accent : BorderStrong;
            t.Foreground = on ? OnAccent : TextSecondary;
        }
        b.PointerPressed += (_, e) => { e.Handled = true; on = !on; set(on); Paint(); };
        Paint();
        return b;
    }

    private TextBlock Mono(string text, IBrush fg)
    {
        var t = new TextBlock { Text = text, FontSize = 8, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

}
