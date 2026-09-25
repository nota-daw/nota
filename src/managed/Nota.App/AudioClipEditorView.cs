// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Clip for AUDIO clips (nota-design "Nota Clip Editor" 1b): the same frame as the
// MIDI editor — a 38px header (clip cell, Sample | Envelope, the envelope target while
// editing one, the Snap switch, the file's format) over the 232px inspector (CLIP,
// PLAYBACK, WARP) and the waveform canvas: a beat ruler, a 22px warp-marker strip, and
// the wave in the track colour, everything past the clip's played region darkened rather
// than hidden. Envelope is a mode of the canvas, not a checkbox.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Nota.Application;
using static Nota.App.ClipEditorKit;

namespace Nota.App;

public sealed class AudioClipEditorView : UserControl
{
    private readonly IAudioEngine _engine;
    private readonly Action _onChanged;
    public int TrackId { get; }
    public int ClipIndex { get; }

    private readonly WaveformView _wave = new();
    private readonly TextBlock _startText = Mono("");
    private readonly TextBlock _lengthText = Mono("");
    private readonly TextBlock _pitchText = Mono("");
    private readonly TextBlock _fileText = Meta("—");
    private readonly TextBlock _warpModeText = new() { FontSize = NotaType.Body };
    private readonly TextBlock _detectedText = Mono("—");
    private readonly TextBlock _markerCount = Meta("");
    private readonly TextBlock _envTargetText = new() { Text = "Volume", FontSize = NotaType.Body, Foreground = NotaPalette.TextPrimary };
    private readonly TextBlock _hint = Hint("");
    private readonly Border _envTargetDrop;
    private Action _syncGain = () => { }, _syncReverse = () => { }, _syncWarp = () => { }, _syncSnap = () => { };

    private double _startBeat;
    private double _lengthBeats = 4;
    private double _gain = 1;
    private int _pitch;
    private bool _reversed;
    private bool _warpEnabled;
    private int _warpMode = 3;   // Complex
    private bool _snapToGrid = true;                  // trim brackets snap to whole beats (default on)
    private double _detectedBpm;
    private bool _envMode;
    private int _envTarget;   // 0 = Volume (0..1), 1 = Pan (-1..1)
    private static readonly string[] WarpModes = { "Beats", "Tones", "Texture", "Complex", "Complex Pro", "Re-Pitch" };
    private static readonly string[] EnvTargets = { "Volume", "Pan" };

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

        // CLIP — where it sits, and its length (± one beat).
        var clip = Section("CLIP",
            Pair(Labeled("Start", Field(_startText)), Labeled("Length", Stepper(_lengthText, () => Resize(-1), () => Resize(1)))));

        // PLAYBACK — gain, varispeed pitch, direction.
        var gain = SliderRow("GAIN", 44, () => _gain / 2.0, v => SetGain(v * 2.0), GainText, out _syncGain,
            reset: () => SetGain(1.0), valueW: 44);
        var pitchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        pitchRow.Children.Add(new TextBlock
        {
            Text = "PITCH", Width = 44, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 0.72,
            Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        });
        var pitch = Stepper(_pitchText, () => Pitch(-1), () => Pitch(1), zone: 24);
        Grid.SetColumn(pitch, 1); pitchRow.Children.Add(pitch);
        // Reverse is non-destructive (the file is untouched) and free to toggle, so it
        // doubles as an audition switch; the wave mirrors so it always reads in playing order.
        var reverse = SwitchRow("Reverse", () => _reversed, ToggleReverse, out _syncReverse, height: 22);
        var playback = Section(SectionTitle("PLAYBACK"), 10, gain, pitchRow, reverse);

        // WARP — on/off and the stretch mode, the two detectors, what they found.
        var warpSwitch = SwitchRow("Warp", () => _warpEnabled, ToggleWarp, out _syncWarp);
        var mode = Dropdown(_warpModeText, a => ShowMenu(a, WarpModes, _warpMode, SetWarpMode));
        mode.Width = 104;
        var warpRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        warpRow.Children.Add(warpSwitch);
        Grid.SetColumn(mode, 1); warpRow.Children.Add(mode);
        var detect = ClipEditorKit.Button("Detect tempo", AutoWarp);
        ToolTip.SetTip(detect, "Detect the file's tempo and conform it to the project");
        var transients = ClipEditorKit.Button("To transients", TransientWarp);
        ToolTip.SetTip(transients, "Pin a marker on every hit so drums lock to the grid");
        var warp = Section(SectionTitle("WARP", _markerCount), 8,
            warpRow, Pair(detect, transients), Card(KeyValue("Detected", _detectedText)));

        var inspector = Inspector(Sections(clip, playback, warp), _hint);

        // Column 1: the waveform (row 0), a horizontal zoom scrollbar (row 1), and a
        // floating overlay (Canvas with null background → clicks pass through except the
        // inline BPM editor).
        _bpmBox = new TextBox
        {
            Classes = { "field" }, IsVisible = false, Width = 56, Height = 20, FontSize = 10, Padding = new Thickness(4, 0),
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
        var col1 = new Grid { RowDefinitions = new RowDefinitions("*,Auto"), Background = NotaPalette.BgSunken };
        col1.Children.Add(waveHost);
        Grid.SetRow(_hScroll, 1); col1.Children.Add(_hScroll);
        _wave.SetSnapToGrid(_snapToGrid);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(inspector);
        Grid.SetColumn(col1, 1);
        grid.Children.Add(col1);

        // Header: Sample | Envelope, the target while editing an envelope, Snap, the format.
        var tabs = Segments(new[] { "Sample", "Envelope" }, 0, i => SetEnvMode(i == 1), out _);
        _envTargetDrop = Dropdown(_envTargetText, a => ShowMenu(a, EnvTargets, _envTarget, SetEnvTarget));
        _envTargetDrop.IsVisible = false;
        var snap = SwitchRow("Snap", () => _snapToGrid, ToggleSnap, out _syncSnap);
        ToolTip.SetTip(snap, "Trim brackets land on the beat grid");
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { snap, HeaderDivider(), _fileText } };
        var name = engine.GetClipName(trackId, clipIndex);
        var header = Header(clipColor, string.IsNullOrWhiteSpace(name) ? $"Audio {trackId}" : name, "AUDIO", tabs, _envTargetDrop, right);
        DockPanel.SetDock(header, Dock.Top);
        Content = new DockPanel { Background = NotaPalette.Gutter, Children = { header, grid } };

        PaintHint();
        Reload();
    }

    private readonly TextBox _bpmBox;
    private readonly Canvas _overlay;
    private readonly ScrollBar _hScroll;
    private int _bpmSeg = -1;

    // Inline BPM editor over the warp strip: position + focus a small field.
    private void ShowBpmEditor(int seg, double bpm, double xCenter, double yTop)
    {
        _bpmSeg = seg;
        _bpmBox.Text = bpm.ToString("0.##", NotaNum.Culture);
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
        if (seg >= 0 && double.TryParse(_bpmBox.Text, NumberStyles.Any, NotaNum.Culture, out var v) && v > 0)
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

    // Re-reads clip geometry + peaks + sample metadata and refreshes the inspector.
    public void Reload()
    {
        if (_engine.TryGetClipInfo(TrackId, ClipIndex, out var ci))
        {
            _startBeat = ci.StartBeat;
            _lengthBeats = ci.LengthBeats > 0 ? ci.LengthBeats : _lengthBeats;
            _startText.Text = Position(_startBeat);
            _lengthText.Text = Duration(_lengthBeats);
        }
        if (_engine.TryGetAudioClipInfo(TrackId, ClipIndex, out var ai))
        {
            _pitch = (int)Math.Round(ai.PitchSemitones);
            _pitchText.Text = Semis(_pitch);
            _pitchText.Foreground = _pitch != 0 ? NotaPalette.AccentBright : NotaPalette.TextPrimary;
            _gain = ai.Gain;
            _syncGain();
            _wave.SetGain(ai.Gain);
            _reversed = ai.Reversed != 0;
            _wave.SetReversed(_reversed);   // draw the waveform the way the clip now plays
            _syncReverse();
            _warpEnabled = ai.WarpEnabled != 0;
            _warpMode = Math.Clamp(ai.WarpMode, 0, WarpModes.Length - 1);
            _syncWarp();
            _warpModeText.Text = WarpModes[_warpMode];
            _warpModeText.Foreground = _warpEnabled ? NotaPalette.TextPrimary : NotaPalette.TextDisabled;
            if (ai.SampleId != 0 && _engine.TryGetSampleInfo(ai.SampleId, out var si) && si.SampleRate > 0)
            {
                double secs = si.Frames / si.SampleRate;
                string ch = si.Channels == 1 ? "Mono" : si.Channels == 2 ? "Stereo" : $"{si.Channels} ch";
                _fileText.Text = string.Format(NotaNum.Culture,
                    "{0} · {1:0.0}\u2009kHz · {2:0.00}\u2009s", ch, si.SampleRate / 1000.0, secs);
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
        int markers = 0;
        if (_warpEnabled)
        {
            // Show the FULL warp with markers + Start/End trim brackets (trim the played
            // window without stretching).
            int n = _engine.GetClipWarpFullPeaks(TrackId, ClipIndex, peaks, 1024);
            _wave.SetPeaks(peaks, n);
            var ms = new double[128]; var mb = new double[128];
            markers = Math.Min(_engine.GetClipWarpMarkers(TrackId, ClipIndex, ms, mb), 128);
            _wave.SetMarkers(ms, mb, markers);
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
        _markerCount.Text = _warpEnabled ? $"{markers} {(markers == 1 ? "marker" : "markers")}" : "off";

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

    private void SetGain(double g)
    {
        _gain = Math.Clamp(g, 0, 2);
        _engine.SetClipGain(TrackId, ClipIndex, (float)_gain);
        _wave.SetGain(_gain);
        _onChanged();
    }

    private string GainText() => _gain <= 0.0011 ? "−∞\u2009dB" : string.Format(NotaNum.Culture, "{0:+0.0;−0.0;0.0}\u2009dB", AudioMath.LinToDb(_gain));

    private void ToggleSnap()
    {
        _snapToGrid = !_snapToGrid;
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

    private void SetWarpMode(int mode)
    {
        _warpMode = Math.Clamp(mode, 0, WarpModes.Length - 1);
        _engine.SetClipWarp(TrackId, ClipIndex, _warpEnabled, _warpMode);
        Reload();
        _onChanged();
    }

    // Detect the file's tempo and snap the clip's length to the beat grid so it
    // conforms to the project BPM (enables warp). Detection may fail on flat/short
    // material, in which case the clip is left as-is.
    private void AutoWarp()
    {
        _detectedBpm = _engine.AutoWarpClip(TrackId, ClipIndex);
        ShowDetected();
        Reload();
        _onChanged();
    }

    // Beats-mode warp: detect transients and pin grid-snapped markers at each hit so
    // percussion locks tightly to the grid ("Beats"). Fails on flat material.
    private void TransientWarp()
    {
        _detectedBpm = _engine.BeatWarpClip(TrackId, ClipIndex);
        ShowDetected();
        Reload();
        _onChanged();
    }

    private void ShowDetected()
    {
        bool found = _detectedBpm > 0;
        _detectedText.Text = found ? string.Format(NotaNum.Culture, "{0:0.00}\u2009BPM", _detectedBpm) : "—";
        _detectedText.Foreground = found ? NotaPalette.TextPrimary : NotaPalette.TextDisabled;
    }

    private void SetEnvMode(bool on)
    {
        _envMode = on;
        _envTargetDrop.IsVisible = on;
        _wave.SetEnvMode(on);
        PaintHint();
    }

    private void SetEnvTarget(int target)
    {
        _envTarget = target;
        _envTargetText.Text = EnvTargets[target];
        LoadEnvelope();
    }

    private void PaintHint() => _hint.Text = _envMode
        ? "Click — point · drag — move · right-click — delete"
        : "Double-click the wave — marker · drag — align";

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

    // ---- waveform + playback cursor + warp markers -----------------------
    // The waveform is drawn in SOURCE time; markers tie a source point to a beat.
    // Interior markers drag along source (aligning audio to their fixed beat);
    // double-click adds one (beat interpolated) / removes an interior one.
    private sealed class WaveformView : Control
    {
        private static readonly IBrush Bg = NotaPalette.BgSunken;
        private static readonly IBrush RulerBg = NotaPalette.Panel;
        private static readonly IPen RulerEdge = new Pen(NotaPalette.BorderDefault, 1);
        private static readonly IPen StripEdge = new Pen(NotaPalette.GraphBorder, 1);
        private static readonly IBrush RulerBar = NotaPalette.TextStrong;
        private static readonly IBrush RulerBeat = NotaPalette.TextDisabled;
        private static readonly IPen MidPen = new Pen(NotaPalette.GridBeat, 1);
        private static readonly IPen PlayheadPen = new Pen(NotaPalette.Accent, 1);
        private static readonly IBrush MarkerInk = NotaPalette.TextMuted;
        private static readonly IBrush MarkerHot = NotaPalette.Accent;
        private static readonly IPen SelLine = new Pen(NotaPalette.AccentDim, 1);
        private static readonly IPen GridBeatPen = new Pen(NotaPalette.GridSubBeat, 1);
        private static readonly IPen GridBarPen = new Pen(NotaPalette.BorderDefault, 1);
        private static readonly Typeface GridFace = NotaFonts.Mono;
        // BPM chip in the marker strip (drag to scrub the segment tempo); the hot one and the
        // selected marker's position chip take Brass Wash with a brass edge.
        private static readonly IBrush ChipBg = NotaPalette.Panel;
        private static readonly IPen ChipEdge = new Pen(NotaPalette.BorderDefault, 1);
        private static readonly IBrush ChipInk = NotaPalette.TextSecondary;
        private static readonly IBrush ChipBgHot = NotaPalette.AccentSubtle;
        private static readonly IPen ChipEdgeHot = new Pen(NotaPalette.BorderBrass, 1);
        private static readonly IBrush ChipInkHot = NotaPalette.AccentBright;
        // Ruler 24, marker strip 22, then the wave.
        private const double RulerH = 24, StripH = 22, WaveTop = RulerH + StripH;
        private const int BeatsPerBar = 4;   // matches the props-rail bar.beat readouts
        private IBrush _wave = NotaPalette.TrackBrushes[1];
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
        private static readonly IBrush Bracket = NotaPalette.TextStrong;
        private static readonly IBrush BracketHot = NotaPalette.TextPrimary;
        private static readonly IBrush OutsideDim = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0xD9);
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
        private static readonly IPen EnvPen = new Pen(NotaPalette.Accent, 1.8);
        private static readonly IPen EnvPenDim = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x60), 1);
        private static readonly IBrush EnvWash = NotaPalette.Wash(NotaPalette.BgSunken, 0x99);
        private static readonly IBrush NodeFill = NotaPalette.TextSecondary;
        private static readonly IBrush NodeHot = NotaPalette.Accent;
        private static readonly IPen NodeRing = new Pen(NotaPalette.BgSunken, 2);
        private sealed class EnvPt { public double beat; public double val; public float curve; }
        private readonly System.Collections.Generic.List<EnvPt> _env = new();
        private double _clipBeats = 1;
        private double _envMin, _envMax = 1;   // value axis (Volume 0..1, Pan -1..1)
        private bool _envMode;
        private int _envDrag = -1, _envHover = -1;
        private int _selMk = -1;               // marker picked by a click (its position shows in the strip)
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
        // The value axis spans the wave area below the ruler and the marker strip.
        private double EnvValToY(double v, double h) { double pad = 6, top = WaveTop + pad; double t = (Math.Clamp(v, _envMin, _envMax) - _envMin) / (_envMax - _envMin); return top + (1 - t) * Math.Max(1, h - pad - top); }
        private double EnvYToVal(double y, double h) { double pad = 6, top = WaveTop + pad; double t = (h - pad - y) / Math.Max(1, h - pad - top); return _envMin + Math.Clamp(t, 0, 1) * (_envMax - _envMin); }
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
            if (_selMk >= _mk.Count) _selMk = -1;
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
            if (h != _selMk) { _selMk = h; InvalidateVisual(); }   // a click picks a marker (or clears the pick)
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
            if (_envMode && _drag < 0)   // envelope mode: light the node under the pointer
            {
                var ep = e.GetPosition(this);
                int hv = HitEnv(ep.X, ep.Y, Bounds.Width, Bounds.Height);
                if (hv != _envHover) { _envHover = hv; InvalidateVisual(); }
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
            if (_hoverMk != -1 || _bpmHoverSeg != -1 || _envHover != -1) { _hoverMk = -1; _bpmHoverSeg = -1; _envHover = -1; InvalidateVisual(); }
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
            double top = WaveTop, wh = Math.Max(0, h - top);
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            _bpmHot.Clear();

            // Ruler (24) and the warp-marker strip (22) above the wave.
            ctx.FillRectangle(RulerBg, new Rect(0, 0, w, RulerH));
            ctx.DrawLine(RulerEdge, new Point(0, RulerH - 0.5), new Point(w, RulerH - 0.5));
            ctx.DrawLine(StripEdge, new Point(0, top - 0.5), new Point(w, top - 0.5));
            DrawRuler(ctx, w);

            // Beat grid through the wave (warped: the beats are known).
            if (_mk.Count > 1)
            {
                double tb = TotalBeats;
                int last = (int)Math.Floor(tb + 1e-6);
                for (int b = 1; b <= last; b++)
                {
                    double x = BeatToX(b, w);
                    if (x < -0.5 || x > w + 0.5) continue;
                    ctx.DrawLine(b % BeatsPerBar == 0 ? GridBarPen : GridBeatPen, new Point(x + 0.5, top), new Point(x + 0.5, h));
                }
            }

            // Peaks span the whole clip (frac 0..1); route through the zoom/scroll axis so
            // zooming magnifies the waveform and only the visible slice is drawn.
            double mid = top + wh / 2;
            ctx.DrawLine(MidPen, new Point(0, mid), new Point(w, mid));
            if (_peaks is not null && _count > 0 && wh > 4)
            {
                double amp = wh / 2 - 6;
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

            // Warp markers: a triangle in the strip (Ink 4; brass under the hand or selected).
            // The selected one carries its position and a dim brass line through the wave.
            for (int i = 0; i < _mk.Count; i++)
            {
                double xr = BeatToX(_mk[i].beat, w);
                if (xr < -6 || xr > w + 6) continue;   // scrolled off-screen
                double x = Math.Clamp(xr, 5, Math.Max(5, w - 5));
                bool hot = i == _hoverMk || i == _drag || i == _selMk;
                if (i == _selMk || i == _drag) ctx.DrawLine(SelLine, new Point(x + 0.5, top), new Point(x + 0.5, h));
                var g = new StreamGeometry();
                using (var gc = g.Open())
                {
                    gc.BeginFigure(new Point(x - 5, RulerH + 6), true);
                    gc.LineTo(new Point(x + 5, RulerH + 6));
                    gc.LineTo(new Point(x, RulerH + 14));
                    gc.EndFigure(true);
                }
                ctx.DrawGeometry(hot ? MarkerHot : MarkerInk, null, g);
            }

            // Per-segment local BPM, centred in the strip between two markers: drag it
            // up/down to scrub, double-click to type. Hidden when the segment is too narrow.
            if (_srcSR > 0)
                for (int i = 0; i + 1 < _mk.Count; i++)
                {
                    double beatSpan = _mk[i + 1].beat - _mk[i].beat;
                    double srcSpan = _mk[i + 1].src - _mk[i].src;
                    if (beatSpan <= 0 || srcSpan <= 0) continue;
                    double x0 = BeatToX(_mk[i].beat, w), x1 = BeatToX(_mk[i + 1].beat, w);
                    if (Math.Abs(x1 - x0) < 72) continue;   // mirrored: x1 sits left of x0
                    double bpm = beatSpan * _srcSR * 60.0 / srcSpan;
                    bool hotChip = i == _bpmHoverSeg || i == _bpmScrubSeg;
                    var num = new FormattedText(bpm.ToString("0.00", NotaNum.Culture), NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, hotChip ? ChipInkHot : ChipInk);
                    double cw = num.Width + 12, chH = 16;
                    double cx = Math.Clamp((x0 + x1) / 2, cw / 2 + 2, w - cw / 2 - 2);
                    if (cx < 0 || cx > w) continue;
                    var chip = new Rect(cx - cw / 2, RulerH + 3, cw, chH);
                    ctx.DrawRectangle(hotChip ? ChipBgHot : ChipBg, hotChip ? ChipEdgeHot : ChipEdge, chip.Deflate(0.5), 3, 3);
                    ctx.DrawText(num, new Point(chip.X + 6, chip.Y + (chH - num.Height) / 2));
                    _bpmHot.Add((chip, i, bpm));
                }

            // The selected marker's position, in a brass chip beside its triangle.
            if (_selMk >= 0 && _selMk < _mk.Count)
            {
                double x = BeatToX(_mk[_selMk].beat, w);
                if (x >= -6 && x <= w + 6)
                {
                    var ft = new FormattedText(Position(_mk[_selMk].beat), NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, ChipInkHot);
                    double cw = ft.Width + 12;
                    double lx = x + 9 + cw > w ? x - 9 - cw : x + 9;
                    var chip = new Rect(lx, RulerH + 3, cw, 16);
                    ctx.DrawRectangle(ChipBgHot, ChipEdgeHot, chip.Deflate(0.5), 3, 3);
                    ctx.DrawText(ft, new Point(chip.X + 6, chip.Y + (16 - ft.Height) / 2));
                }
            }

            // Everything outside the played region is darkened, not hidden; its edges are Ink 2
            // lines with grips pointing into the region.
            if (_srcMode && _srcTotal > 0)
            {
                double xs = SrcToX(_srcOff, w), xe = SrcToX(_srcOff + _srcLen, w);
                DimOutside(ctx, xs, xe, w, h);
                // The grips point INTO the region, which is the other way round when mirrored.
                if (xs >= -1 && xs <= w + 1) DrawBracket(ctx, xs, h, !_reversed, _srcDrag == 0);
                if (xe >= -1 && xe <= w + 1) DrawBracket(ctx, xe, h, _reversed, _srcDrag == 1);
            }
            if (_trimMode && _trimTotal > 0)
            {
                double xs = BeatToX(_trimStart, w), xe = BeatToX(_trimEnd, w);
                DimOutside(ctx, xs, xe, w, h);
                if (xs >= -1 && xs <= w + 1) DrawBracket(ctx, xs, h, !_reversed, _trimDrag == 0);
                if (xe >= -1 && xe <= w + 1) DrawBracket(ctx, xe, h, _reversed, _trimDrag == 1);
            }

            // Clip envelope: in Envelope mode the wave dims under a wash and the curve is drawn
            // in brass with its nodes; otherwise a quiet line keeps it in view.
            if (_envMode)
            {
                ctx.FillRectangle(EnvWash, new Rect(0, top, w, wh));
                if (_envMin < 0)
                {
                    double cy = EnvValToY(0, h);
                    ctx.DrawLine(GridBeatPen, new Point(0, cy), new Point(w, cy));
                }
            }
            if (_env.Count > 0)
            {
                var pen = _envMode ? EnvPen : EnvPenDim;
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
                    for (int i = 0; i < _env.Count; i++)
                    {
                        var p = _env[i];
                        bool hot = i == _envDrag || i == _envHover;
                        ctx.DrawEllipse(hot ? NodeHot : NodeFill, NodeRing, new Point(EnvBeatToX(p.beat, w), EnvValToY(p.val, h)), 4.5, 4.5);
                    }
            }

            if (_frac >= 0 && _frac <= 1)
            {
                // _frac runs over the PLAYED window, so on a reversed clip it walks the region
                // from its end back to its start (which the mirror then draws left-to-right).
                double pf = _reversed ? 1 - _frac : _frac;
                double x = (_srcMode && _srcTotal > 0) ? SrcToX(_srcOff + pf * _srcLen, w)
                         : (_trimMode && _trimTotal > 0) ? BeatToX(_trimStart + pf * (_trimEnd - _trimStart), w)
                         : FracToX(pf, w);
                ctx.DrawLine(PlayheadPen, new Point(x, 0), new Point(x, h));
            }
        }

        // Ruler: bar.beat over the warp's beats, or seconds over the source when unwarped.
        // Bars in Ink 2, beats in Ink 6, mono 9 on the baseline right of each tick.
        private void DrawRuler(DrawingContext ctx, double w)
        {
            if (_mk.Count > 1)
            {
                double tb = TotalBeats;
                double beatW = _zoom * w / tb;
                int step = beatW >= 30 ? 1 : BeatsPerBar;
                int last = (int)Math.Floor(tb + 1e-6);
                for (int b = 0; b <= last; b += step)
                {
                    double x = BeatToX(b, w);
                    if (x < -40 || x > w + 0.5) continue;
                    bool bar = b % BeatsPerBar == 0;
                    ctx.DrawLine(bar ? GridBarPen : GridBeatPen, new Point(x + 0.5, 0), new Point(x + 0.5, RulerH));
                    if (b >= last) continue;
                    int barNo = b / BeatsPerBar + 1, beatNo = b % BeatsPerBar + 1;
                    string label = bar ? barNo.ToString(NotaNum.Culture) : $"{barNo}.{beatNo}";
                    var ft = new FormattedText(label, NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, bar ? RulerBar : RulerBeat);
                    ctx.DrawText(ft, new Point(_reversed ? x - 4 - ft.Width : x + 4, RulerH - 5 - ft.Height));
                }
                return;
            }
            if (!(_srcMode && _srcTotal > 0 && _srcSR > 0)) return;
            double secs = _srcTotal / _srcSR;
            double pxPerSec = _zoom * w / Math.Max(1e-6, secs);
            double[] steps = { 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60 };
            double stepS = 60;
            foreach (var s in steps) if (s * pxPerSec >= 56) { stepS = s; break; }
            for (int k = 0; k * stepS <= secs + 1e-9; k++)
            {
                double t = k * stepS;
                double x = SrcToX(t * _srcSR, w);
                if (x < -40 || x > w + 0.5) continue;
                ctx.DrawLine(GridBeatPen, new Point(x + 0.5, 0), new Point(x + 0.5, RulerH));
                var ft = new FormattedText(string.Format(NotaNum.Culture, stepS < 1 ? "{0:0.0#} s" : "{0:0} s", t),
                    NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, k == 0 ? RulerBar : RulerBeat);
                ctx.DrawText(ft, new Point(_reversed ? x - 4 - ft.Width : x + 4, RulerH - 5 - ft.Height));
            }
        }

        // bar.beat.16th of a warp beat (1-based) for the selected marker's chip.
        private static string Position(double beat)
        {
            int bar = (int)Math.Floor(beat / BeatsPerBar) + 1;
            double inBar = beat - Math.Floor(beat / BeatsPerBar) * BeatsPerBar;
            int be = (int)Math.Floor(inBar) + 1, six = (int)Math.Round((inBar - Math.Floor(inBar)) * 4) + 1;
            if (six > 4) { six = 1; be++; }
            return $"{bar}.{be}.{six}";
        }

        // Shade everything outside the region's two edges. They arrive in content order, which
        // is right-to-left on a mirrored (reversed) view, so normalise before filling.
        private void DimOutside(DrawingContext ctx, double xa, double xb, double w, double h)
        {
            double lo = Math.Clamp(Math.Min(xa, xb), 0, w), hi = Math.Clamp(Math.Max(xa, xb), 0, w);
            double top = WaveTop;
            if (lo > 0.5) ctx.FillRectangle(OutsideDim, new Rect(0, top, lo, h - top));
            if (hi < w - 0.5) ctx.FillRectangle(OutsideDim, new Rect(hi, top, w - hi, h - top));
        }

        // A trim edge: an Ink 2 line through the wave, grips at top and bottom pointing into
        // the region. Which screen side that is depends on the draw order, so the caller
        // passes it — a mirrored (reversed) view has the region's start on the right.
        private void DrawBracket(DrawingContext ctx, double x, double h, bool gripRight, bool hot)
        {
            var brush = hot ? BracketHot : Bracket;
            double top = WaveTop;
            ctx.DrawLine(new Pen(brush, hot ? 2 : 1), new Point(x, top), new Point(x, h));
            double dir = gripRight ? 1 : -1;
            var g = new StreamGeometry();
            using (var gc = g.Open())
            {
                gc.BeginFigure(new Point(x, top), true); gc.LineTo(new Point(x + dir * 7, top)); gc.LineTo(new Point(x, top + 8)); gc.EndFigure(true);
                gc.BeginFigure(new Point(x, h), true); gc.LineTo(new Point(x + dir * 7, h)); gc.LineTo(new Point(x, h - 8)); gc.EndFigure(true);
            }
            ctx.DrawGeometry(brush, null, g);
        }
    }
}
