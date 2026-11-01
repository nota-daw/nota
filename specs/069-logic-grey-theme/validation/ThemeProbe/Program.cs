// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;

internal static class Program
{
    private const BindingFlags Hidden = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    [STAThread]
    private static int Main(string[] args)
    {
        try { Run(args); return 0; }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }

    private static void Run(string[] args)
    {
        string mode = args[0], theme = args[1], root = Path.GetFullPath(args[2]);
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("NOTA_DATA_DIR", Path.Combine(root, "probe-data", "appearance"));
        if (mode == "restart")
        {
            var saved = new SettingsService();
            if (saved.Current.ColourTheme != theme) throw new InvalidOperationException("Theme did not persist across processes.");
            File.WriteAllText(Path.Combine(root, "restart-results.json"), JsonSerializer.Serialize(new[] { "theme persisted across a new process" }));
            return;
        }
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var builder = AppBuilder.Configure<Nota.App.App>().UseWin32().UseSkia().UseHarfBuzz().WithNotaFonts();
        builder.SetupWithLifetime(lifetime);
        var settings = (ISettingsService)App.Services.GetService(typeof(ISettingsService))!;
        var checks = new List<string>();
        void Require(bool ok, string label) { if (!ok) throw new InvalidOperationException(label); checks.Add(label); }
        string initialTheme = string.IsNullOrEmpty(settings.Current.ColourTheme) ? "studio-dark" : settings.Current.ColourTheme;
        var initialFile = Path.GetFullPath(Path.Combine(root, "../../../src/managed/Nota.App/Theme/Themes/", initialTheme + ".json"));
        using var initialPalette = JsonDocument.Parse(File.ReadAllText(initialFile));
        Require(((ISolidColorBrush)Brush("Brush.BgApp")).Color == Color.Parse(initialPalette.RootElement.GetProperty("colors").GetProperty("BgApp").GetString()!),
            "application startup reapplied the saved theme palette");
        settings.Current.ShowWelcomeOnStartup = false;
        settings.Current.CheckForUpdates = false;
        settings.Current.LastSeenVersion = AppInfo.Version;
        var vm = (MainWindowViewModel)App.Services.GetService(typeof(MainWindowViewModel))!;
        using var stop = new CancellationTokenSource();
        DispatcherTimer.RunOnce(() =>
        {
            var main = (MainWindow)lifetime.MainWindow!;
            main.WindowState = WindowState.Normal; main.Width = 1360; main.Height = 900;
            var timeline = main.FindControl<ArrangementView>("Timeline")!;
            var prefs = new PreferencesWindow(new SettingsViewModel(settings), vm);
            var windows = new List<Window> { main, prefs };
            while (vm.Engine.TrackCount > 0) { vm.Engine.TryGetTrackInfo(0, out var info); vm.Engine.RemoveTrack(info.Id); }
            var ids = new List<int>();
            NotaNote[] notes = { new(60, 0, .75, .7f), new(64, 1, .5, .9f), new(67, 2, .75, .6f), new(72, 3, .5, .8f) };
            foreach (var (name, color) in new[] { ("Keys", 3), ("Bass", 1), ("Pads", 6) })
            {
                int id = vm.Engine.AddInstrumentTrack(); ids.Add(id);
                vm.Engine.SetTrackName(id, name); vm.Engine.SetTrackColorIndex(id, color);
                int clip = vm.Engine.AddMidiClip(id, 0, 16);
                vm.Engine.SetClipNotes(id, clip, notes);
            }
            timeline.Refresh(); timeline.SelectTrackFromOutside(ids[0]); timeline.SetPlayhead(2);
            var roll = new PianoRollView(); roll.SetTrackColor(ArrangementView.TrackBrush(3)); roll.SetNotes(notes, 16); roll.SetPlayhead(2, true);
            var rollWindow = new Window { Title = "Piano roll", Content = roll, Width = 1100, Height = 650, Background = (IBrush)Brush("Brush.BgApp") };
            var mixer = new MixerView(vm.Engine); mixer.Refresh();
            var mixerWindow = new Window { Title = "Mixer", Content = mixer, Width = 1100, Height = 650, Background = (IBrush)Brush("Brush.BgApp") };
            rollWindow.Show(); mixerWindow.Show(); windows.Add(rollWindow); windows.Add(mixerWindow);
            prefs.Show();
            typeof(PreferencesWindow).GetMethod("Select", Hidden)!.Invoke(prefs, new object[] { 7 });
            prefs.UpdateLayout();
            var themeList = prefs.GetVisualDescendants().OfType<ListBox>().First(x => x.Classes.Contains("themes")
                && ((IEnumerable)x.ItemsSource!).Cast<object>().Any(t => ThemeId(t) == theme));
            var target = ((IEnumerable)themeList.ItemsSource!).Cast<object>().First(t => ThemeId(t) == theme);
            string Before() => JsonSerializer.Serialize(ids.Select(id => new { id, color = vm.Engine.GetTrackColorIndex(id), notes = vm.Engine.GetClipNotes(id, 0) }), new JsonSerializerOptions { IncludeFields = true });
            string data = Before();
            string ProjectData() => JsonSerializer.Serialize(ProjectService.Capture(vm.Engine,
                new TransportState(120, 1, false, false), new List<string>(), contentNames: false));
            string projectData = ProjectData();
            themeList.SelectedItem = target;
            Require(settings.Current.ColourTheme == theme, "real Appearance picker saved the selected theme");
            Require(Before() == data, "track colours and MIDI notes survived live switching");
            Require(Brush("Brush.RaisedFill") is LinearGradientBrush == (theme == "logic-grey"), "raised fill is static shading only in Logic Grey");
            // Switch away and back while the real windows and picker remain open.
            var studio = ((IEnumerable)themeList.ItemsSource!).Cast<object>().First(t => ThemeId(t) == "studio-dark");
            themeList.SelectedItem = studio;
            Require(Brush("Brush.RaisedFill") is SolidColorBrush, "Studio Dark returns to its original solid raised fill");
            themeList.SelectedItem = target;
            Require(Before() == data, "switching back preserves project data");
            Require(ProjectData() == projectData, "serialized project content is identical after switching themes");

            DispatcherTimer.RunOnce(() =>
            {
                string shots = Path.Combine(root, "screenshots"); Directory.CreateDirectory(shots);
                foreach (int percent in new[] { 100, 125, 150, 200 })
                {
                    foreach (var (label, view) in new (string, Control)[] { ("arrangement", main), ("piano-roll", rollWindow), ("mixer", mixerWindow) })
                        Capture(view, Path.Combine(shots, $"{theme}-{label}-{percent}.png"), percent / 100.0);
                }
                Capture(prefs, Path.Combine(shots, $"{theme}-appearance.png"), 1);
                Require(File.Exists(Path.Combine(shots, $"{theme}-mixer-200.png")), "all three views captured at four render scales");
                File.WriteAllText(Path.Combine(root, theme + "-results.json"), JsonSerializer.Serialize(new { theme, checks, colours = new { background = Brush("Brush.BgApp").ToString(), playhead = Brush("Brush.Playhead").ToString() } }, new JsonSerializerOptions { WriteIndented = true }));
                foreach (var w in windows.Where(w => w != main)) w.Close();
                typeof(MainWindow).GetField("_closeConfirmed", Hidden)!.SetValue(main, true); main.Close();
                stop.Cancel();
            }, TimeSpan.FromMilliseconds(700));
        }, TimeSpan.FromMilliseconds(700));
        using var watchdog = new Timer(_ => Dispatcher.UIThread.Post(() => stop.Cancel()), null, 30000, Timeout.Infinite);
        Dispatcher.UIThread.MainLoop(stop.Token);
        Require(File.Exists(Path.Combine(root, theme + "-results.json")), "UI probe completed before its watchdog");
    }

    private static string ThemeId(object t) => (string)t.GetType().GetProperty("Id")!.GetValue(t)!;
    private static object Brush(string key) => Avalonia.Application.Current!.Resources[key]!;
    private static void Capture(Control control, string path, double scale)
    {
        var size = control.Bounds.Size;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(control); bitmap.Save(path);
    }
}
