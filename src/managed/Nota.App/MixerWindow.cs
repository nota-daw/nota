// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Mixer in its own window (opened from View → Mixer). It hosts the single MixerView
// instance the main window owns (reparented, not rebuilt), so meters/state carry over and
// the tick loop keeps updating it while the window is open. Closing detaches the mixer.
// Its own title bar carries the Strips | Stereo Map switch (specs/008).

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace Nota.App;

internal sealed class MixerWindow : NotaWindow
{
    public ContentControl Host { get; }
    public event Action? WindowClosed;
    /// <summary>The user picked a view in the title bar.</summary>
    public event Action<MixerViewMode>? ViewPicked;
    private readonly Action<int> _setViewSegment;

    private const double BarH = 36;

    private readonly MainWindow _owner;

    public MixerWindow(MainWindow owner, MixerViewMode view) : base(BarH)
    {
        _owner = owner;

        // Route transport keys (Space/Return) in the tunnel phase so a focused control here
        // can't steal them, exactly like the main window does.
        AddHandler(KeyDownEvent, (_, e) => _owner.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);
        // Escape closes the mixer, unless a text field is being edited (it owns Escape then).
        KeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape || e.Handled || FocusManager?.GetFocusedElement() is TextBox) return;
            e.Handled = true;
            Close();
        };

        Title = "Mixer";
        Width = 1040;
        Height = 460;
        MinWidth = 560;
        MinHeight = 280;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        Host = new ContentControl { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };

        // Title bar: caption left, the view switch centred; empty bar area drags the window.
        var caption = new TextBlock
        {
            Text = "Mixer", FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var seg = ClipEditorKit.Segments(new[] { "Strips", "Stereo map" }, view == MixerViewMode.StereoMap ? 1 : 0,
            i => ViewPicked?.Invoke(i == 1 ? MixerViewMode.StereoMap : MixerViewMode.Strips), out _setViewSegment);
        seg.HorizontalAlignment = HorizontalAlignment.Center;
        seg.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        grid.Children.Add(caption);
        Grid.SetColumn(seg, 1);
        grid.Children.Add(seg);
        var bar = new Border
        {
            Height = BarH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 0, 0, 1), Child = grid,
        };
        NotaUiScale.Track(bar, s => bar.Padding = CaptionPadding(s));
        if (!NativeFrame) bar.PointerPressed += OnChromePressed;

        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(Host);
        SetBody(dock);
    }

    /// <summary>Repaints the title-bar switch (the view changed elsewhere).</summary>
    public void ShowView(MixerViewMode view) => _setViewSegment(view == MixerViewMode.StereoMap ? 1 : 0);

    /// <summary>Fits the window to <paramref name="stripsWidth"/> (the mixer's strip row), so
    /// strips fill it with no dead area: clamped to [MinWidth, the screen's working width].
    /// A window that grows past the screen's right edge is moved back onto it.</summary>
    public void FitWidth(double stripsWidth)
    {
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        // The strips are measured inside the interface scale (spec 027). Once shown the frame
        // is in screen units; before that it still holds design units, which the first show
        // scales — so the factor applies only to a visible window.
        double s = IsVisible ? NotaUiScale.Factor : 1;
        double max = screen is null ? double.PositiveInfinity : screen.WorkingArea.Width / screen.Scaling / (IsVisible ? 1 : NotaUiScale.Factor);
        double w = Math.Clamp(Math.Ceiling(stripsWidth * s), MinWidth, Math.Max(MinWidth, max));
        if (Math.Abs(w - Width) < 0.5) return;
        Width = w;
        if (screen is not null && IsVisible)
        {
            int right = screen.WorkingArea.Right, px = (int)Math.Ceiling(w * screen.Scaling);
            if (Position.X + px > right) Position = new PixelPoint(Math.Max(screen.WorkingArea.X, right - px), Position.Y);
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        // Detach the mixer so it can be re-hosted next time, then run this window's pending
        // layout so no entry queued for it here outlives the move (see MainWindow.SetHost).
        Host.Content = null;
        UpdateLayout();
        WindowClosed?.Invoke();
    }
}
