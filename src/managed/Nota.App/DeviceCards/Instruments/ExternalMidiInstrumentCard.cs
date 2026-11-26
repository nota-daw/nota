// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — External MIDI (specs/115): the instrument that plays a synth over a MIDI
// output. Its card picks the output and the channel and says whether the notes get there. A port
// that isn't connected stays chosen under the name it had (FR-006): nothing stands in for it.
// Every pick makes a new instance in the engine (one undo step), so the card rebuilds after it.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Nota.Application;
using Nota.Application.Localization;
using static Nota.App.DeviceCardKit;

namespace Nota.App;

internal sealed class ExternalMidiInstrumentCard : IInstrumentCard
{
    public bool BodyOnly => true;
    public double CardWidth => 260;
    public string Subtitle => Loc.T("device.external_midi.subtitle");

    public Control Build(DeviceCardContext ctx)
    {
        var engine = ctx.Engine;
        int track = ctx.TrackId;
        var dest = engine.GetExternalMidi(track) ?? new ExternalMidiDestination("", 1);

        var output = TextButton(OutputText(engine, dest));
        output.HorizontalAlignment = HorizontalAlignment.Stretch;
        output.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(output).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            OutputMenu(ctx, dest).ShowAt(output);
        };

        var channel = TextButton(ChannelText(dest.Channel));
        channel.HorizontalAlignment = HorizontalAlignment.Stretch;
        channel.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(channel).Properties.IsLeftButtonPressed) return;
            e.Handled = true;
            ChannelMenu(ctx, dest).ShowAt(channel);
        };

        var status = (TextBlock)Hint(StatusText(engine, track, dest));
        status.TextWrapping = Avalonia.Media.TextWrapping.Wrap;
        string shown = status.Text ?? "";
        ctx.AddDeviceRefresher(() =>
        {
            string now = StatusText(engine, track, dest);
            if (now == shown) return;
            shown = now;
            status.Text = now;
        });

        var body = new StackPanel
        {
            Spacing = 10, VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                Labeled(Loc.T("device.external_midi.output"), output),
                Labeled(Loc.T("device.external_midi.channel"), channel),
                status,
            },
        };
        return new Border { Padding = new Thickness(14, 10), Child = Section(body) };
    }

    // ---- text ------------------------------------------------------------------------------

    private static string OutputText(IAudioEngine engine, ExternalMidiDestination d)
    {
        // A role shows itself and the port it is bound to: "Stage Synth → Port X" (FR-014).
        if (d.RoleId.Length > 0 && engine.MidiRoles().FirstOrDefault(r => r.Id == d.RoleId) is { } role)
            return role.OutputUid.Length == 0 ? Loc.F("device.external_midi.role_unbound", role.Name)
                : Loc.F("device.external_midi.role_port", role.Name, PortName(engine, role.OutputUid, role.OutputName));
        if (d.OutputUid.Length == 0) return Loc.T("device.external_midi.no_output");
        var connected = engine.MidiOutputs().FirstOrDefault(o => o.Uid == d.OutputUid);
        return connected.Uid is not null ? connected.Name : (d.OutputName.Length > 0 ? d.OutputName : d.OutputUid);
    }

    internal static string ChannelText(int channel) =>
        channel == ExternalMidiDestination.KeepChannels ? Loc.T("device.external_midi.keep_channels") : Loc.F("device.external_midi.channel_n", channel);

    private static string StatusText(IAudioEngine engine, int track, ExternalMidiDestination d)
    {
        int s = engine.ExternalMidiStatus(track);
        string text = s switch
        {
            1 => Loc.T("device.external_midi.sending"),
            2 => Loc.T("device.external_midi.not_connected"),
            _ => Loc.T("device.external_midi.choose_output"),
        };
        uint dropped = engine.ExternalMidiDropped(track);
        return dropped > 0 ? text + " · " + Loc.F("device.external_midi.dropped", dropped) : text;
    }

    // ---- menus -----------------------------------------------------------------------------

    private static string PortName(IAudioEngine engine, string uid, string saved)
    {
        var o = engine.MidiOutputs().FirstOrDefault(x => x.Uid == uid);
        return o.Uid is not null ? o.Name : saved.Length > 0 ? saved : uid;
    }

    private static MenuFlyout OutputMenu(DeviceCardContext ctx, ExternalMidiDestination d)
    {
        var f = new MenuFlyout();
        // Roles first (specs/115 US4): picking one makes the track follow it.
        foreach (var r in ctx.Engine.MidiRoles())
        {
            string id = r.Id;
            string text = r.OutputUid.Length == 0 ? Loc.F("device.external_midi.role_unbound", r.Name)
                : Loc.F("device.external_midi.role_port", r.Name, PortName(ctx.Engine, r.OutputUid, r.OutputName));
            f.Items.Add(MenuKit.Item(text, d.RoleId == id ? GlyphKind.Check : null, () =>
            {
                Nota.Application.Midi.MidiOutputRoles.Assign(ctx.Engine, ctx.TrackId, id);
                ctx.NotifyChanged();
                ctx.RequestRebuild();
            }));
        }
        f.Items.Add(MenuKit.Item(Loc.T("device.external_midi.manage_roles"), null, () =>
        {
            var owner = (Avalonia.Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)?.MainWindow;
            var win = new MidiRolesWindow(ctx.Engine, () => { ctx.NotifyChanged(); });
            win.Closed += (_, _) => ctx.RequestRebuild();
            if (owner is not null) _ = win.ShowDialog(owner);
            else win.Show();
        }));
        f.Items.Add(new Separator());
        var outputs = ctx.Engine.MidiOutputs();
        // The saved port first when it isn't connected: still chosen, shown by its saved name.
        if (d.RoleId.Length == 0 && d.OutputUid.Length > 0 && !outputs.Any(o => o.Uid == d.OutputUid))
            f.Items.Add(MenuKit.Item(Loc.F("device.external_midi.missing_output", d.OutputName.Length > 0 ? d.OutputName : d.OutputUid),
                GlyphKind.Check, null, null, enabled: false));
        foreach (var o in outputs)
        {
            var mi = MenuKit.Item(o.Name, d.RoleId.Length == 0 && o.Uid == d.OutputUid ? GlyphKind.Check : null,
                () => Set(ctx, o.Uid, o.Name, d.Channel));
            f.Items.Add(mi);
        }
        if (outputs.Count == 0)
            f.Items.Add(MenuKit.Item(Loc.T("device.external_midi.no_outputs_connected"), null, null, null, enabled: false));
        f.Items.Add(new Separator());
        f.Items.Add(MenuKit.Item(Loc.T("device.external_midi.none"), d.OutputUid.Length == 0 ? GlyphKind.Check : null,
            () => Set(ctx, "", "", d.Channel)));
        return f;
    }

    private static MenuFlyout ChannelMenu(DeviceCardContext ctx, ExternalMidiDestination d)
    {
        var f = new MenuFlyout();
        for (int c = 1; c <= 16; c++)
        {
            int ch = c;
            f.Items.Add(MenuKit.Item(ChannelText(ch), d.Channel == ch ? GlyphKind.Check : null,
                () => Set(ctx, d.OutputUid, d.OutputName, ch)));
        }
        f.Items.Add(new Separator());
        f.Items.Add(MenuKit.Item(ChannelText(ExternalMidiDestination.KeepChannels),
            d.Channel == ExternalMidiDestination.KeepChannels ? GlyphKind.Check : null,
            () => Set(ctx, d.OutputUid, d.OutputName, ExternalMidiDestination.KeepChannels)));
        return f;
    }

    // A port picked by hand leaves any role it followed (specs/115 US4: a role is picked as a role).
    private static void Set(DeviceCardContext ctx, string uid, string name, int channel)
    {
        var cur = ctx.Engine.GetExternalMidi(ctx.TrackId);
        if (cur is { } c && c.OutputUid == uid && c.Channel == channel && c.RoleId.Length == 0) return;
        // Changing only the channel keeps a role; picking a port leaves it.
        bool keepRole = cur is { RoleId.Length: > 0 } r && r.OutputUid == uid && r.Channel != channel;
        ctx.Engine.SetExternalMidi(ctx.TrackId, uid, channel, name, keepRole ? cur!.Value.RoleId : "");
        ctx.NotifyChanged();
        ctx.RequestRebuild();
    }
}
