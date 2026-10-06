// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Session view — the mixer under each column, in three sections the toolbar shows or hides:
// I/O (input, monitor, output), Sends (one row per return bus) and Mixer (pan, volume,
// meter, mute / solo / arm). The scene rail carries the row labels.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class SessionView
{
    private const double IoRowH = 22;
    private const double FaderRowH = 18;
    private const double MeterRowH = 6;
    private const double ButtonRowH = 22;
    private const double SectionTop = 6;

    /// <summary>Master fader plumbing (the master level lives on the transport view-model).</summary>
    public Func<float>? GetMasterVolume { get; set; }
    public Action<float>? SetMasterVolume { get; set; }

    private sealed class MixerParts
    {
        public required Control Root { get; init; }
        public required Action UpdateMeter { get; init; }
    }

    private int ReturnRows => Math.Min(_engine.ReturnTrackCount, 4);

    private List<(int Id, string Name)> Returns()
    {
        var list = new List<(int, string)>();
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var ti) && ti.IsReturn) list.Add((ti.Id, TrackNames.Of(_engine, ti)));
        return list;
    }

    private Control MixerRailLabels()
    {
        var stack = new StackPanel { Spacing = Gap };
        TextBlock Label(string text) => new()
        {
            Text = text, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = NotaPalette.TextDisabled,
            Margin = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        Border Row(double h, string text) => new() { Height = h, Child = Label(text) };
        if (_showIO)
            stack.Children.Add(Section(Row(IoRowH, "INPUT"), Row(IoRowH, "MONITOR"), Row(IoRowH, "OUTPUT")));
        if (_showSends && ReturnRows > 0)
        {
            var rows = Returns().Take(ReturnRows).Select((r, i) => (Control)Row(FaderRowH, $"SEND {(char)('A' + i)} · {r.Name.ToUpperInvariant()}")).ToArray();
            stack.Children.Add(Section(rows));
        }
        if (_showMixer)
            stack.Children.Add(Section(Row(FaderRowH, "PAN"), Row(FaderRowH, "VOLUME"), new Border { Height = MeterRowH }, new Border { Height = ButtonRowH }));
        return stack;
    }

    private static StackPanel Section(params Control[] rows)
    {
        var s = new StackPanel { Spacing = Gap, Margin = new Thickness(0, SectionTop, 0, 0) };
        foreach (var r in rows) s.Children.Add(r);
        return s;
    }

    private MixerParts BuildMixer(Column c)
    {
        var root = new StackPanel { Spacing = Gap };
        bool hasTrack = c.Kind != ColKind.Master;
        NotaTrackInfo ti = default;
        if (hasTrack)
            for (int i = 0; i < _engine.TrackCount; i++)
                if (_engine.TryGetTrackInfo(i, out var t) && t.Id == c.TrackId) { ti = t; break; }
        if (c.Kind == ColKind.Track && ti.Armed != 0) _armed.Add(c.TrackId); else _armed.Remove(c.TrackId);

        if (_showIO) root.Children.Add(Section(InputRow(c), MonitorRow(c), OutputRow(c)));

        if (_showSends && ReturnRows > 0)
        {
            var rows = new List<Control>();
            for (int b = 0; b < ReturnRows; b++)
            {
                if (c.Kind is ColKind.Track or ColKind.Group)
                {
                    int bus = b, id = c.TrackId;
                    var f = new MiniFader(_engine.GetTrackSend(id, bus), 1.0) { Default = 0 };
                    var read = ReadoutText();
                    void Show() => read.Text = f.Value <= 0.005 ? "–" : NotaNum.Str(f.Value * 100, "0");
                    f.ValueChanged += v => { _engine.SetTrackSend(id, bus, (float)v); Show(); };
                    Show();
                    rows.Add(FaderRow(f, read));
                }
                else rows.Add(new Border { Height = FaderRowH });
            }
            root.Children.Add(Section(rows.ToArray()));
        }

        Action meter = () => { };
        if (_showMixer)
        {
            Control panRow;
            if (hasTrack)
            {
                int id = c.TrackId;
                var pan = new PanBar(ti.Pan) { VerticalAlignment = VerticalAlignment.Center };   // draws its own C / 12L readout
                pan.PanChanged += p => _engine.SetTrackPan(id, (float)p);
                panRow = FaderRow(pan, ReadoutText());
            }
            else panRow = new Border { Height = FaderRowH };

            double vol0 = hasTrack ? ti.Volume : GetMasterVolume?.Invoke() ?? 1f;
            var vol = new MiniFader(vol0, 1.5) { Default = 1.0 };
            var db = ReadoutText();
            void ShowDb() => db.Text = vol.Value <= 1e-4 ? "−∞" : NotaNum.Db(AudioMath.LinToDb(vol.Value), signed: true);
            vol.ValueChanged += v =>
            {
                if (hasTrack) _engine.SetTrackVolume(c.TrackId, (float)v);
                else SetMasterVolume?.Invoke((float)v);
                ShowDb();
            };
            ShowDb();

            var mL = new MeterLine(); var mR = new MeterLine();
            var meters = new StackPanel { Height = MeterRowH, Spacing = 1, Margin = new Thickness(4, 0), VerticalAlignment = VerticalAlignment.Center, Children = { mL, mR } };
            meter = () =>
            {
                NotaMeter m = default;
                if (hasTrack) { if (!_engine.TryGetTrackMeter(c.TrackId, out m)) m = default; }
                else m = _engine.MasterMeter();
                mL.Level = m.PeakL; mR.Level = m.PeakR;
            };

            var btns = new UniformGrid { Rows = 1, Height = ButtonRowH, Margin = new Thickness(4, 0) };
            if (hasTrack)
            {
                int id = c.TrackId;
                btns.Children.Add(MixToggle("M", ti.Muted != 0, false, v => _engine.SetTrackMute(id, v)));
                btns.Children.Add(MixToggle("S", ti.Soloed != 0, false, v => _engine.SetTrackSolo(id, v)));
                if (c.Kind == ColKind.Track)
                    btns.Children.Add(MixToggle(null, ti.Armed != 0, true, v =>
                    {
                        _engine.SetTrackArmed(id, v);
                        if (v) _armed.Add(id); else _armed.Remove(id);
                        UpdateStates();
                    }));
            }
            root.Children.Add(Section(panRow, FaderRow(vol, db), meters, btns));
        }
        return new MixerParts { Root = root, UpdateMeter = meter };
    }

    private static TextBlock ReadoutText()
    {
        var t = new TextBlock { FontSize = 9, Foreground = NotaPalette.TextMuted, Width = 30, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static Control FaderRow(Control fader, TextBlock readout)
    {
        fader.VerticalAlignment = VerticalAlignment.Center;
        var g = new Grid { Height = FaderRowH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(4, 0), ColumnSpacing = 6 };
        g.Children.Add(fader);
        Grid.SetColumn(readout, 1);
        g.Children.Add(readout);
        return g;
    }

    // M / S / record-arm: a 20px chip. Engaged mute is a raised neutral, solo the brass fill,
    // arm the record red with its pale disc (DESIGN.md § Controls).
    private static Control MixToggle(string? label, bool initial, bool arm, Action<bool> set)
    {
        bool on = initial;
        var text = new TextBlock { Text = label ?? "", FontSize = 10, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var disc = new Glyph(GlyphKind.Record, 7);
        var b = new Border
        {
            Height = 20, Margin = new Thickness(1.5, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
            BorderBrush = NotaPalette.BorderDefault, Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
            Child = arm ? disc : text,
        };
        void Paint()
        {
            if (arm)
            {
                b.Background = on ? NotaPalette.Record : NotaPalette.SurfaceCard;
                b.BorderBrush = on ? NotaPalette.Record : NotaPalette.BorderDefault;
                disc.Foreground = on ? NotaPalette.RecordInk : NotaPalette.TextDisabled;
                return;
            }
            bool solo = label == "S";
            b.Background = on ? (solo ? NotaPalette.Accent : NotaPalette.BorderStrong) : NotaPalette.SurfaceCard;
            text.Foreground = on ? (solo ? NotaPalette.TextOnAccent : NotaPalette.TextPrimary) : NotaPalette.TextMuted;
        }
        ToolTip.SetTip(b, arm ? "Arm for recording" : label == "M" ? "Mute" : "Solo");
        b.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            on = !on; set(on); Paint();
        };
        Paint();
        return b;
    }

    // ---- I/O rows --------------------------------------------------------------------

    private Control InputRow(Column c)
    {
        if (c.Kind != ColKind.Track) return new Border { Height = IoRowH };
        var names = new List<(int Id, string Name, bool Instrument)>();
        for (int i = 0; i < _engine.TrackCount; i++)
            if (_engine.TryGetTrackInfo(i, out var t) && t.Id != c.TrackId && !t.IsGroup)
                names.Add((t.Id, TrackNames.Of(_engine, t), t.IsInstrument));

        var items = new List<(int Value, string Label)>();
        int cur;
        if (c.IsInstrument)
        {
            items.Add((-1, "All Ins"));
            foreach (var n in names.Where(n => n.Instrument)) items.Add((n.Id, n.Name));
            cur = _engine.GetTrackMidiSource(c.TrackId);
        }
        else
        {
            items.Add((0, "Ext In"));
            items.Add((-1, "Master"));
            foreach (var n in names) items.Add((n.Id, n.Name));
            cur = _engine.GetTrackRecordInput(c.TrackId);
        }
        var cb = new ComboBox { Height = IoRowH, FontSize = 10, Padding = new Thickness(6, 0), HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 0) };
        foreach (var it in items) cb.Items.Add(new ComboBoxItem { Content = it.Label, FontSize = 10 });
        int sel = items.FindIndex(it => it.Value == cur);
        cb.SelectedIndex = sel >= 0 ? sel : 0;
        int id = c.TrackId;
        bool inst = c.IsInstrument;
        cb.SelectionChanged += (_, _) =>
        {
            if (cb.SelectedIndex < 0) return;
            int v = items[cb.SelectedIndex].Value;
            if (inst) _engine.SetTrackMidiSource(id, v); else _engine.SetTrackRecordInput(id, v);
        };
        ToolTip.SetTip(cb, inst ? "MIDI input: every input, or another instrument track" : "Record input");
        return cb;
    }

    private Control MonitorRow(Column c)
    {
        if (c.Kind != ColKind.Track || !c.IsAudio) return new Border { Height = IoRowH };
        int id = c.TrackId;
        bool on = _engine.GetTrackMonitor(id);
        var seg = Segmented(new[] { "In", "Auto" }, on ? 0 : 1, i => _engine.SetTrackMonitor(id, i == 0), compact: true);
        seg.Margin = new Thickness(4, 0);
        ToolTip.SetTip(seg, "In: hear the input live through this track · Auto: only while recording");
        return seg;
    }

    private Control OutputRow(Column c)
    {
        string text = c.Kind == ColKind.Master ? "Ext Out 1/2" : "Master";
        if (c.GroupId > 0 && ColumnFor(c.GroupId) is { } g) text = g.Name;
        else if (c.GroupId > 0)
            for (int i = 0; i < _engine.TrackCount; i++)
                if (_engine.TryGetTrackInfo(i, out var t) && t.Id == c.GroupId) { text = TrackNames.Of(_engine, t); break; }
        var b = new Border
        {
            Height = IoRowH, Margin = new Thickness(4, 0), Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge,
            Background = NotaPalette.SurfaceCard, BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(1),
            Child = new TextBlock { Text = text, FontSize = 10, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        ToolTip.SetTip(b, "Output — group a track to route it through the group");
        return b;
    }

    /// <summary>A segmented switch (Border.segmented + ToggleButton.seg); exactly one is lit.</summary>
    private static Border Segmented(IReadOnlyList<string> labels, int selected, Action<int> pick, bool compact = false, bool mono = false)
    {
        var row = new UniformGrid { Rows = 1 };
        var buttons = new List<ToggleButton>();
        for (int i = 0; i < labels.Count; i++)
        {
            int idx = i;
            var tb = new ToggleButton
            {
                Content = labels[i], Classes = { "seg" }, IsChecked = i == selected, HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Center, Padding = new Thickness(compact ? 4 : 8, 0),
                FontSize = compact ? 9 : 10, Height = compact ? 16 : 20, MinWidth = 0,
            };
            if (mono) tb.BindResource(ToggleButton.FontFamilyProperty, "Font.Mono");
            tb.Click += (_, _) =>
            {
                foreach (var b in buttons) b.IsChecked = ReferenceEquals(b, tb);
                pick(idx);
            };
            buttons.Add(tb);
            row.Children.Add(tb);
        }
        return new Border { Classes = { "segmented" }, Padding = new Thickness(2), Child = row };
    }

    /// <summary>A thin horizontal peak meter line (Signal green), 2px tall.</summary>
    private sealed class MeterLine : Control
    {
        private double _level;
        public MeterLine() { Height = 2; }
        public double Level
        {
            set { double v = Math.Clamp(value, 0, 1); if (Math.Abs(v - _level) < 0.004) return; _level = v; InvalidateVisual(); }
        }
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width;
            ctx.DrawRectangle(NotaPalette.SurfaceRaised, null, new Rect(0, 0, w, 2), 1, 1);
            if (_level > 0) ctx.DrawRectangle(NotaPalette.Success, null, new Rect(0, 0, w * _level, 2), 1, 1);
        }
    }
}
