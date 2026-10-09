// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The device window — a rack chain's instrument or effect popped out of a Drum Rack,
// Instrument Rack, Audio Effect Rack or Nota Rhythm voice. The card's header moves into
// the window's own title bar: name · track on the left, then the preset picker
// "‹ Name ⌄ ›", the S / L size and the bypass switch. The device body sits below at its
// card size, and a status bar closes the window (a one-line summary + the processing-type
// badge).
//
// The title bar shares its row with the OS window buttons — the traffic lights on the
// left on macOS, the caption buttons on the right on Windows (Linux keeps the native
// frame above). Whatever does not fit beside them — always the preset picker on a mini
// (260) body — moves to a second row under the title bar, so nothing slides under a
// window button.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Nota.App;

/// <summary>A device's presets for the window's picker: names, the current one (-1 = none
/// applied yet, shown as Init) and how to apply one.</summary>
internal sealed record DeviceWindowPresets(IReadOnlyList<string> Names, int Current, Action<int> Apply);

/// <summary>A device that comes in two sizes: whether it is at S, and how to switch.</summary>
internal sealed record DeviceWindowSize(bool Mini, Action<bool> SetMini);

/// <summary>A device that can be bypassed (read live, so a flip elsewhere follows).</summary>
internal sealed record DeviceWindowBypass(Func<bool> Get, Action<bool> Set);

/// <summary>One build of the window's content. <see cref="BodyHeight"/> NaN = the body's
/// own height (the param grid); a device body is the card body height.</summary>
internal sealed record DeviceWindowPage(Control Body, double BodyWidth, double BodyHeight,
    DeviceWindowPresets? Presets = null, DeviceWindowSize? Size = null);

internal sealed class DeviceWindow : NotaWindow
{
    public const double BarH = 32, RowH = 30, StatusH = 22, PickerW = 188, PickerH = 22;
    private const double Gap = 10, NameMin = 90;   // the least the name · track keeps in the title row
    // Room the OS window buttons take in the title row: the macOS traffic lights (left) and
    // the Windows caption buttons (3 × 46, right). Linux draws its frame above the window.
    private static readonly double LeftInset = !NativeFrame && OperatingSystem.IsMacOS() ? 76 : 12;
    private static readonly double RightInset = !NativeFrame && OperatingSystem.IsWindows() ? 8 + 138 : 8;

    /// <summary>The track whose chain this device sits in — the command palette's target (CP-3).</summary>
    public int ContextTrackId { get; set; } = -1;

    private readonly string _name, _context, _badge;
    private readonly Func<Action<Action>, DeviceWindowPage> _build;
    private readonly DeviceWindowBypass? _bypass;
    private readonly Func<string>? _status;
    private readonly List<Action> _ticks = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private TextBlock _statusText = MonoText(10, NotaPalette.TextMuted);

    private SwitchTrack? _switch;
    private Control? _bypassTag, _bodyHost;
    private bool? _shownBypassed;   // null = not yet painted for the current body

    /// <param name="name">The device name ("Nota Bass").</param>
    /// <param name="context">Where it lives, shown after the name ("Keys · pad C1").</param>
    /// <param name="badge">The processing type for the status bar ("SUBTRACTIVE").</param>
    /// <param name="build">Builds the content; registers its live-follow ticks. Called again
    /// by <see cref="Refill"/> (a preset applied, the size switched, a sample dropped).</param>
    public DeviceWindow(string name, string context, string badge, Func<Action<Action>, DeviceWindowPage> build,
        DeviceWindowBypass? bypass = null, Func<string>? status = null) : base(BarH)
    {
        _name = name; _context = context; _badge = badge.ToUpperInvariant();
        _build = build; _bypass = bypass; _status = status;
        Title = context.Length > 0 ? $"{name} · {context}" : name;
        SizeToContent = SizeToContent.WidthAndHeight;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _timer.Tick += (_, _) => Tick();
        Opened += (_, _) => _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Refill();
    }

    /// <summary>Rebuild the content (fresh ticks) so every control shows the current values.</summary>
    public void Refill()
    {
        _ticks.Clear();
        var page = _build(_ticks.Add);
        SetBody(Layout(page));
        _shownBypassed = null;
        SyncBypass();
        SyncStatus();
    }

    /// <summary>Show over the window that holds <paramref name="anchor"/>; over the main
    /// window, wire MIDI Learn and forward its keys (transport, note keys, undo …).</summary>
    public void ShowFrom(Control anchor)
    {
        if (TopLevel.GetTopLevel(anchor) is not Window owner) { Show(); return; }
        if (owner is MainWindow mw)
        {
            EnableMidiLearn(mw.LearnService);
            AddHandler(KeyDownEvent, (_, e) => mw.HandleTransportKeyTunnel(e), RoutingStrategies.Tunnel);
            KeyDown += (_, e) => { if (!e.Handled) mw.HandleFloatingKeyDown(e); };
            KeyUp += (_, e) => { if (!e.Handled) mw.HandleKeyUp(e); };
            MainWindow.ReleaseTextFocusOnOutsidePress(this);
        }
        Show(owner);
    }

    private void Tick()
    {
        for (int i = 0; i < _ticks.Count; i++) _ticks[i]();
        SyncBypass();
        SyncStatus();
    }

    // ---- layout ---------------------------------------------------------------------

    private Control Layout(DeviceWindowPage page)
    {
        double w = double.IsNaN(page.BodyWidth) ? 0 : page.BodyWidth;
        double presetW = page.Presets is null ? 0 : PickerW + Gap;
        double togglesW = (page.Size is null ? 0 : 34 + Gap) + (_bypass is null ? 0 : 26 + Gap);
        double free = Math.Max(w, 260) - LeftInset - RightInset;
        // Mini bodies (under 400) always drop the picker to its own row, as does a title
        // row the window buttons leave too little of.
        bool presetInBar = page.Presets is null || (w >= 400 && free - presetW - togglesW >= NameMin);
        bool togglesInBar = page.Presets is not null && presetInBar || free - togglesW >= NameMin;

        var barControls = Row();
        var rowControls = Row();
        Control? rowPicker = null;
        if (page.Presets is { } p)
        {
            if (presetInBar) barControls.Children.Add(Picker(p, stretch: false));
            else rowPicker = Picker(p, stretch: true);
        }
        foreach (var c in Toggles(page)) (togglesInBar ? barControls : rowControls).Children.Add(c);

        var dock = new DockPanel();
        var bar = TitleBar(barControls);
        DockPanel.SetDock(bar, Dock.Top); dock.Children.Add(bar);
        if (rowPicker is not null || rowControls.Children.Count > 0)
        {
            var row = SecondRow(rowPicker, rowControls);
            DockPanel.SetDock(row, Dock.Top); dock.Children.Add(row);
        }
        var status = StatusBar();
        DockPanel.SetDock(status, Dock.Bottom); dock.Children.Add(status);
        dock.Children.Add(BodyArea(page));
        return dock;
    }

    private static StackPanel Row() => new()
    {
        Orientation = Orientation.Horizontal, Spacing = Gap, VerticalAlignment = VerticalAlignment.Center,
    };

    // Name · track on the left, the controls that fit on the right. Empty bar area drags
    // the window (controls handle their own presses).
    private Control TitleBar(Control controls)
    {
        var name = new TextBlock
        {
            Text = _name, FontSize = NotaType.Name, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var context = MonoText(10, NotaPalette.TextMuted);
        context.Text = _context.Length > 0 ? "· " + _context : "";
        context.Margin = new Thickness(8, 1, 0, 0);
        // The name keeps its width; the context trims first.
        var names = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), VerticalAlignment = VerticalAlignment.Center };
        name.MaxWidth = 260;
        Grid.SetColumn(context, 1);
        names.Children.Add(name); names.Children.Add(context);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Gap };
        Grid.SetColumn(controls, 1);
        grid.Children.Add(names); grid.Children.Add(controls);

        var bar = new Border
        {
            Height = BarH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(LeftInset, 0, RightInset, 0), Child = grid,
        };
        if (!NativeFrame) bar.PointerPressed += OnChromePressed;
        return bar;
    }

    private static Control SecondRow(Control? picker, StackPanel controls)
    {
        // The picker stretches across; S / L and bypass (when they moved here) sit right.
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = Gap, VerticalAlignment = VerticalAlignment.Center };
        if (picker is not null) grid.Children.Add(picker);
        Grid.SetColumn(controls, 1); grid.Children.Add(controls);
        return new Border
        {
            Height = RowH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(6, 0), Child = grid,
        };
    }

    private Control Picker(DeviceWindowPresets p, bool stretch)
    {
        int n = p.Names.Count;
        void Step(int dir) => p.Apply(p.Current < 0 ? (dir > 0 ? 0 : n - 1) : ((p.Current + dir) % n + n) % n);
        // The picker's frame + two step cells (20 each, with a rule) leave the rest for the name.
        var picker = DeviceCardKit.PresetPicker(p.Names, p.Current, p.Current >= 0 ? p.Names[p.Current] : "", p.Apply, Step,
            nameWidth: stretch ? double.NaN : PickerW - 2 * PickerH - 4, height: PickerH);
        if (!stretch) picker.Width = PickerW;
        return picker;
    }

    private IEnumerable<Control> Toggles(DeviceWindowPage page)
    {
        if (page.Size is { } size)
        {
            var seg = DeviceCardKit.Segments(new[] { "S", "L" }, () => size.Mini ? 0 : 1,
                i => { if ((i == 0) != size.Mini) { size.SetMini(i == 0); Dispatcher.UIThread.Post(Refill); } },
                out _, padX: 0, minSegWidth: 16, fontSize: 9);
            ToolTip.SetTip(seg, "Size: S (260 × 260) / L (700 × 260)");
            yield return seg;
        }
        if (_bypass is { } byp)
        {
            _switch = new SwitchTrack();
            var host = new Border
            {
                Padding = new Thickness(4, 5), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                VerticalAlignment = VerticalAlignment.Center, Child = _switch,
            };
            host.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(host).Properties.IsLeftButtonPressed) return;
                e.Handled = true;
                byp.Set(!byp.Get());
                SyncBypass();
            };
            yield return host;
        }
    }

    private Control BodyArea(DeviceWindowPage page)
    {
        _bodyHost = page.Body;
        var tag = new Border
        {
            Height = 18, Padding = new Thickness(7, 0), CornerRadius = NotaRadius.Badge, BorderThickness = new Thickness(1),
            Background = NotaPalette.AccentSubtle, BorderBrush = NotaPalette.BorderBrass,
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(8),
            IsHitTestVisible = false, IsVisible = false,
            Child = Caps(MonoText(9, NotaPalette.AccentHover), "BYPASSED"),
        };
        _bypassTag = tag;
        return new Border
        {
            Width = page.BodyWidth, Height = page.BodyHeight, Background = NotaPalette.BgApp, ClipToBounds = true,
            Child = new Panel { Children = { page.Body, tag } },
        };
    }

    private Control StatusBar()
    {
        _statusText = MonoText(10, NotaPalette.TextMuted);   // fresh per layout: a refill drops the old tree
        _statusText.TextTrimming = TextTrimming.CharacterEllipsis;
        var badge = Caps(MonoText(9, NotaPalette.TextTertiary), _badge);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(badge, 1);
        grid.Children.Add(_statusText); grid.Children.Add(badge);
        return new Border
        {
            Height = StatusH, Background = NotaPalette.Panel, BorderBrush = NotaPalette.GraphBorder,
            BorderThickness = new Thickness(0, 1, 0, 0), Padding = new Thickness(10, 0), Child = grid,
        };
    }

    // ---- live state -----------------------------------------------------------------

    // Bypassed: the switch goes off, the body loses its brass but stays playable (no
    // opacity for state) and carries the BYPASSED tag.
    private void SyncBypass()
    {
        bool on = _bypass?.Get() ?? false;
        if (on == _shownBypassed) return;
        bool fresh = _shownBypassed is null;
        _shownBypassed = on;
        if (_switch is { } sw)
        {
            sw.IsOn = !on;
            if (sw.Parent is Control host) ToolTip.SetTip(host, on ? "Bypassed — click to enable" : "Active — click to bypass");
        }
        if (_bypassTag is { } tag) tag.IsVisible = on;
        if (_bodyHost is not { } body || (fresh && !on)) return;
        // A fresh body is dimmed once it has laid out (a ScrollViewer's content only exists then).
        if (body.IsLoaded) Inactive.Set(body, on, interactive: true);
        else body.Loaded += Deactivate;
    }

    private void Deactivate(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control c) return;
        c.Loaded -= Deactivate;
        if (_shownBypassed == true && ReferenceEquals(c, _bodyHost)) Inactive.Set(c, true, interactive: true);
    }

    private void SyncStatus()
    {
        string s = _status?.Invoke() ?? "";
        if (_statusText.Text != s) _statusText.Text = s;
    }

    private static TextBlock MonoText(double size, IBrush ink)
    {
        var tb = new TextBlock { FontSize = size, Foreground = ink, VerticalAlignment = VerticalAlignment.Center };
        tb.BindResource(TextBlock.FontFamilyProperty, "Font.Mono");
        return tb;
    }

    private static TextBlock Caps(TextBlock tb, string text)
    {
        tb.Text = text;
        tb.LetterSpacing = tb.FontSize * 0.14;
        return tb;
    }
}
