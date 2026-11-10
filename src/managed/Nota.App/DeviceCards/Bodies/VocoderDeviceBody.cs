// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vocoder body (device kind 26, specs/028-vocoder-effect), a
// build of the spec's design mockup (700 × 260) on the #531 card layout:
//
//   Bands     the envelope graph (bars = the modulator per band, teal = the carrier, the Low /
//             High edges drag) with Q, Attack and Release beside it and Bands, Formant and Hold
//             under it
//   Carrier   the right panel: Saw / Noise / Track, the carrier track's picker (with its colour;
//             the device sidechain) and its Pre / Post tap, Tilt, the saw's Pitch, then the
//             modulator (this track, the voice): Mod gain, and the key and output meters
//   Output    Unvoiced · Dry/wet (Mix) · Gain under the panel
//   S         the graph over the carrier choice and Bands, Formant, Dry/wet and Gain
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
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class VocoderDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match Vocoder.h) ─────────────────────────────
    private const int Bands = 0, Low = 1, High = 2, Q = 3, Formant = 4, Attack = 5, Release = 6, ModGain = 7,
                      Carrier = 8, Tilt = 9, Pitch = 10, Unvoiced = 11, Mix = 12, Gain = 13, Hold = 14;
    // Scope layout (Vocoder::S_* / kTele).
    private const int S_KeyPeak = 0, S_OutPeakL = 1, S_OutPeakR = 2, kTele = 100;

    public double Width => 700;
    public bool FullBleed => true;
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex);
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
        static TextBlock Mono(string t, double fs, IBrush c) => new()
        { Text = t, FontSize = fs, Foreground = c, FontFamily = NotaFonts.UiFamily, VerticalAlignment = VerticalAlignment.Center };
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

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

        Control K(int p, string name, Func<double, string> fmt, string tip, int steps = 0, double size = 34, double cellW = 53)
        {
            var val = Mono(fmt(P(p)), 8, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = engine.DeviceParamDefault(track, di, p), Width = size, Height = size };
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
            return KnobCell(name, knob, val, cellW);
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
        // RIGHT — Carrier (Saw / Noise / Track, the carrier track's picker and tap, Tilt, Pitch)
        // · the modulator (this track: Mod gain, key meter) · the output meter
        // ======================================================================
        var modGain = Slider("Mod gain", ModGain, v => DbF((v - 0.5) * 48), Loc.T("device.vocoder.mod_gain_trims_the_voice"),
            bipolar: true);
        var keyMeter = Meter("Key", out var keyRow, Loc.T("device.vocoder.the_voices_level_after_mod"));
        var outMeter = Meter("Out", out var outRow, Loc.T("device.vocoder.the_vocoders_output_level"));

        Control CarSeg()
        {
            var carSeg = Segments(new[] { "Saw", "Noise", "Track" }, CarrierI,
                i => { SetP(Carrier, i / 2.0); RefreshAll(); }, out var carSync);
            readouts.Add(carSync);
            Learn(carSeg, Carrier);
            ToolTip.SetTip(carSeg, Loc.T("device.vocoder.carrier_what_the_voice_shapes"));
            return carSeg;
        }

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

        var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Center };
        var srcName = new TextBlock { FontSize = 9, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chev = new Glyph { Kind = GlyphKind.ChevronDown, Width = 8, Height = 8, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var pickRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 5 };
        pickRow.Children.Add(dot);
        pickRow.Children.Add(Col(srcName, 1));
        pickRow.Children.Add(Col(chev, 2));
        var picker = new Border
        {
            Height = 18, Padding = new Thickness(5, 0), CornerRadius = NotaRadius.Field, Background = Sunken,
            BorderBrush = NotaPalette.Rule, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = pickRow,
        };
        readouts.Add(() =>
        {
            bool has = HasTrack(), use = CarrierI() == 2;
            srcName.Text = has ? SidechainSourcePicker.Name(engine, engine.DeviceSidechainKey(track, di)) : "None";
            srcName.Foreground = has && use ? TextPrimary : TextDisabled;
            dot.Background = has ? ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(engine, Source())) : NotaPalette.Rule;
        });
        picker.PointerEntered += (_, _) => picker.BorderBrush = Brass;
        picker.PointerExited += (_, _) => picker.BorderBrush = NotaPalette.Rule;
        picker.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(picker).Properties.IsLeftButtonPressed) return;
            var fly = new MenuFlyout();
            var cur = HasTrack() ? engine.DeviceSidechainKey(track, di) : SidechainKey.None;
            void Add(SidechainKey k, string name, bool enabled = true, string? tip = null)
            {
                // The current source is ticked, like every choice menu (spec 034); one the route
                // pool can't take is shown disabled with the reason.
                var mi = new MenuItem { Header = name, ToggleType = MenuItemToggleType.Radio, IsChecked = k == cur, IsEnabled = enabled };
                if (tip != null) ToolTip.SetTip(mi, tip);
                mi.Click += (_, _) =>
                {
                    if (!SidechainSourcePicker.SetDeviceKey(engine, track, di, k)) return;   // no free route
                    if (k.Source >= 0 && CarrierI() != 2) SetP(Carrier, 1);   // picking a track means using it
                    ctx.NotifyChanged(); RefreshAll();
                };
                fly.Items.Add(mi);
            }
            Add(SidechainKey.None, "None");
            fly.Items.Add(new Separator());
            // The candidate tracks, each with its pads / chains / voices (specs/050).
            var ok = new HashSet<int>();
            foreach (var c in Candidates()) ok.Add(c.Id);
            foreach (var en in SidechainSourcePicker.Entries(engine, track, cur))
                if (ok.Contains(en.Key.Source))
                    Add(en.Key, SidechainSourcePicker.Name(engine, en.Key), en.Enabled, en.Tip);
            fly.ShowAt(picker);
            e.Handled = true;
        };
        ToolTip.SetTip(picker, Loc.T("device.vocoder.carrier_track_the_track_the"));

        var tap = Segments(new[] { "Pre", "Post" }, () => engine.DeviceSidechainTapPre(track, di) ? 0 : 1,
            i => { engine.SetDeviceSidechainTapPre(track, di, i == 0); ctx.NotifyChanged(); }, out var tapSync, padX: 3, fontSize: 8,
            dim: () => CarrierI() != 2 || !HasTrack());
        readouts.Add(tapSync);
        ToolTip.SetTip(tap, Loc.T("device.vocoder.where_the_carrier_track_is"));
        var trackRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 4 };
        trackRow.Children.Add(picker);
        trackRow.Children.Add(Col(tap, 1));

        var tilt = Slider("Tilt", Tilt, v => DbF((v - 0.5) * 24), Loc.T("device.vocoder.tilt_brightens_or_darkens_the"),
            dim: () => CarrierI() == 1, bipolar: true);
        var pitch = Slider("Pitch", Pitch, v => NoteName(PitchOf(v) + 12), Loc.T("device.vocoder.pitch_the_internal_saws_note"),
            dim: () => CarrierI() != 0, steps: 85);

        // ======================================================================
        // CENTRE — Bands: the envelope graph over the band knobs
        // ======================================================================
        var info = Mono("", 7, TextTertiary);
        readouts.Add(() => info.Text = NotaNum.F($"{BandsOf(P(Bands))} · ") + HzF(LowHz(P(Low))) + " – " + HzF(HighHz(P(High))));

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
        ToolTip.SetTip(graph, MenuKit.Keys(Loc.T("device.vocoder.the_modulators_level_in_each")));
        ValueEntry.AttachHandles(graph, [ValueEntry.DeviceParam(engine, track, di, Low, "Low", v => HzF(LowHz(v)), RefreshAll)],
            [ValueEntry.DeviceParam(engine, track, di, High, "High", v => HzF(HighHz(v)), RefreshAll)]);

        Control BandsK(double cw = 50) => K(Bands, "Bands", v => BandsOf(v).ToString(NotaNum.Culture), Loc.T("device.vocoder.bands_how_many_bands_8"), steps: 25, cellW: cw);
        Control FormantK(double cw = 50) => K(Formant, "Formant", v => StF(SemisOf(v)), Loc.T("device.vocoder.formant_moves_the_carriers_bands"), steps: 25, cellW: cw);
        Control MixK(double size, double cw) => K(Mix, "Dry/wet", v => NotaNum.F($"{v * 100:0}\u2009%"), Loc.T("device.vocoder.mix_the_carrier_as_it"), size: size, cellW: cw);
        Control GainK(double cw) => K(Gain, "Gain", v => DbF((v - 0.5) * 24), Loc.T("device.vocoder.gain_the_output_level_12"), cellW: cw);

        // ======================================================================
        // Frames: L (the Bands view, the Carrier panel, Unvoiced · Dry/wet · Gain under it) · S
        // (the graph over the carrier choice and Bands, Formant, Dry/wet and Gain)
        // ======================================================================
        Control root;
        if (CardLayout.IsMini(ctx, di))
            root = CardLayout.Mini(graph, CarSeg(), BandsK(56), FormantK(56), MixK(34, 56), GainK(56));
        else
        {
            var hold = Switch("Hold", () => P(Hold) >= 0.5f, () => { SetP(Hold, P(Hold) >= 0.5f ? 0 : 1); RefreshAll(); }, out var holdSync);
            readouts.Add(holdSync);
            Learn(hold, Hold);
            ToolTip.SetTip(hold, Loc.T("device.vocoder.hold_freezes_every_band_where"));
            var bandsView = CardLayout.View(graph,
                CardLayout.KnobColumn(
                    K(Q, "Q", v => NotaNum.F($"{1 + v * 9:0.0}"), Loc.T("device.vocoder.q_how_narrow_each_band"), cellW: 50),
                    K(Attack, "Attack", v => MsF(ExpMap(v, 0.5, 100)), Loc.T("device.vocoder.attack_how_fast_a_band"), cellW: 50),
                    K(Release, "Release", v => MsF(ExpMap(v, 5, 1000)), Loc.T("device.vocoder.release_how_fast_a_band"), cellW: 50)),
                CardLayout.Line(6,
                    new Border { Width = 150, Child = Slider("Bands", Bands, v => BandsOf(v).ToString(NotaNum.Culture), Loc.T("device.vocoder.bands_how_many_bands_8"), steps: 25) },
                    new Border { Width = 150, Child = Slider("Formant", Formant, v => StF(SemisOf(v)), Loc.T("device.vocoder.formant_moves_the_carriers_bands"), steps: 25) },
                    hold));
            (Control, Control) CarrierPanel() => (CardLayout.Head(CardLayout.Title(Loc.T("device.vocoder.carrier")), CarSeg()), CardLayout.Rows(
                trackRow, tilt, pitch, CardLayout.Rule(), modGain, keyRow, outRow));
            root = CardLayout.Large(new[] { "Bands" }, 0, _ => bandsView, _ => CarrierPanel(), info,
                CardLayout.Output(
                    K(Unvoiced, "Unvoiced", v => NotaNum.F($"{v * 100:0}\u2009%"), Loc.T("device.vocoder.unvoiced_adds_noise_where_the"), cellW: 54),
                    MixK(44, 58), GainK(54)),
                _ => RefreshAll());
        }

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
