// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Shared base for every secondary/dialog window so they all wear the same modern
// frameless chrome as the MainWindow: the client area is extended under the decorations
// with a slim custom title bar (centered doc title, draggable, macOS traffic lights in the
// left inset). Subclasses set their content via SetBody() instead of Content, so the title
// bar is never clobbered.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

public abstract class NotaWindow : Window
{
    private readonly ContentControl _bodyHost = new()
    {
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    // A MIDI-learn glass that covers this window's body. Dormant (never drawn, never
    // hit-tested) until a caller wires it to the shared service via EnableMidiLearn — so
    // popped-out device chains / clip editors / rack full-UI editors can be mapped too.
    private readonly MidiLearnOverlay _learnGlass = new() { IsHitTestVisible = false };

    protected NotaWindow() : this(0) { }

    /// <summary>With <paramref name="ownTitleBarHeight"/> &gt; 0 the subclass draws its own
    /// title bar of that height at the top of its body (the device window): the client area
    /// is still extended under the decorations on macOS/Windows, but no shared bar is built.
    /// Linux keeps the native frame either way.</summary>
    protected NotaWindow(double ownTitleBarHeight)
    {
        Background = NotaPalette.Panel;   // a dialog body is the panel tone

        // Body + learn glass share the space below the title bar (glass on top).
        var bodyLayer = new Panel();
        bodyLayer.Children.Add(_bodyHost);
        bodyLayer.Children.Add(_learnGlass);
        bodyLayer.Children.Add(OverlayLayer);

        var dock = new DockPanel();

        // Linux: keep the native OS window frame (GNOME/KDE draw their own title bar with
        // the window Title) — no extended client area, no custom bar. Matches MainWindow's
        // Linux treatment. macOS/Windows get the frameless modern chrome with a slim custom
        // title bar (extend the client area, a 32px raised band), so every window reads as one family.
        if (!OperatingSystem.IsLinux() && ownTitleBarHeight > 0)
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = ownTitleBarHeight;
        }
        else if (!OperatingSystem.IsLinux())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 32;

            var titleText = _chromeTitle = new TextBlock
            {
                Classes = { "Heading" },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("Brush.TextPrimary"),
            };
            titleText[!TextBlock.TextProperty] = this[!TitleProperty];   // mirror the window Title

            // Left inset (78) reserves the macOS traffic-light corner; on Windows reserve the
            // right edge for the native caption buttons that overlay the extended title bar.
            var row = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("78,*,Auto"),
                Margin = OperatingSystem.IsMacOS()
                    ? new Thickness(0, 0, 12, 0)
                    : new Thickness(0, 0, 180, 0),
            };
            Grid.SetColumn(titleText, 1);
            row.Children.Add(titleText);
            // The OS draws its window buttons at system size: keep their room fixed on
            // screen whatever the interface scale (spec 027).
            NotaUiScale.Track(row, s =>
            {
                if (OperatingSystem.IsMacOS()) row.ColumnDefinitions[0].Width = new GridLength(78 / s);
                else row.Margin = new Thickness(0, 0, 180 / s, 0);
            });

            var bar = _chromeBar = new Border
            {
                Height = 32,
                Background = Res("Brush.SurfaceRaised"),
                BorderBrush = Res("Brush.Seam"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            };
            bar.BindResource(Border.BoxShadowProperty, "Shadow.Raised");   // the raised header's bevel
            bar.PointerPressed += OnChromePressed;

            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
        }

        dock.Children.Add(bodyLayer);   // fills the remaining space
        // Everything, title bar included, sits under the interface scale (spec 027).
        double titleBar = OperatingSystem.IsLinux() ? 0 : ownTitleBarHeight > 0 ? ownTitleBarHeight : 32;
        base.Content = NotaUiScale.Wrap(this, dock, titleBar);
    }

    /// <summary>A layer over the body for overlays that open in this window (the command palette).</summary>
    internal Panel OverlayLayer { get; } = new();

    private Border? _chromeBar;
    private TextBlock? _chromeTitle;

    /// <summary>Let the title bar dissolve into the body: no band colour, no hairline, no
    /// title — only the traffic lights and the drag area remain. For launcher-style windows
    /// whose body carries its own identity (the start screen).</summary>
    protected void BlendTitleBar()
    {
        if (_chromeBar is null) return;
        _chromeBar.Background = Brushes.Transparent;   // still hit-tested, so it drags
        _chromeBar.BorderThickness = default;
        if (_chromeTitle is not null) _chromeTitle.IsVisible = false;
    }

    /// <summary>Set the window's body (below the title bar). Use this instead of Content.</summary>
    protected void SetBody(Control body) => _bodyHost.Content = body;

    /// <summary>Wire this window's MIDI-learn glass to the shared service so controls
    /// hosted here can be highlighted/selected while learn is armed.</summary>
    public void EnableMidiLearn(MidiLearnService? service) => _learnGlass.Service = service;

    /// <summary>True when the OS draws the window frame and title (Linux): a subclass's own
    /// title bar then needs no inset for window buttons and does not drag the window.</summary>
    protected static bool NativeFrame => OperatingSystem.IsLinux();

    /// <summary>Padding for a subclass's own title row at interface scale <paramref name="s"/>:
    /// room for the OS window buttons — the macOS traffic lights (left), the Windows caption
    /// buttons (3 × 46, right) — which the OS draws at system size, so it stays fixed on
    /// screen (spec 027). Linux draws its frame above the window: plain padding.</summary>
    protected static Thickness CaptionPadding(double s) => new(
        !NativeFrame && OperatingSystem.IsMacOS() ? 76 / s : 12, 0,
        !NativeFrame && OperatingSystem.IsWindows() ? 8 + 138 / s : 8, 0);

    // Drag the window by its title bar; a double-click toggles maximise when resizable.
    protected void OnChromePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (CanResize && e.ClickCount == 2)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            return;
        }
        BeginMoveDrag(e);
    }

    // Fullscreen hides the shared bar: dialogs join the main window's full-bleed look,
    // leaving fullscreen restores it. (A subclass's own functional toolbar — the device
    // window's preset bar — draws itself and stays.) Linux never builds the bar at all.
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WindowStateProperty && _chromeBar is { } bar)
            bar.IsVisible = WindowState != WindowState.FullScreen;
    }

    private IBrush? Res(string key) => this.TryFindResource(key, out var v) && v is IBrush b ? b : null;
}
