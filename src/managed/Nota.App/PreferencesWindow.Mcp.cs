// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Settings → Remote control → AI control (MCP) (specs/088): the in-app MCP server's switch,
// port, status and a ready client config. Moved off Appearance as it was (#427).

using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private Control McpPane()
    {
        var settings = App.Services.GetService<ISettingsService>();
        var mcp = App.Services.GetService<McpService>();

        var portBox = new TextBox
        {
            Text = (settings?.Current.McpPort ?? 3900).ToString(), Width = 96, Classes = { "field" },
            FontSize = 11, FontFamily = NotaFonts.ValuesFamily, HorizontalAlignment = HorizontalAlignment.Left,
        };
        var statusDot = Dot(BorderStrong);
        var status = new TextBlock
        {
            FontSize = 10, FontFamily = NotaFonts.ValuesFamily, Foreground = TextSecondary,
            VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var copyBtn = Btn(Loc.T("preferences.copy_config"));
        copyBtn.HorizontalAlignment = HorizontalAlignment.Left;
        var details = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Row(Loc.T("preferences.port"), portBox),
                Row(Loc.T("preferences.status"), new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { statusDot, status } }),
                Row("", new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        copyBtn,
                        Caption(Loc.T("preferences.add_this_config_or_the")),
                    },
                }),
            },
        };

        bool Enabled() => settings?.Current.McpEnabled ?? false;
        void Refresh()
        {
            bool on = Enabled();
            status.Text = on ? Loc.F("preferences.listening_on_http_127_0", settings!.Current.McpPort) : Loc.T("common.off");
            statusDot.Fill = on ? NotaPalette.SuccessDim : BorderStrong;
            portBox.IsEnabled = on;
            portBox.Foreground = on ? TextPrimary : TextDisabled;
            copyBtn.IsEnabled = on;
            Inactive.Set(details, !on);
        }
        void ApplyMcp(bool on)
        {
            if (settings is null) return;
            settings.Current.McpEnabled = on;
            if (int.TryParse(portBox.Text, out var p) && p is > 0 and < 65536) settings.Current.McpPort = p;
            settings.Save();
            _ = mcp?.ApplyAsync();
            Refresh();
        }
        var enable = SwitchRow(Loc.T("preferences.enable_mcp_server_let_an"), Enabled(), ApplyMcp);
        portBox.LostFocus += (_, _) => ApplyMcp(Enabled());

        // Copy a ready client-config JSON (Claude Desktop / Claude Code) with the current port.
        string McpConfigJson()
        {
            int port = settings?.Current.McpPort ?? 3900;
            return "{\n  \"mcpServers\": {\n    \"nota\": {\n      \"type\": \"http\",\n      \"url\": \"http://127.0.0.1:" + port + "/\"\n    }\n  }\n}";
        }
        DispatcherTimer? copied = null;
        copyBtn.Click += async (_, _) =>
        {
            var cb = TopLevel.GetTopLevel(this)?.Clipboard;
            if (cb is null) return;
            await cb.SetTextAsync(McpConfigJson());
            copyBtn.Content = Loc.T("preferences.copied");
            copied?.Stop();
            copied = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            copied.Tick += (_, _) => { copied!.Stop(); copyBtn.Content = Loc.T("preferences.copy_config"); };
            copied.Start();
        };
        // Inactive.Set walks the visual tree, so the dimming waits until the pane is shown.
        details.AttachedToVisualTree += (_, _) => Refresh();

        return Sections(Section(Loc.T("preferences.ai_control_mcp"), 12, enable, details));
    }
}
