// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Clip for AUDIO clips (nota-design "Nota Clip Editor" 1b): the same frame as the
// MIDI editor — a 38px header (clip cell, Sample | Envelope, the envelope target while
// editing one, the snap-grid chip, the file's format) over the 232px inspector (CLIP,
// PLAYBACK, WARP) and the waveform canvas: a beat ruler, a 22px warp-marker strip, and
// the wave in the track colour, everything past the clip's played region darkened rather
// than hidden. Envelope and ADSR are modes of the canvas, not checkboxes. ADSR edits the
// clip's amplitude shape with the same handles the arrangement shows on the clip
// (ClipAdsrKit), so an edit in either place shows up in the other.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Application.Editing;
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
    private Action _syncGain = () => { }, _syncReverse = () => { }, _syncWarp = () => { };
    private Action _syncAttack = () => { }, _syncDecay = () => { }, _syncSustain = () => { }, _syncRelease = () => { };
    private ClipAdsr _adsr = ClipAdsr.Identity;
    private int _mode;   // 0 = Sample, 1 = Envelope, 2 = ADSR
    private readonly ContentControl _inspectorBody = new();
    private Control _sampleSections = null!, _adsrSections = null!;

    private double _startBeat;
    private double _lengthBeats = 4;
    private double _gain = 1;
    private int _pitch;
    private bool _reversed;
    private bool _warpEnabled;
    private int _warpMode = 3;   // Complex
    private double _detectedBpm;
    private long _srcFrames;   // the whole sample's frames (unwarped detail peaks)
    private bool _envMode;
    private int _envTarget;   // 0 = Volume (0..1), 1 = Pan (-1..1)
    private static readonly string[] WarpModes = { "Beats", "Tones", "Texture", "Complex", "Complex Pro", "Re-Pitch" };
    private static readonly string[] EnvTargets = { "Volume", "Pan" };

    public AudioClipEditorView(IAudioEngine engine, int trackId, int clipIndex, IBrush clipColor, Action onChanged)
    {
        ValueEntry.SetScope(this, true);   // Tab in a value box walks the values shown here
        _engine = engine;
        TrackId = trackId;
        ClipIndex = clipIndex;
        _onChanged = onChanged;
        _wave.SetColor(clipColor);
        _wave.MarkersCommitted = (src, beat) =>
        {
            WarpEdit(() => _engine.SetClipWarpMarkers(TrackId, ClipIndex, src, beat));
            Reload();
            _onChanged();
        };
        // Zoomed in: the visible slice's own peaks (file order, like the overview).
        _wave.DetailPeaks = (f0, f1, n) =>
        {
            var pk = new float[n * 2];
            int got = 0;
            if (_warpEnabled)
            {
                if (_engine.TryGetAudioClipInfo(TrackId, ClipIndex, out var wi) && wi.WarpBeats > 0)
                    got = _engine.GetClipWarpPeaksRange(TrackId, ClipIndex, f0 * wi.WarpBeats, f1 * wi.WarpBeats, pk, n);
            }
            else if (_srcFrames > 0)
                got = _engine.GetClipSourcePeaksRange(TrackId, ClipIndex, f0 * _srcFrames, f1 * _srcFrames, pk, n);
            return (pk, got);
        };
        _wave.SourceRegionCommitted = (off, len) =>
        {
            _engine.SetClipSourceRegion(TrackId, ClipIndex, off, len);
            Reload();
            _onChanged();
        };
        _wave.WarpTrimCommitted = (ps, pe) =>
        {
            WarpEdit(() => _engine.SetClipWarpTrim(TrackId, ClipIndex, ps, pe));
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
        _wave.AdsrCommitted = SetAdsr;

        // CLIP — where it sits, and its length (± one beat).
        var clip = Section("Clip",
            Pair(Labeled("Start", Field(_startText)), Labeled("Length", Stepper(_lengthText, () => Resize(-1), () => Resize(1)))));

        // PLAYBACK — gain, varispeed pitch, direction.
        var gain = SliderRow("Gain", 44, () => _gain / 2.0, v => SetGain(v * 2.0), GainText, out _syncGain,
            reset: () => SetGain(1.0), valueW: 44);
        var pitchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10 };
        pitchRow.Children.Add(new TextBlock
        {
            Text = "Pitch", Width = 44, FontSize = 9, FontWeight = FontWeight.Medium,
            Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
        });
        var pitch = Stepper(_pitchText, () => Pitch(-1), () => Pitch(1), zone: 24);
        Grid.SetColumn(pitch, 1); pitchRow.Children.Add(pitch);
        // Reverse is non-destructive (the file is untouched) and free to toggle, so it
        // doubles as an audition switch; the wave mirrors so it always reads in playing order.
        var reverse = SwitchRow("Reverse", () => _reversed, ToggleReverse, out _syncReverse, height: 22);
        var playback = Section(SectionTitle("Playback"), 10, gain, pitchRow, reverse);

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
        var warp = Section(SectionTitle("Warp", _markerCount), 8,
            warpRow, Pair(detect, transients), Card(KeyValue("Detected", _detectedText)));

        // ADSR — the clip's amplitude shape (the ADSR tab swaps the inspector to it). Times
        // run on a square-law slider over the clip length, so short attacks stay fine-grained.
        var attack = SliderRow("Attack", 52, () => TimeNorm(_adsr.AttackBeats),
            v => SetAdsrStage(AdsrHandle.Attack, NormTime(v), _adsr.Sustain), () => AdsrTime(_adsr.AttackBeats), out _syncAttack,
            reset: () => SetAdsr(ClipAdsrKit.Reset(AdsrHandle.Attack, _adsr)), valueW: 52);
        var decay = SliderRow("Decay", 52, () => TimeNorm(_adsr.DecayBeats),
            v => SetAdsrStage(AdsrHandle.DecaySustain, _adsr.AttackBeats + NormTime(v), _adsr.Sustain), () => AdsrTime(_adsr.DecayBeats), out _syncDecay,
            reset: () => { var a = _adsr; a.DecayBeats = 0; SetAdsr(a); }, valueW: 52);
        var sustain = SliderRow("Sustain", 52, () => _adsr.Sustain,
            v => SetAdsrStage(AdsrHandle.DecaySustain, _adsr.AttackBeats + _adsr.DecayBeats, v), () => ClipAdsrKit.SustainText(_adsr.Sustain), out _syncSustain,
            reset: () => { var a = _adsr; a.Sustain = 1f; SetAdsr(a); }, valueW: 52);
        var release = SliderRow("Release", 52, () => TimeNorm(_adsr.ReleaseBeats),
            v => SetAdsrStage(AdsrHandle.Release, _lengthBeats - NormTime(v), _adsr.Sustain), () => AdsrTime(_adsr.ReleaseBeats), out _syncRelease,
            reset: () => SetAdsr(ClipAdsrKit.Reset(AdsrHandle.Release, _adsr)), valueW: 52);
        var resetAll = ClipEditorKit.Button("Reset", () => SetAdsr(ClipAdsr.Identity));
        ToolTip.SetTip(resetAll, "Play the whole sample at full level");
        var adsr = Section(SectionTitle("ADSR"), 10, attack, decay, sustain, release, resetAll);

        _sampleSections = Sections(clip, playback, warp);
        _adsrSections = Sections(adsr);
        _inspectorBody.Content = _sampleSections;
        var inspector = Inspector(_inspectorBody, _hint);

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

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(inspector);
        Grid.SetColumn(col1, 1);
        grid.Children.Add(col1);

        // Header: Sample | Envelope | ADSR, the target while editing an envelope, Snap, the format.
        var tabs = Segments(new[] { "Sample", "Envelope", "ADSR" }, 0, SetMode, out _);
        _envTargetDrop = Dropdown(_envTargetText, a => ShowMenu(a, EnvTargets, _envTarget, SetEnvTarget));
        _envTargetDrop.IsVisible = false;
        // The clip editors' snap grid (shared with the piano roll): trim brackets, warp markers
        // and envelope points land on the lines drawn in the wave.
        var (snap, chip) = SnapChip.Create(EditorKind.PianoRoll, () => _wave.DrawnGrid);
        _wave.GridChanged += chip.Refresh;
        var right = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Children = { snap, HeaderDivider(), _fileText } };
        var name = engine.GetClipName(trackId, clipIndex);
        var header = Header(clipColor, string.IsNullOrWhiteSpace(name) ? $"Audio {trackId}" : name, "AUDIO", tabs, _envTargetDrop, right);
        DockPanel.SetDock(header, Dock.Top);
        Content = new DockPanel { Background = NotaPalette.Gutter, Children = { header, grid } };

        PaintHint();
        Reload();
    }

    /// <summary>The snap grid of the axis the wave draws (for the status line of ⌘1–⌘5).</summary>
    internal ResolvedGrid SnapGridState => _wave.DrawnGrid;

    private readonly TextBox _bpmBox;
    private readonly Canvas _overlay;
    private readonly ScrollBar _hScroll;
    private int _bpmSeg = -1;
    private double _bpmEditStart;   // the segment's BPM when the field opened

    // Inline BPM editor over the warp strip: position + focus a small field.
    private void ShowBpmEditor(int seg, double bpm, double xCenter, double yTop)
    {
        _bpmSeg = seg;
        _bpmEditStart = bpm;
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
        if (seg >= 0 && double.TryParse(_bpmBox.Text, NumberStyles.Any, NotaNum.Culture, out var v) && v > 0
            && Math.Abs(v - _bpmEditStart) >= 0.005)   // unchanged → no re-warp
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
            _pitchText.Foreground = _pitch != 0 ? NotaPalette.TextPrimary : NotaPalette.TextSecondary;
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
        _srcFrames = srcFrames;
        _wave.SetSourceFrames(srcFrames);

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
        LoadAdsr();
        SyncWaveScroll();
    }

    // Called from the UI tick: show a playback cursor when the transport is inside
    // this clip, else hide it.
    public void OnPlayhead(double beats, bool playing)
    {
        double frac;
        int scene = SessionClip.SceneOf(ClipIndex);
        if (scene >= 0)
        {
            // A Session slot runs on its own loop clock: follow the slot, not the song position.
            double pos = _engine.SessionSlotState(TrackId, scene) == 3 ? _engine.SessionSlotPosition(TrackId) : -1;
            frac = pos >= 0 && _lengthBeats > 0 && pos <= _lengthBeats ? pos / _lengthBeats : -1;
        }
        else
            frac = (playing && _lengthBeats > 0 && beats >= _startBeat && beats <= _startBeat + _lengthBeats)
                ? (beats - _startBeat) / _lengthBeats : -1;
        _wave.SetPlayhead(frac);
    }

    /// <summary>Set by the host: runs the engine's background warp build after a warp edit.</summary>
    public Action? WarpBuildRequested;

    // A warp-affecting edit skips the synchronous stretch (seconds of UI freeze on a long
    // file): the clip keeps playing its previous cache until the background build lands.
    private void WarpEdit(Action edit)
    {
        _engine.SetDeferWarpBuild(true);
        try { edit(); }
        finally { _engine.SetDeferWarpBuild(false); }
        WarpBuildRequested?.Invoke();
    }

    private void Resize(int deltaBeats)
    {
        double len = Math.Max(0.25, _lengthBeats + deltaBeats);
        // Warped clips own a musical target length; unwarped clips trim the source.
        if (_warpEnabled) WarpEdit(() => _engine.SetClipWarpLength(TrackId, ClipIndex, len));
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

    private void ToggleReverse()
    {
        _engine.SetClipReverse(TrackId, ClipIndex, !_reversed);
        Reload();          // picks the new state up (and re-reads the played peaks)
        _onChanged();
    }

    private void ToggleWarp()
    {
        _warpEnabled = !_warpEnabled;
        WarpEdit(() => _engine.SetClipWarp(TrackId, ClipIndex, _warpEnabled, _warpMode));
        Reload();
        _onChanged();
    }

    private void SetWarpMode(int mode)
    {
        _warpMode = Math.Clamp(mode, 0, WarpModes.Length - 1);
        WarpEdit(() => _engine.SetClipWarp(TrackId, ClipIndex, _warpEnabled, _warpMode));
        Reload();
        _onChanged();
    }

    // Detect the file's tempo and snap the clip's length to the beat grid so it
    // conforms to the project BPM (enables warp). Detection may fail on flat/short
    // material, in which case the clip is left as-is.
    private void AutoWarp()
    {
        WarpEdit(() => _detectedBpm = _engine.AutoWarpClip(TrackId, ClipIndex));
        ShowDetected();
        Reload();
        _onChanged();
    }

    // Beats-mode warp: detect transients and pin grid-snapped markers at each hit so
    // percussion locks tightly to the grid ("Beats"). Fails on flat material.
    private void TransientWarp()
    {
        WarpEdit(() => _detectedBpm = _engine.BeatWarpClip(TrackId, ClipIndex));
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

    private void SetMode(int mode)
    {
        _mode = mode;
        _envMode = mode == 1;
        _envTargetDrop.IsVisible = _envMode;
        _wave.SetEnvMode(_envMode);
        _wave.SetAdsrMode(mode == 2);
        _inspectorBody.Content = mode == 2 ? _adsrSections : _sampleSections;
        PaintHint();
    }

    // ---- ADSR ----------------------------------------------------------------
    private void LoadAdsr()
    {
        _adsr = _engine.GetClipAdsr(TrackId, ClipIndex);
        _wave.SetAdsr(_adsr, _lengthBeats, _engine.Bpm);
        SyncAdsr();
    }

    private void SyncAdsr() { _syncAttack(); _syncDecay(); _syncSustain(); _syncRelease(); }

    // One engine edit (one undo step); the arrangement reloads and draws the same shape.
    private void SetAdsr(ClipAdsr a)
    {
        _adsr = a.Fit(_lengthBeats);
        _engine.SetClipAdsr(TrackId, ClipIndex, _adsr);
        _wave.SetAdsr(_adsr, _lengthBeats, _engine.Bpm);
        SyncAdsr();
        _onChanged();
    }

    // A slider moves one stage the way its handle would (it stops at its neighbours).
    private void SetAdsrStage(AdsrHandle h, double beat, double level)
        => SetAdsr(ClipAdsrKit.Drag(h, _adsr, beat, level, _lengthBeats));

    private double TimeNorm(double beats) => _lengthBeats > 0 ? Math.Sqrt(Math.Clamp(beats / _lengthBeats, 0, 1)) : 0;
    private double NormTime(double v) => v * v * _lengthBeats;
    private string AdsrTime(double beats) => NotaNum.Time(beats * 60.0 / Math.Max(1, _engine.Bpm));

    private void SetEnvTarget(int target)
    {
        _envTarget = target;
        _envTargetText.Text = EnvTargets[target];
        LoadEnvelope();
    }

    private void PaintHint() => _hint.Text = _mode switch
    {
        1 => "Click — point · drag — move · right-click — delete",
        2 => "Drag a handle · double-click — reset",
        _ => "Double-click the wave — marker · drag — align",
    };

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
        WarpEdit(() => _engine.SetClipPitch(TrackId, ClipIndex, _pitch));
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
        // Snap grid (specs/003-adaptive-snap-grid, US4): the clip editors' shared setting,
        // resolved per axis — warp beats for brackets / markers / the wave grid, the
        // envelope's played beats in Envelope mode. Alt held during a drag moves freely.
        private bool _free;
        private ResolvedGrid _lastDrawn;
        private static GridSetting GridSetting => EditorGrids.Shared.Get(EditorKind.PianoRoll);
        private ResolvedGrid WarpGrid => SnapGrid.Resolve(GridSetting, _zoom * Math.Max(1, Bounds.Width) / TotalBeats, BeatsPerBar);
        private ResolvedGrid EnvGrid => SnapGrid.Resolve(GridSetting, _zoom * Math.Max(1, Bounds.Width) / _clipBeats, BeatsPerBar);
        /// <summary>The grid of the axis being drawn (and edited) — what the header chip shows.
        /// An unwarped clip has no beat grid of its own, so its only snapping is the envelope's.</summary>
        public ResolvedGrid DrawnGrid => _envMode || _mk.Count < 2 ? EnvGrid : WarpGrid;
        /// <summary>The drawn step changed (zoom, mode, the setting) — the header chip relabels.</summary>
        public event Action? GridChanged;
        private double SnapOn(double beat, ResolvedGrid g)
            => _free || !g.SnapOn ? beat : SnapGrid.SnapBeat(beat, g.Snap.Beats(BeatsPerBar), BeatsPerBar);
        private double SnapWarp(double beat) => SnapOn(beat, WarpGrid);
        private double SnapEnv(double beat) => SnapOn(beat, EnvGrid);

        private void OnGrid(EditorKind k) { if (k == EditorKind.PianoRoll) InvalidateVisual(); }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            EditorGrids.Shared.Changed += OnGrid;
        }
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            EditorGrids.Shared.Changed -= OnGrid;
        }
        private const double MinSrcFrames = 256;
        private static readonly Cursor ArrowCur = new(StandardCursorType.Arrow);
        private static readonly Cursor ResizeCur = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor ScrubCur = new(StandardCursorType.SizeNorthSouth);
        private StandardCursorType _curKind = StandardCursorType.Arrow;

        // BPM chip drag-to-scrub (like the global BPM DragNumber): hold on a segment's
        // BPM chip and drag up/down; the warp re-times live (committed on release).
        private int _bpmScrubSeg = -1, _bpmHoverSeg = -1;
        private double _bpmScrubStartY, _bpmScrubStartBpm;
        private bool _bpmScrubMoved;   // a plain click (no drag) must not re-warp the clip

        // Volume envelope (M9 follow-up): points (beat, value 0..1) in clip-local beats.
        private static readonly IPen EnvPen = new Pen(NotaPalette.VizInk, 1.8);      // the curve at rest: neutral
        private static readonly IPen EnvPenDim = new Pen(NotaPalette.Wash(NotaPalette.VizInk, 0x60), 1);
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

        // ADSR (the ADSR tab): the clip's amplitude shape over the PLAYED region, handles from
        // ClipAdsrKit. Like the envelope it runs in played time, so it never mirrors.
        private ClipAdsr _adsr = ClipAdsr.Identity;
        private double _adsrLen = 1, _bpm = 120;
        private bool _adsrMode;
        private AdsrHandle _adsrDrag, _adsrHover;
        private ClipAdsr _adsrStart;
        private double _adsrAnchorBeat, _adsrAnchorLevel;
        private Point _adsrPress;
        private static readonly Cursor HandCur = new(StandardCursorType.Hand);
        /// <summary>Raised when an ADSR handle drag ends (or a handle is reset) with the new shape.</summary>
        public Action<ClipAdsr>? AdsrCommitted;

        public void SetAdsrMode(bool on) { _adsrMode = on; _adsrHover = AdsrHandle.None; Cursor = ArrowCur; _curKind = StandardCursorType.Arrow; InvalidateVisual(); }
        public void SetAdsr(ClipAdsr a, double lenBeats, double bpm)
        {
            if (_adsrDrag != AdsrHandle.None) return;   // don't yank a handle out from under the hand
            _adsr = a; _adsrLen = Math.Max(1e-6, lenBeats); _bpm = bpm;
            InvalidateVisual();
        }

        // The played region on screen (left→right in playing order) over the wave's value axis.
        private AdsrFrame AdsrFrameNow(double w, double h)
        {
            double xa, xb;
            if (_srcMode && _srcTotal > 0) { xa = SrcToX(_srcOff, w); xb = SrcToX(_srcOff + _srcLen, w); }
            else if (_trimMode && _trimTotal > 0) { xa = BeatToX(_trimStart, w); xb = BeatToX(_trimEnd, w); }
            else { xa = FracToXView(0, w); xb = FracToXView(1, w); }
            return new AdsrFrame(Math.Min(xa, xb), Math.Max(xa, xb), WaveTop + 6, h - 6, _adsrLen);
        }

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
        // Deep enough to reach single source frames (~8 px each) on any clip length.
        private double _srcFramesAll;   // the whole sample's frames (sets the zoom limit)
        public void SetSourceFrames(double frames) => _srcFramesAll = Math.Max(0, frames);
        private double MaxZoom => Math.Max(60.0, _srcFramesAll * 8 / Math.Max(1, Bounds.Width));
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
        public void SetPeaks(float[] peaks, int count) { _peaks = peaks; _count = count; ClearDetail(); InvalidateVisual(); }
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

        private int HitMarker(double x, double w)
        {
            int best = -1; double bd = HandlePx;
            for (int i = 0; i < _mk.Count; i++) { double d = Math.Abs(BeatToX(_mk[i].beat, w) - x); if (d < bd) { bd = d; best = i; } }
            return best;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            double W = Bounds.Width, H = Bounds.Height;
            _free = (e.KeyModifiers & KeyModifiers.Alt) != 0;

            // ADSR mode takes over the pointer: grab a handle (double-click resets its stage).
            if (_adsrMode)
            {
                if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed) return;
                var p = e.GetPosition(this);
                var hit = ClipAdsrKit.Hit(p, AdsrFrameNow(W, H), _adsr);
                if (hit == AdsrHandle.None) return;
                if (e.ClickCount == 2)
                {
                    _adsr = ClipAdsrKit.Reset(hit, _adsr.Fit(_adsrLen));
                    AdsrCommitted?.Invoke(_adsr);
                }
                else
                {
                    _adsrDrag = hit;
                    _adsrStart = _adsr.Fit(_adsrLen);
                    (_adsrAnchorBeat, _adsrAnchorLevel) = ClipAdsrKit.Anchor(hit, _adsrStart, _adsrLen);
                    _adsrPress = p;
                    e.Pointer.Capture(this);
                }
                e.Handled = true;
                InvalidateVisual();
                return;
            }

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
                            _bpmScrubSeg = b.seg; _bpmScrubStartY = pp.Y; _bpmScrubStartBpm = b.bpm; _bpmScrubMoved = false;
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
                var np = new EnvPt { beat = SnapEnv(EnvXToBeat(p.X, W)), val = EnvYToVal(p.Y, H) };   // add + grab
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
                else AddMarker(SnapWarp(XToBeat(x, w)));                                // add on the grid
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
            _free = (e.KeyModifiers & KeyModifiers.Alt) != 0;   // live, so Alt can toggle mid-drag
            if (_adsrDrag != AdsrHandle.None)
            {
                var p = e.GetPosition(this);
                double dx = p.X - _adsrPress.X, dy = p.Y - _adsrPress.Y;
                if (_adsrDrag == AdsrHandle.AttackOrSustain)
                {
                    if (Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
                    _adsrDrag = ClipAdsrKit.Resolve(_adsrDrag, dx, dy);
                    (_adsrAnchorBeat, _adsrAnchorLevel) = ClipAdsrKit.Anchor(_adsrDrag, _adsrStart, _adsrLen);
                }
                var f = AdsrFrameNow(Bounds.Width, Bounds.Height);
                double beat = _adsrAnchorBeat + dx / Math.Max(1, f.Right - f.Left) * _adsrLen;
                double level = _adsrAnchorLevel - dy / Math.Max(1, f.Bottom - f.Top);
                _adsr = ClipAdsrKit.Drag(_adsrDrag, _adsrStart, beat, level, _adsrLen);
                InvalidateVisual();
                return;
            }
            if (_adsrMode)   // ADSR mode: light the handle under the pointer
            {
                var hv = ClipAdsrKit.Hit(e.GetPosition(this), AdsrFrameNow(Bounds.Width, Bounds.Height), _adsr);
                if (hv != _adsrHover) { _adsrHover = hv; InvalidateVisual(); }
                Cursor = hv != AdsrHandle.None ? HandCur : ArrowCur;
                _curKind = StandardCursorType.Arrow;
                return;
            }
            if (_bpmScrubSeg >= 0)   // drag up = faster (0.5 BPM/px, like the global BPM)
            {
                double dy = _bpmScrubStartY - e.GetPosition(this).Y;
                double bpm = Math.Clamp(_bpmScrubStartBpm + dy * 0.5, 20, 999);
                if (!_bpmScrubMoved && Math.Abs(dy) < 2) return;   // a click, not a drag (yet)
                _bpmScrubMoved = true;
                ApplySegmentBpm(_bpmScrubSeg, bpm, commit: false);   // live warp + redraw, no engine round-trip
                return;
            }
            if (_envDrag >= 0)
            {
                var p = e.GetPosition(this);
                _env[_envDrag].beat = SnapEnv(EnvXToBeat(p.X, Bounds.Width));
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
                bt = SnapWarp(bt);   // land the edge exactly on the grid (Alt / snap off: free)
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
            double b = SnapWarp(XToBeat(e.GetPosition(this).X, w));   // align to the grid
            double lo = _mk[_drag - 1].beat + 1e-3, hi = _mk[_drag + 1].beat - 1e-3;  // stay between neighbours
            _mk[_drag] = (_mk[_drag].src, Math.Clamp(b, lo, hi));   // source fixed; beat moves (warp)
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_adsrDrag != AdsrHandle.None)
            {
                _adsrDrag = AdsrHandle.None;
                e.Pointer.Capture(null);
                if (!_adsr.Equals(_adsrStart)) AdsrCommitted?.Invoke(_adsr);
                InvalidateVisual();
                return;
            }
            if (_bpmScrubSeg >= 0)   // push the scrubbed warp (one undo step) — only if it was dragged
            {
                _bpmScrubSeg = -1; e.Pointer.Capture(null);
                if (_bpmScrubMoved) Commit();
                _bpmScrubMoved = false;
                InvalidateVisual();
                return;
            }
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
            if (_hoverMk != -1 || _bpmHoverSeg != -1 || _envHover != -1 || _adsrHover != AdsrHandle.None)
            { _hoverMk = -1; _bpmHoverSeg = -1; _envHover = -1; _adsrHover = AdsrHandle.None; InvalidateVisual(); }
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

        // ---- zoom-aware waveform detail ----------------------------------------
        // The overview (_peaks) spans the whole clip at a fixed resolution; zoomed in, the
        // host fetches peaks for just the visible slice (padded a screen either side so a
        // scroll doesn't refetch at once). Content (file-order) fracs, like the overview.
        /// <summary>Set by the host: peaks over content fracs [f0, f1] at n buckets → (peaks, count).</summary>
        public Func<double, double, int, (float[] peaks, int count)>? DetailPeaks;
        private float[]? _dPeaks;
        private int _dCount;
        private double _dF0, _dF1;
        private bool _detailQueued;
        private const double DetailPx = 1.5;   // overview buckets wider than this → fetch detail

        private void ClearDetail() { _dPeaks = null; _dCount = 0; }

        // Visible content-frac range (mirrored back when the clip is drawn reversed).
        private (double f0, double f1) VisibleContent(double w)
        {
            double v0 = Math.Clamp(XToFracView(0, w), 0, 1), v1 = Math.Clamp(XToFracView(w, w), 0, 1);
            return _reversed ? (1 - v1, 1 - v0) : (v0, v1);
        }

        private bool DetailCovers(double f0, double f1, double w)
        {
            if (_dPeaks is null || _dCount <= 0 || _dF0 > f0 + 1e-12 || _dF1 < f1 - 1e-12) return false;
            double px = (_dF1 - _dF0) * _zoom * w / _dCount;   // detail bucket width at this zoom
            return px <= DetailPx * 1.01 && px >= 0.25;         // fine enough, and not zoomed far back out
        }

        private void RequestDetail()
        {
            if (_detailQueued || DetailPeaks is null) return;
            _detailQueued = true;
            Dispatcher.UIThread.Post(() =>
            {
                _detailQueued = false;
                if (DetailPeaks is null) return;
                double ww = Bounds.Width;
                var (a, b) = VisibleContent(ww);
                double span = b - a;
                if (span <= 0) return;
                double p0 = Math.Max(0, a - span), p1 = Math.Min(1, b + span);
                int n = (int)Math.Clamp(Math.Ceiling((p1 - p0) * _zoom * ww), 1, MaxDetail);
                // Too wide a slice for one fetch → just the visible part.
                if ((p1 - p0) * _zoom * ww / n > DetailPx) { p0 = a; p1 = b; n = (int)Math.Clamp(Math.Ceiling(span * _zoom * ww), 1, MaxDetail); }
                var (pk, cnt) = DetailPeaks(p0, p1, n);
                if (cnt <= 0) return;
                _dPeaks = pk; _dCount = cnt; _dF0 = p0; _dF1 = p1;
                InvalidateVisual();
            }, DispatcherPriority.Background);
        }
        private const int MaxDetail = 8192;   // the engine's range-peaks cap

        // Peaks whose buckets evenly span content fracs [f0, f1], drawn through the zoom/
        // scroll (and mirror) axis; only the visible buckets are visited.
        private void DrawPeaks(DrawingContext ctx, float[] peaks, int count, double f0, double f1, double w,
                               double mid, double wh, AdsrFrame adsrF, ClipAdsr adsrFit, bool shaped)
        {
            double amp = wh / 2 - 6;
            double span = f1 - f0;
            if (span <= 0) return;
            double bwRaw = span * _zoom * w / count;
            double bw = Math.Max(1, bwRaw);
            var (c0, c1) = VisibleContent(w);
            int i0 = Math.Max(0, (int)Math.Floor((c0 - f0) / span * count) - 1);
            int i1 = Math.Min(count - 1, (int)Math.Ceiling((c1 - f0) / span * count) + 1);
            for (int i = i0; i <= i1; i++)
            {
                // FracToX marks the bucket's leading edge — its RIGHT edge once mirrored.
                double fx = FracToX(f0 + span * i / count, w);
                if (_reversed) fx -= bwRaw;
                if (fx + bw < 0 || fx > w) continue;
                // The ADSR shapes the played region the way it sounds (outside it is darkened anyway).
                double g = _gain;
                if (shaped)
                {
                    double bt = adsrF.Beat(fx + bw / 2);
                    if (bt >= 0 && bt <= _adsrLen) g *= adsrFit.GainAt(bt, _adsrLen);
                }
                float mn = (float)Math.Clamp(peaks[i * 2] * g, -1.0, 1.0);
                float mx = (float)Math.Clamp(peaks[i * 2 + 1] * g, -1.0, 1.0);
                ctx.FillRectangle(_wave, new Rect(fx, mid - mx * amp, bw, Math.Max(1, (mx - mn) * amp)));
            }
        }

        // ---- ruler / grid density ----------------------------------------------
        private const double TickMinPx = 6;     // closest two ruler ticks may sit
        private const double LabelPadPx = 12;   // gap after a ruler label
        // Beat steps the ruler and grid choose from: sub-beats (power-of-two), beats, then bars ×2ⁿ.
        private static readonly double[] BeatSteps = BuildBeatSteps();
        private static double[] BuildBeatSteps()
        {
            var l = new System.Collections.Generic.List<double>();
            for (double f = 1.0 / 4096; f < 4; f *= 2) l.Add(f);   // ruler ticks reach sample-level zoom
            for (double b = BeatsPerBar; b <= BeatsPerBar * 65536.0; b *= 2) l.Add(b);
            return l.ToArray();
        }
        // The smallest step (beats) at least minPx wide.
        private static double BeatStep(double pxPerBeat, double minPx)
        {
            foreach (var s in BeatSteps) if (s * pxPerBeat >= minPx) return s;
            return BeatSteps[^1];
        }
        private static bool IsMultiple(double v, double step) => Math.Abs(v / step - Math.Round(v / step)) < 1e-6;
        // Visible beat range, clamped to the warp.
        private (double b0, double b1) VisibleBeats(double w)
        {
            var (f0, f1) = VisibleContent(w);
            return (f0 * TotalBeats, f1 * TotalBeats);
        }
        private double _monoCharW;
        private double MonoCharW => _monoCharW > 0 ? _monoCharW
            : _monoCharW = new FormattedText("0000000000", NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, RulerBar).Width / 10;

        // bar / bar.beat / bar.beat.16th[.sub] label for a beat (1-based), as fine as the step needs.
        private static string BeatLabel(double beat, double step)
        {
            int bar = (int)Math.Floor(beat / BeatsPerBar + 1e-9) + 1;
            double inBar = beat - (bar - 1) * BeatsPerBar;
            int bt = (int)Math.Floor(inBar + 1e-9) + 1;
            double sub = inBar - (bt - 1);
            if (IsMultiple(beat, BeatsPerBar)) return bar.ToString(NotaNum.Culture);
            if (step >= 1 || sub < 1e-6) return $"{bar}.{bt}";
            int six = (int)Math.Floor(sub * 4 + 1e-6) + 1;   // 16ths within the beat
            return $"{bar}.{bt}.{six}";
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            // Zoomed in, buckets, markers and the playhead can sit past either edge: keep every
            // stroke inside the editor instead of over the inspector or the panel beside it.
            using var bounds = ctx.PushClip(new Rect(0, 0, w, h));
            double top = WaveTop, wh = Math.Max(0, h - top);
            ctx.FillRectangle(Bg, new Rect(0, 0, w, h));
            _bpmHot.Clear();

            // Ruler (24) and the warp-marker strip (22) above the wave.
            ctx.FillRectangle(RulerBg, new Rect(0, 0, w, RulerH));
            ctx.DrawLine(RulerEdge, new Point(0, RulerH - 0.5), new Point(w, RulerH - 0.5));
            ctx.DrawLine(StripEdge, new Point(0, top - 0.5), new Point(w, top - 0.5));
            DrawRuler(ctx, w);

            // The snap grid through the wave: one line per snap position at the drawn step
            // (SnapGrid.GridLines, the same set the drags land on). Envelope mode draws the
            // envelope's played timeline; otherwise the warp beats (warped clips only — an
            // unwarped clip has no beat grid). Only the visible slice is walked.
            var drawn = DrawnGrid;
            if (_envMode || _mk.Count > 1)
            {
                double step = drawn.Draw.Beats(BeatsPerBar);
                double total = _envMode ? _clipBeats : TotalBeats;
                double b0, b1;
                if (_envMode) { b0 = EnvXToBeat(0, w); b1 = EnvXToBeat(w, w); }
                else (b0, b1) = VisibleBeats(w);
                foreach (double b in SnapGrid.GridLines(b0, Math.Min(b1, total - 1e-6), step, BeatsPerBar))
                {
                    if (b < 1e-9) continue;   // the clip's own left edge
                    double x = _envMode ? EnvBeatToX(b, w) : BeatToX(b, w);
                    bool bar = IsMultiple(b, BeatsPerBar);
                    ctx.DrawLine(bar ? GridBarPen : GridBeatPen, new Point(x + 0.5, top), new Point(x + 0.5, h));
                }
            }
            if (drawn != _lastDrawn) { _lastDrawn = drawn; Dispatcher.UIThread.Post(() => GridChanged?.Invoke()); }

            // Peaks span the whole clip (frac 0..1); route through the zoom/scroll axis so
            // zooming magnifies the waveform and only the visible slice is drawn.
            double mid = top + wh / 2;
            ctx.DrawLine(MidPen, new Point(0, mid), new Point(w, mid));
            var adsrF = AdsrFrameNow(w, h);
            var adsrFit = _adsr.Fit(_adsrLen);
            bool shaped = !adsrFit.IsIdentity;
            if (_peaks is not null && _count > 0 && wh > 4)
            {
                // Zoomed in past the overview's resolution → the visible slice's own peaks
                // (re-fetched once the view settles). Until a fresh fetch lands, the previous
                // detail keeps drawing wherever it reaches (it's still far finer than the
                // overview), so zooming never flashes back to the overview's wide blocks.
                var (c0, c1) = VisibleContent(w);
                bool coarse = _zoom * w / _count > DetailPx;
                if (coarse && !DetailCovers(c0, c1, w)) RequestDetail();
                bool useDetail = coarse && _dPeaks is not null && _dCount > 0
                                 && (_dF1 - _dF0) / _dCount < 1.0 / _count && _dF1 > c0 && _dF0 < c1;
                if (!useDetail)
                    DrawPeaks(ctx, _peaks, _count, 0, 1, w, mid, wh, adsrF, adsrFit, shaped);
                else
                {
                    // The overview only outside the detail's span, the detail inside it.
                    double xa = FracToX(_dF0, w), xb = FracToX(_dF1, w);
                    if (xa > xb) (xa, xb) = (xb, xa);
                    if (xa > 0)
                        using (ctx.PushClip(new Rect(0, top, xa, wh)))
                            DrawPeaks(ctx, _peaks, _count, 0, 1, w, mid, wh, adsrF, adsrFit, shaped);
                    if (xb < w)
                        using (ctx.PushClip(new Rect(xb, top, w - xb, wh)))
                            DrawPeaks(ctx, _peaks, _count, 0, 1, w, mid, wh, adsrF, adsrFit, shaped);
                    using (ctx.PushClip(new Rect(Math.Max(0, xa), top, Math.Max(0, Math.Min(w, xb) - Math.Max(0, xa)), wh)))
                        DrawPeaks(ctx, _dPeaks!, _dCount, _dF0, _dF1, w, mid, wh, adsrF, adsrFit, shaped);
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

            // ADSR: in its tab the wave dims under a wash and the shape is drawn in brass with its
            // handles (plus the stage readout while dragging); otherwise a quiet line keeps it in view.
            if (_adsrMode)
            {
                ctx.FillRectangle(EnvWash, new Rect(0, top, w, wh));
                ClipAdsrKit.DrawCurve(ctx, adsrF, adsrFit, EnvPen);
                var hot = _adsrDrag != AdsrHandle.None ? _adsrDrag : _adsrHover;
                ClipAdsrKit.DrawHandles(ctx, adsrF, adsrFit, hot, NodeFill, NodeHot, NodeRing);
                if (_adsrDrag is not (AdsrHandle.None or AdsrHandle.AttackOrSustain))
                {
                    var ft = new FormattedText(ClipAdsrKit.Readout(_adsrDrag, adsrFit, _bpm), NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, ChipInkHot);
                    double cw = ft.Width + 12;
                    var chip = new Rect(Math.Clamp(adsrF.Left + 8, 2, Math.Max(2, w - cw - 2)), top + 6, cw, 16);
                    ctx.DrawRectangle(ChipBgHot, ChipEdgeHot, chip.Deflate(0.5), 3, 3);
                    ctx.DrawText(ft, new Point(chip.X + 6, chip.Y + (16 - ft.Height) / 2));
                }
            }
            else if (shaped && !_envMode)
                ClipAdsrKit.DrawCurve(ctx, adsrF, adsrFit, EnvPenDim);

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
            using var clip = ctx.PushClip(new Rect(0, 0, w, RulerH));   // a label half off-screen stays inside
            double cw = MonoCharW;
            if (_mk.Count > 1)
            {
                // Labels at the smallest step wide enough for its own label text (bars thin
                // to every 2nd/4th/8th… on a long clip); unlabelled ticks at a finer step.
                double tb = TotalBeats;
                double ppb = _zoom * w / tb;
                int barDigits = ((int)Math.Floor(tb / BeatsPerBar) + 1).ToString(NotaNum.Culture).Length;
                double labelStep = BeatSteps[^1];
                foreach (var st in BeatSteps)
                {
                    if (st < 0.25 || st == 2) continue;   // labels: 16ths, half-beats, beats, then bars
                    int chars = st >= BeatsPerBar ? barDigits : st >= 1 ? barDigits + 2 : barDigits + 4;
                    if (st * ppb >= chars * cw + LabelPadPx + 4) { labelStep = st; break; }
                }
                double tickStep = Math.Min(labelStep, BeatStep(ppb, TickMinPx));
                var (b0, b1) = VisibleBeats(w);
                double end = Math.Min(b1 + tickStep, tb + 1e-6);
                for (double b = Math.Max(0, Math.Floor(b0 / tickStep) * tickStep - labelStep); b <= end; b += tickStep)
                {
                    double x = BeatToX(b, w);
                    if (x < -200 || x > w + 0.5) continue;
                    bool labelled = IsMultiple(b, labelStep);
                    bool bar = IsMultiple(b, BeatsPerBar);
                    if (labelled)
                        ctx.DrawLine(bar ? GridBarPen : GridBeatPen, new Point(x + 0.5, 0), new Point(x + 0.5, RulerH));
                    else
                        ctx.DrawLine(GridBeatPen, new Point(x + 0.5, RulerH - 6), new Point(x + 0.5, RulerH));
                    if (!labelled || b >= tb - 1e-6) continue;
                    var ft = new FormattedText(BeatLabel(b, labelStep), NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, bar ? RulerBar : RulerBeat);
                    ctx.DrawText(ft, new Point(_reversed ? x - 4 - ft.Width : x + 4, RulerH - 5 - ft.Height));
                }
                return;
            }
            if (!(_srcMode && _srcTotal > 0 && _srcSR > 0)) return;
            double secs = _srcTotal / _srcSR;
            double pxPerSec = _zoom * w / Math.Max(1e-6, secs);
            double[] steps = { 0.0005, 0.001, 0.002, 0.005, 0.01, 0.02, 0.05, 0.1, 0.25, 0.5, 1, 2, 5, 10, 15, 30, 60, 120, 300, 600, 1200, 1800, 3600 };
            double stepS = steps[^1];
            foreach (var st in steps)
                if (st * pxPerSec >= SecondsLabel(secs, st).Length * cw + LabelPadPx + 4) { stepS = st; break; }
            var (f0, f1) = VisibleContent(w);
            long k0 = Math.Max(0, (long)Math.Floor(f0 * secs / stepS) - 1), k1 = (long)Math.Ceiling(f1 * secs / stepS) + 1;
            for (long k = k0; k <= k1 && k * stepS <= secs + 1e-9; k++)
            {
                double t = k * stepS;
                double x = SrcToX(t * _srcSR, w);
                if (x < -200 || x > w + 0.5) continue;
                ctx.DrawLine(GridBeatPen, new Point(x + 0.5, 0), new Point(x + 0.5, RulerH));
                var ft = new FormattedText(SecondsLabel(t, stepS), NotaNum.Culture, FlowDirection.LeftToRight, GridFace, 9, k == 0 ? RulerBar : RulerBeat);
                ctx.DrawText(ft, new Point(_reversed ? x - 4 - ft.Width : x + 4, RulerH - 5 - ft.Height));
            }
        }

        // Seconds-ruler label: m:ss once a step reaches a minute, else seconds with as many
        // decimals as the step needs.
        private static string SecondsLabel(double t, double step)
        {
            if (step >= 60) { long r = (long)Math.Round(t); return $"{r / 60}:{r % 60:00}"; }
            string fmt = step >= 1 ? "0" : step >= 0.1 ? "0.0#" : step >= 0.01 ? "0.00" : "0.000#";
            return t.ToString(fmt, NotaNum.Culture) + " s";
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
