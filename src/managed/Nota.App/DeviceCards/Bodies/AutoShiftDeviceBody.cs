// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Auto Shift body (pitch correction, device kind 10), a build
// of the "Nota Auto Shift" mockup (700 × 260) on the Shutter / Utility / Valve frame: a centre
// panel with Trace / Scale / MIDI tabs — the detected and corrected pitch on the scale's note
// lanes (Amount · Speed · Human beside it), the sung-note histogram over the scale's notes
// (click to edit), the MIDI target from another track's notes (Glide · Bend beside it) — and a
// right panel with no tabs of its own that follows them: Detect, Key (source, key and scale,
// Fine, Range, Learn, Follow), MIDI — over the output (Preserve formants; Shift · Dry/wet ·
// Formant knobs), which stays. Each param has one home (DESIGN.md § Devices › Card layout). The pictures
// come from the engine (AutoShift.h scopeRead). Every control is a device param, so automation /
// MIDI learn / presets / A-B / persistence come for free; Learn and Reset are device actions,
// the MIDI source rides on the sidechain source, and Follow (copy a Nota Scale's key and
// scale) is driven from here.
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
using Nota.Application.Localization;

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
    public string? Subtitle => "Pitch";

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
        string TrackName(int id) => TrackNames.Of(engine, id);
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
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.UI"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }
        static T GRow<T>(T c, int row) where T : Control { Grid.SetRow(c, row); return c; }
        void LearnBind(Control c, int p) => MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p));

        // Gauge knob bound to a device param (automation gesture + MIDI learn + live follow).
        Control K(int p, string name, Func<double, string> fmt, IBrush? arc = null, string? tip = null, double cellW = 50, double size = 34, bool bipolar = false)
        {
            var val = Mono(fmt(P(p)), 7, TextPrimary);
            var knob = new Knob(P(p), 1.0) { Accent = true, ArcColor = arc, Bipolar = bipolar, Default = Def(p), Width = size, Height = size };
            knob.Format = v => fmt(v);   // read-out at any value: typing searches it
            knob.ValueChanged += v => { Raw(p, v); val.Text = fmt(P(p)); RefreshAll(); };
            knob.GestureBegin += () => Begin(p);
            knob.GestureEnd += () => End(p);
            LearnBind(knob, p);
            if (tip is not null) ToolTip.SetTip(knob, tip);
            readouts.Add(() => { if (knob.Dragging) return; double c = P(p); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; val.Text = fmt(P(p)); });
            return KnobCell(name, knob, val, cellW);
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
            Loc.T("device.auto_shift.key_source_auto_follows_the"),
            iv => SetP(KeySrc, iv / 2f), Src);

        // A latching chip: lit when `lit()`, a click runs `click`. `fill` = a full-width button.
        Border Latch(int p, Func<bool> lit, Action click, Func<string> text, string tip, bool fill = false, Func<bool>? blink = null)
        {
            var dot = new Border { Width = 5, Height = 5, CornerRadius = NotaRadius.Group, Background = Brass, VerticalAlignment = VerticalAlignment.Center, IsVisible = false };
            var tb = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var b = new Border
            {
                Height = 16, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
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
        Border LearnLatch() => Latch(-1, () => Sc(S_Learning) > 0.5, Learn, () => "Learn",
            Loc.T("device.auto_shift.learn_listen_to_what_is"), blink: () => Sc(S_Learning) > 0.5);

        // A dropdown box: [text ▾], opens a menu of items.
        Border Drop(Func<string> text, Func<bool> lit, Action<MenuFlyout> fill, string tip, double minW = 0, bool stretch = false, int learnP = -1)
        {
            var name = new TextBlock { FontSize = 8, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            var chev = new Glyph(GlyphKind.ChevronDown, 7) { Margin = new Thickness(6, 0, 0, 0) };
            var box = new Border
            {
                Height = 16, MinWidth = minW, Background = Sunken, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Cell,
                Padding = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
                Child = new DockPanel { Children = { Docked(chev, Dock.Right), name } },
            };
            if (stretch) box.HorizontalAlignment = HorizontalAlignment.Stretch;
            readouts.Add(() =>
            {
                bool on = lit();
                name.Text = text();
                name.Foreground = TextPrimary;   // emphasis, not state: a stronger frame and weight
                name.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                box.BorderBrush = on ? NotaPalette.BorderStrong : BorderDef;
                chev.Foreground = on ? TextSecondary : TextTertiary;
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
            mi.ToggleType = MenuItemToggleType.Radio; mi.IsChecked = cur;
            mi.Click += (_, _) => pick();
            return mi;
        }
        Control KeyDrop(bool lit) => Drop(() => Keys[KeyOf()], () => lit, fly =>
        {
            for (int k = 0; k < 12; k++) { int kv = k; fly.Items.Add(Item(Keys[k], KeyOf() == k, () => { SetKey(kv); RefreshAll(); })); }
        }, Loc.T("device.auto_shift.key_the_tonic_the_scales"), 0, false, Key);
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
            fly.Items.Add(Item(Loc.T("device.auto_shift.custom_edit_on_the_scale"), On(Custom), () => { SetCustom(ActiveMask()); RefreshAll(); }));
        }, Loc.T("device.auto_shift.scale_the_notes_the_voice"), 62, false, Scale);

        // The MIDI source: [name ▾] lists the instrument tracks. Picking one switches Key Source to MIDI.
        Control FromDrop() => Drop(MidiSrcName, () => MidiSrc() >= 0 && MidiOn(), fly =>
        {
            fly.Items.Add(Item(Loc.T("common.none"), MidiSrc() < 0, () => { engine.SetDeviceSidechainSource(track, di, -1); ctx.NotifyChanged(); RefreshAll(); }));
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
        }, Loc.T("device.auto_shift.from_the_instrument_track_whose"), 74, false, KeySrc);

        Control VoiceDrop() => Drop(VoiceName, () => false, fly =>
        {
            foreach (var v in Voices)
            {
                var vv = v;
                fly.Items.Add(Item(NotaNum.F($"{vv.Name}  ({Note(vv.Lo)}…{Note(vv.Hi)})"), LowNote() == vv.Lo && HighNote() == vv.Hi, () =>
                { SetP(DetLow, (vv.Lo - 24) / 72f); SetP(DetHigh, (vv.Hi - 24) / 72f); RefreshAll(); }));
            }
        }, Loc.T("device.auto_shift.source_sets_the_detection_range"), 0, true);

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

        // Live readings shared by the tabs.
        bool Voiced() => Sc(S_Voiced) > 0.5;
        int DetNote() => (int)Math.Round(Sc(S_DetMidi));
        double DetCents() => (Sc(S_DetMidi) - DetNote()) * 100;
        int TargetNote() => Sc(S_TargetMidi) > 0.5 ? (int)Math.Round(Sc(S_TargetMidi)) : -1;
        string InputText() => Voiced() ? NotaNum.F($"{Note(DetNote())} {CentsF(DetCents())}") : Sc(S_Sibilant) > 0.5 ? "sibilant" : "—";
        string TargetText() => TargetNote() >= 0 ? Note(TargetNote()) : "—";

        // ======================================================================
        // Layout pieces (DESIGN.md § Devices › Card layout: graph, tabs and output)
        // ======================================================================
        // A column of knob cells spread down the graph's right side.
        static Control KnobColumn(params Control[] cells)
        {
            var g = new Grid { Width = 50 };
            for (int i = 0; i < cells.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition(GridLength.Star));
                cells[i].VerticalAlignment = VerticalAlignment.Center;
                g.Children.Add(GRow(cells[i], i));
            }
            return g;
        }
        // A centre view: the graph fills it, its knobs in a column on the right, a selector row under it.
        static Control View(Control graph, Control? knobs, Control? bottom)
        {
            var d = new DockPanel { LastChildFill = true, Margin = new Thickness(6, 6, 6, 5) };
            if (bottom != null) d.Children.Add(Docked(new Border { Margin = new Thickness(0, 5, 0, 0), Child = bottom }, Dock.Bottom));
            if (knobs != null) d.Children.Add(Docked(new Border { Margin = new Thickness(6, 0, 0, 0), Child = knobs }, Dock.Right));
            d.Children.Add(graph);
            return d;
        }
        // The right panel's title bar: the section's name or switch on the left, its mode on the right.
        static Control Head(Control left, Control? right = null)
        {
            var d = new DockPanel { LastChildFill = false, VerticalAlignment = VerticalAlignment.Center };
            if (right != null) d.Children.Add(Docked(right, Dock.Right));
            d.Children.Add(Docked(left, Dock.Left));
            return d;
        }
        static TextBlock Title(string t) => new() { Text = t, FontSize = 9, FontWeight = FontWeight.Medium, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        // Rows spread evenly down the panel; a 1px rule keeps its height.
        static Control Rows(params Control[] rows)
        {
            var grid = new Grid { RowSpacing = 2 };
            for (int r = 0; r < rows.Length; r++)
            {
                grid.RowDefinitions.Add(new RowDefinition(rows[r] is Border { Height: 1 } ? GridLength.Auto : GridLength.Star));
                grid.Children.Add(GRow(rows[r], r));
            }
            return new Border { Padding = new Thickness(8, 4, 8, 5), Child = grid };
        }
        static Border Rule() => new() { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 1) };
        static Control LabelRow(string label, Control c, double labW = 44)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions(NotaNum.F($"{labW},*")), VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(Caps(label));
            g.Children.Add(Col(c, 1));
            return g;
        }
        static Control Pair(Control a, Control b)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(a); g.Children.Add(Col(b, 1));
            return g;
        }

        // ======================================================================
        // CENTRE — Trace: the detected and corrected pitch; Amount · Speed · Human shape the
        // corrected line.
        // ======================================================================
        AsTraceView? trace = null;
        Control TraceTab()
        {
            trace = new AsTraceView();
            ToolTip.SetTip(trace, Loc.T("device.auto_shift.the_detected_pitch_teal_dots"));
            return View(trace, KnobColumn(
                K(Amount, "AMOUNT", PctF, tip: Loc.T("device.auto_shift.amount_how_far_the_voice_2")),
                K(Speed, "SPEED", v => MsF(Exp(v, 2, 300)), tip: Loc.T("device.auto_shift.speed_how_fast_the_retune")),
                K(Human, "HUMAN", PctF, Teal, Loc.T("device.auto_shift.human_keeps_vibrato_and_slow"))), null);
        }

        // ======================================================================
        // CENTRE — Scale: the sung-note histogram over the scale's notes (click to edit).
        // ======================================================================
        AsHistView? histView = null;
        Control ScaleTab()
        {
            histView = new AsHistView();
            histView.NoteClicked += ToggleNote;
            ToolTip.SetTip(histView, Loc.T("device.auto_shift.how_long_each_note_was"));
            LearnBind(histView, Custom);

            // Note chips: tonic filled, in-scale outlined, sung out-of-scale dashed teal.
            var chipGrid = new Grid { ColumnSpacing = 4, Margin = new Thickness(6, 0) };
            var chips = new Border[12]; var chipTx = new TextBlock[12];
            for (int i = 0; i < 12; i++)
            {
                int pc = i;
                chipGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                var tb = new TextBlock { Text = Keys[i], FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var b = new Border { Height = 20, CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
                b.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return; ToggleNote(pc); e.Handled = true; };
                LearnBind(b, NoteC + i);
                ToolTip.SetTip(b, Loc.F("device.auto_shift.click_to_add_it_to", Keys[i]));
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
                    chipTx[i].Foreground = tonic ? OnAccent : inS ? AccentBright : sung ? DeviceCardKit.ModInkBright : TextDisabled;
                    chipTx[i].FontWeight = tonic ? FontWeight.Bold : inS ? FontWeight.SemiBold : FontWeight.Normal;
                }
            });
            return View(histView, null, chipGrid);
        }

        // ======================================================================
        // CENTRE — MIDI: the target from another track's notes; Glide · Bend shape its moves.
        // ======================================================================
        AsTraceView? midiView = null;
        Control MidiTab()
        {
            midiView = new AsTraceView();
            ToolTip.SetTip(midiView, Loc.T("device.auto_shift.the_source_tracks_notes_neutral"));
            return View(midiView, KnobColumn(
                K(MidiGlide, "GLIDE", v => MsF(Exp(v, 5, 800)), tip: Loc.T("device.auto_shift.glide_how_long_the_target")),
                K(MidiBendRange, "BEND", v => NotaNum.F($"±{v * 6:0.#} st"), Teal, Loc.T("device.auto_shift.bend_the_widest_move_between"))), null);
        }

        // ======================================================================
        // RIGHT — follows the centre tab: Detect (Trace) · Key (Scale) · MIDI (MIDI)
        // ======================================================================
        (Control, Control) DetectPanel() => (
            Head(Title(Loc.T("device.auto_shift.detect")), VoiceDrop()),
            Rows(
                SliderRow("LOW", DetLow, NoteF, modulation: true),
                SliderRow("HIGH", DetHigh, NoteF, modulation: true),
                SliderRow("SENS", DetSens, PctF),
                Rule(),
                Toggle(SkipSib, "Skip sibilants", Loc.T("device.auto_shift.skip_sibilants_s_sh_t"))));

        (Control, Control) KeyPanel()
        {
            Control RangeRow()
            {
                var r = SliderRow("RANGE", Range, v => NotaNum.F($"±{1 + v * 11:0} st"));
                ToolTip.SetTip(r, Loc.T("device.auto_shift.range_the_largest_correction_a"));
                return r;
            }
            // Learn: the latch, then what it heard (the runner-up in its tooltip).
            var learnText = Mono("", 8, TextSecondary);
            learnText.TextTrimming = TextTrimming.CharacterEllipsis;
            learnText.Margin = new Thickness(6, 0, 0, 0);
            readouts.Add(() =>
            {
                bool learning = Sc(S_Learning) > 0.5;
                string Ks(int k, double s) => $"{Keys[Math.Clamp(k, 0, 11)]} {(s > 1.5 ? "Minor" : "Major")}";
                learnText.Text = Sc(S_LearnKey) < 0 ? (learning ? Loc.T("device.auto_shift.listening") : Loc.T("device.auto_shift.sing_a_few_bars"))
                    : NotaNum.F($"{Ks((int)Sc(S_LearnKey), Sc(S_LearnScale))} · {Sc(S_LearnMatch) * 100:0} %");
                learnText.Foreground = learning ? AccentBright : TextSecondary;
                ToolTip.SetTip(learnText, Sc(S_LearnKey) < 0 ? null
                    : NotaNum.F($"next {Ks((int)Sc(S_Learn2Key), Sc(S_Learn2Scale))} · {Sc(S_Learn2Match) * 100:0} %"));
            });
            var learn = LearnLatch();
            var learnRow = new DockPanel { VerticalAlignment = VerticalAlignment.Center, Children = { Docked(learn, Dock.Left), learnText } };
            var keyScale = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { KeyDrop(true), ScaleDrop() } };
            return (Head(Title(Loc.T("device.auto_shift.key")), SourceSeg()),
                Rows(
                    LabelRow("KEY", keyScale),
                    SliderRow("FINE", Fine, FineF, bipolar: true),
                    RangeRow(),
                    Rule(),
                    learnRow,
                    Toggle(Follow, Loc.T("device.auto_shift.follow_scale_device"), Loc.T("device.auto_shift.follow_copy_the_key_and"))));
        }

        (Control, Control) MidiPanel() => (
            Head(Title("MIDI"), FromDrop()),
            Rows(
                LabelRow("TARGET", Seg(MidiMode, new[] { "Note", "Scale" }, Loc.T("device.auto_shift.note_the_voice_goes_to"))),
                Pair(Toggle(MidiLatch, "Latch", Loc.T("device.auto_shift.latch_keep_the_last_target")),
                     Toggle(MidiOct, "Oct lock", Loc.T("device.auto_shift.oct_lock_the_notes_own"))),
                Toggle(MidiBend, Loc.T("device.auto_shift.pitch_bend_from_midi"), Loc.T("device.auto_shift.pitch_bend_from_midi_moves"))));

        // Under every panel: the output — Shift · Dry/wet (the main knob) · Formant, with
        // Preserve formants over them.
        Control OutputStrip()
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Margin = new Thickness(-4, 4, -4, 4) };
            Control Cell(Control c, int col) { c.HorizontalAlignment = HorizontalAlignment.Center; c.VerticalAlignment = VerticalAlignment.Bottom; return Col(c, col); }
            g.Children.Add(Cell(K(Shift, "SHIFT", ShiftF, cellW: 54, bipolar: true), 0));
            g.Children.Add(Cell(K(Mix, "DRY/WET", PctF, cellW: 58, size: 44), 1));
            g.Children.Add(Cell(K(FormantShift, "FORMANT", FormF, cellW: 54, bipolar: true), 2));
            var preserve = Toggle(Formant, "Preserve formants", Loc.T("device.auto_shift.preserve_formants_the_vowel_colour"));
            preserve.Margin = new Thickness(0, 5, 0, 0);
            return new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(8, 0),
                Child = new StackPanel { Children = { preserve, g } } };
        }

        // ======================================================================
        // Tab frame + the panel that follows it
        // ======================================================================
        int centreTab = MidiOn() ? 2 : 0;
        var centreHost = new ContentControl();
        var panelHost = new ContentControl();
        var headHost = new ContentControl();
        var centreBodies = new Control?[3];
        var panels = new (Control Head, Control Body)?[3];
        var extras = Mono("", 7, TextTertiary);
        var extrasHost = new Border { Background = Brushes.Transparent, Child = extras, VerticalAlignment = VerticalAlignment.Center };
        readouts.Add(() =>
        {
            extras.Text = centreTab switch
            {
                1 => NotaNum.F($"{KeyScale()} · {BitCount(ActiveMask())} of 12 · analysis {Sc(S_Bars):0} bars")
                     + (Sc(S_AnalysisSec) > 0.3 ? NotaNum.F($" · match {Sc(S_InScale) * 100:0} %") : ""),
                2 => !MidiOn() ? Loc.T("device.auto_shift.set_key_source_to_midi")
                    : Sc(S_MidiNote) < 0 ? (MidiSrc() < 0 ? Loc.T("device.auto_shift.pick_a_track_in_from") : Loc.T("device.auto_shift.no_note_held"))
                    : Voiced() ? NotaNum.F($"input {InputText()} → clip note {TargetText()}") : NotaNum.F($"clip note {Note((int)Sc(S_MidiNote))}"),
                _ => (Voiced() ? NotaNum.F($"input {InputText()} → target {TargetText()}") : Loc.T("device.auto_shift.no_pitch")) + " · " + KeyScale(),
            };
        });
        static int BitCount(int m) { int c = 0; for (; m != 0; m &= m - 1) c++; return c; }
        Control CentreBody(int t) => centreBodies[t] ??= t switch { 1 => ScaleTab(), 2 => MidiTab(), _ => TraceTab() };
        void ShowPanel(int t)
        {
            var (head, body) = panels[t] ??= t switch { 1 => KeyPanel(), 2 => MidiPanel(), _ => DetectPanel() };
            headHost.Content = head;
            panelHost.Content = body;
            RefreshAll();
        }

        var centre = TabFrame(new[] { "Trace", "Scale", "MIDI" }, centreHost, CentreBody, false, centreTab, t => { centreTab = t; ShowPanel(t); Refresh(); }, extrasHost);
        ShowPanel(centreTab);
        var headBar = new Border { Height = 20, Padding = new Thickness(8, 0), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = headHost };
        var rightCol = new DockPanel { LastChildFill = true, Children = { Docked(headBar, Dock.Top), Docked(OutputStrip(), Dock.Bottom), panelHost } };
        var right = new RuleSection { Width = 186, Child = rightCol };
        DockPanel.SetDock(right, Dock.Right);
        var centreBox = new RuleSection { Margin = new Thickness(0, 0, 5, 0), Child = centre };

        // ---- assemble ---------------------------------------------------------------
        var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, centreBox } };
        var root = new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };

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
                    tb.Foreground = on ? NotaPalette.TextPrimary : TextTertiary;
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
