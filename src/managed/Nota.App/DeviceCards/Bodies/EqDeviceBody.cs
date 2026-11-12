// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in Nota EQ-8 body (device kind 0), a build of the "Nota EQ-8" mockup
// (700 × 260), laid out like Dynamic EQ-8: a row of eight band chips over the response graph
// (nodes drag right on the curve; the ANALYZER Pre / Post / Off switch sits in its corner) and a
// panel on the right that edits the selected band (type, FREQ / GAIN / Q — RESO on the cuts —,
// the cut's SLOPE, its CHANNEL: stereo, mid, side, left or right) and the device's GLOBAL
// controls (Auto gain, SCALE, OUTPUT). The live readings come from the engine (Eq.h scopeRead).
// Every control is a device param (raw units), so automation / MIDI learn / presets / A-B /
// persistence come for free. S / L (#531, CardLayout): S (260) is the band chips over the graph
// and the selected band's Freq, Gain, Q and the output. XL (specs/085) is the L body in the panel
// above the chain: the graph takes the extra width and height. FullBleed — the shared shell draws the
// header (name · preset · badge · bypass).

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

internal sealed class EqDeviceBody : IDeviceBody
{
    public double Width => 700;   // the almanac device format: 700 × 260
    public double WidthFor(IAudioEngine engine, int trackId, int deviceIndex) => CardLayout.WidthFor(engine, trackId, deviceIndex, Width);
    public Control? HeaderAccessory(DeviceCardContext ctx, int deviceIndex) => CardLayout.SizeToggle(ctx, deviceIndex, xl: true);
    public bool SupportsXl => true;   // XL (specs/085): the L body below, stretched in the panel above the chain
    public bool FullBleed => true;
    public string? Subtitle => "Equalizer";   // the processing type, shown as the header badge

    public Control Build(DeviceCardContext ctx, int index)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId, di = index;
        int pc = Math.Min(engine.DeviceParamCount(track, di), Eq8.ParamCount);
        var mn = new float[Eq8.ParamCount];
        var mx = new float[Eq8.ParamCount];
        for (int p = 0; p < Eq8.ParamCount; p++)
        {
            mn[p] = p < pc ? engine.DeviceParamMin(track, di, p) : 0;
            mx[p] = p < pc ? engine.DeviceParamMax(track, di, p) : 1;
            if (mx[p] <= mn[p]) mx[p] = mn[p] + 1;
        }
        // Defaults of the appended params, for a device that somehow lacks them.
        float P(int p) => p < pc ? engine.DeviceGetParam(track, di, p)
            : p == Eq8.ScaleP ? 100f : p == Eq8.AnalyzerP ? Eq8.AnaPost : p == Eq8.AnaRangeP ? 1f : 0f;
        float B(int b, int f) => P(Eq8.P(b, f));
        void Begin(int p) => engine.BeginAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void End(int p) => engine.EndAutomationWrite(track, AutomationTarget.DeviceParam, di, p, "");
        void Raw(int p, float v) { if (p < pc) engine.DeviceSetParam(track, di, p, Math.Clamp(v, mn[p], mx[p])); }
        // A discrete edit (a click) is one automation gesture, so it records while the transport does.
        void SetP(int p, float v) { Begin(p); Raw(p, v); End(p); }
        void Learn(Control c, int p) { if (p < pc) MidiLearn.Bind(c, MidiTarget.DeviceParam(track, di, p), engine.DeviceParamName(track, di, p)); }

        var readouts = new List<Action>();
        var bandReadouts = new List<Action>();
        var scope = new float[Eq8.kScope];
        int scN = 0;

        var curve = new Eq8Curve(engine, track, di);
        void RefreshAll()
        {
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            curve.InvalidateVisual();
        }
        int Sel() => curve.SelectedBand;
        bool BandOn(int b) => B(b, Eq8.On) > 0.5f;
        int TypeOf(int b) => Math.Clamp((int)Math.Round(B(b, Eq8.TypeF)), 0, 5);
        int SlopeOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.SlopeBase + b)), 0, 2);
        int ChanOf(int b) => Math.Clamp((int)Math.Round(P(Eq8.ChannelBase + b)), 0, 4);
        bool Capable(int b) => Eq8.HasGain(TypeOf(b));
        bool Cut(int b) => Eq8.IsCut(TypeOf(b));
        bool AutoOn() => P(Eq8.AutoGainP) >= 0.5f;
        // Band labels and notes (specs/049): text on the device, each edit one undo step. A rack
        // chain's EQ-8 (hosted through a proxy engine) keeps params only, so it has none.
        bool textOk = engine is not System.Reflection.DispatchProxy;
        string LabelOf(int b) => engine.EqBandText(track, di, b, 0);
        string NoteOf(int b) => engine.EqBandText(track, di, b, 1);
        void SetText(int b, string label, string note)
        {
            engine.EqSetBandText(track, di, b, label, note);
            RefreshAll();
        }

        // ---- small builders ---------------------------------------------------------
        static TextBlock Caps(string t, IBrush? c = null, double fs = NotaType.KnobLabel) => new()
        { Text = NotaText.Label(t), FontSize = fs, FontWeight = FontWeight.Medium, Foreground = c ?? TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        static TextBlock Mono(string t, double fs, IBrush c)
        { var tb = new TextBlock { Text = t, FontSize = fs, Foreground = c, VerticalAlignment = VerticalAlignment.Center }; tb.BindResource(TextBlock.FontFamilyProperty, "Font.Measure"); return tb; }
        static T Docked<T>(T c, Dock d) where T : Control { DockPanel.SetDock(c, d); return c; }
        static T Col<T>(T c, int col) where T : Control { Grid.SetColumn(c, col); return c; }

        // ======================================================================
        // LEFT — band chips over the response graph
        // ======================================================================
        var chipRow = new UniformGrid { Rows = 1, Margin = new Thickness(3, 0), VerticalAlignment = VerticalAlignment.Center };
        for (int b = 0; b < Eq8.Bands; b++)
        {
            int bb = b;
            var num = Mono((b + 1).ToString(), 8, TextSecondary); num.FontWeight = FontWeight.Bold;
            var type = new TextBlock { FontSize = 7, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis };
            var chn = new TextBlock { FontSize = 7, FontWeight = FontWeight.Medium, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 0, 0, 0) };
            var hz = Mono("", 7, TextPrimary); hz.HorizontalAlignment = HorizontalAlignment.Right; hz.Margin = new Thickness(3, 0, 0, 0);
            // The type (or the band's label) takes what the frequency leaves, shortened to fit.
            var inner = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), ColumnSpacing = 0, Margin = new Thickness(4, 0) };
            inner.Children.Add(num); inner.Children.Add(Col(type, 1)); inner.Children.Add(Col(chn, 2)); inner.Children.Add(Col(hz, 3));
            var chip = new Border
            {
                Height = 15, Margin = new Thickness(1, 0), CornerRadius = NotaRadius.Cell, BorderThickness = new Thickness(1), ClipToBounds = true,
                Cursor = new Cursor(StandardCursorType.Hand), Child = inner,
            };
            ToolTip.SetTip(chip, Loc.F("device.eq.band_click_to_edit_it", b + 1));
            chip.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(chip).Properties.IsLeftButtonPressed) return;
                if (e.ClickCount == 2) SetP(Eq8.P(bb, Eq8.On), BandOn(bb) ? 0 : 1);
                curve.SelectedBand = bb; RefreshAll(); e.Handled = true;
            };
            Learn(chip, Eq8.P(bb, Eq8.On));
            readouts.Add(() =>
            {
                bool on = BandOn(bb), sel = bb == Sel();
                int c = ChanOf(bb);
                num.Foreground = !on ? TextDisabled : sel ? AccentBright : TextSecondary;
                string lab = LabelOf(bb);
                type.Text = lab.Length > 0 ? lab : Eq8.TypeShort[TypeOf(bb)];   // the band's label in place of its type
                type.Foreground = !on ? TextDisabled : lab.Length > 0 ? TextPrimary : TextSecondary;
                chn.Text = c == Eq8.St ? "" : Eq8.ChannelShort[c];
                chn.IsVisible = c != Eq8.St;
                chn.Foreground = on ? Eq8.ChannelInk(c, bright: true) ?? TextSecondary : TextDisabled;
                hz.Text = Eq8.HzShort(B(bb, Eq8.FreqF));
                hz.Foreground = on ? TextPrimary : TextDisabled;
                chip.BorderBrush = sel ? Brass : Brushes.Transparent;
                chip.Background = sel ? Raised : Sunken;
            });
            chipRow.Children.Add(chip);
        }
        var chipStrip = new Border { Height = 20, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = chipRow };
        DockPanel.SetDock(chipStrip, Dock.Top);
        curve.DefaultTip = Loc.T("device.eq.drag_a_node_frequency_and")
            + Loc.T("device.eq.double_click_empty_space_a");
        ToolTip.SetTip(curve, curve.DefaultTip);

        // ANALYZER Pre / Post / Off over the graph's top-left corner.
        var anaSeg = Segments(Eq8.AnalyzerSeg, () => Math.Clamp((int)Math.Round(P(Eq8.AnalyzerP)), 0, 2),
            i => { SetP(Eq8.AnalyzerP, i); RefreshAll(); }, out var anaSync, padX: 4);
        Learn(anaSeg, Eq8.AnalyzerP);
        ToolTip.SetTip(anaSeg, Loc.T("device.eq.analyzer_pre_the_input_spectrum"));
        readouts.Add(anaSync);
        // Range (dB below 0 dBFS at the graph's bottom) and Peak hold, shown while the analyzer is on.
        bool AnaOn() => Math.Clamp((int)Math.Round(P(Eq8.AnalyzerP)), 0, 2) != Eq8.AnaOff;
        var rangeSeg = Segments(Eq8.RangeSeg, () => Math.Clamp((int)Math.Round(P(Eq8.AnaRangeP)), 0, 2),
            i => { SetP(Eq8.AnaRangeP, i); RefreshAll(); curve.InvalidateVisual(); }, out var rangeSync, padX: 4);
        Learn(rangeSeg, Eq8.AnaRangeP);
        ToolTip.SetTip(rangeSeg, Loc.T("device.eq.analyzer_range_the_graphs_bottom"));
        bool PeakOn() => P(Eq8.AnaPeakP) >= 0.5f;
        var peakSw = Switch("Peak", PeakOn, () => { SetP(Eq8.AnaPeakP, PeakOn() ? 0 : 1); RefreshAll(); }, out var peakSync);
        Learn(peakSw, Eq8.AnaPeakP);
        ToolTip.SetTip(peakSw, Loc.T("device.eq.peak_hold_a_thin_line"));
        readouts.Add(() => { rangeSync(); peakSync(); rangeSeg.IsVisible = peakSw.IsVisible = AnaOn() && pc > Eq8.AnaPeakP; });
        bool LabelsOn() => pc <= Eq8.LabelsP || P(Eq8.LabelsP) >= 0.5f;
        var labelsSw = Switch("Labels", LabelsOn, () => { SetP(Eq8.LabelsP, LabelsOn() ? 0 : 1); RefreshAll(); }, out var labelsSync);
        labelsSw.Margin = new Thickness(6, 0, 0, 0);
        Learn(labelsSw, Eq8.LabelsP);
        ToolTip.SetTip(labelsSw, Loc.T("device.eq.labels_show_the_bands_labels"));
        readouts.Add(() => { labelsSync(); labelsSw.IsVisible = textOk && pc > Eq8.LabelsP; });
        var anaBar = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(5, 3), HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top, Children = { Caps("ANALYZER"), anaSeg, rangeSeg, peakSw, labelsSw },
        };
        anaBar.RenderTransform = new ScaleTransform(0.85, 0.85);
        anaBar.RenderTransformOrigin = new RelativePoint(0, 0, RelativeUnit.Relative);
        // Band labels stay clear of the bar (drawn at 85 %, from its margin's corner).
        anaBar.LayoutUpdated += (_, _) =>
        {
            var r = new Rect(0, 0, anaBar.Margin.Left + anaBar.Bounds.Width * 0.85 + 2, anaBar.Margin.Top + anaBar.Bounds.Height * 0.85 + 2);
            if (r != curve.Reserved) { curve.Reserved = r; curve.InvalidateVisual(); }
        };
        Control GraphPanel() => new RuleSection
        {
            Margin = new Thickness(0, 0, 5, 0),
            Child = new DockPanel { LastChildFill = true, Children = { chipStrip, new Border { Padding = new Thickness(5), Child = new Panel { Children = { curve, anaBar } } } } },
        };

        // ======================================================================
        // RIGHT — the selected band + the device's global controls (rebuilt when the selection
        // moves, so MIDI learn and automation bind to that band's own params)
        // ======================================================================
        var bandHost = new ContentControl();

        // The note popup (specs/049): the band's label and a multiline note. Text is kept when it
        // closes (Escape, a click outside, Ctrl / Cmd+Enter), as one undo step.
        // A label box takes Return so a pasted multi-line text arrives whole (a single-line box keeps
        // only its first line); its breaks and tabs become spaces as it lands.
        static void FlattenLabel(TextBox box) => box.TextChanged += (_, _) =>
        {
            var t = box.Text;
            if (t is null || t.IndexOfAny(['\r', '\n', '\t']) < 0) return;
            int caret = box.CaretIndex;
            var flat = t.Replace("\r\n", " ").Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
            box.Text = flat;
            box.CaretIndex = Math.Clamp(caret - (t.Length - flat.Length), 0, flat.Length);
        };

        void OpenNote(int b, Control anchor, bool atPointer)
        {
            var labelBox = new TextBox { Text = LabelOf(b), MaxLength = 24, AcceptsReturn = true, FontSize = 11, Watermark = "Label" };
            FlattenLabel(labelBox);
            var noteBox = new TextBox
            {
                Text = NoteOf(b), MaxLength = 2000, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontSize = 11,
                Height = 96, Watermark = Loc.T("device.eq.what_this_band_is_for"),
            };
            var head = new TextBlock { Text = NotaNum.F($"BAND {b + 1} · {Eq8.TypeNames[TypeOf(b)]}"), FontSize = NotaType.KnobLabel, FontWeight = FontWeight.Medium, Foreground = TextTertiary };
            var hint = new TextBlock { Text = (OperatingSystem.IsMacOS() ? "⌘" : "Ctrl+") + Loc.T("device.eq.enter_or_esc_to_close"), FontSize = 9, Foreground = TextTertiary };
            var fly = new Flyout
            {
                Content = new StackPanel { Width = 230, Spacing = 5, Children = { head, labelBox, noteBox, hint } },
                // From the graph it opens beside it, so the nodes stay in view.
                Placement = atPointer ? PlacementMode.RightEdgeAlignedTop : PlacementMode.BottomEdgeAlignedLeft,
            };
            void Key(object? _, KeyEventArgs ke)
            {
                bool cmd = (ke.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
                if (ke.Key == Avalonia.Input.Key.Escape || (ke.Key == Avalonia.Input.Key.Enter && (cmd || ke.Source == labelBox)))
                { fly.Hide(); ke.Handled = true; }
            }
            labelBox.AddHandler(InputElement.KeyDownEvent, Key, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            noteBox.AddHandler(InputElement.KeyDownEvent, Key, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            fly.Closed += (_, _) => SetText(b, labelBox.Text ?? "", noteBox.Text ?? "");
            fly.ShowAt(anchor);
            Avalonia.Threading.Dispatcher.UIThread.Post(() => { var f = LabelOf(b).Length == 0 ? labelBox : noteBox; f.Focus(); f.CaretIndex = f.Text?.Length ?? 0; },
                Avalonia.Threading.DispatcherPriority.Input);
        }
        if (textOk) curve.NoteRequested += b => OpenNote(b, curve, atPointer: true);

        Control BandPanel(int b)
        {
            bandReadouts.Clear();
            int pOn = Eq8.P(b, Eq8.On), pType = Eq8.P(b, Eq8.TypeF), pF = Eq8.P(b, Eq8.FreqF), pG = Eq8.P(b, Eq8.GainF),
                pQ = Eq8.P(b, Eq8.QF), pSlope = Eq8.SlopeBase + b, pChan = Eq8.ChannelBase + b;

            // 0..1 mappings (frequency and Q are log)
            bool IsLog(int p) => p == pF || p == pQ;
            double ToN(int p, double v) => IsLog(p)
                ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p])
                : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
            double FromN(int p, double n) { n = Math.Clamp(n, 0, 1); return IsLog(p) ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
            double DefOf(int p) => p == pF ? 1000 : p == pG ? 0 : p == pQ ? 0.71 : engine.DeviceParamDefault(track, di, p);

            // ---- header: badge · name · ON ----
            var badgeTxt = Mono((b + 1).ToString(), 8, OnAccent); badgeTxt.FontWeight = FontWeight.Bold; badgeTxt.HorizontalAlignment = HorizontalAlignment.Center;
            var badge = new Border { Width = 13, Height = 13, CornerRadius = NotaRadius.Group, VerticalAlignment = VerticalAlignment.Center, Child = badgeTxt };
            var name = new TextBlock { FontSize = 9, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
            var onSw = Switch("On", () => BandOn(b), () => { SetP(pOn, BandOn(b) ? 0 : 1); RefreshAll(); }, out var onSync);
            Learn(onSw, pOn);
            ToolTip.SetTip(onSw, Loc.T("device.eq.band_on_off_leaves_the"));
            bandReadouts.Add(onSync);
            bandReadouts.Add(() =>
            {
                bool on = BandOn(b);
                badge.Background = on ? AccentBright : NotaPalette.TextAxis;
                badgeTxt.Foreground = on ? OnAccent : TextTertiary;
                name.Text = Eq8.TypeNames[TypeOf(b)];
                name.Foreground = on ? TextPrimary : TextTertiary;
            });
            var head = new DockPanel { Height = 20, Margin = new Thickness(8, 0), Children = { Docked(onSw, Dock.Right), badge, name } };
            var headBorder = new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Child = head };

            // ---- type ----
            var typeSeg = Segments(Eq8.TypeSeg, () => TypeOf(b), i => { SetP(pType, i); RefreshAll(); }, out var typeSync, fill: true, padX: 0);
            Learn(typeSeg, pType);
            ToolTip.SetTip(typeSeg, Loc.T("device.eq.type_high_pass_low_shelf"));
            bandReadouts.Add(typeSync);

            // ---- knobs ----
            Control K(int p, string label, Func<string> fmt, string tip, Func<string>? liveLabel = null, Func<bool>? inactive = null)
            {
                var val = Mono(fmt(), 7, TextPrimary);
                var knob = new Knob(ToN(p, P(p)), 1.0) { Accent = true, Default = ToN(p, DefOf(p)), Width = 32, Height = 32 };
                knob.ValueChanged += v => { Raw(p, (float)FromN(p, v)); val.Text = fmt(); RefreshAll(); };
                knob.GestureBegin += () => Begin(p);
                knob.GestureEnd += () => End(p);
                Learn(knob, p);
                ToolTip.SetTip(knob, tip);
                var cell = KnobCell(label, knob, val, 54);
                TextBlock? lbl = null;
                if (cell is Panel pl) foreach (var c in pl.Children) if (c is TextBlock tb && tb != val) { lbl = tb; break; }
                bool? wasInactive = null;
                bandReadouts.Add(() =>
                {
                    if (!knob.Dragging) { double c = ToN(p, P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                    val.Text = fmt();
                    if (lbl is not null && liveLabel is not null) lbl.Text = liveLabel();
                    bool ina = inactive?.Invoke() ?? false;
                    if (wasInactive != ina) { Inactive.Set(cell, ina); wasInactive = ina; }
                });
                return cell;
            }
            var knobs = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), Children =
            {
                K(pF, "FREQ", () => Eq8.HzShort(B(b, Eq8.FreqF)) + (B(b, Eq8.FreqF) < 1000 ? " Hz" : ""), Loc.T("device.eq.frequency_or_drag_the_node")),
                Col(K(pG, "GAIN", () => Capable(b) ? Eq8.Db(B(b, Eq8.GainF)) + " dB" : "—", Loc.T("device.eq.gain_or_drag_the_node"),
                    inactive: () => !Capable(b)), 1),
                Col(K(pQ, "Q", () => NotaNum.F($"{B(b, Eq8.QF):0.00}"), Loc.T("device.eq.q_the_width_the_resonance"),
                    liveLabel: () => Cut(b) ? "Reso" : "Q"), 2),
            } };

            // ---- label + note ----
            var labelField = new TextBox
            {
                // AcceptsReturn and FlattenLabel, so a pasted multi-line text arrives whole; Enter commits.
                Text = LabelOf(b), MaxLength = 24, AcceptsReturn = true, FontSize = 9, MinHeight = 18, Height = 18,
                Padding = new Thickness(4, 1), VerticalContentAlignment = VerticalAlignment.Center, Watermark = Loc.T("device.eq.name_this_band"),
            };
            void Unfocus() { curve.Focusable = true; curve.Focus(); }   // hand the keys back to the card
            void CommitLabel() { if ((labelField.Text ?? "") != LabelOf(b)) SetText(b, labelField.Text ?? "", NoteOf(b)); }
            labelField.LostFocus += (_, _) => CommitLabel();
            labelField.AddHandler(InputElement.KeyDownEvent, (_, ke) =>
            {
                // Enter keeps the text, Escape reverts it; both hand focus back to the card.
                // Tunnelled, so Enter is ours before the box would take it as a line break.
                if (ke.Key == Avalonia.Input.Key.Enter) { CommitLabel(); ke.Handled = true; Unfocus(); }
                else if (ke.Key == Avalonia.Input.Key.Escape) { labelField.Text = LabelOf(b); ke.Handled = true; Unfocus(); }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            FlattenLabel(labelField);
            ToolTip.SetTip(labelField, Loc.T("device.eq.label_a_short_name_shown"));
            var noteBtn = new Button
            {
                Content = "Note", FontSize = 8, Height = 18, MinHeight = 18, Padding = new Thickness(6, 0), Margin = new Thickness(4, 0, 0, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
            };
            noteBtn.Click += (_, _) => { CommitLabel(); OpenNote(b, noteBtn, atPointer: false); };
            ToolTip.SetTip(noteBtn, Loc.T("device.eq.note_a_longer_note_on"));
            bandReadouts.Add(() =>
            {
                if (!labelField.IsFocused && (labelField.Text ?? "") != LabelOf(b)) labelField.Text = LabelOf(b);
                noteBtn.Foreground = NoteOf(b).Length > 0 ? AccentBright : TextSecondary;
            });
            var labelRow = new DockPanel { IsVisible = textOk, Children = { Docked(new Border { Width = 40, Child = Caps("LABEL") }, Dock.Left), Docked(noteBtn, Dock.Right), labelField } };

            // ---- slope + channel ----
            Control SegRow(string label, Control seg, Func<bool>? dim = null, bool rule = false)
            {
                var lbl = new Border { Width = 40, Child = Caps(label) };
                var row = new DockPanel { Children = { Docked(lbl, Dock.Left), seg } };
                if (dim is not null)
                {
                    bool? was = null;
                    bandReadouts.Add(() => { bool d = dim(); if (was != d) { row.Opacity = d ? 0.4 : 1; was = d; } });
                }
                return rule
                    ? new Border { BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0), Child = row }
                    : row;
            }
            var slopeSeg = Segments(Eq8.SlopeSeg, () => SlopeOf(b), i => { if (Cut(b)) { SetP(pSlope, i); RefreshAll(); } },
                out var slopeSync, fill: true, padX: 0, dim: () => !Cut(b));
            Learn(slopeSeg, pSlope);
            ToolTip.SetTip(slopeSeg, Loc.T("device.eq.slope_how_steep_the_high"));
            bandReadouts.Add(slopeSync);
            var chanSeg = Segments(Eq8.ChannelSeg, () => ChanOf(b), i => { SetP(pChan, i); RefreshAll(); }, out var chanSync, fill: true, padX: 0);
            Learn(chanSeg, pChan);
            ToolTip.SetTip(chanSeg, Loc.T("device.eq.channel_st_both_channels_mid"));
            bandReadouts.Add(chanSync);

            // ---- global: Auto gain, Scale, Output ----
            var autoSw = Switch("Auto gain", AutoOn, () => { SetP(Eq8.AutoGainP, AutoOn() ? 0 : 1); RefreshAll(); }, out var autoSync);
            Learn(autoSw, Eq8.AutoGainP);
            ToolTip.SetTip(autoSw, Loc.T("device.eq.auto_gain_adds_the_opposite"));
            bandReadouts.Add(autoSync);
            var globalRow = new Border
            {
                BorderBrush = NotaPalette.GraphBorder, BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(0, 5, 0, 0),
                Child = new DockPanel { Children = { Docked(autoSw, Dock.Right), Caps("GLOBAL") } },
            };
            Control SliderRow(string label, int p, Func<string> fmt, string tip, double def)
            {
                var trk = new SliderTrack { Bipolar = true, Ink = Teal, Reset = () => { SetP(p, (float)def); RefreshAll(); } };
                trk.Changed += v => { Raw(p, (float)(mn[p] + v * (mx[p] - mn[p]))); RefreshAll(); };
                trk.GestureBegin += () => Begin(p);
                trk.GestureEnd += () => End(p);
                Learn(trk, p);
                ToolTip.SetTip(trk, tip);
                var val = Mono(fmt(), 8, TextPrimary); val.TextAlignment = TextAlignment.Right; val.HorizontalAlignment = HorizontalAlignment.Right;
                ValueEntry.Attach(val, () => (P(p) - mn[p]) / (mx[p] - mn[p]), v => Raw(p, (float)(mn[p] + v * (mx[p] - mn[p]))), fmt,
                    () => Begin(p), () => End(p), RefreshAll, control: trk);
                bandReadouts.Add(() =>
                {
                    if (!trk.Dragging) trk.Norm = (P(p) - mn[p]) / (mx[p] - mn[p]);
                    val.Text = fmt();
                    val.Foreground = Math.Abs(P(p) - def) > 0.05 ? TextPrimary : TextSecondary;
                });
                var g = new Grid { ColumnDefinitions = new ColumnDefinitions("40,*,40"), ColumnSpacing = 6, Height = 11 };
                g.Children.Add(Caps(label));
                g.Children.Add(Col(trk, 1));
                g.Children.Add(Col(val, 2));
                return g;
            }
            var sliders = new StackPanel
            {
                Spacing = 5, Children =
                {
                    SliderRow("SCALE", Eq8.ScaleP, () => NotaNum.F($"{P(Eq8.ScaleP):0}\u2009%"),
                        Loc.T("device.eq.scale_multiplies_every_shelf_and"), 100),
                    SliderRow("OUTPUT", Eq8.OutputP, () => Eq8.Db(P(Eq8.OutputP)) + " dB", Loc.T("device.eq.output_the_level_after_the"), 0),
                },
            };

            var body = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,4,Auto,*,Auto,*,Auto,4,Auto,*,Auto,5,Auto"), Margin = new Thickness(8, 6, 8, 6),
            };
            void Row(Control c, int r) { Grid.SetRow(c, r); body.Children.Add(c); }
            Row(labelRow, 0); Row(typeSeg, 2); Row(knobs, 4);
            Row(SegRow("SLOPE", slopeSeg, () => !Cut(b), rule: true), 6);
            Row(SegRow("CHANNEL", chanSeg), 8);
            Row(globalRow, 10); Row(sliders, 12);
            var panel = new DockPanel { LastChildFill = true, Children = { Docked(headBorder, Dock.Top), body } };
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
            return panel;
        }

        // ---- L: the band chips over the graph, the selected band and the globals on the right ----
        Control LargeBody()
        {
            var right = new RuleSection { Width = 186, Child = bandHost };
            DockPanel.SetDock(right, Dock.Right);
            bandHost.Content = BandPanel(Sel());
            curve.SelectionChanged += () => { bandHost.Content = BandPanel(Sel()); RefreshAll(); };
            var bodyRow = new DockPanel { LastChildFill = true, Margin = new Thickness(5), Children = { right, GraphPanel() } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = bodyRow };
        }

        // ---- S (#531): the band chips over the graph and the selected band's Freq · Gain · Q and
        // the output ---------------------------------------------------------------------------
        Control MiniBody()
        {
            var host = new ContentControl();
            Control Knobs(int b)
            {
                bandReadouts.Clear();
                int pF = Eq8.P(b, Eq8.FreqF), pG = Eq8.P(b, Eq8.GainF), pQ = Eq8.P(b, Eq8.QF);
                Control K(int p, string label, bool log, Func<string> fmt, Func<bool>? inactive = null, bool bipolar = false)
                {
                    double ToN(double v) => log ? Math.Log(Math.Clamp(v, mn[p], mx[p]) / mn[p]) / Math.Log(mx[p] / mn[p]) : (Math.Clamp(v, mn[p], mx[p]) - mn[p]) / (mx[p] - mn[p]);
                    double FromN(double n) { n = Math.Clamp(n, 0, 1); return log ? mn[p] * Math.Pow(mx[p] / mn[p], n) : mn[p] + n * (mx[p] - mn[p]); }
                    var val = Mono(fmt(), 7, TextPrimary);
                    var knob = new Knob(ToN(P(p)), 1.0) { Accent = true, Bipolar = bipolar, Default = ToN(engine.DeviceParamDefault(track, di, p)), Width = 34, Height = 34 };
                    knob.ValueChanged += v => { Raw(p, (float)FromN(v)); val.Text = fmt(); curve.InvalidateVisual(); RefreshAll(); };
                    knob.GestureBegin += () => Begin(p);
                    knob.GestureEnd += () => End(p);
                    Learn(knob, p);
                    bandReadouts.Add(() =>
                    {
                        if (!knob.Dragging) { double c = ToN(P(p)); if (Math.Abs(c - knob.Value) > 1e-4) knob.Value = c; }
                        val.Text = fmt();
                        if (inactive is not null) knob.IsDim = inactive();
                    });
                    return KnobCell(label, knob, val, 56);
                }
                return new UniformGrid { Rows = 1, Margin = new Thickness(0, 6, 0, 0), Children = {
                    K(pF, "FREQ", true, () => Eq8.HzShort(B(b, Eq8.FreqF)) + (B(b, Eq8.FreqF) < 1000 ? " Hz" : "")),
                    K(pG, "GAIN", false, () => Capable(b) ? Eq8.Db(B(b, Eq8.GainF)) + " dB" : "—", () => !Capable(b), bipolar: true),
                    K(pQ, "Q", true, () => NotaNum.F($"{B(b, Eq8.QF):0.00}")),
                    K(Eq8.OutputP, "OUTPUT", false, () => Eq8.Db(P(Eq8.OutputP)) + " dB", bipolar: true) } };
            }
            host.Content = Knobs(Sel());
            curve.SelectionChanged += () => { host.Content = Knobs(Sel()); RefreshAll(); };
            var body = new DockPanel { LastChildFill = true, Margin = new Thickness(6), Children = { chipStrip, Docked(host, Dock.Bottom), new Border { Margin = new Thickness(0, 4, 0, 0), Child = curve } } };
            return new Border { Background = NotaPalette.SurfaceCard, Child = new RuleSection { Margin = new Thickness(5), Child = body } };
        }

        curve.Edited += RefreshAll;
        Control root = CardLayout.IsMini(ctx, di) ? MiniBody() : LargeBody();

        void Refresh()
        {
            scN = engine.DeviceScope(track, di, scope, Eq8.kScope);
            curve.Update(scope, scN);
            for (int i = 0; i < readouts.Count; i++) readouts[i]();
            for (int i = 0; i < bandReadouts.Count; i++) bandReadouts[i]();
        }
        ctx.AddDeviceRefresher(Refresh);
        Refresh();
        return root;
    }
}
