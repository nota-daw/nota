// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Keys editor (instrument kind 16), a build of the
// "Nota Keys" mockup. Two sizes of one device; the S / L toggle in the shell header flips
// them (the instrument's "view" param — see InstrumentView — a new Keys opens as L):
//
//   • L 1050 × 260 in two pressed-in sections (style guide, spec 038), two tabs:
//       Sound  Hammer ▸ Resonator ▸ Pickup (the curve — drag X = symmetry, Y = distance,
//              H1–H8 of what it does to a sine; Clav shows its string and two pickups) ▸
//              Damper (+ the pedal, lit by the Pedal param or a keyboard's CC64), with the
//              model chips in the tab row and a Play rail (tune / age / stretch, the voice
//              pool and the meter) on the right.
//       FX     Preamp ▸ Tremolo (its L / R gains scrolling with the engine's LFO) ▸ Phaser
//              ▸ Chorus ▸ Cabinet ▸ Output — each with its power switch; the chain in words
//              sits in the tab row.
//   • S 260 × 260 — the model chips, the pickup window, Hard · Pickup · Drive · Trem.
//
// BodyOnly — the shared shell draws the header (name / preset / A-B / MPE, MIDI learn, S / L).

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
using static Nota.App.InstrumentCardKit;

namespace Nota.App;

internal sealed class KeysInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "Electric piano";

    public double WidthFor(IAudioEngine engine, int trackId) => InstrumentView.IsMini(engine, trackId) ? DeviceCardKit.CardS : DeviceCardKit.CardWide;

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

    private const double RailW = 196;
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
        int tab = OpenTab.TryGetValue(track, out var ot) ? ot : 0;

        void Refresh()
        {
            pickup.Refresh();
            foreach (var a in readouts) a();
        }
        ctx.SetInstLiveViz(Refresh);
        pickup.Changed += Refresh;

        // ---- shared builders --------------------------------------------------
        Control Knob(string id, string name, Func<float, string> fmt, double size = 26, double cellW = 48, bool inline = false)
            => InstrumentControls.InstKnob(ctx, idx, id, name, Refresh, fmt, size, cellW, null, inline);

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

        // A section's power switch: on/off before its name. Click toggles the param.
        Control PowerSwitch(string id, string title)
        {
            var sw = Switch(title, () => On(id), () => { SetP(id, On(id) ? 0f : 1f); Refresh(); }, out var sync);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(sw, MidiTarget.PluginParam(track, -1, pi), id);
            return sw;
        }

        // Dim every knob in a cell list while its section is off (they stay editable).
        void DimWhen(Func<bool> off, params Control[] cells)
        {
            var knobs = new List<Knob>();
            foreach (var c in cells) if (FindKnob(c) is { } k) knobs.Add(k);
            readouts.Add(() => { bool d = off(); foreach (var k in knobs) k.IsDim = d; });
        }

        Grid Slider(string label, string id, Func<float, string> fmt, bool bipolar = false)
        {
            var row = ParamRow(ctx, idx, id, label, fmt, Refresh, readouts, labelWidth: 48, valueWidth: 34, bipolar: bipolar, learnName: id);
            row.Height = 22;
            return row;
        }

        ctx.AddDeviceRefresher(ReadScope);

        if (mini) return BuildMini(pickup, ModelChips(true), Knob, Refresh);

        // ======================= Sound tab ======================================
        Control Col(string title, Control body, double width, Control? accessory = null)
        {
            var head = new DockPanel { Height = 22 };
            if (accessory is not null) { DockPanel.SetDock(accessory, Dock.Right); head.Children.Add(accessory); }
            head.Children.Add(Cap(title));
            var dp = new DockPanel { Width = width };
            DockPanel.SetDock(head, Dock.Top);
            dp.Children.Add(head); dp.Children.Add(body);
            return dp;
        }
        static Control Arrow() => new Glyph(GlyphKind.ChevronRight, 8)
        {
            Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0),
        };
        static StackPanel VStack(double spacing, params Control[] cs)
        {
            var sp = new StackPanel { Spacing = spacing, VerticalAlignment = VerticalAlignment.Center };
            foreach (var c in cs) { c.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(c); }
            return sp;
        }
        static Grid Grid2(params Control[] cs)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), RowSpacing = 10, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < cs.Length; i++) { cs[i].HorizontalAlignment = HorizontalAlignment.Center; Grid.SetColumn(cs[i], i % 2); Grid.SetRow(cs[i], i / 2); g.Children.Add(cs[i]); }
            return g;
        }

        double Inl = Nota.App.Knob.SizeInline;
        var hammerStack = new StackPanel { Spacing = 14, VerticalAlignment = VerticalAlignment.Center, Children =
        {
            Knob("hard", "Hard", v => NotaNum.Pct(v), Inl, 84, true),
            Knob("velhard", "Vel→H", v => NotaNum.Pct(v), Inl, 84, true),
            Knob("noise", "Noise", NoiseText, Inl, 84, true),
        } };
        var hammer = Col("Hammer", hammerStack, 84);

        var reso = Col("Resonator", Grid2(
            Knob("decay", "Decay", v => NotaNum.Pct(KeysModel.DecayPct(v) / 100)),
            Knob("body", "Body", v => NotaNum.Pct(v)),
            Knob("bright", "Bright", v => NotaNum.Pct(v)),
            Knob("keybright", "Key→Br", v => Signed(KeysModel.Bipolar(v) * 100, "%"))), 100);

        // Pickup: the graph, with the bark readout (or, for Clav, the pickup chips) on its header.
        var bark = Cap("", TextSecondary);
        var puChips = Chips("pupos", KeysModel.PickupPositions, padX: 4);
        var puAcc = new Panel { Children = { bark, puChips } };
        readouts.Add(() =>
        {
            int m = Sel("model", 4);
            bool clav = m == 3;
            puChips.IsVisible = clav; bark.IsVisible = !clav;
            if (!clav) bark.Text = $"bark H2 {KeysModel.BarkDb(KeysModel.Bipolar(G("sym")), G("dist"), m):0} dB";
        });
        var pickupCol = new DockPanel();
        {
            var head = new DockPanel { Height = 22 };
            DockPanel.SetDock(puAcc, Dock.Right); head.Children.Add(puAcc);
            head.Children.Add(Cap("Pickup"));
            DockPanel.SetDock(head, Dock.Top);
            pickup.Margin = new Thickness(0, 2, 0, 0);
            pickupCol.Children.Add(head); pickupCol.Children.Add(pickup);
        }

        // Damper + the pedal: a raised button lit while the pedal is down (the Pedal param or a keyboard's CC64).
        var pedalDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        var pedalTxt = Value("CC64");
        var pedalPill = new Border
        {
            CornerRadius = NotaRadius.Field, Padding = new Thickness(8, 2), Background = NotaPalette.SurfaceHover,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pedalDot, pedalTxt } },
        };
        pedalPill.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        var pedalLbl = Cap("Pedal");
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
            pedalDot.Fill = down ? NotaPalette.Accent : TextTertiary;
            pedalPill.Background = down ? NotaPalette.AccentSoft : NotaPalette.SurfaceHover;
            pedalTxt.Foreground = down ? AccentBright : TextSecondary;
            pedalLbl.Text = down ? "Pedal ↓" : "Pedal";
            pedalLbl.Foreground = down ? TextPrimary : TextTertiary;
        });
        var pedalRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { pedalPill, pedalLbl } };
        var damper = Col("Damper", VStack(4,
            Knob("damper", "Damper", v => NotaNum.Pct(v)),
            Knob("relnoise", "Rel N", NoiseText),
            pedalRow), 92);

        var soundRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,*,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        Control[] soundCells = { hammer, Arrow(), reso, Arrow(), pickupCol, Arrow(), damper };
        for (int i = 0; i < soundCells.Length; i++) { Grid.SetColumn(soundCells[i], i); soundRow.Children.Add(soundCells[i]); }

        // ======================= FX tab =========================================
        // Each stage is a column: its name (with its power switch) over its controls.
        Control Stage(string title, Control body, string? power = null, Control? accessory = null, double width = double.NaN)
        {
            var head = new DockPanel { Height = 22 };
            if (accessory is not null) { DockPanel.SetDock(accessory, Dock.Right); head.Children.Add(accessory); }
            head.Children.Add(power is not null ? PowerSwitch(power, title) : Cap(title));
            var dp = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            dp.Children.Add(head);
            body.Margin = new Thickness(0, 4, 0, 0);
            dp.Children.Add(body);
            if (!double.IsNaN(width)) dp.Width = width;
            return dp;
        }

        var driveK = Knob("drive", "Drive", v => NotaNum.Pct(v), 28, 56);
        var bassK = Knob("bass", "Bass", v => Signed(KeysModel.ShelfDb(v), "dB"), Inl, 42);
        var trebK = Knob("treble", "Treble", v => Signed(KeysModel.ShelfDb(v), "dB"), Inl, 42);
        DimWhen(() => !On("preon"), driveK, bassK, trebK);
        var preamp = Stage("Preamp", new StackPanel
        {
            Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { Center(driveK), new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { bassK, trebK } } },
        }, "preon", width: 86);

        var tremView = new KeysTremView(G);
        ctx.AddDeviceRefresher(() => tremView.SetPhase(scope[KeysModel.ScLfo]));
        var rateK = Knob("tremrate", "Rate", _ => KeysModel.TremRateText(G));
        var depthK = Knob("tremdepth", "Depth", v => NotaNum.Pct(v));
        DimWhen(() => !On("tremon"), rateK, depthK);
        var syncSeg = Chips("tremsync", KeysModel.TremSyncs, padX: 4);
        var syncCell = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { syncSeg, Center(Cap("Sync")) } };
        readouts.Add(() => tremView.InvalidateVisual());
        var tremBody = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 4 };
        tremBody.Children.Add(tremView);
        var tremKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Center, Children = { rateK, depthK, syncCell } };
        Grid.SetRow(tremKnobs, 1); tremBody.Children.Add(tremKnobs);
        var trem = Stage("Tremolo", tremBody, "tremon", Chips("tremmode", KeysModel.TremModes, padX: 4));

        var phRate = Knob("phaserrate", "Rate", v => $"{KeysModel.PhaserHz(v):0.0#} Hz", 26, 56);
        var phDepth = Knob("phaserdepth", "Depth", v => NotaNum.Pct(v), 26, 56);
        DimWhen(() => !On("phaseron"), phRate, phDepth);
        var phaser = Stage("Phaser", VStack(6, phRate, phDepth), "phaseron", width: 78);

        var chMix = Knob("chorusmix", "Mix", v => NotaNum.Pct(v), 26, 56);
        DimWhen(() => !On("choruson"), chMix);
        var chNote = Cap("2 lines · BBD", TextDisabled);
        var chorus = Stage("Chorus", VStack(6, chMix, chNote), "choruson", width: 78);

        // Cabinet: the four boxes as a column of cells in a groove, the chosen one raised.
        var cabList = new StackPanel { Spacing = 2 };
        var cabCells = new List<(Border B, TextBlock T)>();
        for (int c = 0; c < KeysModel.CabNames.Length; c++)
        {
            int cv = c;
            var tb = new TextBlock { Text = KeysModel.CabNames[c], FontSize = NotaType.KnobValue, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Height = NotaSize.Seg, CornerRadius = NotaRadius.Cell, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                SetP("cab", cv / 3f); Refresh(); e.Handled = true;
            };
            cabCells.Add((cell, tb)); cabList.Children.Add(cell);
        }
        var cabGroove = Groove(cabList);
        if (I("cab") is var cabI and >= 0) MidiLearn.Bind(cabGroove, MidiTarget.PluginParam(track, -1, cabI), "Cabinet Type");
        var cabNote = Cap("", TextDisabled); cabNote.HorizontalAlignment = HorizontalAlignment.Center; cabNote.Margin = new Thickness(0, 4, 0, 0);
        readouts.Add(() =>
        {
            int cur = Sel("cab", 4);
            for (int c = 0; c < cabCells.Count; c++)
            {
                bool on = c == cur;
                PaintCell(cabCells[c].B, on);
                cabCells[c].T.Foreground = on ? AccentBright : TextTertiary;
            }
            cabNote.Text = KeysModel.CabNotes[cur];
        });
        var cabinet = Stage("Cabinet", new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { cabGroove, cabNote } }, width: 84);

        var outMeter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) outMeter.Push(m); });
        var outKnobs = VStack(6, Knob("volume", "Volume", VolText, 26, 56), Knob("pan", "Pan", PanText, 26, 56));
        var outBody = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        outBody.Children.Add(outKnobs);
        Grid.SetColumn(outMeter, 1); outBody.Children.Add(outMeter);
        var output = Stage("Output", outBody, width: 80);

        var fxRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 4, 0, 0) };
        Control[] fxCells = { preamp, Arrow(), trem, Arrow(), phaser, Arrow(), chorus, Arrow(), cabinet, Arrow(), output };
        for (int i = 0; i < fxCells.Length; i++) { Grid.SetColumn(fxCells[i], i); fxRow.Children.Add(fxCells[i]); }

        // ======================= Play rail (Sound tab) ==========================
        var voicesChips = Chips("voices", KeysModel.VoiceNames, padX: 5);
        var grid = new KeysVoiceGrid { Margin = new Thickness(0, 6, 0, 0) };
        var voicesLine = Value("");
        var pedalLine = Cap("", TextDisabled);
        ctx.AddDeviceRefresher(() =>
        {
            int limit = KeysModel.VoiceCounts[Sel("voices", 4)];
            int held = Sc(KeysModel.ScHeld), sus = Sc(KeysModel.ScSustained), act = Sc(KeysModel.ScActive);
            grid.Set(limit, held, Math.Max(0, act - held));
            voicesLine.Text = $"{act} / {limit} voices";
            pedalLine.Text = sus > 0 ? $"{sus} held by the pedal" : act > held ? $"{act - held} ringing out" : "";
            pedalLine.IsVisible = pedalLine.Text.Length > 0;
        });
        var railMeter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth, Margin = new Thickness(0, 6, 0, 2) };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) railMeter.Push(m); });
        var voiceInfo = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Bottom, Children = { pedalLine, voicesLine } };
        var railBottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        railBottom.Children.Add(voiceInfo);
        railBottom.Children.Add(railMeter.At(1));
        var voicesRow = new DockPanel { Height = 26, Margin = new Thickness(0, 2, 0, 0) };
        var vLbl = Cap("Voices");
        DockPanel.SetDock(vLbl, Dock.Left); voicesRow.Children.Add(vLbl);
        voicesChips.HorizontalAlignment = HorizontalAlignment.Right; voicesRow.Children.Add(voicesChips);
        var railTop = new StackPanel
        {
            Children =
            {
                Slider("Tune", "tune", v => $"{Math.Round(KeysModel.TuneCents(v)):+0;−0;0} c", bipolar: true),
                Slider("Age", "age", v => NotaNum.Pct(v)),
                Slider("Stretch", "stretch", v => NotaNum.Pct(v)),
                voicesRow, grid,
            },
        };
        var railBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        railBody.Children.Add(railTop);
        railBody.Children.Add(railBottom.At(0, 1));
        var railHead = new DockPanel { Height = TabRowH, Children = { Title("Play") } };
        var rail = Pane(new DockPanel { Children = { railHead.Docked(Dock.Top), railBody } }, new Thickness(SectionPad, 10, SectionPad, 8));
        rail.Width = RailW;

        // ======================= tabs ===========================================
        var modelChips = ModelChips(false);
        var chain = Cap("");
        readouts.Add(() =>
            chain.Text = $"{(On("preon") ? "preamp" : "—")} → {(On("tremon") ? "trem" : "—")} → {(On("phaseron") ? "phaser" : "—")} → "
                       + $"{(On("choruson") ? "chorus" : "—")} → {KeysModel.CabNames[Sel("cab", 4)]}");
        var tabRight = new Panel { Children = { modelChips, chain } };
        modelChips.HorizontalAlignment = chain.HorizontalAlignment = HorizontalAlignment.Right;
        var bodyHost = new Panel { Children = { soundRow, fxRow } };
        Action tabSync = () => { };
        void ShowTab()
        {
            OpenTab[track] = tab;
            tabSync();
            soundRow.IsVisible = tab == 0; fxRow.IsVisible = tab == 1;
            modelChips.IsVisible = tab == 0; chain.IsVisible = tab == 1;
            rail.IsVisible = tab == 0;
            Refresh();
        }
        var tabRow = Tabs(TabNames, () => tab, i => { tab = i; ShowTab(); }, out var ts, tabRight);
        tabSync = ts;

        var tabPanel = Pane(new DockPanel { Children = { tabRow.Docked(Dock.Top), bodyHost } }, new Thickness(SectionPad, 10, SectionPad, 8));

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = SectionGap, Margin = new Thickness(0, CardPad, 0, 0) };
        root.Children.Add(tabPanel);
        root.Children.Add(rail.At(1));
        ShowTab();
        return root;
    }

    private static Control Center(Control c) { c.HorizontalAlignment = HorizontalAlignment.Center; return c; }

    // ---- S: the mini card -------------------------------------------------------
    private static Control BuildMini(KeysPickupView pickup, Border modelChips,
        Func<string, string, Func<float, string>, double, double, bool, Control> knob, Action refresh)
    {
        var cells = new[]
        {
            knob("hard", "Hard", v => NotaNum.Pct(v), 26, 54, false),
            knob("dist", "Pickup", v => NotaNum.Pct(v), 26, 54, false),
            knob("drive", "Drive", v => NotaNum.Pct(v), 26, 54, false),
            knob("tremdepth", "Trem", v => NotaNum.Pct(v), 26, 54, false),
        };
        var knobRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*") };
        for (int i = 0; i < cells.Length; i++)
        {
            cells[i].HorizontalAlignment = HorizontalAlignment.Center;
            Grid.SetColumn(cells[i], i); knobRow.Children.Add(cells[i]);
        }
        modelChips.HorizontalAlignment = HorizontalAlignment.Stretch;
        var inner = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 8 };
        inner.Children.Add(modelChips);
        inner.Children.Add(pickup.At(0, 1));
        inner.Children.Add(knobRow.At(0, 2));
        var section = Pane(inner, new Thickness(10, 10, 10, 8));
        section.Margin = new Thickness(0, CardPad, 0, 0);
        refresh();
        return section;
    }

    private static Knob? FindKnob(Control cell)
        => cell is Panel p ? System.Linq.Enumerable.FirstOrDefault(System.Linq.Enumerable.OfType<Knob>(p.Children)) : null;
}
