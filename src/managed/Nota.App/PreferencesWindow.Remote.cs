// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Remote (design 5b): the switch, the port, what phones may do, and the trusted
// phones — a trusted phone reconnects from its Home Screen icon without a code until it is
// forgotten here. The note at the end warns about the system's local-network / firewall prompt
// before it appears.

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
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Remote;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private const int RemoteIndex = 3;

    /// <summary>Open Settings on the Remote page.</summary>
    public void ShowRemote() => Select(RemoteIndex);

    private Control RemotePane()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var remote = App.Services.GetRequiredService<RemoteService>();

        var portBox = new TextBox
        {
            Text = settings.Current.RemotePort.ToString(), Width = 90, Classes = { "field" },
            FontSize = 12, FontFamily = NotaFonts.MonoFamily, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var statusDot = Dot(BorderStrong);
        var status = new TextBlock { FontSize = 11, FontFamily = NotaFonts.MonoFamily, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

        // Phones may: play notes only, or play and control the project.
        var access = new StackPanel { Spacing = 6 };
        var radios = new List<(Ellipse ring, Ellipse dot)>();
        void PaintAccess()
        {
            for (int i = 0; i < radios.Count; i++)
            {
                bool on = settings.Current.RemoteAccess == i;
                radios[i].ring.Stroke = on ? Brass : BorderStrong;
                radios[i].dot.Fill = on ? Brass : Brushes.Transparent;
            }
        }
        foreach (var (i, name, sub) in new[]
                 {
                     (0, "Play notes only", "Pads and keys. Mixer, transport and mappings are read-only."),
                     (1, "Play and control the project", "Also mixer, transport, macros, XY and Session."),
                 })
        {
            var ring = new Ellipse { Width = 12, Height = 12, StrokeThickness = 1.5 };
            var dot = new Ellipse { Width = 6, Height = 6 };
            radios.Add((ring, dot));
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = name, FontSize = 12, Foreground = TextPrimary });
            text.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = TextMuted, TextWrapping = TextWrapping.Wrap });
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 9, Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Margin = new Thickness(0, 4),
                Children = { new Panel { VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 0, 0), Children = { ring, dot } }, text },
            };
            int idx = i;
            row.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return;
                settings.Current.RemoteAccess = idx;
                settings.Save();
                PaintAccess();
            };
            access.Children.Add(row);
        }
        PaintAccess();

        var trustedHost = new ContentControl();
        void RenderTrusted()
        {
            var connected = remote.Hub.Devices.Select(d => d.DeviceId).ToHashSet();
            var list = remote.Pairing.Trusted.OrderByDescending(d => d.LastSeen).ToList();
            if (list.Count == 0) { trustedHost.Content = EmptyBox("No phone has paired yet. Turn Remote on and scan the code in Connect Phone."); return; }
            var rows = new List<Control>();
            foreach (var d in list)
            {
                bool live = connected.Contains(d.Id) && remote.Running;
                var name = new TextBlock { Text = d.Name, FontSize = 12, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var seen = new TextBlock { Text = live ? "now" : Ago(d.LastSeen), FontSize = 10, FontFamily = NotaFonts.MonoFamily, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
                var forget = Btn("Forget");
                forget.Height = 24; forget.FontSize = 11; forget.Padding = new Thickness(10, 0);
                ToolTip.SetTip(forget, "Disconnect it and make it scan a new code to come back");
                string id = d.Id;
                forget.Click += (_, _) => { remote.Hub.Forget(id); RenderTrusted(); };
                rows.Add(ListRow(38, "Auto,*,Auto,Auto", Dot(live ? NotaPalette.SuccessDim : BorderStrong), name, seen, forget));
            }
            trustedHost.Content = Table(rows);
        }

        bool Enabled() => settings.Current.RemoteEnabled;
        var details = new StackPanel { Spacing = 10 };
        void Refresh()
        {
            bool on = Enabled();
            status.Text = !on ? "Off" : remote.Error ?? (remote.Url is { } u ? $"Listening on {u.Replace("http://", "")}" : "On · no network");
            statusDot.Fill = !on ? BorderStrong : remote.Error is null ? NotaPalette.SuccessDim : NotaPalette.Danger;
            Inactive.Set(details, !on);
            RenderTrusted();
        }
        void Apply(bool on)
        {
            settings.Current.RemoteEnabled = on;
            if (int.TryParse(portBox.Text, out var p) && p is >= 1024 and < 65536) settings.Current.RemotePort = p;
            else portBox.Text = settings.Current.RemotePort.ToString();
            settings.Save();
            _ = remote.ApplyAsync();
            Refresh();
        }
        var enable = SwitchRow("Allow control from a phone", Enabled(), Apply);
        portBox.LostFocus += (_, _) => Apply(Enabled());
        void OnChanged() => Dispatcher.UIThread.Post(Refresh);
        remote.Changed += OnChanged;
        Closed += (_, _) => remote.Changed -= OnChanged;
        DetachedFromVisualTree += (_, _) => remote.Changed -= OnChanged;

        details.Children.Add(Row("Port", portBox));
        details.Children.Add(Row("Status", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, status } }));
        var accessRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*") };
        var accessLabel = RowLabel("Phones may");
        accessLabel.VerticalAlignment = VerticalAlignment.Top;
        accessLabel.Margin = new Thickness(0, 6, 0, 0);
        accessRow.Children.Add(accessLabel);
        Grid.SetColumn(access, 1);
        accessRow.Children.Add(access);
        details.Children.Add(accessRow);
        Refresh();

        string sysNote = OperatingSystem.IsMacOS()
            ? "When you turn this on, macOS asks whether Nota may find devices on the local network. Allow it, or phones can't reach Nota."
            : OperatingSystem.IsWindows()
                ? "When you turn this on, Windows asks whether Nota may use the network. Allow private networks, or phones can't reach Nota."
                : "Phones reach Nota on the port above: if a firewall is on, allow it.";
        var warn = new Border
        {
            Padding = new Thickness(14, 12), CornerRadius = NotaRadius.Panel, Background = RowBg, BorderBrush = Divider, BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Warning, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) },
                    new TextBlock { Text = sysNote + " Nota never shows files or paths to the phone, and serves nothing from your disk.", FontSize = 11, LineHeight = 17, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                },
            },
        };

        return Sections(
            Section("NOTA REMOTE", 10, enable, details),
            Section("TRUSTED DEVICES", 8, trustedHost,
                Caption("A trusted phone reconnects from its Home Screen icon without a code. Forget it and it has to scan again. Turning Remote off disconnects everyone.", muted: true)),
            warn);
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 2) return "just now";
        if (d.TotalHours < 1) return $"{(int)d.TotalMinutes} min ago";
        if (d.TotalDays < 1) return $"{(int)d.TotalHours} h ago";
        if (d.TotalDays < 2) return "yesterday";
        return $"{(int)d.TotalDays} days ago";
    }
}
