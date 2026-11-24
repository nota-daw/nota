// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The Controller submenu in a control's right-click menu (specs/051 Story 7, FR-031–FR-034). A
// window-level handler finds the control under the click by its MIDI Learn binding — so no card
// or mixer strip needs code of its own — and either adds the submenu to the control's own menu
// or opens a menu with just it:
//   "Knob 4 · Arturia KeyLab Essential 61 mk3 (DAW)"   which hardware control drives it now
//   Unbind Knob 4                                       it stops driving this parameter only
//   Bind to ▸ Knob 1 … / Fader 1 … / Loop …             pick one by name
//   Back to default                                     undo the user's change
// A device parameter's binding is its slot on a device page, so Unbind / Bind to edit the
// device's pages (global + the project's copy, FR-018a); anything else is pinned / unbound in
// ControllerOverrides.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Nota.Application;
using Nota.Application.Controllers;
using Nota.Application.Localization;

namespace Nota.App.Controllers;

public sealed class ControllerContextMenu(ControllerService service, DevicePagesStore pages, IAudioEngine engine)
{
    private const string Tag = "nota.controller-menu";

    /// <summary>Listen for right-clicks in <paramref name="root"/> (a window).</summary>
    public void Attach(Control root)
        => root.AddHandler(Control.ContextRequestedEvent, OnContextRequested, RoutingStrategies.Tunnel);

    private void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (e.Source is not Visual v) return;
        Control? bound = null, owner = null;   // owner: the first control on the way up that has a menu of its own
        for (Visual? x = v; x is not null && x is not TopLevel; x = x.GetVisualParent())
        {
            if (x is not Control c) continue;
            if (owner is null && (c.ContextMenu is not null || c.ContextFlyout is not null)) owner = c;
            if (MidiLearn.GetBinding(c) is not null) { bound = c; break; }
        }
        if (bound is null || MidiLearn.GetBinding(bound) is not { } binding) return;
        var items = Items(binding.Target);
        if (items.Count == 0) return;
        var sub = new MenuItem { Header = Loc.T("controllers.menu.title"), Tag = Tag, ItemsSource = items };

        // The control's own menu gets the submenu at its end; otherwise a menu with just it.
        if (owner?.ContextMenu is { } cm)
        {
            Strip(cm.Items);
            cm.Items.Add(new Separator { Tag = Tag });
            cm.Items.Add(sub);
            return;
        }
        if (owner?.ContextFlyout is MenuFlyout mf)
        {
            Strip(mf.Items);
            mf.Items.Add(new Separator { Tag = Tag });
            mf.Items.Add(sub);
            return;
        }
        if (owner is not null) return;   // some other flyout: leave it alone
        var flyout = new MenuFlyout();
        foreach (var i in items) flyout.Items.Add(i);
        flyout.ShowAt(bound, showAtPointer: true);
        e.Handled = true;
    }

    private static void Strip(ItemCollection items)
    {
        foreach (var old in items.OfType<Control>().Where(c => Equals(c.Tag, Tag)).ToList()) items.Remove(old);
    }

    /// <summary>The submenu's items for <paramref name="t"/>; empty when no controller can drive it.</summary>
    public List<Control> Items(MidiTarget t)
    {
        var list = new List<Control>();
        var active = service.Controllers.Where(c => c.Stage == ControllerStage.Active && c.Profile is not null).ToList();
        if (active.Count == 0) return list;

        var device = DeviceOf(t);
        var bindings = service.BindingsFor(t);
        if (bindings.Count == 0)
            list.Add(Disabled(Loc.T("controllers.menu.not_bound")));
        foreach (var b in bindings)
            list.Add(Disabled(Loc.F("controllers.menu.driven_by",
                b.Pinned ? Loc.F("controllers.menu.pinned", b.Label) : b.Label, b.Controller.Profile!.Name)));

        if (bindings.Count > 0)
        {
            string what = bindings[0].Label;
            list.Add(Item(Loc.F("controllers.menu.unbind", what), () =>
            {
                if (device is { } d && bindings.All(b => !b.Pinned)) EditPages(d, pages => FocusedDevice.WithoutParam(pages, d.Ref));
                else service.Overrides?.Unbind(ControllerService.Store(t));
            }));
        }

        var bindTo = new MenuItem { Header = Loc.T("controllers.menu.bind_to") };
        var bindItems = new List<Control>();
        if (device is { } dv)
        {
            if (active.Any(c => c.HasKnobs))
            {
                var knobs = dv.Focused.Knobs(dv.Page);
                for (int k = 0; k < DevicePage.Knobs; k++)
                {
                    int knob = k;
                    string label = ControllerService.KnobLabel(k);
                    if (knobs[k].Name is { Length: > 0 } now) label += " — " + now;
                    bindItems.Add(Item(label, () => EditPages(dv, p => FocusedDevice.WithParamAt(p, dv.Ref, dv.Page, knob))));
                }
            }
        }
        else
        {
            var pinnable = service.PinnableControls();
            var byController = pinnable.GroupBy(p => p.Controller).ToList();
            foreach (var g in byController)
            {
                var target = byController.Count > 1 ? new List<Control>() : bindItems;
                foreach (var (c, decl, label) in g)
                    target.Add(Item(label, () => service.PinControl(c, decl, t)));
                if (byController.Count > 1)
                    bindItems.Add(new MenuItem { Header = g.Key.Profile!.Name, ItemsSource = target });
            }
        }
        if (bindItems.Count > 0)
        {
            bindTo.ItemsSource = bindItems;
            list.Add(bindTo);
        }

        bool changed = device is { } de ? pages.IsEdited(de.Focused.TypeKey) : service.Overrides?.IsChanged(ControllerService.Store(t)) == true;
        if (changed)
            list.Add(Item(device is null ? Loc.T("controllers.menu.reset") : Loc.T("controllers.menu.reset_pages"), () =>
            {
                if (device is { } d) pages.Reset(d.Focused.TypeKey);
                else service.Overrides?.Reset(ControllerService.Store(t));
            }));
        return list;
    }

    /// <summary>A device parameter: its device (resolved with its pages), page reference and the page
    /// Bind to ▸ puts it on (the knobs' current page when it is the focused device, else the first).</summary>
    private sealed record DeviceParam(FocusedDevice Focused, string Ref, int Page);

    private DeviceParam? DeviceOf(MidiTarget t)
    {
        if (ToControllerTarget(t) is not { } ct) return null;
        var focused = service.Focused;
        if (focused is not null && FocusedDevice.RefOf(engine, focused.Address, ct) is { } fr)
            return new DeviceParam(focused, fr, service.KnobFocus()?.Page ?? 0);
        if (FocusedDevice.AddressOf(ct) is not { } a || FocusedDevice.RefOf(engine, a, ct) is not { } r) return null;
        return FocusedDevice.Build(engine, a, pages) is { } f ? new DeviceParam(f, r, 0) : null;
    }

    private void EditPages(DeviceParam d, Func<IReadOnlyList<DevicePage>, List<DevicePage>> edit)
        => pages.Edit(d.Focused.TypeKey, edit(d.Focused.Pages));

    private static ControllerTarget? ToControllerTarget(MidiTarget t) => t.Kind switch
    {
        MidiTargetKind.DeviceParam => ControllerTarget.DeviceParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
        MidiTargetKind.PluginParam => ControllerTarget.PluginParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
        MidiTargetKind.MidiDeviceParam => ControllerTarget.MidiDeviceParam(t.TrackId, t.DeviceIndex, t.ParamIndex),
        MidiTargetKind.RackChainDeviceParam => new ControllerTarget(ControllerTargetKind.RackChainDeviceParam, t.TrackId, t.DeviceIndex, t.ParamIndex, t.Sub),
        MidiTargetKind.RackChainInstrumentParam => new ControllerTarget(ControllerTargetKind.RackChainInstrumentParam, t.TrackId, t.DeviceIndex, t.ParamIndex, t.Sub),
        _ => null,
    };

    private static MenuItem Disabled(string header) => new() { Header = header, IsEnabled = false };

    private static MenuItem Item(string header, Action run)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => run();
        return mi;
    }
}
