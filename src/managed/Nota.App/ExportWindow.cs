// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Export audio (mockup 1i). Range (Loop / Full song are real; Custom is M7+),
// format / sample-rate / bit-depth (M6-4), release tail, normalize (−1 dBTP true
// peak) and dither (16-bit only), plus stem export. Only Custom range is still
// flagged. The dialog returns ExportOptions; MainWindow runs the offline render.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed record ExportOptions(int SampleRate, WavBitDepth Depth, double TailBeats, double RangeBeats, bool Stems, bool Normalize, bool Dither);

public sealed class ExportWindow : NotaWindow
{
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    private static readonly int[] Rates = { 44100, 48000, 96000 };
    private const int BeatsPerBar = 4;

    private readonly double _bpm;
    private double _rangeBeats;
    private TextBlock _rangeReadout = null!;
    private TextBlock _estimate = null!;
    private ComboBox _rate = null!;
    private ComboBox _depth = null!;
    private bool _tailOn = true;
    private bool _stems;
    private bool _normalize;
    private bool _dither;
    private Action? _refreshDitherEnabled;
    private TextBlock _exportLabel = null!;

    public ExportWindow(double fullBeats, double loopBeats, double bpm)
    {
        _bpm = bpm;
        _rangeBeats = loopBeats > 0 ? loopBeats : fullBeats;
        Title = "Export Audio";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Panel;

        var body = new StackPanel { Margin = new Thickness(18, 16), Spacing = 14 };

        // Range.
        _rangeReadout = Mono("", TextSecondary);
        var range = Segmented(new[] { ("Loop", true), ("Full song", true), ("Custom", false) }, 0, i =>
        {
            _rangeBeats = i == 1 ? fullBeats : loopBeats > 0 ? loopBeats : fullBeats;
            RefreshRange();
        });
        var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        rangeRow.Children.Add(range);
        var customNa = new NaBadge { Kind = NaBadgeKind.Future, VerticalAlignment = VerticalAlignment.Center };
        rangeRow.Children.Add(customNa);
        rangeRow.Children.Add(_rangeReadout);
        body.Children.Add(Section("RANGE", rangeRow));

        // Format / SR / bit-depth.
        var format = Combo(new[] { "WAV" }, 0);
        _rate = Combo(new[] { "44 100", "48 000", "96 000" }, 1);
        _depth = Combo(new[] { "16-bit", "24-bit", "32-bit float" }, 1);
        var fmtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        var f0 = Section("FORMAT", format); fmtGrid.Children.Add(f0);
        var f1 = Section("SAMPLE RATE", _rate); Grid.SetColumn(f1, 1); fmtGrid.Children.Add(f1);
        var f2 = Section("BIT DEPTH", _depth); Grid.SetColumn(f2, 2); fmtGrid.Children.Add(f2);
        body.Children.Add(fmtGrid);
        _rate.SelectionChanged += (_, _) => RefreshEstimate();
        _depth.SelectionChanged += (_, _) => { RefreshEstimate(); _refreshDitherEnabled?.Invoke(); };

        // Options: normalize + dither (working toggles) and a release tail (checkbox).
        var normalize = WorkingToggle("Normalize −1 dBTP", () => _normalize, v => { _normalize = v; RefreshEstimate(); });
        var dither = WorkingToggle("Dither", () => _dither, v => { _dither = v; },
                                   enabled: () => _depth.SelectedIndex == 0, hint: "16-bit only");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Children = { normalize, dither } });
        body.Children.Add(TailCheck());

        // Stems (N/A until M6-5).
        body.Children.Add(StemsBlock());

        // Footer.
        _estimate = new TextBlock { FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var export = new Border
        {
            Background = Brass, CornerRadius = new CornerRadius(5), Padding = new Thickness(18, 0), Height = 30,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = (_exportLabel = new TextBlock { Text = "Export WAV", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = OnAccent, VerticalAlignment = VerticalAlignment.Center }),
        };
        export.PointerPressed += (_, _) =>
        {
            int sr = Rates[_rate.SelectedIndex < 0 ? 1 : _rate.SelectedIndex];
            var d = _depth.SelectedIndex switch { 0 => WavBitDepth.Pcm16, 2 => WavBitDepth.Float32, _ => WavBitDepth.Pcm24 };
            double tailBeats = _tailOn ? BeatsPerBar : 0.0;
            bool dither = _dither && d == WavBitDepth.Pcm16;   // only meaningful for 16-bit
            Close(new ExportOptions(sr, d, tailBeats, _rangeBeats, _stems, _normalize, dither));
        };
        var cancel = new TextBlock { Text = "Cancel", FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand) };
        cancel.PointerPressed += (_, _) => Close(null);
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 0) };
        footer.Children.Add(export);
        Grid.SetColumn(_estimate, 1); _estimate.Margin = new Thickness(10, 0, 0, 0); footer.Children.Add(_estimate);
        Grid.SetColumn(cancel, 2); footer.Children.Add(cancel);
        body.Children.Add(footer);

        SetBody(body);
        RefreshRange();
    }

    private void RefreshRange()
    {
        double sec = _bpm > 0 ? _rangeBeats * 60.0 / _bpm : 0;
        int endBar = (int)(_rangeBeats / BeatsPerBar) + 1;
        _rangeReadout.Text = string.Format(CultureInfo.InvariantCulture, "1.1.1 → {0}.1.1 · {1:0.0} s", endBar, sec);
        RefreshEstimate();
    }

    private void RefreshEstimate()
    {
        if (_estimate is null) return;
        double tail = _tailOn ? BeatsPerBar : 0;
        double sec = _bpm > 0 ? (_rangeBeats + tail) * 60.0 / _bpm : 0;
        int sr = Rates[_rate.SelectedIndex < 0 ? 1 : _rate.SelectedIndex];
        int bytesPerSample = _depth.SelectedIndex switch { 0 => 2, 2 => 4, _ => 3 };
        double mb = sec * sr * 2 * bytesPerSample / 1_000_000.0;
        _estimate.Text = _stems
            ? string.Format(CultureInfo.InvariantCulture, "one file per track · {0:0.0} MB each · offline render", mb)
            : string.Format(CultureInfo.InvariantCulture, "≈ 1 file · {0:0.0} MB · offline render", mb);
    }

    // ---- helpers ----------------------------------------------------------

    private Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 6,
        Children = { new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = TextTertiary }, content },
    };

    private ComboBox Combo(string[] items, int selected)
    {
        var c = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var it in items) c.Items.Add(it);
        c.SelectedIndex = selected;   // after Items are populated
        return c;
    }

    private TextBlock Mono(string text, IBrush fg)
    {
        var t = new TextBlock { Text = text, FontSize = 10, Foreground = fg, VerticalAlignment = VerticalAlignment.Center };
        t.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return t;
    }

    // A working pill toggle (label + optional "16-bit only" style hint). `enabled`
    // lets a toggle grey out and force off when it doesn't apply (dither vs bit depth).
    private Control WorkingToggle(string label, Func<bool> get, Action<bool> set,
                                  Func<bool>? enabled = null, string? hint = null)
    {
        var knob = new Border { Width = 26, Height = 15, CornerRadius = new CornerRadius(8), Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1) };
        var dot = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Background = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        knob.Child = dot;
        var text = new TextBlock { Text = label, FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Children = { knob, text } };
        if (hint is not null)
            row.Children.Add(new TextBlock { Text = "· " + hint, FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center });

        void Paint()
        {
            bool on = get();
            knob.Background = on ? Brass : Raised;
            dot.Background = on ? OnAccent : TextTertiary;
            dot.HorizontalAlignment = on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            dot.Margin = on ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
            text.Foreground = on ? TextPrimary : TextSecondary;
        }
        void Sync()   // reflect enabled-state (dither only when 16-bit)
        {
            bool en = enabled?.Invoke() ?? true;
            if (!en && get()) { set(false); }
            row.Opacity = en ? 1.0 : 0.45;
            row.IsHitTestVisible = en;
            Paint();
        }
        row.PointerPressed += (_, _) => { set(!get()); Paint(); };
        if (enabled is not null) _refreshDitherEnabled += Sync;
        Sync();
        return row;
    }

    // Design-system checkbox (Radius.Sm box, brass fill + dark check when on). Small,
    // with a secondary-weight label — used for the release-tail option.
    private Control TailCheck()
    {
        var box = new Border { Width = 16, Height = 16, CornerRadius = new CornerRadius(5), BorderThickness = new Thickness(1) };
        var check = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse("M0 3.5 L3.2 6.7 L9 0"),
            Stroke = OnAccent, StrokeThickness = 1.6,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round,
        };
        // A Viewbox fits the glyph's bounds into a fixed centred area, so the tick can't
        // drift with the geometry's origin (a bare Path centres by its own bounds box).
        var glyph = new Viewbox
        {
            Width = 9, Height = 7, Stretch = Stretch.Uniform, Child = check,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        box.Child = glyph;
        var text = new TextBlock { Text = "Add 1-bar release tail", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        void Paint()
        {
            box.Background = _tailOn ? Brass : Sunken;
            box.BorderBrush = _tailOn ? Brass : BorderStrong;
            check.IsVisible = _tailOn;
            text.Foreground = _tailOn ? TextPrimary : TextSecondary;
        }
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Cursor = new Cursor(StandardCursorType.Hand), Children = { box, text } };
        row.PointerPressed += (_, _) => { _tailOn = !_tailOn; Paint(); RefreshEstimate(); };
        Paint();
        return row;
    }

    private Control StemsBlock()
    {
        // A working pill toggle: one WAV per track into a chosen folder (M6-5).
        var knob = new Border { Width = 26, Height = 15, CornerRadius = new CornerRadius(8), Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1) };
        var dot = new Border { Width = 11, Height = 11, CornerRadius = new CornerRadius(6), Background = TextTertiary, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        knob.Child = dot;
        void Paint()
        {
            knob.Background = _stems ? Brass : Raised;
            dot.Background = _stems ? OnAccent : TextTertiary;
            dot.HorizontalAlignment = _stems ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            dot.Margin = _stems ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
            _exportLabel.Text = _stems ? "Export stems" : "Export WAV";
            RefreshEstimate();
        }
        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                knob,
                new TextBlock { Text = "Export stems", FontSize = 11, FontWeight = FontWeight.Medium, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center },
                new TextBlock { Text = "· one file per track, post-fader", FontSize = 9, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center },
            },
        };
        var headerBar = new Border { Height = 28, Background = new SolidColorBrush(Color.Parse("#1B1916")), BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = header };
        headerBar.PointerPressed += (_, _) => { _stems = !_stems; Paint(); };
        var box = new StackPanel { Children = { headerBar } };
        return new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7), ClipToBounds = true, Child = box };
    }

    private Control Segmented((string label, bool enabled)[] items, int active, Action<int> onSelect)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var cells = new Border[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var t = new TextBlock { Text = items[i].label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { CornerRadius = new CornerRadius(3), Padding = new Thickness(10, 2), Child = t };
            if (items[i].enabled) { cell.Cursor = new Cursor(StandardCursorType.Hand); cell.PointerPressed += (_, _) => { onSelect(idx); Paint(idx); }; }
            else cell.Opacity = 0.5;
            cells[i] = cell;
            inner.Children.Add(cell);
        }
        void Paint(int a)
        {
            for (int i = 0; i < cells.Length; i++)
            {
                bool on = i == a;
                cells[i].Background = on ? Brass : Brushes.Transparent;
                ((TextBlock)cells[i].Child!).Foreground = on ? OnAccent : TextSecondary;
                ((TextBlock)cells[i].Child!).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
            }
        }
        Paint(active);
        return new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5), Padding = new Thickness(2), Child = inner, VerticalAlignment = VerticalAlignment.Center };
    }
}
