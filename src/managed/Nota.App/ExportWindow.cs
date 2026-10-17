// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Export audio (mockup 1i). Range (the transport loop, the full song, or a typed
// Custom start / end),
// format (WAV / FLAC / MP3) / sample-rate / bit-depth-or-bitrate (M6-4 + M7
// codecs), release tail, normalize (−1 dBTP true peak) and dither (16-bit
// targets only), plus stem export. The option rows adapt to the format: FLAC
// offers 16/24-bit integer only, MP3 swaps bit depth for a CBR bitrate list and
// caps the sample rate at 48 kHz (the format's limit). The dialog returns
// ExportOptions; MainWindow runs the offline render.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed record ExportOptions(int SampleRate, WavBitDepth Depth, double TailBeats, double RangeBeats,
                                   bool Stems, bool Normalize, bool Dither,
                                   ExportFormat Format = ExportFormat.Wav, int BitrateKbps = 320,
                                   double StartBeats = 0);

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
    private static readonly string[] RateLabels = { "44 100", "48 000", "96 000" };
    private static readonly int[] Bitrates = { 320, 256, 192, 128 };   // MP3 CBR, best first
    private const int BeatsPerBar = 4;

    private readonly double _bpm;
    private double _startBeats, _rangeBeats;   // the range: where it starts and how long it is
    private int _rangeMode;                     // 0 loop · 1 full song · 2 custom
    private double _customStart, _customEnd;
    private bool _customValid = true;
    private TextBlock _rangeReadout = null!;
    private TextBlock _customError = null!;
    private Control _customRow = null!;
    private Border? _export;
    private TextBlock _estimate = null!;
    private ComboBox _format = null!;
    private ComboBox _rate = null!;
    private ComboBoxItem[] _rateItems = null!;
    private ComboBox _depth = null!;
    private ComboBox _quality = null!;
    private TextBlock _depthTitle = null!;
    private bool _tailOn = true;
    private bool _stems;
    private bool _normalize;
    private bool _dither;
    private Action? _refreshDitherEnabled;
    private TextBlock _exportLabel = null!;

    /// <summary>A loop region from the song start, <paramref name="loopBeats"/> long.</summary>
    public ExportWindow(double fullBeats, double loopBeats, double bpm) : this(fullBeats, 0, loopBeats, bpm) { }

    /// <param name="fullBeats">The song's end (last clip end).</param>
    /// <param name="loopStart">The transport loop region's start; <paramref name="loopEnd"/> its end.</param>
    public ExportWindow(double fullBeats, double loopStart, double loopEnd, double bpm)
    {
        _bpm = bpm;
        bool hasLoop = loopEnd > loopStart;
        // Custom starts from the loop region when there is one, else the whole song.
        (_customStart, _customEnd) = hasLoop ? (loopStart, loopEnd) : (0, fullBeats);
        void SetRange(int mode)
        {
            _rangeMode = mode;
            (_startBeats, _rangeBeats) = mode switch
            {
                1 => (0, fullBeats),
                2 => (_customStart, _customEnd - _customStart),
                _ => hasLoop ? (loopStart, loopEnd - loopStart) : (0, fullBeats),
            };
        }
        SetRange(0);
        Title = "Export audio";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Panel;
        // Escape is Cancel (an open drop-down handles its own Escape first).
        KeyDown += (_, e) => { if (e.Key == Key.Escape && !e.Handled) { e.Handled = true; Close(null); } };

        var body = new StackPanel { Margin = new Thickness(18, 16), Spacing = 14 };

        // Range. Custom shows a start and an end (bar.beat), checked as you type (#102).
        _rangeReadout = Mono("", TextSecondary);
        var range = Segmented(new[] { ("Loop", true), ("Full song", true), ("Custom", true) }, 0, i =>
        {
            SetRange(i);
            RefreshRange();
        });
        var rangeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center };
        rangeRow.Children.Add(range);
        rangeRow.Children.Add(_rangeReadout);
        var startBox = PositionBox(_customStart);
        var endBox = PositionBox(_customEnd);
        _customError = new TextBlock { FontSize = 10, Foreground = NotaPalette.Danger, VerticalAlignment = VerticalAlignment.Center };
        void ReadCustom()
        {
            var a = ParsePosition(startBox.Text);
            var b = ParsePosition(endBox.Text);
            _customValid = a is not null && b is not null && b > a;
            _customError.Text = a is null || b is null ? "Not a position: type bar.beat, like 5.1"
                              : b <= a ? "The end must be after the start" : "";
            if (_customValid) { _customStart = a!.Value; _customEnd = b!.Value; }
            SetRange(2);
            RefreshRange();
        }
        startBox.TextChanged += (_, _) => ReadCustom();
        endBox.TextChanged += (_, _) => ReadCustom();
        // Say what the numbers are: bar, beat and sixteenth, counted from 1 as the transport
        // counter shows them, and that the end is where the export stops (#102).
        var unitsHint = new TextBlock
        {
            Text = "Positions are bar.beat.sixteenth, counted from 1 as in the transport: 5 or 5.1 is the "
                 + "start of bar 5, 5.3 its third beat. The export stops at the end position, so 1 to 5 is four bars.",
            FontSize = 10, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, MaxWidth = 520,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        _customRow = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center,
                    Children = { Caption("Start"), startBox, Caption("End"), endBox, _customError },
                },
                unitsHint,
            },
        };
        body.Children.Add(Section("Range", new StackPanel { Spacing = 8, Children = { rangeRow, _customRow } }));

        // Format / SR / bit-depth-or-quality. The third column swaps its control
        // (and title) with the format: depth for WAV/FLAC, bitrate for MP3.
        _format = Combo(new[] { "WAV", "FLAC", "MP3" }, 0);
        _format.SelectionChanged += (_, _) => ApplyFormat();

        _rate = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _rateItems = new ComboBoxItem[Rates.Length];
        for (int i = 0; i < Rates.Length; i++)
        {
            _rateItems[i] = new ComboBoxItem { Content = RateLabels[i] };
            _rate.Items.Add(_rateItems[i]);
        }
        _rate.SelectedIndex = 1;   // 48 000
        _rate.SelectionChanged += (_, _) => RefreshEstimate();

        _depth = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        _quality = Combo(Array.ConvertAll(Bitrates, b => b.ToString()), 0);
        _quality.SelectionChanged += (_, _) => RefreshEstimate();

        var depthOrQuality = new StackPanel();
        depthOrQuality.Children.Add(_depth);
        depthOrQuality.Children.Add(_quality);
        var fmtGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*"), ColumnSpacing = 12 };
        var f0 = Section("Format", _format); fmtGrid.Children.Add(f0);
        var f1 = Section("Sample rate", _rate); Grid.SetColumn(f1, 1); fmtGrid.Children.Add(f1);
        var f2 = Section("Bit depth", depthOrQuality); Grid.SetColumn(f2, 2); fmtGrid.Children.Add(f2);
        _depthTitle = (TextBlock)((StackPanel)f2).Children[0];
        body.Children.Add(fmtGrid);
        _depth.SelectionChanged += (_, _) => { RefreshEstimate(); _refreshDitherEnabled?.Invoke(); };
        ApplyFormat();   // populates depth items + visibility for the default format

        // Options: normalize + dither (working toggles) and a release tail (checkbox).
        var normalize = WorkingToggle("Normalize −1 dBTP", () => _normalize, v => { _normalize = v; RefreshEstimate(); });
        var dither = WorkingToggle("Dither", () => _dither, v => { _dither = v; },
                                   enabled: () => SelectedFormat != ExportFormat.Mp3 && _depth.SelectedIndex == 0,
                                   hint: "16-bit only");
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 22, Children = { normalize, dither } });
        body.Children.Add(TailCheck());

        // Stems (one file per track, in the selected format).
        body.Children.Add(StemsBlock());

        // Footer.
        _estimate = new TextBlock { FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        // The dialog's primary action: set apart by weight and one tone step, not by colour
        // (orange is for what is live or chosen).
        var export = _export = new Border
        {
            Background = NotaPalette.SurfaceHover, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Control, Padding = new Thickness(18, 0), Height = 28,
            Cursor = new Cursor(StandardCursorType.Hand), VerticalAlignment = VerticalAlignment.Center,
            Child = (_exportLabel = new TextBlock { Text = "Export WAV", FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextHeading, VerticalAlignment = VerticalAlignment.Center }),
        };
        export.BindResource(Border.BoxShadowProperty, "Shadow.Raised");
        export.PointerPressed += (_, _) =>
        {
            if (!RangeValid) return;
            var fmt = SelectedFormat;
            int sr = Rates[_rate.SelectedIndex < 0 ? 1 : _rate.SelectedIndex];
            // MP3 has no bit-depth target; Pcm24 keeps Depth well-formed for the request.
            var d = fmt == ExportFormat.Mp3 ? WavBitDepth.Pcm24
                  : _depth.SelectedIndex switch { 0 => WavBitDepth.Pcm16, 2 => WavBitDepth.Float32, _ => WavBitDepth.Pcm24 };
            double tailBeats = _tailOn ? BeatsPerBar : 0.0;
            bool dither = _dither && d == WavBitDepth.Pcm16;   // only meaningful for 16-bit targets
            int kbps = Bitrates[_quality.SelectedIndex < 0 ? 0 : _quality.SelectedIndex];
            Close(new ExportOptions(sr, d, tailBeats, _rangeBeats, _stems, _normalize, dither, fmt, kbps, _startBeats));
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

    private ExportFormat SelectedFormat
        => _format.SelectedIndex switch { 1 => ExportFormat.Flac, 2 => ExportFormat.Mp3, _ => ExportFormat.Wav };

    // Re-applies the option matrix for the selected format (FR-002/FR-003): the
    // depth list, the third column's control + title, the 96 kHz gate for MP3,
    // the dither rule, and the dependent label/estimate.
    private void ApplyFormat()
    {
        if (_depth is null) return;   // during construction, before fields exist
        var fmt = SelectedFormat;

        _depth.Items.Clear();
        if (fmt == ExportFormat.Wav)
        {
            foreach (var it in new[] { "16-bit", "24-bit", "32-bit float" }) _depth.Items.Add(it);
            _depth.SelectedIndex = 1;
        }
        else
        {
            foreach (var it in new[] { "16-bit", "24-bit" }) _depth.Items.Add(it);
            _depth.SelectedIndex = 1;
        }

        bool mp3 = fmt == ExportFormat.Mp3;
        _depth.IsVisible = !mp3;
        _quality.IsVisible = mp3;
        _depthTitle.Text = mp3 ? "Quality" : "Bit depth";

        // MP3 tops out at 48 kHz (MPEG-1): grey 96 kHz out, and move an existing
        // 96 kHz selection down to 48 kHz so no invalid pair is reachable.
        _rateItems[2].IsEnabled = !mp3;
        if (mp3 && _rate.SelectedIndex == 2) _rate.SelectedIndex = 1;

        _refreshDitherEnabled?.Invoke();
        PaintExportLabel();
        RefreshEstimate();
    }

    private void PaintExportLabel()
    {
        if (_exportLabel is null) return;   // during construction, before the footer exists
        _exportLabel.Text = _stems ? "Export stems" : $"Export {SelectedFormat.ToString().ToUpperInvariant()}";
    }

    private bool RangeValid => _rangeMode != 2 || _customValid;

    private void RefreshRange()
    {
        _customRow.IsVisible = _rangeMode == 2;
        _rangeReadout.IsVisible = RangeValid;
        double sec = _bpm > 0 ? _rangeBeats * 60.0 / _bpm : 0;
        _rangeReadout.Text = string.Format(NotaNum.Culture, "{0} → {1} · {2:0.0} s",
                                           BarBeatText(_startBeats), BarBeatText(_startBeats + _rangeBeats), sec);
        if (_export is not null) Inactive.Set(_export, !RangeValid);
        RefreshEstimate();
    }

    // bar.beat.sixteenth, 1-based, as the readout shows a position.
    private static string BarBeatText(double beats)
    {
        int bar = (int)Math.Floor(beats / BeatsPerBar) + 1;
        double inBar = beats - (bar - 1) * BeatsPerBar;
        int beat = (int)Math.Floor(inBar) + 1;
        int six = (int)Math.Floor((inBar - (beat - 1)) * 4 + 1e-9) + 1;
        return string.Format(NotaNum.Culture, "{0}.{1}.{2}", bar, beat, six);
    }

    /// <summary>Reads a typed position — "5", "5.2" or "5.2.3" (bar, beat, sixteenth, all
    /// 1-based) — as beats from the song start; null when it isn't one.</summary>
    internal static double? ParsePosition(string? text)
    {
        var parts = (text ?? "").Trim().Split('.');
        if (parts.Length > 3) return null;
        var n = new[] { 1, 1, 1 };
        for (int i = 0; i < parts.Length; i++)
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out n[i]) || n[i] < 1) return null;
        if (n[1] > BeatsPerBar || n[2] > 4) return null;
        return (n[0] - 1) * BeatsPerBar + (n[1] - 1) + (n[2] - 1) / 4.0;
    }

    private TextBox PositionBox(double beats)
    {
        var box = new TextBox { Text = BarBeatText(beats), Width = 72, FontSize = 11, PlaceholderText = "bar.beat" };
        ToolTip.SetTip(box, "Bar.beat.sixteenth, counted from 1 — e.g. 5.1 or 5.3.2");
        box.BindResource(TextBox.FontFamilyProperty, "Font.Mono");
        return box;
    }

    private TextBlock Caption(string text)
        => new() { Text = text, FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };

    private void RefreshEstimate()
    {
        if (_estimate is null) return;
        double tail = _tailOn ? BeatsPerBar : 0;
        double sec = _bpm > 0 ? (Math.Max(0, _rangeBeats) + tail) * 60.0 / _bpm : 0;
        int sr = Rates[_rate.SelectedIndex < 0 ? 1 : _rate.SelectedIndex];
        int bytesPerSample = _depth.SelectedIndex switch { 0 => 2, 2 => 4, _ => 3 };
        double mb = SelectedFormat switch
        {
            // MP3 CBR is exact: kbps → 125 bytes/s. FLAC is level-5-typical
            // compression on full-range music — honest ≈, not a promise.
            ExportFormat.Mp3 => sec * Bitrates[_quality.SelectedIndex < 0 ? 0 : _quality.SelectedIndex] * 125 / 1_000_000.0,
            ExportFormat.Flac => 0.65 * sec * sr * 2 * bytesPerSample / 1_000_000.0,
            _ => sec * sr * 2 * bytesPerSample / 1_000_000.0,
        };
        _estimate.Text = _stems
            ? string.Format(NotaNum.Culture, "one file per track · {0:0.0} MB each · offline render", mb)
            : string.Format(NotaNum.Culture, "≈ 1 file · {0:0.0} MB · offline render", mb);
    }

    // ---- helpers ----------------------------------------------------------

    private Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 6,
        Children = { new TextBlock { Text = title, FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = TextTertiary }, content },
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
    // lets a toggle grey out and force off when it doesn't apply (dither vs format
    // and bit depth).
    private Control WorkingToggle(string label, Func<bool> get, Action<bool> set,
                                  Func<bool>? enabled = null, string? hint = null)
    {
        var knob = new Border { Width = 26, Height = 15, CornerRadius = NotaRadius.Body, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1) };
        var dot = new Border { Width = 11, Height = 11, CornerRadius = NotaRadius.Panel, Background = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
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
        void Sync()   // reflect enabled-state (dither only for 16-bit targets)
        {
            bool en = enabled?.Invoke() ?? true;
            if (!en && get()) { set(false); }
            Paint();
            Inactive.Set(row, !en);
        }
        row.PointerPressed += (_, _) => { set(!get()); Paint(); };
        if (enabled is not null) _refreshDitherEnabled += Sync;
        Sync();
        return row;
    }

    // The release-tail option: the shell switch (a themed CheckBox), word to the right.
    private Control TailCheck()
    {
        var sw = new CheckBox { Content = "Add 1-bar release tail", IsChecked = _tailOn, VerticalAlignment = VerticalAlignment.Center };
        sw.IsCheckedChanged += (_, _) => { _tailOn = sw.IsChecked == true; RefreshEstimate(); };
        return sw;
    }

    private Control StemsBlock()
    {
        // A working pill toggle: one file per track (in the export format) into a
        // chosen folder (M6-5).
        var knob = new Border { Width = 26, Height = 15, CornerRadius = NotaRadius.Body, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1) };
        var dot = new Border { Width = 11, Height = 11, CornerRadius = NotaRadius.Panel, Background = TextTertiary, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
        knob.Child = dot;
        void Paint()
        {
            knob.Background = _stems ? Brass : Raised;
            dot.Background = _stems ? OnAccent : TextTertiary;
            dot.HorizontalAlignment = _stems ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            dot.Margin = _stems ? new Thickness(0, 0, 2, 0) : new Thickness(2, 0, 0, 0);
            PaintExportLabel();
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
        var headerBar = new Border { Height = 28, Background = NotaPalette.SurfaceInset, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(10, 0), Cursor = new Cursor(StandardCursorType.Hand), Child = header };
        headerBar.PointerPressed += (_, _) => { _stems = !_stems; Paint(); };
        var box = new StackPanel { Children = { headerBar } };
        return new Border { BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, ClipToBounds = true, Child = box };
    }

    private Control Segmented((string label, bool enabled)[] items, int active, Action<int> onSelect)
    {
        var inner = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var cells = new Border[items.Length];
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            var t = new TextBlock { Text = items[i].label, FontSize = 10, VerticalAlignment = VerticalAlignment.Center };
            var cell = new Border { CornerRadius = NotaRadius.Badge, Padding = new Thickness(10, 2), Child = t };
            if (items[i].enabled) { cell.Cursor = new Cursor(StandardCursorType.Hand); cell.PointerPressed += (_, _) => { onSelect(idx); Paint(idx); }; }
            else t.Foreground = NotaPalette.TextDisabled;
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
        return new Border { Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Tile, Padding = new Thickness(2), Child = inner, VerticalAlignment = VerticalAlignment.Center };
    }
}
