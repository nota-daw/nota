// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Vocoder body (device kind 26, specs/028-vocoder-effect), a
// build of the spec's design mockup (700 × 260) in three columns:
//
//   Modulator / Carrier   the modulator is this track (the voice): its level and Mod gain ·
//                         the carrier: Saw / Noise / Track, its Pre / Post tap, the carrier
//                         track's picker (with its colour; the device sidechain), Tilt and
//                         the saw's Pitch
//   Bands                 the envelope graph (bars = the modulator per band, a grey line = the
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
    public bool FullBleed => true;
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
        // A section's title: 12 / semibold, ink 1.
        static TextBlock Title(string t) => new()
        { Text = t, FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T Row<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(left); g.Children.Add(Col(right, 2));
            return g;
        }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.Rule, VerticalAlignment = VerticalAlignment.Center };

        // A pressed-in section: a 26px title row over the body.
        RuleSection Island(Control head, Control body, double width = double.NaN, double padBottom = 9)
        {
            var headBar = new Border { Height = 26, Child = head };
            return new RuleSection
            {
                Width = width, Padding = new Thickness(SectionPad, SectionPad, SectionPad, padBottom),
                Child = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), body } },
            };
        }

        // Slider row: label · value bar · value. `dim` draws the row in ink 4.
        Grid Slider(string label, int p, Func<double, string> fmt, string tip, double labelW, Func<bool>? dim = null, bool bipolar = false, int steps = 0)
        {
            var row = SliderRow(label, () => P(p), v =>
            {
                if (steps > 1) v = Math.Round(v * (steps - 1)) / (steps - 1);
                Raw(p, v); RefreshAll();
            }, () => fmt(P(p)), out var sync, begin: () => Begin(p), end: () => End(p), reset: () => { Reset(p); RefreshAll(); },
                bipolar: bipolar, dim: dim, labelWidth: labelW - 8, valueWidth: 46);
            readouts.Add(sync);
            Learn(row, p);
            ToolTip.SetTip(row, tip);
            return row;
        }

        Control K(int p, string name, Func<double, string> fmt, string tip, int steps = 0)
        {
            var val = FxParts.Value(fmt(P(p)));
            var knob = new Knob(P(p), 1.0) { Accent = true, Default = engine.DeviceParamDefault(track, di, p) };
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
            return KnobCell(name, knob, val, 42);
        }

        // ======================================================================
        // LEFT — Modulator (this track: its level, Mod gain) · Carrier (Saw / Noise / Track,
        // the tap, the carrier track's picker, Tilt, Pitch)
        // ======================================================================
        const double LW = 48;
        var keyMeter = new MeterBar(horizontal: true) { Height = 6, Width = 64, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(keyMeter, "The voice's level after Mod gain");
        var modTitle = Title("Modulator");
        ToolTip.SetTip(modTitle, "The modulator is the sound of the track the vocoder sits on — the voice");
        var modGain = Slider("Gain", ModGain, v => DbF((v - 0.5) * 48), "Mod gain — trims the voice before the bands hear it, ±24 dB", LW,
            bipolar: true);

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

        // The carrier track's picker, raised like a button: the track's colour, its name 11 / ink 1
        // (ink 4 when it is not in use) and a chevron.
        var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var srcName = new TextBlock { FontSize = NotaType.KnobValue, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var chev = new Glyph(GlyphKind.ChevronDown, 9) { Margin = new Thickness(6, 0, 0, 0), Foreground = TextSecondary };
        var picker = new Border
        {
            Height = NotaSize.Dropdown, Padding = new Thickness(8, 0, 6, 0), CornerRadius = NotaRadius.Field, Background = NotaPalette.SurfaceHover,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = new DockPanel { Children = { Docked(dot, Dock.Left), Docked(chev, Dock.Right), srcName } },
        };
        picker.BindResource(Border.BoxShadowProperty, "Shadow.Lifted");
        readouts.Add(() =>
        {
            bool has = HasTrack(), use = CarrierI() == 2;
            srcName.Text = has ? TrackNames.Of(engine, Source()) : "None";
            srcName.Foreground = has && use ? TextPrimary : TextDisabled;
            dot.Background = has ? ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(engine, Source())) : NotaPalette.Rule;
        });
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
            i => { engine.SetDeviceSidechainTapPre(track, di, i == 0); ctx.NotifyChanged(); }, out var tapSync, padX: 5,
            dim: () => CarrierI() != 2 || !HasTrack());
        readouts.Add(tapSync);
        ToolTip.SetTip(tap, "Where the carrier track is tapped: before its effects and fader, or after. Pre keeps feeding while the track is muted");

        var tilt = Slider("Tilt", Tilt, v => DbF((v - 0.5) * 24), "Tilt — brightens (or darkens) the saw or the carrier track around 1 kHz before the bands, ±12 dB", LW,
            dim: () => CarrierI() == 1, bipolar: true);
        var pitch = Slider("Pitch", Pitch, v => NoteName(PitchOf(v) + 12), "Pitch — the internal saw's note, C0 … C7", LW,
            dim: () => CarrierI() != 0, steps: 85);

        var leftBody = new Grid { RowDefinitions = new RowDefinitions("24,11,26,24,4,24,24,24"), Margin = new Thickness(0, 2, 0, 0) };
        leftBody.Children.Add(modGain);
        leftBody.Children.Add(Row(Rule(), 1));
        leftBody.Children.Add(Row(HeadRow(Title("Carrier"), tap), 2));
        leftBody.Children.Add(Row(carSeg, 3));
        leftBody.Children.Add(Row(picker, 5));
        leftBody.Children.Add(Row(tilt, 6));
        leftBody.Children.Add(Row(pitch, 7));
        var left = Island(HeadRow(modTitle, keyMeter), leftBody, 172);
        DockPanel.SetDock(left, Dock.Left);

        // ======================================================================
        // CENTRE — Bands: the envelope graph over the band knobs
        // ======================================================================
        var info = FxParts.Info();
        readouts.Add(() => info.Text = NotaNum.F($"{BandsOf(P(Bands))} · ") + HzF(LowHz(P(Low))) + " – " + HzF(HighHz(P(High))));
        ToolTip.SetTip(info, "How many bands, and the range they cover");

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
        ToolTip.SetTip(graph, "The modulator's level in each band (bars) and the carrier's (the line). Drag an edge to move Low or High; double-click an edge resets it.");

        var knobs = new UniformGrid { Rows = 1, Columns = 7, Margin = new Thickness(0, 8, 0, 0) };
        knobs.Children.Add(K(Bands, "Bands", v => BandsOf(v).ToString(NotaNum.Culture), "Bands — how many bands, 8 … 32: fewer is more robotic, more is clearer", steps: 25));
        knobs.Children.Add(K(Low, "Low", v => HzF(LowHz(v)), "Low — the lowest band's edge, 20 Hz … 1 kHz"));
        knobs.Children.Add(K(High, "High", v => HzF(HighHz(v)), "High — the highest band's edge, 2 … 20 kHz"));
        knobs.Children.Add(K(Q, "Q", v => NotaNum.F($"{1 + v * 9:0.0}"), "Q — how narrow each band is, 1 … 10"));
        knobs.Children.Add(K(Formant, "Formant", v => StF(SemisOf(v)), "Formant — moves the carrier's bands against the modulator's, ±12 st: up sounds smaller, down larger", steps: 25));
        knobs.Children.Add(K(Attack, "Attack", v => MsF(ExpMap(v, 0.5, 100)), "Attack — how fast a band opens, 0.5 … 100 ms"));
        knobs.Children.Add(K(Release, "Release", v => MsF(ExpMap(v, 5, 1000)), "Release — how fast a band closes, 5 … 1000 ms"));

        var centreBody = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 2, 0, 0), Children = { Docked(knobs, Dock.Bottom), graph } };
        var centre = Island(HeadRow(Title("Bands"), info), centreBody, padBottom: 6);
        centre.Margin = new Thickness(SectionGap, 0);

        // ======================================================================
        // RIGHT — Output: Unvoiced · Mix · Gain · Hold · the output meter
        // ======================================================================
        const double RW = 56;
        var unv = Slider("Unvoiced", Unvoiced, v => NotaNum.F($"{v * 100:0} %"), "Unvoiced — adds noise where the modulator hisses, so consonants stay clear", RW);
        var mix = Slider("Mix", Mix, v => NotaNum.F($"{v * 100:0} %"), "Mix — the carrier as it came in against the vocoded sound", RW);
        var gain = Slider("Gain", Gain, v => DbF((v - 0.5) * 24), "Gain — the output level, ±12 dB", RW, bipolar: true);
        var hold = Switch("Hold", () => P(Hold) >= 0.5f, () => { SetP(Hold, P(Hold) >= 0.5f ? 0 : 1); RefreshAll(); }, out var holdSync);
        readouts.Add(holdSync);
        Learn(hold, Hold);
        ToolTip.SetTip(hold, "Hold — freezes every band where it is; the carrier keeps sounding through it");
        hold.HorizontalAlignment = HorizontalAlignment.Left;

        var outMeter = new MeterBar(horizontal: true) { Height = 6, VerticalAlignment = VerticalAlignment.Center };
        var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{RW},*") };
        outRow.Children.Add(FxParts.Label("Out"));
        outRow.Children.Add(Col(outMeter, 1));
        ToolTip.SetTip(outRow, "The vocoder's output level");

        var rightBody = new Grid { RowDefinitions = new RowDefinitions("24,24,24,11,24,*,24"), Margin = new Thickness(0, 2, 0, 0) };
        rightBody.Children.Add(unv);
        rightBody.Children.Add(Row(mix, 1));
        rightBody.Children.Add(Row(gain, 2));
        rightBody.Children.Add(Row(Rule(), 3));
        rightBody.Children.Add(Row(hold, 4));
        rightBody.Children.Add(Row(outRow, 6));
        var right = Island(Title("Output"), rightBody, 172);
        DockPanel.SetDock(right, Dock.Right);

        // ---- assemble ---------------------------------------------------------------
        var root = new DockPanel { LastChildFill = true, Margin = new Thickness(0, CardPad, 0, 0), Children = { left, right, centre } };

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
