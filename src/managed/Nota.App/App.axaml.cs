// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.App;

public partial class App : Avalonia.Application
{
    /// <summary>App-wide service container (M4.1-A).</summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        // Engine: one native handle, exposed to the layers through its port.
        services.AddSingleton<NotaEngine>();
        services.AddSingleton<IAudioEngine>(sp => sp.GetRequiredService<NotaEngine>());
        services.AddSingleton(new EngineBuildInfo(NotaEngine.Version));
        // Application ports → Infrastructure implementations.
        services.AddSingleton<ILogSink, LogSink>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IPluginCatalog, PluginCatalog>();
        services.AddSingleton<IPresetLibrary, PresetLibrary>();
        services.AddSingleton<IProjectStore, ProjectStore>();
        services.AddSingleton<IPresetStore, PresetStore>();
        services.AddSingleton<IFactoryPresets, FactoryPresetCatalog>();
        services.AddSingleton<IBrowserLibrary, BrowserLibraryService>();
        services.AddSingleton<IAudioDeviceService, AudioDeviceService>();
        services.AddSingleton<IMidiDeviceService, MidiDeviceService>();
        services.AddSingleton<GamepadService>();
        services.AddSingleton<IGamepadService>(sp => sp.GetRequiredService<GamepadService>());
        services.AddSingleton<IAudioExporter, WavAudioExporter>();
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
        services.AddSingleton<McpService>();
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

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
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

    // macOS application-menu items (About / Settings) forward to the main window.
    private MainWindow? MainWin =>
        (ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow as MainWindow;
    private void OnAppAbout(object? sender, EventArgs e) => MainWin?.ShowAbout();
    private void OnAppPreferences(object? sender, EventArgs e) => MainWin?.ShowPreferences();
}
