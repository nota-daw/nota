// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Remote (design 5b): the switch, the port, the address the link and QR show (#85),
// what phones may do, and the trusted phones — a trusted phone reconnects from its Home Screen icon without a code until it is
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
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    /// <summary>Open Settings on the Remote page.</summary>
    public void ShowRemote() => Select("Remote");

    private Control RemotePane()
    {
        var settings = App.Services.GetRequiredService<ISettingsService>();
        var remote = App.Services.GetRequiredService<RemoteService>();

        var portBox = new TextBox
        {
            Text = settings.Current.RemotePort.ToString(), Width = 90, Classes = { "field" },
            FontSize = 11, FontFamily = NotaFonts.UiFamily, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var statusDot = Dot(BorderStrong);
        var status = new TextBlock { FontSize = 10, FontFamily = NotaFonts.UiFamily, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };

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
                     (0, Loc.T("preferences.remote.play_notes_only"), Loc.T("preferences.remote.pads_and_keys_mixer_transport")),
                     (1, Loc.T("preferences.remote.play_and_control_the_project"), Loc.T("preferences.remote.also_mixer_transport_macros_xy")),
                 })
        {
            var ring = new Ellipse { Width = 12, Height = 12, StrokeThickness = 1.5 };
            var dot = new Ellipse { Width = 6, Height = 6 };
            radios.Add((ring, dot));
            var text = new StackPanel { Spacing = 2 };
            text.Children.Add(new TextBlock { Text = name, FontSize = 11, Foreground = TextPrimary });
            text.Children.Add(new TextBlock { Text = sub, FontSize = 10, Foreground = TextMuted, TextWrapping = TextWrapping.Wrap });
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
            if (list.Count == 0) { trustedHost.Content = EmptyBox(Loc.T("preferences.remote.no_phone_has_paired_yet")); return; }
            var rows = new List<Control>();
            foreach (var d in list)
            {
                bool live = connected.Contains(d.Id) && remote.Running;
                var name = new TextBlock { Text = d.Name, FontSize = 11, Foreground = TextPrimary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
                var seen = new TextBlock { Text = live ? Loc.T("preferences.remote.now") : Ago(d.LastSeen), FontSize = 9, FontFamily = NotaFonts.UiFamily, Foreground = TextTertiary, VerticalAlignment = VerticalAlignment.Center };
                var forget = Btn(Loc.T("preferences.remote.forget"));
                forget.Height = 24; forget.FontSize = 10; forget.Padding = new Thickness(10, 0);
                ToolTip.SetTip(forget, Loc.T("preferences.remote.disconnect_it_and_make_it"));
                string id = d.Id;
                forget.Click += (_, _) => { remote.Hub.Forget(id); RenderTrusted(); };
                rows.Add(ListRow(38, "Auto,*,Auto,Auto", Dot(live ? NotaPalette.SuccessDim : BorderStrong), name, seen, forget));
            }
            trustedHost.Content = Table(rows);
        }

        // The address the Connect phone link and QR show (#85): Automatic, or one interface by its
        // name (its first address). A chosen interface that isn't up stays listed as not connected
        // (Automatic stands in until it is back). Rebuilt only when the addresses or the choice
        // change, so an open list isn't closed by the code rotating.
        var addressHost = new ContentControl();
        string addressSig = "";
        void RenderAddress()
        {
            var links = remote.Links;
            string? chosen = settings.Current.RemoteInterface;
            string sig = string.Join("|", links.Select(l => l.Interface + "=" + l.Address)) + ";" + chosen;
            if (sig == addressSig) return;
            addressSig = sig;
            var combo = Combo(280);
            var values = new List<string?> { null };
            combo.Items.Add(Loc.T("preferences.remote.automatic_usb_then_wi_fi"));
            foreach (var l in links)
            {
                if (values.Contains(l.Interface)) continue;
                values.Add(l.Interface);
                combo.Items.Add(Loc.F("preferences.remote.link_on_interface", l.Name, l.Address, l.Interface));
            }
            if (chosen is not null && !values.Contains(chosen))
            {
                values.Add(chosen);
                combo.Items.Add(new ComboBoxItem { Content = Loc.F("preferences.remote.interface_not_connected", chosen), IsEnabled = false });
            }
            combo.SelectedIndex = Math.Max(0, values.IndexOf(chosen));
            combo.SelectionChanged += (_, _) =>
            {
                int i = combo.SelectedIndex;
                if (i < 0 || i >= values.Count || values[i] == settings.Current.RemoteInterface) return;
                remote.ChooseInterface(values[i]);
            };
            ToolTip.SetTip(combo, Loc.T("preferences.remote.the_address_the_connect_phone"));
            addressHost.Content = combo;
        }

        bool Enabled() => settings.Current.RemoteEnabled;
        var details = new StackPanel { Spacing = 10 };
        void Refresh()
        {
            bool on = Enabled();
            string listen = remote.Links.Count == 0 ? Loc.T("preferences.remote.on_no_network")
                : Loc.F("preferences.remote.listening_on", string.Join("  ·  ", remote.Links.Select(l => (l.Usb ? Loc.T("preferences.remote.usb_caps") : "") + l.Address + ":" + settings.Current.RemotePort)));
            status.Text = !on ? Loc.T("common.off") : remote.Error ?? listen;
            statusDot.Fill = !on ? BorderStrong : remote.Error is null ? NotaPalette.SuccessDim : NotaPalette.Danger;
            RenderAddress();
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
        var enable = SwitchRow(Loc.T("preferences.remote.allow_control_from_a_phone"), Enabled(), Apply);
        portBox.LostFocus += (_, _) => Apply(Enabled());
        void OnChanged() => Dispatcher.UIThread.Post(Refresh);
        remote.Changed += OnChanged;
        Closed += (_, _) => remote.Changed -= OnChanged;
        DetachedFromVisualTree += (_, _) => remote.Changed -= OnChanged;

        details.Children.Add(Row(Loc.T("preferences.port"), portBox));
        details.Children.Add(Row(Loc.T("preferences.remote.address_in_the_qr"), addressHost));
        details.Children.Add(Row(Loc.T("preferences.status"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, status } }));
        var accessRow = new Grid { ColumnDefinitions = new ColumnDefinitions($"{LabelCol},*") };
        var accessLabel = RowLabel(Loc.T("preferences.remote.phones_may"));
        accessLabel.VerticalAlignment = VerticalAlignment.Top;
        accessLabel.Margin = new Thickness(0, 6, 0, 0);
        accessRow.Children.Add(accessLabel);
        Grid.SetColumn(access, 1);
        accessRow.Children.Add(access);
        details.Children.Add(accessRow);
        Refresh();

        string sysNote = OperatingSystem.IsMacOS()
            ? Loc.T("preferences.remote.when_you_turn_this_on_2")
            : OperatingSystem.IsWindows()
                ? Loc.T("preferences.remote.when_you_turn_this_on")
                : Loc.T("preferences.remote.phones_reach_nota_on_the");
        var warn = new Border
        {
            Padding = new Thickness(14, 12), CornerRadius = NotaRadius.Group, Background = RowBg, BorderBrush = Divider, BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal, Spacing = 10,
                Children =
                {
                    new Ellipse { Width = 6, Height = 6, Fill = NotaPalette.Warning, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 5, 0, 0) },
                    new TextBlock { Text = sysNote + Loc.T("preferences.remote.nota_never_shows_files_or"), FontSize = 10, LineHeight = 17, Foreground = TextSecondary, TextWrapping = TextWrapping.Wrap, MaxWidth = 560 },
                },
            },
        };

        return Sections(
            Section(Loc.T("preferences.remote.nota_remote"), 10, enable, details),
            Section(Loc.T("preferences.remote.trusted_devices"), 8, trustedHost,
                Caption(Loc.T("preferences.remote.a_trusted_phone_reconnects_from"), muted: true)),
            warn);
    }

    private static string Ago(DateTime utc)
    {
        var d = DateTime.UtcNow - utc;
        if (d.TotalMinutes < 2) return Loc.T("preferences.remote.just_now");
        if (d.TotalHours < 1) return Loc.F("preferences.remote.min_ago", (int)d.TotalMinutes);
        if (d.TotalDays < 1) return Loc.F("preferences.remote.h_ago", (int)d.TotalHours);
        if (d.TotalDays < 2) return "yesterday";
        return Loc.F("preferences.remote.days_ago", (int)d.TotalDays);
    }
}
