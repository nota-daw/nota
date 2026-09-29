// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The sample auditioner pinned under the browser's Files tab ("Nota Preview Player",
// strip 1a): the file's name and format, its real waveform in a well (click = seek,
// hover = time tag, a brass playhead while it plays), then Play/Stop, the time readout,
// Loop, Auto (selecting a file plays it) and a preview-volume fader (double-click =
// 0 dB). The waveform is decoded off the UI thread through the engine's background
// import; playback is the engine's audition voice, polled for its playhead while live.
// Auto / Loop / volume persist in Settings. In a narrow browser the strip sheds by need
// (see Adapt): Auto drops its label, the total time goes (the meta line carries it), the
// fader folds into a speaker that pops it up, then Loop, then the time readout.

using System;
using System.Collections.Generic;
using File = System.IO.File;
using BinaryReader = System.IO.BinaryReader;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class PreviewPlayer : UserControl
{
    private const string IconLoop = "M17 3 l3 3 l-3 3 M20 6 H8 a4 4 0 0 0 -4 4 v1 M7 21 l-3 -3 l3 -3 M4 18 h12 a4 4 0 0 0 4 -4 v-1";
    private const string IconSpeaker = "M4 9 h4 l5 -4 v14 l-5 -4 h-4 z";
    private const string WavesMute = "M16 9 l5 6 M21 9 l-5 6";
    private const string WavesLow = "M16 9.5 a3.5 3.5 0 0 1 0 5";
    private const string WavesHigh = "M16 9.5 a3.5 3.5 0 0 1 0 5 M18.5 7 a7 7 0 0 1 0 10";
    private const double DefaultVolume = 0.7;

    private readonly TextBlock _name, _meta, _timeNow, _timeTotal;
    private readonly PreviewWaveform _wave;
    private readonly Border _playBtn, _loopBtn, _autoBtn;
    private readonly Path _playGlyph, _stopGlyph, _loopIcon, _volWaves;
    private readonly Ellipse _autoDot;
    private readonly TextBlock _autoText;
    private readonly PreviewVolume _volume, _volumePop;
    private readonly Border _volIconBtn;
    private readonly StackPanel _time;
    private readonly Flyout _volFlyout;
    private string[] _metaParts = Array.Empty<string>();
    private bool _volCompact;
    private readonly DispatcherTimer _poll;

    private IAudioEngine? _engine;
    private ISettingsService? _settings;
    private BrowserItem? _item;
    private double _duration;         // seconds; 0 until the header is read
    private double _pos;              // seconds into the file
    private bool _playing, _loop, _auto = true;
    private int _loadGen;

    // Decoded overviews by path, so arrowing back through a folder doesn't re-decode.
    private readonly Dictionary<string, FileInfoCache> _cache = new(StringComparer.Ordinal);
    private readonly Queue<string> _cacheOrder = new();
    private const int CacheMax = 64;
    private const int PeakPoints = 512;

    // Meta is the format line split in parts, most important first (length, rate, depth, channels).
    private sealed record FileInfoCache(double Duration, string[] Meta, float[]? Peaks);

    /// <summary>Audition failed or started — for the status line.</summary>
    public event Action<string>? StatusChanged;

    public PreviewPlayer()
    {
        // Row 1: name · format.
        _name = new TextBlock
        {
            Text = "—", FontSize = 12, FontWeight = FontWeight.SemiBold,
            Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _meta = new TextBlock
        {
            FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextTertiary,
            Margin = new Thickness(8, 0, 0, 1), VerticalAlignment = VerticalAlignment.Bottom,
        };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        head.Children.Add(_name);
        Grid.SetColumn(_meta, 1);
        head.Children.Add(_meta);

        // Row 2: the waveform well.
        _wave = new PreviewWaveform { Height = 46 };
        _wave.SeekRequested += f => { if (_item is not null && _duration > 0) Play(f * _duration); };

        // Row 3: transport.
        _playGlyph = new Path
        {
            Data = Geometry.Parse("M1.5 1 L10 6 L1.5 11 Z"), Width = 11, Height = 12,
            StrokeThickness = 1, StrokeJoin = PenLineJoin.Round,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _stopGlyph = new Path
        {
            Data = new RectangleGeometry(new Rect(1, 1, 8, 8), 1, 1), Width = 10, Height = 10,
            Fill = NotaPalette.TextOnAccent, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _playBtn = Chip(new Panel { Children = { _playGlyph, _stopGlyph } }, 28, 28, 5);
        ToolTip.SetTip(_playBtn, "Play / Stop (Space)");
        _playBtn.PointerPressed += (_, e) => { if (IsLeft(e)) { Toggle(); e.Handled = true; } };

        _timeNow = new TextBlock { FontSize = 10, FontFamily = NotaFonts.MonoFamily, VerticalAlignment = VerticalAlignment.Center };
        _timeTotal = new TextBlock { FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextDisabled, VerticalAlignment = VerticalAlignment.Center };
        var time = _time = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Children = { _timeNow, _timeTotal },
        };

        _loopIcon = IconPath(IconLoop);
        _loopBtn = Chip(Icon12(_loopIcon), 22, 22, 4);
        ToolTip.SetTip(_loopBtn, "Loop");
        _loopBtn.PointerPressed += (_, e) => { if (IsLeft(e)) { SetLoop(!_loop, persist: true); e.Handled = true; } };

        _autoDot = new Ellipse { Width = 6, Height = 6, VerticalAlignment = VerticalAlignment.Center };
        _autoText = new TextBlock { Text = "Auto", FontSize = 10, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        _autoBtn = Chip(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { _autoDot, _autoText },
        }, double.NaN, 22, 4);
        _autoBtn.Padding = new Thickness(7, 0);
        ToolTip.SetTip(_autoBtn, "Auto-audition: play on select");
        _autoBtn.PointerPressed += (_, e) => { if (IsLeft(e)) { SetAuto(!_auto, persist: true); e.Handled = true; } };

        var speaker = IconPath(IconSpeaker);
        speaker.Stroke = NotaPalette.TextMuted;
        _volWaves = IconPath(WavesHigh);
        _volWaves.Stroke = NotaPalette.TextMuted;
        _volume = new PreviewVolume { Width = 46, Height = 16, VerticalAlignment = VerticalAlignment.Center };
        _volume.ValueChanged += (v, done) => ApplyVolume(v, persist: done);
        // Folded (narrow browser) the speaker alone stays; a click pops the fader up.
        _volumePop = new PreviewVolume { Width = 110, Height = 16 };
        _volumePop.ValueChanged += (v, done) => ApplyVolume(v, persist: done);
        _volFlyout = new Flyout { Content = _volumePop, Placement = PlacementMode.Top };
        _volIconBtn = new Border
        {
            Width = 12, Height = 22, Background = Brushes.Transparent,
            Child = Icon12(new Panel { Children = { speaker, _volWaves } }),
        };
        _volIconBtn.PointerPressed += (_, e) =>
        {
            if (!_volCompact || !IsLeft(e)) return;
            _volFlyout.ShowAt(_volIconBtn);
            e.Handled = true;
        };
        var vol = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center,
            Children = { _volIconBtn, _volume },
        };
        _volumeBox = vol;

        var controls = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_playBtn, Dock.Left);
        DockPanel.SetDock(time, Dock.Left);
        DockPanel.SetDock(vol, Dock.Right);
        DockPanel.SetDock(_autoBtn, Dock.Right);
        DockPanel.SetDock(_loopBtn, Dock.Right);
        _autoBtn.Margin = new Thickness(6, 0);
        controls.Children.Add(_playBtn);
        controls.Children.Add(time);
        controls.Children.Add(vol);
        controls.Children.Add(_autoBtn);
        controls.Children.Add(_loopBtn);

        Content = new Border
        {
            Background = NotaPalette.SurfaceInset,
            BorderBrush = NotaPalette.BorderDefault,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(10),
            Child = new StackPanel { Spacing = 8, Children = { head, _wave, controls } },
        };

        SizeChanged += (_, _) => Adapt();

        _poll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _poll.Tick += (_, _) => Poll();

        SetLoop(false, persist: false);
        SetAuto(true, persist: false);
        ApplyVolume(DefaultVolume, persist: false);
        Refresh();
    }

    private readonly StackPanel _volumeBox;

    /// <summary>Wire the engine (playback, decode) and the settings (Auto / Loop / volume).</summary>
    public void Attach(IAudioEngine engine, ISettingsService settings)
    {
        _engine = engine;
        _settings = settings;
        SetAuto(settings.Current.BrowserPreviewAuto, persist: false);
        SetLoop(settings.Current.BrowserPreviewLoop, persist: false);
        ApplyVolume(Math.Clamp(settings.Current.BrowserPreviewVolume, 0, 1), persist: false);
    }

    /// <summary>The browser selection moved. A sample loads its waveform (and plays, with Auto
    /// on); anything else empties the player and stops what it was playing.</summary>
    public void SetItem(BrowserItem? item)
    {
        var sample = item is { Kind: BrowserItemKind.Sample } && !string.IsNullOrEmpty(item.Path) ? item : null;
        if (sample is not null && _item is not null && sample.Path == _item.Path) return;
        bool wasPlaying = _playing;
        _item = sample;
        _pos = 0;
        _duration = 0;
        _metaParts = Array.Empty<string>();
        _wave.SetPeaks(null);
        if (sample is null)
        {
            if (wasPlaying) Stop();
            Refresh();
            return;
        }
        LoadInfo(sample.Path);
        if (_auto) Play(0);
        else { if (wasPlaying) Stop(); Refresh(); }
    }

    /// <summary>Space in the Files list: play the selected sample, or stop it.</summary>
    public void Toggle()
    {
        if (_playing) Stop();
        else Play(0);
    }

    public bool HasSample => _item is not null;

    // --- playback -------------------------------------------------------------

    private void Play(double from)
    {
        if (_engine is null || _item is null) return;
        try
        {
            _engine.SetPreviewLoop(_loop);
            _engine.SetPreviewGain((float)(_volume.Value * _volume.Value));
            _engine.PreviewFileAt(_item.Path, Math.Max(0, from));
            _playing = true;
            _pos = from;
            _poll.Start();
            StatusChanged?.Invoke($"Previewing {_item.Name}");
        }
        catch (Exception ex)
        {
            _playing = false;
            StatusChanged?.Invoke($"Preview failed: {ex.Message}");
        }
        Refresh();
    }

    private void Stop()
    {
        try { _engine?.StopPreview(); } catch { /* engine gone */ }
        _playing = false;
        _pos = 0;
        _poll.Stop();
        Refresh();
    }

    private void Poll()
    {
        if (_engine is null || !_playing) { _poll.Stop(); return; }
        if (!_engine.IsPreviewActive)
        {
            // Ran off the end (or something else stopped the audition — a project load).
            _playing = false;
            _pos = 0;
            _poll.Stop();
        }
        else _pos = _engine.PreviewPosition;
        Refresh();
    }

    private void SetLoop(bool on, bool persist)
    {
        _loop = on;
        try { _engine?.SetPreviewLoop(on); } catch { }
        _loopBtn.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
        _loopBtn.BorderBrush = on ? NotaPalette.AccentEdge : NotaPalette.BorderDefault;
        _loopIcon.Stroke = on ? NotaPalette.Accent : NotaPalette.TextTertiary;
        if (persist && _settings is not null) { _settings.Current.BrowserPreviewLoop = on; _settings.Save(); }
    }

    private void SetAuto(bool on, bool persist)
    {
        _auto = on;
        _autoBtn.Background = on ? NotaPalette.AccentSubtle : Brushes.Transparent;
        _autoBtn.BorderBrush = on ? NotaPalette.AccentEdge : NotaPalette.BorderDefault;
        _autoDot.Fill = on ? NotaPalette.Accent : NotaPalette.BorderStrong;
        _autoText.Foreground = on ? NotaPalette.AccentHover : NotaPalette.TextMuted;
        if (persist && _settings is not null) { _settings.Current.BrowserPreviewAuto = on; _settings.Save(); }
    }

    // v is the fader position; the gain is v² (so the travel reads roughly in dB).
    private void ApplyVolume(double v, bool persist)
    {
        v = Math.Clamp(v, 0, 1);
        _volume.Value = v;
        _volumePop.Value = v;
        try { _engine?.SetPreviewGain((float)(v * v)); } catch { }
        _volWaves.Data = Geometry.Parse(v <= 0.001 ? WavesMute : v < 0.5 ? WavesLow : WavesHigh);
        var db = NotaNum.Db(v <= 0.001 ? double.NegativeInfinity : 40 * Math.Log10(v));
        ToolTip.SetTip(_volumeBox, $"Preview volume {db} · double-click for 0{NotaNum.Thin}dB");
        ToolTip.SetTip(_volumePop, $"Preview volume {db} · double-click for 0{NotaNum.Thin}dB");
        if (persist && _settings is not null) { _settings.Current.BrowserPreviewVolume = v; _settings.Save(); }
    }

    private void Refresh()
    {
        bool has = _item is not null;
        _name.Text = _item?.Name ?? "—";
        _playGlyph.IsVisible = !_playing;
        _stopGlyph.IsVisible = _playing;
        _playBtn.Background = _playing ? NotaPalette.Accent : NotaPalette.SurfaceRaised;
        _playBtn.BorderBrush = _playing ? NotaPalette.Accent : NotaPalette.BorderStrong;
        var glyph = has ? NotaPalette.Accent : NotaPalette.TextDisabled;
        _playGlyph.Fill = glyph;
        _playGlyph.Stroke = glyph;
        _playBtn.Cursor = has ? new Cursor(StandardCursorType.Hand) : Cursor.Default;
        _timeNow.Text = Fmt(_pos);
        _timeNow.Foreground = _playing ? NotaPalette.TextPrimary : NotaPalette.TextMuted;
        _timeTotal.Text = "/ " + Fmt(_duration);
        _wave.SetState(_duration, _pos, _playing);
        Adapt();
    }

    // --- narrow widths ------------------------------------------------------------

    // Fits the strip to the browser's width, shedding by need. Header: the name wins and the
    // meta shortens ("0.50 s · 44.1k · 16-bit · M" → "0.50 s · 44.1k" → "0.50 s" → gone).
    // Controls, cheapest loss first: Auto's label (the lit dot still reads), the total time
    // (the meta line has it), the fader (folds into the speaker, which pops it up), Loop, then
    // the time readout. Play, Auto and the speaker always stay.
    private void Adapt()
    {
        double avail = Bounds.Width - 20;   // the strip's padding
        if (avail <= 0) return;

        // Header.
        double nameW = TextW(_name.Text ?? "", NotaFonts.SansSemiBold, 12);
        string meta = "";
        for (int n = _metaParts.Length; n >= 1; n--)
        {
            // A lone "unsupported" (or a full set) is all-or-nothing; the rest drops from the tail.
            if (n < _metaParts.Length && n > 2) continue;   // full, or length · rate, or length
            var m = string.Join(" · ", _metaParts, 0, n);
            if (nameW + 8 + TextW(m, NotaFonts.Mono, 9) <= avail) { meta = m; break; }
        }
        _meta.Text = meta;
        _meta.IsVisible = meta.Length > 0;

        // Controls.
        const double gap = 6, minMiddle = 8;
        double now = TextW(_timeNow.Text ?? "", NotaFonts.Mono, 10);
        double total = 3 + TextW(_timeTotal.Text ?? "", NotaFonts.Mono, 10);
        double autoFull = 2 + 7 + 6 + 5 + TextW("Auto", NotaFonts.SansSemiBold, 10) + 7, autoDot = 22;
        double volFull = 12 + 5 + 46, volIcon = 22, loop = 22 + gap;

        int level = 0;
        for (; level < 5; level++)
        {
            double left = 28 + gap + now + (level < 2 ? total : 0);
            double right = (level < 4 ? loop : 0) + gap + (level < 1 ? autoFull : autoDot) + gap + (level < 3 ? volFull : volIcon);
            if (left + minMiddle + right <= avail) break;
        }
        _autoText.IsVisible = level < 1;
        _autoBtn.Width = level < 1 ? double.NaN : autoDot;
        _autoBtn.Padding = level < 1 ? new Thickness(7, 0) : new Thickness(0);
        _timeTotal.IsVisible = level < 2;
        _volCompact = level >= 3;
        _volume.IsVisible = !_volCompact;
        _volIconBtn.Width = _volCompact ? volIcon : 12;
        _volIconBtn.Cursor = _volCompact ? HandCursor : Cursor.Default;
        _loopBtn.IsVisible = level < 4;
        _time.IsVisible = level < 5;
    }

    private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

    private static double TextW(string s, Typeface tf, double size)
        => s.Length == 0 ? 0 : new FormattedText(s, NotaNum.Culture, FlowDirection.LeftToRight, tf, size, null).Width;

    // "SS.cc" as the mockup prints it; a minute or more gets "M:SS.cc".
    private static string Fmt(double s)
    {
        s = Math.Max(0, s);
        int whole = (int)s, cs = (int)((s - whole) * 100);
        return whole >= 60 ? $"{whole / 60}:{whole % 60:00}.{cs:00}" : $"{whole:00}.{cs:00}";
    }

    // --- waveform + format, decoded off the UI thread ---------------------------

    private void LoadInfo(string path)
    {
        int gen = ++_loadGen;
        if (_cache.TryGetValue(path, out var hit))
        {
            ShowInfo(hit);
            if (hit.Peaks is not null) return;
        }
        var engine = _engine;
        if (engine is null) return;
        Task.Run(() =>
        {
            IAudioImport? job = null;
            try
            {
                job = engine.OpenAudioImport(path);
                if (job is null) { Post(gen, path, new FileInfoCache(0, new[] { "unsupported" }, null), final: true); return; }
                double dur = job.SampleRate > 0 ? job.TotalFrames / job.SampleRate : 0;
                var meta = FormatMeta(path, dur, job.SampleRate, job.Channels);
                Post(gen, path, new FileInfoCache(dur, meta, null), final: false);
                var peaks = new float[PeakPoints * 2];
                var sw = System.Diagnostics.Stopwatch.StartNew();
                while (true)
                {
                    if (Volatile.Read(ref _loadGen) != gen) return;   // selection moved on
                    int r = job.Step(1 << 16);
                    if (r < 0) return;
                    bool done = r == 0;
                    // Long files fill in progressively (every ~120 ms) rather than all at the end.
                    if (done || sw.ElapsedMilliseconds > 120)
                    {
                        int n = job.ReadPeaks(peaks, PeakPoints);
                        Post(gen, path, new FileInfoCache(dur, meta, ToAmplitudes(peaks, n)), final: done);
                        sw.Restart();
                    }
                    if (done) return;
                }
            }
            catch { /* unreadable file: leave the well empty */ }
            finally { job?.Dispose(); }
        });
    }

    private void Post(int gen, string path, FileInfoCache info, bool final)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (final) Remember(path, info);
            if (gen == _loadGen && _item?.Path == path) ShowInfo(info);
        });
    }

    private void ShowInfo(FileInfoCache info)
    {
        _duration = info.Duration;
        _metaParts = info.Meta;
        _wave.SetPeaks(info.Peaks);
        Refresh();
    }

    private void Remember(string path, FileInfoCache info)
    {
        if (!_cache.ContainsKey(path))
        {
            _cacheOrder.Enqueue(path);
            while (_cacheOrder.Count > CacheMax) _cache.Remove(_cacheOrder.Dequeue());
        }
        _cache[path] = info;
    }

    // (min,max) pairs → peak magnitude per bucket; undecoded buckets (min > max) read as 0.
    private static float[] ToAmplitudes(float[] minMax, int n)
    {
        var a = new float[n];
        for (int i = 0; i < n; i++)
        {
            float mn = minMax[2 * i], mx = minMax[2 * i + 1];
            a[i] = mn > mx ? 0 : Math.Max(Math.Abs(mn), Math.Abs(mx));
        }
        return a;
    }

    // "0.34 s · 44.1k · 24-bit · M"; the bit depth only where the header says (WAV).
    private static string[] FormatMeta(string path, double dur, double sr, int channels)
    {
        var parts = new List<string>
        {
            // Truncated like the time readout, so "1.89 s" never meets "/ 01.89" rounded up to 1.90.
            dur >= 60 ? Fmt(dur) : NotaNum.Unit(Math.Floor(dur * 100) / 100, "0.00", "s"),
            NotaNum.Str(sr / 1000, "0.#") + "k",
        };
        var bits = WavBitDepth(path);
        if (bits is not null) parts.Add(bits);
        parts.Add(channels == 1 ? "M" : channels == 2 ? "S" : channels + "ch");
        return parts.ToArray();
    }

    private static string? WavBitDepth(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            using var br = new BinaryReader(fs);
            if (fs.Length < 12) return null;
            var riff = new string(br.ReadChars(4));
            br.ReadUInt32();
            if ((riff != "RIFF" && riff != "RF64") || new string(br.ReadChars(4)) != "WAVE") return null;
            while (fs.Position + 8 <= fs.Length)
            {
                var id = new string(br.ReadChars(4));
                uint size = br.ReadUInt32();
                if (id == "fmt " && size >= 16)
                {
                    ushort format = br.ReadUInt16();
                    br.ReadBytes(12);                     // channels, rate, byte rate, block align
                    ushort bits = br.ReadUInt16();
                    if (format == 0xFFFE && size >= 26)   // WAVE_FORMAT_EXTENSIBLE: the sub-format decides
                    {
                        br.ReadBytes(8);
                        format = br.ReadUInt16();
                    }
                    return format == 3 ? $"{bits}-bit float" : $"{bits}-bit";
                }
                fs.Position += size + (size & 1);
            }
        }
        catch { }
        return null;
    }

    // --- small builders ---------------------------------------------------------

    private static bool IsLeft(PointerPressedEventArgs e) => e.GetCurrentPoint(null).Properties.IsLeftButtonPressed;

    private static Border Chip(Control child, double w, double h, double radius) => new()
    {
        Width = w, Height = h,
        CornerRadius = new CornerRadius(radius),
        BorderThickness = new Thickness(1),
        Cursor = new Cursor(StandardCursorType.Hand),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new Panel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Children = { child } },
    };

    // A stroke glyph on the design's 24-unit grid (stroke 2 → 1px at 12px).
    private static Path IconPath(string data) => new()
    {
        Data = Geometry.Parse(data), Width = 24, Height = 24, Stretch = Stretch.None,
        StrokeThickness = 2, StrokeJoin = PenLineJoin.Round, StrokeLineCap = PenLineCap.Round,
    };

    private static Viewbox Icon12(Control grid24) => new()
    {
        Width = 12, Height = 12, Stretch = Stretch.Uniform,
        Child = new Panel { Width = 24, Height = 24, Children = { grid24 } },
    };
}

/// <summary>The player's waveform well: peak bars (played part in brass), a hover line with
/// its time, and the playhead. A click asks to seek to that fraction of the file.</summary>
public sealed class PreviewWaveform : Control
{
    private const double InsetX = 4, InsetY = 3;
    private float[]? _peaks;
    private double _duration, _pos;
    private bool _playing;
    private double? _hover;   // 0..1

    public event Action<double>? SeekRequested;

    public PreviewWaveform()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        ClipToBounds = true;
    }

    public void SetPeaks(float[]? peaks) { _peaks = peaks; InvalidateVisual(); }

    public void SetState(double duration, double pos, bool playing)
    {
        _duration = duration; _pos = pos; _playing = playing;
        InvalidateVisual();
    }

    private double Frac(Point p) => Math.Clamp((p.X - InsetX) / Math.Max(1, Bounds.Width - 2 * InsetX), 0, 1);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        SeekRequested?.Invoke(Frac(e.GetPosition(this)));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _hover = _duration > 0 ? Frac(e.GetPosition(this)) : null;
        InvalidateVisual();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _hover = null;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;

        var well = new Rect(0.5, 0.5, w - 1, h - 1);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.GraphBorder, 1), well, 4, 4);
        ctx.FillRectangle(NotaPalette.GridBeat, new Rect(1, Math.Floor(h / 2), w - 2, 1));

        double innerW = w - 2 * InsetX, innerH = h - 2 * InsetY, mid = h / 2;
        double p = _duration > 0 ? Math.Clamp(_pos / _duration, 0, 1) : 0;
        bool started = _playing || _pos > 0;

        if (_peaks is { Length: > 0 } peaks && innerW > 8)
        {
            // ~3.8px a bar with a 1px gap, as in the mockup (72 bars across its 274px well).
            int n = Math.Max(8, (int)((innerW + 1) / 3.8));
            double bw = (innerW - (n - 1)) / n;
            for (int b = 0; b < n; b++)
            {
                int s0 = b * peaks.Length / n, s1 = Math.Max(s0 + 1, (b + 1) * peaks.Length / n);
                float m = 0;
                for (int i = s0; i < s1 && i < peaks.Length; i++) m = Math.Max(m, peaks[i]);
                double amp = Math.Max(0.04, Math.Pow(Math.Min(1, m), 0.55));
                double x = (b + 0.5) / n;
                IBrush c = started && x <= p ? NotaPalette.Accent
                         : _hover is { } hv && x <= hv && x > p ? NotaPalette.AccentMute
                         : _playing ? NotaPalette.AccentEdge
                         : NotaPalette.AccentMute;
                double bh = innerH * amp;
                ctx.FillRectangle(c, new Rect(InsetX + b * (bw + 1), mid - bh / 2, bw, bh), 1);
            }
        }

        if (_hover is { } f)
        {
            double hx = InsetX + innerW * f;
            ctx.FillRectangle(NotaPalette.TextTertiary, new Rect(Math.Round(hx), 0, 1, h));
            var ft = new FormattedText(Fmt(f * _duration), NotaNum.Culture, FlowDirection.LeftToRight,
                                       NotaFonts.Mono, 8, NotaPalette.TextPrimary);
            double tw = ft.Width + 8, th = ft.Height + 2;
            double tx = f > 0.75 ? hx - tw - 4 : hx + 4;
            ctx.FillRectangle(NotaPalette.TrackOff, new Rect(tx, InsetY, tw, th), 2);
            ctx.DrawText(ft, new Point(tx + 4, InsetY + 1));
        }

        if (started && _duration > 0)
            ctx.FillRectangle(NotaPalette.AccentBright, new Rect(InsetX + innerW * p - 0.75, 0, 1.5, h));
    }

    private static string Fmt(double s)
    {
        s = Math.Max(0, s);
        int whole = (int)s, cs = (int)((s - whole) * 100);
        return whole >= 60 ? $"{whole / 60}:{whole % 60:00}.{cs:00}" : $"{whole:00}.{cs:00}";
    }
}

/// <summary>The preview-volume fader: a thin track, a fill to the value and a small bar
/// thumb. Drag anywhere to set; double-click resets to 0 dB (1.0).</summary>
public sealed class PreviewVolume : Control
{
    private double _value = 0.7;
    private bool _dragging;

    /// <summary>(value, gesture finished) — persist on the second.</summary>
    public event Action<double, bool>? ValueChanged;

    public double Value
    {
        get => _value;
        set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); }
    }

    public PreviewVolume()
    {
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        DoubleTapped += (_, e) => { Value = 1; ValueChanged?.Invoke(1, true); e.Handled = true; };
    }

    private void SetFrom(Point p)
    {
        Value = p.X / Math.Max(1, Bounds.Width);
        ValueChanged?.Invoke(_value, false);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        _dragging = true;
        e.Pointer.Capture(this);
        SetFrom(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragging) SetFrom(e.GetPosition(this));
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_dragging) return;
        _dragging = false;
        e.Pointer.Capture(null);
        ValueChanged?.Invoke(_value, true);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width;
        if (w <= 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));   // whole box hit-tests
        ctx.FillRectangle(NotaPalette.BorderDefault, new Rect(0, 7, w, 3), 2);
        double x = w * _value;
        ctx.FillRectangle(NotaPalette.TextSecondary, new Rect(0, 7, x, 3), 2);
        ctx.FillRectangle(NotaPalette.TextPrimary, new Rect(x - 2, 3, 4, 11), 1);
    }
}
