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
        Background = NotaPalette.BgApp;

        // Body + learn glass share the space below the title bar (glass on top).
        var bodyLayer = new Panel();
        bodyLayer.Children.Add(_bodyHost);
        bodyLayer.Children.Add(_learnGlass);
        bodyLayer.Children.Add(OverlayLayer);

        var dock = new DockPanel();

        // Linux: keep the native OS window frame (GNOME/KDE draw their own title bar with
        // the window Title) — no extended client area, no custom bar. Matches MainWindow's
        // Linux treatment. macOS/Windows get the frameless modern chrome with a slim custom
        // title bar (extend the client area, 36px band), so every window reads as one family.
        if (!OperatingSystem.IsLinux() && ownTitleBarHeight > 0)
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = ownTitleBarHeight;
        }
        else if (!OperatingSystem.IsLinux())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 36;

            var titleText = _chromeTitle = new TextBlock
            {
                Classes = { "Caption" },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Res("Brush.TextSecondary"),
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

            var bar = _chromeBar = new Border
            {
                Height = 36,
                Background = Res("Brush.ChromeBg"),
                BorderBrush = Res("Brush.BorderDefault"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = row,
            };
            bar.PointerPressed += OnChromePressed;

            DockPanel.SetDock(bar, Dock.Top);
            dock.Children.Add(bar);
        }

        dock.Children.Add(bodyLayer);   // fills the remaining space
        base.Content = dock;
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

    private IBrush? Res(string key) => this.TryFindResource(key, out var v) && v is IBrush b ? b : null;
}
