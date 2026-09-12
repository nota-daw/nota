// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Controls.Shapes;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Presentation;
using System.Runtime.InteropServices;

namespace Nota.App;

public partial class MainWindow : Window
{
    private MainWindowViewModel? _vm;
    private readonly HashSet<Key> _heldKeys = new();
    // Computer-keyboard play state: the exact pitch each held note key is sounding (so a
    // note releases at the right pitch even if the octave changed while held), plus the
    // Z/X octave shift and C/V velocity (typing keyboard).
    private readonly Dictionary<Key, int> _heldNotePitch = new();
    private int _typingOctave;          // semitone shift = _typingOctave * 12 (Z/X)
    private int _typingVelocity = 100;  // 1..127, applied to typed notes (C/V)

    // Application ports (resolved from the DI container when the VM attaches).
    private IProjectStore _projects = default!;
    private IPresetStore _presets = default!;
    private IFactoryPresets _factory = default!;
    private IRecoveryStore _recovery = default!;
    private IAudioExporter _exporter = default!;

    private SessionView? _session;
    private MeterBar? _masterMeter;
    private MidiLearnService? _learn;
    /// <summary>The MIDI-learn service, so secondary windows can host their own learn glass.</summary>
    internal MidiLearnService? LearnService => _learn;

    // Bottom detail panel (Devices chain / Clip piano roll / Mixer).
    private DeviceChainView? _deviceChain;
    private ClipEditorView? _clipEditor;
    private AudioClipEditorView? _audioEditor;
    private MixerView? _mixer;
    private ModularView? _modular;
    private int _editorTrackId = -1;
    private int _editorClipIndex = -1;
    private int _audioEditorTrackId = -1;
    private int _audioEditorClipIndex = -1;
    private PianoRollView? _editorRoll;

    // Remembered detail-panel height once the user drags the splitter, so reopening the
    // panel keeps their size instead of snapping back to the per-mode default. 0 = unset.
    private double _detailHeight;
    private double _lastSetDetailHeight;
    // True while the detail panel (Devices/Clip) was the last area the user interacted
    // with — Tab then toggles between the two tabs.
    private bool _detailWasLastFocused;

    private IAudioEngine Engine => _vm!.Engine;

    public MainWindow()
    {
        InitializeComponent();
        UpdateWindowTitle();   // "Nota — Untitled" until a project is opened/saved
        // Custom frameless title bar (Phase 2): extend the client area under the
        // decorations on macOS/Windows (traffic lights / caption buttons overlay it).
        // Linux desktops (GNOME/KDE) draw their own title bar and their client-side
        // decoration story is inconsistent, so keep NATIVE decorations there and hide
        // our custom bar — the doc title shows in the OS title bar instead.
        if (OperatingSystem.IsLinux())
        {
            ExtendClientAreaToDecorationsHint = false;
            TitleBar.IsVisible = false;
        }
        else
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 36;
        }
        DataContextChanged += OnDataContextChanged;
        // Platform-specific window icon: Windows/Linux get the .ico bundle from
        // assets/icons/windows so the taskbar / alt-tab shows the brand mark. macOS
        // uses the .icns produced by scripts/bundle-mac.sh for the dock/Finder.
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://Nota.App/Assets/nota.ico")));
            // On Windows the native caption buttons (minimise/maximise/close) overlay
            // the right edge of the extended title bar. Reserve room so the centred doc
            // title (and anything else in that corner) never sits under them.
            // 3 buttons × ~46px wide ≈ 142px; round up a touch. (Linux hides the bar,
            // so this only applies to Windows.)
            if (OperatingSystem.IsWindows())
                TitleBarRow.Margin = new Thickness(0, 0, 180, 0);
        }
    }

    // Custom frameless title bar (Phase 2): drag the window by its top bar; a
    // double-click toggles maximise/restore (standard title-bar behaviour).
    private void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is not MainWindowViewModel vm || _vm is not null) return;
        _vm = vm;

        _learn = App.Services.GetRequiredService<MidiLearnService>();
        LearnOverlay.Service = _learn;
        _learn.PendingChanged += () => LearnOverlay.Refresh();
        Browser.SetMidiLearn(_learn);
        MidiLearn.Bind(PlayBtn, MidiTarget.TransportPlay, "Play");
        MidiLearn.Bind(StopBtn, MidiTarget.TransportStop, "Stop");
        MidiLearn.Bind(RecordBtn, MidiTarget.TransportRecord, "Record");

        _projects = App.Services.GetRequiredService<IProjectStore>();
        _presets = App.Services.GetRequiredService<IPresetStore>();
        _factory = App.Services.GetRequiredService<IFactoryPresets>();
        _recovery = App.Services.GetRequiredService<IRecoveryStore>();
        _exporter = App.Services.GetRequiredService<IAudioExporter>();

        Timeline.Engine = vm.Engine;
        Timeline.FreezeRole = FreezeRoleOf;   // live-freeze (v1.1) header badges
        Timeline.FreezeMenuItems = BuildTrackFreezeMenu;   // track context menu: Freeze / Live Freeze
        Timeline.RefreshStarting = PruneFreezeLinks;       // drop links to deleted / undone tracks
        Timeline.MidiClipActivated += OpenClipEditor;
        Timeline.AudioClipActivated += OpenAudioClipEditor;
        Timeline.ItemDropped += OnArrangementDrop;   // browser drag & drop (M7-5)
        Timeline.ConvertClipRequested += OnConvertClip;   // audio clip → MIDI (Convert / Slice)
        // Arrangement context menus add tracks through the toolbar's own handlers, so the two
        // routes seed, refresh and report identically.
        Timeline.AddTrackRequested += kind =>
        {
            switch (kind)
            {
                case NewTrackKind.Instrument: OnAddInstrumentClicked(this, new RoutedEventArgs()); break;
                case NewTrackKind.Audio:      OnAddAudioClicked(this, new RoutedEventArgs()); break;
                case NewTrackKind.Return:     OnAddReturnClicked(this, new RoutedEventArgs()); break;
            }
        };

        _masterMeter = new MeterBar(horizontal: true);
        MasterMeterHost.Children.Add(_masterMeter);

        // Thin master volume fader (mockup 1b) — MiniFader instead of a stock Slider.
        var masterVol = new MiniFader((double)vm.Transport.MasterVolume, 1.5);
        masterVol.ValueChanged += v => vm.Transport.MasterVolume = v;
        MasterVolHost.Children.Add(masterVol);
        MidiLearn.Bind(masterVol, MidiTarget.MasterVolume, "Master Volume");

        // BPM as a drag/type field (HANDOFF §4).
        var bpmField = new DragNumber((double)vm.Transport.Bpm, 20, 300, 0.5, "0");
        bpmField.ValueChanged += v => vm.Transport.Bpm = (decimal)Math.Round(v);
        BpmHost.Children.Add(bpmField);

        // Time signature: numerator drags 1–16; denominator snaps to a power of two.
        var tsNum = new DragNumber(vm.Transport.TimeSigNumerator, 1, 16, 0.5, "0");
        tsNum.ValueChanged += v => vm.Transport.TimeSigNumerator = (int)Math.Round(v);
        TimeSigNumHost.Children.Add(tsNum);
        var tsDen = new DragNumber(vm.Transport.TimeSigDenominator, 1, 16, 0.5, "0");
        tsDen.ValueChanged += v =>
        {
            int d = NearestPow2(v);
            tsDen.Value = d;                              // snap the readout (setter doesn't re-raise)
            vm.Transport.TimeSigDenominator = d;
        };
        TimeSigDenHost.Children.Add(tsDen);

        vm.Transport.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.Transport.Bpm)) bpmField.Value = (double)vm.Transport.Bpm;
            else if (e.PropertyName == nameof(vm.Transport.TimeSigNumerator)) tsNum.Value = vm.Transport.TimeSigNumerator;
            else if (e.PropertyName == nameof(vm.Transport.TimeSigDenominator)) tsDen.Value = vm.Transport.TimeSigDenominator;
        };

        var cpuGreen = (IBrush?)NotaPalette.Success;
        var cpuAmber = (IBrush?)NotaPalette.Warning;
        var cpuRed = (IBrush?)NotaPalette.Danger;
        vm.PlayheadUpdated += beats =>
        {
            _learn?.Tick();          // apply incoming MIDI mappings / capture a learn message
            LearnOverlay.Refresh();  // keep highlights over rebuilt cards while armed
            Timeline.SetPlayhead(beats);
            _masterMeter?.Push(vm.Engine.MasterMeter());
            Timeline.UpdateMeters();                                   // per-track headers (M6-2)
            Timeline.RefreshAutomationLive();                          // show writes live (M9-C)
            if (_session?.IsVisible == true) _session.UpdateStates(); // live launch/queue/play
            if (_mixer?.IsVisible == true) _mixer.UpdateMeters();      // Mixer tab strips
            if (_modular?.IsVisible == true) _modular.Tick(vm.Engine.IsPlaying); // graph knobs follow automation + edge pulse
            if (DetailBody.Content is MixerView mx && mx.IsEffectivelyVisible) mx.UpdateMeters(); // 1f strips
            // Detail contents update wherever they're shown — docked panel or the floating
            // Devices/Clip window — so key off IsEffectivelyVisible, not the docked panel.
            if (_deviceChain is { IsEffectivelyVisible: true }) _deviceChain.RefreshSynthLive(); // synth graphs follow automation
            if (_audioEditor is { IsEffectivelyVisible: true })
                _audioEditor.OnPlayhead(beats, vm.Engine.IsPlaying);       // live clip playback cursor
            // Piano-roll playback cursor: map the global beat to clip-local time for the
            // open arrangement MIDI clip (session slots run their own clock → skipped).
            if (_clipEditor is { IsEffectivelyVisible: true } && _editorRoll is not null
                && _editorTrackId > 0 && Engine.TryGetClipInfo(_editorTrackId, _editorClipIndex, out var pci) && pci.IsMidi)
                _editorRoll.SetPlayhead(beats - pci.StartBeat, vm.Engine.IsPlaying);

            double load = Math.Clamp(vm.Engine.CpuLoad, 0, 1);         // live DSP load (Phase 11)
            CpuFill.Width = load * 56;
            CpuFill.Background = load > 0.9 ? cpuRed : load > 0.7 ? cpuAmber : cpuGreen;
            CpuText.Text = ((int)Math.Round(load * 100)) + "%";
        };
        // Recording tick fires ~60 Hz: refresh clip data + lane drawing only, WITHOUT
        // rebuilding the header cards — otherwise the input combobox and any open track/
        // clip context menu are torn down every frame and can't be used while recording.
        vm.RecordingTick += () => { Timeline.Refresh(rebuildHeaders: false); ReloadEditorNotes(); };
        // Hit Record with nothing armed → auto-arm the selected/last track (M-fix).
        vm.Transport.RecordArmTarget = () => Timeline.RecordArmTarget();
        vm.Transport.TracksChanged += () => { Timeline.Refresh(); ReloadEditorNotes(); };
        Timeline.LoopChanged += () => vm.Transport.SyncLoop();   // ruler drag / "Loop selection" → transport bar

        Browser.SetViewModel(vm.Browser);
        Browser.ItemActivated += OnBrowserItemActivated;
        Browser.PreviewRequested += OnBrowserPreview;
        Browser.RevealRequested += OnBrowserReveal;
        Browser.DeleteProjectRequested += OnBrowserDeleteProject;
        Browser.EditTagsRequested += OnBrowserEditTags;

        _deviceChain = new DeviceChainView(vm.Engine, _factory, App.Services.GetService<IPluginCatalog>());
        _deviceChain.Changed += () => { Timeline.Refresh(); if (_modular?.IsVisible == true) _modular.Refresh(); };
        _deviceChain.PresetSaveRequested += OnSavePreset;
        _deviceChain.RackPresetSaveRequested += OnSaveRackChainPreset;
        _deviceChain.ItemDropped += OnDevicePanelDrop;   // browser drag onto the device panel
        Timeline.TrackSelected += OnTrackSelected;
        Timeline.ClipGeometryChanged += OnClipGeometryChanged;   // clip trimmed/moved → follow it in the open editor
        Timeline.StatusMessage += msg => { if (_vm is not null) _vm.StatusText = msg; };   // automation-follow hints etc.

        _session = new SessionView(vm.Engine) { IsVisible = false };
        _session.SlotEditRequested += OpenSessionClipEditor;
        _session.ItemDropped += OnSessionDrop;                        // browser drag & drop (M7-5)
        _session.ArrangementChanged += () => Timeline.Refresh();      // M5-6
        Timeline.SessionChanged += () => _session?.Refresh();          // M5-6
        MainContent.Children.Add(_session);

        // Mixer lives in its own window (View → Mixer), not as a main-view tab — created here
        // once and hosted in the window when opened (see MainWindow.Mixer.cs).
        _mixer = new MixerView(vm.Engine) { IsVisible = false };

        // Modular: signal-graph view of the selected track (mockup 1a). Follows track
        // selection like the Detail device chain does.
        _modular = new ModularView(vm.Engine) { IsVisible = false };
        _modular.Changed += () => { Timeline.Refresh(); if (_deviceChain is { } dc && dc.TrackId > 0) dc.Refresh(); };
        _modular.TrackActivated += OnTrackSelected;   // Global-view island → select that track
        _modular.ItemDropped += OnModularDrop;        // browser drag onto the modular canvas
        MainContent.Children.Add(_modular);

        vm.AutosaveRequested += OnAutosaveTick;
        InitGamepad();   // poll pad buttons on each UI tick (live note source)
        Closing += OnMainWindowClosing;   // clean-shutdown marker (M7-7)
        Opened += OnOpenedRecoveryCheck;  // offer recovery snapshot (M7-7)

        // Losing window focus mid-drag (alt-tab) cancels the gesture with no model change
        // (req 2.11). We use Deactivated rather than PointerCaptureLost because the latter fires
        // on every normal button-up on macOS, which would abort commits.
        Deactivated += (_, _) => Timeline.CancelActiveGesture();
        // macOS composes the global menu bar from the app menu (App.axaml) plus the
        // *active* window's NativeMenu (MainWindow.axaml). Launched via `dotnet run`
        // (not a .app bundle) the window doesn't reliably become key on show, so the
        // window menu — File/Edit/View/… — intermittently never installs and only the
        // "Nota" app menu appears. Force activation once shown so it always lands.
        Opened += (_, _) => Activate();

        // Remember the detail panel's height once the user drags the splitter — but only
        // on the Clip tab. The Devices tab keeps its natural card-fitting height, so its
        // drags aren't persisted (a programmatic set matches _lastSetDetailHeight and is
        // ignored; only a real drag differs).
        DetailPanel.SizeChanged += (_, e) =>
        {
            bool clipActive = DetailBody.Content is ClipEditorView or AudioClipEditorView;
            if (clipActive && DetailPanel.IsVisible && e.NewSize.Height > 120
                && Math.Abs(e.NewSize.Height - _lastSetDetailHeight) > 1.0)
                _detailHeight = e.NewSize.Height;
        };

        // Track whether the detail panel was the last area the user clicked into, so Tab
        // can toggle its Devices/Clip tabs (see OnKeyDown). Tunnel + handledEventsToo so it
        // sees every press regardless of what consumes it.
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.Source is Visual v)
                _detailWasLastFocused = DetailPanel.IsVisible && v.GetSelfAndVisualAncestors().Contains(DetailPanel);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Global transport keys (Space = Play/Stop, Return = Stop) must win over whatever
        // control currently holds focus. A focused Slider/CheckBox/ComboBox/plugin knob
        // consumes Space/Enter in its own bubbling handler *before* the event reaches the
        // window, which made Play/Stop "randomly" toggle some other control instead. Handle
        // them in the tunnel phase so the transport always gets first dibs (text inputs are
        // exempted inside the handler so typing still works). See OnGlobalTransportKey.
        AddHandler(KeyDownEvent, OnGlobalTransportKey, RoutingStrategies.Tunnel);

        Closed += (_, _) => vm.Dispose();

        // MCP server: MCP tool edits redraw the arrangement + device panel; start the server if
        // enabled in Preferences (loopback only). Runs off the settings toggle thereafter.
        App.Services.GetRequiredService<ArrangementRefresh>().OnRefresh = () =>
        {
            Timeline.Refresh();
            if (_deviceChain is { } dc && dc.TrackId > 0) dc.Refresh();
        };
        _ = App.Services.GetRequiredService<McpService>().ApplyAsync();

        // TEMP visual-check hook (NOTA_DEBUG_SESSION) — Amplifier card on an audio track.
        if (Environment.GetEnvironmentVariable("NOTA_DEBUG_SESSION") is not null)
        {
            var dbg = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            dbg.Tick += (_, _) =>
            {
                dbg.Stop();
                int t = Engine.AddAudioTrack();
                int ad = Engine.AddBuiltinDevice(t, 6);
                Engine.DeviceSetParam(t, ad, 0, 3f);   // Rock model
                Engine.DeviceSetParam(t, ad, 1, 6f);   // Gain
                Engine.DeviceSetParam(t, ad, 12, 0.4f);// Gate on (visible)
                Timeline.Refresh();
                ShowDevices(t);
            };
            dbg.Start();
        }

        // TEMP (modular editor visual check): seed an instrument track with a MIDI FX
        // and two insert effects, then open the Modular view. Remove before committing.
        if (Environment.GetEnvironmentVariable("NOTA_MODULAR_SHOT") is not null)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                int t = Engine.AddInstrumentTrack();
                Engine.SetTrackBuiltinInstrument(t, 6);   // Nota Volt
                Engine.AddMidiEffect(t, 0);               // Arpeggiator (MIDI FX)
                Engine.AddBuiltinDevice(t, 16);           // Nota EQ-3
                Engine.AddBuiltinDevice(t, 3);            // Nota Delay
                Engine.SetTrackName(t, "Bass");
                int mod = Engine.ModulatorAdd(t, 0);      // LFO
                Engine.ModulatorSet(t, mod, 1, 1f);       // tempo sync
                Engine.CvLinkAdd(t, mod, 0, 0);           // LFO → EQ-3 param 0
                int envm = Engine.ModulatorAdd(t, 1);     // envelope follower
                Engine.CvLinkAdd(t, envm, 1, 0);          // Env → Delay param 0
                int midm = Engine.ModulatorAdd(t, 2);     // MIDI→CV (velocity)
                Engine.CvLinkAdd(t, midm, 0, 1);          // MIDI → EQ-3 param 1
                int adsr = Engine.ModulatorAdd(t, 3);     // ADSR
                Engine.CvLinkAdd(t, adsr, 1, 1);          // ADSR → Delay param 1
                int math = Engine.ModulatorAdd(t, 5);     // Math (LFO + Env)
                Engine.ModulatorSet(t, math, 10, mod);    // input A = LFO
                Engine.ModulatorSet(t, math, 11, envm);   // input B = Env
                Engine.CvLinkAdd(t, math, 0, 2);          // Math → EQ-3 param 2
                int scope = Engine.ModulatorAdd(t, 6);    // Scope (monitors the LFO)
                Engine.ModulatorSet(t, scope, 10, mod);   // input = LFO
                // A couple more tracks so the Global view has several islands.
                int dr = Engine.AddInstrumentTrack(); Engine.SetTrackBuiltinInstrument(dr, 1); Engine.AddBuiltinDevice(dr, 0); Engine.SetTrackName(dr, "Drums");
                int pad = Engine.AddInstrumentTrack(); Engine.SetTrackBuiltinInstrument(pad, 5); Engine.AddBuiltinDevice(pad, 2); Engine.AddBuiltinDevice(pad, 7); Engine.SetTrackName(pad, "Pad");
                Engine.CvLinkAddTo(t, mod, pad, 1, 0);    // Bass LFO → Pad's Auto Filter (cross-track CV)
                Timeline.Refresh();
                SetView(MainView.Modular);
                _modular!.Show(t);
                if (Environment.GetEnvironmentVariable("NOTA_MODULAR_GLOBAL") is not null) _modular!.ShowGlobal();
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

    }

    private void OnAddInstrumentClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        int trackId = Engine.AddInstrumentTrack();
        Engine.AddMidiClip(trackId, 0.0, 4.0);
        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();   // new track shows in the graph/sidebar
        _vm.StatusText = $"Instrument track {trackId} (synth)";
    }

    private void OnAddAudioClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        int trackId = Engine.AddAudioTrack();
        Timeline.Refresh();
        _session?.Refresh();
        if (_modular?.IsVisible == true) _modular.Refresh();
        _vm.StatusText = $"Audio track {trackId} — arm it and hit Rec to record input";
    }

    private void OnAddReturnClicked(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        int trackId = Engine.AddReturnTrack();
        if (trackId <= 0) { _vm.StatusText = "All return buses are in use."; return; }
        Timeline.Refresh();
        if (DetailBody.Content == _deviceChain) _deviceChain?.Refresh(); // new bus -> Sends section
        _session?.Refresh();   // mini-mixer send controls follow the return count
        if (_modular?.IsVisible == true) _modular.Refresh();
        int bus = Engine.TrackReturnIndex(trackId);
        _vm.StatusText = $"Return {bus + 1} added — add an effect, then dial up track sends";
    }

    // --- Session view (M5) --------------------------------------------------

    private enum MainView { Arrangement, Session, Modular }

    private void OnShowArrangement(object? sender, RoutedEventArgs e) => SetView(MainView.Arrangement);
    private void OnShowSession(object? sender, RoutedEventArgs e) => SetView(MainView.Session);
    private void OnShowModular(object? sender, RoutedEventArgs e) => SetView(MainView.Modular);

    private void SetView(MainView view)
    {
        if (_session is null || _modular is null) return;
        bool session = view == MainView.Session, modular = view == MainView.Modular;
        Timeline.IsVisible = view == MainView.Arrangement;
        _session.IsVisible = session;
        _modular.IsVisible = modular;
        ArrangeBtn.IsChecked = view == MainView.Arrangement;
        SessionBtn.IsChecked = session;
        ModularBtn.IsChecked = modular;
        PlayBtn.Classes.Set("session", session);   // play button turns green in Session (1c)
        if (session) _session.Refresh();
        if (modular)
        {
            int t = Timeline.SelectedTrackId > 0 ? Timeline.SelectedTrackId : LastInstrumentTrack();
            if (t > 0) _modular.Show(t);
        }
        SyncClipTab();   // Clip tab availability is per-context (arrangement vs session)
    }

    // Toolbar Snap chip: cycle the arrangement's clip-drag snap grid.
    private static readonly (double beats, string label)[] SnapSteps =
    {
        (0.25, "1/16"), (0.5, "1/8"), (1.0, "1/4"), (2.0, "1/2"), (4.0, "1 bar"),
    };
    private int _snapIndex = 2;   // 1/4
    private void OnCycleSnap(object? sender, RoutedEventArgs e)
    {
        _snapIndex = (_snapIndex + 1) % SnapSteps.Length;
        var (beats, label) = SnapSteps[_snapIndex];
        Timeline.SnapBeats = beats;
        SnapLabel.Text = label;
    }

    // Session launch-quantize steps (beats, label). 0 = launch immediately (no quantize).
    private static readonly (double beats, string label)[] LaunchQSteps =
    {
        (0.0, "None"), (0.25, "1/16"), (0.5, "1/8"), (1.0, "1/4"), (2.0, "1/2"),
        (4.0, "1 Bar"), (8.0, "2 Bars"), (16.0, "4 Bars"),
    };
    private int _launchQIndex = 5;   // 1 Bar — matches the engine default (launchQuant_ = 4)
    private void OnCycleLaunchQ(object? sender, RoutedEventArgs e)
    {
        _launchQIndex = (_launchQIndex + 1) % LaunchQSteps.Length;
        var (beats, label) = LaunchQSteps[_launchQIndex];
        Engine.SetLaunchQuant(beats);
        LaunchQLabel.Text = label;
    }

    // Snap a dragged denominator to the nearest musical power of two (1/2/4/8/16).
    private static int NearestPow2(double v)
    {
        int best = 4; double bd = double.MaxValue;
        foreach (int o in new[] { 1, 2, 4, 8, 16 })
        { double d = System.Math.Abs(o - v); if (d < bd) { bd = d; best = o; } }
        return best;
    }

    // Click the position readout to flip bars.beats ↔ minutes:seconds.
    private void OnTogglePositionDisplay(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        _vm?.Transport.ToggleTimeDisplay();
        e.Handled = true;
    }

    // Follow: keep the arrangement scrolling with the playhead during playback.
    private void OnToggleFollow(object? sender, RoutedEventArgs e)
    {
        Timeline.FollowPlayhead = (sender as ToggleButton)?.IsChecked == true;
        if (Timeline.FollowPlayhead) Timeline.RecenterOnPlayhead();   // jump to the cursor now
    }

    // MIDI Learn: arm/disarm the overlay and reveal the mappings tab so the user
    // can see what they're binding.
    private void OnToggleMidiLearn(object? sender, RoutedEventArgs e)
    {
        if (_learn is null) return;
        bool on = MidiLearnBtn.IsChecked == true;
        _learn.Armed = on;
        if (on) Browser.ShowMidiMap();
    }

    private void OnToggleAutomation(object? sender, RoutedEventArgs e)
    {
        bool on = AutomationToggle.IsChecked == true;
        Timeline.AutomationMode = on;
        AutoModeBar.IsVisible = on;   // record-mode selector is only relevant in automation mode
        if (!on) SetAutoMode(AutomationWriteMode.Read, AutoModeRead); // no stray recording while hidden
    }

    // Automation record mode (M9-C): mutually-exclusive segmented Read/Touch/Latch/Write.
    private void SetAutoMode(AutomationWriteMode mode, ToggleButton active)
    {
        AutoModeRead.IsChecked = active == AutoModeRead;
        AutoModeTouch.IsChecked = active == AutoModeTouch;
        AutoModeLatch.IsChecked = active == AutoModeLatch;
        AutoModeWrite.IsChecked = active == AutoModeWrite;
        _vm?.Engine.SetAutomationWriteMode(mode);
        if (Timeline is not null) Timeline.AutomationWriteMode = mode;
    }
    private void OnAutoModeRead(object? sender, RoutedEventArgs e) => SetAutoMode(AutomationWriteMode.Read, AutoModeRead);
    private void OnAutoModeTouch(object? sender, RoutedEventArgs e) => SetAutoMode(AutomationWriteMode.Touch, AutoModeTouch);
    private void OnAutoModeLatch(object? sender, RoutedEventArgs e) => SetAutoMode(AutomationWriteMode.Latch, AutoModeLatch);
    private void OnAutoModeWrite(object? sender, RoutedEventArgs e) => SetAutoMode(AutomationWriteMode.Write, AutoModeWrite);
}
