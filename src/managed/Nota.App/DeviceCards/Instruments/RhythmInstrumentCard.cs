// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota Rhythm editor (instrument kind 12), built to the
// shared 900×260 card shell. Layout:
//   KIT (left)     — eight selectable drum voices (SYN tag); the selection drives the rest.
//   VOICE (center) — Synth/Sample source (Sample disabled in Phase 1), engine label, the HIT
//                    preview, and the six engine knobs of the selected voice.
//   PERFORM (right)— Swing / Humanize / Accent.
//   STEP (bottom)  — the 16 step buttons for the selected voice + bank (click toggles,
//                    shift-click accents), a per-step velocity row, and a teal playhead.
// The 7 per-voice knobs + 4 globals are plugin-params → automation / persist / clone. The
// step patterns are structural state, edited via engine.InstrumentAction and read from the
// instrument state blob.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Nota.Application;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class RhythmInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public string Subtitle => "DRUM MACHINE";
    public double CardWidth => 900;

    // action ids — must match RhythmMachine::Act.
    private const int A_ToggleStep = 0, A_SetVel = 1, A_ToggleAccent = 2, A_SelectBank = 3, A_ClearBank = 4, A_SelectVoice = 5, A_SetSource = 6;
    private const int Voices = 8, Steps = 16;

    private static readonly string[] Names = { "Kick", "Snare", "Clap", "Rim", "Closed Hat", "Open Hat", "Tom", "Perc" };
    private static readonly string[] Engines = { "Analog", "Noise", "Clap", "Rim", "Metal", "Metal", "Analog", "Ring" };
    // Kit-voice identity hues — one dot per voice, themed through NotaPalette.Ink.
    private static readonly string[] Dots = { "#C4756A", "#D8A03D", "#7FC9C6", "#B58AC4", "#7E8A6A", "#8FB56A", "#6D8FB5", "#5B9E9C" };

    private static readonly IBrush Ink = NotaPalette.TextOnAccent;
    private static readonly IBrush Panel = NotaPalette.SurfaceInset;
    private static readonly IBrush Inset = NotaPalette.BgSunken;
    private static readonly IBrush SeqBg = NotaPalette.SurfaceDeep;
    private static readonly IBrush Border2 = NotaPalette.BorderDefault;
    private static readonly IBrush StepOff = NotaPalette.BgSunken;
    private static readonly IBrush StepOn = NotaPalette.VGradient((NotaPalette.Accent, 0), (NotaPalette.AccentDeep, 1));
    private static readonly IBrush StepAcc = NotaPalette.VGradient((NotaPalette.AccentGlow, 0), (NotaPalette.Accent, 1));
    private static readonly IBrush TealB = NotaPalette.Teal;
    private static readonly IBrush TealBright = NotaPalette.TealBright;
    private static readonly IBrush Muted = NotaPalette.TextTertiary;
    private static readonly IBrush VelFill = NotaPalette.VGradient((NotaPalette.Ink("#A7B58A"), 0), (NotaPalette.Ink("#7E8A6A"), 1));

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        int pc = engine.PluginParamCount(track, -1);
        var idx = new Dictionary<string, int>();
        for (int i = 0; i < pc; i++) idx[engine.PluginParamId(track, -1, i)] = i;
        float G(string id) => idx.TryGetValue(id, out var i) ? engine.PluginParamGet(track, -1, i) : 0f;
        void Act(int id, int iarg, float farg = 0f) => engine.InstrumentAction(track, id, iarg, farg);

        // ---- read pattern + selection from the instrument state blob ----
        byte[] st = engine.GetPluginState(track, -1);
        int bankByte = 4 + pc * 4, patBase = bankByte + 4;
        int sel = st.Length > bankByte + 1 ? Math.Clamp(st[bankByte + 1], (byte)0, (byte)(Voices - 1)) : 0;
        int bank = st.Length > bankByte ? Math.Clamp(st[bankByte], (byte)0, (byte)3) : 0;
        int Ix(int v, int s) => patBase + ((bank * Voices + v) * Steps + s) * 3;
        bool On(int v, int s) { int i = Ix(v, s); return i + 0 < st.Length && st[i] != 0; }
        float Vel(int v, int s) { int i = Ix(v, s) + 1; return i < st.Length ? st[i] / 255f : 0.7f; }
        bool Acc(int v, int s) { int i = Ix(v, s) + 2; return i < st.Length && st[i] != 0; }

        var readouts = new List<Action>();
        TextBlock Mono(string t, IBrush c, double fs = 9) { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono"); return tb; }
        TextBlock Sec(string t, IBrush? c = null) => new() { Text = t, FontSize = 8, FontWeight = FontWeight.Bold, Foreground = c ?? Muted, VerticalAlignment = VerticalAlignment.Center };

        // ---- KIT column -----------------------------------------------------
        var kitList = new StackPanel { Spacing = 1 };
        var kitDots = new Ellipse[Voices];
        for (int v = 0; v < Voices; v++)
        {
            int vv = v; bool selRow = v == sel;
            bool isSmp = engine.RhythmVoiceSource(track, v) == 1;
            var dot = new Ellipse { Width = 7, Height = 7, Fill = NotaPalette.Ink(Dots[v]), VerticalAlignment = VerticalAlignment.Center };
            kitDots[v] = dot;
            var nm = new TextBlock { Text = Names[v], FontSize = 10, Foreground = selRow ? NotaPalette.AccentBright : NotaPalette.TextStrong, VerticalAlignment = VerticalAlignment.Center };
            var tag = new TextBlock { Text = isSmp ? "SMP" : "SYN", FontSize = 7, FontWeight = FontWeight.Bold, Foreground = isSmp ? TealB : selRow ? Brass : Muted, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Height = 15, Margin = new Thickness(6, 0) };
            Grid.SetColumn(dot, 0); Grid.SetColumn(nm, 1); Grid.SetColumn(tag, 2);
            nm.Margin = new Thickness(6, 0, 0, 0);
            var rowBox = new Border { CornerRadius = new CornerRadius(4), Cursor = new Cursor(StandardCursorType.Hand), Child = row, Background = selRow ? NotaPalette.Wash(NotaPalette.Accent, 0x24) : Brushes.Transparent, BorderBrush = selRow ? NotaPalette.AccentTint : Brushes.Transparent, BorderThickness = new Thickness(1) };
            row.Children.Add(dot); row.Children.Add(nm); row.Children.Add(tag);
            rowBox.PointerPressed += (_, _) => { Act(A_SelectVoice, vv); ctx.RequestRebuild(); };
            // Drop an audio file (browser or Finder) onto a voice → load it as that voice's sample.
            ToolTip.SetTip(rowBox, "Drop a sample to load it into this voice");
            DragDrop.SetAllowDrop(rowBox, true);
            DragDrop.AddDragOverHandler(rowBox, (_, e) => { if (BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.Copy; rowBox.BorderBrush = TealB; ctx.HideDropGlow(); } });
            DragDrop.AddDragLeaveHandler(rowBox, (_, _) => rowBox.BorderBrush = selRow ? NotaPalette.AccentTint : Brushes.Transparent);
            DragDrop.AddDropHandler(rowBox, (_, e) =>
            {
                foreach (var it in BrowserView.DroppedItems(e))
                    if (it.Path is { Length: > 0 } p) { engine.SetRhythmVoiceSample(track, vv, p); break; }
                ctx.RequestRebuild(); e.Handled = true;
            });
            kitList.Children.Add(rowBox);
        }
        var kitHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        kitHead.Children.Add(Sec("KIT"));
        var kh = Sec("synth or sample"); kh.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(kh, 1); kitHead.Children.Add(kh);
        var kitScroll = new ScrollViewer
        {
            Content = kitList, Margin = new Thickness(0, 5, 0, 0),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        var kitDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(kitHead, Dock.Top); kitDock.Children.Add(kitHead);
        kitDock.Children.Add(kitScroll);
        var kitCol = new Border { Width = 188, BorderBrush = Border2, BorderThickness = new Thickness(0, 0, 1, 0), Padding = new Thickness(8, 7), Child = kitDock };

        // ---- VOICE column ---------------------------------------------------
        var voiceHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        voiceHead.Children.Add(new TextBlock { Text = "VOICE — ", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        voiceHead.Children.Add(new TextBlock { Text = Names[sel], FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        // Synth | Sample source toggle (Phase 2 — Sample loads a one-shot via the Sampler).
        int voiceSrc = engine.RhythmVoiceSource(track, sel);
        long voiceSid = engine.TryGetRhythmVoiceInfo(track, sel, out var vinfo) ? vinfo.SampleId : 0;
        bool sampleMode = voiceSrc == 1 && voiceSid != 0;
        var srcSeg = new Border { Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(1), HorizontalAlignment = HorizontalAlignment.Right };
        Border SrcChip(string text, bool on, IBrush onBg)
            => new() { Background = on ? onBg : Brushes.Transparent, CornerRadius = new CornerRadius(4), Padding = new Thickness(9, 2), Cursor = new Cursor(StandardCursorType.Hand),
                       Child = new TextBlock { Text = text, FontSize = 9, FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal, Foreground = on ? NotaPalette.BgApp : Muted } };
        var synthChip = SrcChip("Synth", !sampleMode, Brass);
        var sampleChip = SrcChip("Sample", sampleMode, TealB);
        synthChip.PointerPressed += (_, _) => { Act(A_SetSource, sel, 0f); ctx.RequestRebuild(); };
        async void LoadSampleDialog(Control anchor)
        {
            var top = TopLevel.GetTopLevel(anchor);
            if (top is null) return;
            var files = await top.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
            {
                Title = "Load drum sample", AllowMultiple = false,
                FileTypeFilter = new[] { new Avalonia.Platform.Storage.FilePickerFileType("Audio") { Patterns = new[] { "*.wav", "*.flac", "*.mp3", "*.aif", "*.aiff", "*.ogg" } } },
            });
            if (files.Count > 0 && files[0].TryGetLocalPath() is { } p) { engine.SetRhythmVoiceSample(track, sel, p); ctx.RequestRebuild(); }
        }
        sampleChip.PointerPressed += (_, _) => { if (voiceSid != 0) { Act(A_SetSource, sel, 1f); ctx.RequestRebuild(); } else LoadSampleDialog(sampleChip); };
        ToolTip.SetTip(sampleChip, "Load a one-shot sample for this voice (or drop a file on the voice)");
        srcSeg.Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Children = { synthChip, sampleChip } };
        var engBox = new Border { Height = 17, Background = Inset, BorderBrush = Border2, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = sampleMode ? "Sample" : Engines[sel], FontSize = 9, Foreground = NotaPalette.TextStrong, VerticalAlignment = VerticalAlignment.Center } };
        var voiceHeadRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(voiceHead, 0); Grid.SetColumn(srcSeg, 1); Grid.SetColumn(engBox, 2);
        voiceHeadRow.Children.Add(voiceHead); voiceHeadRow.Children.Add(srcSeg); voiceHeadRow.Children.Add(engBox);

        // HIT preview (synth) or WAVE preview (sample).
        var hit = new RhythmHitViz();
        var hitHead = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Height = 10 };
        hitHead.Children.Add(Sec(sampleMode ? "WAVE" : "HIT"));
        var hitVal = Mono("", sampleMode ? TealB : Brass, 8); hitVal.HorizontalAlignment = HorizontalAlignment.Right; Grid.SetColumn(hitVal, 1); hitHead.Children.Add(hitVal);
        Control hitInner;
        if (sampleMode)
        {
            var wave = new RhythmWaveViz();
            var data = engine.ReadSample(voiceSid);
            wave.SetSamples(data, 1);
            hitVal.Text = "click to replace";
            hitInner = wave;
        }
        else hitInner = hit;
        var hitDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(hitHead, Dock.Top); hitDock.Children.Add(hitHead);
        hitDock.Children.Add(new Border { Margin = new Thickness(0, 2, 0, 0), Child = hitInner });
        var hitBox = new Border { Width = 148, Background = Inset, BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(5, 4), Child = hitDock };
        if (sampleMode) { hitBox.Cursor = new Cursor(StandardCursorType.Hand); hitBox.PointerPressed += (_, _) => LoadSampleDialog(hitBox); }

        // engine knobs of the selected voice.
        string P(string p) => $"v{sel}_{p}";
        var knobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("tune"), "TUNE", DoRefresh, 40, 58));
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("decay"), "DECAY", DoRefresh, 40, 58));
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("punch"), "PUNCH", DoRefresh, 40, 58));
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("tone"), "TONE", DoRefresh, 40, 58));
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("drive"), "DRIVE", DoRefresh, 40, 58));
        knobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, P("level"), "LEVEL", DoRefresh, 40, 58));
        var voiceMid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        Grid.SetColumn(hitBox, 0); Grid.SetColumn(knobs, 1);
        voiceMid.Children.Add(hitBox); voiceMid.Children.Add(knobs);
        var voiceDock = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(voiceHeadRow, Dock.Top); voiceDock.Children.Add(voiceHeadRow);
        voiceDock.Children.Add(new Border { Margin = new Thickness(0, 6, 0, 0), Child = voiceMid });
        var voiceCol = new Border { Padding = new Thickness(10, 7), Child = voiceDock };

        // ---- PERFORM column -------------------------------------------------
        var perfKnobs = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
        perfKnobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, "swing", "SWING", DoRefresh, 40, 52, TealB));
        perfKnobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, "humanize", "HUMAN", DoRefresh, 40, 52, TealB));
        perfKnobs.Children.Add(InstrumentControls.InstKnob(ctx, idx, "accent", "ACCENT", DoRefresh, 40, 52));
        var perfDock = new DockPanel { LastChildFill = true };
        var perfHead = Sec("PERFORM"); DockPanel.SetDock(perfHead, Dock.Top); perfDock.Children.Add(perfHead);
        var perfFoot = Mono("shift+step = accent", NotaPalette.TextDisabled, 8); perfFoot.HorizontalAlignment = HorizontalAlignment.Center;
        DockPanel.SetDock(perfFoot, Dock.Bottom); perfDock.Children.Add(perfFoot);
        perfDock.Children.Add(perfKnobs);
        var perfCol = new Border { Width = 170, Background = Panel, BorderBrush = Border2, BorderThickness = new Thickness(1, 0, 0, 0), Padding = new Thickness(10, 7), Child = perfDock };

        var upper = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(kitCol, 0); Grid.SetColumn(voiceCol, 1); Grid.SetColumn(perfCol, 2);
        upper.Children.Add(kitCol); upper.Children.Add(voiceCol); upper.Children.Add(perfCol);

        // ---- STEP sequencer (bottom) ----------------------------------------
        var stepBtns = new Border[Steps];
        var velFills = new Border[Steps];
        var onArr = new bool[Steps]; var accArr = new bool[Steps]; var velArr = new float[Steps];
        for (int s = 0; s < Steps; s++) { onArr[s] = On(sel, s); accArr[s] = Acc(sel, s); velArr[s] = Vel(sel, s); }
        const double VelH = 11;

        void StyleStep(int s)
        {
            var b = stepBtns[s];
            bool on = onArr[s], acc = accArr[s];
            b.Background = acc && on ? StepAcc : on ? StepOn : StepOff;
            b.BorderBrush = acc && on ? NotaPalette.AccentBright : on ? NotaPalette.AccentHover : Border2;
            b.BorderThickness = new Thickness(1);
        }
        void SyncVel(int s) => velFills[s].Height = VelH * (onArr[s] ? Math.Max(0.12f, velArr[s]) : 0);

        // Four beat-groups, each four steps; both rows use flex columns so the 16 fill the width.
        var stepsGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 9 };
        var velGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 9 };
        for (int grp = 0; grp < 4; grp++)
        {
            var gG = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4 };
            var gV = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*"), ColumnSpacing = 4 };
            for (int k = 0; k < 4; k++)
            {
                int s = grp * 4 + k;
                var beat = new TextBlock { Text = (s % 4 == 0) ? (s + 1).ToString() : "", FontSize = 7, Foreground = Muted, Margin = new Thickness(4, 3, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false };
                var btn = new Border { Height = 24, CornerRadius = new CornerRadius(5), Child = beat, Cursor = new Cursor(StandardCursorType.Hand) };
                stepBtns[s] = btn; StyleStep(s);
                int ss = s;
                btn.PointerPressed += (_, e) =>
                {
                    bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                    if (shift) { Act(A_ToggleAccent, sel * Steps + ss); accArr[ss] = !accArr[ss]; if (accArr[ss] && !onArr[ss]) { Act(A_ToggleStep, sel * Steps + ss); onArr[ss] = true; } }
                    else { Act(A_ToggleStep, sel * Steps + ss); onArr[ss] = !onArr[ss]; if (onArr[ss] && velArr[ss] <= 0) velArr[ss] = 0.7f; }
                    StyleStep(ss); SyncVel(ss);
                };
                Grid.SetColumn(btn, k); gG.Children.Add(btn);

                var fill = new Border { VerticalAlignment = VerticalAlignment.Bottom, Background = VelFill, CornerRadius = new CornerRadius(2) };
                velFills[s] = fill; SyncVel(s);
                var vb = new Border { Background = NotaPalette.SurfaceAbyss, CornerRadius = new CornerRadius(2), Child = fill, Cursor = new Cursor(StandardCursorType.SizeNorthSouth), ClipToBounds = true };
                void SetVelFromY(PointerEventArgs pe)
                {
                    double h = Math.Max(1, vb.Bounds.Height);
                    float nv = (float)Math.Clamp(1.0 - pe.GetPosition(vb).Y / h, 0.0, 1.0);
                    velArr[ss] = nv; Act(A_SetVel, sel * Steps + ss, nv);
                    if (!onArr[ss]) { Act(A_ToggleStep, sel * Steps + ss); onArr[ss] = true; StyleStep(ss); }
                    SyncVel(ss);
                }
                vb.PointerPressed += (_, e) => { e.Pointer.Capture(vb); SetVelFromY(e); };
                vb.PointerMoved += (_, e) => { if (e.GetCurrentPoint(vb).Properties.IsLeftButtonPressed) SetVelFromY(e); };
                vb.PointerReleased += (_, e) => e.Pointer.Capture(null);
                Grid.SetColumn(vb, k); gV.Children.Add(vb);
            }
            Grid.SetColumn(gG, grp); stepsGrid.Children.Add(gG);
            Grid.SetColumn(gV, grp); velGrid.Children.Add(gV);
        }
        velGrid.Height = VelH;

        var stepHead = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        stepHead.Children.Add(new TextBlock { Text = "STEP — ", FontSize = 8, FontWeight = FontWeight.Bold, Foreground = Muted, VerticalAlignment = VerticalAlignment.Center });
        stepHead.Children.Add(new TextBlock { Text = Names[sel], FontSize = 8, FontWeight = FontWeight.Bold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        // bank chips A B C D
        var banks = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 1, Margin = new Thickness(8, 0, 0, 0) };
        for (int b = 0; b < 4; b++)
        {
            int bb = b; bool cur = b == bank;
            var chip = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1), Cursor = new Cursor(StandardCursorType.Hand), Background = cur ? TealB : Brushes.Transparent, Child = new TextBlock { Text = ((char)('A' + b)).ToString(), FontSize = 9, FontWeight = FontWeight.SemiBold, Foreground = cur ? NotaPalette.BgApp : Muted } };
            chip.PointerPressed += (_, _) => { Act(A_SelectBank, bb); ctx.RequestRebuild(); };
            banks.Children.Add(chip);
        }
        stepHead.Children.Add(banks);
        var playText = Mono("▶ –", TealB, 8); playText.HorizontalAlignment = HorizontalAlignment.Right;
        var stepHeadRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        stepHeadRow.Children.Add(stepHead); Grid.SetColumn(playText, 1); stepHeadRow.Children.Add(playText);

        var seqInner = new StackPanel { Spacing = 4, Children = { stepHeadRow, stepsGrid, velGrid } };
        var seqCol = new Border { Height = 78, Background = SeqBg, BorderBrush = Border2, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 6), Child = seqInner };

        // ---- assemble body --------------------------------------------------
        var body = new DockPanel { LastChildFill = true, Background = NotaPalette.BgApp };
        DockPanel.SetDock(seqCol, Dock.Bottom); body.Children.Add(seqCol);
        body.Children.Add(upper);

        // ---- live follow ----------------------------------------------------
        int lastHead = -1;
        var scope = new float[16];
        void DoRefresh()
        {
            if (!sampleMode) { hit.Set(G(P("decay")), G(P("tune")), G(P("punch")), 0); hitVal.Text = Names[sel]; }
        }
        void Refresh()
        {
            int n = engine.InstrumentScope(track, scope);
            int head = n > 0 ? (int)Math.Round(scope[0]) : -1;
            if (head != lastHead)
            {
                if (lastHead >= 0 && lastHead < Steps) StyleStep(lastHead);
                if (head >= 0 && head < Steps) { stepBtns[head].BorderBrush = TealBright; stepBtns[head].BorderThickness = new Thickness(2); }
                lastHead = head;
                playText.Text = head >= 0 ? $"▶ step {head + 1} / 16" : "▶ –";
            }
            float flash = (n > 1 + sel) ? scope[1 + sel] : 0f;
            if (!sampleMode) hit.Set(G(P("decay")), G(P("tune")), G(P("punch")), flash);
            // kit dot flashes
            for (int v = 0; v < Voices && 1 + v < n; v++) kitDots[v].Opacity = 0.55 + 0.45 * Math.Clamp(scope[1 + v], 0f, 1f);
        }
        ctx.SetInstLiveViz(Refresh);
        DoRefresh();
        return body;
    }
}
