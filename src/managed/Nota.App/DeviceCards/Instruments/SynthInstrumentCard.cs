// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Synth editor (instrument kind 0). The synth's three
// sections are tabs inside the 700 × 260 card, so each one gets its graph full size with
// its knobs under it instead of three cramped bands:
//
//   Osc     waveform over eight cycles · WAVE · PULSE W · DETUNE · OCTAVE · UNISON
//   Env     the amplitude ADSR (drag its breakpoints) · A / D / S / R · VEL → VOL
//   Filter  the response curve (drag for cutoff / reso) · TYPE · CUTOFF · RESO · ENV
//
// A 186px rail stays put on every tab — voice mode, volume, pan, glide, unison spread,
// velocity tracking and the track's meter — over a status strip that reads the patch back
// in words. Brass is the parameter itself; teal is modulation (detune, env → cutoff).

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

internal sealed class SynthInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "SUBTRACTIVE";

    private const double RailW = 186, TabH = 20, StatusH = 18, RowH = 60;

    private static readonly string[] WaveNames = { "Saw", "Square", "Triangle", "Sine" };
    private static readonly string[] FilterNames = { "Off", "LP", "HP", "BP" };
    private static readonly string[] UnisonNames = { "1", "2", "4", "7" };
    private static readonly int[] UnisonCounts = { 1, 2, 4, 7 };
    private static readonly string[] VoiceNames = { "Poly 16", "Mono", "Legato" };
    private static readonly string[] TabNames = { "Osc", "Env", "Filter" };
    private static readonly string[] TabHints =
    {
        "wave · pulse width · detune",
        "a · d · s · r",
        "type · cutoff · reso · env",
    };

    // Engine's perceptual map (Synth.h): lo * (hi/lo)^v.
    private static double ExpMap(float v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0f, 1f));
    private static string Secs(float v, double lo, double hi) => NotaNum.Time(ExpMap(v, lo, hi));
    private static string Db(float v) => NotaNum.Db(AudioMath.LinToDb(v));
    private static string KHz(float v) => NotaNum.Hz(ExpMap(v, 20, 18000));
    private static string Cents(float v) => NotaNum.Unit((v - 0.5f) * 100, "+0;−0;0", "c");
    private static string Octaves(float v) => ((int)Math.Round((v - 0.5f) * 4)).ToString("+0;−0;0", NotaNum.Culture);
    private static string Glide(float v) => v <= 1e-4f ? NotaNum.Unit(0, "0", "ms") : NotaNum.Time(v * v * 2.0);
    private static string Bipolar(float v) => NotaNum.Unit((v - 0.5f) * 200, "+0;−0;0", "%");
    private static string PanText(float v)
    {
        int p = (int)Math.Round((v - 0.5f) * 200);
        return p == 0 ? "C" : p < 0 ? $"{-p} L" : $"{p} R";
    }

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        int Sel(string id, int n) => Math.Clamp((int)Math.Round(G(id) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();          // everything that re-reads a param
        int tab = 0;                                // the open tab: 0 Osc · 1 Env · 2 Filter
        var bodies = new (Control Graph, Control Row)[TabNames.Length];
        var osc = new SynthViz(SynthViz.K.Osc, engine, track, idx);
        var env = new SynthViz(SynthViz.K.Adsr, engine, track, idx);
        var flt = new SynthViz(SynthViz.K.Filter, engine, track, idx);
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        meta.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

        void Refresh()
        {
            osc.Refresh(); env.Refresh(); flt.Refresh();
            foreach (var a in readouts) a();
            status.Text = Summary();
            meta.Text = Meta();
        }
        ctx.SetInstLiveViz(Refresh);
        env.Changed += Refresh; flt.Changed += Refresh;

        // ---- shared builders --------------------------------------------------
        Control Knob(string id, string name, Func<float, string> fmt, double size = 34, double cellW = 46, IBrush? arc = null)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, size, cellW, arc);

        // A param-backed segment strip. `values` are the normalized stops it writes.
        Control Chips(string id, string[] names)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // A caps caption over a control, left-aligned — the almanac's label-above form.
        Control Capped(string label, Control c) => new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock
                {
                    Text = label, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
                    LetterSpacing = NotaType.KnobLabelTracking, Foreground = TextTertiary,
                },
                c,
            },
        };

        // The row of controls under a tab's graph.
        Control Row(params Control[] cells)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cells) sp.Children.Add(c);
            return new Border
            {
                Height = RowH, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 4, 0, 0), Child = sp,
            };
        }

        // ---- Osc tab ----------------------------------------------------------
        var pwKnobCell = Knob("pulsewidth", "Pulse W", v => NotaNum.Pct(0.05 + v * 0.90));
        // Pulse width only shapes the square — dim it (never hide it) on the other shapes.
        var pwKnob = FindKnob(pwKnobCell);
        void SyncPw() { if (pwKnob is not null) pwKnob.IsDim = Sel("wave", 4) != 1; }
        readouts.Add(SyncPw);

        var oscRow = Row(
            Capped("WAVE", Chips("wave", WaveNames)),
            pwKnobCell,
            Knob("detune", "Detune", Cents, arc: Teal),
            Knob("octave", "Octave", Octaves),
            Capped("UNISON", Chips("unison", UnisonNames)));

        // ---- Env tab ----------------------------------------------------------
        // The switch turns velocity tracking off and back on; the rail knob is the amount.
        float lastVel = Math.Max(0.05f, G("velamp"));
        var velSwitch = Switch("Vel → Vol",
            () => G("velamp") > 1e-3f,
            () => { if (G("velamp") > 1e-3f) { lastVel = G("velamp"); SetP("velamp", 0f); } else SetP("velamp", lastVel); Refresh(); },
            out var velSync,
            liveLabel: () => G("velamp") > 1e-3f ? $"Vel → Vol  {NotaNum.Pct(G("velamp"))}" : "Vel → Vol  off");
        readouts.Add(velSync);
        if (I("velamp") is var vi and >= 0) MidiLearn.Bind(velSwitch, MidiTarget.PluginParam(track, -1, vi), "Vel → Vol");

        var envRow = Row(
            Knob("attack", "Attack", v => Secs(v, 0.001, 2.0)),
            Knob("decay", "Decay", v => Secs(v, 0.002, 2.0)),
            Knob("sustain", "Sustain", Db, size: 44, cellW: 52),
            Knob("release", "Release", v => Secs(v, 0.002, 3.0)),
            velSwitch);

        // ---- Filter tab -------------------------------------------------------
        var cutCell = Knob("cutoff", "Cutoff", KHz, size: 44, cellW: 52);
        var resoCell = Knob("resonance", "Reso", v => v.ToString("0.00", NotaNum.Culture));
        var envCell = Knob("filenv", "Env → Cutoff", Bipolar, cellW: 66, arc: Teal);
        var cutKnob = FindKnob(cutCell); var resoKnob = FindKnob(resoCell); var envKnob = FindKnob(envCell);
        // Off bypasses the filter entirely — its three knobs go inactive but stay editable.
        void SyncFilter()
        {
            bool off = Sel("filtype", 4) == 0;
            if (cutKnob is not null) cutKnob.IsDim = off;
            if (resoKnob is not null) resoKnob.IsDim = off;
            if (envKnob is not null) envKnob.IsDim = off;
        }
        readouts.Add(SyncFilter);

        var fltRow = Row(
            Capped("TYPE", Chips("filtype", FilterNames)),
            cutCell, resoCell, envCell);

        // ---- the tab panel ----------------------------------------------------
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var hint = new TextBlock
        {
            FontSize = NotaType.Axis, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
        };
        hint.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var tabStrip = new Grid
        {
            Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = Brushes.Transparent,
        };
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border
            {
                Padding = new Thickness(7, 0), BorderThickness = new Thickness(0, 0, 0, 2),
                Cursor = new Cursor(StandardCursorType.Hand), Child = tb,
            };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; ShowTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(hint, 1); hint.HorizontalAlignment = HorizontalAlignment.Right; tabStrip.Children.Add(hint);

        var graphHost = new Panel();
        var rowHost = new Panel();
        bodies[0] = (osc, oscRow); bodies[1] = (env, envRow); bodies[2] = (flt, fltRow);
        foreach (var (g, r) in bodies) { graphHost.Children.Add(g); rowHost.Children.Add(r); }

        void ShowTab()
        {
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                bodies[i].Graph.IsVisible = on;
                bodies[i].Row.IsVisible = on;
            }
            hint.Text = TabHints[tab];
            Refresh();
        }

        var tabBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 5, Margin = new Thickness(7, 5) };
        tabBody.Children.Add(graphHost);
        Grid.SetRow(rowHost, 1); tabBody.Children.Add(rowHost);

        var tabPanel = SectionBox(new Grid { RowDefinitions = new RowDefinitions("Auto,*") });
        var tabGrid = (Grid)tabPanel.Child!;
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabBody, 1); tabGrid.Children.Add(tabBody);

        // ---- the rail ---------------------------------------------------------
        Grid Slider(string label, string id, Func<string> text, bool bipolar = false)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id) : null,
                end: pi >= 0 ? () => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: 42, valueWidth: 40);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        var voiceChips = Chips("voicemode", VoiceNames);
        var railHeader = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
        railHeader.Children.Add(new TextBlock
        {
            Text = "VOICES", FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        });
        voiceChips.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(voiceChips, 1); railHeader.Children.Add(voiceChips);

        var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        // The three captions sit at their real height on the meter's scale (−60 … +6 dB),
        // not spread evenly — a scale that lies is worse than no scale.
        var meterScale = new Grid
        {
            VerticalAlignment = VerticalAlignment.Stretch,
            RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))),
        };
        for (int i = 0; i < 3; i++)
        {
            var cap = Axis(i == 0 ? "0" : i == 1 ? "−12" : "−48");
            Grid.SetRow(cap, i * 2 + 1); meterScale.Children.Add(cap);
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 9),
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var railBottom = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Knob("spread", "Spread", v => NotaNum.Pct(v), size: 44, cellW: 50),
                Knob("velamp", "Vel → Vol", v => NotaNum.Pct(v), cellW: 46, arc: Teal),
            },
        };
        var railBottomHost = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Stretch };
        railBottomHost.Children.Add(railBottom);
        Grid.SetColumn(meterBlock, 1); railBottomHost.Children.Add(meterBlock);

        var railBody = new StackPanel
        {
            Spacing = 6, Margin = new Thickness(8, 6),
            Children =
            {
                Slider("Volume", "gain", () => Db(G("gain"))),
                Slider("Pan", "pan", () => PanText(G("pan")), bipolar: true),
                Slider("Glide", "glide", () => Glide(G("glide"))),
            },
        };
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*") };
        railGrid.Children.Add(HeaderStrip(railHeader));
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var railBottomFrame = new Border
        {
            BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0),
            Margin = new Thickness(8, 0, 8, 6), Padding = new Thickness(0, 5, 0, 0), Child = railBottomHost,
        };
        Grid.SetRow(railBottomFrame, 2); railGrid.Children.Add(railBottomFrame);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ---- status strip -----------------------------------------------------
        string Summary() => tab switch
        {
            0 => $"{WaveNames[Sel("wave", 4)]} · unison {UnisonCounts[Sel("unison", 4)]} · detune {Cents(G("detune"))}"
                 + (Sel("wave", 4) == 1 ? $" · pulse {NotaNum.Pct(0.05 + G("pulsewidth") * 0.90)}" : ""),
            1 => $"Env {Secs(G("attack"), 0.001, 2.0)} · {Secs(G("decay"), 0.002, 2.0)} · {Db(G("sustain"))} · {Secs(G("release"), 0.002, 3.0)}",
            _ => Sel("filtype", 4) == 0
                 ? "Filter off — the oscillator goes straight to the amplifier"
                 : $"Filter {FilterNames[Sel("filtype", 4)]} · cutoff {KHz(G("cutoff"))} · Q {G("resonance").ToString("0.00", NotaNum.Culture)} · env {Bipolar(G("filenv"))}",
        };
        string Meta()
        {
            int uni = UnisonCounts[Sel("unison", 4)];
            string mode = VoiceNames[Sel("voicemode", 3)].ToUpperInvariant();
            string oct = Octaves(G("octave"));
            return $"{mode} · UNISON {uni} · OCT {oct} · {Db(G("gain"))}";
        }
        var statusBar = new Grid
        {
            Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10,
            Background = NotaPalette.SurfaceAbyss,
        };
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);

        // ---- assembly ---------------------------------------------------------
        var body = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap,
            Margin = new Thickness(NotaSpace.DeviceGap), 
        };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel
        {
            LastChildFill = true, Background = NotaPalette.Gutter,
            Children = { statusHost, body },
        };
        ShowTab();
        return root;
    }

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int i = -1, pc = engine.PluginParamCount(trackId, -1);
        for (int k = 0; k < pc; k++) if (engine.PluginParamId(trackId, -1, k) == "voicemode") { i = k; break; }
        if (i < 0) return null;
        int mode = Math.Clamp((int)Math.Round(engine.PluginParamGet(trackId, -1, i) * 2), 0, 2);
        return mode == 0 ? null : $"{Math.Min(active, 1)}/1";
    }

    // A device section: card ground, hairline, radius 6 — the almanac's section box.
    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    // A section's own header strip: a hairline under it, nothing else.
    private static Border HeaderStrip(Control child) => new()
    {
        BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = child,
    };

    /// <summary>Row definitions that put a caption for each dB mark at its place on the
    /// meter's scale: a star gap, the caption (Auto), … and a star gap for the remainder.</summary>
    private static IEnumerable<string> ScaleRows(params double[] dbs)
    {
        double prev = 0;
        foreach (var db in dbs)
        {
            double fromTop = 1.0 - MeterScale.NormDb(db);
            yield return Math.Max(0, fromTop - prev).ToString("0.000", NotaNum.Culture) + "*";
            yield return "Auto";
            prev = fromTop;
        }
        yield return Math.Max(0, 1.0 - prev).ToString("0.000", NotaNum.Culture) + "*";
    }

    private static TextBlock Axis(string text)
    {
        var tb = new TextBlock { Text = text, FontSize = NotaType.Axis, Foreground = NotaPalette.TextAxis };
        tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return tb;
    }

    /// <summary>The Knob inside a KnobCell, so the card can dim it when its section is off.</summary>
    private static Knob? FindKnob(Control cell)
        => cell is Panel p ? System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Knob>(p.Children)) : null;
}
