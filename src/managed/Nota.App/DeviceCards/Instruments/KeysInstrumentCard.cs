// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Keys editor (instrument kind 16), a build of the
// "Nota Keys" mockup. Two sizes of one device; the S / L toggle in the shell header flips
// them (the instrument's "view" param — see InstrumentView — a new Keys opens as L):
//
//   • L 700 × 260, two tabs:
//       Sound  HAMMER ▸ RESONATOR ▸ PICKUP (the curve — drag X = symmetry, Y = distance,
//              H1–H8 of what it does to a sine; Clav shows its string and two pickups) ▸
//              DAMPER (+ the pedal, lit by the Pedal param or a keyboard's CC64), with the
//              model chips in the tab strip and a PLAY rail (tune / age / stretch, the voice
//              pool and the meter) on the right.
//       FX     PREAMP ▸ TREMOLO (its L / R gains scrolling with the engine's LFO) ▸ PHASER
//              ▸ CHORUS ▸ CABINET ▸ OUTPUT — each with its power dot; the chain in words
//              sits in the tab strip.
//   • S 260 × 260 — the model chips, the pickup window, HARD · PICKUP · DRIVE · TREM.
//
// Brass is the parameter itself; teal is modulation depth (vel → hardness, key → bright,
// tremolo depth). BodyOnly — the shared shell draws the header (name / preset / A-B / MPE).

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class KeysInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "ELECTRIC PIANO";

    public double WidthFor(IAudioEngine engine, int trackId) => InstrumentView.IsMini(engine, trackId) ? 260 : 700;

    public Control? HeaderAccessory(DeviceCardContext ctx)
    {
        var e = ctx.Engine; int t = ctx.TrackId;
        if (InstrumentView.Index(e, t) < 0) return null;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        if (!InstrumentView.IsMini(e, t)) row.Children.Add(MidiCardHeader.LearnButton(ctx));
        row.Children.Add(MidiCardHeader.SizeToggle(ctx, () => InstrumentView.IsMini(e, t), m => InstrumentView.SetMini(e, t, m)));
        return row;
    }

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int pc = engine.PluginParamCount(trackId, -1);
        for (int i = 0; i < pc; i++)
            if (engine.PluginParamId(trackId, -1, i) == "voices")
                return $"{Math.Max(0, active)}/{KeysModel.VoiceCounts[KeysModel.Index(engine.PluginParamGet(trackId, -1, i), 4)]}";
        return null;
    }

    private const double TabH = 20, StatusH = 18, RailW = 142;
    private static readonly string[] TabNames = { "Sound", "FX" };
    private static readonly Dictionary<int, int> OpenTab = new();   // per track, survives a rebuild

    private static string Signed(double v, string unit) => $"{(v >= 0.5 ? "+" : v <= -0.5 ? "−" : "")}{Math.Round(Math.Abs(v))}\u2009{unit}";
    private static string PanText(float v)
    {
        int p = (int)Math.Round((v - 0.5f) * 200);
        return p == 0 ? "C" : p < 0 ? $"{-p} L" : $"{p} R";
    }
    private static string NoiseText(float v) => KeysModel.NoiseDb(v) is { } db ? NotaNum.Db(db) : "off";
    private static string VolText(float v) => KeysModel.VolumeDb(v) is { } db ? NotaNum.Db(db) : "−∞\u2009dB";

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
        int Sel(string id, int n) => KeysModel.Index(G(id), n);
        bool On(string id) => G(id) >= 0.5f;

        bool mini = InstrumentView.IsMini(engine, track);
        var readouts = new List<Action>();
        var scope = new float[KeysModel.ScopeLength];
        int Sc(int k) => (int)Math.Round(scope[k]);
        void ReadScope() { Array.Clear(scope); engine.InstrumentScope(track, scope); }

        var pickup = new KeysPickupView(engine, track, idx) { Mini = mini };
        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        meta.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        int tab = OpenTab.TryGetValue(track, out var ot) ? ot : 0;

        void Refresh()
        {
            pickup.Refresh();
            foreach (var a in readouts) a();
        }
        ctx.SetInstLiveViz(Refresh);
        pickup.Changed += Refresh;

        // ---- shared builders --------------------------------------------------
        Control Knob(string id, string name, Func<float, string> fmt, double size = 28, double cellW = 46, IBrush? arc = null)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, size, cellW, arc);

        Border Chips(string id, string[] names, bool fill = false, Action<int>? after = null, Func<bool>? dim = null, double padX = 6)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { SetP(id, iv / (float)(n - 1)); after?.Invoke(iv); Refresh(); }, out var sync, fill: fill, dim: dim, padX: padX);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        // Picking a model resets the pickup it was voiced with and its usual cabinet.
        void ModelPicked(int m)
        {
            var (sym, dist, cab) = KeysModel.ModelDefaults(m);
            SetP("sym", (float)((sym + 1) / 2)); SetP("dist", (float)dist); SetP("cab", cab / 3f);
        }
        Border ModelChips(bool fill) => Chips("model", fill ? KeysModel.ModelShort : KeysModel.ModelNames, fill, ModelPicked, padX: fill ? 6 : 5);

        static TextBlock Caps(string text, IBrush? ink = null) => new()
        {
            Text = text.ToUpperInvariant(), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = ink ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };

        // A section's power dot: brass and glowing when on. Click toggles the param.
        Control PowerDot(string id)
        {
            var dot = new Ellipse { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
            var host = new Border { Background = Brushes.Transparent, Padding = new Thickness(2), Cursor = new Cursor(StandardCursorType.Hand), Child = dot, VerticalAlignment = VerticalAlignment.Center };
            void Paint()
            {
                bool on = On(id);
                dot.Fill = on ? NotaPalette.Accent : NotaPalette.BorderStrong;
                host.Effect = on ? new DropShadowEffect { Color = NotaPalette.AccentColor, BlurRadius = 5, OffsetX = 0, OffsetY = 0 } : null;
            }
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                SetP(id, On(id) ? 0f : 1f); Paint(); Refresh(); e.Handled = true;
            };
            if (I(id) is var pi and >= 0) MidiLearn.Bind(host, MidiTarget.PluginParam(track, -1, pi), id);
            readouts.Add(Paint);
            Paint();
            return host;
        }

        // Dim every knob in a cell list while its section is off (they stay editable).
        void DimWhen(Func<bool> off, params Control[] cells)
        {
            var knobs = new List<Knob>();
            foreach (var c in cells) if (FindKnob(c) is { } k) knobs.Add(k);
            readouts.Add(() => { bool d = off(); foreach (var k in knobs) k.IsDim = d; });
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id) : null,
                end: pi >= 0 ? () => engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id) : null,
                reset: pi >= 0 ? () => { SetP(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: 46, valueWidth: 28);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            return row;
        }

        // ---- status strip ------------------------------------------------------
        string PedalWord() => Sc(KeysModel.ScPedal) > 0 ? "pedal ↓" : "pedal —";
        string SoundLine()
        {
            int m = Sel("model", 4);
            string pu = m == 3 ? KeysModel.PickupPositions[Sel("pupos", 3)]
                : $"pickup {NotaNum.Pct(G("dist"))} / {Signed(KeysModel.Bipolar(G("sym")) * 100, "%")}";
            return $"{KeysModel.ModelNames[m]} · hard {NotaNum.Pct(G("hard"))} · {pu} · damper {NotaNum.Pct(G("damper"))} · {PedalWord()}";
        }
        string FxLine()
        {
            string pre = On("preon") ? $"preamp {NotaNum.Pct(G("drive"))}" : "preamp off";
            string trem = On("tremon") ? $"trem {KeysModel.TremModes[Sel("tremmode", 2)].ToLowerInvariant()} {KeysModel.TremRateText(G)} {NotaNum.Pct(G("tremdepth"))}" : "trem off";
            string ph = On("phaseron") ? $"phaser {KeysModel.PhaserHz(G("phaserrate")):0.0#}\u2009Hz" : "phaser off";
            string ch = On("choruson") ? $"chorus {NotaNum.Pct(G("chorusmix"))}" : "chorus off";
            return $"{pre} · {trem} · {ph} · {ch} · cab {KeysModel.CabNames[Sel("cab", 4)]}";
        }
        string VoicesText() => $"{Sc(KeysModel.ScActive)} / {KeysModel.VoiceCounts[Sel("voices", 4)]}";
        double sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
        string Rate() => $"{sr / 1000:0.#}\u2009kHz";

        var meterOut = new TextBlock { FontSize = 8, Foreground = TextSecondary };
        ctx.AddDeviceRefresher(() =>
        {
            ReadScope();
            status.Text = mini ? $"{KeysModel.ModelNames[Sel("model", 4)]} · {VoicesText()} · {PedalWord()}"
                        : tab == 0 ? SoundLine() : FxLine();
            if (engine.TryGetTrackMeter(track, out var mt))
            {
                double pk = Math.Max(mt.PeakL, mt.PeakR);
                meterOut.Text = pk > 1e-6 ? $"out {NotaNum.Db(AudioMath.LinToDb((float)pk))}" : "out −∞\u2009dB";
            }
            meta.Text = mini ? meterOut.Text : $"{Rate()} · {VoicesText()} voices";
        });

        if (mini) return BuildMini(ctx, pickup, ModelChips(true), Knob, status, meta, Refresh);

        // ======================= SOUND tab ======================================
        Control Col(string title, Control body, double width, Control? accessory = null)
        {
            var head = new DockPanel { Height = 14, Margin = new Thickness(0, 0, 0, 3) };
            if (accessory is not null) { DockPanel.SetDock(accessory, Dock.Right); head.Children.Add(accessory); }
            head.Children.Add(Caps(title));
            var dp = new DockPanel { Width = width };
            DockPanel.SetDock(head, Dock.Top);
            dp.Children.Add(head); dp.Children.Add(body);
            return dp;
        }
        static Control Arrow() => new Glyph(GlyphKind.ChevronRight, 8)
        {
            Foreground = NotaPalette.BorderStrong, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0),
        };
        static StackPanel VStack(params Control[] cs)
        {
            var sp = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cs) { c.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(c); }
            return sp;
        }
        static Grid Grid2(params Control[] cs)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < cs.Length; i++) { cs[i].HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(cs[i], i % 2); Grid.SetRow(cs[i], i / 2); g.Children.Add(cs[i]); }
            return g;
        }

        var hammer = Col("Hammer", VStack(
            Knob("hard", "Hard", v => NotaNum.Pct(v), size: 24),
            Knob("velhard", "Vel→H", v => NotaNum.Pct(v), size: 24, arc: Teal),
            Knob("noise", "Noise", NoiseText, size: 24)), 46);

        var reso = Col("Resonator", Grid2(
            Knob("decay", "Decay", v => NotaNum.Pct(KeysModel.DecayPct(v) / 100)),
            Knob("body", "Body", v => NotaNum.Pct(v)),
            Knob("bright", "Bright", v => NotaNum.Pct(v)),
            Knob("keybright", "Key→Br", v => Signed(KeysModel.Bipolar(v) * 100, "%"), arc: Teal)), 94);

        // PICKUP: the graph, with the bark readout (or, for Clav, the pickup chips) on its header.
        var bark = new TextBlock { FontSize = 8, Foreground = NotaPalette.TealBright, VerticalAlignment = VerticalAlignment.Center };
        bark.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var puChips = Chips("pupos", KeysModel.PickupPositions, padX: 4);
        var puAcc = new Panel { Children = { bark, puChips } };
        readouts.Add(() =>
        {
            int m = Sel("model", 4);
            bool clav = m == 3;
            puChips.IsVisible = clav; bark.IsVisible = !clav;
            if (!clav) bark.Text = $"bark H2 {KeysModel.BarkDb(KeysModel.Bipolar(G("sym")), G("dist"), m):0}\u2009dB";
        });
        var pickupCol = new DockPanel();
        {
            var head = new DockPanel { Height = 14, Margin = new Thickness(0, 0, 0, 3) };
            DockPanel.SetDock(puAcc, Dock.Right); head.Children.Add(puAcc);
            head.Children.Add(Caps("Pickup"));
            DockPanel.SetDock(head, Dock.Top);
            pickupCol.Children.Add(head); pickupCol.Children.Add(pickup);
        }

        // DAMPER + the pedal.
        var pedalDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        var pedalTxt = new TextBlock { Text = "CC64", FontSize = 8, VerticalAlignment = VerticalAlignment.Center };
        pedalTxt.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var pedalPill = new Border
        {
            CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(5, 1),
            Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, HorizontalAlignment = HorizontalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { pedalDot, pedalTxt } },
        };
        var pedalLbl = Caps("Pedal");
        pedalLbl.HorizontalAlignment = HorizontalAlignment.Center;
        pedalPill.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(pedalPill).Properties.IsLeftButtonPressed) return;
            SetP("pedal", On("pedal") ? 0f : 1f); Refresh(); e.Handled = true;
        };
        if (I("pedal") is var pedI and >= 0) MidiLearn.Bind(pedalPill, MidiTarget.PluginParam(track, -1, pedI), "Damper Pedal");
        ToolTip.SetTip(pedalPill, "Sustain pedal — click to hold it down; a keyboard's CC64 works too");
        ctx.AddDeviceRefresher(() =>
        {
            bool down = Sc(KeysModel.ScPedal) > 0 || On("pedal");
            pedalDot.Fill = down ? NotaPalette.Success : NotaPalette.BorderStrong;
            pedalPill.BorderBrush = down ? NotaPalette.Success : BorderDef;
            pedalTxt.Foreground = down ? TextPrimary : TextTertiary;
            pedalLbl.Text = down ? "PEDAL ↓" : "PEDAL";
            pedalLbl.Foreground = down ? TextPrimary : TextTertiary;
        });
        var damper = Col("Damper", VStack(
            Knob("damper", "Damper", v => NotaNum.Pct(v), size: 26),
            Knob("relnoise", "Rel N", NoiseText, size: 24),
            new StackPanel { Spacing = 3, Children = { pedalPill, pedalLbl } }), 52);

        var soundRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(7, 5, 7, 6) };
        Control[] soundCells = { hammer, Arrow(), reso, Arrow(), pickupCol, Arrow(), damper };
        for (int i = 0; i < soundCells.Length; i++) { Grid.SetColumn(soundCells[i], i); soundRow.Children.Add(soundCells[i]); }

        // ======================= FX tab =========================================
        Border Island(string title, Control body, string? power = null, Control? accessory = null, double width = double.NaN)
        {
            var head = new DockPanel { Height = 18, Margin = new Thickness(6, 0) };
            if (accessory is not null) { DockPanel.SetDock(accessory, Dock.Right); head.Children.Add(accessory); }
            var left = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            if (power is not null) left.Children.Add(PowerDot(power));
            var tl = Caps(title, power is null ? TextTertiary : TextPrimary);
            if (power is not null) readouts.Add(() => tl.Foreground = On(power) ? TextPrimary : TextTertiary);
            left.Children.Add(tl);
            head.Children.Add(left);
            var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            grid.Children.Add(new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 0, 0, 1), Child = head });
            body.Margin = new Thickness(5, 5, 5, 5);
            Grid.SetRow(body, 1); grid.Children.Add(body);
            var b = new Border
            {
                Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
                CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = grid,
            };
            if (!double.IsNaN(width)) b.Width = width;
            return b;
        }

        var driveK = Knob("drive", "Drive", v => NotaNum.Pct(v), size: 34, cellW: 50);
        var bassK = Knob("bass", "Bass", v => Signed(KeysModel.ShelfDb(v), "dB"), size: 24, cellW: 34);
        var trebK = Knob("treble", "Treble", v => Signed(KeysModel.ShelfDb(v), "dB"), size: 24, cellW: 34);
        DimWhen(() => !On("preon"), driveK, bassK, trebK);
        var preamp = Island("Preamp", new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { Center(driveK), new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { bassK, trebK } } },
        }, "preon", width: 80);

        var tremView = new KeysTremView(G);
        ctx.AddDeviceRefresher(() => tremView.SetPhase(scope[KeysModel.ScLfo]));
        var rateK = Knob("tremrate", "Rate", _ => KeysModel.TremRateText(G));
        var depthK = Knob("tremdepth", "Depth", v => NotaNum.Pct(v), arc: Teal);
        DimWhen(() => !On("tremon"), rateK, depthK);
        var syncSeg = Chips("tremsync", KeysModel.TremSyncs, padX: 4);
        var syncCell = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { syncSeg, Center(Caps("Sync")) } };
        readouts.Add(() => tremView.InvalidateVisual());
        var tremBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 4 };
        tremBody.Children.Add(tremView);
        var tremKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { rateK, depthK, syncCell } };
        Grid.SetRow(tremKnobs, 1); tremBody.Children.Add(tremKnobs);
        var trem = Island("Tremolo", tremBody, "tremon", Chips("tremmode", KeysModel.TremModes, padX: 4));

        var phRate = Knob("phaserrate", "Rate", v => $"{KeysModel.PhaserHz(v):0.0#}\u2009Hz");
        var phDepth = Knob("phaserdepth", "Depth", v => NotaNum.Pct(v));
        DimWhen(() => !On("phaseron"), phRate, phDepth);
        var phaser = Island("Phaser", VStack(phRate, phDepth), "phaseron", width: 62);

        var chMix = Knob("chorusmix", "Mix", v => NotaNum.Pct(v));
        DimWhen(() => !On("choruson"), chMix);
        var chNote = new TextBlock { Text = "2 lines · BBD", FontSize = 7, Foreground = NotaPalette.TextAxis, HorizontalAlignment = HorizontalAlignment.Center };
        var chorus = Island("Chorus", VStack(chMix, chNote), "choruson", width: 62);

        var cabList = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var cabCells = new List<(Border B, TextBlock T)>();
        for (int c = 0; c < KeysModel.CabNames.Length; c++)
        {
            int cv = c;
            var tb = new TextBlock { Text = KeysModel.CabNames[c], FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Height = 15, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                SetP("cab", cv / 3f); Refresh(); e.Handled = true;
            };
            cabCells.Add((cell, tb)); cabList.Children.Add(cell);
        }
        if (I("cab") is var cabI and >= 0) MidiLearn.Bind(cabList, MidiTarget.PluginParam(track, -1, cabI), "Cabinet Type");
        var cabNote = new TextBlock { FontSize = 7, Foreground = NotaPalette.TextAxis, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 3, 0, 0) };
        cabList.Children.Add(cabNote);
        readouts.Add(() =>
        {
            int cur = Sel("cab", 4);
            for (int c = 0; c < cabCells.Count; c++)
            {
                bool on = c == cur;
                cabCells[c].B.Background = on ? Brass : NotaPalette.BgSunken;
                cabCells[c].B.BorderBrush = on ? Brass : BorderDef;
                cabCells[c].T.Foreground = on ? OnAccent : TextTertiary;
                cabCells[c].T.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            cabNote.Text = KeysModel.CabNotes[cur];
        });
        var cabinet = Island("Cabinet", cabList, width: 72);

        var outMeter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) outMeter.Push(m); });
        var outKnobs = VStack(Knob("volume", "Volume", VolText, size: 30), Knob("pan", "Pan", PanText, size: 24));
        var outBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        outBody.Children.Add(outKnobs);
        Grid.SetColumn(outMeter, 1); outBody.Children.Add(outMeter);
        var output = Island("Output", outBody, width: 92);

        var fxRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(6, 5, 6, 6) };
        Control[] fxCells = { preamp, Arrow(), trem, Arrow(), phaser, Arrow(), chorus, Arrow(), cabinet, Arrow(), output };
        for (int i = 0; i < fxCells.Length; i++) { Grid.SetColumn(fxCells[i], i); fxRow.Children.Add(fxCells[i]); }

        // ======================= PLAY rail (Sound tab) ==========================
        var voicesChips = Chips("voices", KeysModel.VoiceNames, padX: 3);
        var grid = new KeysVoiceGrid { Margin = new Thickness(0, 2, 0, 0) };
        var voicesLine = new TextBlock { FontSize = 8, Foreground = TextPrimary };
        voicesLine.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        var pedalLine = new TextBlock { FontSize = 7, Foreground = NotaPalette.TextAxis };
        ctx.AddDeviceRefresher(() =>
        {
            int limit = KeysModel.VoiceCounts[Sel("voices", 4)];
            int held = Sc(KeysModel.ScHeld), sus = Sc(KeysModel.ScSustained), act = Sc(KeysModel.ScActive);
            grid.Set(limit, held, Math.Max(0, act - held));
            voicesLine.Text = $"{act} / {limit} voices";
            pedalLine.Text = sus > 0 ? $"{sus} held by the pedal" : act > held ? $"{act - held} ringing out" : "";
        });
        var railMeter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) railMeter.Push(m); });
        var voiceInfo = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom, Children = { voicesLine, pedalLine } };
        var railBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        railBottom.Children.Add(voiceInfo);
        Grid.SetColumn(railMeter, 1); railBottom.Children.Add(railMeter);
        var voicesRow = new DockPanel();
        var vLbl = new TextBlock { Text = "VOICES", FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(vLbl, Dock.Left); voicesRow.Children.Add(vLbl);
        voicesChips.HorizontalAlignment = HorizontalAlignment.Right; voicesRow.Children.Add(voicesChips);
        var railTop = new StackPanel
        {
            Spacing = 5,
            Children =
            {
                Slider("Tune", "tune", () => $"{Math.Round(KeysModel.TuneCents(G("tune"))):+0;−0;0}\u2009c", bipolar: true),
                Slider("Age", "age", () => NotaNum.Pct(G("age"))),
                Slider("Stretch", "stretch", () => NotaNum.Pct(G("stretch"))),
                voicesRow, grid,
            },
        };
        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 4, Margin = new Thickness(7, 5, 7, 6) };
        railBody.Children.Add(railTop);
        Grid.SetRow(railBottom, 1); railBody.Children.Add(railBottom);
        var railHead = new DockPanel { Height = TabH, Margin = new Thickness(8, 0) };
        var railHeadTxt = Caps("Play");
        railHead.Children.Add(railHeadTxt);
        var railGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railGrid.Children.Add(new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = railHead });
        Grid.SetRow(railBody, 1); railGrid.Children.Add(railBody);
        var rail = SectionBox(railGrid);
        rail.Width = RailW;

        // ======================= tabs ===========================================
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        var modelChips = ModelChips(false);
        var chain = new TextBlock { FontSize = NotaType.Axis, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        chain.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        readouts.Add(() =>
            chain.Text = $"{(On("preon") ? "preamp" : "—")} → {(On("tremon") ? "trem" : "—")} → {(On("phaseron") ? "phaser" : "—")} → "
                       + $"{(On("choruson") ? "chorus" : "—")} → {KeysModel.CabNames[Sel("cab", 4)]}");
        var tabRight = new Panel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 0, 6, 0), Children = { modelChips, chain } };
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Background = Brushes.Transparent };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(tabRight, 1); tabStrip.Children.Add(tabRight);
        var bodyHost = new Panel { Children = { soundRow, fxRow } };

        void ShowTab()
        {
            OpenTab[track] = tab;
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
            soundRow.IsVisible = tab == 0; fxRow.IsVisible = tab == 1;
            modelChips.IsVisible = tab == 0; chain.IsVisible = tab == 1;
            rail.IsVisible = tab == 0;
            Refresh();
        }
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; ShowTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }

        var tabPanel = SectionBox(new Grid { RowDefinitions = new RowDefinitions("Auto,*") });
        var tg = (Grid)tabPanel.Child!;
        tg.Children.Add(new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = tabStrip });
        Grid.SetRow(bodyHost, 1); tg.Children.Add(bodyHost);

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap, Margin = new Thickness(NotaSpace.DeviceGap) };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);

        var statusHost = StatusStrip(status, meta);
        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };
        ShowTab();
        return root;
    }

    private static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

    // ---- S: the mini card -------------------------------------------------------
    private static Control BuildMini(DeviceCardContext ctx, KeysPickupView pickup, Border modelChips,
        Func<string, string, Func<float, string>, double, double, IBrush?, Control> knob,
        TextBlock status, TextBlock meta, Action refresh)
    {
        var cells = new[]
        {
            knob("hard", "Hard", v => NotaNum.Pct(v), 30, 54, null),
            knob("dist", "Pickup", v => NotaNum.Pct(v), 30, 54, null),
            knob("drive", "Drive", v => NotaNum.Pct(v), 30, 54, null),
            knob("tremdepth", "Trem", v => NotaNum.Pct(v), 30, 54, Teal),
        };
        var knobRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(cells[i], i); knobRow.Children.Add(cells[i]);
        }
        modelChips.HorizontalAlignment = HorizontalAlignment.Stretch;
        var inner = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 5, Margin = new Thickness(6, 6, 6, 4) };
        inner.Children.Add(modelChips);
        Grid.SetRow(pickup, 1); inner.Children.Add(pickup);
        Grid.SetRow(knobRow, 2); inner.Children.Add(knobRow);
        var island = SectionBox(inner);
        island.Margin = new Thickness(NotaSpace.DeviceGap);

        var statusHost = StatusStrip(status, meta);
        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, island } };
        refresh();
        return root;
    }

    private static Border StatusStrip(TextBlock status, TextBlock meta)
    {
        var bar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10, Background = NotaPalette.SurfaceAbyss };
        bar.Children.Add(status);
        Grid.SetColumn(meta, 1); bar.Children.Add(meta);
        return new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = bar,
        };
    }

    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    private static Knob? FindKnob(Control cell)
        => cell is Panel p ? System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Knob>(p.Children)) : null;
}
