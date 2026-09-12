// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Clip for AUDIO clips: a waveform view with a live playback cursor
// beside a 200px props rail (name / start / length / gain / pitch / file info,
// plus a Warp placeholder for a later milestone). Mirrors ClipEditorView's
// layout so the Clip tab looks the same for MIDI and audio.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class AudioClipEditorView : UserControl
{
    // Ember Graphite palette (matches the MIDI editor's props rail).
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    private readonly IAudioEngine _engine;
    private readonly Action _onChanged;
    public int TrackId { get; }
    public int ClipIndex { get; }

    private readonly WaveformView _wave = new();
    private readonly TextBlock _lengthText;
    private readonly TextBlock _pitchText;
    private readonly TextBlock _gainText;
    private readonly TextBlock _fileText;
    private readonly MiniFader _gain;

    private double _startBeat;
    private double _lengthBeats = 4;
    private int _pitch;
    private bool _reversed;
    private readonly TextBlock _reverseToggleText;
    private bool _warpEnabled;
    private int _warpMode = 3;   // Complex
    private bool _snapToGrid = true;                  // trim brackets snap to whole beats (default on)
    private readonly TextBlock _snapToggleText;
    private readonly TextBlock _warpToggleText;
    private readonly TextBlock _warpModeText;
    private readonly TextBlock _autoText;
    private readonly TextBlock _transientText;
    private readonly TextBlock _detectedText;
    private double _detectedBpm;
    private readonly TextBlock _envToggleText;
    private readonly TextBlock _envTargetText;
    private bool _envMode;
    private int _envTarget;   // 0 = Volume (0..1), 1 = Pan (-1..1)
    private static readonly string[] WarpModes = { "Beats", "Tones", "Texture", "Complex", "Complex Pro", "Re-Pitch" };

    public AudioClipEditorView(IAudioEngine engine, int trackId, int clipIndex, IBrush clipColor, Action onChanged)
    {
        _engine = engine;
        TrackId = trackId;
        ClipIndex = clipIndex;
        _onChanged = onChanged;
        _wave.SetColor(clipColor);
        _wave.MarkersCommitted = (src, beat) =>
        {
            _engine.SetClipWarpMarkers(TrackId, ClipIndex, src, beat);
            Reload();
            _onChanged();
        };
        _wave.SourceRegionCommitted = (off, len) =>
        {
            _engine.SetClipSourceRegion(TrackId, ClipIndex, off, len);
            Reload();
            _onChanged();
        };
        _wave.WarpTrimCommitted = (ps, pe) =>
        {
            _engine.SetClipWarpTrim(TrackId, ClipIndex, ps, pe);
            Reload();
            _onChanged();
        };
        _wave.EnvelopeCommitted = (beats, values, curves) =>
        {
            var pts = new AutomationPoint[beats.Length];
            for (int i = 0; i < beats.Length; i++) pts[i] = new AutomationPoint(beats[i], (float)values[i], curves[i]);
            if (_envTarget == 1) _engine.SetClipPanEnvelope(TrackId, ClipIndex, pts);
            else _engine.SetClipVolumeEnvelope(TrackId, ClipIndex, pts);
            _onChanged();
        };
        _envToggleText = new TextBlock { Text = "Edit envelope: Off", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _envTargetText = new TextBlock { Text = "Volume ▾", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

        _lengthText = Mono("—");
        _pitchText = Mono("0 st");
        _gainText = Mono("0.0 dB"); _gainText.Foreground = TextSecondary;
        _fileText = new TextBlock { Text = "—", FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        _gain = new MiniFader(1.0, 2.0) { VerticalAlignment = VerticalAlignment.Center };
        _gain.ValueChanged += v => { _engine.SetClipGain(TrackId, ClipIndex, (float)v); ShowGain(v); _wave.SetGain(v); _onChanged(); };
        _reverseToggleText = new TextBlock { Text = "Reverse: Off", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _snapToggleText = new TextBlock { Text = "Grid snap: On", FontSize = 10, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _warpToggleText = new TextBlock { Text = "Warp: Off", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _warpModeText = new TextBlock { Text = "Complex ▾", FontSize = 10, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _autoText = new TextBlock { Text = "Auto-warp to tempo", FontSize = 10, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _transientText = new TextBlock { Text = "Warp to transients", FontSize = 10, Foreground = AccentBright, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _detectedText = new TextBlock { Text = "Detected: —", FontSize = 9, Foreground = TextTertiary };

        var body = new StackPanel { Spacing = 10, Margin = new Thickness(10) };
        body.Children.Add(Section("CLIP", ReadoutBox($"Audio {trackId}", TextPrimary, 24, 11, false)));

        var startLen = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), ColumnSpacing = 6 };
        startLen.Children.Add(Labeled("START", ReadoutBox(Position(0), TextPrimary, 22, 10, true, out var startBox)));
        _startBox = startBox;
        var lenBox = FieldBox(22); lenBox.Child = _lengthText;
        var lenWrap = Labeled("LENGTH", lenBox);
        Grid.SetColumn(lenWrap, 1); startLen.Children.Add(lenWrap);
        body.Children.Add(startLen);

        // Length steppers (trim the clip end, ± one beat).
        var lenMinus = StepBtn("−", () => Resize(-1));
        var lenPlus = StepBtn("+", () => Resize(1));
        var lenStep = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        lenStep.Children.Add(lenMinus);
        var lenHint = new TextBlock { Text = "length ± 1 beat", FontSize = 9, Foreground = TextTertiary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(lenHint, 1); lenStep.Children.Add(lenHint);
        Grid.SetColumn(lenPlus, 2); lenStep.Children.Add(lenPlus);
        body.Children.Add(Section("DURATION", lenStep));

        // Gain fader + dB readout.
        var gainRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        gainRow.Children.Add(_gain);
        Grid.SetColumn(_gainText, 1); _gainText.VerticalAlignment = VerticalAlignment.Center; _gainText.Margin = new Thickness(6, 0, 0, 0);
        gainRow.Children.Add(_gainText);
        body.Children.Add(Section("GAIN", gainRow));

        // Pitch (varispeed transpose) stepper.
        var pMinus = StepBtn("−", () => Pitch(-1));
        var pPlus = StepBtn("+", () => Pitch(1));
        var pStep = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 6 };
        pStep.Children.Add(pMinus);
        var pBox = FieldBox(22); pBox.Child = _pitchText;
        Grid.SetColumn(pBox, 1); pStep.Children.Add(pBox);
        Grid.SetColumn(pPlus, 2); pStep.Children.Add(pPlus);
        body.Children.Add(Section("PITCH (semitones)", pStep));

        // Reverse: play the clip's region back-to-front. Non-destructive (the file is
        // untouched) and free to toggle, so it doubles as an audition switch.
        var revChip = Chip(_reverseToggleText);
        revChip.PointerPressed += (_, e) => { e.Handled = true; ToggleReverse(); };
        var revHint = new TextBlock
        {
            Text = "Plays the clip backwards · the waveform below mirrors with it, so it always reads left to right in playing order",
            FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap,
        };
        body.Children.Add(Section("DIRECTION", new StackPanel { Spacing = 4, Children = { revChip, revHint } }));

        // Warp: on/off + mode (time-stretch to project tempo, Signalsmith Stretch).
        var warpToggle = Chip(_warpToggleText);
        warpToggle.PointerPressed += (_, e) => { e.Handled = true; ToggleWarp(); };
        var warpModeChip = Chip(_warpModeText);
        warpModeChip.PointerPressed += (_, e) => { e.Handled = true; CycleWarpMode(); };
        var warpRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        warpToggle.MinWidth = 74;
        warpRow.Children.Add(warpToggle);
        Grid.SetColumn(warpModeChip, 1); warpRow.Children.Add(warpModeChip);
        var autoChip = Chip(_autoText);
        autoChip.PointerPressed += (_, e) => { e.Handled = true; AutoWarp(); };
        var transientChip = Chip(_transientText);
        transientChip.PointerPressed += (_, e) => { e.Handled = true; TransientWarp(); };
        var warpHint = new TextBlock { Text = "Auto detects tempo · Transients locks drum hits to the grid (Beats) · double-click waveform to add markers · drag to align", FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(Section("WARP", new StackPanel { Spacing = 4, Children = { warpRow, autoChip, transientChip, _detectedText, warpHint } }));

        // Clip envelopes: toggle edit mode on the waveform + pick Volume / Pan target.
        var envToggle = Chip(_envToggleText);
        envToggle.PointerPressed += (_, e) => { e.Handled = true; ToggleEnvMode(); };
        var envTargetChip = Chip(_envTargetText);
        envTargetChip.PointerPressed += (_, e) => { e.Handled = true; CycleEnvTarget(); };
        var envRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 6 };
        envToggle.MinWidth = 96;
        envRow.Children.Add(envToggle);
        Grid.SetColumn(envTargetChip, 1); envRow.Children.Add(envTargetChip);
        var envHint = new TextBlock { Text = "Draw a curve over the clip · click to add · drag to move · right-click to delete", FontSize = 9, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        body.Children.Add(Section("CLIP ENVELOPES", new StackPanel { Spacing = 4, Children = { envRow, envHint } }));

        body.Children.Add(Section("FILE", _fileText));

        var props = new Border
        {
            Width = 200, Background = Panel, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new ScrollViewer
            {
                Content = body,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            },
        };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(props);

        // Column 1: the waveform (row 0), a horizontal zoom scrollbar (row 1), and a
        // floating overlay (Canvas with null background → clicks pass through except the
        // inline BPM editor).
        _bpmBox = new TextBox
        {
            IsVisible = false, Width = 56, Height = 20, FontSize = 10, Padding = new Thickness(4, 0),
            Background = Sunken, Foreground = TextPrimary, BorderBrush = Brass, BorderThickness = new Thickness(1),
            VerticalContentAlignment = VerticalAlignment.Center,
        };
        _bpmBox.KeyDown += OnBpmKey;
        _bpmBox.LostFocus += (_, _) => CommitBpm();
        _overlay = new Canvas();
        _overlay.Children.Add(_bpmBox);
        _wave.BpmEditRequested = ShowBpmEditor;

        _hScroll = new ScrollBar { Orientation = Orientation.Horizontal, Height = 10, IsVisible = false, Minimum = 0 };
        _hScroll.Scroll += (_, _) => _wave.SetScroll(_hScroll.Value);
        _wave.ViewChanged = SyncWaveScroll;

        var waveHost = new Grid();
        waveHost.Children.Add(_wave);
        waveHost.Children.Add(_overlay);

        // Toolbar above the waveform: a grid-snap toggle for the trim brackets (on by
        // default). When on, dragging a blue bracket snaps its edge to the beat grid so
        // the clip trims exactly on the bar/beat.
        var snapChip = Chip(_snapToggleText); snapChip.MinWidth = 96;
        snapChip.PointerPressed += (_, e) => { e.Handled = true; ToggleSnap(); };
        var toolbar = new Border
        {
            Height = 26, Background = Panel, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(6, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { snapChip } },
        };

        var col1 = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(toolbar, 0); col1.Children.Add(toolbar);
        Grid.SetRow(waveHost, 1); col1.Children.Add(waveHost);
        Grid.SetRow(_hScroll, 2); col1.Children.Add(_hScroll);
        _wave.SetSnapToGrid(_snapToGrid);
        var waveWrap = new Border { Background = Sunken, Margin = new Thickness(0), Child = col1 };
        Grid.SetColumn(waveWrap, 1);
        grid.Children.Add(waveWrap);

        var header = Header();
        DockPanel.SetDock(header, Dock.Top);
        var dock = new DockPanel();
        dock.Children.Add(header);
        dock.Children.Add(grid);
        Content = dock;

        Reload();
    }

    private Border _startBox = null!;
    private readonly TextBox _bpmBox;
    private readonly Canvas _overlay;
    private readonly ScrollBar _hScroll;
    private int _bpmSeg = -1;

    // Inline BPM editor over the warp bar: position + focus a small field.
    private void ShowBpmEditor(int seg, double bpm, double xCenter, double yTop)
    {
        _bpmSeg = seg;
        _bpmBox.Text = bpm.ToString("0.##", CultureInfo.InvariantCulture);
        Canvas.SetLeft(_bpmBox, Math.Max(0, xCenter - _bpmBox.Width / 2));
        Canvas.SetTop(_bpmBox, Math.Max(0, yTop - 2));
        _bpmBox.IsVisible = true;
        _bpmBox.Focus();
        _bpmBox.SelectAll();
    }

    private void OnBpmKey(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter) { CommitBpm(); e.Handled = true; }
        else if (e.Key == Avalonia.Input.Key.Escape) { HideBpm(); e.Handled = true; }
    }

    private void CommitBpm()
    {
        if (!_bpmBox.IsVisible) return;
        int seg = _bpmSeg;
        HideBpm();
        if (seg >= 0 && double.TryParse(_bpmBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) && v > 0)
            _wave.SetSegmentBpm(seg, v);   // → MarkersCommitted → engine + Reload
    }

    private void HideBpm() { _bpmBox.IsVisible = false; _bpmSeg = -1; }

    // Mirror the waveform's zoom/scroll into the scrollbar (shown only when zoomed in).
    private void SyncWaveScroll()
    {
        bool zoomed = _wave.Zoom > 1.001;
        _hScroll.IsVisible = zoomed;
        _hScroll.Maximum = _wave.MaxScrollFrac;
        _hScroll.ViewportSize = 1.0 / _wave.Zoom;
        _hScroll.Value = Math.Clamp(_wave.ScrollFrac, 0, _wave.MaxScrollFrac);
    }

    // Re-reads clip geometry + peaks + sample metadata and refreshes the rail.
    public void Reload()
    {
        if (_engine.TryGetClipInfo(TrackId, ClipIndex, out var ci))
        {
            _startBeat = ci.StartBeat;
            _lengthBeats = ci.LengthBeats > 0 ? ci.LengthBeats : _lengthBeats;
            if (_startBox.Child is TextBlock sb) sb.Text = Position(_startBeat);
            _lengthText.Text = Duration(_lengthBeats);
        }
        if (_engine.TryGetAudioClipInfo(TrackId, ClipIndex, out var ai))
        {
            _pitch = (int)Math.Round(ai.PitchSemitones);
            _pitchText.Text = $"{_pitch} st";
            _gain.Value = ai.Gain;
            ShowGain(ai.Gain);
            _wave.SetGain(ai.Gain);
            _reversed = ai.Reversed != 0;
            _wave.SetReversed(_reversed);   // draw the waveform the way the clip now plays
            _reverseToggleText.Text = _reversed ? "Reverse: On" : "Reverse: Off";
            _reverseToggleText.Foreground = _reversed ? AccentBright : TextSecondary;
            _warpEnabled = ai.WarpEnabled != 0;
            _warpMode = Math.Clamp(ai.WarpMode, 0, WarpModes.Length - 1);
            _warpToggleText.Text = _warpEnabled ? "Warp: On" : "Warp: Off";
            _warpToggleText.Foreground = _warpEnabled ? AccentBright : TextSecondary;
            _warpModeText.Text = WarpModes[_warpMode] + " ▾";
            if (ai.SampleId != 0 && _engine.TryGetSampleInfo(ai.SampleId, out var si) && si.SampleRate > 0)
            {
                double secs = si.Frames / si.SampleRate;
                string ch = si.Channels == 1 ? "Mono" : si.Channels == 2 ? "Stereo" : $"{si.Channels}ch";
                _fileText.Text = string.Format(CultureInfo.InvariantCulture,
                    "{0} · {1:0.0} kHz · {2:0.00} s", ch, si.SampleRate / 1000.0, secs);
            }
            else _fileText.Text = "—";
        }

        // Sample geometry (for the BPM readouts and the Start/End brackets).
        double srcSR = 0; long srcFrames = 0; bool haveSample = false;
        _engine.TryGetAudioClipInfo(TrackId, ClipIndex, out var gi);
        if (gi.SampleId != 0 && _engine.TryGetSampleInfo(gi.SampleId, out var gs) && gs.Frames > 0)
        { haveSample = true; srcSR = gs.SampleRate; srcFrames = gs.Frames; }
        _wave.SetSourceRate(srcSR);

        // Warp markers (only meaningful while warping). Unwarped clips instead show the
        // whole sample with draggable Start/End brackets (source-region editing).
        var peaks = new float[1024 * 2];
        if (_warpEnabled)
        {
            // Show the FULL warp with markers + Start/End trim brackets (trim the played
            // window without stretching).
            int n = _engine.GetClipWarpFullPeaks(TrackId, ClipIndex, peaks, 1024);
            _wave.SetPeaks(peaks, n);
            var ms = new double[128]; var mb = new double[128];
            int mn = Math.Min(_engine.GetClipWarpMarkers(TrackId, ClipIndex, ms, mb), 128);
            _wave.SetMarkers(ms, mb, mn);
            _wave.SetSourceRegion(0, 0, 0);   // source-frame brackets off (warped)
            _wave.SetWarpTrim(gi.WarpPlayStart, gi.WarpPlayEnd, gi.WarpBeats);
        }
        else
        {
            _wave.SetWarpTrim(0, 0, 0);       // warp trim brackets off (unwarped)
            int n = _engine.GetClipSourcePeaks(TrackId, ClipIndex, peaks, 1024);
            _wave.SetPeaks(peaks, n);
            _wave.SetMarkers(Array.Empty<double>(), Array.Empty<double>(), 0);
            if (haveSample)
            {
                double len = gi.LengthFrames > 0 ? gi.LengthFrames : srcFrames - gi.SourceOffsetFrames;
                _wave.SetSourceRegion(gi.SourceOffsetFrames, len, srcFrames);
            }
            else _wave.SetSourceRegion(0, 0, 0);
        }

        // Clip envelope: clip-local domain = warpBeats when warped, else clip length.
        LoadEnvelope();
        SyncWaveScroll();
    }

    // Called from the UI tick: show a playback cursor when the transport is inside
    // this clip, else hide it.
    public void OnPlayhead(double beats, bool playing)
    {
        double frac = (playing && _lengthBeats > 0 && beats >= _startBeat && beats <= _startBeat + _lengthBeats)
            ? (beats - _startBeat) / _lengthBeats : -1;
        _wave.SetPlayhead(frac);
    }

    private void Resize(int deltaBeats)
    {
        double len = Math.Max(0.25, _lengthBeats + deltaBeats);
        // Warped clips own a musical target length; unwarped clips trim the source.
        if (_warpEnabled) _engine.SetClipWarpLength(TrackId, ClipIndex, len);
        else _engine.TrimClip(TrackId, ClipIndex, _startBeat, len);
        Reload();
        _onChanged();
    }

    private void ToggleSnap()
    {
        _snapToGrid = !_snapToGrid;
        _snapToggleText.Text = _snapToGrid ? "Grid snap: On" : "Grid snap: Off";
        _snapToggleText.Foreground = _snapToGrid ? AccentBright : TextSecondary;
        _wave.SetSnapToGrid(_snapToGrid);
    }

    private void ToggleReverse()
    {
        _engine.SetClipReverse(TrackId, ClipIndex, !_reversed);
        Reload();          // picks the new state up (and re-reads the played peaks)
        _onChanged();
    }

    private void ToggleWarp()
    {
        _warpEnabled = !_warpEnabled;
        _engine.SetClipWarp(TrackId, ClipIndex, _warpEnabled, _warpMode);
        Reload();
        _onChanged();
    }

    private void CycleWarpMode()
    {
        _warpMode = (_warpMode + 1) % WarpModes.Length;
        _engine.SetClipWarp(TrackId, ClipIndex, _warpEnabled, _warpMode);
        Reload();
        _onChanged();
    }

    // Detect the file's tempo and snap the clip's length to the beat grid so it
    // conforms to the project BPM (enables warp). Detection may fail on flat/short
    // material, in which case the clip is left as-is.
    private void AutoWarp()
    {
        double bpm = _engine.AutoWarpClip(TrackId, ClipIndex);
        _detectedBpm = bpm;
        ShowDetected();
        Reload();
        _onChanged();
    }

    // Beats-mode warp: detect transients and pin grid-snapped markers at each hit so
    // percussion locks tightly to the grid ("Beats"). Fails on flat material.
    private void TransientWarp()
    {
        double bpm = _engine.BeatWarpClip(TrackId, ClipIndex);
        _detectedBpm = bpm;
        ShowDetected();
        Reload();
        _onChanged();
    }

    private void ShowDetected()
        => _detectedText.Text = _detectedBpm > 0
            ? string.Format(CultureInfo.InvariantCulture, "Detected: {0:0.0} BPM", _detectedBpm)
            : "Detected: —";

    private void ToggleEnvMode()
    {
        _envMode = !_envMode;
        _envToggleText.Text = _envMode ? "Edit envelope: On" : "Edit envelope: Off";
        _envToggleText.Foreground = _envMode ? AccentBright : TextSecondary;
        _wave.SetEnvMode(_envMode);
    }

    private void CycleEnvTarget()
    {
        _envTarget = _envTarget == 0 ? 1 : 0;
        _envTargetText.Text = (_envTarget == 1 ? "Pan" : "Volume") + " ▾";
        LoadEnvelope();
    }

    // Loads the active target's envelope into the waveform with its value axis.
    private void LoadEnvelope()
    {
        var env = _envTarget == 1
            ? _engine.GetClipPanEnvelope(TrackId, ClipIndex)
            : _engine.GetClipVolumeEnvelope(TrackId, ClipIndex);
        var eb = new double[env.Length]; var ev = new double[env.Length]; var ec = new float[env.Length];
        for (int i = 0; i < env.Length; i++) { eb[i] = env[i].Beat; ev[i] = env[i].Value; ec[i] = env[i].Curve; }
        double min = _envTarget == 1 ? -1 : 0, max = 1;
        _wave.SetEnvelope(eb, ev, ec, Math.Max(1e-6, _lengthBeats), min, max);
    }

    private void Pitch(int d)
    {
        _pitch = Math.Clamp(_pitch + d, -48, 48);
        _engine.SetClipPitch(TrackId, ClipIndex, _pitch);
        Reload();          // pitch changes the clip's beat-length (varispeed)
        _onChanged();
    }

    private void ShowGain(double v) => _gainText.Text = v <= 0.0011 ? "-∞" : $"{AudioMath.LinToDb(v):+0.0;-0.0} dB";

    // --- helpers (mirror ClipPropsView) -----------------------------------

    private Control Header()
    {
        var tab = new Border
        {
            Height = 22, Background = AccentSubtle, BorderBrush = Brass, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = "Audio", FontSize = 11, Foreground = AccentBright, VerticalAlignment = VerticalAlignment.Center },
        };
        return new Border
        {
            Height = 30, Background = Panel, BorderBrush = BorderDef, BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(10, 0),
            Child = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, Children = { tab } },
        };
    }

    private static TextBlock Mono(string text)
    {
        var t = new TextBlock { Text = text, FontSize = 10, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(7, 0, 0, 0) };
        t.BindResource(FontFamilyProperty, "Font.Mono");
        return t;
    }

    private static Control Section(string title, Control content) => new StackPanel
    {
        Spacing = 4,
        Children = { new TextBlock { Text = title, FontSize = 9, FontWeight = FontWeight.Bold, Foreground = TextTertiary }, content },
    };

    private static Control Labeled(string title, Control content) => new StackPanel
    {
        Spacing = 2, Children = { new TextBlock { Text = title, FontSize = 9, Foreground = TextTertiary }, content },
    };

    private static Border FieldBox(double h) => new()
    {
        Height = h, Background = Sunken, BorderBrush = BorderDef, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
    };

    private static Border ReadoutBox(string text, IBrush fg, double h, double fs, bool mono)
        => ReadoutBox(text, fg, h, fs, mono, out _);

    private static Border ReadoutBox(string text, IBrush fg, double h, double fs, bool mono, out Border box)
    {
        var t = new TextBlock { Text = text, FontSize = fs, Foreground = fg, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(mono ? 7 : 8, 0, 0, 0) };
        if (mono) t.BindResource(FontFamilyProperty, "Font.Mono");
        box = FieldBox(h);
        box.Child = t;
        return box;
    }

    private static Border Chip(Control child) => new()
    {
        Height = 22, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand), Child = child,
    };

    private static Border StepBtn(string glyph, Action onClick)
    {
        var b = new Border
        {
            Width = 22, Height = 22, Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(5), Cursor = new Cursor(StandardCursorType.Hand),
            Child = new TextBlock { Text = glyph, FontSize = 11, Foreground = TextSecondary, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        b.PointerPressed += (_, e) => { e.Handled = true; onClick(); };
        return b;
    }

    private static string Position(double beat)
    {
        int bar = (int)(beat / 4) + 1, be = (int)(beat % 4) + 1, six = (int)Math.Round(beat % 1 * 4) + 1;
        return string.Format(CultureInfo.InvariantCulture, "{0}. {1}. {2}", bar, be, six);
    }

    private static string Duration(double beats)
    {
        int bars = (int)(beats / 4), be = (int)(beats % 4), six = (int)Math.Round(beats % 1 * 4);
        return string.Format(CultureInfo.InvariantCulture, "{0}. {1}. {2}", bars, be, six);
    }

    // ---- waveform + playback cursor + warp markers -----------------------
    // The waveform is drawn in SOURCE time; markers tie a source point to a beat.
    // Interior markers drag along source (aligning audio to their fixed beat);
    // double-click adds one (beat interpolated) / removes an interior one.
    private sealed class WaveformView : Control
    {
        private static readonly IBrush Bg = NotaPalette.BgSunken;
        private static readonly IBrush Mid = NotaPalette.Wash(NotaPalette.TextSecondary, 0x40);
        private static readonly IBrush PlayheadCursor = NotaPalette.AccentBright;
        private static readonly IBrush Marker = NotaPalette.Marker;
        private static readonly IBrush GridBeat = NotaPalette.Wash(NotaPalette.BorderStrong, 0x50);
        private static readonly IBrush GridBar = NotaPalette.Wash(NotaPalette.BorderStrong, 0xC0);
        private static readonly IBrush GridLabel = NotaPalette.TextTertiary;
        private static readonly Typeface GridFace = new(FontFamily.Default);
        // BPM chip on the warp bar (drag to scrub the segment tempo).
        private static readonly IBrush BpmChipBg = NotaPalette.Wash(NotaPalette.Ink("#12181B"), 0xDE);
        private static readonly IBrush BpmChipBgHot = NotaPalette.Wash(NotaPalette.Ink("#1A2B31"), 0xF2);
        private static readonly IBrush BpmSuffix = NotaPalette.Wash(NotaPalette.Marker, 0x99);
        private const int BeatsPerBar = 4;   // matches the props-rail bar.beat readouts
        private IBrush _wave = new SolidColorBrush(NotaPalette.TrackColors[1]);
        private float[]? _peaks;
        private int _count;
        private double _frac = -1;
        private double _gain = 1.0;
        private readonly System.Collections.Generic.List<(double src, double beat)> _mk = new();
        private int _drag = -1;
        private int _hoverMk = -1;          // marker under the cursor (hover highlight)
        private double _srcSR;              // source sample rate (for per-marker BPM)
        private const double HandlePx = 7;

        // Source-region brackets (unwarped clips): whole sample drawn, Start/End handles
        // pick the playable region [_srcOff, _srcOff+_srcLen] within _srcTotal frames.
        private static readonly IBrush Bracket = NotaPalette.Marker;
        private static readonly IBrush BracketHot = NotaPalette.MarkerHot;
        private static readonly IBrush OutsideDim = NotaPalette.Wash(NotaPalette.BgSunken, 0xAA);
        private double _srcOff, _srcLen, _srcTotal;
        private bool _srcMode;
        private int _srcDrag = -1;          // 0 = start bracket, 1 = end bracket, -1 = none

        // Warped-clip trim brackets: played beat window [_trimStart, _trimEnd] over the
        // full warp [0, _trimTotal(=warpBeats)]. Trims without stretching.
        private double _trimStart, _trimEnd, _trimTotal;
        private bool _trimMode;
        private int _trimDrag = -1;         // 0 = start, 1 = end
        private bool _snapGrid = true;      // trim brackets snap to whole beats (toggle above the wave)
        private const double MinSrcFrames = 256;
        private static readonly Cursor ArrowCur = new(StandardCursorType.Arrow);
        private static readonly Cursor ResizeCur = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor ScrubCur = new(StandardCursorType.SizeNorthSouth);
        private StandardCursorType _curKind = StandardCursorType.Arrow;

        // BPM chip drag-to-scrub (like the global BPM DragNumber): hold on a segment's
        // BPM chip and drag up/down; the warp re-times live (committed on release).
        private int _bpmScrubSeg = -1, _bpmHoverSeg = -1;
        private double _bpmScrubStartY, _bpmScrubStartBpm;

        // Volume envelope (M9 follow-up): points (beat, value 0..1) in clip-local beats.
        private static readonly IBrush EnvLine = NotaPalette.AccentBright;
        private static readonly IBrush EnvLineDim = NotaPalette.Wash(NotaPalette.AccentBright, 0x60);
        private sealed class EnvPt { public double beat; public double val; public float curve; }
        private readonly System.Collections.Generic.List<EnvPt> _env = new();
        private double _clipBeats = 1;
        private double _envMin, _envMax = 1;   // value axis (Volume 0..1, Pan -1..1)
        private bool _envMode;
        private int _envDrag = -1;
        private EnvPt? _envBend;               // segment being bent (M9-D)

        /// <summary>Raised when markers change (drag/add/delete) with the full sorted list.</summary>
        public Action<double[], double[]>? MarkersCommitted;
        /// <summary>Raised when the envelope changes, with the full sorted (beats, values, curves).</summary>
        public Action<double[], double[], float[]>? EnvelopeCommitted;
        /// <summary>Raised when a Start/End bracket drag ends, with the new (offsetFrames, lengthFrames).</summary>
        public Action<double, long>? SourceRegionCommitted;
        /// <summary>Raised when a warped-clip trim bracket drag ends, with the new (playStart, playEnd) beats.</summary>
        public Action<double, double>? WarpTrimCommitted;

        // Warped-clip trim window (beats over the full warp). warpBeats &lt;= 0 disables.
        public void SetWarpTrim(double playStart, double playEnd, double warpBeats)
        {
            _trimTotal = warpBeats;
            _trimStart = playStart;
            _trimEnd = playEnd > 0 ? Math.Min(playEnd, warpBeats) : warpBeats;
            _trimMode = warpBeats > 0;
            InvalidateVisual();
        }
        /// <summary>Raised on a click on a segment BPM label: (segmentIndex, currentBpm, xCenterPx, yTopPx).</summary>
        public Action<int, double, double, double>? BpmEditRequested;
        // Clickable BPM-label rects, rebuilt each Render (segment index + shown bpm).
        private readonly System.Collections.Generic.List<(Rect rect, int seg, double bpm)> _bpmHot = new();

        // Re-time a warp segment to a BPM: hold the source span, set the beat span from
        // the tempo, and shift all later markers by the delta (keeps them monotonic).
        // commit=false updates the markers locally + redraws (live scrub); commit=true
        // pushes them to the engine (one undo step). Recomputed from an absolute BPM each
        // call, so repeated live calls don't drift.
        public void ApplySegmentBpm(int seg, double bpm, bool commit)
        {
            if (seg < 0 || seg + 1 >= _mk.Count || _srcSR <= 0 || !(bpm > 0)) return;
            double srcSpan = _mk[seg + 1].src - _mk[seg].src;
            if (srcSpan <= 0) return;
            double newBeatSpan = Math.Max(0.05, bpm * srcSpan / (60.0 * _srcSR));
            double delta = newBeatSpan - (_mk[seg + 1].beat - _mk[seg].beat);
            for (int j = seg + 1; j < _mk.Count; j++) _mk[j] = (_mk[j].src, _mk[j].beat + delta);
            if (commit) Commit(); else InvalidateVisual();
        }
        public void SetSegmentBpm(int seg, double bpm) => ApplySegmentBpm(seg, bpm, commit: true);

        public void SetSnapToGrid(bool on) { _snapGrid = on; }
        public void SetSourceRate(double sr) { _srcSR = sr; }
        // Whole-sample source-region view (unwarped). totalFrames &lt;= 0 disables the brackets.
        public void SetSourceRegion(double offsetFrames, double lengthFrames, double totalFrames)
        {
            _srcTotal = totalFrames; _srcOff = offsetFrames; _srcLen = lengthFrames;
            _srcMode = totalFrames > 0;
            InvalidateVisual();
        }

        // Horizontal zoom/scroll over a normalised clip axis (frac 0..1 spans the whole
        // clip). All three domains (source frames / warp beats / envelope beats) map to
        // this same axis, so one zoom+scroll drives everything. _zoom=1 is fit-to-width.
        private double _zoom = 1.0;
        private double _scrollFrac;
        private const double MaxZoom = 60.0;
        /// <summary>Raised when zoom/scroll changes so the host can resync its scrollbar.</summary>
        public Action? ViewChanged;
        public double Zoom => _zoom;
        public double ScrollFrac => _scrollFrac;
        public double MaxScrollFrac => Math.Max(0, 1.0 - 1.0 / _zoom);
        public void SetScroll(double frac) { _scrollFrac = Math.Clamp(frac, 0, MaxScrollFrac); InvalidateVisual(); }

        // Reverse: the clip is drawn MIRRORED, so the screen always reads left-to-right in
        // playing order — a reversed clip's audible head (the region's end) sits on the left.
        // Everything in the source/warp domain (peaks, brackets, markers, BPM chips, grid)
        // goes through Frac<->X below, so mirroring here flips the whole view coherently and
        // every hit-test/drag keeps working in content coordinates. The zoom/scroll window
        // lives in VIEW space, hence the separate …View pair.
        private bool _reversed;
        public void SetReversed(bool on) { if (_reversed == on) return; _reversed = on; InvalidateVisual(); }
        private double Mirror(double f) => _reversed ? 1.0 - f : f;   // involution: content <-> view
        private double FracToXView(double vf, double w) => (vf - _scrollFrac) * _zoom * w;
        private double XToFracView(double x, double w) => x / Math.Max(1, _zoom * w) + _scrollFrac;
        private double FracToX(double f, double w) => FracToXView(Mirror(f), w);
        private double XToFrac(double x, double w) => Mirror(XToFracView(x, w));

        private double SrcToX(double frame, double w) => _srcTotal > 0 ? FracToX(frame / _srcTotal, w) : 0;
        private double XToSrc(double x, double w) => Math.Clamp(XToFrac(x, w), 0, 1) * _srcTotal;

        public void SetEnvMode(bool on) { _envMode = on; InvalidateVisual(); }
        public void SetEnvelope(double[] beats, double[] values, float[] curves, double clipBeats, double min, double max)
        {
            _clipBeats = Math.Max(1e-6, clipBeats);
            _envMin = min; _envMax = max;
            _env.Clear();
            for (int i = 0; i < beats.Length; i++) _env.Add(new EnvPt { beat = beats[i], val = values[i], curve = curves[i] });
            InvalidateVisual();
        }
        // The clip envelope is authored in PLAYED time (the renderer evaluates it against the
        // timeline position), so it always runs left-to-right — it must NOT mirror with the
        // waveform. It stays on the view axis directly.
        private double EnvBeatToX(double beat, double w) => FracToXView(beat / _clipBeats, w);
        private double EnvXToBeat(double x, double w) => Math.Clamp(XToFracView(x, w), 0, 1) * _clipBeats;
        private double EnvValToY(double v, double h) { double pad = 4; double t = (Math.Clamp(v, _envMin, _envMax) - _envMin) / (_envMax - _envMin); return pad + (1 - t) * (h - 2 * pad); }
        private double EnvYToVal(double y, double h) { double pad = 4; double t = (h - pad - y) / Math.Max(1, h - 2 * pad); return _envMin + Math.Clamp(t, 0, 1) * (_envMax - _envMin); }
        private static double EnvShape(double t, float curve) => curve == 0f ? t : Math.Pow(t, Math.Pow(2.0, -curve * 4.0));
        private int HitEnv(double x, double y, double w, double h)
        {
            for (int i = 0; i < _env.Count; i++)
                if (Math.Abs(EnvBeatToX(_env[i].beat, w) - x) <= HandlePx && Math.Abs(EnvValToY(_env[i].val, h) - y) <= HandlePx)
                    return i;
            return -1;
        }
        private System.Collections.Generic.List<EnvPt> EnvSorted()
        {
            var ord = new System.Collections.Generic.List<EnvPt>(_env);
            ord.Sort((a, b) => a.beat.CompareTo(b.beat));
            return ord;
        }
        // Left point of the segment whose (curved) line is under (x,y), or null.
        private EnvPt? HitEnvSegment(double x, double y, double w, double h)
        {
            var ord = EnvSorted();
            double beat = EnvXToBeat(x, w);
            for (int k = 1; k < ord.Count; k++)
            {
                var a = ord[k - 1]; var b = ord[k];
                if (beat < a.beat || beat > b.beat) continue;
                double span = b.beat - a.beat;
                if (span <= 0) return null;
                double vy = a.val + (b.val - a.val) * EnvShape((beat - a.beat) / span, a.curve);
                return Math.Abs(EnvValToY(vy, h) - y) <= 6 ? a : null;
            }
            return null;
        }
        private void CommitEnv()
        {
            _env.Sort((a, b) => a.beat.CompareTo(b.beat));
            var beats = new double[_env.Count]; var vals = new double[_env.Count]; var curves = new float[_env.Count];
            for (int i = 0; i < _env.Count; i++) { beats[i] = _env[i].beat; vals[i] = _env[i].val; curves[i] = _env[i].curve; }
            EnvelopeCommitted?.Invoke(beats, vals, curves);
        }

        public void SetColor(IBrush b) { _wave = b; InvalidateVisual(); }
        public void SetPeaks(float[] peaks, int count) { _peaks = peaks; _count = count; InvalidateVisual(); }
        public void SetPlayhead(double frac)
        {
            if (Math.Abs(frac - _frac) < 1e-4) return;
            _frac = frac; InvalidateVisual();
        }
        public void SetMarkers(double[] src, double[] beat, int count)
        {
            _mk.Clear();
            for (int i = 0; i < count; i++) _mk.Add((src[i], beat[i]));
            InvalidateVisual();
        }
        public void SetGain(double g) { if (Math.Abs(g - _gain) < 1e-4) return; _gain = g; InvalidateVisual(); }

        // Markers map in the BEAT domain, matching the warped waveform drawn across
        // the full clip length. Dragging changes a marker's beat (its source stays).
        private double TotalBeats => _mk.Count > 1 ? Math.Max(1e-6, _mk[^1].beat) : 1;
        private double BeatToX(double beat, double w) => FracToX(beat / TotalBeats, w);
        private double XToBeat(double x, double w) => Math.Clamp(XToFrac(x, w), 0, 1) * TotalBeats;
        // Snap a beat to the nearest whole-beat grid line (the 1.1, 1.2 … grid).
        private static double SnapBeat(double beat) => Math.Round(beat);

        private int HitMarker(double x, double w)
        {
            int best = -1; double bd = HandlePx;
            for (int i = 0; i < _mk.Count; i++) { double d = Math.Abs(BeatToX(_mk[i].beat, w) - x); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            double W = Bounds.Width, H = Bounds.Height;

            // BPM chip: drag up/down to scrub the segment tempo (like the global BPM),
            // double-click to type an exact value.
            if (!_envMode && !e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                var pp = e.GetPosition(this);
                foreach (var b in _bpmHot)
                    if (b.rect.Contains(pp))
                    {
                        if (e.ClickCount == 2)
                            BpmEditRequested?.Invoke(b.seg, b.bpm, (b.rect.Left + b.rect.Right) / 2, b.rect.Top);
                        else
                        {
                            _bpmScrubSeg = b.seg; _bpmScrubStartY = pp.Y; _bpmScrubStartBpm = b.bpm;
                            e.Pointer.Capture(this);
                        }
                        e.Handled = true;
                        return;
                    }
            }

            // Envelope edit mode takes over the pointer (warp markers stay put).
            if (_envMode)
            {
                var p = e.GetPosition(this);
                bool right = e.GetCurrentPoint(this).Properties.IsRightButtonPressed;
                int hit = HitEnv(p.X, p.Y, W, H);
                if (hit >= 0)
                {
                    if (right || e.ClickCount == 2) { _env.RemoveAt(hit); CommitEnv(); InvalidateVisual(); }
                    else { _envDrag = hit; e.Pointer.Capture(this); }
                    e.Handled = true;
                    return;
                }
                if (HitEnvSegment(p.X, p.Y, W, H) is { } seg)   // bend / reset a segment (M9-D)
                {
                    if (right || e.ClickCount == 2) { seg.curve = 0f; CommitEnv(); InvalidateVisual(); }
                    else { _envBend = seg; e.Pointer.Capture(this); }
                    e.Handled = true;
                    return;
                }
                if (right) return;
                var np = new EnvPt { beat = EnvXToBeat(p.X, W), val = EnvYToVal(p.Y, H) };   // add + grab
                _env.Add(np);
                _envDrag = _env.IndexOf(np);
                e.Pointer.Capture(this);
                e.Handled = true;
                InvalidateVisual();
                return;
            }

            // Source-region brackets (unwarped whole-sample view): grab Start / End.
            if (_srcMode)
            {
                double xw = Bounds.Width, xp = e.GetPosition(this).X;
                double xs = SrcToX(_srcOff, xw), xe = SrcToX(_srcOff + _srcLen, xw);
                // Prefer whichever handle is nearer when both are within reach.
                bool nearStart = Math.Abs(xp - xs) <= HandlePx, nearEnd = Math.Abs(xp - xe) <= HandlePx;
                if (nearStart && (!nearEnd || Math.Abs(xp - xs) <= Math.Abs(xp - xe))) _srcDrag = 0;
                else if (nearEnd) _srcDrag = 1;
                if (_srcDrag >= 0) { e.Pointer.Capture(this); e.Handled = true; }
                return;
            }

            // Warped-clip trim brackets take priority over markers at the window edges.
            if (_trimMode)
            {
                double xw = Bounds.Width, xp = e.GetPosition(this).X;
                double xs = BeatToX(_trimStart, xw), xe = BeatToX(_trimEnd, xw);
                bool nearStart = Math.Abs(xp - xs) <= HandlePx, nearEnd = Math.Abs(xp - xe) <= HandlePx;
                if (nearStart && (!nearEnd || Math.Abs(xp - xs) <= Math.Abs(xp - xe))) _trimDrag = 0;
                else if (nearEnd) _trimDrag = 1;
                if (_trimDrag >= 0) { e.Pointer.Capture(this); e.Handled = true; return; }
            }

            if (_mk.Count < 2) return;
            double w = Bounds.Width, x = e.GetPosition(this).X;
            if (e.ClickCount == 2)
            {
                int hit = HitMarker(x, w);
                if (hit > 0 && hit < _mk.Count - 1) { _mk.RemoveAt(hit); Commit(); }   // delete interior
                else AddMarker(SnapBeat(XToBeat(x, w)));                                // add on the grid
                e.Handled = true;
                return;
            }
            int h = HitMarker(x, w);
            if (h > 0 && h < _mk.Count - 1) { _drag = h; e.Pointer.Capture(this); e.Handled = true; }
        }

        // Wheel scrolls the clip horizontally; ⌘/Ctrl+wheel zooms toward the cursor.
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            double w = Bounds.Width;
            var mods = e.KeyModifiers;
            if ((mods & (KeyModifiers.Control | KeyModifiers.Meta)) != 0)
            {
                double anchor = XToFracView(e.GetPosition(this).X, w);      // keep the point under the cursor fixed (view space)
                double old = _zoom;
                _zoom = Math.Clamp(_zoom * WheelInput.ZoomFactor(e.Delta.Y, 1.2), 1.0, MaxZoom);
                if (Math.Abs(_zoom - old) < 1e-9) { e.Handled = true; return; }
                _scrollFrac = Math.Clamp(anchor - (anchor - _scrollFrac) * (old / _zoom), 0, MaxScrollFrac);
            }
            else
            {
                // Device-normalized so a trackpad flick doesn't jump to the ends; the wave is
                // horizontal, so honour a horizontal swipe too.
                double d = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
                _scrollFrac = Math.Clamp(_scrollFrac - WheelInput.Notches(d) * 0.12 / _zoom, 0, MaxScrollFrac);
            }
            ViewChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_bpmScrubSeg >= 0)   // drag up = faster (0.5 BPM/px, like the global BPM)
            {
                double dy = _bpmScrubStartY - e.GetPosition(this).Y;
                double bpm = Math.Clamp(_bpmScrubStartBpm + dy * 0.5, 20, 999);
                ApplySegmentBpm(_bpmScrubSeg, bpm, commit: false);   // live warp + redraw, no engine round-trip
                return;
            }
            if (_envDrag >= 0)
            {
                var p = e.GetPosition(this);
                _env[_envDrag].beat = EnvXToBeat(p.X, Bounds.Width);
                _env[_envDrag].val = EnvYToVal(p.Y, Bounds.Height);
                InvalidateVisual();
                return;
            }
            if (_envBend is { } bl)
            {
                var p = e.GetPosition(this);
                var ord = EnvSorted();
                int k = ord.IndexOf(bl);
                if (k >= 0 && k + 1 < ord.Count)
                {
                    double v0 = bl.val, v1 = ord[k + 1].val;
                    if (Math.Abs(v1 - v0) < 1e-4) bl.curve = 0f;
                    else
                    {
                        double frac = Math.Clamp((EnvYToVal(p.Y, Bounds.Height) - v0) / (v1 - v0), 0.02, 0.98);
                        double ee = Math.Log(frac) / Math.Log(0.5);
                        bl.curve = (float)Math.Clamp(-Math.Log2(ee) / 4.0, -1.0, 1.0);
                    }
                    InvalidateVisual();
                }
                return;
            }
            if (_srcDrag >= 0)
            {
                double sw = Bounds.Width;
                double f = XToSrc(e.GetPosition(this).X, sw);
                if (_srcDrag == 0) _srcOff = Math.Clamp(f, 0, _srcOff + _srcLen - MinSrcFrames);
                else { double end = Math.Clamp(f, _srcOff + MinSrcFrames, _srcTotal); _srcLen = end - _srcOff; }
                InvalidateVisual();
                return;
            }
            if (_trimDrag >= 0)   // warped trim brackets (beat domain)
            {
                const double minBeat = 0.05;
                double bt = XToBeat(e.GetPosition(this).X, Bounds.Width);
                if (_snapGrid) bt = SnapBeat(bt);   // land the edge exactly on the beat grid
                if (_trimDrag == 0) _trimStart = Math.Clamp(bt, 0, _trimEnd - minBeat);
                else _trimEnd = Math.Clamp(bt, _trimStart + minBeat, _trimTotal);
                InvalidateVisual();
                return;
            }
            if (_drag < 0)   // no active drag: track hover for marker/bracket highlight + cursor
            {
                UpdateHover(e.GetPosition(this), Bounds.Width);
                return;
            }
            double w = Bounds.Width;
            double b = SnapBeat(XToBeat(e.GetPosition(this).X, w));   // align to the beat grid
            double lo = _mk[_drag - 1].beat + 1e-3, hi = _mk[_drag + 1].beat - 1e-3;  // stay between neighbours
            _mk[_drag] = (_mk[_drag].src, Math.Clamp(b, lo, hi));   // source fixed; beat moves (warp)
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_bpmScrubSeg >= 0) { _bpmScrubSeg = -1; e.Pointer.Capture(null); Commit(); return; }   // push the scrubbed warp (one undo step)
            if (_envDrag >= 0 || _envBend is not null) { _envDrag = -1; _envBend = null; e.Pointer.Capture(null); CommitEnv(); return; }
            if (_srcDrag >= 0)
            {
                _srcDrag = -1; e.Pointer.Capture(null);
                SourceRegionCommitted?.Invoke(_srcOff, (long)Math.Round(_srcLen));
                return;
            }
            if (_trimDrag >= 0)
            {
                _trimDrag = -1; e.Pointer.Capture(null);
                WarpTrimCommitted?.Invoke(_trimStart, _trimEnd);
                return;
            }
            if (_drag < 0) return;
            _drag = -1; e.Pointer.Capture(null); Commit();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hoverMk != -1 || _bpmHoverSeg != -1) { _hoverMk = -1; _bpmHoverSeg = -1; InvalidateVisual(); }
            SetCursorKind(StandardCursorType.Arrow);
            base.OnPointerExited(e);
        }

        private void SetCursorKind(StandardCursorType kind)
        {
            if (kind == _curKind) return;
            _curKind = kind;
            Cursor = kind switch
            {
                StandardCursorType.SizeWestEast => ResizeCur,
                StandardCursorType.SizeNorthSouth => ScrubCur,
                _ => ArrowCur,
            };
        }

        // Update hover state (BPM chip / marker / bracket under the cursor) + cursor.
        private void UpdateHover(Point p, double w)
        {
            double x = p.X;
            int bpmHover = -1;
            foreach (var b in _bpmHot) if (b.rect.Contains(p)) { bpmHover = b.seg; break; }
            if (bpmHover != _bpmHoverSeg) { _bpmHoverSeg = bpmHover; InvalidateVisual(); }
            if (bpmHover >= 0) { SetCursorKind(StandardCursorType.SizeNorthSouth); return; }

            bool overHandle = false;
            if (_srcMode)
            {
                double xs = SrcToX(_srcOff, w), xe = SrcToX(_srcOff + _srcLen, w);
                overHandle = Math.Abs(x - xs) <= HandlePx || Math.Abs(x - xe) <= HandlePx;
            }
            else
            {
                if (_trimMode)   // warped trim brackets (edges of the played window)
                {
                    double xs = BeatToX(_trimStart, w), xe = BeatToX(_trimEnd, w);
                    overHandle = Math.Abs(x - xs) <= HandlePx || Math.Abs(x - xe) <= HandlePx;
                }
                if (!overHandle && _mk.Count >= 2)
                {
                    int hv = HitMarker(x, w);
                    if (hv != _hoverMk) { _hoverMk = hv; InvalidateVisual(); }
                    overHandle = hv > 0 && hv < _mk.Count - 1;   // only interior markers drag
                }
                else if (overHandle && _hoverMk != -1) { _hoverMk = -1; InvalidateVisual(); }
            }
            SetCursorKind(overHandle ? StandardCursorType.SizeWestEast : StandardCursorType.Arrow);
        }

        private void AddMarker(double beat)
        {
            for (int i = 0; i + 1 < _mk.Count; i++)
            {
                if (beat <= _mk[i].beat || beat >= _mk[i + 1].beat) continue;
                double t = (beat - _mk[i].beat) / (_mk[i + 1].beat - _mk[i].beat);
                double src = _mk[i].src + (_mk[i + 1].src - _mk[i].src) * t;   // interpolate source → no warp jump
                _mk.Insert(i + 1, (src, beat));
                Commit();
                return;
            }
        }

        private void Commit()
        {
            var src = new double[_mk.Count]; var beat = new double[_mk.Count];
            for (int i = 0; i < _mk.Count; i++) { src[i] = _mk[i].src; beat[i] = _mk[i].beat; }
            MarkersCommitted?.Invoke(src, beat);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            double mid = h / 2;
            ctx.DrawLine(new Pen(Mid, 1), new Point(0, mid), new Point(w, mid));
            _bpmHot.Clear();

            // Peaks span the whole clip (frac 0..1); route through the zoom/scroll axis so
            // zooming magnifies the waveform and only the visible slice is drawn.
            if (_peaks is not null && _count > 0 && h > 4)
            {
                double amp = h / 2 - 3;
                double bw = Math.Max(1, _zoom * w / _count);
                for (int i = 0; i < _count; i++)
                {
                    // FracToX marks the bucket's leading edge — its RIGHT edge once mirrored.
                    double fx = FracToX((double)i / _count, w);
                    if (_reversed) fx -= bw;
                    if (fx + bw < 0) { if (_reversed) break; continue; }
                    if (fx > w) { if (_reversed) continue; break; }
                    float mn = (float)Math.Clamp(_peaks[i * 2] * _gain, -1.0, 1.0);
                    float mx = (float)Math.Clamp(_peaks[i * 2 + 1] * _gain, -1.0, 1.0);
                    ctx.FillRectangle(_wave, new Rect(fx, mid - mx * amp, bw, Math.Max(1, (mx - mn) * amp)));
                }
            }

            // Beat grid (only meaningful while warping): bar lines are brighter/
            // thicker, interior beats faint. Labels read "bar.beat" (1.1, 1.2 …)
            // and are skipped when beats are too close to fit legibly.
            if (_mk.Count > 1)
            {
                double tb = TotalBeats;
                double beatW = _zoom * w / tb;   // pixel width of one beat (zoom-aware)
                bool label = beatW >= 20;
                int last = (int)Math.Floor(tb + 1e-6);
                for (int b = 0; b <= last; b++)
                {
                    double x = BeatToX(b, w);
                    if (x < -0.5) continue;
                    if (x > w + 0.5) break;
                    bool bar = b % BeatsPerBar == 0;
                    ctx.DrawLine(new Pen(bar ? GridBar : GridBeat, bar ? 1 : 0.6), new Point(x, 0), new Point(x, h));
                    if (label && b < last)
                    {
                        int barNo = b / BeatsPerBar + 1, beatNo = b % BeatsPerBar + 1;
                        var ft = new FormattedText($"{barNo}.{beatNo}", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, GridFace, 8.5, GridLabel);
                        // Keep the label inside its own beat: that's the other side when mirrored.
                        ctx.DrawText(ft, new Point(_reversed ? x - 2 - ft.Width : x + 2, h - ft.Height - 2));
                    }
                }
            }

            // Warp markers: accent line + triangle handle for all. End markers are
            // clamped just inside the edges (so they're visible) and aren't draggable.
            // The hovered / dragged marker lights up (brighter + thicker + larger grip).
            for (int i = 0; i < _mk.Count; i++)
            {
                bool end = i == 0 || i == _mk.Count - 1;
                bool hot = i == _hoverMk || i == _drag;
                var brush = hot ? BracketHot : Marker;
                double xr = BeatToX(_mk[i].beat, w);
                if (xr < -6 || xr > w + 6) continue;   // scrolled off-screen
                double x = Math.Clamp(xr, 2, Math.Max(2, w - 2));
                ctx.DrawLine(new Pen(brush, hot ? 2.4 : (end ? 1 : 1.6)), new Point(x, 0), new Point(x, h));
                double hw = hot ? 5.5 : 4;
                var g = new StreamGeometry();
                using (var gc = g.Open()) { gc.BeginFigure(new Point(x - hw, 0), true); gc.LineTo(new Point(x + hw, 0)); gc.LineTo(new Point(x, hot ? 11 : 8)); gc.EndFigure(true); }
                ctx.DrawGeometry(brush, null, g);
            }

            // Per-segment local BPM (the warp bar): a rounded chip showing the tempo
            // the audio plays at between consecutive markers. Drag it up/down to scrub.
            // Centred over each segment, hidden when the segment is too narrow.
            if (_srcSR > 0)
                for (int i = 0; i + 1 < _mk.Count; i++)
                {
                    double beatSpan = _mk[i + 1].beat - _mk[i].beat;
                    double srcSpan = _mk[i + 1].src - _mk[i].src;
                    if (beatSpan <= 0 || srcSpan <= 0) continue;
                    double x0 = BeatToX(_mk[i].beat, w), x1 = BeatToX(_mk[i + 1].beat, w);
                    if (Math.Abs(x1 - x0) < 46) continue;   // mirrored: x1 sits left of x0
                    double bpm = beatSpan * _srcSR * 60.0 / srcSpan;
                    bool hotChip = i == _bpmHoverSeg || i == _bpmScrubSeg;
                    var txtBrush = hotChip ? BracketHot : Marker;
                    var num = new FormattedText($"{bpm:0.0}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GridFace, 10, txtBrush);
                    var suf = new FormattedText("BPM", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, GridFace, 6.5, BpmSuffix);
                    double padX = 6, padY = 2.5, gap = 3;
                    double cw = num.Width + gap + suf.Width + 2 * padX, chH = num.Height + 2 * padY;
                    double cx = Math.Clamp((x0 + x1) / 2, cw / 2 + 2, w - cw / 2 - 2);
                    if (cx < 0 || cx > w) continue;
                    var chip = new Rect(cx - cw / 2, 5, cw, chH);
                    ctx.DrawRectangle(hotChip ? BpmChipBgHot : BpmChipBg, new Pen(txtBrush, 1), chip, 4, 4);
                    ctx.DrawText(num, new Point(chip.X + padX, chip.Y + padY));
                    ctx.DrawText(suf, new Point(chip.X + padX + num.Width + gap, chip.Y + chH - suf.Height - padY + 0.5));
                    _bpmHot.Add((chip, i, bpm));
                }

            // Clip envelope: brass polyline + points, brighter in edit mode. A centre
            // reference line is drawn for a bipolar (pan) axis.
            if (_envMode && _envMin < 0)
            {
                double cy = EnvValToY(0, h);
                ctx.DrawLine(new Pen(GridBeat, 1), new Point(0, cy), new Point(w, cy));
            }
            if (_env.Count > 0)
            {
                var pen = new Pen(_envMode ? EnvLine : EnvLineDim, _envMode ? 1.6 : 1);
                var ord = EnvSorted();
                double fy = EnvValToY(ord[0].val, h);
                ctx.DrawLine(pen, new Point(0, fy), new Point(EnvBeatToX(ord[0].beat, w), fy));   // hold before first
                for (int i = 1; i < ord.Count; i++)
                {
                    var a = ord[i - 1]; var b = ord[i];
                    double ax = EnvBeatToX(a.beat, w), ay = EnvValToY(a.val, h), bx = EnvBeatToX(b.beat, w), by = EnvValToY(b.val, h);
                    if (a.curve == 0f) { ctx.DrawLine(pen, new Point(ax, ay), new Point(bx, by)); continue; }
                    Point prev = new(ax, ay);
                    for (int s = 1; s <= 16; s++)
                    {
                        double tt = s / 16.0;
                        double vy = a.val + (b.val - a.val) * EnvShape(tt, a.curve);
                        Point cur = new(ax + (bx - ax) * tt, EnvValToY(vy, h));
                        ctx.DrawLine(pen, prev, cur); prev = cur;
                    }
                }
                double ly = EnvValToY(ord[^1].val, h);
                ctx.DrawLine(pen, new Point(EnvBeatToX(ord[^1].beat, w), ly), new Point(w, ly));  // hold after last
                if (_envMode)
                    foreach (var p in ord)
                        ctx.DrawEllipse(EnvLine, null, new Point(EnvBeatToX(p.beat, w), EnvValToY(p.val, h)), 3, 3);
            }

            // Source-region Start/End brackets (unwarped whole-sample view): dim the
            // trimmed-off audio and draw a grabbable bracket at each edge of the region.
            if (_srcMode && _srcTotal > 0)
            {
                double xs = SrcToX(_srcOff, w), xe = SrcToX(_srcOff + _srcLen, w);
                DimOutside(ctx, xs, xe, w, h);
                // The grips point INTO the region, which is the other way round when mirrored.
                if (xs >= -1 && xs <= w + 1) DrawBracket(ctx, xs, h, !_reversed, _srcDrag == 0);
                if (xe >= -1 && xe <= w + 1) DrawBracket(ctx, xe, h, _reversed, _srcDrag == 1);
            }

            // Warped-clip trim brackets (beat domain over the full warp): dim the trimmed
            // beats and draw a grabbable bracket at each edge of the played window.
            if (_trimMode && _trimTotal > 0)
            {
                double xs = BeatToX(_trimStart, w), xe = BeatToX(_trimEnd, w);
                DimOutside(ctx, xs, xe, w, h);
                if (xs >= -1 && xs <= w + 1) DrawBracket(ctx, xs, h, !_reversed, _trimDrag == 0);
                if (xe >= -1 && xe <= w + 1) DrawBracket(ctx, xe, h, _reversed, _trimDrag == 1);
            }

            if (_frac >= 0 && _frac <= 1)
            {
                // _frac is relative to the played window; map it into the current domain.
                // _frac runs over the PLAYED window, so on a reversed clip it walks the region
                // from its end back to its start (which the mirror then draws left-to-right).
                double pf = _reversed ? 1 - _frac : _frac;
                double x = (_srcMode && _srcTotal > 0) ? SrcToX(_srcOff + pf * _srcLen, w)
                         : (_trimMode && _trimTotal > 0) ? BeatToX(_trimStart + pf * (_trimEnd - _trimStart), w)
                         : FracToX(pf, w);
                ctx.DrawLine(new Pen(PlayheadCursor, 1.5), new Point(x, 0), new Point(x, h));
            }
        }

        // Shade everything outside the region's two edges. They arrive in content order, which
        // is right-to-left on a mirrored (reversed) view, so normalise before filling.
        private void DimOutside(DrawingContext ctx, double xa, double xb, double w, double h)
        {
            double lo = Math.Clamp(Math.Min(xa, xb), 0, w), hi = Math.Clamp(Math.Max(xa, xb), 0, w);
            if (lo > 0.5) ctx.FillRectangle(OutsideDim, new Rect(0, 0, lo, h));
            if (hi < w - 0.5) ctx.FillRectangle(OutsideDim, new Rect(hi, 0, w - hi, h));
        }

        // A trim bracket: vertical line + grips at top and bottom pointing into the region.
        // Which screen side that is depends on the draw order, so the caller passes it —
        // a mirrored (reversed) view has the region's start on the right.
        private void DrawBracket(DrawingContext ctx, double x, double h, bool gripRight, bool hot)
        {
            var brush = hot ? BracketHot : Bracket;
            ctx.DrawLine(new Pen(brush, hot ? 2.4 : 1.6), new Point(x, 0), new Point(x, h));
            double dir = gripRight ? 1 : -1;
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(x, 0), true); gc.LineTo(new Point(x + dir * 8, 0)); gc.LineTo(new Point(x, 10)); gc.EndFigure(true);
                gc.BeginFigure(new Point(x, h), true); gc.LineTo(new Point(x + dir * 8, h)); gc.LineTo(new Point(x, h - 10)); gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, g);
        }
    }
}
