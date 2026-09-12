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

    protected NotaWindow()
    {
        Background = NotaPalette.BgApp;

        // Body + learn glass share the space below the title bar (glass on top).
        var bodyLayer = new Panel();
        bodyLayer.Children.Add(_bodyHost);
        bodyLayer.Children.Add(_learnGlass);

        var dock = new DockPanel();

        // Linux: keep the native OS window frame (GNOME/KDE draw their own title bar with
        // the window Title) — no extended client area, no custom bar. Matches MainWindow's
        // Linux treatment. macOS/Windows get the frameless modern chrome with a slim custom
        // title bar (extend the client area, 36px band), so every window reads as one family.
        if (!OperatingSystem.IsLinux())
        {
            ExtendClientAreaToDecorationsHint = true;
            ExtendClientAreaTitleBarHeightHint = 36;

            var titleText = new TextBlock
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

            var bar = new Border
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

    /// <summary>Set the window's body (below the title bar). Use this instead of Content.</summary>
    protected void SetBody(Control body) => _bodyHost.Content = body;

    /// <summary>Wire this window's MIDI-learn glass to the shared service so controls
    /// hosted here can be highlighted/selected while learn is armed.</summary>
    public void EnableMidiLearn(MidiLearnService? service) => _learnGlass.Service = service;

    // Drag the window by its title bar; a double-click toggles maximise when resizable.
    private void OnChromePressed(object? sender, PointerPressedEventArgs e)
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

/// <summary>Concrete <see cref="NotaWindow"/> for code that builds a one-off popup window
/// inline (rack sampler / full-UI editors). Set/replace its body via <see cref="SetContent"/>.</summary>
public sealed class NotaPopupWindow : NotaWindow
{
    public void SetContent(Control body) => SetBody(body);
}
