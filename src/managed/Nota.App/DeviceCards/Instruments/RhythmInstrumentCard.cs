// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — the Nota Rhythm editor (instrument kind 12): an eight-voice drum
// machine in the 700 × 260 card the almanac draws for it. Reading order, left to right:
//
//   KIT 104       the voices — a hue dot, the name, SYN / SMP. Click selects and plays the
//                 voice; drop a file on one to load it as a sample; right-click for more.
//   VOICE         the selected voice's editor. Synth: the HIT contour (drag it — across is
//                 Decay, up / down Tune) and Tune · Decay · Punch · Tone · Drive · Level.
//                 Sample: the file with its Start / End region (drag the lines), Reverse,
//                 and Start · Length · Tune (st) · Decay · Drive · Level.
//   PERFORM 186   Swing · Human · Accent (teal — they shape the playing, not the sound),
//                 Master and Glue (the bus compressor), the output meter.
//   STEP          a full-width strip: the selected voice's sixteen steps in bank A–D, a
//                 velocity lane under them. Brass is the step, bright brass the accent, a
//                 brass outline a quiet step; teal is the playhead and the velocity.
//   status        what the voice is and where its steps are · voices · rate · tempo · CPU.
//
// Every knob is a plugin param → automation / persist / clone, followed live. The steps are
// structural state, edited via engine.InstrumentAction and read from the state blob; the
// engine bumps an edit revision in its scope, so an edit made elsewhere (MCP) shows up here.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;
using RM = Nota.Application.RhythmModel;

namespace Nota.App;

internal sealed class RhythmInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "DRUM MACHINE";
    public double CardWidth => 700;

    public string? VoiceLabel(IAudioEngine engine, int trackId, int active) => $"{Math.Max(0, active)}/{RM.Voices}";

    private const double KitW = 104, RailW = 186, StepH = 46, HeadH = 18, StatusH = 18, GraphW = 112;

    // Kit-voice identity hues — one per voice, painted only through NotaPalette.Ink.
    private static readonly string[] Dots = { "#C77F55", "#D8A03D", "#3E8E8E", "#7A6FB0", "#9AA64A", "#58B368", "#5AA0B8", "#B57286" };

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        int I(string id) => idx.TryGetValue(id, out var i) ? i : -1;
        float G(string id) => I(id) is var i and >= 0 ? engine.PluginParamGet(track, -1, i) : 0f;
        void SetP(string id, float v) { if (I(id) is var i and >= 0) engine.PluginParamSet(track, -1, i, Math.Clamp(v, 0f, 1f)); }
        void Begin(string id) { if (I(id) >= 0) engine.BeginAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void End(string id) { if (I(id) >= 0) engine.EndAutomationWrite(track, AutomationTarget.PluginParam, -1, -1, id); }
        void Write(string id, float v) { Begin(id); SetP(id, v); End(id); }
        void Act(int id, int iarg, float farg = 0f) => engine.InstrumentAction(track, id, iarg, farg);

        // ---- state ----------------------------------------------------------------------
        var pat = RM.Parse(engine.GetPluginState(track, -1), pc);
        int sel = pat.SelectedVoice;
        int bank = pat.CurrentBank;
        var isSample = new bool[RM.Voices];
        for (int v = 0; v < RM.Voices; v++) isSample[v] = engine.RhythmVoiceSource(track, v) == 1;
        long sid = engine.TryGetRhythmVoiceInfo(track, sel, out var vinfo) ? vinfo.SampleId : 0;
        bool sampleMode = isSample[sel] && sid != 0;
        string sampleName = sid != 0 ? engine.SampleName(sid) : "";
        if (sampleName.Length == 0 && sid != 0) sampleName = "sample";
        double sampleSecs = sid != 0 && engine.TryGetSampleInfo(sid, out var sinfo) && sinfo.SampleRate > 0 ? sinfo.Frames / sinfo.SampleRate : 0;
        string P(string p) => RM.Id(sel, p);
        IBrush Hue(int v) => NotaPalette.Ink(Dots[v]);

        var readouts = new List<Action>();
        var scope = new float[RM.ScopeLength];
        int lastRev = -1;
        float flashSel = 0, posSel = -1;
        var strip = new RhythmStepStrip();
        var bankCells = new Border[RM.Banks];
        var bankTexts = new TextBlock[RM.Banks];

        static TextBlock Cap(string t, IBrush? c = null) => new()
        {
            Text = t, FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Bold,
            LetterSpacing = NotaType.SectionLabelTracking, Foreground = c ?? TextTertiary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        static TextBlock Mono(string t, IBrush? c = null, double fs = 7) => new()
        {
            Text = t, FontSize = fs, Foreground = c ?? TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, FontFamily = NotaFonts.MonoFamily,
        };

        var status = new TextBlock { FontSize = 8, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var meta = Mono("", TextSecondary, 8);

        void Refresh() { foreach (var r in readouts) r(); }

        // ---- shared builders ---------------------------------------------------------------
        Control PKnob(string id, string label, Func<float, string> fmt, double size = 30, double cellW = 42, string? tip = null)
        {
            if (!idx.TryGetValue(id, out var pi)) return new Panel();
            var value = new TextBlock { Text = fmt(G(id)), Foreground = TextPrimary };
            var knob = new Knob(G(id), 1.0)
            {
                Accent = true, Default = engine.InstrumentParamDefault(track, pi), Width = size, Height = size,
            };
            knob.ValueChanged += v => { engine.PluginParamSet(track, -1, pi, (float)v); value.Text = fmt((float)v); Refresh(); };
            knob.GestureBegin += () => Begin(id);
            knob.GestureEnd += () => End(id);
            ctx.AddInstFader(pi, knob, value, fmt);
            MidiLearn.Bind(knob, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            var cell = KnobCell(label, knob, value, cellW);
            cell.VerticalAlignment = VerticalAlignment.Center;
            if (tip is not null) ToolTip.SetTip(cell, tip);
            return cell;
        }

        Grid Slider(string label, string id, string tip)
        {
            int pi = I(id);
            var row = SliderRow(label, () => G(id), v => { SetP(id, (float)v); Refresh(); }, () => RM.Pct(G(id)), out var sync,
                begin: pi >= 0 ? () => Begin(id) : null,
                end: pi >= 0 ? () => End(id) : null,
                reset: pi >= 0 ? () => { Write(id, engine.InstrumentParamDefault(track, pi)); Refresh(); } : null,
                labelWidth: 38, valueWidth: 28, modulation: true);
            row.ColumnSpacing = 6;
            readouts.Add(sync);
            ToolTip.SetTip(row, tip);
            if (pi >= 0) MidiLearn.Bind(row, MidiTarget.PluginParam(track, -1, pi), engine.PluginParamName(track, -1, pi));
            return row;
        }

        // A small raised button; a latching one turns brass-washed while on.
        Border SmallButton(Control content, TextBlock label, Action click, Func<bool>? on = null)
        {
            var b = new Border
            {
                Height = 14, CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Padding = new Thickness(6, 0),
                Cursor = new Cursor(StandardCursorType.Hand), Child = content, VerticalAlignment = VerticalAlignment.Center,
            };
            void Paint()
            {
                bool lit = on?.Invoke() ?? false;
                b.Background = lit ? NotaPalette.AccentSubtle : NotaPalette.SurfaceRaised;
                b.BorderBrush = lit ? NotaPalette.BorderBrass : BorderDef;
                label.Foreground = lit ? AccentBright : NotaPalette.TextStrong;
            }
            b.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(b).Properties.IsLeftButtonPressed) return;
                click(); Paint(); e.Handled = true;
            };
            readouts.Add(Paint);
            Paint();
            return b;
        }

        // A freshly loaded one-shot plays at its own pitch and in full (the synth's tune
        // default would transpose it); select its voice so the card shows it.
        void Loaded(int voice)
        {
            Write(RM.Id(voice, "tune"), 0.5f);
            Write(RM.Id(voice, "start"), 0f);
            Write(RM.Id(voice, "length"), 1f);
            Act(RM.A_SelectVoice, voice);
        }

        async void LoadSample(Control anchor, int voice)
        {
            var topLevel = TopLevel.GetTopLevel(anchor);
            if (topLevel is null) return;
            var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Load a sample into {RM.VoiceNames[voice]}", AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.flac", "*.mp3", "*.aif", "*.aiff", "*.ogg" } } },
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p && engine.SetRhythmVoiceSample(track, voice, p))
            {
                Loaded(voice);
                ctx.NotifyChanged(); ctx.RequestRebuild();
            }
        }

        void AcceptDrops(Control target, int voice, Action<bool> hover)
        {
            DragDrop.SetAllowDrop(target, true);
            DragDrop.AddDragOverHandler(target, (_, e) => { if (BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.Copy; hover(true); ctx.HideDropGlow(); e.Handled = true; } });
            DragDrop.AddDragLeaveHandler(target, (_, _) => hover(false));
            DragDrop.AddDropHandler(target, (_, e) =>
            {
                hover(false);
                foreach (var it in BrowserView.DroppedItems(e))
                    if (it.Path is { Length: > 0 } p && engine.SetRhythmVoiceSample(track, voice, p))
                    {
                        Loaded(voice);
                        ctx.NotifyChanged(); ctx.RequestRebuild();
                        break;
                    }
                e.Handled = true;
            });
        }

        // ---- KIT ------------------------------------------------------------------------------
        var kitRows = new Grid { Margin = new Thickness(2) };
        var kitDots = new Ellipse[RM.Voices];
        for (int v = 0; v < RM.Voices; v++)
        {
            kitRows.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
            int vv = v; bool on = v == sel;
            var dot = new Ellipse { Width = 5, Height = 5, Fill = Hue(v), VerticalAlignment = VerticalAlignment.Center };
            kitDots[v] = dot;
            var nm = new TextBlock
            {
                Text = RM.VoiceNames[v], FontSize = 8, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = on ? AccentBright : TextPrimary, VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(5, 0, 4, 0),
            };
            var tag = Mono(isSample[v] ? "SMP" : "SYN", on ? AccentBright : TextTertiary);
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(5, 0) };
            row.Children.Add(dot);
            Grid.SetColumn(nm, 1); row.Children.Add(nm);
            Grid.SetColumn(tag, 2); row.Children.Add(tag);
            IBrush restBorder = on ? NotaPalette.BorderBrass : Brushes.Transparent;
            var box = new Border
            {
                CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1), Cursor = new Cursor(StandardCursorType.Hand),
                Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent, BorderBrush = restBorder, Child = row,
            };
            ToolTip.SetTip(box, $"{RM.VoiceNames[v]} · MIDI {DeviceCardKit.NoteName(RM.MidiNotes[v])}\nClick: select and play · drop a file to load it · right-click for more");
            box.PointerPressed += (_, e) =>
            {
                var pt = e.GetCurrentPoint(box);
                e.Handled = true;
                if (pt.Properties.IsRightButtonPressed) { KitMenu(vv, box); return; }
                if (!pt.Properties.IsLeftButtonPressed) return;
                Act(RM.A_Audition, vv, 0.8f);
                if (vv != sel) { Act(RM.A_SelectVoice, vv); ctx.RequestRebuild(); }
            };
            AcceptDrops(box, vv, h => box.BorderBrush = h ? Teal : restBorder);
            Grid.SetRow(box, v); kitRows.Children.Add(box);
        }
        void KitMenu(int v, Control anchor)
        {
            var fly = new MenuFlyout();
            var load = new MenuItem { Header = "Load sample…" };
            load.Click += (_, _) => LoadSample(anchor, v);
            fly.Items.Add(load);
            bool has = engine.TryGetRhythmVoiceInfo(track, v, out var vi) && vi.SampleId != 0;
            var src = new MenuItem { Header = isSample[v] ? "Play the synth engine" : "Play the loaded sample", IsEnabled = isSample[v] || has };
            src.Click += (_, _) => { Act(RM.A_SetSource, v, isSample[v] ? 0f : 1f); ctx.NotifyChanged(); ctx.RequestRebuild(); };
            fly.Items.Add(src);
            fly.Items.Add(new Separator());
            var clear = new MenuItem { Header = $"Clear its steps in bank {RM.BankNames[bank]}" };
            clear.Click += (_, _) => { Act(RM.A_ClearVoice, v); ctx.NotifyChanged(); ReloadPattern(); };
            fly.Items.Add(clear);
            fly.ShowAt(anchor);
        }
        var kitHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(7, 0) };
        kitHead.Children.Add(Cap("KIT"));
        var kitSub = Mono("syn/smp", NotaPalette.TextDisabled); kitSub.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(kitSub, 1); kitHead.Children.Add(kitSub);
        var kit = Section(kitHead, kitRows);
        kit.Width = KitW;

        // ---- VOICE ----------------------------------------------------------------------------
        var srcSeg = Segments(new[] { "Synth", "Sample" }, () => sampleMode ? 1 : 0, i =>
        {
            if (i == 1)
            {
                if (sid != 0) { Act(RM.A_SetSource, sel, 1f); ctx.NotifyChanged(); ctx.RequestRebuild(); }
                else LoadSample(kit, sel);
            }
            else if (sampleMode) { Act(RM.A_SetSource, sel, 0f); ctx.NotifyChanged(); ctx.RequestRebuild(); }
        }, out _, padX: 6, fontSize: 7);
        ToolTip.SetTip(srcSeg, "Synth: the voice's own engine · Sample: a loaded one-shot (loads one if the voice has none)");

        var folder = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M1 3.5 L4.5 3.5 L5.6 2 L11 2 L11 10 L1 10 Z"), Stroke = NotaPalette.TextStrong, StrokeThickness = 1.2,
            Width = 7, Height = 7, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center,
        };
        var fileText = new TextBlock
        {
            Text = sampleMode ? sampleName : "Load file…", FontSize = 7, FontWeight = FontWeight.SemiBold, MaxWidth = 90,
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center,
        };
        if (sampleMode) fileText.FontFamily = NotaFonts.MonoFamily;
        var fileBtn = SmallButton(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { folder, fileText } }, fileText,
            () => LoadSample(kit, sel), () => sampleMode);
        readouts.Add(() => folder.Stroke = sampleMode ? AccentBright : NotaPalette.TextStrong);
        ToolTip.SetTip(fileBtn, sampleMode ? $"{sampleName} — click to load another file" : "Load a one-shot into this voice (or drop a file on the voice)");

        var voiceHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(7, 0), VerticalAlignment = VerticalAlignment.Center };
        voiceHead.Children.Add(Cap("VOICE"));
        voiceHead.Children.Add(new TextBlock { Text = RM.VoiceNames[sel], FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        var voiceTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        voiceTools.Children.Add(srcSeg);
        voiceTools.Children.Add(fileBtn);
        if (sampleMode)
        {
            var revText = new TextBlock { Text = "Reverse", FontSize = 7, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
            var rev = SmallButton(revText, revText,
                () => { Write(P("reverse"), G(P("reverse")) >= 0.5f ? 0f : 1f); Refresh(); }, () => G(P("reverse")) >= 0.5f);
            ToolTip.SetTip(rev, "Play the region back to front");
            if (I(P("reverse")) is var ri and >= 0) MidiLearn.Bind(rev, MidiTarget.PluginParam(track, -1, ri), engine.PluginParamName(track, -1, ri));
            voiceTools.Children.Add(rev);
        }
        var voiceHeadRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        voiceHeadRow.Children.Add(voiceHead);
        Grid.SetColumn(voiceTools, 1); voiceHeadRow.Children.Add(voiceTools);

        Control graph;
        if (sampleMode)
        {
            var sv = new RhythmSampleView { Width = GraphW };
            if (engine.TryGetSampleInfo(sid, out var si)) sv.SetSamples(engine.ReadSample(sid), si.Channels);
            sv.LoadRequested = () => LoadSample(sv, sel);
            sv.DragBegin = _ => { Begin(P("start")); Begin(P("length")); };
            sv.DragEnd = _ => { End(P("start")); End(P("length")); };
            sv.Dragged = (which, f) =>
            {
                float st = G(P("start")), en = (float)RM.RegionEnd(st, G(P("length")));
                if (which == 0) { float ns = (float)Math.Clamp(f, 0, en - 0.01); SetP(P("start"), ns); SetP(P("length"), en - ns); }
                else SetP(P("length"), (float)Math.Clamp(f - st, 0.01, 1.0));
                Refresh();
            };
            readouts.Add(() =>
            {
                float st = G(P("start")), en = (float)RM.RegionEnd(st, G(P("length")));
                string secs = sampleSecs > 0 ? " · " + NotaNum.Unit((en - st) * sampleSecs, "0.00", "s") : "";
                sv.Set(sampleName, Hue(sel), st, en, G(P("reverse")) >= 0.5f, posSel, $"{RM.Pct(st)} → {RM.Pct(en)}{secs}");
            });
            ToolTip.SetTip(sv, "Drag the brass lines to set Start and End · double-click to load another file");
            AcceptDrops(sv, sel, _ => { });
            graph = sv;
        }
        else
        {
            var hv = new RhythmHitView { Width = GraphW };
            hv.DragBegin = () => { Begin(P("decay")); Begin(P("tune")); };
            hv.DragEnd = () => { End(P("decay")); End(P("tune")); };
            hv.Dragged = (dx, dy) => { SetP(P("decay"), G(P("decay")) + (float)(dx * 1.2)); SetP(P("tune"), G(P("tune")) + (float)(dy * 1.2)); Refresh(); };
            readouts.Add(() =>
            {
                float d = G(P("decay")), t = G(P("tune")), pu = G(P("punch"));
                double dec = RM.DecaySeconds(sel, d, pu), pit = RM.PitchSeconds(sel, t);
                string cap = pit > 0 ? $"{RM.Ms(pit)} · {RM.Ms(dec)}" : RM.Ms(dec);
                hv.Set(RM.VoiceNames[sel], dec, pit, RM.DecaySeconds(sel, 1, 0) * 0.8, pu, flashSel, cap);
            });
            ToolTip.SetTip(hv, "The hit: amp contour over the pitch drop · drag across for Decay, up / down for Tune (Shift = fine)");
            AcceptDrops(hv, sel, _ => { });
            graph = hv;
        }

        var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"), VerticalAlignment = VerticalAlignment.Center };
        Control[] cells = sampleMode
            ? new[]
            {
                PKnob(P("start"), "START", v => RM.Pct(v), tip: "Where in the file the hit starts"),
                PKnob(P("length"), "LENGTH", v => RM.Pct(v), tip: "How much of the file plays, from Start"),
                PKnob(P("tune"), "TUNE", v => RM.Semis(RM.SampleSemis(v)), tip: "Transpose, ±12 semitones"),
                PKnob(P("decay"), "DECAY", v => RM.Pct(v), tip: "Amp decay — shortens the hit"),
                PKnob(P("drive"), "DRIVE", v => RM.Pct(v), tip: "Saturation"),
                PKnob(P("level"), "LEVEL", v => RM.Pct(v), tip: "The voice's level in the kit"),
            }
            : new[]
            {
                PKnob(P("tune"), "TUNE", v => RM.Pct(v), tip: "Pitch (Kick, Snare, Tom, Hats, Perc) or noise colour (Clap, Rim)"),
                PKnob(P("decay"), "DECAY", v => RM.Pct(v), tip: "Amp decay"),
                PKnob(P("punch"), "PUNCH", v => RM.Pct(v), tip: "Attack click / snap"),
                PKnob(P("tone"), "TONE", v => RM.Pct(v), tip: "Snare body ↔ noise · Hats metal ↔ noise · Perc ring ratio"),
                PKnob(P("drive"), "DRIVE", v => RM.Pct(v), tip: "Saturation"),
                PKnob(P("level"), "LEVEL", v => RM.Pct(v), tip: "The voice's level in the kit"),
            };
        for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); knobs.Children.Add(cells[i]); }

        var voiceBody = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6, Margin = new Thickness(6, 5) };
        voiceBody.Children.Add(graph);
        Grid.SetColumn(knobs, 1); voiceBody.Children.Add(knobs);
        var voice = Section(voiceHeadRow, voiceBody);

        // ---- PERFORM --------------------------------------------------------------------------
        var perfHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(8, 0) };
        perfHead.Children.Add(Cap("PERFORM"));
        var perfSub = Mono("shift+step = accent", NotaPalette.TextDisabled); perfSub.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(perfSub, 1); perfHead.Children.Add(perfSub);

        var sliders = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                Slider("SWING", "swing", "Swing: delays every second 16th — at 100\u2009% by half a step"),
                Slider("HUMAN", "humanize", "Humanize: a random nudge to every hit's velocity"),
                Slider("ACCENT", "accent", "How much louder an accented step plays"),
            },
        };

        var meter = new MeterBar { Height = 48, Width = MeterScale.StereoWidth, VerticalAlignment = VerticalAlignment.Center };
        ctx.AddDeviceRefresher(() => { if (engine.TryGetTrackMeter(track, out var m)) meter.Push(m); });
        var meterScale = new Grid { Height = 48, RowDefinitions = new RowDefinitions(string.Join(",", ScaleRows(0, -12, -48))) };
        for (int i = 0; i < 3; i++)
        {
            var c = Mono(i == 0 ? "0" : i == 1 ? "−12" : "−48", NotaPalette.TextAxis);
            Grid.SetRow(c, i * 2 + 1); meterScale.Children.Add(c);
        }
        var meterBlock = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"), ColumnSpacing = 4,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
        };
        meterBlock.Children.Add(meter);
        Grid.SetColumn(meterScale, 1); meterBlock.Children.Add(meterScale);

        var master = PKnob("volume", "MASTER", v => RM.Pct(v), 40, 46, "The kit's output level");
        var glue = PKnob("glue", "GLUE", v => RM.Pct(v), 30, 40, "Glue: a bus compressor on the whole kit — holds the hits together (0 = off)");
        var perfFoot = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*"), ColumnSpacing = 6 };
        perfFoot.Children.Add(master);
        Grid.SetColumn(glue, 1); perfFoot.Children.Add(glue);
        Grid.SetColumn(meterBlock, 2); perfFoot.Children.Add(meterBlock);
        var perfFootHost = new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = perfFoot };

        var perfBody = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 6, Margin = new Thickness(8, 6) };
        perfBody.Children.Add(sliders);
        Grid.SetRow(perfFootHost, 1); perfBody.Children.Add(perfFootHost);
        var perform = Section(perfHead, perfBody);
        perform.Width = RailW;

        // ---- STEP -----------------------------------------------------------------------------
        void LoadStrip()
        {
            for (int s = 0; s < RM.Steps; s++)
            {
                strip.On[s] = pat.On[bank, sel, s]; strip.Acc[s] = pat.Acc[bank, sel, s];
                strip.Vel[s] = pat.Vel[bank, sel, s];
            }
            strip.Changed();
        }
        strip.Commit = s =>
        {
            int arg = sel * RM.Steps + s;
            bool on = strip.On[s], acc = strip.Acc[s];
            float vel = strip.Vel[s];
            if (pat.On[bank, sel, s] != on) { Act(RM.A_ToggleStep, arg); pat.On[bank, sel, s] = on; }
            if (on && Math.Abs(pat.Vel[bank, sel, s] - vel) > 0.5f / 255f) { Act(RM.A_SetVel, arg, vel); pat.Vel[bank, sel, s] = vel; }
            if (pat.Acc[bank, sel, s] != acc) { Act(RM.A_ToggleAccent, arg); pat.Acc[bank, sel, s] = acc; }
            ctx.NotifyChanged();
            Refresh();
        };
        void SetSteps(Func<int, (bool On, float Vel, bool Acc)> f)
        {
            for (int s = 0; s < RM.Steps; s++)
            {
                var (on, vel, acc) = f(s);
                strip.On[s] = on; strip.Vel[s] = on ? vel : strip.Vel[s]; strip.Acc[s] = on && acc;
                strip.Commit!(s);
            }
            strip.Changed();
        }
        strip.StepMenu = s =>
        {
            var fly = new MenuFlyout();
            MenuItem Item(string h, Action a, bool check = false)
            {
                var mi = new MenuItem { Header = h };
                if (check) mi.Icon = new Ellipse { Width = 6, Height = 6, Fill = Brass };
                mi.Click += (_, _) => a();
                fly.Items.Add(mi);
                return mi;
            }
            float cur = strip.Vel[s];
            bool on = strip.On[s];
            Item($"Step {s + 1} accent", () => { strip.On[s] = true; strip.Acc[s] = !(on && strip.Acc[s]); strip.Vel[s] = Math.Max(cur, RM.NormalVel); strip.Commit!(s); strip.Changed(); }, on && strip.Acc[s]);
            Item($"Step {s + 1} quiet", () => { strip.On[s] = true; strip.Acc[s] = false; strip.Vel[s] = RM.QuietVel; strip.Commit!(s); strip.Changed(); }, on && !strip.Acc[s] && cur < RM.QuietBelow);
            var velMenu = new MenuItem { Header = "Velocity" };
            foreach (var pv in new[] { 1f, 0.75f, 0.5f, 0.25f })
            {
                var mi = new MenuItem { Header = RM.Pct(pv) };
                if (on && Math.Abs(cur - pv) < 0.02f) mi.Icon = new Ellipse { Width = 6, Height = 6, Fill = Brass };
                mi.Click += (_, _) => { strip.On[s] = true; strip.Vel[s] = pv; strip.Commit!(s); strip.Changed(); };
                velMenu.Items.Add(mi);
            }
            fly.Items.Add(velMenu);
            fly.Items.Add(new Separator());
            Item("Fill every beat", () => SetSteps(k => (k % 4 == 0, RM.NormalVel, false)));
            Item("Fill every 8th", () => SetSteps(k => (k % 2 == 0, RM.NormalVel, false)));
            Item("Fill every 16th", () => SetSteps(_ => (true, RM.NormalVel, false)));
            Item("Off-beats", () => SetSteps(k => (k % 4 == 2, RM.NormalVel, false)));
            fly.Items.Add(new Separator());
            (bool, float, bool)[] Snap() { var a = new (bool, float, bool)[RM.Steps]; for (int k = 0; k < RM.Steps; k++) a[k] = (strip.On[k], strip.Vel[k], strip.Acc[k]); return a; }
            Item("Shift left", () => { var a = Snap(); SetSteps(k => a[(k + 1) % RM.Steps]); });
            Item("Shift right", () => { var a = Snap(); SetSteps(k => a[(k + RM.Steps - 1) % RM.Steps]); });
            Item("Reverse", () => { var a = Snap(); SetSteps(k => a[RM.Steps - 1 - k]); });
            fly.Items.Add(new Separator());
            Item($"Clear {RM.VoiceNames[sel]}", () => SetSteps(k => (false, strip.Vel[k], false)));
            fly.ShowAt(strip);
        };

        // Banks A–D: the playing bank in teal (it is the performance, not a parameter).
        var bankRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1 };
        for (int b = 0; b < RM.Banks; b++)
        {
            int bb = b;
            var tb = new TextBlock { Text = RM.BankNames[b], FontSize = 7, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var cell = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(5, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = tb };
            ToolTip.SetTip(cell, $"Bank {RM.BankNames[b]} — play and edit this pattern · right-click to copy or clear it");
            cell.PointerPressed += (_, e) =>
            {
                var pt = e.GetCurrentPoint(cell);
                e.Handled = true;
                if (pt.Properties.IsRightButtonPressed) { BankMenu(bb, cell); return; }
                if (!pt.Properties.IsLeftButtonPressed || bb == bank) return;
                Act(RM.A_SelectBank, bb); ctx.NotifyChanged(); ReloadPattern();
            };
            bankCells[b] = cell; bankTexts[b] = tb; bankRow.Children.Add(cell);
        }
        void PaintBanks()
        {
            for (int b = 0; b < RM.Banks; b++)
            {
                bool on = b == bank;
                bankCells[b].Background = on ? Teal : Brushes.Transparent;
                bankTexts[b].Foreground = on ? OnAccent : TextTertiary;
                bankTexts[b].FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        void BankMenu(int b, Control anchor)
        {
            var fly = new MenuFlyout();
            var copy = new MenuItem { Header = $"Copy {RM.BankNames[b]} to" };
            for (int d = 0; d < RM.Banks; d++)
            {
                if (d == b) continue;
                int dd = d;
                var mi = new MenuItem { Header = $"Bank {RM.BankNames[d]}" };
                mi.Click += (_, _) => { Act(RM.A_CopyBank, b * RM.Banks + dd); ctx.NotifyChanged(); ReloadPattern(); };
                copy.Items.Add(mi);
            }
            fly.Items.Add(copy);
            var clear = new MenuItem { Header = $"Clear bank {RM.BankNames[b]}" };
            clear.Click += (_, _) => { Act(RM.A_ClearBank, b); ctx.NotifyChanged(); ReloadPattern(); };
            fly.Items.Add(clear);
            fly.ShowAt(anchor);
        }
        var bankBox = new Border
        {
            Background = NotaPalette.BgSunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(1), VerticalAlignment = VerticalAlignment.Center, Child = bankRow,
        };

        var stepCount = Mono("", TextTertiary);
        stepCount.HorizontalAlignment = HorizontalAlignment.Right;
        readouts.Add(() => stepCount.Text = $"{pat.Count(bank, sel)} of {RM.Steps} · 1/16 · bank {RM.BankNames[bank]}");
        var stepHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), ColumnSpacing = 6, Height = 12 };
        stepHead.Children.Add(Cap("STEP"));
        var stepName = new TextBlock { Text = RM.VoiceNames[sel], FontSize = 8, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(stepName, 1); stepHead.Children.Add(stepName);
        Grid.SetColumn(bankBox, 2); stepHead.Children.Add(bankBox);
        Grid.SetColumn(stepCount, 3); stepHead.Children.Add(stepCount);
        var stepGrid = new Grid { RowDefinitions = new RowDefinitions("Auto,*"), RowSpacing = 3, Margin = new Thickness(6, 4) };
        stepGrid.Children.Add(stepHead);
        Grid.SetRow(strip, 1); stepGrid.Children.Add(strip);
        var steps = SectionBox(stepGrid);
        steps.Height = StepH;

        void ReloadPattern()
        {
            pat = RM.Parse(engine.GetPluginState(track, -1), pc);
            if (pat.SelectedVoice != sel) { ctx.RequestRebuild(); return; }
            bank = pat.CurrentBank;
            LoadStrip(); PaintBanks(); Refresh();
        }

        // ---- status ----------------------------------------------------------------------------
        readouts.Add(() => status.Text = RM.Summary(G, pat, sel, sampleMode, sampleName));
        var statusBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        statusBar.Children.Add(status);
        Grid.SetColumn(meta, 1); statusBar.Children.Add(meta);
        var statusHost = new Border
        {
            Height = StatusH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 1, 0, 0),
            Background = NotaPalette.SurfaceAbyss, Padding = new Thickness(8, 0), Child = statusBar,
        };

        // ---- live ------------------------------------------------------------------------------
        void Live()
        {
            int n = engine.InstrumentScope(track, scope);
            if (n >= RM.ScopeLength)
            {
                int rev = (int)scope[RM.S_Rev];
                if (lastRev >= 0 && rev != lastRev) ReloadPattern();
                lastRev = rev;
                int head = (int)Math.Round(scope[RM.S_Step]);
                strip.SetHead(head);
                for (int v = 0; v < RM.Voices; v++)
                    kitDots[v].Fill = scope[RM.S_Flash0 + v] > 0.1f ? Hue(v) : NotaPalette.Wash(NotaPalette.Ink(Dots[v]), 0x8C);
                float f = scope[RM.S_Flash0 + sel], p = scope[RM.S_Pos0 + sel];
                if (Math.Abs(f - flashSel) > 0.05f || Math.Abs(p - posSel) > 0.002f) { flashSel = f; posSel = p; Refresh(); }
            }
            int voices = Math.Max(0, engine.InstrumentVoiceCount(track));
            double sr = engine.SampleRate > 0 ? engine.SampleRate : 48000;
            meta.Text = $"{voices}/{RM.Voices} voices · {NotaNum.Unit(sr / 1000, "0.#", "kHz")} · {NotaNum.Bpm(engine.Bpm)} BPM · CPU {NotaNum.Unit(engine.CpuLoad * 100, "0.0", "%")}";
        }

        // ---- assembly --------------------------------------------------------------------------
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = NotaSpace.DeviceGap };
        top.Children.Add(kit);
        Grid.SetColumn(voice, 1); top.Children.Add(voice);
        Grid.SetColumn(perform, 2); top.Children.Add(perform);
        var body = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), RowSpacing = NotaSpace.DeviceGap, Margin = new Thickness(NotaSpace.DeviceGap) };
        body.Children.Add(top);
        Grid.SetRow(steps, 1); body.Children.Add(steps);

        DockPanel.SetDock(statusHost, Dock.Bottom);
        var root = new DockPanel { LastChildFill = true, Background = NotaPalette.Gutter, Children = { statusHost, body } };

        LoadStrip(); PaintBanks();
        ctx.AddDeviceRefresher(Live);
        ctx.SetInstLiveViz(Refresh);
        Refresh();
        Live();
        return root;
    }

    // A section: card ground, hairline, radius 6, with an 18px header strip.
    private static Border Section(Control head, Control body)
    {
        var headHost = new Border { Height = HeadH, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };
        var g = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        g.Children.Add(headHost);
        Grid.SetRow(body, 1); g.Children.Add(body);
        return SectionBox(g);
    }

    private static Border SectionBox(Control child) => new()
    {
        Background = NotaPalette.SurfaceCard, BorderBrush = BorderDef, BorderThickness = new Thickness(1),
        CornerRadius = NotaRadius.Tile, ClipToBounds = true, Child = child,
    };

    /// <summary>Row definitions that put a caption for each dB mark at its place on the meter.</summary>
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
}
