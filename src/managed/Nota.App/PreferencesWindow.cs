// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences (mockup 1h): 760×560 with a 170px sidebar (Audio / MIDI /
// Plug-ins / Library / Appearance / Shortcuts) and a content pane per section.
// Audio device / sample-rate / buffer are persisted natively (audio.json) and
// applied by restarting the backend (MainWindowViewModel.ApplyAudioSettings);
// toolbar side persists via SettingsViewModel; scan folders via the catalog.
// Test-tone / CPU-check are flagged (NaBadge) — not wired.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed class PreferencesWindow : NotaWindow
{
    private static readonly IBrush Panel = NotaPalette.SurfaceCard;
    private static readonly IBrush Sidebar = NotaPalette.SurfaceInset;
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush Raised = NotaPalette.SurfaceRaised;
    private static readonly IBrush Divider = NotaPalette.BorderDefault;
    private static readonly IBrush BorderStrong = NotaPalette.BorderStrong;
    private static readonly IBrush Brass = NotaPalette.Accent;
    private static readonly IBrush AccentBright = NotaPalette.AccentBright;
    private static readonly IBrush AccentSubtle = NotaPalette.AccentSubtle;
    private static readonly IBrush Success = NotaPalette.Success;
    private static readonly IBrush OnAccent = NotaPalette.TextOnAccent;
    private static readonly IBrush TextPrimary = NotaPalette.TextPrimary;
    private static readonly IBrush TextSecondary = NotaPalette.TextSecondary;
    private static readonly IBrush TextTertiary = NotaPalette.TextTertiary;

    private static readonly string[] Sections = { "Audio", "MIDI", "Gamepads", "Plug-ins", "Library", "Appearance", "Shortcuts" };

    private readonly ObservableCollection<string> _paths = new();
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
    private TextBlock? _audioStatus;

    private readonly ContentControl _content = new();
    private readonly List<Border> _navItems = new();

    public PreferencesWindow(SettingsViewModel vm, MainWindowViewModel? main = null)
    {
        _ = vm;   // toolbar-side pref retired with the fixed 1b layout
        _main = main;
        Title = "Preferences";
        Width = 760;
        Height = 596;   // + title-bar band
        Background = Panel;

        var nav = new StackPanel { Spacing = 2, Margin = new Thickness(8) };
        for (int i = 0; i < Sections.Length; i++)
        {
            int idx = i;
            var item = new Border { CornerRadius = new CornerRadius(5), Padding = new Thickness(10, 6), Cursor = new Cursor(StandardCursorType.Hand) };
            item.Child = new TextBlock { Text = Sections[i], FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
            item.PointerPressed += (_, _) => Select(idx);
            _navItems.Add(item);
            nav.Children.Add(item);
        }
        var sidebar = new Border { Width = 170, Background = Sidebar, BorderBrush = Divider, BorderThickness = new Thickness(0, 0, 1, 0), Child = nav };

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(sidebar);
        Grid.SetColumn(_content, 1);
        grid.Children.Add(_content);
        SetBody(grid);

        Select(0);
    }

    private void Select(int index)
    {
        for (int i = 0; i < _navItems.Count; i++)
        {
            bool on = i == index;
            _navItems[i].Background = on ? AccentSubtle : Brushes.Transparent;
            ((TextBlock)_navItems[i].Child!).Foreground = on ? AccentBright : TextSecondary;
            ((TextBlock)_navItems[i].Child!).FontWeight = on ? FontWeight.SemiBold : FontWeight.Normal;
        }
        // Padding lives inside the scrolled content: a ScrollViewer's own Padding isn't part of
        // its extent, so the last rows of a long pane (Shortcuts) couldn't be scrolled into view.
        _content.Content = new ScrollViewer { Content = new Border { Padding = new Thickness(20, 18), Child = BuildPane(index) } };
    }

    private Control BuildPane(int index) => index switch
    {
        0 => AudioPane(),
        1 => MidiPane(),
        2 => GamepadsPane(),
        3 => PluginsPane(),
        4 => LibraryPane(),
        5 => AppearancePane(),
        _ => ShortcutsPane(),
    };

    // ---- Audio ------------------------------------------------------------

    private Control AudioPane()
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(SectionLabel("AUDIO DEVICE"));

        if (_main is null)
        {
            body.Children.Add(new TextBlock { Text = "Audio settings are unavailable in this window.", FontSize = 11, Foreground = TextTertiary });
            return body;
        }

        _loading = true;
        var engine = _main.Engine;
        var cfg = engine.GetAudioConfig();

        var outputCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        FillDeviceCombo(outputCombo, _outputUids, _audioDevices.OutputDevices(), cfg.OutputUid);
        outputCombo.SelectionChanged += (_, _) => { int i = outputCombo.SelectedIndex; if (i >= 0 && i < _outputUids.Count) { engine.SetAudioOutputDevice(_outputUids[i]); Apply(); } };

        var inputCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        FillDeviceCombo(inputCombo, _inputUids, _audioDevices.InputDevices(), cfg.InputUid);
        inputCombo.SelectionChanged += (_, _) => { int i = inputCombo.SelectedIndex; if (i >= 0 && i < _inputUids.Count) { engine.SetAudioInputDevice(_inputUids[i]); Apply(); } };

        var srCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var sr in SampleRates) srCombo.Items.Add(sr == 0 ? "Device default" : $"{sr:0} Hz");
        srCombo.SelectedIndex = IndexOf(SampleRates, cfg.SampleRate);
        srCombo.SelectionChanged += (_, _) => { int i = srCombo.SelectedIndex; if (i >= 0) { engine.SetAudioSampleRate(SampleRates[i]); Apply(); } };

        var bufCombo = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var b in BufferSizes) bufCombo.Items.Add(b == 0 ? "Device default" : $"{b} frames");
        bufCombo.SelectedIndex = IndexOf(BufferSizes, cfg.BufferFrames);
        bufCombo.SelectionChanged += (_, _) => { int i = bufCombo.SelectedIndex; if (i >= 0) { engine.SetAudioBufferFrames(BufferSizes[i]); Apply(); } };

        // WASAPI exclusive (Windows only): lets Nota own the output device for
        // the lowest latency, at the cost of muting other apps. The device then
        // dictates the sample rate / buffer, so those combos are ignored while
        // it's on. If the device refuses exclusive (busy / not allowed), the
        // backend falls back to shared mode and the status line warns the user.
        var exclusiveCheck = new CheckBox
        {
            Content = "WASAPI exclusive mode (lowest latency — mutes other apps)",
            IsChecked = cfg.WasapiExclusive,
            FontSize = 11,
        };
        exclusiveCheck.IsCheckedChanged += (_, _) =>
        {
            if (_loading) return;
            engine.SetAudioWasapiExclusive(exclusiveCheck.IsChecked ?? false);
            Apply();
        };
        var exclusiveNote = Caption("Exclusive mode takes over the device, so the sample rate and buffer size above are ignored — the device's native values are used. If the device is busy or doesn't allow exclusive access, Nota falls back to shared mode automatically.");

        _audioStatus = new TextBlock { FontSize = 11, Foreground = Success, TextWrapping = TextWrapping.Wrap, Text = StatusLine() };
        _audioStatus.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");

        body.Children.Add(Row("Output", outputCombo));
        body.Children.Add(Row("Input", inputCombo));
        body.Children.Add(Row("Sample rate", srCombo));
        body.Children.Add(Row("Buffer size", bufCombo));
        // WASAPI exclusive is a Windows/WASAPI concept — hide it on other platforms.
        if (OperatingSystem.IsWindows())
        {
            body.Children.Add(exclusiveCheck);
            body.Children.Add(exclusiveNote);
        }
        body.Children.Add(Row("Latency", _audioStatus));

        body.Children.Add(DividerLine());
        body.Children.Add(SectionLabel("TEST"));
        var testTone = DisabledChip("Play test tone");
        var cpu = DisabledChip("CPU check");
        body.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children = { testTone, new NaBadge { Kind = NaBadgeKind.NA }, cpu, new NaBadge { Kind = NaBadgeKind.NA } },
        });

        _loading = false;
        return body;
    }

    private void Apply()
    {
        if (_loading || _main is null) return;
        _main.ApplyAudioSettings();
        if (_audioStatus is not null) _audioStatus.Text = StatusLine();
    }

    private string StatusLine()
    {
        if (_main is null) return "";
        double sr = _main.Engine.NegotiatedSampleRate;
        int buf = _main.Engine.NegotiatedBufferFrames;
        if (sr <= 0) return "Audio device not running.";
        string mode = OperatingSystem.IsWindows() && _main.Engine.AudioExclusiveFallback
            ? " · exclusive refused → shared"
            : OperatingSystem.IsWindows() && _main.Engine.GetAudioConfig().WasapiExclusive ? " · exclusive" : "";
        return buf > 0 ? $"{sr:0} Hz · {buf} frames{mode}" : $"{sr:0} Hz{mode}";
    }

    // ---- MIDI -------------------------------------------------------------

    private Control MidiPane()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(SectionLabel("MIDI INPUTS"));
        if (_main is null) { body.Children.Add(Caption("MIDI settings are unavailable in this window.")); return body; }

        var engine = _main.Engine;
        var devices = _midiDevices.InputDevices();
        if (devices.Count == 0) { body.Children.Add(Caption("No MIDI inputs detected.")); return body; }

        _loading = true;
        foreach (var dev in devices)
        {
            string uid = dev.Uid;
            var check = new CheckBox { Content = dev.Name, IsChecked = engine.IsMidiInputEnabled(uid) };
            check.IsCheckedChanged += (_, _) => { if (_loading) return; engine.SetMidiInputEnabled(uid, check.IsChecked ?? true); _main.ApplyMidiSettings(); };
            body.Children.Add(check);
        }
        body.Children.Add(Caption("Uncheck a controller to stop Nota listening to it. New devices are on by default."));
        _loading = false;
        return body;
    }

    // ---- Gamepads -----------------------------------------------------------

    private Control GamepadsPane()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(SectionLabel("GAMEPAD INPUT"));
        if (_main is null) { body.Children.Add(Caption("Gamepad settings are unavailable in this window.")); return body; }
        if (!OperatingSystem.IsMacOS())
        {
            body.Children.Add(Caption("Gamepad input is macOS-only in this build."));
            return body;
        }

        // The master switch lives in Settings (not the native engine): the pad thread
        // always runs once started so new pads are still detected, but button edges only
        // reach the engine while this is on.
        var enable = new CheckBox
        {
            Content = "Use a connected gamepad for notes and mapped controls",
            IsChecked = _main.Settings.Current.GamepadEnabled,
        };
        enable.IsCheckedChanged += (_, _) =>
        {
            _main.Settings.Current.GamepadEnabled = enable.IsChecked ?? false;
            _main.Settings.Save();
        };
        body.Children.Add(enable);

        // Connected pads, refreshed by the service hot-plug event (the UI tick
        // raises it when the pad count changes). Each row shows a live-connection
        // dot, the pad name, and a "Last:" readout that flashes on every button
        // press so the user can confirm the pad is wired up before they record.
        var padList = new StackPanel { Spacing = 6 };
        var lastLabels = new Dictionary<int, TextBlock>();      // pad slot → activity readout
        var flashTimers = new Dictionary<int, DispatcherTimer>(); // pad slot → fade-back timer

        void RebuildPads()
        {
            padList.Children.Clear();
            lastLabels.Clear();
            var pads = _gamepads.Pads();
            if (pads.Count == 0)
            {
                padList.Children.Add(Caption("No gamepad detected. Connect one and it appears here."));
                return;
            }
            for (int i = 0; i < pads.Count; i++)
            {
                var last = new TextBlock
                {
                    Text = "—", FontSize = 11, Foreground = TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, MinWidth = 64, TextAlignment = TextAlignment.Right,
                };
                lastLabels[i] = last;

                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto") };
                var dot = new Ellipse
                {
                    Width = 8, Height = 8, Fill = Success, Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var name = new TextBlock
                {
                    Text = pads[i].Name, FontSize = 12, Foreground = TextPrimary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };
                var lastCap = new TextBlock
                {
                    Text = "Last: ", FontSize = 11, Foreground = TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                Grid.SetColumn(dot, 0);
                Grid.SetColumn(name, 1);
                Grid.SetColumn(lastCap, 2);
                Grid.SetColumn(last, 3);
                row.Children.Add(dot);
                row.Children.Add(name);
                row.Children.Add(lastCap);
                row.Children.Add(last);
                padList.Children.Add(new Border
                {
                    Background = Sunken, CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 6), Child = row,
                });
            }
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
        body.Children.Add(padList);

        body.Children.Add(DividerLine());
        body.Children.Add(SectionLabel("LAYOUT"));
        body.Children.Add(Caption("Face buttons play C4 D4 E4 F4 · shoulder buttons play G4 A4 B4 C5 · d-pad up/down shifts the octave · d-pad left/right nudges velocity. Notes go to the armed (record-enabled) instrument track — enable Record to capture them, or use the audition track to just play."));
        body.Children.Add(Caption("Any button can be mapped to a control instead: click MIDI in the top-right, click the control, then press the button. A mapped button drives that control and stops playing its note; the rest of the pad keeps the layout above. Mappings are listed in the browser's MIDI Map tab and travel with the project."));
        return body;
    }

    // ---- Plug-ins ---------------------------------------------------------

    private Control PluginsPane()
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(SectionLabel("PLUGIN SCAN FOLDERS"));

        var pathList = new ListBox { ItemsSource = _paths, Height = 200 };
        RefreshPaths();
        var addBtn = new Button { Content = "Add folder…" };
        addBtn.Click += async (_, _) =>
        {
            Activate();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Add plugin scan folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath();
            if (path is null) return;
            _catalog.AddScanPath(path);
            RefreshPaths();
        };
        var removeBtn = new Button { Content = "Remove" };
        removeBtn.Click += (_, _) => { int i = pathList.SelectedIndex; if (i >= 0) { _catalog.RemoveScanPath(i); RefreshPaths(); } };

        body.Children.Add(pathList);
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { addBtn, removeBtn } });
        body.Children.Add(Caption("Extra folders are searched for VST3 plugins on the next rescan (AU uses the system registry)."));

        body.Children.Add(DividerLine());
        body.Children.Add(SectionLabel("SCAN"));
        var rescanStatus = Caption("");
        var rescan = new Button { Content = "Rescan plugins" };
        rescan.Click += (_, _) =>
        {
            if (_main is null) { rescanStatus.Text = "Plugin scanning is unavailable in this window."; return; }
            var workerName = OperatingSystem.IsWindows() ? "nota-scanworker.exe" : "nota-scanworker";
            var worker = System.IO.Path.Combine(AppContext.BaseDirectory, workerName);
            if (!System.IO.File.Exists(worker)) { rescanStatus.Text = "Scanner worker not found."; return; }
            int n = _main.Browser.Scan(worker);
            rescanStatus.Text = $"Found {n} plugin{(n == 1 ? "" : "s")}.";
        };
        body.Children.Add(rescan);
        body.Children.Add(rescanStatus);
        return body;
    }

    // ---- Library ----------------------------------------------------------

    private Control LibraryPane()
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(SectionLabel("CONTENT FOLDERS"));
        if (_main is null) { body.Children.Add(Caption("Content folders are unavailable in this window.")); return body; }
        var settings = _main.Settings;
        body.Children.Add(FolderRow("Samples",
            () => settings.Current.SamplesFolder, () => settings.ResolvedSamplesFolder(),
            path => { settings.Current.SamplesFolder = path; settings.Save(); _main.Browser.RebuildSamples(); }));
        body.Children.Add(FolderRow("Projects",
            () => settings.Current.ProjectsFolder, () => settings.ResolvedProjectsFolder(),
            path => { settings.Current.ProjectsFolder = path; settings.Save(); _main.Browser.RebuildProjects(); }));
        return body;
    }

    private Control FolderRow(string label, Func<string> get, Func<string> resolved, Action<string> set)
    {
        var pathText = Caption(string.IsNullOrWhiteSpace(get()) ? $"(default) {resolved()}" : get());
        var choose = new Button { Content = "Choose…", Classes = { "ghost" } };
        choose.Click += async (_, _) =>
        {
            Activate();
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = $"Choose {label} folder", AllowMultiple = false });
            if (folders.Count == 0) return;
            var path = folders[0].TryGetLocalPath();
            if (path is null) return;
            set(path);
            pathText.Text = path;
        };
        return new StackPanel
        {
            Spacing = 3,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = label, FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center }, choose } },
                pathText,
            },
        };
    }

    // ---- Appearance -------------------------------------------------------

    // Dark / Light / System as a segmented control. The choice applies immediately —
    // NotaThemeService re-tints the palette and repaints every open window — and is
    // persisted so the next launch starts in the same variant.
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
                Content = label, Classes = { "seg" },
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

        return new Border { Classes = { "segmented" }, Child = strip };
    }

    private Control AppearancePane()
    {
        var body = new StackPanel { Spacing = 14 };
        body.Children.Add(SectionLabel("UI"));

        body.Children.Add(Row("Theme", ThemePicker()));
        body.Children.Add(Caption("Ember Graphite is the warm dark palette; Ember Paper is the same system on a light ground. System follows the OS appearance and switches with it."));

        // ---- AI control (MCP server) ----
        body.Children.Add(DividerLine());
        body.Children.Add(SectionLabel("AI CONTROL (MCP)"));
        var settings = App.Services.GetService<ISettingsService>();
        var mcp = App.Services.GetService<McpService>();
        var enable = new CheckBox { Content = "Enable MCP server (let an AI drive Nota)", FontSize = 11, IsChecked = settings?.Current.McpEnabled ?? false };
        var portBox = new TextBox { Text = (settings?.Current.McpPort ?? 3900).ToString(), Width = 90, Classes = { "field" } };
        var status = Caption("");
        void RefreshStatus() => status.Text = (settings?.Current.McpEnabled ?? false)
            ? $"Listening on http://127.0.0.1:{settings!.Current.McpPort}/ (loopback)"
            : "Off.";
        void ApplyMcp()
        {
            if (settings is null) return;
            settings.Current.McpEnabled = enable.IsChecked ?? false;
            if (int.TryParse(portBox.Text, out var p) && p is > 0 and < 65536) settings.Current.McpPort = p;
            settings.Save();
            _ = mcp?.ApplyAsync();
            RefreshStatus();
        }
        enable.IsCheckedChanged += (_, _) => ApplyMcp();
        portBox.LostFocus += (_, _) => ApplyMcp();
        RefreshStatus();

        // Copy a ready client-config JSON (Claude Desktop / Claude Code) with the current port.
        string McpConfigJson()
        {
            int port = settings?.Current.McpPort ?? 3900;
            return "{\n  \"mcpServers\": {\n    \"nota\": {\n      \"type\": \"http\",\n      \"url\": \"http://127.0.0.1:" + port + "/\"\n    }\n  }\n}";
        }
        var copyBtn = new Button { Content = "Copy config", FontSize = 11 };
        copyBtn.Click += async (_, _) =>
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb is not null) { await cb.SetTextAsync(McpConfigJson()); status.Text = "Config copied to clipboard."; }
        };

        body.Children.Add(enable);
        body.Children.Add(Row("Port", portBox));
        body.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { copyBtn, status } });
        body.Children.Add(Caption("Add this config (or the URL) as an MCP server in Claude Desktop / Claude Code. Loopback only; off by default."));
        return body;
    }

    // ---- Shortcuts --------------------------------------------------------

    // NB: this list mirrors the real key handlers — keep the two in sync (see the
    // `nota-shortcuts` skill). Bindings live in MainWindow.Input.cs (transport, global
    // editing, computer-keyboard notes) and PianoRollView.cs (note editing). ⌘ = Meta on
    // macOS / Ctrl on Windows.
    private static readonly (string Title, (string Key, string Action)[] Rows)[] ShortcutGroups =
    {
        ("TRANSPORT", new[]
        {
            ("Space", "Play / Stop"),
            ("Return", "Stop (again → back to the start)"),
            ("R", "Record"),
            ("M", "Metronome"),
            ("⌘L", "Loop on / off · loop the time selection"),
        }),
        ("ARRANGEMENT & EDITING", new[]
        {
            ("⌘Z   ⌘⇧Z", "Undo / redo"),
            ("⌘A", "Toggle automation mode"),
            ("⌘M", "Toggle the Mixer view"),
            ("Tab", "Switch Devices / Clip in the detail panel"),
            ("Esc", "Cancel the current gesture / clear the selection · close the patch-bay overlay"),
            ("⌘G   ⌘⇧G", "Group / ungroup selected tracks"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste clip (or automation range)"),
            ("⌘D", "Duplicate the selected clip(s) / range"),
            ("⌘E", "Split at the playhead · at the range edges"),
            ("⌘J", "Consolidate the selection into one clip per track"),
            ("0", "Deactivate / activate the selected clip(s)"),
            ("Delete", "Delete the selected clip / range"),
        }),
        ("PIANO ROLL", new[]
        {
            ("← →", "Move notes by the grid"),
            ("↑ ↓", "Transpose by a semitone"),
            ("⇧↑  ⇧↓", "Transpose by an octave"),
            ("⌘A", "Select all notes"),
            ("⌘D", "Duplicate the selection"),
            ("⌘C  ⌘X  ⌘V", "Copy / cut / paste notes"),
            ("Delete", "Delete the selected notes"),
        }),
        ("PLAY NOTES (COMPUTER KEYBOARD)", new[]
        {
            ("A S D F G H J K", "White keys, from C4"),
            ("W E · T Y U", "Black keys (sharps)"),
            ("Z / X", "Shift octave down / up"),
            ("C / V", "Lower / raise velocity"),
        }),
        ("GAMEPAD (MACOS)", new[]
        {
            ("Face buttons", "Play C4 D4 E4 F4"),
            ("Shoulder buttons", "Play G4 A4 B4 C5"),
            ("D-pad ↑ / ↓", "Shift the gamepad octave"),
            ("D-pad ← / →", "Lower / raise velocity"),
            ("Any button", "Mappable through MIDI Learn — a mapped button drives the control instead"),
        }),
        ("MOUSE", new[]
        {
            ("Double-click clip", "Open in the clip editor"),
            ("Drag overview strip", "Drag sideways to scroll · up / down to zoom out / in"),
            ("Drag overview edge", "Zoom by resizing the viewport window"),
            ("Double-click overview", "Fit the whole project on screen"),
            ("Drag ⠿", "Reorder devices (◀ ▶)"),
            ("Drag knob", "Change a device value · hold ⌘ or ⇧ for fine steps"),
            ("Double-click knob", "Reset the value to its default"),
            ("Drag jack → jack", "Patch a cable in Nota Consort · ⌥-click a jack to pull its cables"),
        }),
    };

    private Control ShortcutsPane()
    {
        var body = new StackPanel { Spacing = 14 };
        foreach (var (title, rows) in ShortcutGroups)
        {
            body.Children.Add(SectionLabel(title));
            var stack = new StackPanel { Spacing = 6 };
            foreach (var (key, action) in rows) stack.Children.Add(ShortcutRow(key, action));
            body.Children.Add(stack);
        }
        return body;
    }

    private Control ShortcutRow(string key, string action)
    {
        var cap = new Border
        {
            Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 2),
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var kt = new TextBlock { Text = key, FontSize = 10, Foreground = TextPrimary };
        kt.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        cap.Child = kt;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("190,*") };
        grid.Children.Add(cap);
        var a = new TextBlock { Text = action, FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
        Grid.SetColumn(a, 1);
        grid.Children.Add(a);
        return grid;
    }

    // ---- helpers ----------------------------------------------------------

    private Border DisabledChip(string text) => new()
    {
        Background = Raised, BorderBrush = BorderStrong, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(5),
        Padding = new Thickness(12, 4), Opacity = 0.6, VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = TextPrimary },
    };

    private static TextBlock SectionLabel(string text) => new()
    {
        Text = text, FontSize = 10, FontWeight = FontWeight.Bold, Foreground = TextTertiary,
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text, FontSize = 10, Foreground = TextTertiary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center,
    };

    private static Control DividerLine() => new Border { Height = 1, Background = Divider };

    private static Control Row(string label, Control control) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Children =
        {
            new TextBlock { Text = label, Width = 130, FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center },
            control,
        },
    };

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

    private void RefreshPaths()
    {
        _paths.Clear();
        int n = _catalog.ScanPathCount;
        for (int i = 0; i < n; i++) _paths.Add(_catalog.ScanPath(i) ?? "?");
    }
}
