// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings (nota-design/Nota Settings.html): 880×640 with a 188px sidebar grouped as
// Devices (Audio / MIDI / Gamepads / Remote), Plug-ins (Plug-ins / Downloads: plug-ins + sample packs)
// and General (Library / Appearance / Shortcuts), and a content pane per section under a
// title + subtitle header. Audio device / sample-rate / buffer are persisted natively (audio.json)
// and applied by restarting the backend (MainWindowViewModel.ApplyAudioSettings); scan
// folders go through the catalog. Test: a 440 Hz sine through the master (toggle; stopped
// when the pane or window goes away) and a 5 s CPU check sampling the live DSP load +
// dropout count.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class PreferencesWindow : NotaWindow
{
    private static readonly IBrush Ground = NotaPalette.SurfaceCard;
    private static readonly IBrush Sidebar = NotaPalette.Panel;
    private static readonly IBrush RowBg = NotaPalette.Panel;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush Hover = NotaPalette.SurfaceHover;
    private static readonly IBrush Divider = NotaPalette.BorderDefault;
    private static readonly IBrush Hairline = NotaPalette.GraphBorder;   // Brush.Hairline — rules between rows
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentHover = NotaPalette.AccentHover;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush Success = NotaPalette.Success;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextMuted = NotaPalette.TextMuted;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;
    private static readonly IBrush TextDisabled = NotaPalette.TextDisabled;

    private const double LabelCol = 150;   // the label column every form row shares

    // Sidebar: group heading, then its pages. Icons are Glyph kinds drawn at 14px.
    private sealed record Page(string Title, string Subtitle, GlyphKind Icon);

    private static readonly (string Group, Page[] Pages)[] Nav =
    {
        ("Devices", new[]
        {
            new Page("Audio", "Device, sample rate and buffer", GlyphKind.Levels),
            new Page("MIDI", "Controllers Nota listens to", GlyphKind.MidiPort),
            new Page("Gamepads", "Play notes or drive mapped controls", GlyphKind.Gamepad),
            new Page("Remote", "Phones and tablets on this network", GlyphKind.Phone),
        }),
        ("Plug-ins", new[]
        {
            new Page("Plug-ins", "Where Nota looks for VST3", GlyphKind.Plug),
            new Page("Downloads", "Plug-ins, sample packs and AI models", GlyphKind.Downloads),
        }),
        ("General", new[]
        {
            new Page("Library", "Content folders and version history", GlyphKind.Library),
            new Page("Appearance", "Theme, waveforms and AI control", GlyphKind.Appearance),
            new Page("Shortcuts", "Keyboard reference", GlyphKind.Shortcuts),
        }),
    };


    private readonly MainWindowViewModel? _main;

    private readonly IPluginCatalog _catalog = App.Services.GetRequiredService<IPluginCatalog>();
    private readonly IAudioDeviceService _audioDevices = App.Services.GetRequiredService<IAudioDeviceService>();
    private readonly IMidiDeviceService _midiDevices = App.Services.GetRequiredService<IMidiDeviceService>();
    private readonly IGamepadService _gamepads = App.Services.GetRequiredService<IGamepadService>();

    private static readonly double[] SampleRates = { 0, 44100, 48000, 88200, 96000 };
    private static readonly int[] BufferSizes = { 0, 64, 128, 256, 512, 1024, 2048 };
    private readonly List<string> _outputUids = new();
    private readonly List<string> _inputUids = new();

    private bool _loading;
    private TextBlock? _latencyValue, _latencyDetail;

    private const double TestToneHz = 440;
    private const int CpuCheckTicks = 50;   // × 100 ms = 5 s
    private bool _toneOn;
    private DispatcherTimer? _cpuTimer;

    private int _selFolder;
    private string _shortcutFilter = "";

    private readonly ContentControl _content = new();
    private readonly List<NavItem> _navItems = new();
    private int _selected = -1;

    private sealed class NavItem
    {
        public required Border Root;
        public required Border Edge;
        public required Glyph Icon;
        public required TextBlock Label;
        public required TextBlock Badge;
    }

    public PreferencesWindow(SettingsViewModel vm, MainWindowViewModel? main = null)
    {
        _ = vm;   // toolbar-side pref retired with the fixed 1b layout
        _main = main;
        Title = "Settings";
        Width = 880;
        Height = 640;
        Background = Ground;

        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(10, 12) };
        int index = 0;
        foreach (var (group, pages) in Nav)
        {
            nav.Children.Add(new TextBlock
            {
                Text = group, FontSize = 9, FontWeight = FontWeight.Medium,
                Foreground = TextDisabled, Margin = new Thickness(10, 12, 10, 5),
            });
            foreach (var page in pages) nav.Children.Add(BuildNavItem(page, index++));
        }
        var sidebar = new Border
        {
            Width = 188, Background = Sidebar, BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 1, 0), Child = nav,
        };

        // The download dock is its own row under the scrolled pane: it shortens the pane
        // instead of covering its last rows, and stays put while the user scrolls or switches panes.
        var column = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        column.Children.Add(_content);
        var dock = StoreDock();
        Grid.SetRow(dock, 1);
        column.Children.Add(dock);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(sidebar);
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        SetBody(grid);

        // A running download outlives the window (DownloadJobs); only stop listening to it.
        Jobs.Changed += RenderStoreLists;
        Jobs.ProgressChanged += UpdateDock;
        Closed += (_, _) => { StopTests(); Jobs.Changed -= RenderStoreLists; Jobs.ProgressChanged -= UpdateDock; };
        Select(0);
        EnsureStoreLoading();
        UpdateDock();
    }

    /// <summary>Switch to Downloads, on the given source (0 plug-ins, 1 sample packs, 2 AI models).</summary>
    public void ShowDownloads(int source)
    {
        _downloadSource = Math.Clamp(source, 0, DownloadSources.Length - 1);
        Select(DownloadsIndex);
    }

    private static IEnumerable<Page> Pages => Nav.SelectMany(g => g.Pages);

    /// <summary>Switches to the page with this title ("Audio", "Shortcuts" …) — the palette's
    /// Settings: … commands.</summary>
    public void ShowPage(string title)
    {
        int i = 0;
        foreach (var p in Pages) { if (p.Title == title) { Select(i); return; } i++; }
    }

    private Control BuildNavItem(Page page, int idx)
    {
        var edge = new Border
        {
            Width = 2, CornerRadius = NotaRadius.Bar, Margin = new Thickness(0, 7),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
        };
        var icon = new Glyph(page.Icon, 14);
        var label = new TextBlock { Text = page.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var badge = new TextBlock
        {
            FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(10, 0) };
        row.Children.Add(icon);
        Grid.SetColumn(label, 1); row.Children.Add(label);
        Grid.SetColumn(badge, 2); row.Children.Add(badge);

        var root = new Border
        {
            Height = 28, CornerRadius = NotaRadius.Tile, Background = Brushes.Transparent,
            Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Panel { Children = { row, edge } },
        };
        root.PointerPressed += (_, e) => { if (e.GetCurrentPoint(root).Properties.IsLeftButtonPressed) Select(idx); };
        root.PointerEntered += (_, _) => { if (idx != _selected) root.Background = Hover; };
        root.PointerExited += (_, _) => { if (idx != _selected) root.Background = Brushes.Transparent; };
        _navItems.Add(new NavItem { Root = root, Edge = edge, Icon = icon, Label = label, Badge = badge });
        return root;
    }

    private void Select(int index)
    {
        StopTests();   // the Audio pane is rebuilt on every visit — don't leave a tone / check orphaned
        _selected = index;
        for (int i = 0; i < _navItems.Count; i++)
        {
            bool on = i == index;
            var n = _navItems[i];
            n.Root.Background = on ? NotaPalette.SurfaceHover : Brushes.Transparent;
            n.Edge.Background = on ? Brass : Brushes.Transparent;
            n.Icon.Foreground = on ? Brass : TextTertiary;
            n.Label.Foreground = on ? TextPrimary : TextSecondary;
            n.Label.FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }

        var page = Pages.ElementAt(index);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = page.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Bottom });
        header.Children.Add(new TextBlock { Text = page.Subtitle, FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 2) });

        if (index == DownloadsIndex) { _content.Content = DownloadsPane(header); return; }

        // Padding lives inside the scrolled content: a ScrollViewer's own Padding isn't part of
        // its extent, so the last rows of a long pane (Shortcuts) couldn't be scrolled into view.
        _content.Content = new ScrollViewer
        {
            Content = new Border
            {
                Padding = new Thickness(32, 26, 32, 40),
                Child = new StackPanel { Children = { header, BuildPane(index) } },
            },
        };
    }

    // The Downloads item carries the number of registry plugins not installed yet.
    private void UpdateDownloadsBadge(int count)
    {
        var badge = _navItems[DownloadsIndex].Badge;
        badge.Text = count.ToString();
        badge.IsVisible = count > 0;
    }

    private Control BuildPane(int index) => index switch
    {
        0 => AudioPane(),
        1 => MidiPane(),
        2 => GamepadsPane(),
        RemoteIndex => RemotePane(),
        4 => PluginsPane(),
        6 => LibraryPane(),
        7 => AppearancePane(),
        _ => ShortcutsPane(),
    };

    // ---- Audio ------------------------------------------------------------

    private Control AudioPane()
    {
        if (_main is null) return Caption("Audio settings are unavailable in this window.");

        _loading = true;
        var engine = _main.Engine;
        var cfg = engine.GetAudioConfig();

        var outputCombo = Combo(280);
        FillDeviceCombo(outputCombo, _outputUids, _audioDevices.OutputDevices(), cfg.OutputUid);
        outputCombo.SelectionChanged += (_, _) => { int i = outputCombo.SelectedIndex; if (i >= 0 && i < _outputUids.Count) { engine.SetAudioOutputDevice(_outputUids[i]); Apply(); } };

        var inputCombo = Combo(280);
        FillDeviceCombo(inputCombo, _inputUids, _audioDevices.InputDevices(), cfg.InputUid);
        inputCombo.SelectionChanged += (_, _) => { int i = inputCombo.SelectedIndex; if (i >= 0 && i < _inputUids.Count) { engine.SetAudioInputDevice(_inputUids[i]); Apply(); } };

        var srCombo = Combo(150);
        foreach (var sr in SampleRates) srCombo.Items.Add(sr == 0 ? "Device default" : NotaNum.Unit(sr, "0", "Hz"));
        srCombo.SelectedIndex = IndexOf(SampleRates, cfg.SampleRate);
        srCombo.SelectionChanged += (_, _) => { int i = srCombo.SelectedIndex; if (i >= 0) { engine.SetAudioSampleRate(SampleRates[i]); Apply(); } };

        var bufCombo = Combo(150);
        foreach (var b in BufferSizes) bufCombo.Items.Add(b == 0 ? "Device default" : b.ToString());
        bufCombo.SelectedIndex = IndexOf(BufferSizes, cfg.BufferFrames);
        bufCombo.SelectionChanged += (_, _) => { int i = bufCombo.SelectedIndex; if (i >= 0) { engine.SetAudioBufferFrames(BufferSizes[i]); Apply(); } };

        _latencyValue = new TextBlock { FontSize = 12, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary };
        _latencyDetail = new TextBlock { FontSize = 12, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        UpdateLatency();
        var latency = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _latencyValue, _latencyDetail } };

        var device = new List<Control>
        {
            Row("Output", outputCombo), Row("Input", inputCombo),
            Row("Sample rate", srCombo), Row("Buffer size", bufCombo),
        };
        // WASAPI exclusive (Windows only): lets Nota own the output device for the lowest
        // latency, at the cost of muting other apps. The device then dictates the sample rate /
        // buffer, so those are ignored while it's on. If the device refuses exclusive (busy /
        // not allowed), the backend falls back to shared mode and the latency line says so.
        if (OperatingSystem.IsWindows())
        {
            device.Add(Row("Exclusive mode", SwitchRow("WASAPI exclusive — lowest latency, mutes other apps", cfg.WasapiExclusive, on =>
            {
                if (_loading) return;
                engine.SetAudioWasapiExclusive(on);
                Apply();
            })));
            device.Add(Row("", Caption("The device's native sample rate and buffer size are used instead of the ones above. If it is busy or doesn't allow exclusive access, Nota falls back to shared mode.")));
        }
        device.Add(Row("Latency", latency));

        var body = Sections(
            Section("Audio device", 6, device.ToArray()),
            Section("Test", 8, TestToneRow(), CpuCheckRow(engine)));
        _loading = false;
        return body;
    }

    private Control TestToneRow()
    {
        var button = Btn("Play test tone");
        button.Width = 132;
        var note = Caption($"{TestToneHz:0}\u2009Hz sine at −14\u2009dBFS, through the master.");
        button.Click += (_, _) =>
        {
            SetTone(!_toneOn);
            button.Content = _toneOn
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new Glyph(GlyphKind.Stop, 8), new TextBlock { Text = "Stop tone" } } }
                : "Play test tone";
            button.Classes.Set("primary", _toneOn);
        };
        return TestRow("Output check", button, note);
    }

    private void SetTone(bool on)
    {
        if (_main is null || _toneOn == on) return;
        _toneOn = on;
        if (on) _main.Engine.SetFrequency((float)TestToneHz);
        _main.Engine.SetTestTone(on);
    }

    // Samples the smoothed DSP load (render time / block budget) every 100 ms for 5 s and
    // counts dropouts over the same window, then gives a verdict for the current buffer size.
    private Control CpuCheckRow(IAudioEngine engine)
    {
        var button = Btn("CPU check");
        button.Width = 132;
        var result = Caption("Measures audio-thread load for 5\u2009s — play your project for a real-world figure.");
        button.Click += (_, _) =>
        {
            if (_cpuTimer is not null) return;
            if (engine.NegotiatedSampleRate <= 0)
            {
                result.Text = "Audio device not running.";
                result.Foreground = NotaPalette.Danger;
                return;
            }
            button.Content = "Measuring…";
            result.Foreground = TextTertiary;
            int ticks = 0, xrunsStart = engine.XrunCount;
            double sum = 0, peak = 0;
            _cpuTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _cpuTimer.Tick += (_, _) =>
            {
                double load = Math.Clamp(engine.CpuLoad, 0, 1);
                sum += load; peak = Math.Max(peak, load); ticks++;
                if (ticks < CpuCheckTicks)
                {
                    result.Text = $"Measuring… {(CpuCheckTicks - ticks + 9) / 10}\u2009s left · now {load * 100:0}\u2009%";
                    return;
                }
                StopCpuCheck();
                button.Content = "CPU check";
                int xruns = Math.Max(0, engine.XrunCount - xrunsStart);
                var (verdict, brush) = peak > 0.9 || xruns > 0
                    ? ("overloaded — raise the buffer size", NotaPalette.Danger)
                    : peak > 0.7
                        ? ("close to the limit — consider a larger buffer", NotaPalette.Warning)
                        : ("plenty of headroom", TextSecondary);
                result.Text = $"Last check: {sum / ticks * 100:0}\u2009% average, {peak * 100:0}\u2009% peak · {xruns} dropout{(xruns == 1 ? "" : "s")} — {verdict}.";
                result.Foreground = brush;
            };
            result.Text = "Measuring… 5\u2009s left — play your project for a real-world figure.";
            _cpuTimer.Start();
        };
        return TestRow("Performance", button, result);
    }

    private void StopCpuCheck()
    {
        _cpuTimer?.Stop();
        _cpuTimer = null;
    }

    private void StopTests()
    {
        SetTone(false);
        StopCpuCheck();
    }

    private void Apply()
    {
        if (_loading || _main is null) return;
        _main.ApplyAudioSettings();
        UpdateLatency();
    }

    // "11.6 ms" · "512 frames @ 44.1 kHz" — the negotiated block, not the requested one.
    private void UpdateLatency()
    {
        if (_main is null || _latencyValue is null || _latencyDetail is null) return;
        double sr = _main.Engine.NegotiatedSampleRate;
        int buf = _main.Engine.NegotiatedBufferFrames;
        string mode = OperatingSystem.IsWindows() && _main.Engine.AudioExclusiveFallback
            ? " · exclusive refused → shared"
            : OperatingSystem.IsWindows() && _main.Engine.GetAudioConfig().WasapiExclusive ? " · exclusive" : "";
        string rate = NotaNum.Unit(sr / 1000, "0.#", "kHz");
        (_latencyValue.Text, _latencyDetail.Text) = sr <= 0
            ? ("", "Audio device not running.")
            : buf > 0
                ? (NotaNum.Unit(buf / sr * 1000, "0.0", "ms"), $"{buf} frames @ {rate}{mode}")
                : (rate, mode.TrimStart(' ', '·'));
        _latencyValue.IsVisible = _latencyValue.Text.Length > 0;
    }

    // ---- MIDI -------------------------------------------------------------

    private Control MidiPane()
    {
        if (_main is null) return Caption("MIDI settings are unavailable in this window.");

        var engine = _main.Engine;
        var devices = _midiDevices.InputDevices();
        var items = new List<Control> { };
        if (devices.Count == 0)
            items.Add(EmptyBox("No MIDI inputs detected. Connect a controller and reopen Settings."));
        else
        {
            var rows = new List<Control>();
            foreach (var dev in devices)
            {
                string uid = dev.Uid;
                bool on = engine.IsMidiInputEnabled(uid);
                var dot = Dot(on ? NotaPalette.Success : BorderStrong);   // enabled: positive status
                var name = new TextBlock
                {
                    Text = dev.Name, FontSize = 12, Foreground = on ? TextPrimary : TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var sw = new ToggleSwitch(on, large: true) { VerticalAlignment = VerticalAlignment.Center };
                sw.Changed += v =>
                {
                    dot.Fill = v ? NotaPalette.Success : BorderStrong;
                    name.Foreground = v ? TextPrimary : TextTertiary;
                    engine.SetMidiInputEnabled(uid, v);
                    _main.ApplyMidiSettings();
                };
                var row = ListRow(40, "Auto,*,Auto", dot, name, sw);
                row.Cursor = new Cursor(StandardCursorType.Hand);
                row.PointerPressed += (_, e) => { if (e.GetCurrentPoint(row).Properties.IsLeftButtonPressed && !e.Handled) sw.Toggle(); };
                rows.Add(row);
            }
            items.Add(Table(rows));
        }
        items.Add(Caption("Switch a controller off to stop Nota listening to it. New devices are on by default."));
        return Sections(Section("MIDI inputs", 8, items.ToArray()), MpeSection(engine));
    }

    private static readonly int[] MpeBendRanges = [12, 24, 48, 96];

    // MPE: each note on its own channel (2–16) with its own bend / pressure / slide. Channel 1
    // stays an ordinary keyboard, so turning this on costs a plain controller nothing.
    private Control MpeSection(IAudioEngine engine)
    {
        var (on, range) = engine.GetMpe();
        var rangeCombo = Combo(150);
        foreach (var r in MpeBendRanges) rangeCombo.Items.Add($"±{r} semitones");
        int idx = Array.IndexOf(MpeBendRanges, range);
        rangeCombo.SelectedIndex = idx >= 0 ? idx : Array.IndexOf(MpeBendRanges, 48);
        rangeCombo.IsEnabled = on;
        rangeCombo.SelectionChanged += (_, _) =>
        {
            int k = rangeCombo.SelectedIndex;
            if (k < 0) return;
            engine.SetMpe(engine.GetMpe().Enabled, MpeBendRanges[k]);
            _main!.ApplyMidiSettings();
        };
        var sw = SwitchRow("Per-note expression from MPE controllers", on, v =>
        {
            rangeCombo.IsEnabled = v;
            engine.SetMpe(v, engine.GetMpe().BendRange);
            _main!.ApplyMidiSettings();
        });
        return Section("MPE", 8,
            Row("Expression", sw),
            Row("Bend range", rangeCombo),
            Row("", Caption("Channels 2–16 each carry one note with its own pitch bend, pressure and slide (CC74). " +
                            "A controller that announces its range overrides this one. Channel 1 plays as an ordinary keyboard, " +
                            "its pitch wheel and aftertouch moving the whole instrument. Nota Synth, Volt, Aurora, Operator, " +
                            "Pentad and Physical play per-note expression.")));
    }

    // ---- Gamepads -----------------------------------------------------------

    private Control GamepadsPane()
    {
        if (_main is null) return Caption("Gamepad settings are unavailable in this window.");
        if (!OperatingSystem.IsMacOS()) return Caption("Gamepad input is macOS-only in this build.");

        // The master switch lives in Settings (not the native engine): the pad thread
        // always runs once started so new pads are still detected, but button edges only
        // reach the engine while this is on.
        var enable = SwitchRow("Use a connected gamepad for notes and mapped controls", _main.Settings.Current.GamepadEnabled, on =>
        {
            _main.Settings.Current.GamepadEnabled = on;
            _main.Settings.Save();
        });

        // Connected pads, refreshed by the service hot-plug event (the UI tick raises it when
        // the pad count changes). Each row shows a live-connection dot, the pad name, and a
        // "Last" readout that flashes on every button press so the user can confirm the pad is
        // wired up before they record.
        var padHost = new ContentControl();
        var lastLabels = new Dictionary<int, TextBlock>();      // pad slot → activity readout
        var flashTimers = new Dictionary<int, DispatcherTimer>(); // pad slot → fade-back timer

        void RebuildPads()
        {
            lastLabels.Clear();
            var pads = _gamepads.Pads();
            if (pads.Count == 0)
            {
                padHost.Content = EmptyBox("No gamepad detected. Connect one and it appears here.");
                return;
            }
            var rows = new List<Control>();
            for (int i = 0; i < pads.Count; i++)
            {
                var last = new TextBlock
                {
                    Text = "—", FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right,
                };
                lastLabels[i] = last;
                var name = new TextBlock
                {
                    Text = pads[i].Name, FontSize = 12, Foreground = TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var cap = new TextBlock { Text = "Last", FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
                rows.Add(ListRow(40, "Auto,*,Auto,Auto", Dot(Success), name, cap, last));
            }
            padHost.Content = Table(rows);
        }
        RebuildPads();
        _gamepads.PadsChanged += RebuildPads;

        // Flash the matching row's readout on each press, then fade it back so a
        // held stream of edges keeps it lit.
        void OnActivity(int pad, string label)
        {
            if (!lastLabels.TryGetValue(pad, out var tb)) return;
            tb.Text = label;
            tb.Foreground = Brass;
            if (!flashTimers.TryGetValue(pad, out var timer))
            {
                int slot = pad;
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    if (lastLabels.TryGetValue(slot, out var t)) t.Foreground = TextSecondary;
                };
                flashTimers[pad] = timer;
            }
            timer.Stop();
            timer.Start();
        }
        _gamepads.Activity += OnActivity;

        // Detach when the window closes so a dead pane never receives ticks.
        Closed += (_, _) =>
        {
            _gamepads.PadsChanged -= RebuildPads;
            _gamepads.Activity -= OnActivity;
            foreach (var t in flashTimers.Values) t.Stop();
        };

        var map = new List<Control>();
        foreach (var (input, action) in new[]
                 {
                     ("Face buttons", "C4  D4  E4  F4"), ("Shoulder buttons", "G4  A4  B4  C5"),
                     ("D-pad up / down", "Octave −1 / +1"), ("D-pad left / right", "Velocity − / +"),
                 })
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*") };
            grid.Children.Add(new TextBlock { Text = input, FontSize = 12, Foreground = TextSecondary, Margin = new Thickness(12, 9) });
            var a = new TextBlock { Text = action, FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary, Margin = new Thickness(12, 9), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(a, 1);
            grid.Children.Add(a);
            map.Add(grid);
        }

        return Sections(
            Section("Gamepad input", 10, enable, padHost),
            Section("Layout", 10,
                Table(map),
                Caption("Notes go to the armed (record-enabled) instrument track — enable Record to capture them, or use the audition track to just play.", muted: true),
                Caption("Any button can be mapped to a control instead: click MIDI in the top-right, click the control, then press the button. A mapped button drives that control and stops playing its note; the rest of the pad keeps the layout above. The sticks and the trigger travel map the same way and are continuous, so they suit a knob or a fader — they play no notes, and do nothing until mapped. Mappings are listed in the browser's MIDI Map tab and travel with the project.")));
    }

    // ---- Plug-ins ---------------------------------------------------------

    private Control PluginsPane()
    {
        var list = new StackPanel();
        var well = new Border
        {
            Background = Sunken, BorderBrush = Divider, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Panel, Padding = new Thickness(4), MinHeight = 132, Child = list,
        };
        var addBtn = Btn("Add folder…");
        var removeBtn = Btn("Remove");

        // Nota's own install folder (Downloads) is always searched — it is re-added at startup,
        // so it is tagged and can't be removed.
        bool IsDefault(string? path) => string.Equals(path, _store.PluginsDir, StringComparison.Ordinal);

        void RenderFolders()
        {
            list.Children.Clear();
            int n = _catalog.ScanPathCount;
            if (_selFolder >= n) _selFolder = n - 1;
            for (int i = 0; i < n; i++)
            {
                int idx = i;
                var path = _catalog.ScanPath(i) ?? "?";
                bool sel = i == _selFolder;
                var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
                grid.Children.Add(new TextBlock
                {
                    Text = path, FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = sel ? TextPrimary : TextSecondary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var tag = new TextBlock { Text = IsDefault(path) ? "default" : "", FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(tag, 1);
                grid.Children.Add(tag);
                var row = new Border
                {
                    Height = 28, Padding = new Thickness(8, 0), CornerRadius = NotaRadius.Control,
                    Background = sel ? AccentSubtle : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), Child = grid,
                };
                row.PointerPressed += (_, _) => { _selFolder = idx; RenderFolders(); };
                list.Children.Add(row);
            }
            removeBtn.IsEnabled = _selFolder >= 0 && _selFolder < n && !IsDefault(_catalog.ScanPath(_selFolder));
        }
        RenderFolders();

        addBtn.Click += async (_, _) =>
        {
            Activate();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add plugin scan folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath();
            if (path is null) return;
            _catalog.AddScanPath(path);
            _selFolder = _catalog.ScanPathCount - 1;
            RenderFolders();
        };
        removeBtn.Click += (_, _) =>
        {
            if (_selFolder < 0 || _selFolder >= _catalog.ScanPathCount) return;
            _catalog.RemoveScanPath(_selFolder);
            _selFolder = Math.Max(0, _selFolder - 1);
            RenderFolders();
        };

        var rescanStatus = new TextBlock { FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        var rescan = Btn("Rescan plugins");
        rescan.Click += async (_, _) =>
        {
            if (_main is null) { rescanStatus.Text = "Plugin scanning is unavailable in this window."; return; }
            rescan.IsEnabled = false;
            rescan.Content = "Scanning…";
            int folders = _catalog.ScanPathCount;
            rescanStatus.Text = $"Searching {folders} folder{(folders == 1 ? "" : "s")}";
            try
            {
                int? n = await PluginScan.RescanAsync(_main);
                rescanStatus.Text = n is { } c ? $"Found {c} plugin{(c == 1 ? "" : "s")}." : "Scanner worker not found.";
            }
            finally { rescan.IsEnabled = true; rescan.Content = "Rescan plugins"; }
        };

        return Sections(
            Section("Plugin scan folders", 10,
                well,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addBtn, removeBtn } },
                Caption("Extra folders are searched for VST3 plugins on the next rescan (AU uses the system registry).")),
            Section("Scan", 10,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { rescan, rescanStatus } }));
    }

    // ---- Library ----------------------------------------------------------

    private Control LibraryPane()
    {
        if (_main is null) return Caption("Content folders are unavailable in this window.");
        var settings = _main.Settings;
        return Sections(Section("Content folders", 10, Table(new List<Control>
        {
            FolderRow("Samples",
                () => settings.Current.SamplesFolder, () => settings.ResolvedSamplesFolder(),
                path => { settings.Current.SamplesFolder = path; settings.Save(); _main.Browser.RebuildSamples(); }),
            FolderRow("Projects",
                () => settings.Current.ProjectsFolder, () => settings.ResolvedProjectsFolder(),
                path => { settings.Current.ProjectsFolder = path; settings.Save(); _main.Browser.RebuildProjects(); }),
        })), VersionHistorySection(_main));
    }

    // On by default. Turning it off erases history, so it asks first — and stays on if not confirmed.
    private Control VersionHistorySection(MainWindowViewModel main)
    {
        var row = (StackPanel)SwitchRow("Keep a version history of each project", main.Settings.Current.KeepVersionHistory, _ => { });
        var sw = (ToggleSwitch)row.Children[0];
        sw.Changed += async on =>
        {
            if (on) { main.SetKeepVersionHistory(true); return; }
            bool ok = await new ConfirmWindow("Turn off version history",
                "This erases the version history of the open project now, and of any other project the next time you save it. Older versions can't be brought back.",
                "Turn off and erase", "Cancel").ShowDialog<bool>(this);
            if (ok) main.SetKeepVersionHistory(false);
            else sw.IsOn = true;
        };
        return Section("Version history", 10, row,
            Caption("Every save records a version, listed in the browser's History tab. Versions share their audio, so each one costs only what it adds."));
    }

    private Control FolderRow(string label, Func<string> get, Func<string> resolved, Action<string> set)
    {
        bool custom = !string.IsNullOrWhiteSpace(get());
        var path = new TextBlock
        {
            Text = custom ? get() : resolved(), FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var tag = new TextBlock { Text = custom ? "custom" : "default", FontSize = 10, Foreground = TextTertiary };
        var choose = Btn("Choose…");
        choose.Click += async (_, _) =>
        {
            Activate();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Choose {label} folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            var p = folders[0].TryGetLocalPath();
            if (p is null) return;
            set(p);
            path.Text = p;
            tag.Text = "custom";
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("110,*,Auto"), ColumnSpacing = 12, Margin = new Thickness(14, 12) };
        grid.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center });
        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center, Children = { path, tag } };
        Grid.SetColumn(text, 1); grid.Children.Add(text);
        Grid.SetColumn(choose, 2); grid.Children.Add(choose);
        return grid;
    }

    // ---- Appearance -------------------------------------------------------

    // The colour themes (spec 011), mode first: a Dark · Light · Follow system segmented
    // control over two rows of cards, dark themes and light themes, each card a miniature
    // Nota window drawn in its own colours. Both rows always show, so the page never changes
    // height: the mode only decides which cards are chosen — one while wearing a single
    // theme, one per row while following the system. Picking a card of the other kind
    // switches mode. Every pick applies at once (NotaThemeService re-tints the palette and
    // repaints every open window) and is saved.
    private static Control ThemePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var darkCards = ThemeCards(NotaThemeKind.Dark);
        var lightCards = ThemeCards(NotaThemeKind.Light);
        var darkLabel = ModeLabel();
        var lightLabel = ModeLabel();

        var modes = new List<ToggleButton>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var label in new[] { "Dark", "Light", "Follow system" })
        {
            var btn = new ToggleButton { Classes = { "seg" }, FontSize = 12, Content = label };
            modes.Add(btn);
            strip.Children.Add(btn);
        }
        var segmented = new Border { Classes = { "segmented" }, HorizontalAlignment = HorizontalAlignment.Left, Child = strip };

        bool loading = true;
        void Show()
        {
            loading = true;
            var c = NotaThemeService.Choice;
            var mode = Appearance.ModeOf(c, (NotaThemes.Find(c.Single) ?? NotaThemes.Default).IsDark);
            for (int i = 0; i < modes.Count; i++) modes[i].IsChecked = i == (int)mode;
            bool follow = mode == AppearanceMode.FollowSystem;
            // The labels change text and ink, never size: the row in use reads one step up.
            darkLabel.Text = follow ? "In dark mode" : "Dark";
            lightLabel.Text = follow ? "In light mode" : "Light";
            darkLabel.Foreground = mode != AppearanceMode.Light ? TextSecondary : TextMuted;
            lightLabel.Foreground = mode != AppearanceMode.Dark ? TextSecondary : TextMuted;
            darkCards.SelectedItem = follow ? NotaThemes.Find(c.Dark) : mode == AppearanceMode.Dark ? NotaThemes.Find(c.Single) : null;
            lightCards.SelectedItem = follow ? NotaThemes.Find(c.Light) : mode == AppearanceMode.Light ? NotaThemes.Find(c.Single) : null;
            loading = false;
        }

        void Commit(AppearanceChoice choice)
        {
            var single = NotaThemes.Find(choice.Single) ?? NotaThemes.Default;
            Appearance.Store(settings.Current, choice, single.IsDark);
            settings.Save();
            NotaThemeService.Set(choice);
            Show();
        }

        for (int i = 0; i < modes.Count; i++)
        {
            var mode = (AppearanceMode)i;   // the keys are in AppearanceMode order
            modes[i].Click += (_, _) => Commit(Appearance.WithMode(NotaThemeService.Choice, mode));
        }
        darkCards.SelectionChanged += (_, _) =>
        {
            if (!loading && darkCards.SelectedItem is NotaColourTheme t) Commit(Appearance.WithPick(NotaThemeService.Choice, t.Id, isDark: true));
        };
        lightCards.SelectionChanged += (_, _) =>
        {
            if (!loading && lightCards.SelectedItem is NotaColourTheme t) Commit(Appearance.WithPick(NotaThemeService.Choice, t.Id, isDark: false));
        };

        Show();
        return new StackPanel
        {
            Spacing = 10,
            Children = { segmented, darkLabel, darkCards, lightLabel, lightCards },
        };
    }

    // The themes of one kind as cards in a row: the preview in a frame that takes the
    // accent when chosen (colour only; the frame is always 2 px), the name under it.
    private static ListBox ThemeCards(NotaThemeKind kind)
    {
        var list = new ListBox
        {
            Classes = { "themes" }, SelectionMode = SelectionMode.Single,
            HorizontalAlignment = HorizontalAlignment.Left,
            ItemsPanel = new FuncTemplate<Panel?>(() => new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 }),
            ItemsSource = NotaThemes.OfKind(kind).ToList(),
        };
        list.ItemTemplate = new FuncDataTemplate<NotaColourTheme>((t, _) => t is null ? new TextBlock() : new StackPanel
        {
            Spacing = 6,
            Children =
            {
                new Border { Classes = { "ThemeFrame" }, Child = new ThemePreview(t) },
                new TextBlock { Text = t.Name, Classes = { "RowName" } },
            },
        });
        return list;
    }

    private static TextBlock ModeLabel() => new() { FontSize = 11, Margin = new Thickness(0, 4, 0, -4) };

    // A miniature Nota window in one theme's own colours, whichever theme is live, so each
    // card previews itself: title bar, transport with the view keys, browser, arrangement
    // lanes with clips in the first track colours and the playhead, and the device panel.
    private sealed class ThemePreview : Control
    {
        public const double W = 184, H = 112;
        private readonly NotaColourTheme _theme;
        public ThemePreview(NotaColourTheme theme) { _theme = theme; Width = W; Height = H; }

        private IBrush B(SolidColorBrush slot) => new ImmutableSolidColorBrush(NotaPalette.ColorIn(slot, _theme));

        public override void Render(DrawingContext ctx)
        {
            var seam = new Pen(B(NotaPalette.Seam), 1);
            void Fill(SolidColorBrush slot, double x, double y, double w, double h) => ctx.FillRectangle(B(slot), new Rect(x, y, w, h));
            void Key(SolidColorBrush slot, double x, double y, double w, double h) =>
                ctx.DrawRectangle(B(slot), seam, new RoundedRect(new Rect(x + 0.5, y + 0.5, w - 1, h - 1), NotaRadius.ClipValue));
            void HLine(double y, double x0, double x1) => ctx.DrawLine(seam, new Point(x0, y + 0.5), new Point(x1, y + 0.5));
            void VLine(double x, double y0, double y1) => ctx.DrawLine(seam, new Point(x + 0.5, y0), new Point(x + 0.5, y1));

            // Ground, title bar and transport.
            Fill(NotaPalette.BgApp, 0, 0, W, H);
            HLine(8, 0, W);
            Fill(NotaPalette.Panel, 0, 9, W, 13);
            HLine(22, 0, W);
            Key(NotaPalette.Accent, 4, 12, 9, 7);
            Key(NotaPalette.SurfaceRaised, 14, 12, 9, 7);
            Key(NotaPalette.SurfaceRaised, 24, 12, 9, 7);
            Key(NotaPalette.BgSunken, 40, 12, 34, 7);
            Key(NotaPalette.BgSunken, W - 42, 12, 38, 7);

            // Browser column: a list of names.
            const double top = 23, detailTop = 82, browserW = 44;
            Fill(NotaPalette.Panel, 0, top, browserW, detailTop - top);
            VLine(browserW, top, detailTop);
            double[] names = { 30, 22, 26, 18, 24, 20 };
            for (int i = 0; i < names.Length; i++)
                Fill(i == 1 ? NotaPalette.TextPrimary : NotaPalette.TextMuted, 5, top + 5 + i * 8, names[i], 2);

            // Arrangement: four lanes, a clip per lane in the first track colours, the playhead.
            double laneH = (detailTop - top) / 4, x0 = browserW + 1;
            (double At, double Len)[] clips = { (0.08, 0.42), (0.30, 0.55), (0.04, 0.30), (0.52, 0.36) };
            for (int i = 0; i < 4; i++)
            {
                double y = top + i * laneH, span = W - x0;
                var track = NotaPalette.TrackBrushes[i % NotaPalette.TrackBrushes.Length];
                var r = new Rect(x0 + span * clips[i].At, y + 2, span * clips[i].Len, laneH - 4);
                ctx.FillRectangle(B(track), r, (float)NotaRadius.ClipValue);
                if (i < 3) HLine(y + laneH - 1, x0, W);
            }
            ctx.FillRectangle(B(NotaPalette.Accent), new Rect(x0 + (W - x0) * 0.46, top, 1, detailTop - top));

            // Device panel: two cards with their header strips.
            HLine(detailTop, 0, W);
            Fill(NotaPalette.Panel, 0, detailTop + 1, W, H - detailTop - 1);
            foreach (var (x, w) in new[] { (5.0, 84.0), (93.0, 86.0) })
            {
                Key(NotaPalette.SurfaceCard, x, detailTop + 5, w, H - detailTop - 10);
                Fill(NotaPalette.SurfaceRaised, x + 1, detailTop + 6, w - 2, 5);
            }
        }
    }

    // Bars / Solid for the arrangement's audio-clip waveforms and the browser preview. Saving
    // fires Settings.Changed, which the lanes and the preview apply at once.
    private static Control WaveformPicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(int Style, ToggleButton Btn)>();
        foreach (var (style, label) in new[] { (0, "Bars"), (1, "Solid") })
        {
            var st = style;
            var btn = new ToggleButton { Classes = { "seg" }, FontSize = 12, Content = label, IsChecked = settings.Current.ArrangementWaveform == st };
            btn.Click += (_, _) =>
            {
                foreach (var (bs, bb) in buttons) bb.IsChecked = bs == st;
                if (settings.Current.ArrangementWaveform == st) return;
                settings.Current.ArrangementWaveform = st;
                settings.Save();
            };
            buttons.Add((st, btn));
            strip.Children.Add(btn);
        }
        return new Border { Classes = { "segmented" }, HorizontalAlignment = HorizontalAlignment.Left, Child = strip };
    }

    private Control AppearancePane()
    {
        var settings = App.Services.GetService<ISettingsService>();
        var mcp = App.Services.GetService<McpService>();

        // ---- AI control (MCP server) ----
        var portBox = new TextBox
        {
            Text = (settings?.Current.McpPort ?? 3900).ToString(), Width = 96, Classes = { "field" },
            FontSize = 12, FontFamily = NotaFonts.MonoFamily, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var statusDot = Dot(BorderStrong);
        var status = new TextBlock
        {
            FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var copyBtn = Btn("Copy config");
        copyBtn.HorizontalAlignment = HorizontalAlignment.Left;
        var details = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Row("Port", portBox),
                Row("Status", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, status } }),
                Row("", new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        copyBtn,
                        Caption("Add this config (or the URL) as an MCP server in Claude Desktop / Claude Code. Loopback only; off by default."),
                    },
                }),
            },
        };

        bool Enabled() => settings?.Current.McpEnabled ?? false;
        void Refresh()
        {
            bool on = Enabled();
            status.Text = on ? $"Listening on http://127.0.0.1:{settings!.Current.McpPort}/ (loopback)" : "Off";
            statusDot.Fill = on ? NotaPalette.SuccessDim : BorderStrong;
            portBox.IsEnabled = on;
            portBox.Foreground = on ? TextPrimary : TextDisabled;
            copyBtn.IsEnabled = on;
            Inactive.Set(details, !on);
        }
        void ApplyMcp(bool on)
        {
            if (settings is null) return;
            settings.Current.McpEnabled = on;
            if (int.TryParse(portBox.Text, out var p) && p is > 0 and < 65536) settings.Current.McpPort = p;
            settings.Save();
            _ = mcp?.ApplyAsync();
            Refresh();
        }
        var enable = SwitchRow("Enable MCP server (let an AI drive Nota)", Enabled(), ApplyMcp);
        portBox.LostFocus += (_, _) => ApplyMcp(Enabled());

        // Copy a ready client-config JSON (Claude Desktop / Claude Code) with the current port.
        string McpConfigJson()
        {
            int port = settings?.Current.McpPort ?? 3900;
            return "{\n  \"mcpServers\": {\n    \"nota\": {\n      \"type\": \"http\",\n      \"url\": \"http://127.0.0.1:" + port + "/\"\n    }\n  }\n}";
        }
        DispatcherTimer? copied = null;
        copyBtn.Click += async (_, _) =>
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb is null) return;
            await cb.SetTextAsync(McpConfigJson());
            copyBtn.Content = "Copied";
            copied?.Stop();
            copied = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            copied.Tick += (_, _) => { copied!.Stop(); copyBtn.Content = "Copy config"; };
            copied.Start();
        };
        // Inactive.Set walks the visual tree, so the dimming waits until the pane is shown.
        details.AttachedToVisualTree += (_, _) => Refresh();

        return Sections(
            Section("UI", 10,
                TopRow("Colour theme", ThemePicker()),
                Row("", Caption("Themes change colours only. Follow system switches between your dark-mode and light-mode theme with the OS appearance.")),
                Row("Waveform", WaveformPicker()),
                Row("", Caption("How audio clips draw on the arrangement, and samples in the browser's preview: Bars spaces the peaks out so the rhythm reads; Solid draws every pixel."))),
            Section("AI control (MCP)", 12, enable, details));
    }

    // ---- Shortcuts --------------------------------------------------------

    // NB: this list mirrors the real key handlers — keep the two in sync (see the
    // `nota-shortcuts` skill). Bindings live in MainWindow.Input.cs (transport, global
    // editing, computer-keyboard notes), PianoRollView.cs (note editing) and PreviewPlayer.cs
    // (the Files tab's sample player). Written with the macOS glyphs; MenuKit.Keys spells them
    // Ctrl / Alt / Shift on Windows and Linux. A Key made only of
    // keys (see IsKeyToken) is drawn as one key-cap per token; a gesture is plain text.
    private static readonly (string Title, (string Key, string Action)[] Rows)[] ShortcutGroups =
    {
        ("File", new[]
        {
            ("⌘N   ⌘O", "New project / open a project"),
            ("⌘S   ⌘⇧S", "Save (records a version) / save as"),
            ("⌥⌘S", "Save a version with a note"),
            ("⌘⇧E", "Export audio"),
            ("⌘,", "Settings"),
        }),
        ("Window", new[]
        {
            ("F11", "Enter / leave full screen — the title bar hides in full screen"),
        }),
        ("Command palette", new[]
        {
            ("⌘⇧P", "Open / close the command palette — from any window, even while typing"),
            ("↑ ↓   PgUp PgDn", "Move through the results"),
            ("Return", "Apply where the palette was opened (the target shows in the field)"),
            ("⌘Return", "An instrument or instrument preset on a new track · a track: open it in Devices"),
            ("⌥Return", "Replace the selected device instead of inserting after it"),
            ("⇧Return", "Apply and keep the palette open for the next device"),
            ("Tab   ⇧Tab", "Filter: all · actions · tracks · devices · presets"),
            ("> @ + # ~", "Type first to filter: actions, tracks, devices, presets, Modular nodes"),
            ("Esc", "Close and go back where you were"),
        }),
        ("Transport", new[]
        {
            ("Space", "Play / Stop"),
            ("Return", "Stop (again → back to the start) · in the Session grid it launches the selection"),
            ("⌘R", "Record"),
            ("⌘M", "Metronome"),
            ("⌘L", "Loop on / off · loop the selected clips or time range"),
            ("⌘⇧R", "Loop recording: Overdub (every pass adds) / Replace (keep the latest pass)"),
        }),
        ("Arrangement & editing", new[]
        {
            ("⌘Z   ⌘⇧Z", "Undo / redo"),
            ("⌘A", "Select all clips"),
            ("⌘⇧A", "Toggle automation mode"),
            ("⌘⇧M", "Toggle the Mixer view"),
            ("Tab", "Cycle Devices / Pattern / Clip in the detail panel"),
            ("Esc", "Cancel the current gesture / clear the selection · close the patch-bay overlay"),
            ("⌘G   ⌘⇧G", "Group / ungroup selected tracks"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste clip (or automation range) · a clip copied in Session pastes here"),
            ("⌘⇧C", "Copy the selected clips to Session · the first scene row where they fit"),
            ("⌘⇧V", "Paste the last range as audio rendered through its devices"),
            ("⌘D", "Duplicate the selected clip(s) / range"),
            ("⌘⇧D", "Duplicate time · insert a copy of the time range after it, pushing everything later right"),
            ("⌘⇧I", "Insert silence · push everything from the range start right by its length"),
            ("⌘E", "Split at the playhead · at the range edges"),
            ("⌘J", "Consolidate the selection into one clip per track"),
            ("0", "Deactivate / activate the selected clip(s)"),
            ("Delete", "Delete the selected clip / range"),
        }),
        ("Grid (Arrangement or clip editor — whichever you used last)", new[]
        {
            ("⌘1   ⌘2", "Narrow / widen the grid · in Auto, change the density"),
            ("⌘3", "Triplet grid on / off"),
            ("⌘4", "Snap to grid on / off"),
            ("⌘5", "Switch between the adaptive (Auto) and a fixed grid"),
            ("⌥ while dragging", "Move freely, ignoring the grid"),
        }),
        ("Tracks (after a click in the track headers)", new[]
        {
            ("⌘A", "Select all tracks"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste the selected tracks · paste lands after the last one"),
            ("⌘D", "Duplicate the selected tracks"),
            ("Delete", "Delete the selected tracks · a group goes with its tracks"),
        }),
        ("Session (after a click in the grid)", new[]
        {
            ("← → ↑ ↓", "Move the selection between slots and scenes"),
            ("Return", "Launch the selected clip or scene"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste a clip · a cut clip moves when pasted · clips copied in the Arrangement paste here"),
            ("⌘⇧C", "Copy the selected clip, group slot or scene to the Arrangement at the playhead"),
            ("⌘D", "Duplicate a clip into the slot below · a scene below itself"),
            ("⌘I", "Insert a scene below the selection"),
            ("F2", "Rename the selected clip or scene"),
            ("Delete", "Delete the selected clip or scene"),
            ("Drag clip", "Move it to another slot · ⌥ copies"),
            ("Drag scene", "Reorder scenes"),
            ("Double-click slot", "Edit the clip · an empty MIDI slot gets a new clip"),
            ("Double-click header", "Rename the track"),
            ("Right-click header or grid", "Add an instrument, audio or return track · rename or delete one"),
        }),
        ("Piano roll", new[]
        {
            ("← →", "Move notes by the grid"),
            ("↑ ↓", "Transpose by a semitone"),
            ("⇧↑  ⇧↓", "Transpose by an octave"),
            ("⇧←  ⇧→", "Shorten / lengthen the notes by the grid"),
            ("⌥↑  ⌥↓", "Velocity +10 / −10 · ⌥⇧ by 1"),
            ("⌘A", "Select all notes"),
            ("⌘D", "Duplicate the selection · replaces the notes it lands on"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste notes · paste lands on the originals"),
            ("Esc", "Deselect · moved or pasted notes replace the notes under them"),
            ("Delete", "Delete the selected notes"),
            ("Drag note edge", "Change the note's start or end"),
            ("Drag velocity stem", "Set velocity · selected notes move together · ⇧ adds a note"),
            ("Drag in the Bend / Pres / Slide lane", "Draw the selected note's MPE curve · ⌥ draws a straight line · drag a point to move it"),
            ("Double-click in an MPE lane", "Add a point · on a point, delete it · right-click to clear the curve"),
            ("⌘⌥ + wheel", "Zoom rows (vertical zoom)"),
            ("Drag on the keys", "Zoom rows · down taller, up shorter"),
            ("⌥ + pinch", "Zoom rows (trackpad)"),
            ("⇧F", "Fit to notes"),
        }),
        ("Play notes (computer keyboard)", new[]
        {
            ("A S D F G H J K", "White keys, from C4"),
            ("W E · T Y U", "Black keys (sharps)"),
            ("Z / X", "Shift octave down / up"),
            ("C / V", "Lower / raise velocity"),
        }),
        ("Browser · preview", new[]
        {
            ("↑ ↓", "Move through samples, presets and devices · with Auto on, each one plays as it is selected"),
            ("Click play button", "Play / stop the selected sample, preset or device"),
            ("Click waveform", "Play from that point"),
            ("Click WAVE / SPEC", "Switch the well between the waveform and a live spectrum"),
            ("Scroll on the track button", "Step through the demo tracks an effect preset is heard through"),
            ("Double-click volume", "Reset the preview volume to 0\u2009dB"),
        }),
        ("Gamepad (macOS)", new[]
        {
            ("Face buttons", "Play C4 D4 E4 F4"),
            ("Shoulder buttons", "Play G4 A4 B4 C5"),
            ("D-pad ↑ / ↓", "Shift the gamepad octave"),
            ("D-pad ← / →", "Lower / raise velocity"),
            ("Any button", "Mappable through MIDI Learn — a mapped button drives the control instead"),
            ("Sticks · triggers", "Mappable as continuous controls (a knob, a fader); they play no notes"),
        }),
        ("Mouse", new[]
        {
            ("Double-click clip", "Open in the clip editor"),
            ("Right-click the metronome", "Count-in before Play / Record: off · 1 · 2 · 4 bars"),
            ("Drag an audio clip's ADSR handle", "Shape attack · decay + sustain · release (top corners and the dot on hover) · double-click resets the stage"),
            ("⇧-click track header", "Add the track to the selection · again to remove it"),
            ("Right-click track headers", "A multi-selection gets its own menu: group, colour, track height, freeze, copy, duplicate, delete"),
            ("Drag a track's bottom edge", "Resize the track (header or lane) · snaps to compact / standard / large · a multi-selection resizes together"),
            ("Drag a track's bottom edge + ⌥", "Resize every track at once"),
            ("Double-click a track's bottom edge", "Back to the standard height"),
            ("Drag the track-list edge", "Widen or narrow the track headers · double-click resets"),
            ("Drag sections lane", "Mark a new section over the dragged bars"),
            ("Click section", "Jump the playhead to its start"),
            ("Double-click section", "Rename it"),
            ("Drag section · its edge", "Move / resize it, snapped to bars"),
            ("Drag overview strip", "Drag sideways to scroll · up / down to zoom out / in"),
            ("Drag overview edge", "Zoom by resizing the viewport window"),
            ("Double-click overview", "Fit the whole project on screen"),
            ("Drag a device header", "Reorder devices (or right-click the header → Move left / right)"),
            ("Right-click a device header", "A / B compare, move, copy, delete, presets, save preset"),
            ("Delete (device selected)", "Remove the selected device — the header no longer carries a close button"),
            ("Drag an audio clip onto an instrument track", "Load its audio into the Sampler, Grain, Drum Rack (next free pad) or Instrument Rack · rest there to open its devices"),
            ("Drag an audio clip out of the Arrangement", "Take it as a sample: drop on a pad, a Grain, a Rhythm voice, a Session slot — the clip stays put"),
            ("Drag clip + ⌥", "Position freely, ignoring the grid for this drag (the magnet in the transport latches the same thing)"),
            ("Drag knob", "Change a device value · hold ⌘ or ⇧ for fine steps"),
            ("Double-click knob", "Reset the value to its default"),
            ("Double-click a slider's value", "Type a value (units like k, ms, dB, L / R work) · Return sets it, Esc cancels"),
            ("Drag jack → jack", "Patch a cable in Nota Consort · ⌥-click a jack to pull its cables"),
            ("Click · ⇧ · ⌥ a Rhythm step", "Step on / off · accent · quiet step — drag across to paint, up / down (or scroll) for its velocity"),
            ("Click a Rhythm voice", "Select and play it · drop a file on it to load a sample"),
        }),
    };

    private Control ShortcutsPane()
    {
        var groups = new StackPanel { Spacing = 18 };
        var filter = new TextBox { PlaceholderText = "Filter shortcuts", Text = _shortcutFilter, Classes = { "search" }, FontSize = 12 };

        void Render()
        {
            groups.Children.Clear();
            // Each word must appear in the row's action, its keys or its group's title, so
            // "keyboard" finds the whole "Play notes (computer keyboard)" group (#40).
            var words = _shortcutFilter.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var (title, rows) in ShortcutGroups)
            {
                var shown = rows.Where(r => words.All(w => title.Contains(w, StringComparison.OrdinalIgnoreCase)
                                                           || MenuKit.Keys(r.Action).Contains(w, StringComparison.OrdinalIgnoreCase)
                                                           || MenuKit.Keys(r.Key).Contains(w, StringComparison.OrdinalIgnoreCase))).ToList();
                if (shown.Count == 0) continue;
                var head = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 0, 0, 4),
                    Children =
                    {
                        new TextBlock { Text = title, Classes = { "SectionLabel" }, VerticalAlignment = VerticalAlignment.Center },
                        new TextBlock { Text = shown.Count.ToString(), FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = TextDisabled, VerticalAlignment = VerticalAlignment.Center },
                    },
                };
                groups.Children.Add(new StackPanel { Spacing = 6, Children = { head, Table(shown.Select(r => ShortcutRow(r.Key, r.Action)).ToList()) } });
            }
            if (groups.Children.Count == 0) groups.Children.Add(Caption("Nothing matches."));
        }
        filter.TextChanged += (_, _) => { _shortcutFilter = filter.Text ?? ""; Render(); };
        Render();

        return new StackPanel { Spacing = 18, Children = { SearchField(filter, 320), groups } };
    }

    private static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal) { "Space", "Return", "Delete", "Tab", "Esc", "PgUp", "PgDn", "⌘Return", "⌥Return", "⇧Return", "⇧Tab" };

    // A token that names a key: no lower-case letters (⌘⇧Z, ←, A, 0), or a named key.
    private static bool IsKeyToken(string t) => NamedKeys.Contains(t) || !t.Any(char.IsLower);

    private static Control ShortcutRow(string key, string action)
    {
        var tokens = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Control keys;
        if (tokens.All(IsKeyToken))
        {
            var wrap = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var t in tokens)
                wrap.Children.Add(t is "/" or "·"
                    ? new TextBlock { Text = t, FontSize = 11, Foreground = TextTertiary, Margin = new Thickness(0, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center }
                    : KeyCap(MenuKit.Keys(t)));
            keys = wrap;
        }
        else
        {
            keys = new TextBlock { Text = MenuKit.Keys(key), FontSize = 12, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*"), ColumnSpacing = 12, MinHeight = 20, Margin = new Thickness(12, 7) };   // 34 with the margin
        grid.Children.Add(keys);
        var a = new TextBlock { Text = MenuKit.Keys(action), FontSize = 12, LineHeight = 17, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(a, 1);
        grid.Children.Add(a);
        return grid;
    }

    // A key-cap: raised face, hairline sides and a stronger bottom edge.
    private static Control KeyCap(string label)
    {
        var text = new TextBlock
        {
            Text = label, FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        return new Border
        {
            Height = 20, MinWidth = 20, Margin = new Thickness(0, 1, 4, 1),
            CornerRadius = NotaRadius.Control, Background = BorderStrong,
            BorderBrush = Divider, BorderThickness = new Thickness(1, 1, 1, 0),
            Child = new Border
            {
                Background = Raised, CornerRadius = NotaRadius.Badge, Margin = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 0), Child = text,
            },
        };
    }

    // ---- helpers ----------------------------------------------------------

    // Stacks sections top to bottom; every one after the first sits under a hairline.
    private static Control Sections(params Control[] sections)
    {
        var stack = new StackPanel();
        for (int i = 0; i < sections.Length; i++)
            stack.Children.Add(i == 0 ? sections[i] : new Border
            {
                Margin = new Thickness(0, 22, 0, 0), Padding = new Thickness(0, 20, 0, 0),
                BorderBrush = Hairline, BorderThickness = new Thickness(0, 1, 0, 0), Child = sections[i],
            });
        return stack;
    }

    private static Control Section(string title, double spacing, params Control[] children)
    {
        var stack = new StackPanel { Spacing = spacing };
        stack.Children.Add(SectionLabel(title));
        foreach (var c in children) stack.Children.Add(c);
        return stack;
    }

    // Rows in one bordered box on the panel ground, a hairline between them.
    private static Control Table(IReadOnlyList<Control> rows)
    {
        var stack = new StackPanel();
        for (int i = 0; i < rows.Count; i++)
            stack.Children.Add(new Border
            {
                Background = RowBg, BorderBrush = Hairline, BorderThickness = new Thickness(0, i == 0 ? 0 : 1, 0, 0), Child = rows[i],
            });
        return new Border
        {
            BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel,
            ClipToBounds = true, Child = stack,
        };
    }

    // A fixed-height table row: cells laid into the given columns, 12 apart.
    private static Grid ListRow(double height, string columns, params Control[] cells)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(columns), ColumnSpacing = 12, Height = height,
            Margin = new Thickness(12, 0), Background = Brushes.Transparent,
        };
        for (int i = 0; i < cells.Length; i++) { Grid.SetColumn(cells[i], i); grid.Children.Add(cells[i]); }
        return grid;
    }

    // The empty state of a device list: a dashed outline, an idle dot and one line.
    private static Control EmptyBox(string text)
    {
        var outline = new Rectangle
        {
            Stroke = Divider, StrokeThickness = 1, StrokeDashArray = new AvaloniaList<double> { 3, 3 },
            RadiusX = NotaRadius.PanelValue, RadiusY = NotaRadius.PanelValue,
        };
        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(14, 0), VerticalAlignment = VerticalAlignment.Center,
            Children = { Dot(BorderStrong), new TextBlock { Text = text, FontSize = 11, Foreground = TextMuted, VerticalAlignment = VerticalAlignment.Center } },
        };
        return new Panel { Height = 44, Children = { outline, line } };
    }

    private static Ellipse Dot(IBrush fill) => new() { Width = 6, Height = 6, Fill = fill, VerticalAlignment = VerticalAlignment.Center };

    // A settings switch (28×16) with its sentence; the sentence toggles it too.
    private static Control SwitchRow(string text, bool on, Action<bool> changed)
    {
        var sw = new ToggleSwitch(on, large: true) { VerticalAlignment = VerticalAlignment.Center };
        sw.Changed += changed;
        var label = new TextBlock { Text = text, FontSize = 12, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        label.Cursor = new Cursor(StandardCursorType.Hand);
        label.PointerPressed += (_, e) => { if (e.GetCurrentPoint(label).Properties.IsLeftButtonPressed) { sw.Toggle(); e.Handled = true; } };
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { sw, label } };
    }

    // A search / filter box: the sunken field with a magnifier, the borderless TextBox inside.
    private static Control SearchField(TextBox box, double maxWidth)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
        grid.Children.Add(new Glyph(GlyphKind.Search, 11) { Foreground = TextTertiary });
        box.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(box, 1);
        grid.Children.Add(box);
        return new Border
        {
            Height = 30, MaxWidth = maxWidth, Padding = new Thickness(10, 0), Background = Sunken,
            BorderBrush = Divider, BorderThickness = new Thickness(1), CornerRadius = NotaRadius.Panel, Child = grid,
            HorizontalAlignment = double.IsInfinity(maxWidth) ? HorizontalAlignment.Stretch : HorizontalAlignment.Left,
            Width = double.IsInfinity(maxWidth) ? double.NaN : maxWidth,
        };
    }

    // Settings buttons sit at the form scale: 28 tall, 12px label.
    private static Button Btn(string text) => new()
    {
        Content = text, Height = 28, FontSize = 12, CornerRadius = NotaRadius.Tile, Padding = new Thickness(14, 0),
    };

    private static ComboBox Combo(double width) => new()
    {
        Width = width, FontSize = 12, CornerRadius = NotaRadius.Tile, HorizontalAlignment = HorizontalAlignment.Left,
    };

    // Label · button · caption; the caption gets the remaining width so a long result wraps.
    private static Control TestRow(string label, Button button, TextBlock caption)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},132,*"), ColumnSpacing = 14, MinHeight = 34 };
        grid.Children.Add(RowLabel(label));
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        Grid.SetColumn(caption, 2);
        grid.Children.Add(caption);
        return grid;
    }

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text, Classes = { "SectionLabel" }, Margin = new Thickness(0, 0, 0, 4),
    };

    private static TextBlock RowLabel(string text) => new()
    {
        Text = text, FontSize = 12, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center,
    };

    // Ink 5 for a note; Ink 4 (muted) for the explanation a pane leads with.
    private static TextBlock Caption(string text, bool muted = false) => new()
    {
        Text = text, FontSize = 11, LineHeight = 17, Foreground = muted ? TextMuted : TextTertiary,
        TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    // Label column · control, 34 tall at least. An empty label indents under the controls.
    private static Control Row(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*"), MinHeight = 34 };
        if (label.Length > 0) grid.Children.Add(RowLabel(label));
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    // A row whose control is taller than one line: the label sits beside the control's
    // first line (a 26px key) instead of floating at its middle.
    private static Control TopRow(string label, Control control)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*"), MinHeight = 34 };
        var l = RowLabel(label);
        l.VerticalAlignment = VerticalAlignment.Top;
        l.Margin = new Thickness(0, 5, 0, 0);
        grid.Children.Add(l);
        control.VerticalAlignment = VerticalAlignment.Top;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    private static void FillDeviceCombo(ComboBox combo, List<string> uids, IReadOnlyList<AudioDevice> devices, string savedUid)
    {
        uids.Clear();
        combo.Items.Add("System default");
        uids.Add("");
        int selected = 0;
        for (int i = 0; i < devices.Count; i++)
        {
            combo.Items.Add(devices[i].Name);
            uids.Add(devices[i].Uid);
            if (devices[i].Uid == savedUid && savedUid.Length > 0) selected = i + 1;
        }
        combo.SelectedIndex = selected;
    }

    private static int IndexOf(double[] values, double v) { for (int i = 0; i < values.Length; i++) if (values[i] == v) return i; return 0; }
    private static int IndexOf(int[] values, int v) { for (int i = 0; i < values.Length; i++) if (values[i] == v) return i; return 0; }
}
