// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — Nota Mosaic (instrument kind 17), a build of the "Nota Mosaic" mockup.
// One device, two sizes (the shell's S / L toggle — the instrument's "view" param):
//
//   • L 700 × 260. Centre: a tab panel —
//       Zones   the zone map (keys × velocity, the keyboard under it, a brass tick under
//               every root), Attack / Release to show the release zones, and the selected
//               zone's row: root · layer · round-robin, keys, velocity, fine, gain, pan.
//               Drag a tile to move it, an edge to resize; the keyboard plays.
//       Sample  the selected zone's sample: start / end / loop (drag), loop mode, crossfade,
//               gain, snap to zero, trim silence.
//       Groups  the layers: zones, gain, tune, round-robin and its mode, exclusive groups;
//               release triggers with their level and the length → level curve, the
//               velocity curve. The rail edits the selected group.
//       Pitch   the keyboard with the selected zone's root; transpose, detune, keytrack, the
//               zone's root and fine tune, the MPE bend range.
//       Env     the amp envelope (drag it), A / D / S / R, velocity curve, MPE pressure.
//       Filter  the filter response (drag it), type, cutoff, reso, keytrack, env → cutoff,
//               MPE slide.
//     Rail: VOICES — Poly N (its popover: polyphony, what the voices are doing, the stealing
//     order, strikes per key, old-voice fade) / Mono / Choke, volume, pan, glide, the pedal,
//     output, Vel → Vol and the meter. States take the rail over: the SFZ import report, the
//     memory while samples load, the source of a missing pack.
//   • S 260 × 260 — the name, the mini map, zones · layers and the RAM; a loading bar.
//
// Samples are referenced, never copied (see MosaicProgram). The program is edited here as a
// MosaicProgram and committed whole (one undo step); a drag previews in the map and commits
// on release. Brass is the parameter; teal is modulation depth (Vel → Vol, MPE).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using Avalonia.Platform.Storage;
using Nota.Application.Mosaic;
using Nota.Presentation;
using Path = System.IO.Path;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class MosaicInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "MULTISAMPLE";

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

    // The header readout: what the program is and how much RAM it takes (S: the voices).
    public string? VoiceLabel(IAudioEngine engine, int trackId, int active)
    {
        int poly = 64;
        int pc = engine.PluginParamCount(trackId, -1);
        for (int i = 0; i < pc; i++) if (engine.PluginParamId(trackId, -1, i) == "polyphony") poly = MosaicModel.Poly(engine.PluginParamGet(trackId, -1, i));
        if (InstrumentView.IsMini(engine, trackId)) return $"{Math.Max(0, active)}/{poly}";
        if (!engine.TryGetMosaicStatus(trackId, out var st) || st.FilesTotal == 0) return $"{Math.Max(0, active)}/{poly}";
        if (st.State == 1) return $"LOADING {(int)Math.Round(100.0 * st.DiskDone / Math.Max(1, st.DiskTotal))} %";
        var ui = Ui(trackId);
        string tag = ui.SourceTag;
        return $"{tag} · {NotaNum.Bytes(st.RamBytes)}";
    }

    // ---- per-track UI state (survives a rebuild) --------------------------------------------
    internal sealed class UiState
    {
        public int Tab, Zone = -1, Group;
        public bool ReleaseView, ReportOpen;
        public SfzImportReport? Report;
        public bool ReportDismissed;
        public string SourceTag = "FILES";
    }
    private static readonly Dictionary<int, UiState> States = new();
    internal static UiState Ui(int track) => States.TryGetValue(track, out var s) ? s : States[track] = new UiState();
    /// <summary>A fresh SFZ import on this track: the card shows its banner and report.</summary>
    internal static void SetImportReport(int track, SfzImportReport report) { var u = Ui(track); u.Report = report; u.ReportDismissed = false; u.ReportOpen = true; }

    /// <summary>Raised when the card asks for the multisample preview (files / a folder dropped on it).</summary>
    internal static event Action<int, IReadOnlyList<string>>? CreateMultisampleRequested;
    /// <summary>Raised when the card asks to install a registry sample pack (Settings → Downloads).</summary>
    internal static event Action<string>? InstallPackRequested;

    private const double RailW = 186, TabH = 20, StatusH = 18;
    private static readonly string[] TabNames = { "Zones", "Sample", "Groups", "Pitch", "Env", "Filter" };

    private static string SourceTagOf(MosaicProgram p) => p.SourceKind switch
    {
        "sfz" => p.PackId.Length > 0 ? "PACK" : "SFZ",
        "folder" => p.PackId.Length > 0 ? "PACK" : "MULTI",
        "factory" => "BUILT-IN",
        "sample" => "ONE-SHOT",
        _ => p.PackId.Length > 0 ? "PACK" : "MULTI",
    };

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        var ui = Ui(track);
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetP(string id, double v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, (float)Math.Clamp(v, 0, 1)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void Write(string id, double v) { Begin(id); SetP(id, v); End(id); }
        float Def(string id) => I(id) is var i and >= 0 ? engine.InstrumentParamDefault(track, i) : 0f;
        int Sel(string id, int n) => MosaicModel.Index(G(id), n);

        // ---- the program --------------------------------------------------------------------
        var prog = MosaicProgram.Parse(engine.MosaicProgram(track));
        engine.TryGetMosaicStatus(track, out var status);
        int knownSerial = status.Serial;
        ui.SourceTag = SourceTagOf(prog);
        if (ui.Zone >= prog.Zones.Count) ui.Zone = -1;
        if (ui.Zone < 0 && prog.Zones.Count > 0)
        {
            // The first attack zone around middle C.
            int best = 0, bd = int.MaxValue;
            for (int i = 0; i < prog.Zones.Count; i++)
            {
                var z = prog.Zones[i];
                int d = (z.Release ? 1000 : 0) + Math.Abs(z.Root - 60) * 2 + (127 - z.VelHi) / 16;
                if (d < bd) { bd = d; best = i; }
            }
            ui.Zone = best;
        }
        if (ui.Group >= prog.Groups.Count) ui.Group = 0;
        bool hasRelease = prog.Zones.Any(z => z.Release);
        if (!hasRelease) ui.ReleaseView = false;
        MosaicZone? Zsel() => ui.Zone >= 0 && ui.Zone < prog.Zones.Count ? prog.Zones[ui.Zone] : null;

        void Commit(bool rebuild = false)
        {
            engine.MosaicSetProgram(track, prog.Serialize());
            if (engine.TryGetMosaicStatus(track, out var s2)) knownSerial = s2.Serial;
            ctx.NotifyChanged();
            if (rebuild) ctx.RequestRebuild();
        }

        var readouts = new List<Action>();
        Action refresh = () => { };
        void Refresh() => refresh();
        var snap = new MosaicModel.Snapshot();
        var scope = new float[MosaicModel.ScopeLength];
        var samSnap = new SamplerModel.Snapshot();
        bool mini = InstrumentView.IsMini(engine, track);

        // Loading / missing.
        bool Loading() => status.State == 1;
        bool AllMissing() => status.State == 2 && status.FilesTotal > 0 && status.Missing == status.FilesTotal;
        double LoadFrac() => status.DiskTotal > 0 ? Math.Clamp(status.DiskDone / (double)status.DiskTotal, 0, 1)
                           : status.FilesTotal > 0 ? status.FilesDone / (double)status.FilesTotal : 0;
        long RamEstimate() => LoadFrac() > 0.02 ? (long)(status.RamBytes / LoadFrac()) : status.RamBytes;

        static TextBlock Caps(string t, IBrush? c = null) => new()
        {
            Text = t.ToUpperInvariant(), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.KnobLabelTracking, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 8) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary, VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily,
        };

        // ---- shared builders ------------------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = NotaSize.KnobSecondary, double cellW = 52, IBrush? arc = null, string? tip = null)
        {
            int pi = I(id);
            if (pi < 0) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0) { Accent = true, ArcColor = arc, Default = Def(id), Width = size, Height = size };
            knob.ValueChanged += v => { SetP(id, v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            readouts.Add(() =>
            {
                if (knob.Dragging) return;
                float v = G(id);
                if (Math.Abs(v - knob.Value) > 1e-4) knob.Value = v;
                value.Text = fmt(v);
            });
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), id);
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (tip is not null) ToolTip.SetTip(cell, tip);
            return cell;
        }

        // A knob over a zone / group value (program data): live in the map, committed on release.
        Control VKnob(string label, Func<double> getNorm, Action<double> setNorm, Func<string> fmt, double def, IBrush? arc = null, string? tip = null, double size = NotaSize.KnobSecondary)
        {
            var value = new TextBlock { Text = fmt(), Foreground = TextPrimary };
            var knob = new Knob(getNorm(), 1.0) { Accent = true, ArcColor = arc, Default = def, Width = size, Height = size };
            knob.ValueChanged += v => { setNorm(v); value.Text = fmt(); Refresh(); };
            knob.GestureEnd += () => Commit();
            readouts.Add(() => { if (knob.Dragging) return; double v = getNorm(); if (Math.Abs(v - knob.Value) > 1e-4) knob.Value = v; value.Text = fmt(); });
            var cell = KnobCell(label, knob, value, 52);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (tip is not null) ToolTip.SetTip(cell, tip);
            return cell;
        }

        Border Chips(string id, string[] names, double padX = 6, double fontSize = 8)
        {
            int n = names.Length;
            var seg = Segments(names, () => Sel(id, n), iv => { Write(id, iv / (double)(n - 1)); Refresh(); }, out var sync, padX: padX, fontSize: fontSize);
            readouts.Add(sync);
            if (I(id) is var pi and >= 0) MidiLearn.Bind(seg, MidiTarget.PluginParam(track, -1, pi), id);
            return seg;
        }

        Grid Slider(string label, string id, Func<string> text, bool bipolar = false, bool modulation = false, string? tip = null)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, v); Refresh(); }, text, out var sync,
                begin: pi >= 0 ? () => Begin(id) : null, end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, Def(id)); Refresh(); } : null,
                bipolar: bipolar, labelWidth: 46, valueWidth: 40, modulation: modulation);
            readouts.Add(sync);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), id);
            if (tip is not null) ToolTip.SetTip(row, tip);
            return row;
        }

        // A raised text button; a latching one turns brass-washed while on.
        Border Button(string text, Action click, string tip, Func<bool>? on = null, Func<bool>? enabled = null, bool primary = false)
        {
            var tb = new TextBlock { Text = text, FontSize = 8, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Border
            {
                Height = 17, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(8, 0),
                Child = tb, VerticalAlignment = VerticalAlignment.Center,
            };
            ToolTip.SetTip(b, tip);
            void Paint()
            {
                bool lit = on?.Invoke() ?? false, ok = enabled?.Invoke() ?? true;
                b.Background = primary && ok ? Brass : lit ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised;
                b.BorderBrush = primary && ok ? Brass : lit ? NotaPalette.BorderBrass : BorderDef;
                tb.Foreground = !ok ? TextTertiary : primary ? OnAccent : lit ? AccentBright : NotaPalette.TextStrong;
                tb.FontWeight = primary ? FontWeight.SemiBold : FontWeight.Normal;
                b.Cursor = ok ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
            }
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed || !(enabled?.Invoke() ?? true)) return;
                click(); Paint(); Refresh(); e.Handled = true;
            };
            readouts.Add(Paint);
            Paint();
            return b;
        }

        // A small number box: drag up / down (Shift fine), wheel, double-click resets.
        Border NumBox(Func<string> text, Action<int> step, Action? reset = null, double width = 44, string? tip = null, Action? done = null)
        {
            var tb = Mono(text(), TextPrimary, 9);
            tb.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new Border
            {
                Height = 17, MinWidth = width, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
                Background = NotaPalette.BgSunken, Child = tb, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), Padding = new Thickness(4, 0),
            };
            double y0 = 0, acc = 0; bool drag = false;
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2 && reset is not null) { reset(); done?.Invoke(); tb.Text = text(); Refresh(); e.Handled = true; return; }
                drag = true; y0 = e.GetPosition(b).Y; acc = 0; e.Pointer.Capture(b); e.Handled = true;
            };
            b.PointerMoved += (_, e) =>
            {
                if (!drag) return;
                double y = e.GetPosition(b).Y;
                acc += (y0 - y) / (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 12.0 : 4.0); y0 = y;
                int n = (int)Math.Truncate(acc);
                if (n != 0) { acc -= n; step(n); tb.Text = text(); Refresh(); }
            };
            b.PointerReleased += (_, e) => { if (!drag) return; drag = false; e.Pointer.Capture(null); done?.Invoke(); };
            b.PointerWheelChanged += (_, e) => { step(e.Delta.Y > 0 ? 1 : -1); tb.Text = text(); done?.Invoke(); Refresh(); e.Handled = true; };
            readouts.Add(() => { if (!drag) tb.Text = text(); });
            if (tip is not null) ToolTip.SetTip(b, tip);
            return b;
        }
        Control Field(string cap, Control c) => new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Children = { Caps(cap), c } };
        Control ReadField(string cap, Func<string> text, IBrush? ink = null)
        {
            var t = Mono(text(), ink ?? TextPrimary, 9);
            readouts.Add(() => t.Text = text());
            return Field(cap, t);
        }
        static Grid Row(double spacing, params (Control C, string W)[] cols)
        {
            var g = new Grid { ColumnSpacing = spacing, VerticalAlignment = VerticalAlignment.Stretch };
            for (int i = 0; i < cols.Length; i++)
            {
                g.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse(cols[i].W)));
                cols[i].C.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(cols[i].C, i); g.Children.Add(cols[i].C);
            }
            return g;
        }

        // ---- words ------------------------------------------------------------------------------
        string LayerOf(MosaicZone z)
        {
            string gname = z.Group >= 0 && z.Group < prog.Groups.Count ? prog.Groups[z.Group].Name : "";
            int dot = gname.LastIndexOf('·');
            return dot >= 0 ? gname[(dot + 1)..].Trim() : gname.Length > 0 ? gname : $"{z.VelLo}–{z.VelHi}";
        }
        int SeqLenOf(MosaicZone z) => z.Group >= 0 && z.Group < prog.Groups.Count ? prog.Groups[z.Group].SeqLen : 1;
        string ZoneChip(MosaicZone z) => z.Release
            ? $"{MosaicNames.NoteName(z.Root)} · release"
            : $"{MosaicNames.NoteName(z.Root)} · {LayerOf(z)}{(SeqLenOf(z) > 1 ? $" · rr {z.Seq}/{SeqLenOf(z)}" : "")}";
        string Keys(MosaicZone z) => z.KeyLo == z.KeyHi ? MosaicNames.NoteName(z.KeyLo) : $"{MosaicNames.NoteName(z.KeyLo)}–{MosaicNames.NoteName(z.KeyHi)}";
        string Cents(double c) => NotaNum.Unit(c, "+0;−0;0", "c");
        string DbS(double db) => NotaNum.Db(db, signed: true);
        string PanS(double p) => Math.Abs(p) < 0.015 ? "C" : (p < 0 ? "L" : "R") + NotaNum.Str(Math.Abs(p) * 100, "0");
        string DbLin(float v) => NotaNum.Db(MosaicModel.VolumeDb(v), floor: -80);
        string St(float v) => NotaNum.Unit(MosaicModel.TransposeSt(v), "+0;−0;0", "st");
        string GlideText(float v) { double s = MosaicModel.GlideSec(v); return s <= 0 ? NotaNum.Unit(0, "0", "ms") : NotaNum.Time(s); }

        // ======================= the zone map (Zones tab, and S) =================================
        var map = new MosaicZoneMap { Mini = mini, Interactive = !mini };
        string EmptyText() => mini ? "Empty" : "Drop samples or a folder here · or pick a preset";
        void SetMap() => map.Set(prog.Zones, ui.ReleaseView, ui.Zone, EmptyText());
        SetMap();
        map.Selected += i => { ui.Zone = i; if (i >= 0 && i < prog.Zones.Count) ui.Group = prog.Zones[i].Group; Refresh(); };
        map.Edited += (i, klo, khi, vlo, vhi, done) =>
        {
            if (i < 0 || i >= prog.Zones.Count) return;
            var z = prog.Zones[i];
            // Every round-robin tile stacked with it moves along.
            foreach (var o in prog.Zones)
                if (o != z && o.Release == z.Release && o.Group == z.Group && o.KeyLo == z.KeyLo && o.KeyHi == z.KeyHi && o.VelLo == z.VelLo && o.VelHi == z.VelHi)
                { o.KeyLo = klo; o.KeyHi = khi; o.VelLo = vlo; o.VelHi = vhi; }
            z.KeyLo = klo; z.KeyHi = khi; z.VelLo = vlo; z.VelHi = vhi;
            SetMap();
            if (done) Commit();
            Refresh();
        };
        map.KeyDown += k => engine.TrackNoteOn(track, k, 0.75f);
        map.KeyUp += k => engine.TrackNoteOff(track, k);

        // ---- status / meta ------------------------------------------------------------------------
        var statusText = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Mono("", TextSecondary, 8);
        double esr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
        string Rate() => NotaNum.Unit(esr / 1000, "0.#", "kHz");
        int Poly() => MosaicModel.Poly(G("polyphony"));

        if (mini) return BuildMini();

        // ======================= Zones tab =======================================================
        var zoneChip = new Border
        {
            CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = NotaPalette.BorderBrass,
            Background = NotaPalette.AccentSubtle, Padding = new Thickness(7, 2), VerticalAlignment = VerticalAlignment.Center,
            Child = Mono("", AccentBright, 9),
        };
        readouts.Add(() => ((TextBlock)zoneChip.Child!).Text = Zsel() is { } z ? ZoneChip(z) : "no zone");
        var zKeys = ReadField("Keys", () => Zsel() is { } z ? Keys(z) : "—");
        var zVel = ReadField("Vel", () => Zsel() is { } z ? $"{z.VelLo}–{z.VelHi}" : "—");
        var zXf = ReadField("Xf vel", () => Zsel() is { } z && z.XVelInHi > z.XVelInLo ? NotaNum.Str(z.XVelInHi - z.XVelInLo, "0") : "—");
        var zFine = Field("Fine", NumBox(() => Zsel() is { } z ? Cents(z.Tune) : "—", d => { if (Zsel() is { } z) z.Tune = Math.Clamp(z.Tune + d, -100, 100); },
            () => { if (Zsel() is { } z) z.Tune = 0; }, 40, "The zone's fine tune — drag, wheel, double-click resets", () => Commit()));
        var zGain = Field("Gain", NumBox(() => Zsel() is { } z ? DbS(z.GainDb) : "—", d => { if (Zsel() is { } z) z.GainDb = Math.Clamp(Math.Round(z.GainDb * 10 + d) / 10.0, -48, 24); },
            () => { if (Zsel() is { } z) z.GainDb = 0; }, 50, "The zone's level", () => Commit()));
        var zPan = Field("Pan", NumBox(() => Zsel() is { } z ? PanS(z.Pan) : "—", d => { if (Zsel() is { } z) z.Pan = Math.Clamp(Math.Round(z.Pan * 100 + d) / 100.0, -1, 1); },
            () => { if (Zsel() is { } z) z.Pan = 0; }, 32, "The zone's pan", () => Commit()));
        int tab = ui.Tab;
        Action showTab = () => { };
        var openSample = Button("Open in Sample", () => { tab = 1; showTab(); }, "Edit this zone's start, end and loop", enabled: () => Zsel() is not null);
        var zoneRow = Row(12, (zoneChip, "Auto"), (zKeys, "Auto"), (zVel, "Auto"), (zXf, "Auto"), (zFine, "Auto"), (zGain, "Auto"), (zPan, "Auto"), (new Panel(), "*"), (openSample, "Auto"));

        // Loading: over the map, and the row says what still works.
        var loadBar = new Border { Height = 3, CornerRadius = NotaRadius.Badge, Background = Brass, HorizontalAlignment = HorizontalAlignment.Left };
        var loadTrack = new Border { Height = 3, CornerRadius = NotaRadius.Badge, Background = NotaPalette.BorderDefault, Child = loadBar };
        var loadPct = Mono("", AccentBright, 9);
        var loadLine = Mono("", TextSecondary, 8);
        var loadHead = new DockPanel { Children = { loadPct } };
        DockPanel.SetDock(loadPct, Dock.Right);
        loadHead.Children.Add(new TextBlock { Text = "Loading samples", FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary });
        var loadBox = new Border
        {
            Width = 250, Padding = new Thickness(10, 8), CornerRadius = NotaRadius.Tile, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
            Background = NotaPalette.SurfaceCard, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel { Spacing = 5, Children = { loadHead, loadTrack, loadLine, new TextBlock { Text = "Notes are silent until loading finishes", FontSize = 7.5, Foreground = TextTertiary } } },
        };
        loadBox.BindResource(Border.BoxShadowProperty, "Shadow.Popover");
        var loadScrim = new Border { Background = NotaPalette.Wash(NotaPalette.BgSunken, 0x99), IsHitTestVisible = false };
        var loadLayer = new Panel { Children = { loadScrim, loadBox } };
        loadTrack.SizeChanged += (_, _) => loadBar.Width = loadTrack.Bounds.Width * LoadFrac();
        var loadRow = Row(10, (new TextBlock { Text = "Zones can already be selected and edited", FontSize = 8, Foreground = TextTertiary }, "*"),
            (Button("Cancel", () => { prog = new MosaicProgram { Name = prog.Name }; Commit(rebuild: true); }, "Stop loading — the instrument empties (undo brings it back)"), "Auto"));

        // SFZ import banner over the map.
        var bannerText = Mono("", TextPrimary, 8);
        var bannerDot = new Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Success, VerticalAlignment = VerticalAlignment.Center };
        var reportLink = new TextBlock { Text = "Report", FontSize = 8, Foreground = AccentBright, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        var bannerClose = new Glyph(GlyphKind.Close, 7) { Foreground = TextTertiary, Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center };
        Action rebuildRail = () => { };
        reportLink.PointerPressed += (_, e) => { ui.ReportOpen = !ui.ReportOpen; rebuildRail(); e.Handled = true; };
        var banner = new Border
        {
            Height = 18, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = NotaPalette.SuccessDim,
            Background = NotaPalette.Wash(NotaPalette.Success, 0x14), Padding = new Thickness(6, 0), Margin = new Thickness(0, 0, 0, 3),
        };
        var bannerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 6 };
        bannerGrid.Children.Add(bannerDot);
        Grid.SetColumn(bannerText, 1); bannerGrid.Children.Add(bannerText);
        Grid.SetColumn(reportLink, 2); bannerGrid.Children.Add(reportLink);
        Grid.SetColumn(bannerClose, 3); bannerGrid.Children.Add(bannerClose);
        banner.Child = bannerGrid;
        bannerClose.PointerPressed += (_, e) => { ui.ReportDismissed = true; ui.ReportOpen = false; banner.IsVisible = false; rebuildRail(); e.Handled = true; };
        bool ShowBanner() => ui.Report is not null && !ui.ReportDismissed && prog.SourceKind == "sfz";
        banner.IsVisible = ShowBanner();
        if (ui.Report is { } rep0) bannerText.Text = rep0.Summary();

        // A missing pack: the map gives way to what to do about it.
        Control MissingPanel()
        {
            bool pack = prog.PackId.Length > 0;
            string title = pack ? $"Pack “{(prog.PackName.Length > 0 ? prog.PackName : prog.PackId)}” is not installed" : "The samples aren't where the program says";
            string body = pack
                ? $"This preset uses {prog.Files.Count} samples from the pack. Zone settings are kept, no sound until installed."
                : $"None of its {prog.Files.Count} samples were found. Zone settings are kept — point Mosaic at the folder that holds them.";
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Center };
            if (pack) buttons.Children.Add(Button("Install pack", () => InstallPackRequested?.Invoke(prog.PackId), "Settings → Downloads → Sample Packs", primary: true));
            buttons.Children.Add(Button("Locate folder…", () => _ = LocateAsync(), "Pick the folder that holds the samples — Mosaic finds each file in it by its path or its name"));
            return new Border
            {
                Background = NotaPalette.BgSunken, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(NotaRadius.ControlValue),
                Child = new StackPanel
                {
                    Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 380,
                    Children =
                    {
                        new TextBlock { Text = title, FontSize = 11, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center },
                        new TextBlock { Text = body, FontSize = 8, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center },
                        Mono(pack ? $"{prog.PackId} · {prog.Zones.Count} zones" : MosaicPaths.FileName(prog.Files.FirstOrDefault() ?? ""), TextTertiary, 8),
                        buttons,
                    },
                },
            };
        }

        // Finds every sample under a folder the user picks: by its relative tail, else its name.
        async System.Threading.Tasks.Task LocateAsync()
        {
            var top = TopLevel.GetTopLevel(map);
            if (top is null) return;
            var picked = await top.StorageProvider.OpenFolderPickerAsync(new Avalonia.Platform.Storage.FolderPickerOpenOptions { Title = "Locate the samples", AllowMultiple = false });
            var dir = picked.FirstOrDefault()?.TryGetLocalPath();
            if (dir is null) return;
            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)) byName.TryAdd(Path.GetFileName(f), f); } catch { }
            int found = 0;
            for (int i = 0; i < prog.Files.Count; i++)
            {
                string reference = prog.Files[i];
                string rel = reference.Contains(':') && reference.IndexOf(':') >= 2 ? reference[(reference.IndexOf(':') + 1)..] : reference;
                var parts = rel.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                string? hit = null;
                for (int k = 0; k < parts.Length && hit is null; k++)
                {
                    string cand = Path.Combine(dir, Path.Combine(parts[k..]));
                    if (File.Exists(cand)) hit = cand;
                }
                hit ??= byName.TryGetValue(Path.GetFileName(rel), out var n) ? n : null;
                if (hit is not null) { prog.Files[i] = hit; found++; }
            }
            if (found > 0) Commit(rebuild: true);
        }

        var mapHost = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        mapHost.Children.Add(banner);
        var mapCell = new Panel();
        if (AllMissing() && prog.Zones.Count > 0) mapCell.Children.Add(MissingPanel());
        else { mapCell.Children.Add(map); mapCell.Children.Add(loadLayer); }
        Grid.SetRow(mapCell, 1); mapHost.Children.Add(mapCell);
        var zoneRowHost = new Panel { Children = { zoneRow, loadRow } };

        // ======================= Sample tab ======================================================
        var wave = new SamplerWaveView();
        wave.SetEmpty("Select a zone");
        long waveSampleId = 0; int waveZone = -1; float[] raw = Array.Empty<float>(); int wch = 1; long wframes = 0; double wsr = 0;
        void LoadWave()
        {
            long sid = ui.Zone >= 0 ? engine.MosaicZoneSampleId(track, ui.Zone) : 0;
            if (sid == waveSampleId && ui.Zone == waveZone) return;
            waveSampleId = sid; waveZone = ui.Zone;
            raw = Array.Empty<float>(); wframes = 0;
            if (sid != 0 && engine.TryGetSampleInfo(sid, out var si))
            {
                raw = engine.ReadSample(sid); wch = Math.Max(1, si.Channels); wframes = si.Frames; wsr = si.SampleRate;
                wave.SetPeaks(SamplerModel.Peaks(raw, wch, 1200), wsr > 0 ? wframes / wsr : 0);
            }
            else wave.SetEmpty(Loading() ? "Loading…" : Zsel() is null ? "Select a zone" : "Sample not loaded");
        }
        double FrameNorm(double frames, double def) => wframes > 1 ? (frames < 0 ? def : Math.Clamp(frames / wframes, 0, 1)) : def;
        double ToFrames(double norm) => Math.Round(Math.Clamp(norm, 0, 1) * wframes);
        (double S, double E, double LS, double LE) ZoneWindow(MosaicZone z)
            => (FrameNorm(z.Start, 0), FrameNorm(z.End, 1), FrameNorm(z.LoopStart, FrameNorm(z.Start, 0)), FrameNorm(z.LoopEnd, FrameNorm(z.End, 1)));
        wave.Moved += (h, f) =>
        {
            if (Zsel() is not { } z || wframes <= 1) return;
            switch (SamplerWaveView.HandleId(h))
            {
                case "start": z.Start = ToFrames(Math.Min(f, ZoneWindow(z).E - 0.001)); break;
                case "end": z.End = ToFrames(Math.Max(f, ZoneWindow(z).S + 0.001)); break;
                case "loopstart": z.LoopStart = ToFrames(f); break;
                case "loopend": z.LoopEnd = ToFrames(f); break;
            }
            Refresh();
        };
        wave.DragEnd += _ => Commit();
        wave.ResetHandle += h =>
        {
            if (Zsel() is not { } z) return;
            switch (SamplerWaveView.HandleId(h)) { case "start": z.Start = 0; break; case "end": z.End = -1; break; case "loopstart": z.LoopStart = -1; break; case "loopend": z.LoopEnd = -1; break; }
            Commit(); Refresh();
        };
        readouts.Add(() =>
        {
            LoadWave();
            if (Zsel() is { } z)
            {
                var (s, e, ls, le) = ZoneWindow(z);
                double dur = wsr > 0 ? wframes / wsr : 0;
                wave.Set(s, e, ls, le, z.LoopMode, z.Crossfade,
                    wframes > 1 ? $"{prog.FileName(z.File)} · {NotaNum.Unit(wsr / 1000, "0.#", "k")} · {NotaNum.Unit(dur, "0.00", "s")} · root {MosaicNames.NoteName(z.Root)}" : prog.FileName(z.File),
                    z.LoopMode == 0 ? "no loop" : $"{MosaicModel.LoopNames[z.LoopMode].ToLowerInvariant()} {NotaNum.Str(ls * dur, "0.00")} – {NotaNum.Unit(le * dur, "0.00", "s")}");
            }
        });
        var loopSeg = Segments(MosaicModel.LoopNames, () => Zsel()?.LoopMode ?? 0, iv => { if (Zsel() is { } z) { z.LoopMode = iv; Commit(); } Refresh(); }, out var loopSync, padX: 7, fontSize: 8);
        readouts.Add(loopSync);
        ToolTip.SetTip(loopSeg, "Off plays once · Fwd loops · Ping bounces — this zone only");
        var xfKnob = VKnob("Crossfade", () => Math.Sqrt(Math.Clamp((Zsel()?.Crossfade ?? 0) / 0.5, 0, 1)), v => { if (Zsel() is { } z) z.Crossfade = v * v * 0.5; },
            () => NotaNum.Time(Zsel()?.Crossfade ?? 0), 0, tip: "Blends the loop's end into its start (forward loops)");
        var zGainKnob = VKnob("Gain", () => ((Zsel()?.GainDb ?? 0) + 24) / 48.0, v => { if (Zsel() is { } z) z.GainDb = Math.Round((v * 48 - 24) * 10) / 10; },
            () => DbS(Zsel()?.GainDb ?? 0), 0.5, tip: "This zone's level");
        void SnapAll()
        {
            if (Zsel() is not { } z || wframes <= 1) return;
            var (s, e, ls, le) = ZoneWindow(z);
            z.Start = ToFrames(SamplerModel.SnapZero(raw, wch, wframes, s));
            if (z.End >= 0) z.End = ToFrames(SamplerModel.SnapZero(raw, wch, wframes, e));
            if (z.LoopStart >= 0) z.LoopStart = ToFrames(SamplerModel.SnapZero(raw, wch, wframes, ls));
            if (z.LoopEnd >= 0) z.LoopEnd = ToFrames(SamplerModel.SnapZero(raw, wch, wframes, le));
            Commit();
        }
        void Trim()
        {
            if (Zsel() is not { } z || wframes <= 1 || SamplerModel.TrimSilence(raw, wch, wframes) is not { } t) return;
            z.Start = ToFrames(t.Start); z.End = ToFrames(t.End);
            Commit();
        }
        var snapBtn = Button("Snap to zero", SnapAll, "Move Start, End and the loop to the nearest zero crossing", enabled: () => wframes > 1);
        var trimBtn = Button("Trim silence", Trim, "Move Start and End to where the sound begins and fades out (−48 dB)", enabled: () => wframes > 1);
        snapBtn.HorizontalAlignment = HorizontalAlignment.Stretch; trimBtn.HorizontalAlignment = HorizontalAlignment.Stretch;
        var sampleChip = Mono("", AccentBright, 9);
        readouts.Add(() => sampleChip.Text = Zsel() is { } z ? ZoneChip(z) : "");
        var sampleRow = Row(14, (Field("Zone", sampleChip), "Auto"), (Field("Loop", loopSeg), "Auto"), (xfKnob, "Auto"), (zGainKnob, "Auto"), (new Panel(), "*"),
            (new StackPanel { Spacing = 3, Children = { snapBtn, trimBtn } }, "Auto"));

        // ======================= Groups tab ======================================================
        var groupList = new StackPanel { Spacing = 1 };
        Action rebuildGroups = () => { };
        string RrModeWord(int m) => MosaicProgram.RrNames[Math.Clamp(m, 0, 2)] is var w && m == 2 ? "Random no repeat" : MosaicProgram.RrNames[Math.Clamp(m, 0, 2)];
        int ZonesIn(int g) => prog.Zones.Count(z => z.Group == g);
        bool IsReleaseGroup(int g) => prog.Zones.Any(z => z.Group == g) && prog.Zones.Where(z => z.Group == g).All(z => z.Release);
        string ExclOf(int g)
        {
            var zs = prog.Zones.Where(z => z.Group == g).ToList();
            if (zs.Count == 0 || zs.All(z => z.Excl == 0 && z.OffBy == 0)) return "—";
            var e = zs.First(z => z.Excl != 0 || z.OffBy != 0);
            return $"{(e.Excl != 0 ? e.Excl.ToString(NotaNum.Culture) : "—")} / {(e.OffBy != 0 ? e.OffBy.ToString(NotaNum.Culture) : "—")}";
        }
        Grid GroupRowGrid() => new() { ColumnDefinitions = new ColumnDefinitions("*,44,52,40,22,112,36"), ColumnSpacing = 4, Height = 17 };
        TextBlock Cell(Grid g, int col, string text, IBrush? ink = null, bool mono = true, TextAlignment align = TextAlignment.Right)
        {
            var tb = mono ? Mono(text, ink ?? TextPrimary, 8) : new TextBlock { Text = text, FontSize = 8, Foreground = ink ?? TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
            tb.TextAlignment = align;
            Grid.SetColumn(tb, col); g.Children.Add(tb);
            return tb;
        }
        var groupHead = GroupRowGrid();
        foreach (var (c, t, a) in new[] { (0, "GROUP", TextAlignment.Left), (1, "ZONES", TextAlignment.Right), (2, "GAIN", TextAlignment.Right), (3, "TUNE", TextAlignment.Right), (4, "RR", TextAlignment.Right), (5, "RR MODE", TextAlignment.Left), (6, "EXCL", TextAlignment.Right) })
        {
            var cap = Caps(t); cap.TextAlignment = a; cap.HorizontalAlignment = a == TextAlignment.Left ? HorizontalAlignment.Left : HorizontalAlignment.Right;
            Grid.SetColumn(cap, c); groupHead.Children.Add(cap);
        }
        rebuildGroups = () =>
        {
            groupList.Children.Clear();
            for (int g = 0; g < prog.Groups.Count; g++)
            {
                int gi = g;
                var gr = prog.Groups[g];
                bool sel = g == ui.Group;
                var row = GroupRowGrid();
                var name = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
                name.Children.Add(new TextBlock { Text = gr.Name.Length > 0 ? gr.Name : $"Group {g + 1}", FontSize = 8.5, FontWeight = sel ? FontWeight.SemiBold : FontWeight.Normal, Foreground = sel ? AccentBright : TextPrimary, VerticalAlignment = VerticalAlignment.Center });
                if (IsReleaseGroup(g)) name.Children.Add(new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Badge, Padding = new Thickness(3, 0), Child = Mono("TRIG REL", TextTertiary, 7) });
                Grid.SetColumn(name, 0); row.Children.Add(name);
                Cell(row, 1, ZonesIn(g).ToString(NotaNum.Culture));
                Cell(row, 2, DbS(gr.GainDb));
                Cell(row, 3, Cents(gr.Tune), Math.Abs(gr.Tune) > 0.01 ? NotaPalette.TealBright : NotaPalette.Teal);
                Cell(row, 4, gr.SeqLen.ToString(NotaNum.Culture));
                if (sel && gr.SeqLen > 1)
                {
                    var seg = Segments(new[] { "Seq", "Rnd", "Rnd≠" }, () => gr.RrMode, iv => { gr.RrMode = iv; Commit(); rebuildGroups(); rebuildRail(); }, out _, padX: 4, fontSize: 7);
                    seg.HorizontalAlignment = HorizontalAlignment.Left;
                    Grid.SetColumn(seg, 5); row.Children.Add(seg);
                }
                else Cell(row, 5, gr.SeqLen > 1 ? RrModeWord(gr.RrMode) : "—", gr.SeqLen > 1 ? TextPrimary : TextTertiary, align: TextAlignment.Left);
                Cell(row, 6, ExclOf(g), TextTertiary);
                var host = new Border
                {
                    Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                    Background = sel ? NotaPalette.AccentSubtle : g % 2 == 0 ? NotaPalette.Wash(NotaPalette.SurfaceRaised, 0x80) : Brushes.Transparent,
                    BorderBrush = sel ? NotaPalette.BorderBrass : Brushes.Transparent, Child = row,
                };
                host.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                    ui.Group = gi; rebuildGroups(); rebuildRail(); Refresh(); e.Handled = true;
                };
                groupList.Children.Add(host);
            }
        };
        rebuildGroups();
        var groupScroll = new ScrollViewer { Content = groupList, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };
        var groupTable = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 2 };
        groupHead.Margin = new Thickness(7, 0);
        groupTable.Children.Add(groupHead);
        Grid.SetRow(groupScroll, 1); groupTable.Children.Add(groupScroll);

        // Release triggers · their level · length → level · the velocity curve · + Group.
        var relSwitch = Switch("", () => G("relon") >= 0.5f, () => Write("relon", G("relon") >= 0.5f ? 0 : 1), out var relSync, liveLabel: () => G("relon") >= 0.5f ? "on" : "off");
        readouts.Add(relSync);
        if (I("relon") is var rli and >= 0) MidiLearn.Bind(relSwitch, MidiTarget.PluginParam(track, -1, rli), "Release Triggers");
        var relVolKnob = PKnob("relvol", "Rel vol", v => NotaNum.Db(MosaicModel.RelVolDb(v), signed: true), NotaSize.KnobMain, 52, tip: "The release samples' level");
        var lenCurve = new MosaicCurveView
        {
            Width = 120, Height = 34,
            Curve = x => 0.85 * Math.Pow(10, MosaicModel.RelLenDb(G("rellen")) * Math.Min(1, x * 2) / 20.0),
            Get = () => G("rellen"), SetValue = v => { SetP("rellen", v); Refresh(); }, Begin = () => Begin("rellen"), End = () => End("rellen"),
            Default = Def("rellen"), UpIsMore = false, Node = 0.5,
        };
        ToolTip.SetTip(lenCurve, "How much quieter a release sample gets the longer the key was held — drag up / down");
        var lenWord = Mono("", TextSecondary, 7.5);
        readouts.Add(() => { lenWord.Text = $"{NotaNum.Db(MosaicModel.RelLenDb(G("rellen")), signed: true)} after 2 s hold"; lenCurve.InvalidateVisual(); });
        var velCurve = new MosaicCurveView
        {
            Width = 64, Height = 34,
            Curve = x => MosaicModel.VelCurveAt(x, G("velcurve")),
            Get = () => G("velcurve"), SetValue = v => { SetP("velcurve", v); Refresh(); }, Begin = () => Begin("velcurve"), End = () => End("velcurve"),
            Default = Def("velcurve"), UpIsMore = false,
        };
        ToolTip.SetTip(velCurve, "Velocity → level: drag up for soft (quiet notes louder), down for hard");
        var velWord = Mono("", TextSecondary, 7.5);
        string VelCurveWord() { int c = MosaicModel.VelCurve(G("velcurve")); return c == 0 ? "linear" : c < 0 ? $"soft +{-c}" : $"hard +{c}"; }
        readouts.Add(() => { velWord.Text = VelCurveWord(); velCurve.InvalidateVisual(); });
        var addGroup = Button("+ Group", () =>
        {
            prog.Groups.Add(new MosaicGroup { Name = $"Group {prog.Groups.Count + 1}" });
            ui.Group = prog.Groups.Count - 1;
            Commit(); rebuildGroups(); rebuildRail();
        }, "A new, empty layer — move zones into it from the rail");
        var groupBottom = Row(18,
            (new StackPanel { Spacing = 4, Children = { Caps("Release triggers"), relSwitch } }, "Auto"),
            (relVolKnob, "Auto"),
            (new StackPanel { Spacing = 2, Children = { Caps("Length → level"), lenCurve, lenWord } }, "Auto"),
            (new StackPanel { Spacing = 2, Children = { Caps("Vel curve"), velCurve, velWord } }, "Auto"),
            (new Panel(), "*"), (addGroup, "Auto"));

        // ======================= Pitch tab =======================================================
        var keysView = new SamplerKeysView();
        ToolTip.SetTip(keysView, "Click a key to make it the selected zone's root");
        keysView.RootPicked += k => { if (Zsel() is { } z) { z.Root = k; Commit(); SetMap(); } Refresh(); };
        readouts.Add(() => keysView.Set(Zsel()?.Root ?? 60, -1, snap.Live ? snap.Note : -1, G("pitchtrack"), Zsel() is { } z ? ZoneChip(z) : ""));
        var rootBox = Field("Root", NumBox(() => Zsel() is { } z ? MosaicNames.NoteName(z.Root) : "—", d => { if (Zsel() is { } z) z.Root = Math.Clamp(z.Root + d, 0, 127); },
            null, 40, "The zone's root — drag or scroll", () => { Commit(); SetMap(); }));
        int Detected() => Zsel() is { } z ? SamplerModel.DetectRoot(prog.FileName(z.File)) : -1;
        var detectBtn = Button("Detect from file name", () => { if (Zsel() is { } z && Detected() >= 0) { z.Root = Detected(); Commit(); SetMap(); } },
            "Make the note in the zone's file name its root", on: () => Zsel() is { } z && Detected() == z.Root, enabled: () => Detected() >= 0);
        var fineKnob = VKnob("Fine", () => ((Zsel()?.Tune ?? 0) + 100) / 200.0, v => { if (Zsel() is { } z) z.Tune = Math.Round(v * 200 - 100); }, () => Cents(Zsel()?.Tune ?? 0), 0.5, Teal, "The zone's fine tune, ±100 cents");
        var pitchRow = Row(12,
            (PKnob("transpose", "Transpose", St, tip: "Shift every note, ±24 semitones"), "Auto"),
            (PKnob("detune", "Detune", v => Cents(MosaicModel.DetuneCents(v)), arc: Teal, tip: "Fine tune of the whole instrument, ±50 cents"), "Auto"),
            (PKnob("pitchtrack", "Keytrack", v => NotaNum.Pct(v), tip: "How far the key moves the pitch — 100 % chromatic, 0 % every key plays the root"), "Auto"),
            (rootBox, "Auto"), (fineKnob, "Auto"),
            (PKnob("bendrange", "Bend", v => NotaNum.Unit(MosaicModel.BendSemis(v), "0", "st"), arc: Teal, tip: "The pitch wheel's range (MPE bends are per note)"), "Auto"),
            (new Panel(), "*"), (detectBtn, "Auto"));

        // ======================= Env tab =========================================================
        var env = new SamplerEnvView();
        ToolTip.SetTip(env, "Drag the nodes: attack, decay and sustain (up / down), release · Shift for fine steps · double-click resets");
        env.DragBegin += Begin;
        env.DragEnd += End;
        env.Changed += (id, v) => { SetP(id, v); Refresh(); };
        env.Reset += id => { Write(id, Def(id)); Refresh(); };
        string EnvTl() => $"{NotaNum.Time(MosaicModel.AttackSec(G("attack")))} · {NotaNum.Time(MosaicModel.DecaySec(G("decay")))} · {DbLin(G("sustain"))} · {NotaNum.Time(MosaicModel.ReleaseSec(G("release")))}";
        readouts.Add(() => env.Set(G("attack"), G("decay"), G("sustain"), G("release"), samSnap, EnvTl(), ""));
        var envRow = Row(12,
            (PKnob("attack", "Attack", v => NotaNum.Time(MosaicModel.AttackSec(v))), "Auto"),
            (PKnob("decay", "Decay", v => NotaNum.Time(MosaicModel.DecaySec(v))), "Auto"),
            (PKnob("sustain", "Sustain", DbLin, NotaSize.KnobMain, 56), "Auto"),
            (PKnob("release", "Release", v => NotaNum.Time(MosaicModel.ReleaseSec(v))), "Auto"),
            (new Panel(), "*"),
            (PKnob("velcurve", "Vel curve", v => { int c = MosaicModel.VelCurve(v); return c == 0 ? "linear" : c < 0 ? $"soft {-c}" : $"hard {c}"; }, arc: Teal, tip: "Velocity → level curve"), "Auto"),
            (PKnob("mpepressure", "Pressure", v => NotaNum.Pct(v), arc: Teal, tip: "MPE pressure (aftertouch) → level"), "Auto"));

        // ======================= Filter tab ======================================================
        var filt = new SamplerFilterView();
        ToolTip.SetTip(filt, "Drag: left / right for the cutoff, up / down for the resonance · Shift for fine steps · double-click resets");
        filt.DragBegin += () => { Begin("cutoff"); Begin("resonance"); };
        filt.DragEnd += () => { End("cutoff"); End("resonance"); };
        filt.Changed += (id, v) => { SetP(id, v); Refresh(); };
        filt.Reset += () => { Write("cutoff", Def("cutoff")); Write("resonance", Def("resonance")); Refresh(); };
        string FiltTl()
        {
            int t = Sel("filtertype", 4);
            if (t == 0) return "off · the samples pass untouched";
            return $"{MosaicModel.FilterNames[t]} 12 dB/oct · {NotaNum.Hz(MosaicModel.CutoffHz(G("cutoff")))} · reso {NotaNum.Pct(G("resonance"))}";
        }
        readouts.Add(() => filt.Set(Sel("filtertype", 4), G("cutoff"), G("resonance"), snap.Live ? snap.CutoffHz : -1, FiltTl()));
        var envBtn = Button("Env → Cutoff", () => Write("envcutoff", G("envcutoff") >= 0.5f ? 0 : 1), "Let the amp envelope open (or close) the filter", on: () => G("envcutoff") >= 0.5f);
        var filterRow = Row(10,
            (Field("Type", Chips("filtertype", MosaicModel.FilterNames, padX: 6)), "Auto"),
            (PKnob("cutoff", "Cutoff", v => NotaNum.Hz(MosaicModel.CutoffHz(v)), NotaSize.KnobMain, 56), "Auto"),
            (PKnob("resonance", "Reso", v => NotaNum.Pct(v)), "Auto"),
            (PKnob("keytrack", "Keytrack", v => NotaNum.Pct(v), arc: Teal, tip: "The cutoff follows the key"), "Auto"),
            (new Panel(), "*"), (envBtn, "Auto"),
            (PKnob("envamount", "Env", v => NotaNum.Unit(MosaicModel.EnvOctaves(v), "+0.0;−0.0;0.0", "oct"), arc: Teal), "Auto"),
            (PKnob("mpeslide", "Slide", v => NotaNum.Pct(v), arc: Teal, tip: "MPE slide (CC74) → cutoff"), "Auto"));

        // ======================= the tab panel ===================================================
        const double RowH = 62;
        Control Page(Control view, Control row, double rowH = RowH)
        {
            var rowHost = new Border { Height = rowH, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 3, 0, 0), Child = row };
            var page = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = 4, Margin = new Thickness(7, 5, 7, 4) };
            page.Children.Add(view);
            Grid.SetRow(rowHost, 1); page.Children.Add(rowHost);
            return page;
        }
        var pages = new Control[]
        {
            Page(mapHost, zoneRowHost, 34),
            Page(wave, sampleRow),
            Page(groupTable, groupBottom, 66),
            Page(keysView, pitchRow),
            Page(env, envRow),
            Page(filt, filterRow),
        };
        var tabCells = new Border[TabNames.Length];
        var tabTexts = new TextBlock[TabNames.Length];
        var tabRow = new StackPanel { Orientation = Orientation.Horizontal };
        for (int i = 0; i < TabNames.Length; i++)
        {
            int iv = i;
            var tb = new TextBlock { Text = TabNames[i], FontSize = NotaType.DeviceSection, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { Padding = new Thickness(7, 0), BorderThickness = new Thickness(0, 0, 0, 2), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            cell.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) return;
                tab = iv; showTab(); e.Handled = true;
            };
            tabCells[i] = cell; tabTexts[i] = tb; tabRow.Children.Add(cell);
        }
        // The strip's right side: Attack / Release + the count (Zones), the articulation (SFZ), a hint.
        var arSeg = Segments(new[] { "Attack", "Release" }, () => ui.ReleaseView ? 1 : 0, iv => { ui.ReleaseView = iv == 1; SetMap(); Refresh(); }, out var arSync, padX: 5, fontSize: 7);
        readouts.Add(arSync);
        arSeg.IsVisible = hasRelease;
        var countText = Mono("", TextTertiary, 7);
        readouts.Add(() =>
        {
            int n = prog.Zones.Count(z => z.Release == ui.ReleaseView);
            int rr = prog.MaxRoundRobin;
            countText.Text = tab switch
            {
                0 => $"{n} zones{(rr > 1 && !ui.ReleaseView ? $" · rr {rr}" : "")}",
                2 => "gain · tune · rr · exclusive",
                3 => "transpose · root · fine · bend",
                4 => "a · d · s · r · curve",
                5 => "type · cutoff · reso · keytrack",
                _ => "start · end · loop",
            };
        });
        var articulation = ArticulationPicker();
        var stripRight = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) };
        if (articulation is not null) stripRight.Children.Add(articulation);
        stripRight.Children.Add(arSeg);
        stripRight.Children.Add(countText);
        var tabStrip = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        tabStrip.Children.Add(tabRow);
        Grid.SetColumn(stripRight, 1); tabStrip.Children.Add(stripRight);
        var tabHost = new Panel();
        foreach (var p in pages) tabHost.Children.Add(p);
        var dropLayer = DropOverlay();
        tabHost.Children.Add(dropLayer);
        var tabGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        tabGrid.Children.Add(HeaderStrip(tabStrip));
        Grid.SetRow(tabHost, 1); tabGrid.Children.Add(tabHost);
        var tabPanel = SectionBox(tabGrid);

        // SFZ: pick the articulation (a controller's value the zones were imported with).
        Control? ArticulationPicker()
        {
            if (prog.SourceKind != "sfz" || prog.Ccs.Count == 0) return null;
            string? sfzPath = App.Services?.GetService(typeof(IMosaicPacks)) is IMosaicPacks packs ? packs.Resolve(prog.SourceRef) : prog.SourceRef;
            if (sfzPath is null || !File.Exists(sfzPath)) return null;
            var ranges = SfzImporter.CcRanges(sfzPath);
            var choices = new List<(int Cc, int Value, string Label)>();
            foreach (var (cc, list) in ranges)
                foreach (var (lo, hi, lab) in list) choices.Add((cc, lo, lab.Length > 0 ? lab : $"cc{cc} {lo}–{hi}"));
            if (choices.Count < 2) return null;
            int Current() => choices.FindIndex(c => prog.Ccs.TryGetValue(c.Cc, out var v) && ranges[c.Cc].Any(r => r.Lo == c.Value && v >= r.Lo && v <= r.Hi));
            var artLabel = new TextBlock { FontSize = 8, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
            var sub = Mono("", TextTertiary, 7);
            void Paint() { int c = Current(); artLabel.Text = c >= 0 ? choices[c].Label : "—"; sub.Text = c >= 0 ? $"cc{choices[c].Cc}={choices[c].Value}" : ""; }
            Paint();
            var box = new Border
            {
                Height = 15, Padding = new Thickness(6, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), BorderBrush = BorderDef,
                Background = NotaPalette.BgSunken, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { artLabel, sub, new Glyph(GlyphKind.ChevronDown, 6) { Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center } } },
            };
            ToolTip.SetTip(box, "The articulation — the SFZ's controller value its zones were chosen by");
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                var f = new MenuFlyout();
                for (int i = 0; i < choices.Count; i++)
                {
                    var c = choices[i];
                    var mi = new MenuItem { Header = $"{c.Label}  ·  cc{c.Cc}={c.Value}", ToggleType = MenuItemToggleType.Radio, IsChecked = i == Current() };
                    mi.Click += (_, _) =>
                    {
                        var over = new Dictionary<int, int>(prog.Ccs) { [c.Cc] = c.Value };
                        var packs = App.Services?.GetService(typeof(IMosaicPacks)) as IMosaicPacks;
                        var res = SfzImporter.Import(sfzPath, over, packs is null ? null : packs.ToRef);
                        res.Program.Name = prog.Name; res.Program.SourceRef = prog.SourceRef; res.Program.PackId = prog.PackId; res.Program.PackName = prog.PackName;
                        prog = res.Program;
                        SetImportReport(track, res.Report);
                        Commit(rebuild: true);
                    };
                    f.Items.Add(mi);
                }
                f.ShowAt(box);
                e.Handled = true;
            };
            return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center, Children = { Caps("Artic"), box } };
        }

        // ======================= the rail ========================================================
        var railHost = new ContentControl();
        var rail = SectionBox(railHost);
        rail.Width = RailW;

        Control RailHead(string title, Control? right = null, string? sub = null)
        {
            var head = new Grid { Height = TabH, ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
            var cap = Caps(title); cap.LetterSpacing = NotaType.SectionLabelTracking;
            head.Children.Add(cap);
            Control? r = right ?? (sub is null ? null : Mono(sub, TextTertiary, 7));
            if (r is not null) { r.HorizontalAlignment = HorizontalAlignment.Right; r.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(r, 1); head.Children.Add(r); }
            return HeaderStrip(head);
        }
        Control RailBody(Control head, Control body)
        {
            var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
            g.Children.Add(head);
            body.Margin = new Thickness(8, 6, 8, 5);
            Grid.SetRow(body, 1); g.Children.Add(body);
            return g;
        }
        Control KV(string k, Func<string> v, IBrush? ink = null)
        {
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 14 };
            g.Children.Add(Mono(k, TextSecondary, 8));
            var val = Mono(v(), ink ?? TextPrimary, 8);
            readouts.Add(() => val.Text = v());
            Grid.SetColumn(val, 1); g.Children.Add(val);
            return g;
        }
        static Control Rule() => new Border { Height = 1, Background = NotaPalette.GraphBorder, Margin = new Thickness(0, 3) };

        // The voice-mode strip: Poly N ▾ (its popover) · Mono · Choke.
        Control VoiceSeg()
        {
            var cells = new Border[3]; var texts = new TextBlock[3];
            Glyph? chevron = null;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
            for (int i = 0; i < 3; i++)
            {
                int iv = i;
                var tb = new TextBlock { FontSize = 7.5, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                Control content = tb;
                if (i == 0)   // Poly opens its popover: a chevron says so
                {
                    var chev = new Glyph(GlyphKind.ChevronDown, 5) { VerticalAlignment = VerticalAlignment.Center };
                    chevron = chev;
                    content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Children = { tb, chev } };
                }
                var c = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(5, 1), Cursor = new Cursor(StandardCursorType.Hand), Child = content };
                c.PointerPressed += (_, e) =>
                {
                    if (!e.GetCurrentPoint(c).Properties.IsLeftButtonPressed) return;
                    if (iv == 0 && Sel("voicemode", 3) == 0) PolyPopover().ShowAt(c);
                    else Write("voicemode", iv / 2.0);
                    Refresh(); e.Handled = true;
                };
                cells[i] = c; texts[i] = tb; row.Children.Add(c);
            }
            readouts.Add(() =>
            {
                int cur = Sel("voicemode", 3);
                for (int i = 0; i < 3; i++)
                {
                    bool on = i == cur;
                    cells[i].Background = on ? Brass : Brushes.Transparent;
                    texts[i].Foreground = on ? OnAccent : TextTertiary;
                    texts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                    texts[i].Text = i == 0 ? $"Poly {Poly()}" : MosaicModel.VoiceNames[i];
                }
                if (chevron is not null) chevron.Foreground = cur == 0 ? OnAccent : TextTertiary;
            });
            if (I("voicemode") is var vmi and >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, vmi), "voicemode");
            ToolTip.SetTip(row, "Poly: chords (click again for the voice settings) · Mono: one at a time, legato with Glide · Choke: a new note cuts the ringing ones");
            var host = new Border
            {
                Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Control,
                Padding = new Thickness(1), Child = row, VerticalAlignment = VerticalAlignment.Center,
            };
            return host;
        }

        // The Poly popover: polyphony, the voices now, the stealing order, per key, old fade.
        Flyout PolyPopover()
        {
            var polySeg = Segments(MosaicModel.PolyCounts.Select(n => n.ToString(NotaNum.Culture)).ToArray(), () => Sel("polyphony", 4),
                iv => { Write("polyphony", iv / 3.0); Refresh(); }, out var ps, fill: true, padX: 4, fontSize: 9);
            var busyBar = new Border { Height = 3, Background = Brass, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = NotaRadius.Badge };
            var busyTrack = new Border { Height = 3, Background = NotaPalette.BorderDefault, CornerRadius = NotaRadius.Badge, Child = busyBar };
            var busyText = Mono("", TextSecondary, 8);
            void PaintBusy()
            {
                ps();
                busyBar.Width = busyTrack.Bounds.Width * Math.Clamp(snap.Voices / (double)Math.Max(1, Poly()), 0, 1);
                busyText.Text = $"{snap.Held} busy · {snap.PedalHeld} on pedal · {snap.Release} release";
            }
            var order = new StackPanel { Spacing = 2 };
            for (int i = 0; i < MosaicModel.StealOrder.Length; i++)
                order.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { Mono((i + 1).ToString(NotaNum.Culture), AccentBright, 8), new TextBlock { Text = MosaicModel.StealOrder[i], FontSize = 8.5, Foreground = TextPrimary } } });
            var perKey = NumBox(() => MosaicModel.PerKey(G("perkey")).ToString(NotaNum.Culture), d => SetP("perkey", (MosaicModel.PerKey(G("perkey")) - 1 + d) / 3.0),
                () => SetP("perkey", Def("perkey")), 30, "How many strikes of one key may ring together");
            var fade = NumBox(() => NotaNum.Unit(MosaicModel.OldFadeMs(G("oldfade")), "0", "ms"), d => SetP("oldfade", MosaicModel.OldFadeNorm(MosaicModel.OldFadeMs(G("oldfade")) + d)),
                () => SetP("oldfade", Def("oldfade")), 44, "How fast a stolen or superseded voice fades");
            var bottom = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            bottom.Children.Add(new StackPanel { Spacing = 3, Children = { Caps("Per key"), perKey } });
            var fadeCol = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Right, Children = { Caps("Old voice fade"), fade } };
            Grid.SetColumn(fadeCol, 1); bottom.Children.Add(fadeCol);
            var panel = new StackPanel
            {
                Width = 196, Spacing = 6, Margin = new Thickness(4),
                Children = { Caps("Polyphony"), polySeg, busyTrack, busyText, Rule(), Caps("Voice stealing · order"), order, Rule(), bottom },
            };
            var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
            timer.Tick += (_, _) => PaintBusy();
            var fly = new Flyout { Content = panel, Placement = PlacementMode.BottomEdgeAlignedRight };
            fly.Opened += (_, _) => { PaintBusy(); timer.Start(); };
            fly.Closed += (_, _) => timer.Stop();
            return fly;
        }

        Control VoicesRail()
        {
            var sliders = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    Slider("Volume", "volume", () => DbLin(G("volume"))),
                    Slider("Pan", "pan", () => PanS(G("pan") * 2 - 1), bipolar: true),
                    Slider("Glide", "glide", () => GlideText(G("glide")), tip: "Slide from the last note — in Mono, legato"),
                },
            };
            // The pedal: on / off (CC64 heard), lit while it is down.
            var pedalDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
            var pedalTxt = Mono("CC64", TextTertiary, 8);
            var pedalPill = new Border
            {
                CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(5, 1), Cursor = new Cursor(StandardCursorType.Hand),
                Background = Brushes.Transparent, Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { pedalDot, pedalTxt } },
            };
            pedalPill.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(pedalPill).Properties.IsLeftButtonPressed) return; Write("pedal", G("pedal") >= 0.5f ? 0 : 1); Refresh(); e.Handled = true; };
            if (I("pedal") is var pdi and >= 0) MidiLearn.Bind(pedalPill, MidiTarget.PluginParam(track, -1, pdi), "Voice Pedal");
            ToolTip.SetTip(pedalPill, "The sustain pedal: a keyboard's CC64 holds released keys — click to stop listening to it");
            var voicesTxt = Mono("", TextPrimary, 9);
            readouts.Add(() =>
            {
                bool on = G("pedal") >= 0.5f, down = on && snap.Pedal;
                pedalDot.Fill = down ? Success : on ? Brass : NotaPalette.BorderStrong;
                pedalPill.BorderBrush = down ? Success : on ? NotaPalette.BorderBrass : BorderDef;
                pedalTxt.Foreground = on ? TextPrimary : TextTertiary;
                voicesTxt.Text = $"{snap.Voices} / {Poly()}";
            });
            var pedalRow = new Grid { ColumnDefinitions = new ColumnDefinitions("50,Auto,*"), ColumnSpacing = 4 };
            var pl = new TextBlock { Text = "PEDAL", FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
            pedalRow.Children.Add(pl);
            Grid.SetColumn(pedalPill, 1); pedalRow.Children.Add(pedalPill);
            voicesTxt.HorizontalAlignment = HorizontalAlignment.Right;
            Grid.SetColumn(voicesTxt, 2); pedalRow.Children.Add(voicesTxt);
            sliders.Children.Add(pedalRow);

            var outKnob = PKnob("output", "Output", v => NotaNum.Pct(v), NotaSize.KnobMain, 50, tip: "The level after everything");
            var velKnob = PKnob("velamount", "Vel → Vol", v => NotaNum.Pct(v), cellW: 50, arc: Teal, tip: "How much velocity sets the level");
            var meter = new MeterBar { VerticalAlignment = VerticalAlignment.Stretch, Width = MeterScale.StereoWidth };
            ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
            var outRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 4 };
            outRow.Children.Add(outKnob);
            Grid.SetColumn(velKnob, 1); outRow.Children.Add(velKnob);
            meter.HorizontalAlignment = HorizontalAlignment.Right; meter.Margin = new Thickness(0, 2, 0, 2);
            Grid.SetColumn(meter, 2); outRow.Children.Add(meter);
            var outHost = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = outRow };
            var body = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6 };
            body.Children.Add(sliders);
            Grid.SetRow(outHost, 1); body.Children.Add(outHost);
            return RailBody(RailHead("Voices", VoiceSeg()), body);
        }

        Control GroupRail()
        {
            if (ui.Group < 0 || ui.Group >= prog.Groups.Count) return VoicesRail();
            var gr = prog.Groups[ui.Group];
            string title = $"Group · {(gr.Name.Length > 0 ? gr.Name : (ui.Group + 1).ToString(NotaNum.Culture))}";
            Grid GSlider(string label, Func<double> get, Action<double> set, Func<string> text, bool bipolar, bool mod)
            {
                var row = SliderRow(label, get, v => { set(v); Refresh(); }, text, out var sync, end: () => { Commit(); rebuildGroups(); }, reset: () => { set(bipolar ? 0.5 : 24.0 / 36.0); Commit(); rebuildGroups(); },
                    bipolar: bipolar, labelWidth: 46, valueWidth: 40, modulation: mod);
                readouts.Add(sync);
                return row;
            }
            var gain = GSlider("Gain", () => (gr.GainDb + 24) / 36.0, v => gr.GainDb = Math.Round((v * 36 - 24) * 10) / 10, () => DbS(gr.GainDb), false, false);
            var tune = GSlider("Tune", () => (gr.Tune + 100) / 200.0, v => gr.Tune = Math.Round(v * 200 - 100), () => Cents(gr.Tune), true, true);
            var seq = NumBox(() => gr.SeqLen.ToString(NotaNum.Culture), d => gr.SeqLen = Math.Clamp(gr.SeqLen + d, 1, 16), () => gr.SeqLen = 1, 36,
                "Round-robin steps: the group's zones take turns 1 … N (each zone's step is set by its file)", () => { Commit(); rebuildGroups(); });
            var seqRow = new Grid { ColumnDefinitions = new ColumnDefinitions("50,Auto") };
            seqRow.Children.Add(new TextBlock { Text = "SEQ LEN", FontSize = NotaType.RowLabel, FontWeight = FontWeight.Bold, LetterSpacing = NotaType.RowLabelTracking, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });
            Grid.SetColumn(seq, 1); seqRow.Children.Add(seq);
            var rrSeg = Segments(new[] { "Sequential", "Random", "No repeat" }, () => gr.RrMode, iv => { gr.RrMode = iv; Commit(); rebuildGroups(); }, out var rrSync, fill: true, padX: 3, fontSize: 7.5);
            readouts.Add(rrSync);
            int ExclVal(bool off) { var z = prog.Zones.FirstOrDefault(z => z.Group == ui.Group); return z is null ? 0 : off ? z.OffBy : z.Excl; }
            void SetExcl(bool off, int v) { foreach (var z in prog.Zones.Where(z => z.Group == ui.Group)) { if (off) z.OffBy = v; else z.Excl = v; } }
            var exG = NumBox(() => ExclVal(false) is var v && v > 0 ? $"group {v}" : "group —", d => SetExcl(false, Math.Clamp(ExclVal(false) + d, 0, 64)), () => SetExcl(false, 0), 70,
                "This group's exclusive number", () => { Commit(); rebuildGroups(); SetMap(); });
            var exO = NumBox(() => ExclVal(true) is var v && v > 0 ? $"off_by {v}" : "off_by —", d => SetExcl(true, Math.Clamp(ExclVal(true) + d, 0, 64)), () => SetExcl(true, 0), 70,
                "Voices of this group stop when a note of that exclusive group starts", () => { Commit(); rebuildGroups(); SetMap(); });
            var exRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { exG, exO } };
            var note = new TextBlock { Text = "Exclusive is for hi-hats and valves: a new note chokes the off_by group.", FontSize = 7.5, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
            var body = new StackPanel
            {
                Spacing = 6,
                Children = { gain, tune, seqRow, Caps("Round-robin"), rrSeg, Caps("Exclusive · group / off by"), exRow, note },
            };
            return RailBody(RailHead(title, sub: $"{ZonesIn(ui.Group)} zones"), body);
        }

        Control ReportRail()
        {
            var r = ui.Report!;
            var list = new StackPanel { Spacing = 0, Margin = new Thickness(0, 0, 9, 0) };
            list.Children.Add(KV("Regions", () => r.Regions.ToString(NotaNum.Culture)));
            list.Children.Add(KV("Imported", () => r.Imported.ToString(NotaNum.Culture)));
            list.Children.Add(KV("Groups", () => r.Groups.ToString(NotaNum.Culture)));
            list.Children.Add(KV("#include", () => r.Includes.ToString(NotaNum.Culture)));
            list.Children.Add(KV("CC sets", () => r.CcState.Count == 0 ? "—" : $"{r.CcState.Count} · via set_cc"));
            list.Children.Add(KV("Paths ..\\ → /", () => r.PathsFixed.ToString(NotaNum.Culture)));
            if (r.Missing > 0) list.Children.Add(KV("Missing", () => r.Missing.ToString(NotaNum.Culture), NotaPalette.DangerBright));
            list.Children.Add(Rule());
            list.Children.Add(Caps("Ignored"));
            foreach (var (fam, n) in r.Ignored.OrderByDescending(kv => kv.Value).Take(7))
                list.Children.Add(KV(fam, () => n.ToString(NotaNum.Culture), TextTertiary));
            if (r.Ignored.Count == 0) list.Children.Add(Mono("nothing", TextTertiary, 8));
            return RailBody(RailHead("Import", sub: $"{r.File} · {NotaNum.Unit(r.Seconds, "0.0", "s")}"), new ScrollViewer { Content = list, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled });
        }

        Control MemoryRail()
        {
            var body = new StackPanel
            {
                Spacing = 2,
                Children =
                {
                    Caps("Memory"),
                    KV("Loaded", () => NotaNum.Bytes(status.RamBytes)),
                    KV("Estimated", () => NotaNum.Bytes(RamEstimate())),
                    KV("Files", () => $"{status.FilesDone} / {status.FilesTotal}"),
                    KV("Missing", () => status.Missing.ToString(NotaNum.Culture), status.Missing > 0 ? NotaPalette.DangerBright : null),
                    Rule(),
                    KV("Total in RAM", () => NotaNum.Bytes(status.State == 2 ? status.RamBytes : RamEstimate())),
                },
            };
            return RailBody(RailHead("Voices", VoiceSeg()), body);
        }

        Control SourceRail()
        {
            string reference = prog.Files.FirstOrDefault() ?? "";
            string where = prog.PackId.Length > 0 ? $"pack://{prog.PackId}/{MosaicPaths.FileName(reference)}…" : reference;
            var body = new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    Caps("Source"),
                    new TextBlock { Text = where, FontSize = 8, Foreground = TextPrimary, FontFamily = NotaFonts.MonoFamily, TextWrapping = TextWrapping.Wrap },
                    new TextBlock { Text = "Samples are not copied into the project; the program keeps where they are.", FontSize = 7.5, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap },
                },
            };
            return RailBody(RailHead("Voices", VoiceSeg()), body);
        }

        rebuildRail = () =>
        {
            Control c =
                AllMissing() && prog.Zones.Count > 0 ? SourceRail()
                : Loading() ? MemoryRail()
                : ui.ReportOpen && ui.Report is not null && !ui.ReportDismissed ? ReportRail()
                : tab == 2 ? GroupRail()
                : VoicesRail();
            railHost.Content = c;
            Refresh();
        };

        // ======================= status ==========================================================
        string Summary()
        {
            if (AllMissing() && prog.Zones.Count > 0) return "Samples unavailable · notes muted";
            if (Loading()) return $"Loading · {NotaNum.Bytes(status.RamBytes)} / {NotaNum.Bytes(RamEstimate())} · notes muted";
            if (prog.IsEmpty) return "Empty · drop samples or a folder, or pick a preset";
            string name = prog.Name.Length > 0 ? prog.Name + " · " : "";
            switch (tab)
            {
                case 0 when ui.ReleaseView:
                {
                    int sounding = snap.Zones.Count(z => z.Zone >= 0 && z.Zone < prog.Zones.Count && prog.Zones[z.Zone].Release);
                    return $"Release · {sounding} sounding · {NotaNum.Db(MosaicModel.RelVolDb(G("relvol")), signed: true)} · {NotaNum.Db(MosaicModel.RelLenDb(G("rellen")), signed: true)} after 2 s hold";
                }
                case 0:
                case 1:
                case 3:
                {
                    string pedal = snap.Pedal ? " · pedal down" : "";
                    string missing = status.Missing > 0 ? $" · {status.Missing} missing" : "";
                    return $"{name}{prog.Summary()}{pedal}{missing}";
                }
                case 2:
                {
                    string rel = G("relon") >= 0.5f ? $"release {NotaNum.Db(MosaicModel.RelVolDb(G("relvol")), signed: true)}" : "release off";
                    var rrg = prog.Groups.FirstOrDefault(g => g.SeqLen > 1);
                    string rr = rrg is null ? "no round-robin" : $"rr {rrg.SeqLen} {MosaicProgram.RrNames[rrg.RrMode].ToLowerInvariant()}";
                    return $"{prog.Groups.Count} groups · {rr} · {rel}";
                }
                case 4: return $"Env {EnvTl()} · vel {NotaNum.Pct(G("velamount"))} · curve {VelCurveWord()}";
                default:
                {
                    int t = Sel("filtertype", 4);
                    if (t == 0) return "Filter off — the samples pass untouched";
                    return $"Filter {MosaicModel.FilterNames[t]} · cutoff {NotaNum.Hz(MosaicModel.CutoffHz(G("cutoff")))} · reso {NotaNum.Pct(G("resonance"))}";
                }
            }
        }
        var statusBar = new Grid { Height = StatusH, ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(statusText);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0), Background = NotaPalette.SurfaceAbyss,
            Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ======================= assembly ========================================================
        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = NotaSpace.DeviceGap, Margin = new Thickness(NotaSpace.DeviceGap) };
        body.Children.Add(tabPanel);
        Grid.SetColumn(rail, 1); body.Children.Add(rail);
        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };
        HookDrop(root, dropLayer);

        bool lastLoading = Loading(), lastMissing = AllMissing();
        showTab = () =>
        {
            ui.Tab = tab;
            for (int i = 0; i < TabNames.Length; i++)
            {
                bool on = i == tab;
                tabCells[i].Background = on ? NotaPalette.SurfaceRaised : Brushes.Transparent;
                tabCells[i].BorderBrush = on ? Brass : Brushes.Transparent;
                tabTexts[i].Foreground = on ? AccentBright : TextTertiary;
                tabTexts[i].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
                pages[i].IsVisible = on;
            }
            arSeg.IsVisible = hasRelease && tab == 0;
            if (articulation is not null) articulation.IsVisible = tab == 0;
            rebuildRail();
        };
        refresh = () =>
        {
            foreach (var r in readouts) r();
            statusText.Text = Summary();
            statusText.Foreground = AllMissing() && prog.Zones.Count > 0 ? NotaPalette.DangerBright : Loading() ? AccentBright : TextSecondary;
            bool ld = Loading();
            loadLayer.IsVisible = ld;
            zoneRow.IsVisible = !ld; loadRow.IsVisible = ld;
            if (ld)
            {
                double f = LoadFrac();
                loadPct.Text = NotaNum.Pct(f);
                loadLine.Text = $"{NotaNum.Bytes(status.RamBytes)} / {NotaNum.Bytes(RamEstimate())} · {status.FilesDone} / {status.FilesTotal} files";
                loadBar.Width = loadTrack.Bounds.Width * f;
            }
        };
        void Live()
        {
            engine.TryGetMosaicStatus(track, out status);
            // Another program (undo, a preset, the MCP): start over from it.
            if (status.Serial != knownSerial) { knownSerial = status.Serial; ctx.RequestRebuild(); return; }
            int n = engine.InstrumentScope(track, scope);
            MosaicModel.Parse(scope.AsSpan(0, Math.Max(0, n)), snap);
            samSnap.Live = snap.Live; samSnap.Voices = snap.Voices; samSnap.Env = snap.Env; samSnap.Stage = snap.Stage;
            samSnap.Note = snap.Note; samSnap.CutoffHz = snap.CutoffHz; samSnap.PlayPos = snap.Pos;
            map.SetSounding(snap.Zones, snap.Keys);
            if (tab == 1) wave.SetPlayhead(snap.Live && snap.Zone == ui.Zone ? snap.Pos : -1);
            meta.Text = $"{Rate()} · {snap.Voices}/{Poly()} voices · CPU {NotaNum.Unit(engine.CpuLoad * 100, "0.0", "%")}";
            bool ld = Loading(), miss = AllMissing();
            if (ld != lastLoading || miss != lastMissing)
            {
                lastLoading = ld; lastMissing = miss;
                if (miss != (mapCell.Children.Count == 1)) { ctx.RequestRebuild(); return; }
                waveSampleId = -1;
                rebuildRail();
            }
            Refresh();
        }
        ctx.AddDeviceRefresher(Live);
        ctx.SetInstLiveViz(Refresh);
        showTab();
        Live();
        return root;

        // ======================= drop: files / a folder → a multisample ============================
        Control DropOverlay()
        {
            var title = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Center };
            var line = new TextBlock { FontSize = 8.5, Foreground = TextPrimary, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            var hint = new TextBlock { FontSize = 7.5, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, Text = "Opens the mapping preview. A single file replaces the sample, as before." };
            var dash = new Rectangle
            {
                Stroke = AccentBright, StrokeThickness = 1.2, StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                RadiusX = 6, RadiusY = 6, Fill = NotaPalette.Wash(NotaPalette.SurfaceCard, 0xE6),
            };
            var box = new Panel
            {
                Margin = new Thickness(5), IsVisible = false, IsHitTestVisible = false,
                Children = { dash, new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center, Children = { title, line, hint } } },
            };
            box.Tag = (Action<string, string>)((t, l) => { title.Text = t; line.Text = l; });
            return box;
        }

        void HookDrop(Control target, Control overlay)
        {
            DragDrop.SetAllowDrop(target, true);
            void Show(bool on, string t = "", string l = "")
            {
                overlay.IsVisible = on;
                if (on && overlay.Tag is Action<string, string> set) set(t, l);
            }
            DragDrop.AddDragOverHandler(target, (_, e) =>
            {
                var item = BrowserView.CurrentDrag;
                bool folder = item is { Kind: BrowserItemKind.Folder } && Directory.Exists(item.Path);
                bool sample = item is { Kind: BrowserItemKind.Sample };
                bool files = item is null && e.DataTransfer.Contains(DataFormat.File);
                if (!folder && !sample && !files) return;
                e.DragEffects = DragDropEffects.Copy;
                e.Handled = true;
                ctx.HideDropGlow();
                if (sample) Show(true, "Release to load the sample", item!.Name);
                else
                {
                    var paths = folder ? new List<string> { item!.Path } : DroppedPaths(e);
                    var audio = ExpandAudio(paths);
                    if (audio.Count == 1) Show(true, "Release to load the sample", Path.GetFileName(audio[0]));
                    else
                    {
                        var stems = audio.Select(p => MultisampleMapper.ParseName(p).Stem).Distinct().ToList();
                        Show(true, "Release to build a multisample",
                            audio.Count == 0 ? "a folder of samples" : $"{audio.Count} files · {stems.Count} instrument{(stems.Count == 1 ? "" : "s")} found by name: {string.Join(", ", stems.Take(4))}");
                    }
                }
            });
            DragDrop.AddDragLeaveHandler(target, (_, _) => Show(false));
            DragDrop.AddDropHandler(target, (_, e) =>
            {
                Show(false);
                var item = BrowserView.CurrentDrag;
                List<string> paths;
                if (item is { Kind: BrowserItemKind.Sample }) paths = new List<string> { item.Path };
                else if (item is { Kind: BrowserItemKind.Folder } && Directory.Exists(item.Path)) paths = new List<string> { item.Path };
                else if (item is null) paths = DroppedPaths(e);
                else return;
                var audio = ExpandAudio(paths);
                if (audio.Count == 0) return;
                e.Handled = true;
                if (audio.Count == 1 && !paths.Any(Directory.Exists))
                {
                    var packs = App.Services?.GetService(typeof(IMosaicPacks)) as IMosaicPacks;
                    int rootNote = SamplerModel.DetectRoot(audio[0]) is var d and >= 0 ? d : 60;
                    prog = MosaicProgram.Single(packs?.ToRef(audio[0]) ?? audio[0], rootNote, Path.GetFileNameWithoutExtension(audio[0]));
                    ui.Zone = 0; ui.Report = null;
                    Commit(rebuild: true);
                    return;
                }
                CreateMultisampleRequested?.Invoke(track, paths.Any(Directory.Exists) ? paths : audio);
            });
        }

        // ======================= S ===============================================================
        Control BuildMini()
        {
            var zonesTxt = Mono("", TextSecondary, 8);
            var ramTxt = Mono("", TextSecondary, 8);
            var miniBar = new Border { Height = 3, Background = Brass, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = NotaRadius.Badge };
            var miniTrack = new Border { Height = 3, Background = NotaPalette.BorderDefault, CornerRadius = NotaRadius.Badge, Child = miniBar, Margin = new Thickness(0, 4, 0, 0) };
            var inner = new Grid { RowDefinitions = new RowDefinitions("*,Auto,Auto"), RowSpacing = 4, Margin = new Thickness(6, 6, 6, 4) };
            inner.Children.Add(map);
            Grid.SetRow(miniTrack, 1); inner.Children.Add(miniTrack);
            var foot = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            foot.Children.Add(zonesTxt);
            Grid.SetColumn(ramTxt, 1); foot.Children.Add(ramTxt);
            Grid.SetRow(foot, 2); inner.Children.Add(foot);
            var island = SectionBox(inner);
            island.Margin = new Thickness(NotaSpace.DeviceGap);
            void MiniLive()
            {
                engine.TryGetMosaicStatus(track, out status);
                if (status.Serial != knownSerial) { knownSerial = status.Serial; ctx.RequestRebuild(); return; }
                int n = engine.InstrumentScope(track, scope);
                MosaicModel.Parse(scope.AsSpan(0, Math.Max(0, n)), snap);
                map.SetSounding(snap.Zones, snap.Keys);
                bool ld = Loading();
                miniTrack.IsVisible = ld;
                miniBar.Width = miniTrack.Bounds.Width * LoadFrac();
                zonesTxt.Text = ld ? $"Loading {NotaNum.Pct(LoadFrac())}" : prog.IsEmpty ? "Empty" : $"{prog.Zones.Count} zones · {prog.VelocityLayers} layer{(prog.VelocityLayers == 1 ? "" : "s")}";
                zonesTxt.Foreground = ld ? AccentBright : AllMissing() && prog.Zones.Count > 0 ? NotaPalette.DangerBright : TextSecondary;
                ramTxt.Text = ld ? $"{NotaNum.Bytes(status.RamBytes)} / {NotaNum.Bytes(RamEstimate())}" : NotaNum.Bytes(status.RamBytes);
            }
            ctx.AddDeviceRefresher(MiniLive);
            MiniLive();
            var dropMini = DropOverlay();
            var host = new Panel { Children = { island, dropMini } };
            HookDrop(host, dropMini);
            return host;
        }
    }

    private static List<string> DroppedPaths(DragEventArgs e)
    {
        var list = new List<string>();
        if (!e.DataTransfer.Contains(DataFormat.File)) return list;
        var files = e.DataTransfer.TryGetFiles();
        if (files is null) return list;
        foreach (var f in files) if (f.TryGetLocalPath() is { Length: > 0 } p) list.Add(p);
        return list;
    }

    private static List<string> ExpandAudio(IEnumerable<string> paths)
    {
        var list = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
            {
                try { list.AddRange(Directory.EnumerateFiles(p, "*", SearchOption.AllDirectories).Where(MultisampleMapper.IsAudio).Take(4000)); } catch { }
            }
            else if (MultisampleMapper.IsAudio(p)) list.Add(p);
        }
        return list;
    }

    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    private static Border HeaderStrip(Control child) => new()
    {
        BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = child,
    };
}
