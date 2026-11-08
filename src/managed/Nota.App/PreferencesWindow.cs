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
using System.Runtime.InteropServices;
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
using Nota.Application.Localization;
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
    // Id is the stable English name other code asks for (ShowPage("Audio")); Title is what is shown.
    private sealed record Page(string Id, string Title, string Subtitle, GlyphKind Icon);

    private static readonly (string Group, Page[] Pages)[] Nav =
    {
        (Loc.T("preferences.devices_settings"), new[]
        {
            new Page("Audio", Loc.T("common.audio"), Loc.T("preferences.device_sample_rate_and_buffer"), GlyphKind.Levels),
            new Page("MIDI", "MIDI", Loc.T("preferences.controllers_nota_listens_to"), GlyphKind.MidiPort),
            new Page("Gamepads", Loc.T("preferences.gamepads"), Loc.T("preferences.play_notes_or_drive_mapped"), GlyphKind.Gamepad),
            new Page("Remote", Loc.T("preferences.remote"), Loc.T("preferences.phones_and_tablets_on_this"), GlyphKind.Phone),
        }),
        (Loc.T("preferences.plug_ins"), new[]
        {
            new Page("Plug-ins", Loc.T("preferences.plug_ins"), Loc.T("preferences.where_nota_looks_for_vst3"), GlyphKind.Plug),
            new Page("Downloads", Loc.T("preferences.downloads"), Loc.T("preferences.plug_ins_sample_packs_and"), GlyphKind.Downloads),
        }),
        (Loc.T("preferences.general"), new[]
        {
            new Page("Library", Loc.T("preferences.library"), Loc.T("preferences.content_folders_and_version_history"), GlyphKind.Library),
            new Page("Appearance", Loc.T("preferences.appearance"), Loc.T("preferences.theme_scale_waveforms_and_ai"), GlyphKind.Appearance),
            new Page("Shortcuts", Loc.T("preferences.shortcuts"), Loc.T("preferences.keyboard_reference"), GlyphKind.Shortcuts),
            new Page("Updates", Loc.T("preferences.updates"), Loc.T("preferences.new_versions_of_nota"), GlyphKind.Update),
        }),
    };


    private readonly MainWindowViewModel? _main;

    private readonly IPluginCatalog _catalog = App.Services.GetRequiredService<IPluginCatalog>();
    private readonly IAudioDeviceService _audioDevices = App.Services.GetRequiredService<IAudioDeviceService>();
    private readonly IMidiDeviceService _midiDevices = App.Services.GetRequiredService<IMidiDeviceService>();
    private readonly IGamepadService _gamepads = App.Services.GetRequiredService<IGamepadService>();

    private static readonly double[] SampleRates = { 0, 44100, 48000, 88200, 96000 };
    private static int[] BufferSizes => AudioFormatStatus.BufferSizes;
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
        Title = Loc.T("common.settings");
        Width = 880;
        Height = 640;
        // The widest page (Appearance: two theme cards beside the labels) still fits; the
        // interface scale multiplies this, so a live switch to 150 / 200 % grows the window
        // instead of clipping it (spec 027).
        MinWidth = 800;
        MinHeight = 480;
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
        // The driver restarted on its own (its panel changed the buffer, or it failed over to
        // WASAPI): the Audio pane's rows and latency describe the old state — rebuild them.
        if (_main is not null)
        {
            _main.AudioDeviceRestarted += OnAudioDeviceRestarted;
            Closed += (_, _) => _main.AudioDeviceRestarted -= OnAudioDeviceRestarted;
        }
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
        foreach (var p in Pages) { if (p.Id == title) { Select(i); return; } i++; }
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
        // The subtitle takes the width left beside the title and wraps: a longer translation must
        // not run under a control placed at the right (the Downloads switch).
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 10, Margin = new Thickness(0, 0, 0, 20) };
        header.Children.Add(new TextBlock { Text = page.Title, FontSize = 17, FontWeight = FontWeight.SemiBold, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Bottom });
        var subtitle = new TextBlock { Text = page.Subtitle, FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Bottom, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 2) };
        Grid.SetColumn(subtitle, 1);
        header.Children.Add(subtitle);

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
        UpdatesIndex => UpdatesPane(),
        _ => ShortcutsPane(),
    };

    // ---- Audio ------------------------------------------------------------

    private Control AudioPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.audio_settings_are_unavailable_in"));

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
        foreach (var sr in SampleRates) srCombo.Items.Add(sr == 0 ? Loc.T("preferences.device_default") : NotaNum.Unit(sr, "0", "Hz"));
        srCombo.SelectedIndex = IndexOf(SampleRates, cfg.SampleRate);
        srCombo.SelectionChanged += (_, _) => { int i = srCombo.SelectedIndex; if (i >= 0) { engine.SetAudioSampleRate(SampleRates[i]); Apply(); } };

        var bufCombo = Combo(150);
        foreach (var b in BufferSizes) bufCombo.Items.Add(b == 0 ? Loc.T("preferences.device_default") : b.ToString());
        bufCombo.SelectedIndex = IndexOf(BufferSizes, cfg.BufferFrames);
        bufCombo.SelectionChanged += (_, _) => { int i = bufCombo.SelectedIndex; if (i >= 0) { engine.SetAudioBufferFrames(BufferSizes[i]); Apply(); } };

        _latencyValue = new TextBlock { FontSize = 12, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary };
        _latencyDetail = new TextBlock { FontSize = 12, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap };
        UpdateLatency();
        var latency = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _latencyValue, _latencyDetail } };

        var device = new List<Control>();
        // Audio system (Windows, specs/049): WASAPI, or ASIO through the interface's own driver.
        bool asioUi = OperatingSystem.IsWindows() && engine.AsioAvailable;
        bool asio = asioUi && engine.AudioSystem == AudioSystem.Asio;
        if (asioUi) AudioSystemRows(device, engine, asio);
        if (asio)
        {
            AsioRows(device, engine, srCombo);
            device.Add(Row(Loc.T("preferences.latency"), latency));
            var asioBody = Sections(
                Section(Loc.T("preferences.audio_device"), 6, device.ToArray()),
                InputsSection(engine),
                Section(Loc.T("preferences.test"), 8, TestToneRow(), CpuCheckRow(engine)));
            _loading = false;
            return asioBody;
        }
        device.AddRange(new[]
        {
            Row(Loc.T("common.output"), outputCombo), Row(Loc.T("common.input"), inputCombo),
            Row(Loc.T("preferences.sample_rate"), srCombo), Row(Loc.T("preferences.buffer_size"), bufCombo),
        });
        // WASAPI exclusive (Windows only): lets Nota own the output device for the lowest
        // latency, at the cost of muting other apps. The device then dictates the sample rate /
        // buffer, so those are ignored while it's on. If the device refuses exclusive (busy /
        // not allowed), the backend falls back to shared mode and the latency line says so.
        if (OperatingSystem.IsWindows())
        {
            device.Add(Row(Loc.T("preferences.exclusive_mode"), SwitchRow(Loc.T("preferences.wasapi_exclusive_lowest_latency_mutes"), cfg.WasapiExclusive, on =>
            {
                if (_loading) return;
                engine.SetAudioWasapiExclusive(on);
                Apply();
            })));
            device.Add(Row("", Caption(Loc.T("preferences.the_devices_native_sample_rate"))));
        }
        device.Add(Row(Loc.T("preferences.latency"), latency));

        var body = Sections(
            Section(Loc.T("preferences.audio_device"), 6, device.ToArray()),
            InputsSection(engine),
            Section(Loc.T("preferences.test"), 8, TestToneRow(), CpuCheckRow(engine)));
        _loading = false;
        return body;
    }

    // "Audio system  [WASAPI | ASIO]". With no ASIO driver installed the ASIO item is disabled
    // and a caption says what to install (contracts/preferences-ui.md).
    private void AudioSystemRows(List<Control> device, IAudioEngine engine, bool asio)
    {
        var drivers = engine.AsioDrivers();
        var combo = Combo(150);
        combo.Items.Add("WASAPI");
        combo.Items.Add(new ComboBoxItem { Content = Loc.T("preferences.asio_caps"), IsEnabled = drivers.Count > 0 || asio });
        combo.SelectedIndex = asio ? 1 : 0;
        combo.SelectionChanged += (_, _) =>
        {
            if (_loading || combo.SelectedIndex < 0) return;
            bool toAsio = combo.SelectedIndex == 1;
            engine.SetAudioSystem(toAsio ? AudioSystem.Asio : AudioSystem.Wasapi);
            if (toAsio && engine.AsioDriver.Length == 0 && drivers.Count > 0) engine.SetAsioDriver(drivers[0].Uid);
            Apply();
            RebuildAudioPane();
        };
        device.Add(Row(Loc.T("preferences.audio_system"), combo));
        if (drivers.Count == 0 && !asio)
            device.Add(Row("", Caption(RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? Loc.T("preferences.no_asio_driver_found_few")
                : Loc.T("preferences.no_asio_driver_found_install"))));
    }

    // Driver · Output pair · Input pair · Sample rate · Buffer size (read-only) under ASIO.
    // Pairs come from the open driver, so they show once it runs; the buffer size is the
    // driver's own setting (FR-008), changed in its panel ("Driver settings…").
    private void AsioRows(List<Control> device, IAudioEngine engine, ComboBox srCombo)
    {
        bool running = engine.ActiveAudioSystem == AudioSystem.Asio;
        var drivers = engine.AsioDrivers();
        var driverCombo = Combo(280);
        string saved = engine.AsioDriver;
        int selected = -1;
        for (int i = 0; i < drivers.Count; i++)
            if (string.Equals(drivers[i].Uid, saved, StringComparison.OrdinalIgnoreCase)) selected = i;
        // A saved driver that is no longer installed stays visible as the choice, so picking
        // any installed one (even the only one) is a real change that stages and applies it.
        bool missing = saved.Length > 0 && selected < 0;
        int offset = missing ? 1 : 0;
        if (missing) driverCombo.Items.Add(Loc.T("preferences.saved_driver_not_found"));
        foreach (var d in drivers) driverCombo.Items.Add(d.Name);
        driverCombo.SelectedIndex = missing ? 0 : selected >= 0 ? selected : (drivers.Count > 0 ? 0 : -1);
        driverCombo.SelectionChanged += (_, _) =>
        {
            int i = driverCombo.SelectedIndex - offset;
            if (_loading || i < 0 || i >= drivers.Count) return;
            engine.SetAsioDriver(drivers[i].Uid);   // a different driver: start from its first pairs
            engine.SetAsioPair(false, 0);
            engine.SetAsioPair(true, 0);
            Apply();
            RebuildAudioPane();
        };
        // Running: the driver's own panel (a change there comes back as a reset event). In the
        // WASAPI fallback: try the driver again — it may be free or plugged in now (US4-3).
        var panel = Btn(running ? Loc.T("preferences.driver_settings") : Loc.T("common.try_again"));
        panel.IsEnabled = running || drivers.Count > 0;
        panel.Click += (_, _) =>
        {
            if (running) { engine.OpenAsioControlPanel(); return; }
            int i = driverCombo.SelectedIndex - offset;   // retry the driver shown, not a stale one
            if (i >= 0 && i < drivers.Count) engine.SetAsioDriver(drivers[i].Uid);
            Apply();
            RebuildAudioPane();
        };
        device.Add(Row(Loc.T("preferences.driver"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { driverCombo, panel } }));

        if (running)
        {
            device.Add(Row(Loc.T("common.output"), PairCombo(engine, input: false, engine.AsioChannels(false))));
            var ins = engine.AsioChannels(true);
            device.Add(Row(Loc.T("common.input"), ins.Count > 0 ? PairCombo(engine, input: true, ins) : Caption(Loc.T("preferences.no_inputs_on_this_driver"))));
        }

        // Sample rate is a request: a rate the driver refuses is reverted and explained (FR-007).
        // The view model remembers the refusal, so it also shows after an apply that waited for
        // the driver's panel to close (the pane is rebuilt then).
        var refused = Caption("");
        refused.Foreground = NotaPalette.Warning;
        void ShowRefusal()
        {
            double r = _main?.RefusedSampleRate ?? 0;
            refused.Text = r > 0 ? Loc.F("preferences.the_driver_doesnt_support", NotaNum.Unit(r / 1000, "0.#", "kHz")) : "";
            refused.IsVisible = r > 0;
        }
        ShowRefusal();
        srCombo.SelectionChanged += (_, _) =>
        {
            int i = srCombo.SelectedIndex;
            if (_loading || i <= 0 || engine.ActiveAudioSystem != AudioSystem.Asio) return;
            // The earlier handler applied the rate. A rate the driver refused is dropped by the
            // engine — the saved config goes back to "device default" — so the row follows it.
            ShowRefusal();
            if (_main?.RefusedSampleRate != SampleRates[i]) return;
            _loading = true;
            srCombo.SelectedIndex = 0;
            _loading = false;
        };
        device.Add(Row(Loc.T("preferences.sample_rate"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { srCombo, refused } }));

        int buf = engine.NegotiatedBufferFrames;
        device.Add(Row(Loc.T("preferences.buffer_size"), Caption(running && buf > 0
            ? Loc.F("preferences.frames_set_in_the_drivers", buf)
            : Loc.T("preferences.set_in_the_drivers_settings"))));
    }

    // Channel pairs of the open driver, labelled from its own names ("Out 1 + 2", "Analog 3 + 4");
    // a last odd channel is a mono pair.
    private ComboBox PairCombo(IAudioEngine engine, bool input, IReadOnlyList<string> channels)
    {
        var combo = Combo(280);
        var firsts = new List<int>();
        for (int c = 0; c < channels.Count; c += 2)
        {
            combo.Items.Add(PairLabel(channels[c], c + 1 < channels.Count ? channels[c + 1] : null));
            firsts.Add(c);
        }
        int staged = engine.AsioPair(input);
        combo.SelectedIndex = Math.Max(0, firsts.IndexOf(staged));
        combo.SelectionChanged += (_, _) =>
        {
            int i = combo.SelectedIndex;
            if (_loading || i < 0 || i >= firsts.Count) return;
            engine.SetAsioPair(input, firsts[i]);
            Apply();
        };
        return combo;
    }

    internal static string PairLabel(string a, string? b)
    {
        if (b is null) return a;
        int p = 0;
        while (p < a.Length && p < b.Length && a[p] == b[p]) p++;
        while (p > 0 && a[p - 1] != ' ') p--;   // share whole words only ("Out 1" + "Out 2")
        return p > 0 ? $"{a} + {b[p..]}" : $"{a} + {b}";
    }

    // The audio pane's rows depend on the audio system and the open driver: rebuild it after
    // the combo that changed them has finished its event.
    private void RebuildAudioPane() => Dispatcher.UIThread.Post(() => Select(_selected));

    private void OnAudioDeviceRestarted()
    {
        if (_selected == 0) RebuildAudioPane();   // 0 = the Audio page
    }

    private Control TestToneRow()
    {
        var button = Btn(Loc.T("preferences.play_test_tone"));
        button.Width = 132;
        var note = Caption(Loc.F("preferences.hz_sine_at_14_dbfs", TestToneHz));
        button.Click += (_, _) =>
        {
            SetTone(!_toneOn);
            button.Content = _toneOn
                ? new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { new Glyph(GlyphKind.Stop, 8), new TextBlock { Text = Loc.T("preferences.stop_tone") } } }
                : Loc.T("preferences.play_test_tone");
            button.Classes.Set("primary", _toneOn);
        };
        return TestRow(Loc.T("preferences.output_check"), button, note);
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
        var button = Btn(Loc.T("preferences.cpu_check"));
        button.Width = 132;
        var result = Caption(Loc.T("preferences.measures_audio_thread_load_for"));
        button.Click += (_, _) =>
        {
            if (_cpuTimer is not null) return;
            if (engine.NegotiatedSampleRate <= 0)
            {
                result.Text = Loc.T("preferences.audio_device_not_running");
                result.Foreground = NotaPalette.Danger;
                return;
            }
            button.Content = Loc.T("preferences.measuring");
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
                    result.Text = Loc.F("preferences.measuring_s_left_now", (CpuCheckTicks - ticks + 9) / 10, load * 100);
                    return;
                }
                StopCpuCheck();
                button.Content = Loc.T("preferences.cpu_check");
                int xruns = Math.Max(0, engine.XrunCount - xrunsStart);
                var (verdict, brush) = peak > 0.9 || xruns > 0
                    ? (Loc.T("preferences.overloaded_raise_the_buffer_size"), NotaPalette.Danger)
                    : peak > 0.7
                        ? (Loc.T("preferences.close_to_the_limit_consider"), NotaPalette.Warning)
                        : (Loc.T("preferences.plenty_of_headroom"), TextSecondary);
                result.Text = Loc.F("preferences.last_check_average_peak", sum / ticks * 100, peak * 100, Loc.Plural("preferences.dropouts", xruns), verdict);
                result.Foreground = brush;
            };
            result.Text = Loc.T("preferences.measuring_5_s_left_play");
            _cpuTimer.Start();
        };
        return TestRow(Loc.T("preferences.performance"), button, result);
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
        InputSetupAccess.Push(_main.Engine);   // another device: its own switched-off inputs
        UpdateLatency();
    }

    // "11.6 ms" · "512 frames @ 44.1 kHz" — the negotiated block, not the requested one.
    // Under ASIO the driver's own output latency, and the round trip when it has inputs.
    private void UpdateLatency()
    {
        if (_main is null || _latencyValue is null || _latencyDetail is null) return;
        double sr = _main.Engine.NegotiatedSampleRate;
        int buf = _main.Engine.NegotiatedBufferFrames;
        var eng = _main.Engine;
        if (OperatingSystem.IsWindows() && eng.AudioSystem == AudioSystem.Asio && sr > 0)
        {
            string khz = NotaNum.Unit(sr / 1000, "0.#", "kHz");
            if (eng.ActiveAudioSystem != AudioSystem.Asio)
            {
                string why = eng.AudioFallbackReason;
                _latencyValue.Text = buf > 0 ? NotaNum.Unit(buf / sr * 1000, "0.0", "ms") : "";
                _latencyDetail.Text = Loc.F("preferences.asio_unavailable_wasapi", why.Length > 0 ? why : Loc.T("preferences.the_driver_didnt_start"));
            }
            else
            {
                int outLat = eng.OutputLatencyFrames > 0 ? eng.OutputLatencyFrames : buf;
                int inLat = eng.InputLatencyFrames;
                string roundTrip = inLat > 0 ? Loc.F("preferences.round_trip", NotaNum.Unit((inLat + outLat) / sr * 1000, "0.0", "ms")) : "";
                _latencyValue.Text = NotaNum.Unit(outLat / sr * 1000, "0.0", "ms");
                _latencyDetail.Text = Loc.F("preferences.asio_frames", buf, khz, roundTrip);
            }
            _latencyValue.IsVisible = _latencyValue.Text.Length > 0;
            return;
        }
        string mode = OperatingSystem.IsWindows() && _main.Engine.AudioExclusiveFallback
            ? " · " + Loc.T("preferences.exclusive_refused_shared")
            : OperatingSystem.IsWindows() && _main.Engine.GetAudioConfig().WasapiExclusive ? " · " + Loc.T("preferences.exclusive") : "";
        string rate = NotaNum.Unit(sr / 1000, "0.#", "kHz");
        (_latencyValue.Text, _latencyDetail.Text) = sr <= 0
            ? ("", Loc.T("preferences.audio_device_not_running"))
            : buf > 0
                ? (NotaNum.Unit(buf / sr * 1000, "0.0", "ms"), Loc.F("preferences.frames", buf, rate, mode))
                : (rate, mode.TrimStart(' ', '·'));
        _latencyValue.IsVisible = _latencyValue.Text.Length > 0;
    }

    // ---- MIDI -------------------------------------------------------------

    private Control MidiPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.midi_settings_are_unavailable_in"));

        var engine = _main.Engine;
        var devices = _midiDevices.InputDevices();
        var items = new List<Control> { };
        if (devices.Count == 0)
            items.Add(EmptyBox(Loc.T("preferences.no_midi_inputs_detected_connect")));
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
        items.Add(Caption(Loc.T("preferences.switch_a_controller_off_to")));
        return Sections(Section(Loc.T("preferences.midi_inputs"), 8, items.ToArray()), MpeSection(engine));
    }

    private static readonly int[] MpeBendRanges = [12, 24, 48, 96];

    // MPE: each note on its own channel (2–16) with its own bend / pressure / slide. Channel 1
    // stays an ordinary keyboard, so turning this on costs a plain controller nothing.
    private Control MpeSection(IAudioEngine engine)
    {
        var (on, range) = engine.GetMpe();
        var rangeCombo = Combo(150);
        foreach (var r in MpeBendRanges) rangeCombo.Items.Add(Loc.F("preferences.semitones", r));
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
        var sw = SwitchRow(Loc.T("preferences.per_note_expression_from_mpe"), on, v =>
        {
            rangeCombo.IsEnabled = v;
            engine.SetMpe(v, engine.GetMpe().BendRange);
            _main!.ApplyMidiSettings();
        });
        return Section("MPE", 8,
            Row(Loc.T("preferences.expression"), sw),
            Row(Loc.T("preferences.bend_range"), rangeCombo),
            Row("", Caption(Loc.T("preferences.channels_2_16_each_carry") +
                            Loc.T("preferences.nota_synth_volt_aurora_operator"))));
    }

    // ---- Gamepads -----------------------------------------------------------

    private Control GamepadsPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.gamepad_settings_are_unavailable_in"));
        if (!OperatingSystem.IsMacOS()) return Caption(Loc.T("preferences.gamepad_input_is_macos_only"));

        // The master switch lives in Settings (not the native engine): the pad thread
        // always runs once started so new pads are still detected, but button edges only
        // reach the engine while this is on.
        var enable = SwitchRow(Loc.T("preferences.use_a_connected_gamepad_for"), _main.Settings.Current.GamepadEnabled, on =>
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
                padHost.Content = EmptyBox(Loc.T("preferences.no_gamepad_detected_connect_one"));
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
                var cap = new TextBlock { Text = Loc.T("preferences.last"), FontSize = 11, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
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
                     (Loc.T("preferences.face_buttons"), "C4  D4  E4  F4"), (Loc.T("preferences.shoulder_buttons"), "G4  A4  B4  C5"),
                     (Loc.T("preferences.d_pad_up_down"), Loc.T("preferences.octave_1_1")), (Loc.T("preferences.d_pad_left_right"), Loc.T("preferences.velocity")),
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
            Section(Loc.T("preferences.gamepad_input"), 10, enable, padHost),
            Section(Loc.T("preferences.layout"), 10,
                Table(map),
                Caption(Loc.T("preferences.notes_go_to_the_armed"), muted: true),
                Caption(Loc.T("preferences.any_button_can_be_mapped"))));
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
        var addBtn = Btn(Loc.T("preferences.add_folder"));
        var removeBtn = Btn(Loc.T("preferences.remove"));

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
                var tag = new TextBlock { Text = IsDefault(path) ? Loc.T("preferences.default") : "", FontSize = 10, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
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
            var folders = await this.PickFoldersAsync(new FolderPickerOpenOptions { Title = Loc.T("preferences.add_plugin_scan_folder"), AllowMultiple = false });
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
        var rescan = Btn(Loc.T("preferences.rescan_plugins"));
        rescan.Click += async (_, _) =>
        {
            if (_main is null) { rescanStatus.Text = Loc.T("preferences.plugin_scanning_is_unavailable_in"); return; }
            rescan.IsEnabled = false;
            rescan.Content = Loc.T("preferences.scanning");
            int folders = _catalog.ScanPathCount;
            rescanStatus.Text = Loc.Plural("preferences.searching_folders", folders);
            try
            {
                // An explicit Rescan also retries plugins that failed before.
                var progress = new Progress<PluginScanProgress>(p => rescanStatus.Text = PluginScan.Describe(p));
                int? n = await PluginScan.RescanAsync(_main, progress, retryFailed: true);
                int failed = _catalog.FailedCount;
                rescanStatus.Text = n is not { } c ? Loc.T("preferences.scanner_worker_not_found")
                    : Loc.Plural("preferences.found_plugins", c) +
                      (failed > 0 ? Loc.F("preferences.couldnt_be_loaded_see_plugin", failed) : "");
            }
            finally { rescan.IsEnabled = true; rescan.Content = Loc.T("preferences.rescan_plugins"); }
        };

        var settings = App.Services.GetRequiredService<ISettingsService>();
        var openOnInsert = SwitchRow(Loc.T("preferences.open_plug_in_window_on"), settings.Current.OpenPluginWindowOnInsert, on =>
        {
            settings.Current.OpenPluginWindowOnInsert = on;
            settings.Save();
        });

        return Sections(
            Section(Loc.T("preferences.plug_in_window"), 10, openOnInsert,
                Caption(Loc.T("preferences.adding_a_plug_in_from"))),
            Section(Loc.T("preferences.plugin_scan_folders"), 10,
                well,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addBtn, removeBtn } },
                Caption(Loc.T("preferences.extra_folders_are_searched_for"))),
            Section(Loc.T("preferences.scan"), 10,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14, Children = { rescan, rescanStatus } }));
    }

    // ---- Library ----------------------------------------------------------

    private Control LibraryPane()
    {
        if (_main is null) return Caption(Loc.T("preferences.content_folders_are_unavailable_in"));
        var settings = _main.Settings;
        return Sections(Section(Loc.T("preferences.content_folders"), 10, Table(new List<Control>
        {
            FolderRow(Loc.T("preferences.samples"),
                () => settings.Current.SamplesFolder, () => settings.ResolvedSamplesFolder(),
                path => { settings.Current.SamplesFolder = path; settings.Save(); _main.Browser.RebuildSamples(); }),
            FolderRow(Loc.T("preferences.projects"),
                () => settings.Current.ProjectsFolder, () => settings.ResolvedProjectsFolder(),
                path => { settings.Current.ProjectsFolder = path; settings.Save(); _main.Browser.RebuildProjects(); }),
        })), VersionHistorySection(_main));
    }

    // On by default. Turning it off erases history, so it asks first — and stays on if not confirmed.
    private Control VersionHistorySection(MainWindowViewModel main)
    {
        var row = (StackPanel)SwitchRow(Loc.T("preferences.keep_a_version_history_of"), main.Settings.Current.KeepVersionHistory, _ => { });
        var sw = (ToggleSwitch)row.Children[0];
        sw.Changed += async on =>
        {
            if (on) { main.SetKeepVersionHistory(true); return; }
            bool ok = await new ConfirmWindow(Loc.T("preferences.turn_off_version_history"),
                Loc.T("preferences.this_erases_the_version_history"),
                Loc.T("preferences.turn_off_and_erase"), Loc.T("common.cancel")).ShowDialog<bool>(this);
            if (ok) main.SetKeepVersionHistory(false);
            else sw.IsOn = true;
        };
        return Section(Loc.T("preferences.version_history"), 10, row,
            Caption(Loc.T("preferences.every_save_records_a_version")));
    }

    private Control FolderRow(string label, Func<string> get, Func<string> resolved, Action<string> set)
    {
        bool custom = !string.IsNullOrWhiteSpace(get());
        var path = new TextBlock
        {
            Text = custom ? get() : resolved(), FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextPrimary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var tag = new TextBlock { Text = custom ? Loc.T("preferences.custom") : Loc.T("preferences.default"), FontSize = 10, Foreground = TextTertiary };
        var choose = Btn(Loc.T("preferences.choose"));
        choose.Click += async (_, _) =>
        {
            Activate();
            var folders = await this.PickFoldersAsync(new FolderPickerOpenOptions { Title = Loc.F("preferences.choose_folder", label), AllowMultiple = false });
            if (folders.Count == 0) return;
            var p = folders[0].TryGetLocalPath();
            if (p is null) return;
            set(p);
            path.Text = p;
            tag.Text = Loc.T("preferences.custom");
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
        foreach (var label in new[] { Loc.T("preferences.dark"), Loc.T("preferences.light"), Loc.T("preferences.follow_system") })
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
            darkLabel.Text = follow ? Loc.T("preferences.in_dark_mode") : Loc.T("preferences.dark");
            lightLabel.Text = follow ? Loc.T("preferences.in_light_mode") : Loc.T("preferences.light");
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

    // A miniature Nota window in one theme's own colours (with the chosen accent), whichever
    // theme is live, so each card previews itself: title bar, transport with the view keys, browser, arrangement
    // lanes with clips in the first track colours and the playhead, and the device panel.
    private sealed class ThemePreview : Control
    {
        public const double W = 184, H = 112;
        private readonly NotaColourTheme _theme;
        public ThemePreview(NotaColourTheme theme) { _theme = theme; Width = W; Height = H; }

        // The theme as it is worn with the chosen accent (spec 084), so the card previews the pair.
        private IBrush B(SolidColorBrush slot) => new ImmutableSolidColorBrush(NotaPalette.ColorIn(slot, NotaThemeService.Worn(_theme)));

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

    // Smooth / Solid / Bars for the arrangement's audio-clip waveforms (Solid and Bars also set
    // the browser preview). Saving
    // fires Settings.Changed, which the lanes and the preview apply at once.
    private static Control WaveformPicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(int Style, ToggleButton Btn)>();
        foreach (var (style, label) in new[] { (0, Loc.T("preferences.smooth")), (1, Loc.T("preferences.solid")), (2, Loc.T("preferences.bars")) })
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

    // Left / Right for the arrangement's track-header column (#72). Saving fires
    // Settings.Changed, which moves the column at once and ticks the View menu item.
    // The same event repaints these buttons, so a change from the View menu updates an
    // Appearance pane that is already open.
    private static Control HeaderSidePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(bool Right, ToggleButton Btn)>();
        void ShowSaved()
        {
            bool right = settings.Current.ArrangementHeadersRight;
            foreach (var (br, bb) in buttons) bb.IsChecked = br == right;
        }
        foreach (var (right, label) in new[] { (false, Loc.T("preferences.left")), (true, Loc.T("preferences.right")) })
        {
            var r = right;
            var btn = new ToggleButton { Classes = { "seg" }, FontSize = 12, Content = label };
            btn.Click += (_, _) =>
            {
                if (settings.Current.ArrangementHeadersRight == r) { ShowSaved(); return; }
                settings.Current.ArrangementHeadersRight = r;
                settings.Save();
            };
            buttons.Add((r, btn));
            strip.Children.Add(btn);
        }
        ShowSaved();
        var box = new Border { Classes = { "segmented" }, HorizontalAlignment = HorizontalAlignment.Left, Child = strip };
        var listening = false;
        void OnChanged() => ShowSaved();
        box.AttachedToVisualTree += (_, _) =>
        {
            ShowSaved();
            if (listening) return;
            listening = true;
            settings.Changed += OnChanged;
        };
        box.DetachedFromVisualTree += (_, _) =>
        {
            if (!listening) return;
            listening = false;
            settings.Changed -= OnChanged;
        };
        return box;
    }

    // 75–200 % for the whole interface (spec 027, #38). Applies live — this window rescales
    // under the pointer too — and is saved for the next launch.
    private static Control ScalePicker()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var strip = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        var buttons = new List<(int Percent, ToggleButton Btn)>();
        int current = InterfaceScale.Normalize(settings.Current.InterfaceScale);
        foreach (var percent in InterfaceScale.Presets)
        {
            var p = percent;
            var btn = new ToggleButton { Classes = { "seg" }, FontSize = 12, Content = $"{p}\u2009%", IsChecked = current == p };
            btn.Click += (_, _) =>
            {
                foreach (var (bp, bb) in buttons) bb.IsChecked = bp == p;
                if (settings.Current.InterfaceScale == p) return;
                settings.Current.InterfaceScale = p;
                settings.Save();
                NotaUiScale.Set(p);
            };
            buttons.Add((p, btn));
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
        var copyBtn = Btn(Loc.T("preferences.copy_config"));
        copyBtn.HorizontalAlignment = HorizontalAlignment.Left;
        var details = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Row(Loc.T("preferences.port"), portBox),
                Row(Loc.T("preferences.status"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, status } }),
                Row("", new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        copyBtn,
                        Caption(Loc.T("preferences.add_this_config_or_the")),
                    },
                }),
            },
        };

        bool Enabled() => settings?.Current.McpEnabled ?? false;
        void Refresh()
        {
            bool on = Enabled();
            status.Text = on ? Loc.F("preferences.listening_on_http_127_0", settings!.Current.McpPort) : Loc.T("common.off");
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
        var enable = SwitchRow(Loc.T("preferences.enable_mcp_server_let_an"), Enabled(), ApplyMcp);
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
            copyBtn.Content = Loc.T("preferences.copied");
            copied?.Stop();
            copied = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            copied.Tick += (_, _) => { copied!.Stop(); copyBtn.Content = Loc.T("preferences.copy_config"); };
            copied.Start();
        };
        // Inactive.Set walks the visual tree, so the dimming waits until the pane is shown.
        details.AttachedToVisualTree += (_, _) => Refresh();

        // Piano-roll key labels: every note for beginners, C-only by default.
        var labelAll = SwitchRow(Loc.T("preferences.label_every_note_in_the"), settings?.Current.PianoRollLabelAllNotes ?? false, on =>
        {
            if (settings is null) return;
            settings.Current.PianoRollLabelAllNotes = on;
            settings.Save();
        });

        // Detail panel (#308): a selection opens a closed panel only when this is on.
        var openOnSelect = SwitchRow(Loc.T("preferences.open_the_detail_panel_when"), settings?.Current.OpenDetailOnSelect ?? false, on =>
        {
            if (settings is null) return;
            settings.Current.OpenDetailOnSelect = on;
            settings.Save();
        });

        var splitFades = SwitchRow(Loc.T("preferences.fade_audio_clips_when_splitting"), settings?.Current.SplitFades ?? true, on =>
        {
            if (settings is null) return;
            settings.Current.SplitFades = on;
            settings.Save();
            _main.Engine.SplitFades = on;
        });

        return Sections(
            Section("UI", 10,
                TopRow(Loc.T("preferences.colour_theme"), ThemePicker()),
                Row("", Caption(Loc.T("preferences.themes_change_colours_only_follow"))),
                TopRow(Loc.T("preferences.accent"), AccentPicker()),
                Row("", Caption(Loc.T("preferences.accent_caption"))),
                Row(Loc.T("preferences.waveform"), WaveformPicker()),
                Row("", Caption(Loc.T("preferences.how_audio_clips_draw_on"))),
                Row("", splitFades),
                Row("", Caption(Loc.T("preferences.splitting_an_audio_clip_adds"))),
                Row(Loc.T("preferences.track_headers"), HeaderSidePicker()),
                Row("", Caption(Loc.T("preferences.which_side_of_the_arrangement"))),
                Row(Loc.T("preferences.interface_scale"), ScalePicker()),
                Row("", Caption(Loc.T("preferences.makes_the_whole_interface_larger"))),
                Row(Loc.T("preferences.language"), LanguagePicker()),
                Row("", Caption(Loc.T("preferences.the_language_of_menus_panels")))),
            Section(Loc.T("common.piano_roll"), 10, labelAll,
                Caption(Loc.T("preferences.shows_every_note_name_c"))),
            Section(Loc.T("preferences.detail_panel"), 10, openOnSelect,
                Caption(Loc.T("preferences.off_a_click_only_selects"))),
            Section(Loc.T("preferences.ai_control_mcp"), 12, enable, details));
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
        (Loc.T("common.file"), new[]
        {
            ("⌘N   ⌘O", Loc.T("preferences.new_project_open_a_project")),
            ("⌘S   ⌘⇧S", Loc.T("preferences.save_records_a_version_save")),
            ("⌥⌘S", Loc.T("preferences.save_a_version_with_a")),
            ("⌘⇧E", Loc.T("preferences.export_audio")),
            ("⌘,", Loc.T("common.settings")),
            ("⌘Q", Loc.T("preferences.quit_nota_asks_about_unsaved")),
        }),
        (Loc.T("preferences.window"), new[]
        {
            ("F11", Loc.T("preferences.enter_leave_full_screen_the")),
            ("⌘⇧F", Loc.T("preferences.the_clip_editor_fills_the")),
            (Loc.T("preferences.double_click_a_panel_edge"), Loc.T("preferences.reset_the_detail_panel_the")),
        }),
        (Loc.T("preferences.command_palette"), new[]
        {
            ("⌘⇧P", Loc.T("preferences.open_close_the_command_palette")),
            ("↑ ↓   PgUp PgDn", Loc.T("preferences.move_through_the_results")),
            ("Return", Loc.T("preferences.apply_where_the_palette_was")),
            ("⌘Return", Loc.T("preferences.an_instrument_or_instrument_preset")),
            ("⌥Return", Loc.T("preferences.replace_the_selected_device_instead")),
            ("⇧Return", Loc.T("preferences.apply_and_keep_the_palette")),
            ("Tab   ⇧Tab", Loc.T("preferences.filter_all_actions_tracks_devices")),
            ("> @ + # ~", Loc.T("preferences.type_first_to_filter_actions")),
            ("Esc", Loc.T("preferences.close_and_go_back_where")),
        }),
        (Loc.T("common.transport"), new[]
        {
            ("Space", Loc.T("preferences.play_stop_stop_goes_back")),
            ("⇧Space", Loc.T("preferences.play_pause_the_playhead_stays")),
            ("Return", Loc.T("preferences.stop_again_back_to_the")),
            ("⌘R", Loc.T("common.record")),
            ("⌘.", Loc.T("preferences.discard_the_take_and_record")),
            ("⌘M", Loc.T("common.metronome")),
            ("⌘L", Loc.T("preferences.loop_on_off_loop_the")),
            (Loc.T("preferences.double_click_loop_brace"), Loc.T("preferences.loop_on_off_in_the")),
            (Loc.T("preferences.drag_loop_edge_drag_loop"), Loc.T("preferences.resize_that_end_of_the")),
            (Loc.T("preferences.right_click_ruler"), Loc.T("preferences.loop_on_off_loop_the_2")),
            ("P", Loc.T("preferences.show_playhead_scroll_the_arrangement")),
            ("⌘⇧R", Loc.T("preferences.loop_recording_overdub_every_pass")),
            (Loc.T("preferences.click_the_position_read_out"), Loc.T("preferences.switch_it_between_bars_and")),
        }),
        (Loc.T("preferences.arrangement_editing"), new[]
        {
            ("⌘Z   ⌘⇧Z", Loc.T("preferences.undo_redo")),
            ("⌘A", Loc.T("preferences.select_all_clips")),
            ("⌘⇧A", Loc.T("preferences.toggle_automation_mode")),
            ("⌘⇧M", Loc.T("preferences.toggle_the_mixer_view")),
            ("Tab", Loc.T("preferences.cycle_devices_pattern_clip_in")),
            ("Esc", Loc.T("preferences.cancel_the_current_gesture_clear")),
            ("⌘G   ⌘⇧G", Loc.T("preferences.group_ungroup_selected_tracks_on")),
            ("⌘C  ⌘X  ⌘V", Loc.T("preferences.copy_cut_paste_clips_a")),
            ("⌘⇧C", Loc.T("preferences.copy_the_selected_clips_to")),
            ("⌘⇧V", Loc.T("preferences.paste_the_last_range_as")),
            ("⌘D", Loc.T("preferences.duplicate_the_selected_clip_s")),
            ("⌘⇧D", Loc.T("preferences.duplicate_time_insert_a_copy")),
            ("⌘⇧I", Loc.T("preferences.insert_silence_push_everything_from")),
            ("⌘E", Loc.T("preferences.split_at_the_playhead_at")),
            ("⌘J", Loc.T("preferences.consolidate_the_selection_into_one")),
            ("0", Loc.T("preferences.deactivate_activate_the_selected_clip")),
            ("←   →", Loc.T("preferences.nudge_the_selected_clips_by_2")),
            ("⌥←   ⌥→", Loc.T("preferences.nudge_the_selected_clips_by")),
            ("⌥↑   ⌥↓", Loc.T("preferences.move_the_selected_clips_to")),
            (Loc.T("preferences.drag_below_the_last_track"), Loc.T("preferences.move_the_clips_to_a")),
            ("Delete", Loc.T("preferences.delete_the_selected_clip_range")),
        }),
        (Loc.T("preferences.grid_arrangement_or_clip_editor"), new[]
        {
            ("⌘1   ⌘2", Loc.T("preferences.narrow_widen_the_grid_in")),
            ("⌘3", Loc.T("preferences.triplet_grid_on_off")),
            ("⌘4", Loc.T("preferences.snap_to_grid_on_off")),
            ("⌘5", Loc.T("preferences.switch_between_the_adaptive_auto")),
            (Loc.T("preferences.alt_while_dragging"), Loc.T("preferences.move_freely_ignoring_the_grid")),
            (Loc.T("preferences.audio_clip_editor_on_adsr"), Loc.T("preferences.cmd_1_5_act_on_the_arrangement")),
        }),
        (Loc.T("preferences.tracks_after_a_click_in"), new[]
        {
            ("⌘A", Loc.T("preferences.select_all_tracks")),
            ("⇧-click   ⌘-click", Loc.T("preferences.select_a_range_of_tracks")),
            ("⌘C  ⌘X  ⌘V", Loc.T("preferences.copy_cut_paste_the_selected_2")),
            ("⌘D", Loc.T("preferences.duplicate_the_selected_tracks")),
            ("Delete", Loc.T("preferences.delete_the_selected_tracks_a")),
        }),
        (Loc.T("preferences.devices_after_a_click_on"), new[]
        {
            ("⇧-click   ⌘-click", Loc.T("preferences.select_a_range_of_devices")),
            ("Esc", Loc.T("preferences.clear_the_device_selection_so")),
            ("⌘C  ⌘X  ⌘V", Loc.T("preferences.copy_cut_paste_the_selected")),
            ("⌘D", Loc.T("preferences.duplicate_the_selected_devices_each")),
            ("⌘G", Loc.T("preferences.group_the_selected_devices_into")),
            ("⌘⇧G", Loc.T("preferences.take_the_devices_out_of")),
            ("Delete", Loc.T("preferences.delete_the_selected_devices_with")),
            (Loc.T("preferences.drag_a_selected_devices_header"), Loc.T("preferences.move_the_selected_devices_of")),
            (Loc.T("preferences.drag_a_device_onto_a"), Loc.T("preferences.move_it_there_out_of")),
            (Loc.T("preferences.drag_a_device_onto_a_2"), Loc.T("preferences.copy_it_there")),
        }),
        (Loc.T("preferences.racks"), new[]
        {
            ("Esc", Loc.T("preferences.end_macro_mapping_map_on")),
            (Loc.T("preferences.drag_a_zone_edge"), Loc.T("preferences.set_a_chains_key_velocity")),
            (Loc.T("preferences.hover_the_zones"), Loc.T("preferences.see_which_chains_a_note")),
            (Loc.T("preferences.double_click_a_chain_or"), Loc.T("preferences.rename_it")),
            (Loc.T("preferences.right_click_a_macro"), Loc.T("preferences.map_it_to_a_parameter")),
            (Loc.T("preferences.drag_a_devices_header"), Loc.T("preferences.move_it_within_its_chain")),
            (Loc.T("preferences.click_a_device"), Loc.T("preferences.select_it_for_c_x")),
        }),
        (Loc.T("preferences.modular_after_a_click_on"), new[]
        {
            ("F", Loc.T("preferences.fit_every_node_on_screen")),
            ("B", Loc.T("preferences.bypass_the_selected_device")),
            ("⌘D", Loc.T("preferences.duplicate_the_selected_device_with")),
            ("Delete", Loc.T("preferences.delete_the_selected_device_modulator")),
            ("Esc", Loc.T("preferences.show_fewer_parameters_on_every")),
        }),
        (Loc.T("preferences.session_after_a_click_in"), new[]
        {
            ("← → ↑ ↓", Loc.T("preferences.move_the_selection_between_slots")),
            ("Return", Loc.T("preferences.launch_the_selected_clip_or")),
            ("⌘C  ⌘X  ⌘V", Loc.T("preferences.copy_cut_paste_a_clip")),
            ("⌘⇧C", Loc.T("preferences.copy_the_selected_clip_group")),
            ("⌘D", Loc.T("preferences.duplicate_a_clip_into_the")),
            ("⌘I", Loc.T("preferences.insert_a_scene_below_the")),
            ("F2", Loc.T("preferences.rename_the_selected_clip_or")),
            ("Delete", Loc.T("preferences.delete_the_selected_clip_or")),
            (Loc.T("preferences.drag_clip"), Loc.T("preferences.move_it_to_another_slot")),
            (Loc.T("preferences.drag_scene"), Loc.T("preferences.reorder_scenes")),
            (Loc.T("preferences.double_click_slot"), Loc.T("preferences.edit_the_clip_an_empty")),
            (Loc.T("preferences.double_click_header"), Loc.T("preferences.rename_the_track")),
            (Loc.T("preferences.right_click_header_or_grid"), Loc.T("preferences.add_an_instrument_audio_or")),
        }),
        (Loc.T("common.piano_roll"), new[]
        {
            ("← →", Loc.T("preferences.move_notes_by_the_grid")),
            ("↑ ↓", Loc.T("preferences.transpose_by_a_semitone")),
            ("⇧↑  ⇧↓", Loc.T("preferences.transpose_by_an_octave")),
            ("⇧←  ⇧→", Loc.T("preferences.shorten_lengthen_the_notes_by")),
            ("⌥↑  ⌥↓", Loc.T("preferences.velocity_10_10_by_1")),
            (Loc.T("preferences.wheel_over_a_note"), Loc.T("preferences.change_its_velocity_the_whole")),
            ("⌘A", Loc.T("preferences.select_all_notes")),
            ("⌘D", Loc.T("preferences.duplicate_the_selection_replaces_the")),
            ("⌘C  ⌘X  ⌘V", Loc.T("preferences.copy_cut_paste_notes_paste")),
            ("Esc", Loc.T("preferences.deselect_moved_or_pasted_notes")),
            ("Delete", Loc.T("preferences.delete_the_selected_notes")),
            (Loc.T("preferences.double_click_empty_grid"), Loc.T("preferences.add_a_note_with_the")),
            (Loc.T("preferences.double_click_and_hold_drag"), Loc.T("preferences.shape_the_new_note_move")),
            (Loc.T("preferences.drag_note_edge"), Loc.T("preferences.change_the_notes_start_or")),
            (Loc.T("preferences.click_drag_the_ruler"), Loc.T("preferences.move_the_playhead_there_drag_2")),
            (Loc.T("preferences.drag_the_playhead_line"), Loc.T("preferences.scrub_the_playhead_on_the")),
            (Loc.T("preferences.drag_velocity_stem"), Loc.T("preferences.set_velocity_selected_notes_move")),
            (Loc.T("preferences.double_click_velocity_stem"), Loc.T("preferences.put_the_note_back_to")),
            (Loc.T("preferences.right_click_velocity_stem"), Loc.T("preferences.reset_velocity_from_a_menu")),
            (Loc.T("preferences.drag_in_the_bend_pres"), Loc.T("preferences.draw_the_selected_notes_mpe")),
            (Loc.T("preferences.double_click_in_an_mpe"), Loc.T("preferences.add_a_point_on_a")),
            (Loc.T("preferences.click_the_bend_lanes_range"), Loc.T("preferences.pick_how_far_the_bend")),
            (Loc.T("preferences.click_the_controller_tab"), Loc.T("preferences.show_primary_controller_lane")),
            (Loc.T("preferences.click_lane_controller_dropdown"), Loc.T("preferences.choose_lane_controller")),
            (Loc.T("preferences.controller_lane_buttons"), Loc.T("preferences.manage_controller_lanes")),
            (Loc.T("preferences.drag_in_the_sustain_lane"), Loc.T("preferences.draw_a_pedal_block_on")),
            (Loc.T("preferences.drag_in_a_controller_lane"), Loc.T("preferences.draw_the_controllers_curve_draws")),
            (Loc.T("preferences.drag_lane_divider"), Loc.T("preferences.resize_lane_remember_height")),
            (Loc.T("preferences.click_a_clip_in_the"), Loc.T("preferences.make_it_the_active_clip")),
            (Loc.T("preferences.cmd_alt_wheel"), Loc.T("preferences.zoom_rows_vertical_zoom")),
            (Loc.T("preferences.click_a_key"), Loc.T("preferences.play_it_on_the_tracks")),
            (Loc.T("preferences.drag_on_the_keys"), Loc.T("preferences.zoom_rows_down_taller_up")),
            ("⌥ + pinch", Loc.T("preferences.zoom_rows_trackpad")),
            ("⇧F", Loc.T("preferences.fit_to_notes")),
            ("⌥⇧F", Loc.T("preferences.fold_only_the_scales_notes")),
        }),
        (Loc.T("preferences.audio_clip_editor"), new[]
        {
            (Loc.T("preferences.click_drag_the_ruler"), Loc.T("preferences.move_the_playhead_there_drag")),
            (Loc.T("preferences.drag_the_playhead_line"), Loc.T("preferences.scrub_the_playhead")),
            (Loc.T("preferences.double_click_the_wave"), Loc.T("preferences.add_a_warp_marker_on")),
            (Loc.T("preferences.drag_a_warp_marker"), Loc.T("preferences.align_the_audio_to_the")),
            ("Esc", Loc.T("preferences.while_dragging_a_warp_marker")),
            (Loc.T("preferences.drag_double_click_tempo"), Loc.T("preferences.set_the_tempo_the_clip")),
        }),
        (Loc.T("preferences.play_notes_computer_keyboard"), new[]
        {
            ("A S D F G H J K", Loc.T("preferences.white_keys_from_c4")),
            ("W E · T Y U", Loc.T("preferences.black_keys_sharps")),
            ("Z / X", Loc.T("preferences.shift_octave_down_up")),
            ("C / V", Loc.T("preferences.lower_raise_velocity")),
        }),
        (Loc.T("preferences.browser_preview"), new[]
        {
            ("↑ ↓", Loc.T("preferences.move_through_samples_presets_and")),
            (Loc.T("preferences.click_play_button"), Loc.T("preferences.play_stop_the_selected_sample")),
            (Loc.T("preferences.click_waveform"), Loc.T("preferences.play_from_that_point")),
            (Loc.T("preferences.click_wave_spec"), Loc.T("preferences.switch_the_well_between_the")),
            (Loc.T("preferences.scroll_on_the_track_button"), Loc.T("preferences.step_through_the_demo_tracks")),
            (Loc.T("preferences.alt_click_volume"), Loc.T("preferences.type_the_preview_volume_double")),
        }),
        (Loc.T("preferences.gamepad_macos"), new[]
        {
            (Loc.T("preferences.face_buttons"), Loc.T("preferences.play_c4_d4_e4_f4")),
            (Loc.T("preferences.shoulder_buttons"), Loc.T("preferences.play_g4_a4_b4_c5")),
            ("D-pad ↑ / ↓", Loc.T("preferences.shift_the_gamepad_octave")),
            ("D-pad ← / →", Loc.T("preferences.lower_raise_velocity")),
            (Loc.T("preferences.any_button"), Loc.T("preferences.mappable_through_midi_learn_a")),
            (Loc.T("preferences.sticks_triggers"), Loc.T("preferences.mappable_as_continuous_controls_a")),
        }),
        (Loc.T("preferences.mouse"), new[]
        {
            (Loc.T("preferences.double_click_clip"), Loc.T("preferences.open_in_the_clip_editor")),
            (Loc.T("preferences.double_click_a_track_header"), Loc.T("preferences.open_its_devices_in_the")),
            (Loc.T("preferences.double_click_a_browser_instrument"), Loc.T("preferences.swap_it_onto_the_selected")),
            (Loc.T("preferences.alt_double_click_a_browser"), Loc.T("preferences.always_start_a_new_track")),
            (Loc.T("preferences.right_click_the_metronome"), Loc.T("preferences.count_in_before_play_record")),
            (Loc.T("preferences.drag_an_audio_clips_adsr"), Loc.T("preferences.shape_attack_decay_sustain_release")),
            (Loc.T("preferences.click_track_header"), Loc.T("preferences.add_the_track_to_the")),
            (Loc.T("preferences.click_a_groups_link_key"), Loc.T("preferences.link_the_groups_tracks_arming")),
            (Loc.T("preferences.click_a_clip_or_arm"), Loc.T("preferences.in_a_linked_group_select")),
            (Loc.T("preferences.right_click_track_headers"), Loc.T("preferences.a_multi_selection_gets_its")),
            (Loc.T("preferences.drag_a_tracks_bottom_edge"), Loc.T("preferences.resize_the_track_header_or")),
            (Loc.T("preferences.drag_a_tracks_bottom_edge_2"), Loc.T("preferences.resize_every_track_at_once")),
            (Loc.T("preferences.double_click_a_tracks_bottom"), Loc.T("preferences.back_to_the_standard_height")),
            (Loc.T("preferences.drag_a_returns_or_the"), Loc.T("preferences.resize_that_row_header_or")),
            ("⌘-click / ⇧-click return headers", Loc.T("preferences.select_several_returns_they_resize")),
            (Loc.T("preferences.click_a_height_key_in"), Loc.T("preferences.set_every_track_to_compact")),
            (Loc.T("preferences.drag_the_track_list_edge"), Loc.T("preferences.widen_or_narrow_the_track")),
            (Loc.T("preferences.drag_sections_lane"), Loc.T("preferences.mark_a_new_section_over")),
            (Loc.T("preferences.click_section"), Loc.T("preferences.jump_the_playhead_to_its")),
            (Loc.T("preferences.double_click_section"), Loc.T("preferences.rename_it_right_click_reset")),
            (Loc.T("preferences.return_esc_renaming_a_section"), Loc.T("preferences.keep_the_name_empty_automatic")),
            (Loc.T("preferences.drag_section"), Loc.T("preferences.rearrange_the_song_it_and")),
            (Loc.T("preferences.drag_section_2"), Loc.T("preferences.move_only_the_section_marker")),
            (Loc.T("preferences.drag_section_edge"), Loc.T("preferences.resize_it_snapped_to_bars")),
            (Loc.T("preferences.right_click_section"), Loc.T("preferences.move_earlier_later_duplicate_or")),
            (Loc.T("preferences.drag_overview_strip"), Loc.T("preferences.drag_sideways_to_scroll_up")),
            (Loc.T("preferences.drag_overview_edge"), Loc.T("preferences.zoom_by_resizing_the_viewport")),
            (Loc.T("preferences.double_click_overview"), Loc.T("preferences.fit_the_whole_project_on")),
            (Loc.T("preferences.drag_a_device_header"), Loc.T("preferences.reorder_devices_or_right_click")),
            (Loc.T("preferences.right_click_a_device_header"), Loc.T("preferences.a_b_compare_move_copy")),
            (Loc.T("preferences.delete_device_selected"), Loc.T("preferences.remove_the_selected_device_the")),
            (Loc.T("preferences.drag_an_audio_clip_onto"), Loc.T("preferences.load_its_audio_into_the")),
            (Loc.T("preferences.drag_an_audio_clip_out"), Loc.T("preferences.take_it_as_a_sample")),
            (Loc.T("preferences.drag_clip_2"), Loc.T("preferences.position_freely_ignoring_the_grid")),
            (Loc.T("preferences.wheel_over_the_device_chain"), Loc.T("preferences.scroll_it_sideways_over_an")),
            (Loc.T("preferences.drag_knob"), Loc.T("preferences.change_a_device_value_hold")),
            (Loc.T("preferences.drag_slider"), Loc.T("preferences.left_right_or_up_down")),
            (Loc.T("preferences.click_a_knob_fader_or"), Loc.T("preferences.type_a_value_units_like")),
            (Loc.T("preferences.return_esc_tab_tab_typing"), Loc.T("preferences.set_it_cancel_set_it")),
            (Loc.T("preferences.click_a_graph_handle_envelope"), Loc.T("preferences.type_its_values_a_node")),
            (Loc.T("preferences.double_click_a_knob_fader"), Loc.T("preferences.reset_the_value_to_its")),
            (Loc.T("preferences.double_click_a_mod_matrix"), Loc.T("preferences.clear_the_route_volt_aurora")),
            (Loc.T("preferences.right_click_a_knob_fader"), Loc.T("preferences.type_value_reset_to_default")),
            (Loc.T("preferences.drag_jack_jack"), Loc.T("preferences.patch_a_cable_in_nota")),
            (Loc.T("preferences.click_a_rhythm_step"), Loc.T("preferences.step_on_off_accent_quiet")),
            (Loc.T("preferences.click_a_rhythm_voice"), Loc.T("preferences.select_and_play_it_drop")),
        }),
    };

    private Control ShortcutsPane()
    {
        var groups = new StackPanel { Spacing = 18 };
        var filter = new TextBox { PlaceholderText = Loc.T("preferences.filter_shortcuts"), Text = _shortcutFilter, Classes = { "search" }, FontSize = 12 };

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
            if (groups.Children.Count == 0) groups.Children.Add(Caption(Loc.T("preferences.nothing_matches")));
        }
        filter.TextChanged += (_, _) => { _shortcutFilter = filter.Text ?? ""; Render(); };
        Render();

        return new StackPanel { Spacing = 18, Children = { VelocityWheelSection(), SearchField(filter, 320), groups } };
    }

    private static readonly double[] VelocityWheelSteps = { 0, 0.25, 0.5, 0.75, 1, 1.25, 1.5, 1.75, 2 };

    // ⇧ + wheel over a piano-roll note (specs/063): its direction and step, beside the reference.
    private Control VelocityWheelSection()
    {
        var settings = App.Services.GetService<ISettingsService>();
        var step = Combo(150);
        foreach (var m in VelocityWheelSteps) step.Items.Add(m == 0 ? Loc.T("common.off") : $"{m.ToString("0.##", NotaNum.Culture)}×");
        int idx = Array.FindIndex(VelocityWheelSteps, m => Math.Abs(m - PianoRollView.VelocityWheelMultiplier) < 1e-6);
        step.SelectedIndex = idx >= 0 ? idx : Array.IndexOf(VelocityWheelSteps, 1.0);
        step.SelectionChanged += (_, _) =>
        {
            int k = step.SelectedIndex;
            if (k < 0) return;
            PianoRollView.VelocityWheelMultiplier = VelocityWheelSteps[k];
            if (settings is null) return;
            settings.Current.PianoRollVelocityWheelMultiplier = VelocityWheelSteps[k];
            settings.Save();
        };
        var reverse = SwitchRow(Loc.T("preferences.wheel_up_lowers_the_velocity"), PianoRollView.VelocityWheelReversed, on =>
        {
            PianoRollView.VelocityWheelReversed = on;
            if (settings is null) return;
            settings.Current.PianoRollVelocityWheelReversed = on;
            settings.Save();
        });
        return Section(Loc.T("preferences.velocity_wheel"), 8,
            Row(Loc.T("preferences.direction"), reverse),
            Row(Loc.T("preferences.step"), step),
            Row("", Caption(Loc.T("preferences.wheel_over_a_note_in"))));
    }

    private static readonly HashSet<string> NamedKeys = new(StringComparer.Ordinal) { "Space", "Return", "Delete", "Tab", "Esc", "PgUp", "PgDn", "⌘Return", "⌥Return", "⇧Return", "⇧Tab", "⇧Space" };

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
        MinWidth = width, MaxWidth = width * 1.7, FontSize = 12, CornerRadius = NotaRadius.Tile, HorizontalAlignment = HorizontalAlignment.Left,
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
        TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0),
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
        combo.Items.Add(Loc.T("preferences.system_default"));
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
