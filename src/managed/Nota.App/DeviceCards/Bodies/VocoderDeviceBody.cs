// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vocoder body (device kind 26, specs/028-vocoder-effect), a
// build of the spec's design mockup (700 × 260) in three columns:
//
//   Modulator / Carrier   the modulator is this track (the voice): Mod gain and the key meter ·
//                         the carrier: Saw / Noise / Track, the carrier track's picker (with
//                         its colour; the device sidechain) and its Pre / Post tap, Tilt and
//                         the saw's Pitch
//   Bands                 the envelope graph (bars = the modulator per band, teal = the
//                         carrier, the Low / High edges drag) over Bands, Low, High, Q,
//                         Formant, Attack and Release
//   Output                Unvoiced, Mix, Gain, Hold and the output meter
//
// Every control but the picker and the tap is a device param (normalized 0..1), so automation /
// MIDI learn / presets / A-B / persistence come for free; the carrier track and the tap are the
// device's sidechain routing, like the compressor's key. The live reading comes from
// Vocoder.h scopeRead.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class VocoderDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Vocoder.h) ─────────────────────────────
    private const int Bands = 0, Low = 1, High = 2, Q = 3, Formant = 4, Attack = 5, Release = 6, ModGain = 7,
                      Carrier = 8, Tilt = 9, Pitch = 10, Unvoiced = 11, Mix = 12, Gain = 13, Hold = 14;
    // Scope layout (Vocoder::S_* / kTele).
    private const int S_KeyPeak = 0, S_OutPeakL = 1, S_OutPeakR = 2, kTele = 100;

    public double Width => 700;
    public string? Subtitle => "Vocoder";

    // Unit mappings (Vocoder.h).
    private static double ExpMap(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
    private static double ExpNorm(double x, double lo, double hi) => Math.Clamp(Math.Log(x / lo) / Math.Log(hi / lo), 0, 1);
    private static double LowHz(double v) => ExpMap(v, 20, 1000);
    private static double HighHz(double v) => ExpMap(v, 2000, 20000);
    private static int BandsOf(double v) => 8 + (int)Math.Round(v * 24);
    private static int SemisOf(double v) => (int)Math.Round((v - 0.5) * 24);
    private static int PitchOf(double v) => (int)Math.Round(v * 84);   // semitones above C0
    private static int CarrierOf(double v) => Math.Clamp((int)Math.Round(v * 2), 0, 2);

    private static string HzF(double hz) => hz < 999.5 ? NotaNum.F($"{hz:0} Hz") : NotaNum.F($"{hz / 1000:0.0} k");
    private static string MsF(double ms) => ms < 10 ? NotaNum.F($"{ms:0.0} ms") : NotaNum.F($"{ms:0} ms");
    private static string DbF(double db) => (db > 0.05 ? "+" : db < -0.05 ? "−" : "") + NotaNum.F($"{Math.Abs(db):0.0} dB");
    private static string StF(int st) => (st > 0 ? "+" : st < 0 ? "−" : "") + NotaNum.F($"{Math.Abs(st)} st");

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, double v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        void Learn(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kTele];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;

        bool On() => !engine.DeviceBypassed(track, di);
        int Source() => engine.DeviceSidechainSource(track, di);
        int CarrierI() => CarrierOf(P(Carrier));                     // 0 Saw · 1 Noise · 2 Track
        bool HasTrack() => Source() >= 0 && TrackExists(Source());
        bool Passing() => CarrierI() == 2 && !HasTrack();             // Track carrier, none picked: the track passes through
        bool TrackExists(int id)
        {
            for (int i = 0; i < engine.TrackCount; i++) if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == id) return true;
            return false;
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Title(string t) => new()
        {
            Text = NotaText.Label(t), FontSize = NotaType.DeviceSection, FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextMuted, VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, double fs, IBrush c) => new()
        { Text = t, FontSize = fs, Foreground = c, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center };
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }

        Grid Slider(string label, int p, Func<double, string> fmt, string tip, Func<bool>? dim = null, bool bipolar = false, int steps = 0)
        {
            var row = SliderRow(label, () => P(p), v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); RefreshAll();
            }, () => fmt(P(p)), out var sync, begin: () => Begin(p), end: () => End(p), reset: () => { Reset(p); RefreshAll(); },
                bipolar: bipolar, dim: dim, labelWidth: 44, valueWidth: 40);
            readouts.Add(sync);
            Learn(row, p);
            ToolTip.SetTip(row, tip);
            return row;
        }

        Control K(int p, string name, Func<double, string> fmt, string tip, int steps = 0)
        {
            var val = Mono(fmt(P(p)), 8, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = engine.DeviceParamDefault(track, di, p), Width = 34, Height = 34 };
            knob.Format = v => fmt(steps > 1 ? Math.Round(v * (steps - 1)) / (steps - 1) : v);   // read-out at any value: typing searches it
            knob.ValueChanged += v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); val.Text = fmt(P(p)); RefreshAll();
            };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            Learn(knob, p);
            ToolTip.SetTip(knob, tip);
            readouts.Add(() =>
            {
                if (!knob.Dragging) { double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                val.Text = fmt(P(p));
                bool dim = Passing();
                knob.IsDim = dim;
                val.Foreground = dim ? TextDisabled : TextPrimary;
            });
            return KnobCell(name, knob, val, 53);
        }

        MeterBar Meter(string label, out Control row, string tip)
        {
            var bar = new MeterBar(horizontal: true) { Height = 8, MinWidth = 40, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(new TextBlock
            {
                Text = NotaText.Label(label), FontSize = NotaType.RowLabel, FontWeight = FontWeight.Medium,
                Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center,
            });
            g.Children.Add(Col(bar, 1));
            ToolTip.SetTip(g, tip);
            row = g;
            return bar;
        }

        // ======================================================================
        // LEFT — Modulator (this track: Mod gain, key meter) · Carrier (Saw / Noise / Track,
        // the carrier track's picker and tap, Tilt, Pitch)
        // ======================================================================
        var modWhat = new TextBlock
        {
            Text = "This track", FontSize = NotaType.RowLabel, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTip.SetTip(modWhat, "The modulator is the sound of the track the vocoder sits on — the voice");
        var modGain = Slider("Gain", ModGain, v => DbF((v - 0.5) * 48), "Mod gain — trims the voice before the bands hear it, ±24 dB",
            bipolar: true);
        var keyMeter = Meter("Key", out var keyRow, "The voice's level after Mod gain");

        var carSeg = Segments(new[] { "Saw", "Noise", "Track" }, CarrierI,
            i => { SetP(Carrier, i / 2.0); RefreshAll(); }, out var carSync, fill: true, padX: 0);
        readouts.Add(carSync);
        Learn(carSeg, Carrier);
        ToolTip.SetTip(carSeg, "Carrier — what the voice shapes: an internal saw (at Pitch), noise, or another track picked below");

        // The tracks a carrier may come from: every other track and return, but not this track
        // nor any group it sits in (their signal holds this vocoder's output — a loop).
        List<(int Id, string Name, int Color)> Candidates()
        {
            var parent = new Dictionary<int, int>();
            var infos = new List<NotaTrackInfo>();
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti)) { infos.Add(ti); parent[ti.Id] = ti.GroupId; }
            var banned = new HashSet<int> { track };
            for (int g = parent.TryGetValue(track, out var p0) ? p0 : -1, guard = 0; g > 0 && guard < 32; guard++)
            { banned.Add(g); g = parent.TryGetValue(g, out var pg) ? pg : -1; }
            var list = new List<(int, string, int)>();
            foreach (var ti in infos)
                if (!banned.Contains(ti.Id)) list.Add((ti.Id, TrackNames.Of(engine, ti), ArrangementView.EffectiveColorIndex(engine, ti.Id)));
            return list;
        }

        var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Bar, VerticalAlignment = VerticalAlignment.Center };
        var srcName = new TextBlock { FontSize = 9, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chev = new Glyph { Kind = GlyphKind.ChevronDown, Width = 8, Height = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var pickRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
        pickRow.Children.Add(dot);
        pickRow.Children.Add(Col(srcName, 1));
        pickRow.Children.Add(Col(chev, 2));
        var picker = new Border
        {
            Height = 18, Padding = new Thickness(5, 0), CornerRadius = NotaRadius.Control, Background = Sunken,
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = pickRow,
        };
        readouts.Add(() =>
        {
            bool has = HasTrack(), use = CarrierI() == 2;
            srcName.Text = has ? TrackNames.Of(engine, Source()) : "None";
            srcName.Foreground = has && use ? TextPrimary : TextDisabled;
            dot.Background = has ? ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(engine, Source())) : NotaPalette.Rule;
        });
        picker.PointerEntered += (_, _) => picker.BorderBrush = Brass;
        picker.PointerExited += (_, _) => picker.BorderBrush = NotaPalette.Rule;
        picker.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(picker).Properties.IsLeftButtonPressed) return;
            var fly = new MenuFlyout();
            void Add(int id, string name)
            {
                // The current source is ticked, like every choice menu (spec 034).
                var mi = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = id == (HasTrack() ? Source() : -1) };
                mi.Click += (_, _) =>
                {
                    engine.SetDeviceSidechainSource(track, di, id);
                    if (id >= 0 && CarrierI() != 2) SetP(Carrier, 1);   // picking a track means using it
                    ctx.NotifyChanged(); RefreshAll();
                };
                fly.Items.Add(mi);
            }
            Add(-1, "None");
            fly.Items.Add(new Separator());
            foreach (var (id, name, _) in Candidates()) Add(id, name);
            fly.ShowAt(picker);
            e.Handled = true;
        };
        ToolTip.SetTip(picker, "Carrier track — the track the voice shapes when Carrier is Track; this track and its groups are not offered");

        var tap = Segments(new[] { "Pre", "Post" }, () => engine.DeviceSidechainTapPre(track, di) ? 0 : 1,
            i => { engine.SetDeviceSidechainTapPre(track, di, i == 0); ctx.NotifyChanged(); }, out var tapSync, padX: 3, fontSize: 8,
            dim: () => CarrierI() != 2 || !HasTrack());
        readouts.Add(tapSync);
        ToolTip.SetTip(tap, "Where the carrier track is tapped: before its effects and fader, or after. Pre keeps feeding while the track is muted");
        var trackRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
        trackRow.Children.Add(picker);
        trackRow.Children.Add(Col(tap, 1));

        var tilt = Slider("Tilt", Tilt, v => DbF((v - 0.5) * 24), "Tilt — brightens (or darkens) the saw or the carrier track around 1 kHz before the bands, ±12 dB",
            dim: () => CarrierI() == 1, bipolar: true);
        var pitch = Slider("Pitch", Pitch, v => NoteName(PitchOf(v) + 12), "Pitch — the internal saw's note, C0 … C7",
            dim: () => CarrierI() != 0, steps: 85);

        var modHead = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        modHead.Children.Add(Title("Modulator"));
        modHead.Children.Add(Col(modWhat, 1));
        var modBody = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto"), RowSpacing = 5 };
        modBody.Children.Add(modHead);
        modBody.Children.Add(Row(modGain, 1));
        modBody.Children.Add(Row(keyRow, 2));
        var carBody = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto"), RowSpacing = 5 };
        carBody.Children.Add(Title("Carrier"));
        carBody.Children.Add(Row(carSeg, 1));
        carBody.Children.Add(Row(trackRow, 2));
        carBody.Children.Add(Row(tilt, 3));
        carBody.Children.Add(Row(pitch, 4));
        var modSect = Section(new Border { Padding = new Thickness(8, 6), Child = modBody });
        var carSect = Section(new Border { Padding = new Thickness(8, 6), Child = carBody });
        var left = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), Width = 152 };
        left.Children.Add(modSect);
        left.Children.Add(Row(carSect, 1));

        // ======================================================================
        // CENTRE — Bands: the envelope graph over the band knobs
        // ======================================================================
        var info = Mono("", 8, TextTertiary);
        readouts.Add(() => info.Text = NotaNum.F($"{BandsOf(P(Bands))} · ") + HzF(LowHz(P(Low))) + " – " + HzF(HighHz(P(High))));
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(Title("Bands"));
        head.Children.Add(Col(info, 1));

        var graph = new VocoderGraph();
        graph.GestureBegin += edge => Begin(edge == 0 ? Low : High);
        graph.GestureEnd += edge => End(edge == 0 ? Low : High);
        graph.EdgeChanged += (edge, hz) =>
        {
            if (edge == 0) Raw(Low, ExpNorm(Math.Min(hz, HighHz(P(High)) / 2), 20, 1000));
            else Raw(High, ExpNorm(Math.Max(hz, LowHz(P(Low)) * 2), 2000, 20000));
            RefreshAll();
        };
        graph.ResetRequested += edge => { Reset(edge == 0 ? Low : High); RefreshAll(); };
        Learn(graph, Low);
        ToolTip.SetTip(graph, "The modulator's level in each band (bars) and the carrier's (teal). Drag an edge to move Low or High; double-click an edge resets it.");

        var knobs = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 4, 0, 0) };
        knobs.Children.Add(K(Bands, "Bands", v => BandsOf(v).ToString(NotaNum.Culture), "Bands — how many bands, 8 … 32: fewer is more robotic, more is clearer", steps: 25));
        knobs.Children.Add(K(Low, "Low", v => HzF(LowHz(v)), "Low — the lowest band's edge, 20 Hz … 1 kHz"));
        knobs.Children.Add(K(High, "High", v => HzF(HighHz(v)), "High — the highest band's edge, 2 … 20 kHz"));
        knobs.Children.Add(K(Q, "Q", v => NotaNum.F($"{1 + v * 9:0.0}"), "Q — how narrow each band is, 1 … 10"));
        knobs.Children.Add(K(Formant, "Formant", v => StF(SemisOf(v)), "Formant — moves the carrier's bands against the modulator's, ±12 st: up sounds smaller, down larger", steps: 25));
        knobs.Children.Add(K(Attack, "Attack", v => MsF(ExpMap(v, 0.5, 100)), "Attack — how fast a band opens, 0.5 … 100 ms"));
        knobs.Children.Add(K(Release, "Release", v => MsF(ExpMap(v, 5, 1000)), "Release — how fast a band closes, 5 … 1000 ms"));

        var centreBody = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(head, Dock.Top);
        DockPanel.SetDock(knobs, Dock.Bottom);
        head.Margin = new Thickness(0, 0, 0, 5);
        centreBody.Children.Add(head);
        centreBody.Children.Add(knobs);
        centreBody.Children.Add(graph);
        var centre = Section(new Border { Padding = new Thickness(8, 6), Child = centreBody });

        // ======================================================================
        // RIGHT — Output: Unvoiced · Mix · Gain · Hold · the output meter
        // ======================================================================
        var unv = Slider("Unvoiced", Unvoiced, v => NotaNum.F($"{v * 100:0} %"), "Unvoiced — adds noise where the modulator hisses, so consonants stay clear");
        var mix = Slider("Mix", Mix, v => NotaNum.F($"{v * 100:0} %"), "Mix — the carrier as it came in against the vocoded sound");
        var gain = Slider("Gain", Gain, v => DbF((v - 0.5) * 24), "Gain — the output level, ±12 dB", bipolar: true);
        var hold = Switch("Hold", () => P(Hold) >= 0.5f, () => { SetP(Hold, P(Hold) >= 0.5f ? 0 : 1); RefreshAll(); }, out var holdSync);
        readouts.Add(holdSync);
        Learn(hold, Hold);
        ToolTip.SetTip(hold, "Hold — freezes every band where it is; the carrier keeps sounding through it");
        var outMeter = Meter("Out", out var outRow, "The vocoder's output level");

        var outBody = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto,Auto,*,Auto"), RowSpacing = 6 };
        outBody.Children.Add(Title("Output"));
        outBody.Children.Add(Row(unv, 1));
        outBody.Children.Add(Row(mix, 2));
        outBody.Children.Add(Row(gain, 3));
        outBody.Children.Add(Row(new Border { HorizontalAlignment = HorizontalAlignment.Left, Child = hold }, 4));
        outBody.Children.Add(Row(outRow, 6));
        var right = Section(new Border { Padding = new Thickness(8, 6), Child = outBody });
        right.Width = 138;

        // ---- assemble ---------------------------------------------------------------
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        root.Children.Add(left);
        root.Children.Add(Col(centre, 1));
        root.Children.Add(Col(right, 2));

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, kTele);
            bool on = On(), has = !Passing();
            graph.Set(scope, scN, LowHz(P(Low)), HighHz(P(High)), on, has);
            float key = (float)Sc(S_KeyPeak);
            keyMeter.Push(new NotaMeter { PeakL = key, PeakR = key });
            outMeter.Push(new NotaMeter { PeakL = (float)Sc(S_OutPeakL), PeakR = (float)Sc(S_OutPeakR) });
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
