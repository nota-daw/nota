// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Connect Phone (design 5a): the popup under the top bar's Remote button and View → Connect
// Phone…. One switch when Remote is off; when on, a large QR, the address and the four-digit
// code with its countdown ring, then the connected phones (signal, name, latency and screen,
// the track each plays, ✕ to drop it).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Remote;

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
        CornerRadius = NotaRadius.Body;
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
        string sig = string.Join("|", _remote.Hub.Devices.Select(d => $"{d.ConnectionId}:{d.Name}:{d.TrackId}"))
            + ";" + string.Join("|", _remote.Links.Select(l => l.Address));
        if (sig != _lastDevices || _qr is null != !_remote.Running) Build();
        else Tick();
    });

    private void Build()
    {
        _body.Children.Clear();
        _qr = null; _code = _left = null; _ring = null;
        bool on = _settings.Current.RemoteEnabled;

        var title = new TextBlock { Text = "Connect Phone", FontSize = 13, FontWeight = FontWeight.SemiBold, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center };
        var sw = new ToggleSwitch(on) { VerticalAlignment = VerticalAlignment.Center };
        sw.Changed += v => { _settings.Current.RemoteEnabled = v; _settings.Save(); _ = _remote.ApplyAsync(); };
        var allow = new TextBlock { Text = "Allow", FontSize = 11, Foreground = NotaPalette.TextSecondary, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 7, Margin = new Thickness(18, 16, 18, 12) };
        head.Children.Add(title);
        Grid.SetColumn(sw, 1); head.Children.Add(sw);
        Grid.SetColumn(allow, 2); head.Children.Add(allow);
        _body.Children.Add(head);

        if (!on)
        {
            _body.Children.Add(Note(OperatingSystem.IsMacOS()
                ? "Remote is off. Turn it on to show a QR code. The first time, macOS asks to allow Nota on the local network; click Allow."
                : OperatingSystem.IsWindows()
                    ? "Remote is off. Turn it on to show a QR code. The first time, Windows asks whether Nota may use the network; allow private networks."
                    : "Remote is off. Turn it on to show a QR code a phone on the same Wi-Fi can scan.", new Thickness(18, 0, 18, 18)));
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
            _body.Children.Add(Note("This computer isn't on a network. Join the same Wi-Fi as the phone, share the phone's hotspot with it, or plug in a USB cable (USB tethering on the phone; Internet Sharing on the Mac).", new Thickness(18, 0, 18, 18)));
            return;
        }

        _qr = new QrView { Width = 150, Height = 150, Text = _remote.QrText };
        var info = new StackPanel { Spacing = 10 };
        info.Children.Add(Field("ADDRESS", AddressRows()));
        _code = new TextBlock { FontSize = 24, FontWeight = FontWeight.Medium, LetterSpacing = 4.8, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextHeading, VerticalAlignment = VerticalAlignment.Center };
        _ring = new Arc { Width = 20, Height = 20, StrokeThickness = 2.5, Stroke = NotaPalette.AccentDim, StartAngle = -90, VerticalAlignment = VerticalAlignment.Center };
        var ringBack = new Ellipse { Width = 20, Height = 20, StrokeThickness = 2.5, Stroke = NotaPalette.TrackOff };
        _left = new TextBlock { FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center };
        var codeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { _code, new Panel { Children = { ringBack, _ring } }, _left } };
        info.Children.Add(Field("CODE", codeRow));
        bool usb = _remote.Links is { Count: > 0 } links && links[0].Usb;
        info.Children.Add(Note(usb
            ? "Point the phone camera at the code. Over the cable the link is instant and needs no Wi-Fi; the code changes every 2 minutes and after each connection."
            : "Point the phone camera at the code. Same Wi-Fi only — or plug in a USB cable (USB tethering on the phone; Internet Sharing on the Mac) for an instant link. The code changes every 2 minutes and after each connection.",
            new Thickness(0)));
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 16, Margin = new Thickness(18, 0, 18, 16) };
        top.Children.Add(_qr);
        Grid.SetColumn(info, 1); top.Children.Add(info);
        _body.Children.Add(top);

        var devices = _remote.Hub.Devices;
        _lastDevices = string.Join("|", devices.Select(d => $"{d.ConnectionId}:{d.Name}:{d.TrackId}"))
            + ";" + string.Join("|", _remote.Links.Select(l => l.Address));
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
            sig.Children.Add(new Border { Width = 3, Height = 4 + b * 4, CornerRadius = NotaRadius.Bar, VerticalAlignment = VerticalAlignment.Bottom, Background = b < bars ? c : NotaPalette.BorderStrong });

        string sub = d.RttMs < 0 ? "connecting" : weak ? $"{d.RttMs} ms · weak network"
            : d.Usb ? $"USB · {d.RttMs} ms · {(d.Screen.Length > 0 ? d.Screen : "connected")}"
            : $"{d.RttMs} ms · {(d.Screen.Length > 0 ? d.Screen : "connected")}";
        var names = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        names.Children.Add(new TextBlock { Text = d.Name, FontSize = 12, FontWeight = FontWeight.Medium, Foreground = NotaPalette.TextPrimary, TextTrimming = TextTrimming.CharacterEllipsis });
        names.Children.Add(new TextBlock { Text = sub, FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = weak ? NotaPalette.Warning : NotaPalette.TextTertiary });

        var trackBar = new Border { Width = 4, Height = 18, CornerRadius = NotaRadius.Clip, VerticalAlignment = VerticalAlignment.Center, Background = d.TrackId > 0 ? TrackColorBrush(d.TrackId) : NotaPalette.BorderStrong };
        var trackName = new TextBlock { Text = d.TrackId > 0 ? _engine.GetTrackName(d.TrackId) : "—", FontSize = 11, Foreground = NotaPalette.TextSecondary, Width = 70, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        var x = new Border
        {
            Width = 26, Height = 26, CornerRadius = NotaRadius.Tile, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Child = new Glyph(GlyphKind.Close, 9) { Foreground = NotaPalette.TextMuted, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        ToolTip.SetTip(x, "Disconnect this phone (it stays trusted — forget it in Settings → Remote)");
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

    // The addresses, best first: a "USB" pill on the cable link, the rest dimmer under it.
    private Control AddressRows()
    {
        var links = _remote.Links;
        var host = _remote.Url!.Replace("http://", "");
        var first = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        first.Children.Add(new TextBlock { Text = host, FontSize = 13, FontFamily = NotaFonts.MonoFamily, Foreground = NotaPalette.TextPrimary, VerticalAlignment = VerticalAlignment.Center });
        if (links.Count > 0 && links[0].Usb)
            first.Children.Add(new Border
            {
                Height = 16, Padding = new Thickness(5, 0), CornerRadius = NotaRadius.Bar, VerticalAlignment = VerticalAlignment.Center,
                Background = NotaPalette.TrackOff, BorderBrush = NotaPalette.BorderBrass, BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "USB", FontSize = 8, FontWeight = FontWeight.Bold, LetterSpacing = 1, Foreground = NotaPalette.AccentBright, VerticalAlignment = VerticalAlignment.Center },
            });
        var rows = new StackPanel { Spacing = links.Count > 1 ? 3 : 0, Children = { first } };
        for (int i = 1; i < links.Count; i++)
            rows.Children.Add(new TextBlock
            {
                Text = $"{links[i].Name}  {links[i].Address}:{_settings.Current.RemotePort}", FontSize = 10, FontFamily = NotaFonts.MonoFamily,
                Foreground = NotaPalette.TextTertiary, Margin = new Thickness(0, 1, 0, 0),
            });
        return rows;
    }

    private static Control Field(string label, Control value)
    {
        var s = new StackPanel { Spacing = 3 };
        s.Children.Add(new TextBlock { Text = label, FontSize = 9, FontWeight = FontWeight.Bold, LetterSpacing = 1.1, Foreground = NotaPalette.TextTertiary });
        s.Children.Add(value);
        return s;
    }

    private static TextBlock Note(string text, Thickness margin, IBrush? ink = null) => new()
    {
        Text = text, FontSize = 11, LineHeight = 16.5, TextWrapping = TextWrapping.Wrap,
        Foreground = ink ?? NotaPalette.TextMuted, Margin = margin,
    };
}
