// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings (nota-design/Nota Settings.html): 880×640 with a 188px sidebar grouped as
// Devices (Audio / MIDI / Gamepads), Plug-ins (Plug-ins / Downloads: plug-ins + sample packs)
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

    // Sidebar: group heading, then its pages. Icons are 24×24 stroke paths drawn at 14px.
    private sealed record Page(string Title, string Subtitle, string Icon);

    private static readonly (string Group, Page[] Pages)[] Nav =
    {
        ("DEVICES", new[]
        {
            new Page("Audio", "Device, sample rate and buffer", "M4 10 V14 M8 7 V17 M12 4 V20 M16 8 V16 M20 11 V13"),
            new Page("MIDI", "Controllers Nota listens to", "M4 5 H20 V19 H4 Z M8 5 V13 M12 5 V13 M16 5 V13"),
            new Page("Gamepads", "Play notes or drive mapped controls", "M7 8 H17 A4 4 0 0 1 21 12 V13 A4 4 0 0 1 14 16 H10 A4 4 0 0 1 3 13 V12 A4 4 0 0 1 7 8 Z M7 11 V13 M6 12 H8 M16 11.5 H16.01 M18 12.5 H18.01"),
        }),
        ("PLUG-INS", new[]
        {
            new Page("Plug-ins", "Where Nota looks for VST3", "M9 3 V7 M15 3 V7 M6 7 H18 V11 A6 6 0 0 1 6 11 Z M12 17 V21"),
            new Page("Downloads", "Plug-ins and sample packs", "M12 4 V15 M7 10 L12 15 L17 10 M5 19 H19"),
        }),
        ("GENERAL", new[]
        {
            new Page("Library", "Content folders and version history", "M3 7 A1 1 0 0 1 4 6 H9 L11 8 H20 A1 1 0 0 1 21 9 V18 A1 1 0 0 1 20 19 H4 A1 1 0 0 1 3 18 Z"),
            new Page("Appearance", "Theme and AI control", "M12 4 A8 8 0 1 0 12 20 Z M12 4 A8 8 0 0 1 12 20"),
            new Page("Shortcuts", "Keyboard reference", "M3 7 H21 V17 H3 Z M7 11 H7.01 M11 11 H11.01 M15 11 H15.01 M8 14 H16"),
        }),
    };

    private const string SearchIcon = "M4 10.5 A6.5 6.5 0 1 0 17 10.5 A6.5 6.5 0 1 0 4 10.5 Z M15.5 15.5 L21 21";

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
        public required Path Icon;
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
                Text = group, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 1.1,
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

        Closed += (_, _) => { StopTests(); CancelStoreWork(); };
        Select(0);
        EnsureStoreLoading();
    }

    private static IEnumerable<Page> Pages => Nav.SelectMany(g => g.Pages);

    private Control BuildNavItem(Page page, int idx)
    {
        var edge = new Border
        {
            Width = 2, CornerRadius = NotaRadius.Bar, Margin = new Thickness(0, 7),
            HorizontalAlignment = HorizontalAlignment.Left, Background = Brushes.Transparent,
        };
        var icon = new Path { Data = Geometry.Parse(page.Icon), StrokeThickness = 1.6, StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round };
        var label = new TextBlock { Text = page.Title, FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var badge = new TextBlock
        {
            FontSize = 9, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary,
            VerticalAlignment = VerticalAlignment.Center, IsVisible = false,
        };
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8, Margin = new Thickness(10, 0) };
        var iconBox = IconBox(icon, 14);
        row.Children.Add(iconBox);
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

    // A 24×24 stroke path scaled into a square box — the sidebar and search icons.
    private static Control IconBox(Path path, double size)
        => new Viewbox { Width = size, Height = size, VerticalAlignment = VerticalAlignment.Center, Child = new Canvas { Width = 24, Height = 24, Children = { path } } };

    private void Select(int index)
    {
        StopTests();   // the Audio pane is rebuilt on every visit — don't leave a tone / check orphaned
        _selected = index;
        for (int i = 0; i < _navItems.Count; i++)
        {
            bool on = i == index;
            var n = _navItems[i];
            n.Root.Background = on ? AccentSubtle : Brushes.Transparent;
            n.Edge.Background = on ? Brass : Brushes.Transparent;
            n.Icon.Stroke = on ? Brass : TextTertiary;
            n.Label.Foreground = on ? AccentHover : TextSecondary;
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
        3 => PluginsPane(),
        5 => LibraryPane(),
        6 => AppearancePane(),
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
            Section("AUDIO DEVICE", 6, device.ToArray()),
            Section("TEST", 8, TestToneRow(), CpuCheckRow(engine)));
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
                var dot = Dot(on ? Brass : BorderStrong);
                var name = new TextBlock
                {
                    Text = dev.Name, FontSize = 12, Foreground = on ? TextPrimary : TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var sw = new ToggleSwitch(on, large: true) { VerticalAlignment = VerticalAlignment.Center };
                sw.Changed += v =>
                {
                    dot.Fill = v ? Brass : BorderStrong;
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
        return Sections(Section("MIDI INPUTS", 8, items.ToArray()));
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
            Section("GAMEPAD INPUT", 10, enable, padHost),
            Section("LAYOUT", 10,
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
            Section("PLUGIN SCAN FOLDERS", 10,
                well,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addBtn, removeBtn } },
                Caption("Extra folders are searched for VST3 plugins on the next rescan (AU uses the system registry).")),
            Section("SCAN", 10,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { rescan, rescanStatus } }));
    }

    // ---- Library ----------------------------------------------------------

    private Control LibraryPane()
    {
        if (_main is null) return Caption("Content folders are unavailable in this window.");
        var settings = _main.Settings;
        return Sections(Section("CONTENT FOLDERS", 10, Table(new List<Control>
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
        return Section("VERSION HISTORY", 10, row,
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

    // Graphite / Paper / System as a segmented control, each with a swatch of its ground. The
    // choice applies immediately — NotaThemeService re-tints the palette and repaints every
    // open window — and is persisted so the next launch starts in the same variant.
    private static Control ThemePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(AppTheme Mode, ToggleButton Btn)>();

        foreach (var (mode, label) in new[]
                 {
                     (AppTheme.Dark, "Ember Graphite"),
                     (AppTheme.Light, "Ember Paper"),
                     (AppTheme.System, "System"),
                 })
        {
            var m = mode;
            var btn = new ToggleButton
            {
                Classes = { "seg" }, FontSize = 12,
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 7,
                    Children = { new ThemeSwatch(m), new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center } },
                },
                IsChecked = settings.Current.Theme == m,
            };
            btn.Click += (_, _) =>
            {
                // A segment can only be turned on: clicking the live one must not clear it.
                foreach (var (bm, b) in buttons) b.IsChecked = bm == m;
                if (settings.Current.Theme == m) return;
                settings.Current.Theme = m;
                settings.Save();
                NotaThemeService.Set(m);
            };
            buttons.Add((m, btn));
            strip.Children.Add(btn);
        }

        return new Border { Classes = { "segmented" }, HorizontalAlignment = HorizontalAlignment.Left, Child = strip };
    }

    // A 10px chip of a variant's ground — both variants at once, whichever one is live.
    // System is split corner to corner, Paper over Graphite.
    private sealed class ThemeSwatch : Control
    {
        private readonly AppTheme _mode;
        public ThemeSwatch(AppTheme mode) { _mode = mode; Width = 10; Height = 10; VerticalAlignment = VerticalAlignment.Center; }

        public override void Render(DrawingContext ctx)
        {
            var dark = new ImmutableSolidColorBrush(NotaPalette.ColorIn(NotaPalette.SurfaceCard, NotaThemeVariant.Dark));
            var light = new ImmutableSolidColorBrush(NotaPalette.ColorIn(NotaPalette.SurfaceCard, NotaThemeVariant.Light));
            var r = new Rect(0, 0, Width, Height);
            var box = new RoundedRect(r, NotaRadius.ClipValue);
            using (ctx.PushClip(box))
            {
                ctx.FillRectangle(_mode == AppTheme.Dark ? dark : light, r);
                if (_mode == AppTheme.System)
                {
                    var g = new StreamGeometry();
                    using (var c = g.Open())
                    {
                        c.BeginFigure(new Point(r.Right, r.Top), true);
                        c.LineTo(r.BottomRight);
                        c.LineTo(r.BottomLeft);
                        c.EndFigure(true);
                    }
                    ctx.DrawGeometry(dark, null, g);
                }
            }
            ctx.DrawRectangle(null, new Pen(BorderStrong, 1), new RoundedRect(r.Deflate(0.5), NotaRadius.ClipValue));
        }
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
                Row("Theme", ThemePicker()),
                Row("", Caption("Ember Graphite is the warm dark palette; Ember Paper is the same system on a light ground. System follows the OS appearance and switches with it."))),
            Section("AI CONTROL (MCP)", 12, enable, details));
    }

    // ---- Shortcuts --------------------------------------------------------

    // NB: this list mirrors the real key handlers — keep the two in sync (see the
    // `nota-shortcuts` skill). Bindings live in MainWindow.Input.cs (transport, global
    // editing, computer-keyboard notes), PianoRollView.cs (note editing) and PreviewPlayer.cs
    // (the Files tab's sample player). ⌘ = Meta on macOS / Ctrl on Windows. A Key made only of
    // keys (see IsKeyToken) is drawn as one key-cap per token; a gesture is plain text.
    private static readonly (string Title, (string Key, string Action)[] Rows)[] ShortcutGroups =
    {
        ("FILE", new[]
        {
            ("⌘N   ⌘O", "New project / open a project"),
            ("⌘S   ⌘⇧S", "Save (records a version) / save as"),
            ("⌥⌘S", "Save a version with a note"),
            ("⌘I   ⌘⇧E", "Import audio / export audio"),
        }),
        ("TRANSPORT", new[]
        {
            ("Space", "Play / Stop"),
            ("Return", "Stop (again → back to the start)"),
            ("⌘R", "Record"),
            ("⌘M", "Metronome"),
            ("⌘L", "Loop on / off · loop the selected clips or time range"),
        }),
        ("ARRANGEMENT & EDITING", new[]
        {
            ("⌘Z   ⌘⇧Z", "Undo / redo"),
            ("⌘A", "Select all clips"),
            ("⌘⇧A", "Toggle automation mode"),
            ("⌘⇧M", "Toggle the Mixer view"),
            ("Tab", "Cycle Devices / Pattern / Clip in the detail panel"),
            ("Esc", "Cancel the current gesture / clear the selection · close the patch-bay overlay"),
            ("⌘G   ⌘⇧G", "Group / ungroup selected tracks"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste clip (or automation range)"),
            ("⌘⇧V", "Paste the last range as audio rendered through its devices"),
            ("⌘D", "Duplicate the selected clip(s) / range"),
            ("⌘E", "Split at the playhead · at the range edges"),
            ("⌘J", "Consolidate the selection into one clip per track"),
            ("0", "Deactivate / activate the selected clip(s)"),
            ("Delete", "Delete the selected clip / range"),
        }),
        ("TRACKS (AFTER A CLICK IN THE TRACK HEADERS)", new[]
        {
            ("⌘A", "Select all tracks"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste the selected tracks · paste lands after the last one"),
            ("⌘D", "Duplicate the selected tracks"),
            ("Delete", "Delete the selected tracks · a group goes with its tracks"),
        }),
        ("PIANO ROLL", new[]
        {
            ("← →", "Move notes by the grid"),
            ("↑ ↓", "Transpose by a semitone"),
            ("⇧↑  ⇧↓", "Transpose by an octave"),
            ("⌘A", "Select all notes"),
            ("⌘D", "Duplicate the selection · replaces the notes it lands on"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste notes"),
            ("Delete", "Delete the selected notes"),
            ("Drag note edge", "Change the note's start or end"),
            ("Drag velocity stem", "Set velocity · selected notes move together · ⇧ adds a note"),
        }),
        ("PLAY NOTES (COMPUTER KEYBOARD)", new[]
        {
            ("A S D F G H J K", "White keys, from C4"),
            ("W E · T Y U", "Black keys (sharps)"),
            ("Z / X", "Shift octave down / up"),
            ("C / V", "Lower / raise velocity"),
        }),
        ("BROWSER · PREVIEW", new[]
        {
            ("↑ ↓", "Move through samples, presets and devices · with Auto on, each one plays as it is selected"),
            ("Click play button", "Play / stop the selected sample, preset or device"),
            ("Click waveform", "Play from that point"),
            ("Click WAVE / SPEC", "Switch the well between the waveform and a live spectrum"),
            ("Scroll on the track button", "Step through the demo tracks an effect preset is heard through"),
            ("Double-click volume", "Reset the preview volume to 0\u2009dB"),
        }),
        ("GAMEPAD (MACOS)", new[]
        {
            ("Face buttons", "Play C4 D4 E4 F4"),
            ("Shoulder buttons", "Play G4 A4 B4 C5"),
            ("D-pad ↑ / ↓", "Shift the gamepad octave"),
            ("D-pad ← / →", "Lower / raise velocity"),
            ("Any button", "Mappable through MIDI Learn — a mapped button drives the control instead"),
            ("Sticks · triggers", "Mappable as continuous controls (a knob, a fader); they play no notes"),
        }),
        ("MOUSE", new[]
        {
            ("Double-click clip", "Open in the clip editor"),
            ("Drag an audio clip's ADSR handle", "Shape attack · decay + sustain · release (top corners and the dot on hover) · double-click resets the stage"),
            ("⇧-click track header", "Add the track to the selection · again to remove it"),
            ("Right-click track headers", "A multi-selection gets its own menu: group, colour, freeze, copy, duplicate, delete"),
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
            ("Drag clip + ⌥", "Position freely, ignoring the grid for this drag (the magnet in the transport latches the same thing)"),
            ("Drag knob", "Change a device value · hold ⌘ or ⇧ for fine steps"),
            ("Double-click knob", "Reset the value to its default"),
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
            var q = _shortcutFilter.Trim();
            foreach (var (title, rows) in ShortcutGroups)
            {
                var shown = rows.Where(r => q.Length == 0
                                            || r.Action.Contains(q, StringComparison.OrdinalIgnoreCase)
                                            || r.Key.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
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

    private static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal) { "Space", "Return", "Delete", "Tab", "Esc" };

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
                    : KeyCap(t));
            keys = wrap;
        }
        else
        {
            keys = new TextBlock { Text = key, FontSize = 12, Foreground = TextPrimary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        }

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*"), ColumnSpacing = 12, MinHeight = 20, Margin = new Thickness(12, 7) };   // 34 with the margin
        grid.Children.Add(keys);
        var a = new TextBlock { Text = action, FontSize = 12, LineHeight = 17, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
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
        var icon = new Path { Data = Geometry.Parse(SearchIcon), Stroke = TextTertiary, StrokeThickness = 2, StrokeLineCap = PenLineCap.Round };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 7 };
        grid.Children.Add(IconBox(icon, 11));
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
        Text = text, Classes = { "SectionLabel" }, LetterSpacing = 1.2, Margin = new Thickness(0, 0, 0, 4),
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

    private static void FillDeviceCombo(ComboBox combo, List<string> uids, IReadOnlyList<AudioDevice> devices, string savedUid)
    {
        uids.Clear();
        combo.Items.Add("System Default");
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
