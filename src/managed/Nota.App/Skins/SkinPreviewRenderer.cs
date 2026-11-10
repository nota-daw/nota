// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Pictures of the app in a skin (spec 087, contracts/preview-surfaces.md) — what the MCP
// preview_theme tool sends back, so a client can see how a palette maps onto Nota. Everything
// happens inside one UI-thread job: wear the skin (NotaThemeService.RenderScoped), build each
// surface, lay it out at a fixed size and render it to a PNG, then put the live theme back.
// The compositor only sends frames between jobs, so the screen never shows the skin.
//
// Most surfaces are real views over the sample scene's own engine (never started), built for
// the picture and thrown away: the same theme gives the same pictures. Three are the live
// window's own parts — the whole window, its transport row and its browser — because they
// are bound to the app's one engine and library: they show the user's project as it is,
// re-coerced for the skin during the job and back right after.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Infrastructure;
using Nota.Presentation;

namespace Nota.App;

internal static class SkinPreviewRenderer
{
    private sealed record Surface(string Name, string Description, double Width, double Height, bool Live);

    /// <summary>The surfaces in the order they are rendered. Names are stable (MCP clients ask for them).</summary>
    private static readonly Surface[] Surfaces =
    {
        new("overview", "The main window as it is now: top bar, browser, arrangement and the detail panel (the user's open project)", 0, 0, true),
        new("top-bar", "The transport row: transport keys, position, tempo, view switches, CPU (live values)", 0, 0, true),
        new("arrangement", "Arrangement: track headers, MIDI and audio clips in the track colours, an automation lane", 1200, 520, false),
        new("mixer", "Mixer: channel strips with sends, a return and the master", 1200, 520, false),
        new("device-rack", "Device rack: an instrument and two audio effects as device cards", 1200, 300, false),
        new("piano-roll", "Piano roll: chords on the keyboard grid with the velocity lane", 1200, 480, false),
        new("browser", "The browser as it is now: categories, the list and the search field (the user's library)", 0, 0, true),
        new("preferences", "Preferences → Appearance: the theme cards, accent, scale and text size", 900, 640, false),
        new("menu", "A track context menu: rows with icons and shortcuts, a submenu, a separator", 320, 0, false),
        new("dialog", "A confirmation dialog with a primary and a secondary button", 480, 0, false),
    };

    public static IReadOnlyList<string> Names => Surfaces.Select(s => s.Name).ToList();

    public sealed record Image(string Name, string Description, byte[] Png);

    /// <summary>Render <paramref name="theme"/> on <paramref name="names"/> (every surface when null).
    /// Starts and ends on the UI thread. Unknown names come back in <c>Unknown</c>.
    ///
    /// Two phases. First the sample views are built in their own windows, shown far off screen
    /// and fully transparent, wearing the live theme. The UI thread is then let go a few times,
    /// so Loaded handlers, posted work and virtualising lists (realised from the layout manager's
    /// viewport events) settle. Then one job wears the skin, re-skins and lays out every tree,
    /// draws them and puts the live theme back.</summary>
    public static async Task<(List<Image> Images, List<string> Unknown)> RenderAsync(NotaColourTheme theme, bool themeAccent, IReadOnlyList<string>? names)
    {
        var wanted = names is { Count: > 0 } ? names : Names;
        var unknown = wanted.Where(n => Surfaces.All(s => s.Name != n)).Distinct().ToList();
        var surfaces = Surfaces.Where(s => wanted.Contains(s.Name)).ToList();
        var images = new List<Image>();
        if (surfaces.Count == 0) return (images, unknown);

        var hosts = new List<(Surface Surface, Window Host, Control Content)>();
        try
        {
            var scene = surfaces.Any(s => !s.Live) ? SampleScene.Shared : null;
            foreach (var s in surfaces.Where(s => !s.Live))
            {
                try { if (Host(s, scene!) is { } h) hosts.Add(h); }
                catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException) { }
            }
            for (int i = 0; i < 4; i++) await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);

            var live = new List<Visual>();
            NotaThemeService.RenderScoped(theme, themeAccent, () => NotaTextSize.Scoped(0, () =>
            {
                foreach (var s in surfaces)
                {
                    byte[]? png = null;
                    try
                    {
                        if (s.Live) png = RenderLive(s, live);
                        else if (hosts.FirstOrDefault(h => h.Surface == s) is { Host: not null } h) png = Snap(h.Host, h.Content);
                    }
                    catch (Exception e) when (e is InvalidOperationException or ArgumentException or NullReferenceException)
                    {
                        png = null;   // a surface that cannot be drawn is left out, the rest still come back
                    }
                    if (png is not null) images.Add(new Image(s.Name, s.Description, png));
                }
            }));
            // The live parts were re-skinned for the picture; give them the live skin back.
            foreach (var v in live) NotaSkin.Reapply(v);
        }
        finally
        {
            foreach (var (_, host, _) in hosts) host.Close();
        }
        return (images, unknown);
    }

    // ── the live window's own parts ─────────────────────────────────────────────────────

    private static byte[]? RenderLive(Surface s, List<Visual> touched)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: MainWindow main })
            return null;
        Control? target = s.Name switch
        {
            "overview" => main,
            "top-bar" => main.FindControl<Border>("TransportBar"),
            "browser" => main.FindControl<BrowserView>("Browser"),
            _ => null,
        };
        if (target is null || target.Bounds.Width < 1 || target.Bounds.Height < 1) return null;
        touched.Add(target);
        NotaSkin.Reapply(target);
        // Layout with the skin's typeface, at the size it has on screen.
        if (target is TopLevel top)
        {
            var size = top.ClientSize;
            top.Measure(size);
            top.Arrange(new Rect(size));
            return Snap(top, size);
        }
        var bounds = target.Bounds;
        target.Measure(bounds.Size);
        target.Arrange(bounds);
        return Snap(target, bounds.Size);
    }

    // ── built from the sample scene ─────────────────────────────────────────────────────

    // A surface in its own window: shown, so the layout manager and Loaded run, but far off
    // screen, fully transparent, not activated and not in the task bar.
    private static (Surface, Window, Control)? Host(Surface s, SampleScene scene)
    {
        Window host;
        Control content;
        switch (s.Name)
        {
            case "preferences":
            {
                var settings = App.Services.GetRequiredService<ISettingsService>();
                var win = new PreferencesWindow(new SettingsViewModel(settings));
                win.ShowPage("Appearance");
                host = win;
                break;
            }
            case "dialog":
                host = new ConfirmWindow(Loc.T("main.browser.delete_project"),
                    Loc.F("main.browser.move_to_the_trash", "Night Drive"), Loc.T("common.delete"), Loc.T("common.cancel"));
                break;
            default:
                var built = Build(s, scene);
                if (built is null) return null;
                host = new Window { Background = NotaPalette.BgApp, Content = built };
                break;
        }
        host.ShowInTaskbar = false;
        host.ShowActivated = false;
        host.CanResize = false;
        host.Opacity = 0;
        host.WindowStartupLocation = WindowStartupLocation.Manual;
        host.Position = new PixelPoint(-32000, -32000);
        host.SizeToContent = s.Height > 0 ? SizeToContent.Manual : SizeToContent.Height;
        host.Width = s.Width;
        if (s.Height > 0) host.Height = s.Height;
        host.Show();
        // macOS gives a window shown off screen no size of its own: set it once it exists.
        host.Width = s.Width;
        if (s.Height > 0) host.Height = s.Height;
        content = host.Content as Control ?? throw new InvalidOperationException("nothing to draw");
        Refresh(content, scene);
        return (s, host, content);
    }

    private static Control? Build(Surface s, SampleScene scene) => s.Name switch
    {
        "arrangement" => new ArrangementView { Engine = scene.Engine },
        "mixer" => new MixerView(scene.Engine),
        "device-rack" => new DeviceChainView(scene.Engine, App.Services.GetRequiredService<IFactoryPresets>()),
        "piano-roll" => PianoRoll(scene),
        "menu" => Menu(scene),
        _ => null,
    };

    // Views that fill themselves from the engine once they are in a tree.
    private static void Refresh(Control content, SampleScene scene)
    {
        switch (content)
        {
            case ArrangementView a: a.Refresh(); break;
            case MixerView m: m.Refresh(); break;
            case DeviceChainView d: d.Show(scene.Keys); d.RefreshSynthLive(); break;
        }
    }

    private static Control PianoRoll(SampleScene scene)
    {
        var e = scene.Engine;
        e.TryGetClipInfo(scene.Keys, scene.KeysClip, out var info);
        var set = new Nota.Application.Editing.ClipSet(
            new[] { new Nota.Application.Editing.OpenClip(scene.Keys, scene.KeysClip, info.StartBeat, info.LengthBeats > 0 ? info.LengthBeats : 16) },
            scene.Keys, scene.KeysClip, 4);
        var roll = new PianoRollView { BeatsPerBar = 4 };
        SolidColorBrush Brush(int track) => ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(e, track));
        roll.SetTrackColor(Brush(scene.Keys));
        roll.SetClipIdentity(scene.Keys, scene.KeysClip);
        roll.SetClips(set, set.Clips.Select(c => new Nota.Application.Editing.ClipNotes(c.TrackId, c.ClipIndex, e.GetClipNotes(c.TrackId, c.ClipIndex))).ToList(), Brush);
        return roll;
    }

    // The track menu's rows laid out as the menu presenter draws them (a popup has no window
    // to render here), with the first row hovered.
    private static Control? Menu(SampleScene scene)
    {
        var arrangement = new ArrangementView { Engine = scene.Engine };
        var flyout = arrangement.BuildTrackMenu(arrangement, scene.Keys);
        if (flyout is null) return null;
        var items = flyout.Items.OfType<Control>().ToList();
        flyout.Items.Clear();
        var panel = new StackPanel();
        Grid.SetIsSharedSizeScope(panel, true);
        foreach (var item in items) panel.Children.Add(item);
        if (items.OfType<MenuItem>().FirstOrDefault(m => m.IsEnabled) is { } first)
            ((IPseudoClasses)first.Classes).Set(":pointerover", true);
        return new Border
        {
            Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1),
            CornerRadius = NotaRadius.Field, Padding = new Thickness(4), Child = panel,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
        };
    }

    // ── layout and pixels ───────────────────────────────────────────────────────────────

    // One hosted surface, in the worn skin: re-skin its tree, lay it out, and draw its content on
    // the window's ground (the window itself is transparent and off screen).
    private static byte[] Snap(Window host, Control content)
    {
        NotaSkin.Reapply(host);
        host.UpdateLayout();
        host.UpdateLayout();
        var size = content.Bounds.Size;
        var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height)));
        using var bitmap = new RenderTargetBitmap(px, new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
            ctx.FillRectangle(host.Background ?? NotaPalette.BgApp, new Rect(0, 0, px.Width, px.Height));
        using var top = new RenderTargetBitmap(px, new Vector(96, 96));
        top.Render(content);
        using (var ctx = bitmap.CreateDrawingContext(false))
            ctx.DrawImage(top, new Rect(0, 0, px.Width, px.Height));
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return ms.ToArray();
    }

    private static byte[] Snap(Visual visual, Size size)
    {
        var px = new PixelSize(Math.Max(1, (int)Math.Ceiling(size.Width)), Math.Max(1, (int)Math.Ceiling(size.Height)));
        using var bitmap = new RenderTargetBitmap(px, new Vector(96, 96));
        bitmap.Render(visual);
        using var ms = new MemoryStream();
        bitmap.Save(ms);
        return ms.ToArray();
    }
}
