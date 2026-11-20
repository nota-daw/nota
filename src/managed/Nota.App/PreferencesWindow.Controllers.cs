// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Preferences → MIDI → Controllers (specs/051): every connected port with the profile Nota
// recognised it by, a picker to choose another (or none: plain MIDI), and an on / off switch.
// Below, why profile files were skipped, and the user profiles folder.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.App.Controllers;
using Nota.Application.Localization;

namespace Nota.App;

public sealed partial class PreferencesWindow
{
    private Control ControllersSection()
    {
        var service = App.Services.GetRequiredService<ControllerService>();
        var items = new List<Control>();
        var warnings = new StackPanel { Spacing = 2 };
        void FillWarnings()
        {
            warnings.Children.Clear();
            foreach (var w in service.ProfileWarnings) warnings.Children.Add(Caption(w, muted: true));
        }
        FillWarnings();
        var connected = service.Controllers.Where(c => c.Stage != ControllerStage.Gone).OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
        if (connected.Count == 0)
            items.Add(EmptyBox(Loc.T("preferences.controllers.none_connected")));
        else
        {
            var rows = new List<Control>();
            foreach (var c in connected)
            {
                string uid = c.Uid;
                var choice = service.ChoiceFor(uid);
                bool active = c.Stage == ControllerStage.Active;
                var dot = Dot(active ? NotaPalette.Success : BorderStrong);
                var name = new TextBlock
                {
                    Text = c.Name, FontSize = 12, Foreground = active ? TextPrimary : TextTertiary,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                };

                // Automatic (shows what it found) · every profile · None (plain MIDI).
                var combo = Combo(220);
                var ids = new List<string?> { null };
                combo.Items.Add(c.Profile is { } found && choice.ProfileId is null ? Loc.F("preferences.controllers.automatic_found", found.Name) : Loc.T("preferences.controllers.automatic"));
                foreach (var p in service.Profiles.OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    ids.Add(p.Id);
                    combo.Items.Add(p.Origin == Application.Controllers.ProfileOrigin.User ? Loc.F("preferences.controllers.yours", p.Name) : p.Name);
                }
                // Any port can be driven as a control surface, recognised or not (FR-025).
                ids.Add(ControllerService.ForcedMcu);
                combo.Items.Add(Loc.T("preferences.controllers.force_mcu"));
                ids.Add(ControllerService.ForcedHui);
                combo.Items.Add(Loc.T("preferences.controllers.force_hui"));
                ids.Add("none");
                combo.Items.Add(Loc.T("preferences.controllers.plain_midi"));
                combo.SelectedIndex = Math.Max(0, ids.IndexOf(choice.ProfileId));
                combo.VerticalAlignment = VerticalAlignment.Center;

                var sw = new ToggleSwitch(choice.Enabled, large: true) { VerticalAlignment = VerticalAlignment.Center };
                // Soft takeover (FR-021): off, a knob jumps the value as with MIDI Learn.
                var pickup = new ToggleSwitch(choice.PickupOn, large: false) { VerticalAlignment = VerticalAlignment.Center };
                var pickupBox = new StackPanel
                {
                    Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center,
                    [ToolTip.TipProperty] = Loc.T("preferences.controllers.pickup_tip"),
                    Children = { new TextBlock { Text = Loc.T("preferences.controllers.pickup"), FontSize = 11, Foreground = TextSecondary, VerticalAlignment = VerticalAlignment.Center }, pickup },
                };
                pickupBox.IsVisible = c.Profile is not null;   // pickup is a profile's behaviour; plain MIDI has none
                pickup.Changed += _ => service.SetChoice(uid, service.ChoiceFor(uid).ProfileId, service.ChoiceFor(uid).Enabled, pickup.IsOn);
                void Apply()
                {
                    int k = combo.SelectedIndex;
                    service.SetChoice(uid, k >= 0 && k < ids.Count ? ids[k] : null, sw.IsOn);
                    bool on = c.Stage == ControllerStage.Active;
                    dot.Fill = on ? NotaPalette.Success : BorderStrong;
                    name.Foreground = on ? TextPrimary : TextTertiary;
                }
                combo.SelectionChanged += (_, _) => Apply();
                sw.Changed += _ => Apply();
                // Learn it (FR-030): a controller Nota doesn't know gets the step-by-step wizard; one
                // with a profile — shipped or the user's — opens the mapping editor on it (specs/110),
                // which can still walk everything again. A Mackie / HUI surface speaks its protocol.
                bool editable = c.Profile is { Protocol: Application.Controllers.ControllerProtocol.Generic };
                var setup = Btn(editable ? Loc.T("preferences.controllers.edit_mapping") : Loc.T("preferences.controllers.set_up"));
                setup.IsVisible = c.Profile is null || editable;
                ToolTip.SetTip(setup, editable ? Loc.T("preferences.controllers.edit_mapping_tip") : Loc.T("preferences.controllers.set_up_tip"));
                setup.VerticalAlignment = VerticalAlignment.Center;
                setup.Click += async (_, _) =>
                {
                    // The editor works on the profile's file; one that vanished from disk is learned anew.
                    if (c.Profile is { Protocol: Application.Controllers.ControllerProtocol.Generic } p && service.ProfileJson(p) is not null)
                        await new ControllerMappingWindow(service, c).ShowDialog(this);
                    else
                        await new ControllerSetupWindow(service, c).ShowDialog(this);
                    FillWarnings();
                    Dispatcher.UIThread.Post(() => Select(_selected));   // the row now shows the new profile and "Edit mapping…"
                };
                rows.Add(ListRow(40, "Auto,*,Auto,Auto,Auto,Auto", dot, name, setup, combo, pickupBox, sw));
            }
            items.Add(Table(rows));
            // A surface Nota can't answer: no output port paired with this input (Story 4 scenario 6).
            foreach (var c in connected.Where(c => c.OutputUid.Length == 0 && c.Profile is not null))
                items.Add(Caption(Loc.F("preferences.controllers.no_feedback", c.Name), muted: true));
        }
        items.Add(Caption(Loc.T("preferences.controllers.caption")));


        var reload = Btn(Loc.T("preferences.controllers.reload"));
        reload.Click += (_, _) => { service.ReloadProfiles(); FillWarnings(); };
        var open = Btn(Loc.T("preferences.controllers.open_folder"));
        open.Click += (_, _) =>
        {
            try
            {
                string dir = service.UserProfileFolder;
                Directory.CreateDirectory(dir);
                if (OperatingSystem.IsMacOS()) Process.Start("open", new[] { dir });
                else if (OperatingSystem.IsWindows()) Process.Start("explorer.exe", $"\"{dir}\"");
                else Process.Start("xdg-open", new[] { dir });
            }
            catch (Exception) { /* nothing to open: the caption still names the folder */ }
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { reload, open } };
        items.Add(buttons);
        items.Add(warnings);
        return Section(Loc.T("preferences.controllers.title"), 8, items.ToArray());
    }
}
