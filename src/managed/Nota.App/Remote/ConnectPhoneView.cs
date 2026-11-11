// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Connect Phone (design 5a): the popup under the top bar's Remote button and View → Connect
// Phone…. One switch when Remote is off; when on, a large QR, the address (open it in the
// browser, copy it; another address of the computer is a click away, #85) and the four-digit
// code with its countdown ring, then the connected phones (signal, name, latency and screen,
// the track each plays, ✕ to drop it).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Remote;
using Nota.Application.Localization;

namespace Nota.App;

internal sealed class ConnectPhoneView : Border
{
    private readonly RemoteService _remote;
    private readonly ISettingsService _settings;
    private readonly IAudioEngine _engine;
    private readonly StackPanel _body = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private QrView? _qr;
    private TextBlock? _code, _left;
    private Arc? _ring;
    private string _lastDevices = "";

    public ConnectPhoneView(RemoteService remote, ISettingsService settings, IAudioEngine engine)
    {
        _remote = remote;
        _settings = settings;
        _engine = engine;
        Width = 400;
        Background = NotaPalette.SurfaceRaised;
        BorderBrush = NotaPalette.BorderStrong;
        BorderThickness = new Thickness(1);
        CornerRadius = NotaRadius.Card;
        ClipToBounds = true;
        Child = _body;

        _timer.Tick += (_, _) => Tick();
        AttachedToVisualTree += (_, _) => { _remote.Changed += OnChanged; _timer.Start(); Build(); };
        DetachedFromVisualTree += (_, _) => { _remote.Changed -= OnChanged; _timer.Stop(); };
    }

    private void OnChanged() => Dispatcher.UIThread.Post(() =>
    {
        // The code rotating or a phone's latency moving only updates text; a phone coming or
        // going, or the network's addresses changing (a cable plugged in), rebuilds.
        string sig = string.Join("|", _remote.Hub.Devices.Select(d => $"{d.ConnectionId}:{d.Name}:{d.TrackId}")) + ";" + LinksSig();
        if (sig != _lastDevices || _qr is null != !_remote.Running) Build();
        else Tick();
    });

    private void Build()
    {
        _body.Children.Clear();
        _qr = null; _code = _left = null; _ring = null;
        bool on = _settings.Current.RemoteEnabled;

        var title = new TextBlock { Text = Loc.T("remote.connect.connect_phone"), FontSize = 12, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var sw = new ToggleSwitch(on, shell: true) { VerticalAlignment = VerticalAlignment.Center };
        sw.Changed += v => { _settings.Current.RemoteEnabled = v; _settings.Save(); _ = _remote.ApplyAsync(); };
        var allow = new TextBlock { Text = Loc.T("remote.connect.allow"), FontSize = 10, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7, Margin = new Thickness(18, 16, 18, 12) };
        head.Children.Add(title);
        Grid.SetColumn(sw, 1); head.Children.Add(sw);
        Grid.SetColumn(allow, 2); head.Children.Add(allow);
        _body.Children.Add(head);

        if (!on)
        {
            _body.Children.Add(Note(OperatingSystem.IsMacOS()
                ? Loc.T("remote.connect.remote_is_off_turn_it_3")
                : OperatingSystem.IsWindows()
                    ? Loc.T("remote.connect.remote_is_off_turn_it_2")
                    : Loc.T("remote.connect.remote_is_off_turn_it"), new Thickness(18, 0, 18, 18)));
            _lastDevices = "";
            return;
        }
        if (_remote.Error is { } err)
        {
            _body.Children.Add(Note(err, new Thickness(18, 0, 18, 18), NotaPalette.DangerBright));
            return;
        }
        if (_remote.Url is null)
        {
            _body.Children.Add(Note(Loc.T("remote.connect.this_computer_isnt_on_a"), new Thickness(18, 0, 18, 18)));
            return;
        }

        _qr = new QrView { Width = 150, Height = 150, Text = _remote.QrText };
        var info = new StackPanel { Spacing = 10 };
        info.Children.Add(Field(Loc.T("remote.connect.address"), AddressRows()));
        _code = new TextBlock { FontSize = 23, FontWeight = FontWeight.Medium, LetterSpacing = 4.8, FontFamily = NotaFonts.UiFamily, Foreground = NotaPalette.TextHeading, VerticalAlignment = VerticalAlignment.Center };
        _ring = new Arc { Width = 20, Height = 20, StrokeThickness = 2.5, Stroke = NotaPalette.VizInk, StartAngle = -90, VerticalAlignment = VerticalAlignment.Center };
        var ringBack = new Ellipse { Width = 20, Height = 20, StrokeThickness = 2.5, Stroke = NotaPalette.TrackOff };
        _left = new TextBlock { FontSize = 9, FontFamily = NotaFonts.UiFamily, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _code, new Panel { Children = { ringBack, _ring } }, _left } };
        info.Children.Add(Field(Loc.T("remote.connect.code"), codeRow));
        bool usb = _remote.Link is { Usb: true };
        info.Children.Add(Note(usb
            ? Loc.T("remote.connect.point_the_phone_camera_at")
            : Loc.T("remote.connect.point_the_phone_camera_at_2"),
            new Thickness(0)));
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, Margin = new Thickness(18, 0, 18, 16) };
        top.Children.Add(_qr);
        Grid.SetColumn(info, 1); top.Children.Add(info);
        _body.Children.Add(top);

        var devices = _remote.Hub.Devices;
        _lastDevices = string.Join("|", devices.Select(d => $"{d.ConnectionId}:{d.Name}:{d.TrackId}")) + ";" + LinksSig();
        if (devices.Count > 0)
        {
            var list = new StackPanel();
            for (int i = 0; i < devices.Count; i++) list.Children.Add(DeviceRow(devices[i], i > 0));
            _body.Children.Add(new Border { BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(0, 1, 0, 0), Child = list });
        }
        Tick();
    }

    private Control DeviceRow(RemoteDeviceInfo d, bool rule)
    {
        bool weak = d.RttMs > 50;
        var c = weak ? NotaPalette.Warning : NotaPalette.SuccessDim;
        int bars = d.RttMs < 0 ? 1 : d.RttMs <= 20 ? 3 : d.RttMs <= 50 ? 2 : 1;
        var sig = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Height = 12 };
        for (int b = 0; b < 3; b++)
            sig.Children.Add(new Border { Width = 3, Height = 4 + b * 4, CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Bottom, Background = b < bars ? c : NotaPalette.BorderStrong });

        string screen = d.Screen.Length > 0 ? d.Screen : Loc.T("remote.connect.connected");
        string sub = d.RttMs < 0 ? Loc.T("remote.connect.connecting") : weak ? Loc.F("remote.connect.ms_weak_network", d.RttMs)
            : d.Usb ? Loc.F("remote.connect.usb_ms", d.RttMs, screen)
            : Loc.F("remote.connect.ms", d.RttMs, screen);
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = d.Name, FontSize = 11, FontWeight = FontWeight.Medium, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis });
        names.Children.Add(new TextBlock { Text = sub, FontSize = 9, FontFamily = NotaFonts.UiFamily, Foreground = weak ? NotaPalette.Warning : NotaPalette.TextTertiary });

        var trackBar = new Border { Width = 4, Height = 18, CornerRadius = NotaRadius.Field, VerticalAlignment = VerticalAlignment.Center, Background = d.TrackId > 0 ? TrackColorBrush(d.TrackId) : NotaPalette.BorderStrong };
        var trackName = new TextBlock { Text = d.TrackId > 0 ? _engine.GetTrackName(d.TrackId) : "—", FontSize = 10, Foreground = NotaPalette.TextSecondary, Width = 70, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        var x = new Border
        {
            Width = 26, Height = 26, CornerRadius = NotaRadius.Group, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Glyph(GlyphKind.Close, 9) { Foreground = NotaPalette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(x, Loc.T("remote.connect.disconnect_this_phone_it_stays"));
        x.PointerEntered += (_, _) => x.Background = NotaPalette.SurfaceHover;
        x.PointerExited += (_, _) => x.Background = Brushes.Transparent;
        int conn = d.ConnectionId;
        x.PointerPressed += (_, e) => { if (e.GetCurrentPoint(x).Properties.IsLeftButtonPressed) { _remote.Hub.Disconnect(conn); e.Handled = true; } };

        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto"), ColumnSpacing = 10, Height = 46, Margin = new Thickness(18, 0) };
        g.Children.Add(sig);
        Grid.SetColumn(names, 1); g.Children.Add(names);
        Grid.SetColumn(trackBar, 2); g.Children.Add(trackBar);
        Grid.SetColumn(trackName, 3); g.Children.Add(trackName);
        Grid.SetColumn(x, 4); g.Children.Add(x);
        return new Border { BorderBrush = NotaPalette.BorderDefault, BorderThickness = new Thickness(0, rule ? 1 : 0, 0, 0), Child = g };
    }

    private IBrush TrackColorBrush(int trackId) => ArrangementView.TrackBrush(ArrangementView.EffectiveColorIndex(_engine, trackId));

    // The code and its countdown move every second; the QR follows the code.
    private void Tick()
    {
        if (_code is null || _left is null || _ring is null || _qr is null) return;
        _code.Text = _remote.Pairing.Code;
        _qr.Text = _remote.QrText;
        var left = _remote.Pairing.CodeRemaining;
        _left.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00}";
        _ring.SweepAngle = 360 * left.TotalSeconds / RemotePairing.CodeLifetime.TotalSeconds;
    }

    // The addresses and the interface chosen for the QR: a change to either rebuilds.
    private string LinksSig() => string.Join("|", _remote.Links.Select(l => l.Address)) + ";" + _settings.Current.RemoteInterface;

    // The address in the QR — a "USB" pill on the cable link, then open in the browser and copy —
    // and the computer's other addresses dimmer under it: a click puts one in the QR (#85), and
    // "Automatic" goes back to the best one once an address has been chosen.
    private Control AddressRows()
    {
        var links = _remote.Links;
        var chosen = _remote.Link;
        var url = _remote.Url!;
        var first = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        first.Children.Add(new TextBlock { Text = url.Replace("http://", ""), FontSize = 12, FontFamily = NotaFonts.UiFamily, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        if (chosen is { Usb: true })
            first.Children.Add(new Border
            {
                Height = 16, Padding = new Thickness(5, 0), CornerRadius = NotaRadius.Fine, VerticalAlignment = VerticalAlignment.Center,
                Background = NotaPalette.SurfaceRaised, BorderBrush = NotaPalette.Seam, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = Loc.T("remote.connect.usb_caps"), FontSize = 8.5, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center },
            });
        first.Children.Add(new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                LinkKey(GlyphKind.PopOut, Loc.T("remote.connect.open_in_the_browser"), _ => Host?.Launcher.LaunchUriAsync(new Uri(url))),
                LinkKey(GlyphKind.Copy, Loc.T("remote.connect.copy_the_link"), g =>
                {
                    if (Host?.Clipboard is not { } cb) return;
                    _ = cb.SetTextAsync(url);
                    g.Kind = GlyphKind.Check;   // copied: a tick for a moment
                    DispatcherTimer.RunOnce(() => g.Kind = GlyphKind.Copy, TimeSpan.FromSeconds(1.5));
                }),
            },
        });
        var rows = new StackPanel { Spacing = 3, Children = { first } };
        foreach (var l in links)
        {
            if (chosen is { } c && l.Address == c.Address) continue;
            string iface = l.Interface;
            rows.Children.Add(OtherRow($"{l.Name}  {l.Address}:{_settings.Current.RemotePort}", Loc.T("remote.connect.show_this_address_in_the"), () => _remote.ChooseInterface(iface)));
        }
        if (_settings.Current.RemoteInterface is not null)
            rows.Children.Add(OtherRow(Loc.T("remote.connect.automatic"), Loc.T("remote.connect.show_the_best_address_usb"), () => _remote.ChooseInterface(null)));
        return rows;
    }

    // The window the popup opens from: it reaches the browser and the clipboard, which a popup's
    // own root may not.
    private TopLevel? Host => (Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow
        ?? TopLevel.GetTopLevel(this);

    // A dim address row that takes a click.
    private static Control OtherRow(string text, string tip, Action click)
    {
        var t = new TextBlock
        {
            Text = text, FontSize = 9, FontFamily = NotaFonts.UiFamily, Foreground = NotaPalette.TextTertiary, Margin = new Thickness(0, 1, 0, 0),
            Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand), HorizontalAlignment = HorizontalAlignment.Left,
        };
        ToolTip.SetTip(t, tip);
        t.PointerEntered += (_, _) => t.Foreground = NotaPalette.TextPrimary;
        t.PointerExited += (_, _) => t.Foreground = NotaPalette.TextTertiary;
        t.PointerPressed += (_, e) => { if (e.GetCurrentPoint(t).Properties.IsLeftButtonPressed) { e.Handled = true; click(); } };
        return t;
    }

    // A small glyph key beside the address; the click gets the glyph, to show what it did.
    private static Control LinkKey(GlyphKind glyph, string tip, Action<Glyph> click)
    {
        var g = new Glyph(glyph, 10) { Foreground = NotaPalette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var k = new Border
        {
            Width = 20, Height = 20, CornerRadius = NotaRadius.Group, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = g,
        };
        ToolTip.SetTip(k, tip);
        k.PointerEntered += (_, _) => k.Background = NotaPalette.SurfaceHover;
        k.PointerExited += (_, _) => k.Background = Brushes.Transparent;
        k.PointerPressed += (_, e) => { if (e.GetCurrentPoint(k).Properties.IsLeftButtonPressed) { e.Handled = true; click(g); } };
        return k;
    }

    private static Control Field(string label, Control value)
    {
        var s = new StackPanel { Spacing = 3 };
        s.Children.Add(new TextBlock { Text = NotaText.Label(label), FontSize = 10, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextMuted });
        s.Children.Add(value);
        return s;
    }

    private static TextBlock Note(string text, Thickness margin, IBrush? ink = null) => new()
    {
        Text = text, FontSize = 10, LineHeight = 16.5, TextWrapping = TextWrapping.Wrap,
        Foreground = ink ?? NotaPalette.TextMuted, Margin = margin,
    };
}
