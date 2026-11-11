// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.App;

public partial class App : Avalonia.Application
{
    /// <summary>App-wide service container (M4.1-A).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        // Numbers read the same on every OS locale: a point, and U+2212 for the minus.
        NotaNum.Install();
        // One typeface with tabular figures, so digits keep their width everywhere.
        NotaFonts.InstallTabular();
        NotaTextSize.Install();
        // A skin's typeface and corner radii (spec 087), by the same kind of coercion.
        NotaSkin.Install();
        // Rounded, bordered, clipping Borders clip their child to the inner edge.
        BorderInnerClip.Install();
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        // Engine: one native handle, exposed to the layers through its port.
        // Tell native code where its data lives first, so it never has to guess:
        // audio.json / midi.json and the plugin catalog land next to C# settings.
        NotaEngine.SetDataDir(NotaPaths.DataDir);
        services.AddSingleton<NotaEngine>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<NotaEngine>());
        services.AddSingleton(new EngineBuildInfo(NotaEngine.Version));
        // Application ports → Infrastructure implementations.
        services.AddSingleton<ILogSink, LogSink>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPluginCatalog, PluginCatalog>();
        services.AddSingleton<IPluginStore, PluginStore>();
        services.AddSingleton<ISampleStore, SampleStore>();
        services.AddSingleton<ISampleAnalyzer, SampleAnalyzer>();
        services.AddSingleton<ISampleIndex, SampleLibraryIndex>();
        services.AddSingleton<IModelStore>(_ => new ModelStore());
        services.AddSingleton(_ => NotaLanguages.Installed);
        services.AddSingleton<LanguageCatalog>();
        services.AddSingleton<IAppUpdater>(_ => new AppUpdater(AppInfo.Version));
        services.AddSingleton<IClipAi, ClipAi>();
        services.AddSingleton<IPresetLibrary, PresetLibrary>();
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<IProjectHistory>(_ => new ProjectHistory(AppInfo.Version));
        services.AddSingleton<IPresetStore, PresetStore>();
        services.AddSingleton<Nota.Application.Articulations.IArticulationLibrary>(_ => new Nota.Infrastructure.Articulations.ArticulationLibraryStore());   // specs/085
        services.AddSingleton<IFactoryPresets, FactoryPresetCatalog>();
        services.AddSingleton<IPresetAudition, PresetAudition>();
        services.AddSingleton<IDrumKits, DrumKitService>();
        services.AddSingleton<IMosaicPacks, Nota.Infrastructure.Mosaic.MosaicPackLibrary>();
        services.AddSingleton<IBrowserLibrary, BrowserLibraryService>();
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IMidiDeviceService, MidiDeviceService>();
        services.AddSingleton<GamepadService>();
        services.AddSingleton<IGamepadService>(sp => sp.GetRequiredService<GamepadService>());
        services.AddSingleton<IAudioExporter, AudioExporter>();
        services.AddSingleton<IProcessCpu, ProcessCpuSampler>();
        // UI-thread timer factory lives in the App layer (over DispatcherTimer).
        services.AddSingleton<IPeriodicTimerFactory, AvaloniaPeriodicTimerFactory>();
        services.AddSingleton<RecoveryService>();
        services.AddSingleton<IRecoveryStore>(sp => sp.GetRequiredService<RecoveryService>());
        // MIDI Learn service (shared by the UI overlay and the MCP MIDI tools).
        services.AddSingleton<MidiLearnService>();
        // MCP server (AI drives the app) — off unless enabled in Preferences.
        services.AddSingleton<Nota.Mcp.IEngineDispatch, AvaloniaEngineDispatch>();
        services.AddSingleton<ArrangementRefresh>();
        services.AddSingleton<Nota.Mcp.IArrangementRefresh>(sp => sp.GetRequiredService<ArrangementRefresh>());
        services.AddSingleton<ProjectVersionsBridge>();
        services.AddSingleton<McpService>();
        services.AddSingleton<RemoteService>();
        // Presentation.
        services.AddSingleton<TransportViewModel>();
        services.AddSingleton<BrowserViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        Services = services.BuildServiceProvider();

        // Log crashes so a later bug report carries the stack trace.
        var log = Services.GetRequiredService<ILogSink>();
        log.Info($"App started · v{AppInfo.Version}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            log.Error("Unhandled exception", e.ExceptionObject as Exception);
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            log.Error("Unobserved task exception", e.Exception);
            e.SetObserved();
        };

        // Interface language (spec 068), before any view model, window or static text table
        // exists — and before the themes, whose load problems are interface text (spec 087):
        // the language is fixed for the run, so those texts are built once, in it.
        InstallLanguage(Services.GetRequiredService<ISettingsService>().Current, log);
        // Colour theme (Preferences → Appearance), applied before the first window paints so
        // nothing flashes one theme on the way to another. Old Dark / Light / System
        // settings resolve onto the Studio themes (Appearance.Resolve).
        foreach (var err in NotaThemes.LoadErrors) log.Error($"Colour theme skipped — {err}", null);
        // User themes (spec 087) load first, so a stored user theme id resolves to itself.
        NotaThemes.ReloadUser();
        foreach (var (file, reason) in NotaThemes.UserLoadProblems) log.Info($"User theme skipped — {file}: {reason}");
        // The accent (spec 084) is resolved with it, so the first frame already wears it too.
        var startSettings = Services.GetRequiredService<ISettingsService>().Current;
        NotaThemeService.StoredChoice = () => Appearance.Resolve(
            Services.GetRequiredService<ISettingsService>().Current, NotaThemeService.KnownThemes);
        NotaThemeService.Set(Appearance.Resolve(startSettings, NotaThemeService.KnownThemes));
        NotaThemeService.SetAccent(Accent.Resolve(startSettings));
        // Interface scale (spec 027), likewise before the first window: windows and every
        // popup they open take it from here.
        NotaUiScale.InitPopups();
        NotaUiScale.Set(Services.GetRequiredService<ISettingsService>().Current.InterfaceScale);
        // Text size (spec 038), likewise: every text the app draws takes its step.
        NotaTextSize.Set(Services.GetRequiredService<ISettingsService>().Current.TextSize);
        TranslateAppMenu();
        TextBoxMenu.Install();   // the text boxes' Cut / Copy / Paste menu is Avalonia's, in English

        // Nota Mosaic's samples resolve against the Samples and data folders; installed sample
        // packs get their Mosaic presets in the background (once per pack version).
        Nota.Infrastructure.Mosaic.MosaicRoots.Install(Services.GetRequiredService<ISettingsService>());
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            await System.Threading.Tasks.Task.Delay(4000);
            try { await Services.GetRequiredService<IMosaicPacks>().ScanAsync(); }
            catch (Exception e) { log.Error("Couldn't make Mosaic presets for the sample packs", e); }
        });

        // Inputs switched off on the Audio I/O page stay off for takes (specs/086).
        try { InputSetupAccess.Push(Services.GetRequiredService<IAudioEngine>()); }
        catch (Exception e) { log.Error("Couldn't apply the Audio I/O setup", e); }
        // Main and Cue's output pairs and the click (specs/089).
        try { OutputSetupAccess.Push(Services.GetRequiredService<IAudioEngine>()); }
        catch (Exception e) { log.Error("Couldn't apply the output setup", e); }

        // Plugins installed from the registry (Preferences → Get Plug-ins) live in their own folder.
        try { PluginScan.EnsureStoreScanPath(Services.GetRequiredService<IPluginCatalog>(), Services.GetRequiredService<IPluginStore>()); }
        catch (Exception e) { log.Error("Couldn't register the Get Plug-ins folder", e); }

        // Linux: JUCE's message loop isn't shared with Avalonia's (it is on macOS/Windows),
        // so hosted plugins' editors and timers only run when we service it. Without this,
        // plugin windows stay black and unclosable and plugins stay silent.
        // Paced: 10 ms while plugins post messages, 100 ms once the queue stays empty (#55).
        if (OperatingSystem.IsLinux())
        {
            var pacer = new PumpPacer();
            Avalonia.Threading.DispatcherTimer? pump = null;
            pump = new Avalonia.Threading.DispatcherTimer(PumpPacer.Fast,
                Avalonia.Threading.DispatcherPriority.Normal, (_, _) =>
                {
                    var next = pacer.Next(NotaEngine.PumpPluginHost());
                    if (pump!.Interval != next) pump.Interval = next;
                });
            pump.Start();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // A downloaded update installs once the app is gone (see AppUpdater).
            // When a language restart is also pending, the installer owns the relaunch: a second helper
            // would start the old app, or open the bundle between the installer's two moves.
            desktop.Exit += (_, _) =>
            {
                var updater = Services.GetRequiredService<IAppUpdater>();
                if (updater.PendingVersion is not null && AppRestart.Pending)
                {
                    updater.RelaunchAfterInstall = true;
                    AppRestart.Cancel();
                }
                updater.RunPendingInstall();
            };
            desktop.Exit += (_, _) => AppUpdates.Shared.CancelAll();
            // A Settings → Downloads install still running when the app quits is abandoned.
            desktop.Exit += (_, _) => DownloadJobs.Shared.CancelAll();
            // A language change asked for a restart (Preferences → Appearance): start again once gone.
            desktop.Exit += (_, _) => AppRestart.RunPending();

            // Show the splash immediately, then defer the heavy engine spin-up to a
            // background dispatcher tick so the splash actually paints before the audio
            // backend loads (a synchronous VM resolve here would freeze the launch).
            var splash = new SplashWindow();
            splash.Show();
            var startedAt = DateTime.UtcNow;

            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                // Crash recovery (M7-7): check for a surviving session marker BEFORE
                // marking this session active, so a snapshot can be offered on launch.
                var recovery = Services.GetRequiredService<IRecoveryStore>();
                var pending = recovery.PendingRecovery();
                recovery.BeginSession();

                var vm = Services.GetRequiredService<MainWindowViewModel>();   // constructs the engine
                // Every audio restart may open another device (the system default changed):
                // its own input names and switched-off inputs (specs/086).
                vm.AudioApplied += () =>
                {
                    try { InputSetupAccess.Push(vm.Engine); }
                    catch (Exception e) { log.Error("Couldn't apply the Audio I/O setup", e); }
                    try { OutputSetupAccess.Push(vm.Engine); }
                    catch (Exception e) { log.Error("Couldn't apply the output setup", e); }
                };
                var main = new MainWindow { DataContext = vm, PendingRecovery = pending };
                desktop.MainWindow = main;

                // Keep the splash up for a short minimum so it doesn't just flash on a fast
                // machine, then swap it for the main window (which fires its launch dialogs).
                // Show the main window BEFORE closing the splash: with ShutdownMode =
                // OnLastWindowClose, a zero-window moment between the two would shut the app down.
                var remaining = Math.Max(0, 650 - (DateTime.UtcNow - startedAt).TotalMilliseconds);
                void Reveal() { main.Show(); splash.Close(); }
                if (remaining <= 0) Reveal();
                else
                {
                    var timer = new Avalonia.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromMilliseconds(remaining) };
                    timer.Tick += (_, _) => { timer.Stop(); Reveal(); };
                    timer.Start();
                }
            }, Avalonia.Threading.DispatcherPriority.Background);
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>Installs the interface language for this run. The saved choice, unless
    /// NOTA_UI_LANGUAGE names one — a code, or the path of a language file (spec 077) — which
    /// holds for this run only and is never saved, for screenshots, layout checks and
    /// translators. The OS language is deliberately not consulted: English is the default
    /// everywhere (spec 068). A language that is not installed reads as English; the stored
    /// choice is left as written, so it can still be downloaded (LanguageStartup).</summary>
    private static void InstallLanguage(Settings settings, ILogSink log)
    {
        var wanted = Environment.GetEnvironmentVariable("NOTA_UI_LANGUAGE");
        bool isPath = !string.IsNullOrWhiteSpace(wanted)
                      && (wanted.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || System.IO.File.Exists(wanted));
        TranslationTable? fromFile = null;
        if (isPath)
        {
            fromFile = NotaLanguages.FromFile(wanted, out var fileError);
            if (fromFile is null) log.Error($"Interface language — {wanted}: {fileError}", null);
        }
        foreach (var err in NotaLanguages.LoadErrors) log.Error($"Interface language — {err}", null);
        // A key in no table, not even English, shows as itself: the smoke test rules it out.
        Loc.MissingKey += key => log.Warn($"Interface text key not found: {key}");
        string code;
        TranslationTable? table;
        if (fromFile is not null) { code = fromFile.Code; table = fromFile; }
        else
        {
            code = UiLanguage.Normalize(isPath || string.IsNullOrWhiteSpace(wanted) ? settings.Language : wanted, NotaLanguages.Codes);
            table = NotaLanguages.Find(code);
        }
        // A text asked for before this point was built in English and will not follow the language.
        if (Loc.UsedBeforeInstall) log.Warn("Interface text was built before the language was installed");
        // English is the default whatever the OS language is: only the stored choice (or the override above) is read.
        Loc.Install(table, code);
        // The system's own text (application menu, open panels) follows an explicit choice from the next start.
        if (code != UiLanguage.Default) MacSystemLanguage.Set(code);
        log.Info($"Interface language · {code}" + (fromFile is not null ? $" (file {wanted})" : ""));
    }

    // App.axaml is loaded in Initialize(), before the language is installed, so the macOS
    // application menu's few texts are set here, once it is: each item is found by its English
    // header in App.axaml and given the text of its key.
    private void TranslateAppMenu()
    {
        if (NativeMenu.GetMenu(this) is not { } menu) return;
        foreach (var item in menu.Items.OfType<NativeMenuItem>())
            item.Header = item.Header switch
            {
                "About Nota" => Loc.T("common.about_nota"),
                "Check for updates…" => Loc.T("common.check_for_updates"),
                "Settings…" => Loc.T("app.settings"),
                var other => other,
            };
    }

    // macOS application-menu items (About / Check for updates / Settings) forward to the main window.
    private MainWindow? MainWin =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
    private void OnAppAbout(object? sender, EventArgs e) => MainWin?.ShowAbout();
    private void OnAppPreferences(object? sender, EventArgs e) => MainWin?.ShowPreferences();
    private void OnAppCheckUpdates(object? sender, EventArgs e) => _ = MainWin?.CheckForUpdatesAsync();
}
