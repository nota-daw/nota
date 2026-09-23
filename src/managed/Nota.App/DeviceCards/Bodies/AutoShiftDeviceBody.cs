// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Auto Shift body (pitch correction, device kind 10), a build
// of the "Nota Auto Shift" mockup (700 × 260) on the Shutter / Utility / Valve frame: an
// always-visible PITCH column (where the voice sits · the correction now), a centre panel
// with Trace / Scale / MIDI tabs — the detected and corrected pitch on the scale's note
// lanes, the sung-note histogram under the scale's notes (click to edit, Learn), the MIDI
// target from another track's notes — a right panel with Shift / Detect tabs, and a status
// strip. The pictures come from the engine (AutoShift.h scopeRead). Every control is a
// device param, so automation / MIDI learn / presets / A-B / persistence come for free;
// Learn and Reset are device actions, the MIDI source rides on the sidechain source, and
// Follow (copy a Nota Scale's key and scale) is driven from here.
// FullBleed — the shared shell draws the header (name · preset · badge · bypass).

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

internal sealed class AutoShiftDeviceBody : IDeviceBody
{
    // ── Parameter indices (must match AutoShift.h) ───────────────────────────
    private const int Key = 0, Scale = 1, Amount = 2, Speed = 3, Shift = 4, Mix = 5, Range = 6, Formant = 7, KeySrc = 8, Follow = 9,
        Human = 10, Fine = 11, FormantShift = 12, DetLow = 13, DetHigh = 14, DetSens = 15, SkipSib = 16, Custom = 17, NoteC = 18,
        MidiMode = 30, MidiLatch = 31, MidiOct = 32, MidiGlide = 33, MidiBendRange = 34, MidiBend = 35;
    // Scope layout (AutoShift::S_* / kTele / kHist).
    private const int S_DetMidi = 0, S_TargetMidi = 1, S_OutMidi = 2, S_CorrCents = 3, S_Conf = 4, S_Hz = 5, S_Voiced = 6, S_Sibilant = 7,
        S_SampleRate = 8, S_Cpu = 9, S_Latency = 10, S_MidiNote = 11, S_MidiVel = 12, S_MidiHeld = 13, S_MidiLive = 14, S_Learning = 15,
        S_LearnKey = 16, S_LearnScale = 17, S_LearnMatch = 18, S_Learn2Key = 19, S_Learn2Scale = 20, S_Learn2Match = 21, S_InScale = 22,
        S_AnalysisSec = 23, S_WindowSec = 24, S_HistN = 25, S_Mask = 26, S_Key = 27, S_Ratio = 28, S_InDb = 29, S_Bars = 30,
        kTele = 32, kHist = 384;
    private const int PcAt = kTele, H_Det = kTele + 12, H_Out = H_Det + kHist, H_Midi = H_Out + kHist, kScope = H_Midi + kHist;
    private const int A_ResetAnalysis = 0, A_Learn = 1;

    private static readonly string[] Keys = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private static readonly string[] Scales = { "Chromatic", "Major", "Minor", "Penta Maj", "Penta Min" };
    private static readonly int[] Masks = { 0x0FFF, 0x0AB5, 0x05AD, 0x0295, 0x04A9 };   // relative to the key
    // More modes: picked from the scale list, they become a Custom scale on the key.
    private static readonly (string Name, int Mask)[] Modes =
    {
        ("Harmonic Minor", 0x09AD), ("Melodic Minor", 0x0AAD), ("Dorian", 0x06AD), ("Phrygian", 0x05AB),
        ("Lydian", 0x0AD5), ("Mixolydian", 0x06B5), ("Blues", 0x04E9), ("Whole Tone", 0x0555),
    };
    // Detection presets (Det Low / Det High as MIDI notes).
    private static readonly (string Name, int Lo, int Hi)[] Voices =
    {
        ("Voice · all", 36, 84), ("Voice · soprano", 57, 88), ("Voice · alto", 52, 84), ("Voice · tenor", 45, 76),
        ("Voice · baritone", 40, 69), ("Voice · bass", 36, 64), ("Instrument", 28, 96),
    };

    public double Width => 700;
    public bool FullBleed => true;
    public string? Subtitle => "PITCH";

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        float P(int p) => engine.DeviceGetParam(track, di, p);
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, double v) => engine.DeviceSetParam(track, di, p, (float)Math.Clamp(v, 0, 1));
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Reset(int p) { Begin(p); Raw(p, engine.DeviceParamDefault(track, di, p)); End(p); }
        double Def(int p) => engine.DeviceParamDefault(track, di, p);
        bool On(int p) => P(p) >= 0.5f;
        int Sel(int p, int n) => Math.Clamp((int)Math.Round(P(p) * (n - 1)), 0, n - 1);

        var readouts = new List<Action>();
        void RefreshAll() { for (int i = 0; i < readouts.Count; i++) readouts[i](); }
        var scope = new float[kScope];
        int scN = 0;
        double Sc(int i) => scN > i ? scope[i] : 0;
        bool Blink() => Environment.TickCount64 % 1200 < 820;

        // ---- units ----------------------------------------------------------------
        static double Exp(double v, double lo, double hi) => lo * Math.Pow(hi / lo, Math.Clamp(v, 0, 1));
        static string Note(int m) => Keys[((m % 12) + 12) % 12] + (m / 12 - 1);
        static string CentsF(double c) => NotaNum.F($"{c:+0;−0;0} ¢");
        static string MsF(double ms) => ms >= 100 ? NotaNum.F($"{ms:0} ms") : ms >= 10 ? NotaNum.F($"{ms:0} ms") : NotaNum.F($"{ms:0.#} ms");
        int KeyOf() => Math.Clamp((int)Math.Round(P(Key) * 11), 0, 11);
        int ScaleOf() => Sel(Scale, 5);
        int Src() => P(KeySrc) < 0.25f ? 0 : P(KeySrc) < 0.75f ? 1 : 2;
        bool MidiOn() => Src() == 2;
        int CustomMask() { int m = 0; for (int i = 0; i < 12; i++) if (On(NoteC + i)) m |= 1 << i; return m; }
        static int Rot(int m, int k) { k = ((k % 12) + 12) % 12; return ((m << k) | (m >> (12 - k))) & 0x0FFF; }
        int ActiveMask() => On(Custom) ? CustomMask() : Rot(Masks[ScaleOf()], KeyOf());
        string ScaleName()
        {
            if (!On(Custom)) return Scales[ScaleOf()];
            int m = CustomMask(), k = KeyOf();
            for (int i = 0; i < Masks.Length; i++) if (Rot(Masks[i], k) == m) return Scales[i];
            foreach (var md in Modes) if (Rot(md.Mask, k) == m) return md.Name;
            return "Custom";
        }
        string KeyScale() => $"{Keys[KeyOf()]} {ScaleName()}";
        double SpeedMs() => Exp(P(Speed), 2, 300);
        double RangeSt() => 1 + P(Range) * 11;
        double ShiftSt() => (P(Shift) - 0.5) * 24;
        double FineCt() => (P(Fine) - 0.5) * 200;
        double FormPct() => (P(FormantShift) - 0.5) * 200;
        double GlideMs() => Exp(P(MidiGlide), 5, 800);
        double BendSt() => P(MidiBendRange) * 6;
        int LowNote() => 24 + (int)Math.Round(P(DetLow) * 72);
        int HighNote() => 24 + (int)Math.Round(P(DetHigh) * 72);
        string VoiceName()
        {
            foreach (var v in Voices) if (v.Lo == LowNote() && v.Hi == HighNote()) return v.Name;
            return "Custom";
        }
        string ShiftF(double v) => NotaNum.F($"{(v - 0.5) * 24:+0;−0;0} st");
        string FineF(double v) => CentsF((v - 0.5) * 200);
        string FormF(double v) => NotaNum.F($"{(v - 0.5) * 200:+0;−0;0} %");
        string PctF(double v) => NotaNum.F($"{v * 100:0} %");
        string NoteF(double v) => Note(24 + (int)Math.Round(v * 72));

        // Key-source MIDI track: the sidechain source slot.
        int MidiSrc() => engine.DeviceSidechainSource(track, di);
        string TrackName(int id)
        {
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == id)
                { string n = engine.GetTrackName(id); return n.Length > 0 ? n : NotaNum.F($"Track {i + 1}"); }
            return "—";
        }
        string MidiSrcName() => MidiSrc() >= 0 ? TrackName(MidiSrc()) : "none";

        // Key change: a Custom scale moves with its tonic.
        void SetKey(int k)
        {
            int old = KeyOf();
            if (On(Custom) && k != old)
            {
                int m = Rot(CustomMask(), k - old);
                for (int i = 0; i < 12; i++) if (On(NoteC + i) != ((m >> i & 1) != 0)) SetP(NoteC + i, (m >> i & 1) != 0 ? 1f : 0f);
            }
            SetP(Key, k / 11f);
            if (Src() == 0) SetP(KeySrc, 0.5f);   // picking a key by hand leaves Auto
        }
        void SetScale(int s) { SetP(Scale, s / 4f); if (On(Custom)) SetP(Custom, 0f); }
        void SetCustom(int mask)
        {
            for (int i = 0; i < 12; i++) { bool on = (mask >> i & 1) != 0; if (On(NoteC + i) != on) SetP(NoteC + i, on ? 1f : 0f); }
            if (!On(Custom)) SetP(Custom, 1f);
        }
        void ToggleNote(int pc)
        {
            int m = ActiveMask() ^ (1 << pc);
            if (m == 0) return;   // keep at least one note
            SetCustom(m);
            RefreshAll();
        }
        void Learn()
        {
            engine.DeviceAction(track, di, A_Learn, Sc(S_Learning) > 0.5 ? 0 : 1, 0);
            ctx.NotifyChanged();
        }

        // Follow: copy the key and scale of a Nota Scale — on this track, on the MIDI source, or anywhere.
        (int track, int idx) FindScaleDevice()
        {
            (int, int) On(int t)
            {
                int n = engine.TrackMidiEffectCount(t);
                for (int i = 0; i < n; i++) if (engine.MidiEffectName(t, i) == "Nota Scale") return (t, i);
                return (-1, -1);
            }
            var r = On(track);
            if (r.Item1 < 0 && MidiSrc() >= 0) r = On(MidiSrc());
            for (int i = 0; i < engine.TrackCount && r.Item1 < 0; i++)
                if (engine.TryGetTrackInfo(i, out var ti)) r = On(ti.Id);
            return r;
        }
        long followAt = 0;
        void FollowTick()
        {
            if (!On(Follow) || Environment.TickCount64 - followAt < 250) return;
            followAt = Environment.TickCount64;
            var (ft, fi) = FindScaleDevice();
            if (ft < 0) return;
            int root = Math.Clamp((int)Math.Round(engine.MidiEffectGetParam(ft, fi, 0)), 0, 11);
            int sc = Math.Clamp((int)Math.Round(engine.MidiEffectGetParam(ft, fi, 1)), 0, 10);
            int[] preset = { 2741, 1453, 2477, 1709, 1451, 2773, 1717, 661, 1193, 4095 };
            int rel = 0;
            if (sc < 10) rel = preset[sc];
            else for (int i = 0; i < 12; i++) if (engine.MidiEffectGetParam(ft, fi, 3 + i) >= 0.5f) rel |= 1 << i;
            if (rel == 0) return;
            int abs = Rot(rel, root);
            if (KeyOf() != root) Raw(Key, root / 11.0);
            int named = Array.FindIndex(Masks, m => Rot(m, root) == abs);
            if (named >= 0) { if (ScaleOf() != named) Raw(Scale, named / 4.0); if (On(Custom)) Raw(Custom, 0); }
            else if (ActiveMask() != abs || !On(Custom))
            {
                for (int i = 0; i < 12; i++) Raw(NoteC + i, (abs >> i & 1) != 0 ? 1 : 0);
                Raw(Custom, 1);
            }
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = 7) => new()
        { Text = t, FontSize = fs, FontWeight = FontWeight.Bold, Foreground = c ?? TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        static StackPanel Row(double sp, params Control[] cs)
        { var s = new StackPanel { Orientation = Orientation.Horizontal, Spacing = sp, VerticalAlignment = VerticalAlignment.Center }; foreach (var c in cs) s.Children.Add(c); return s; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void LearnBind(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, string? tip = null)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Default = Def(p), Width = 34, Height = 34 };
            knob.ValueChanged += v => { Raw(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            LearnBind(knob, p);
            if (tip is not null) ToolTip.SetTip(knob, tip);
            readouts.Add(() => { if (knob.Dragging) return; double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, 44);
        }

        // On/off switch bound to a param (>= 0.5 = on).
        Control Toggle(int p, string label, string tip)
        {
            var wrap = Switch(label, () => On(p), () => { SetP(p, On(p) ? 0f : 1f); RefreshAll(); }, out var sync);
            readouts.Add(sync);
            LearnBind(wrap, p);
            ToolTip.SetTip(wrap, tip);
            return wrap;
        }

        // Segmented pill over a normalized discrete param.
        Control Seg(int p, string[] names, string tip, Action<int>? pick = null, Func<int>? current = null)
        {
            int n = names.Length;
            var seg = Segments(names, current ?? (() => Sel(p, n)), iv => { if (pick is not null) pick(iv); else SetP(p, n > 1 ? iv / (float)(n - 1) : 0f); RefreshAll(); }, out var sync, padX: 5);
            readouts.Add(sync);
            LearnBind(seg, p);
            ToolTip.SetTip(seg, tip);
            return seg;
        }
        Control SourceSeg() => Seg(KeySrc, new[] { "Auto", "Manual", "MIDI" },
            "Key source — Auto follows the key you sing, Manual uses the key and scale you set, MIDI takes the target from another track's notes (MIDI tab)",
            iv => SetP(KeySrc, iv / 2f), Src);

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false, Func<bool>? blink = null)
        {
            var dot = new Border { Width = 5, Height = 5, CornerRadius = NotaRadius.Pill, Background = Brass, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border
            {
                Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, HorizontalAlignment = HorizontalAlignment.Center, Children = { dot, tb } },
            };
            if (fill) b.HorizontalAlignment = HorizontalAlignment.Stretch;
            void Hi()
            {
                bool on = lit();
                b.Background = on ? NotaPalette.AccentSubtle : fill ? Raised : Brushes.Transparent;
                b.BorderBrush = on ? Brass : fill ? Brushes.Transparent : NotaPalette.BorderStrong;
                tb.Foreground = on ? AccentBright : fill ? TextPrimary : TextSecondary;
                tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                tb.Text = text();
                bool bl = blink?.Invoke() ?? false;
                dot.IsVisible = bl;
                dot.Opacity = bl && !Blink() ? 0.3 : 1;
            }
            b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; click(); RefreshAll(); e.Handled = true; };
            ToolTip.SetTip(b, tip);
            if (p >= 0) LearnBind(b, p);
            readouts.Add(Hi); Hi();
            return b;
        }
        Border ToggleLatch(int p, string text, string tip) => Latch(p, () => On(p), () => SetP(p, On(p) ? 0f : 1f), () => text, tip);
        Border LearnLatch() => Latch(-1, () => Sc(S_Learning) > 0.5, Learn, () => "Learn",
            "Learn — listen to what is sung, then click again to set the key and scale that fit it best", blink: () => Sc(S_Learning) > 0.5);

        // A dropdown box: [text ▾], opens a menu of items.
        Border Drop(Func<string> text, Func<bool> lit, Action<MenuFlyout> fill, string tip, double minW = 0, bool stretch = false, int learnP = -1)
        {
            var name = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(6, 0, 0, 0) };
            var box = new Border
            {
                Height = 16, MinWidth = minW, Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            if (stretch) box.HorizontalAlignment = HorizontalAlignment.Stretch;
            readouts.Add(() =>
            {
                bool on = lit();
                name.Text = text();
                name.Foreground = on ? AccentBright : TextPrimary;
                name.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                box.BorderBrush = on ? Brass : BorderDef;
                chev.Foreground = on ? NotaPalette.AccentDim : TextTertiary;
            });
            ToolTip.SetTip(box, tip);
            if (learnP >= 0) LearnBind(box, learnP);
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var fly = new MenuFlyout();
                fill(fly);
                fly.ShowAt(box);
                e.Handled = true;
            };
            return box;
        }
        static MenuItem Item(string label, bool cur, Action pick)
        {
            var mi = new MenuItem { Header = label };
            if (cur) mi.Icon = new Avalonia.Controls.Shapes.Ellipse { Width = 6, Height = 6, Fill = Brass };
            mi.Click += (_, _) => pick();
            return mi;
        }
        Control KeyDrop(bool lit) => Drop(() => Keys[KeyOf()], () => lit, fly =>
        {
            for (int k = 0; k < 12; k++) { int kv = k; fly.Items.Add(Item(Keys[k], KeyOf() == k, () => { SetKey(kv); RefreshAll(); })); }
        }, "Key — the tonic; the scale's notes are counted from it", 0, false, Key);
        Control ScaleDrop() => Drop(ScaleName, () => false, fly =>
        {
            for (int s = 0; s < Scales.Length; s++) { int sv = s; fly.Items.Add(Item(Scales[s], !On(Custom) && ScaleOf() == s, () => { SetScale(sv); RefreshAll(); })); }
            fly.Items.Add(new Separator());
            foreach (var md in Modes)
            {
                var m = md;
                fly.Items.Add(Item(m.Name, On(Custom) && CustomMask() == Rot(m.Mask, KeyOf()), () => { SetCustom(Rot(m.Mask, KeyOf())); RefreshAll(); }));
            }
            fly.Items.Add(new Separator());
            fly.Items.Add(Item("Custom — edit on the Scale tab", On(Custom), () => { SetCustom(ActiveMask()); RefreshAll(); }));
        }, "Scale — the notes the voice is pulled to", 62, false, Scale);

        // The MIDI source: [name ▾] lists the instrument tracks. Picking one switches Key Source to MIDI.
        Control FromDrop() => Drop(MidiSrcName, () => MidiSrc() >= 0 && MidiOn(), fly =>
        {
            fly.Items.Add(Item("None", MidiSrc() < 0, () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); }));
            for (int i = 0; i < engine.TrackCount; i++)
                if (engine.TryGetTrackInfo(i, out var ti) && ti.Id != track && ti.IsInstrument)
                {
                    int id = ti.Id;
                    fly.Items.Add(Item(TrackName(id), MidiSrc() == id, () =>
                    {
                        engine.SetDeviceSidechainSource(track, di, id);
                        if (!MidiOn()) SetP(KeySrc, 1f);
                        ctx.NotifyChanged(); RefreshAll();
                    }));
                }
        }, "From — the instrument track whose notes set the target (its MIDI clips and effects, even when muted)", 74, false, KeySrc);

        Control VoiceDrop() => Drop(VoiceName, () => false, fly =>
        {
            foreach (var v in Voices)
            {
                var vv = v;
                fly.Items.Add(Item(NotaNum.F($"{vv.Name}  ({Note(vv.Lo)}…{Note(vv.Hi)})"), LowNote() == vv.Lo && HighNote() == vv.Hi, () =>
                { SetP(DetLow, (vv.Lo - 24) / 72f); SetP(DetHigh, (vv.Hi - 24) / 72f); RefreshAll(); }));
            }
        }, "Source — sets the detection range for a voice type or an instrument", 0, true);

        // Slider row: caps label · track · mono value.
        Control SliderRow(string label, int p, Func<double, string> fmt, bool bipolar = false, bool modulation = false, IBrush? valueInk = null)
        {
            var bar = DeviceCardKit.SliderRow("", () => P(p), v => { Raw(p, v); RefreshAll(); }, () => fmt(P(p)), out var sync,
                begin: () => Begin(p), end: () => End(p), reset: () => Reset(p), bipolar: bipolar, valueWidth: 34, modulation: modulation);
            readouts.Add(sync);
            LearnBind(bar, p);
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("44,*"), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(bar, 1));
            return g;
        }

        static Grid HeadRow(Control left, Control right)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18, ColumnSpacing = 6 };
            g.Children.Add(left); g.Children.Add(Col(right, 1));
            return g;
        }
        TextBlock RightInfo(Func<string> text)
        {
            var t = Mono("", 7, AccentBright);
            readouts.Add(() => t.Text = text());
            return t;
        }
        Control KnobRow(Control[] knobs, Func<string> info1, Func<string> info2)
        {
            var i1 = Mono("", 7, TextTertiary); i1.HorizontalAlignment = HorizontalAlignment.Right;
            var i2 = Mono("", 7, TextTertiary); i2.HorizontalAlignment = HorizontalAlignment.Right;
            i1.TextTrimming = i2.TextTrimming = TextTrimming.CharacterEllipsis;
            readouts.Add(() => { i1.Text = info1(); i2.Text = info2(); });
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 52, ColumnSpacing = 6 };
            g.Children.Add(Row(4, knobs));
            g.Children.Add(Col(new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { i1, i2 } }, 1));
            return g;
        }
        static Control TabBody(Control head, Control window, Control bottom) => new DockPanel
        {
            LastChildFill = true, Margin = new Thickness(8, 5, 8, 0),
            Children = { Docked(head, Dock.Top), Docked(bottom, Dock.Bottom), new Border { Margin = new Thickness(0, 5, 0, 0), Child = window } },
        };

        // Live readings shared by the tabs.
        bool Voiced() => Sc(S_Voiced) > 0.5;
        int DetNote() => (int)Math.Round(Sc(S_DetMidi));
        double DetCents() => (Sc(S_DetMidi) - DetNote()) * 100;
        int TargetNote() => Sc(S_TargetMidi) > 0.5 ? (int)Math.Round(Sc(S_TargetMidi)) : -1;
        string InputText() => Voiced() ? NotaNum.F($"{Note(DetNote())} {CentsF(DetCents())}") : Sc(S_Sibilant) > 0.5 ? "sibilant" : "—";
        string TargetText() => TargetNote() >= 0 ? Note(TargetNote()) : "—";

        // ======================================================================
        // LEFT — PITCH column (where the voice sits · the correction now)
        // ======================================================================
        var pitchTitle = Caps("PITCH"); pitchTitle.HorizontalAlignment = HorizontalAlignment.Center;
        var inPill = new AsPill { Ink = Brass, VerticalAlignment = VerticalAlignment.Stretch };
        var corrPill = new AsPill { Ink = Teal, Bipolar = true, VerticalAlignment = VerticalAlignment.Stretch };
        var inLbl = Caps("IN"); inLbl.HorizontalAlignment = HorizontalAlignment.Center;
        var corrLbl = Caps("CORR"); corrLbl.HorizontalAlignment = HorizontalAlignment.Center;
        ToolTip.SetTip(inPill, "The detected pitch, low to high across the detection range (Detect tab)");
        ToolTip.SetTip(corrPill, "The correction now — up or down from the centre, full height = 2 semitones");
        var pitchNote = Mono("—", 9, AccentBright); pitchNote.HorizontalAlignment = HorizontalAlignment.Center; pitchNote.FontWeight = FontWeight.Medium;
        var pitchCents = Mono("", 7, TextPrimary); pitchCents.HorizontalAlignment = HorizontalAlignment.Center;
        readouts.Add(() =>
        {
            bool v = Voiced();
            double lo = LowNote(), hi = Math.Max(lo + 1, HighNote());
            inPill.Set(v ? (Sc(S_DetMidi) - lo) / (hi - lo) : double.NaN);
            corrPill.Set(v ? Sc(S_CorrCents) / 200.0 : double.NaN);
            pitchNote.Text = v ? (TargetNote() >= 0 ? Note(TargetNote()) : Note(DetNote())) : "—";
            pitchNote.Foreground = v ? AccentBright : TextTertiary;
            pitchCents.Text = v ? CentsF(Sc(S_CorrCents)) : "";
            pitchTitle.Foreground = v && (MidiOn() || Math.Abs(Sc(S_CorrCents)) > 1) ? AccentBright : TextTertiary;
        });
        Control PillCol(AsPill pill, TextBlock lbl) => new DockPanel { Children = { Docked(lbl, Dock.Bottom), new Border { Margin = new Thickness(0, 0, 0, 3), Child = pill } } };
        var pills = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 9, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
        pills.Children.Add(PillCol(inPill, inLbl));
        pills.Children.Add(Col(PillCol(corrPill, corrLbl), 1));
        var pitchCol = new Border
        {
            Width = 56, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(0, 5, 0, 4),
            Child = new DockPanel { Children = { Docked(pitchTitle, Dock.Top), Docked(pitchCents, Dock.Bottom), Docked(pitchNote, Dock.Bottom), pills } },
        };
        DockPanel.SetDock(pitchCol, Dock.Left);

        // ======================================================================
        // CENTRE — Trace
        // ======================================================================
        AsTraceView? trace = null;
        Control TraceTab()
        {
            trace = new AsTraceView();
            ToolTip.SetTip(trace, "The detected pitch (teal dots) and the corrected output (brass) over the last two seconds, on the scale's note lanes; the target lane is lit");
            var head = HeadRow(Row(6, Caps("KEY"), KeyDrop(false), ScaleDrop(), SourceSeg(), LearnLatch()), RightInfo(() => TargetNote() >= 0 ? "target " + TargetText() : ""));
            var knobs = KnobRow(new[]
            {
                K(Amount, "AMOUNT", PctF, tip: "Amount — how far the voice is pulled to the target note"),
                K(Speed, "SPEED", v => MsF(Exp(v, 2, 300)), tip: "Speed — how fast the retune glides (2 ms = hard tune)"),
                K(Range, "RANGE", v => NotaNum.F($"±{1 + v * 11:0} st"), tip: "Range — the largest correction; a note further off than this is only pulled this far"),
                K(Human, "HUMAN", PctF, Teal, "Human — keeps vibrato and slow drift around the corrected note (0 = flat)"),
            },
            () => Voiced() ? NotaNum.F($"input {InputText()} → target {TargetText()}") : "no pitch",
            () => (P(Amount) >= 0.95f && SpeedMs() < 12 ? "hard correction" : P(Amount) < 0.4f ? "light correction" : "natural correction")
                  + (P(Human) < 0.05f ? " · vibrato cut" : NotaNum.F($" · vibrato kept {P(Human) * 100:0} %")));
            return TabBody(head, trace, knobs);
        }

        // ======================================================================
        // CENTRE — Scale
        // ======================================================================
        AsHistView? histView = null;
        Control ScaleTab()
        {
            histView = new AsHistView();
            histView.NoteClicked += ToggleNote;
            ToolTip.SetTip(histView, "How long each note was sung (the last ~16 bars) over the scale's notes — brass in the scale, teal outside it. Click a column to add or remove its note");
            LearnBind(histView, Custom);
            var scaleSeg = Seg(Scale, new[] { "Chrom", "Major", "Minor", "Pent", "Custom" },
                "Scale — Chromatic, Major, Minor, Pentatonic (major or minor — click again to swap) or Custom (the notes below)",
                iv =>
                {
                    if (iv == 4) SetCustom(ActiveMask());
                    else if (iv == 3) SetScale(!On(Custom) && ScaleOf() == 3 ? 4 : !On(Custom) && ScaleOf() == 4 ? 3 : ScaleOf() == 2 ? 4 : 3);
                    else SetScale(iv);
                },
                () => On(Custom) ? 4 : ScaleOf() >= 3 ? 3 : ScaleOf());
            var head = HeadRow(Row(6, Caps("KEY"), KeyDrop(true), scaleSeg, LearnLatch()),
                RightInfo(() => Sc(S_AnalysisSec) > 0.3 ? NotaNum.F($"match {Sc(S_InScale) * 100:0} %") : ""));

            // Note chips: tonic filled, in-scale outlined, sung out-of-scale dashed teal.
            var chipGrid = new Grid { ColumnSpacing = 4, Margin = new Thickness(6, 0) };
            var chips = new Border[12]; var chipTx = new TextBlock[12];
            for (int i = 0; i < 12; i++)
            {
                int pc = i;
                chipGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var tb = new TextBlock { Text = Keys[i], FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Height = 22, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; ToggleNote(pc); e.Handled = true; };
                LearnBind(b, NoteC + i);
                ToolTip.SetTip(b, NotaNum.F($"{Keys[i]} — click to add it to the scale or take it out (makes the scale Custom)"));
                chips[i] = b; chipTx[i] = tb;
                chipGrid.Children.Add(Col(b, i));
            }
            readouts.Add(() =>
            {
                int mask = ActiveMask(), key = KeyOf();
                for (int i = 0; i < 12; i++)
                {
                    bool inS = (mask >> i & 1) != 0, tonic = inS && i == key;
                    bool sung = !inS && Sc(PcAt + i) > 0.08;
                    chips[i].Background = tonic ? Brass : inS ? NotaPalette.AccentSubtle : Sunken;
                    chips[i].BorderBrush = inS ? Brass : sung ? Teal : BorderDef;
                    chipTx[i].Foreground = tonic ? OnAccent : inS ? AccentBright : sung ? NotaPalette.TealBright : TextDisabled;
                    chipTx[i].FontWeight = tonic ? FontWeight.Bold : inS ? FontWeight.SemiBold : FontWeight.Normal;
                }
            });
            var capL = Mono("", 7, TextTertiary); var capR = Mono("", 7, TextTertiary);
            readouts.Add(() =>
            {
                capL.Text = NotaNum.F($"tonic {Keys[KeyOf()]} · click a note to add or remove it");
                int mask = ActiveMask(), best = -1; double bv = 0.08;
                for (int i = 0; i < 12; i++) if ((mask >> i & 1) == 0 && Sc(PcAt + i) > bv) { bv = Sc(PcAt + i); best = i; }
                if (best < 0) { capR.Text = ""; return; }
                int to = -1;
                for (int d = 1; d <= 6 && to < 0; d++) { if ((mask >> ((best + d) % 12) & 1) != 0) to = (best + d) % 12; else if ((mask >> ((best - d + 12) % 12) & 1) != 0) to = (best - d + 12) % 12; }
                capR.Text = to >= 0 ? NotaNum.F($"{Keys[best]} pulls to {Keys[to]}") : "";
            });
            var caption = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(6, 0) };
            caption.Children.Add(capL); caption.Children.Add(Col(capR, 1));
            var bottom = new StackPanel { Height = 52, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { new Border { Height = 5 }, chipGrid, caption } };
            return TabBody(head, histView, bottom);
        }

        // ======================================================================
        // CENTRE — MIDI
        // ======================================================================
        AsTraceView? midiView = null;
        Control MidiTab()
        {
            midiView = new AsTraceView();
            ToolTip.SetTip(midiView, "The source track's notes (brass blocks) and the voice (teal) over the last two seconds — with Key Source MIDI the voice is pulled to the notes");
            var head = HeadRow(Row(6, Caps("FROM"), FromDrop(),
                    Seg(MidiMode, new[] { "Note", "Scale" }, "Note — the voice goes to the last held note; Scale — the held notes form the scale"),
                    ToggleLatch(MidiLatch, "Latch", "Latch — keep the last target between notes (off = no correction while no note is held)"),
                    ToggleLatch(MidiOct, "Oct lock", "Oct lock — the note's own octave (off = whichever octave of it is nearest the voice)")),
                RightInfo(() => MidiOn() ? NotaNum.F($"{Sc(S_MidiHeld):0} {(Math.Round(Sc(S_MidiHeld)) == 1 ? "note" : "notes")}") : "Key Source is not MIDI"));
            var knobs = KnobRow(new[]
            {
                K(Amount, "AMOUNT", PctF, tip: "Amount — how far the voice is pulled to the note"),
                K(MidiGlide, "GLIDE", v => MsF(Exp(v, 5, 800)), tip: "Glide — how long the target bends from one note to the next (Pitch bend from MIDI)"),
                K(Speed, "SPEED", v => MsF(Exp(v, 2, 300)), tip: "Speed — how fast the voice follows the target"),
                K(MidiBendRange, "BEND", v => NotaNum.F($"±{v * 6:0.#} st"), Teal, "Bend — the widest move between notes that bends over Glide; wider leaps jump"),
            },
            () => !MidiOn() ? "set Key Source to MIDI to follow the notes"
                : Sc(S_MidiNote) < 0 ? (MidiSrc() < 0 ? "pick a track in FROM" : "no note held")
                : Voiced() ? NotaNum.F($"input {InputText()} → clip note {TargetText()}") : NotaNum.F($"clip note {Note((int)Sc(S_MidiNote))}"),
            () => On(MidiLatch) ? "latch holds the target between notes" : "no correction between notes");
            return TabBody(head, midiView, knobs);
        }

        // ======================================================================
        // RIGHT — Shift / Detect
        // ======================================================================
        static Control LabelRow(string label, Control c, double labW = 44)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")) };
            g.Children.Add(new TextBlock { Text = label, FontSize = 7, FontWeight = FontWeight.Bold, Foreground = TextTertiary, LetterSpacing = 0.8, VerticalAlignment = VerticalAlignment.Center });
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Spread(params Control[] rows)
        {
            var defs = new List<string>();
            for (int i = 0; i < rows.Length; i++) { if (i > 0) defs.Add("*"); defs.Add("Auto"); }
            var g = new Grid { RowDefinitions = new RowDefinitions(string.Join(",", defs)) };
            for (int i = 0; i < rows.Length; i++) g.Children.Add(GRow(rows[i], i * 2));
            return new Border { Padding = new Thickness(8, 6), Child = g };
        }
        // A reading box: caps title · dot + line · second line.
        Control InfoBox(Func<(string title, bool hot, IBrush dot, bool blink, string l1, string l2)> read, string tip)
        {
            var t = Caps("");
            var dot = new Border { Width = 6, Height = 6, CornerRadius = NotaRadius.Pill, VerticalAlignment = VerticalAlignment.Center };
            var l1 = Mono("", 9, TextPrimary);
            var l2 = Mono("", 8, TextSecondary);
            l1.TextTrimming = l2.TextTrimming = TextTrimming.CharacterEllipsis;
            var b = new Border
            {
                Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(6, 4),
                Child = new StackPanel { Spacing = 2, Children = { t, Row(5, dot, l1), l2 } },
            };
            readouts.Add(() =>
            {
                var r = read();
                t.Text = r.title; l1.Text = r.l1; l2.Text = r.l2;
                t.Foreground = r.hot ? AccentBright : TextTertiary;
                l1.Foreground = r.hot ? AccentBright : TextPrimary;
                b.BorderBrush = r.hot ? NotaPalette.BorderBrass : NotaPalette.GraphBorder;
                dot.Background = r.dot;
                dot.Opacity = r.blink && !Blink() ? 0.3 : 1;
            });
            ToolTip.SetTip(b, tip);
            return b;
        }

        Control ShiftTab()
        {
            var noteBox = InfoBox(() =>
            {
                if (MidiOn())
                {
                    int mn = (int)Sc(S_MidiNote);
                    bool live = mn >= 0;
                    return ("MIDI TARGET", true, live ? Brass : BorderStrong, live && Sc(S_MidiHeld) > 0,
                        live ? NotaNum.F($"{Note(mn)} · vel {Sc(S_MidiVel):0}") : MidiSrc() < 0 ? "no source" : "no note",
                        live && Voiced() ? NotaNum.F($"leads {CentsF(Sc(S_CorrCents))} · glide {MsF(GlideMs())}") : NotaNum.F($"from {MidiSrcName()}"));
                }
                bool v = Voiced();
                return ("NOTE", false, v ? Success : BorderStrong, false,
                    v ? NotaNum.F($"{Note(DetNote())} · {Sc(S_Hz):0.0} Hz") : Sc(S_Sibilant) > 0.5 ? (On(SkipSib) ? "sibilant · passed" : "sibilant") : "no pitch",
                    v ? NotaNum.F($"correction {CentsF(Sc(S_CorrCents))} · conf {Sc(S_Conf) * 100:0} %") : NotaNum.F($"in {Sc(S_InDb):0} dB"));
            }, "What the detector hears now and the correction applied — or, with Key Source MIDI, the note the voice is pulled to");
            var follow = Toggle(Follow, "Follow Scale device", "Follow — copy the key and scale of a Nota Scale MIDI effect (this track, the MIDI source, or the first one found)");
            var bend = Toggle(MidiBend, "Pitch bend from MIDI", "Pitch bend from MIDI — moves between notes bend over Glide when they are within Bend; off = the target steps");
            readouts.Add(() => { follow.IsVisible = !MidiOn(); bend.IsVisible = MidiOn(); });
            return Spread(
                SliderRow("SHIFT", Shift, ShiftF, bipolar: true),
                SliderRow("FINE", Fine, FineF, bipolar: true),
                SliderRow("FORMANT", FormantShift, FormF, bipolar: true, modulation: true),
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = SliderRow("MIX", Mix, PctF) },
                noteBox,
                new StackPanel { Spacing = 5, Children = {
                    Toggle(Formant, "Preserve formants", "Preserve formants — the vowel colour stays put while the pitch moves (off = it moves with the pitch, like a tape speed-up)"),
                    new Panel { Children = { follow, bend } } } });
        }

        Control DetectTab()
        {
            var learnBox = InfoBox(() =>
            {
                bool learning = Sc(S_Learning) > 0.5;
                if (Sc(S_LearnKey) < 0)
                    return ("LEARN", learning, learning ? Brass : BorderStrong, learning, learning ? "listening…" : "—", "sing a few bars");
                string Ks(int k, double s) => $"{Keys[Math.Clamp(k, 0, 11)]} {(s > 1.5 ? "Minor" : "Major")}";
                return ("LEARN", learning, learning ? Brass : Success, learning,
                    NotaNum.F($"{Ks((int)Sc(S_LearnKey), Sc(S_LearnScale))} · {Sc(S_LearnMatch) * 100:0} %"),
                    NotaNum.F($"next {Ks((int)Sc(S_Learn2Key), Sc(S_Learn2Scale))} · {Sc(S_Learn2Match) * 100:0} %"));
            }, "The key that fits what was sung (Krumhansl profiles over the last ~16 bars), and the runner-up. Learn sets it; Auto follows it");
            return Spread(
                LabelRow("SOURCE", VoiceDrop()),
                SliderRow("LOW", DetLow, NoteF, modulation: true),
                SliderRow("HIGH", DetHigh, NoteF, modulation: true),
                new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = SliderRow("SENS", DetSens, PctF) },
                learnBox,
                new StackPanel { Spacing = 5, Children = {
                    Toggle(Follow, "Follow Scale device", "Follow — copy the key and scale of a Nota Scale MIDI effect"),
                    Toggle(SkipSib, "Skip sibilants", "Skip sibilants — s, sh, t and breath pass unshifted, so they don't turn metallic") } });
        }

        // ======================================================================
        // Tab frames
        // ======================================================================
        int centreTab = MidiOn() ? 2 : 0;
        var centreHost = new ContentControl();
        var rightHost = new ContentControl();
        var centreBodies = new Control?[3];
        var rightBodies = new Control?[2];
        var extras = Mono("", 7, TextTertiary);
        var extrasHost = new Border { Background = Brushes.Transparent, Child = extras, VerticalAlignment = VerticalAlignment.Center };
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"analysis {Sc(S_Bars):0} bars · {BitCount(ActiveMask())} of 12"),
                2 => "in: " + MidiSrcName(),
                _ => NotaNum.F($"window {(Sc(S_WindowSec) > 0 ? Sc(S_WindowSec) : 2):0.#} s · {KeyScale()}"),
            };
        });
        static int BitCount(int m) { int c = 0; for (; m != 0; m &= m - 1) c++; return c; }
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => ScaleTab(), 2 => MidiTab(), _ => TraceTab() };
        Control RightBody(int t) => rightBodies[t] ??= t == 1 ? DetectTab() : ShiftTab();

        var centre = TabFrame(new[] { "Trace", "Scale", "MIDI" }, centreHost, CentreBody, false, centreTab, t => { centreTab = t; Refresh(); }, extrasHost);
        var rightFrame = TabFrame(new[] { "Shift", "Detect" }, rightHost, RightBody, true, 0, _ => RefreshAll(), null);
        var right = new Border { Width = 186, Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = rightFrame };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new Border { Background = Card2, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Margin = new Thickness(5, 0), ClipToBounds = true, Child = centre };

        // ======================================================================
        // Status strip
        // ======================================================================
        var statusLeft = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var statusRight = Mono("", 8, TextSecondary);
        string StatusText()
        {
            var parts = new List<string>();
            string src = Src() switch { 0 => "auto", 2 => "MIDI", _ => "manual" };
            switch (centreTab)
            {
                case 1:
                    parts.Add(KeyScale());
                    parts.Add(Sc(S_Learning) > 0.5 ? "learning" : src);
                    parts.Add(NotaNum.F($"{BitCount(ActiveMask())} notes"));
                    parts.Add(NotaNum.F($"{VoiceName().ToLowerInvariant()} {Note(LowNote())}…{Note(HighNote())}"));
                    parts.Add(NotaNum.F($"sens {P(DetSens) * 100:0} %"));
                    if (On(SkipSib)) parts.Add("skip sibilants");
                    break;
                case 2:
                    parts.Add("MIDI");
                    parts.Add(MidiSrcName());
                    parts.Add(On(MidiMode) ? "scale" : "note");
                    if (On(MidiLatch)) parts.Add("latch");
                    if (On(MidiOct)) parts.Add("oct lock");
                    parts.Add(NotaNum.F($"amount {P(Amount) * 100:0} %"));
                    parts.Add(On(MidiBend) ? NotaNum.F($"glide {MsF(GlideMs())} within ±{BendSt():0.#} st") : "steps");
                    if (Math.Abs(FormPct()) >= 0.5) parts.Add(NotaNum.F($"formant {FormPct():+0;−0} %"));
                    if (!MidiOn()) parts.Add("key source is " + src);
                    break;
                default:
                    parts.Add(KeyScale());
                    parts.Add(src);
                    parts.Add(NotaNum.F($"amount {P(Amount) * 100:0} %"));
                    parts.Add("speed " + MsF(SpeedMs()));
                    parts.Add(NotaNum.F($"range ±{RangeSt():0} st"));
                    if (P(Human) >= 0.01f) parts.Add(NotaNum.F($"human {P(Human) * 100:0} %"));
                    parts.Add(NotaNum.F($"shift {ShiftSt():+0;−0;0}") + (Math.Abs(FineCt()) >= 0.5 ? NotaNum.F($" {FineCt():+0;−0} ¢") : ""));
                    parts.Add(NotaNum.F($"mix {P(Mix) * 100:0} %"));
                    break;
            }
            if (On(Follow) && centreTab != 2) parts.Add("follows Nota Scale");
            return string.Join(" · ", parts);
        }
        readouts.Add(() =>
        {
            statusLeft.Text = StatusText();
            double sr = Sc(S_SampleRate);
            statusRight.Text = sr > 0 ? NotaNum.F($"{sr / 1000:0.#} kHz · latency {Sc(S_Latency):0} smp · CPU {Sc(S_Cpu) * 100:0.0} %") : "";
        });
        var statusGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        statusGrid.Children.Add(statusLeft);
        statusGrid.Children.Add(Col(statusRight, 1));
        var status = new Border { Height = 18, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(8, 0), Child = statusGrid };
        DockPanel.SetDock(status, Dock.Bottom);

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { pitchCol, right, centreBox } };
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.SurfaceInset, Children = { status, bodyRow } };

        void Refresh()
        {
            FollowTick();
            scN = engine.DeviceScope(track, di, scope, kScope);
            int n = scN >= kScope ? Math.Clamp((int)Sc(S_HistN), 0, kHist) : 0;
            string win = NotaNum.F($"{(Sc(S_WindowSec) > 0 ? Sc(S_WindowSec) : 2):0.#} s");
            if (centreTab == 0 && trace is not null) trace.Set(scope, H_Det, H_Out, H_Midi, n, ActiveMask(), Sc(S_TargetMidi), false, win);
            if (centreTab == 1 && histView is not null) histView.Set(scope, PcAt, ActiveMask(), KeyOf());
            if (centreTab == 2 && midiView is not null) midiView.Set(scope, H_Det, H_Out, H_Midi, n, ActiveMask(), Sc(S_TargetMidi), true, win);
            RefreshAll();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;

        // local: a tab frame (bar + swapping body), optional extras on the right of the bar.
        Control TabFrame(string[] tabs, ContentControl host, Func<int, Control> body, bool centered, int initial, Action<int>? changed, Control? extrasCtl)
        {
            int sel = initial;
            var btns = new Border[tabs.Length];
            void Hi()
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    bool on = i == sel;
                    btns[i].Background = on ? Card2 : Brushes.Transparent;
                    btns[i].BorderBrush = on ? Brass : Brushes.Transparent;
                    var tb = (TextBlock)btns[i].Child!;
                    tb.Foreground = on ? AccentBright : TextTertiary;
                    tb.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                }
            }
            var bar = centered ? (Avalonia.Controls.Panel)new UniformGrid { Rows = 1 } : new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < tabs.Length; i++)
            {
                int iv = i;
                var b = new Border
                {
                    Padding = new Thickness(9, 0), BorderThickness = new Thickness(0, 0, 0, 2), BorderBrush = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new TextBlock { Text = tabs[i], FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                };
                b.PointerPressed += (_, _) => { sel = iv; Hi(); host.Content = body(iv); changed?.Invoke(iv); };
                btns[i] = b;
                bar.Children.Add(b);
            }
            var barDock = new DockPanel { Height = 20, LastChildFill = centered };
            if (extrasCtl != null) { var ex = new Border { Padding = new Thickness(0, 0, 8, 0), Child = extrasCtl }; DockPanel.SetDock(ex, Dock.Right); barDock.Children.Add(ex); }
            if (!centered) DockPanel.SetDock(bar, Dock.Left);
            barDock.Children.Add(bar);
            var barBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = barDock };
            DockPanel.SetDock(barBorder, Dock.Top);
            Hi();
            host.Content = body(sel);
            return new DockPanel { LastChildFill = true, Children = { barBorder, host } };
        }
    }
}
