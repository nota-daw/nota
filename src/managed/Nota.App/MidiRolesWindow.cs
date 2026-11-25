// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MIDI output roles (specs/115 US4): the project's named destinations, such as "Stage Synth",
// that External MIDI tracks follow. Here a role is added, renamed, bound to an output, rebound
// (with the tracks it will move listed first, and only while the transport is stopped — FR-015)
// or deleted (refused while tracks still follow it). Each edit is one undo step.

using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Nota.Application;
using Nota.Application.Localization;
using Nota.Application.Midi;

namespace Nota.App;

public sealed class MidiRolesWindow : NotaWindow
{
    private readonly IAudioEngine _engine;
    private readonly Action? _edited;
    private readonly StackPanel _rows = new() { Spacing = 8 };
    private readonly TextBlock _status = new() { FontSize = 10, Foreground = NotaPalette.TextTertiary, TextWrapping = Avalonia.Media.TextWrapping.Wrap };

    public MidiRolesWindow(IAudioEngine engine, Action? edited = null)
    {
        _engine = engine;
        _edited = edited;
        Title = Loc.T("midi_roles.title");
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = NotaPalette.BgApp;

        var add = new Button { Content = Loc.T("midi_roles.add"), Classes = { "ghost" } };
        add.Click += (_, _) => Try(() =>
        {
            int n = 1;
            string name;
            do name = Loc.F("midi_roles.new_role_n", n++); while (MidiOutputRoles.CheckName(_engine, name) is not null);
            MidiOutputRoles.Create(_engine, name);
        });
        var close = new Button { Content = Loc.T("common.close"), Classes = { "primary" } };
        close.Click += (_, _) => Close();

        SetBody(new StackPanel
        {
            Margin = new Thickness(20), Spacing = 14,
            Children =
            {
                new TextBlock { Text = Loc.T("midi_roles.caption"), Classes = { "Caption" }, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new ScrollViewer { MaxHeight = 360, Content = _rows },
                _status,
                new DockPanel
                {
                    Children =
                    {
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add } },
                        new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right, Children = { close } },
                    },
                },
            },
        });
        Rebuild();
    }

    private void Try(Action a)
    {
        try { a(); _status.Text = ""; _edited?.Invoke(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException) { _status.Text = ex.Message; }
        Rebuild();
    }

    private void Rebuild()
    {
        _rows.Children.Clear();
        var roles = _engine.MidiRoles();
        if (roles.Count == 0)
            _rows.Children.Add(new TextBlock { Text = Loc.T("midi_roles.none"), Foreground = NotaPalette.TextTertiary, FontSize = 10 });
        foreach (var r in roles) _rows.Children.Add(Row(r));
    }

    private Control Row(MidiOutputRole role)
    {
        var name = new TextBox { Text = role.Name, Width = 150, MaxLength = MidiOutputRoles.MaxNameLength };
        name.LostFocus += (_, _) => { if (name.Text is { } t && t.Trim() != role.Name) Try(() => MidiOutputRoles.Rename(_engine, role.Id, t)); };

        // The output picker: the bound output first when it isn't connected, kept by its name.
        var outputs = _engine.MidiOutputs().ToList();
        var pick = new ComboBox { Width = 200 };
        var uids = new System.Collections.Generic.List<string> { "" };
        pick.Items.Add(Loc.T("midi_roles.unbound"));
        if (role.OutputUid.Length > 0 && outputs.All(o => o.Uid != role.OutputUid))
        {
            uids.Add(role.OutputUid);
            pick.Items.Add(Loc.F("device.external_midi.missing_output", role.OutputName.Length > 0 ? role.OutputName : role.OutputUid));
        }
        foreach (var o in outputs) { uids.Add(o.Uid); pick.Items.Add(o.Name); }
        pick.SelectedIndex = Math.Max(0, uids.IndexOf(role.OutputUid));
        bool playing = _engine.IsPlaying;
        pick.IsEnabled = !playing;   // a rebind waits for the transport to stop (FR-015)
        var users = MidiOutputRoles.UsedBy(_engine, role.Id).Select(_engine.GetTrackName).ToList();
        ToolTip.SetTip(pick, playing ? Loc.T("midi_roles.stop_to_rebind")
            : users.Count > 0 ? Loc.F("midi_roles.rebind_moves", string.Join(", ", users)) : null);
        pick.SelectionChanged += (_, _) =>
        {
            int i = pick.SelectedIndex;
            if (i < 0 || i >= uids.Count || uids[i] == role.OutputUid) return;
            string uid = uids[i];
            string outName = outputs.FirstOrDefault(o => o.Uid == uid).Name ?? "";
            Try(() => MidiOutputRoles.Rebind(_engine, role.Id, uid, outName));
            if (users.Count > 0) _status.Text = Loc.F("midi_roles.moved", string.Join(", ", users));
        };

        var delete = new Button { Content = Loc.T("common.delete"), Classes = { "ghost" } };
        delete.Click += (_, _) => Try(() => MidiOutputRoles.Delete(_engine, role.Id));
        var used = new TextBlock
        {
            Text = users.Count == 0 ? Loc.T("midi_roles.unused") : Loc.F("midi_roles.used_by", string.Join(", ", users)),
            FontSize = 10, Foreground = NotaPalette.TextTertiary, VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis,
        };
        return new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, pick, delete } },
                used,
            },
        };
    }
}
