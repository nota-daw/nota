// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What Show me needs from the main window (specs/098, research R7): the live control behind a
// registry locator name, and the reveal steps — switch the view, show the Mixer or the Detail
// panel, pick a browser tab. Reveals use the same methods the user's own clicks do, and only
// change what is on screen (FR-018): no selection, no edit, no transport.

using System;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Nota.Application.Localization;
using Nota.Application;
using Nota.Presentation.Assistant;

namespace Nota.App;

public partial class MainWindow
{
    /// <summary>The control a registry locator names, or null when it isn't there.</summary>
    internal Control? LocateAnchor(string locator)
    {
        if (locator.StartsWith("Browser.Tab.", StringComparison.Ordinal))
            return Browser.RailTabControl(BrowserTabIndex(locator["Browser.Tab.".Length..]));
        if (locator.StartsWith(LocatorPrefix.Device, StringComparison.Ordinal))
            return DeviceSlugs.ByAnchor(locator[LocatorPrefix.Device.Length..]) is { } d && _deviceChain is not null
                   && ReferenceEquals(DeviceHost.Content, _deviceChain) ? _deviceChain.CardOf(d.Group, d.EngineKind) : null;
        return locator switch
        {
            "Detail.ClipEditor" => ClipDetailHost.Content as ClipEditorView,
            "Detail.AudioEditor" => ClipDetailHost.Content as AudioClipEditorView,
            "Detail.Pattern" => ClipDetailHost.Content as DrumPatternView,
            "Browser.Search" => Browser.SearchControl,
            "Browser.Collapse" => Browser.CollapseControl,
            "Main.Session" => _session,
            "Main.Modular" => _modular,
            "Main.Timeline" => Timeline,
            "Main.Browser" => Browser,
            _ => this.FindControl<Control>(locator),
        };
    }

    /// <summary>Show me on a command with no button (specs/098): a Settings page opens on that
    /// page — showing a page changes nothing — and every such step names the command's menu path.</summary>
    internal string CommandHint(string commandId)
    {
        if (commandId.StartsWith("prefs.", StringComparison.Ordinal) && commandId != "prefs.open") _commands.Run(commandId);
        var cmd = System.Linq.Enumerable.FirstOrDefault(Nota.Application.Palette.CommandCatalog.Create(), c => c.Id == commandId);
        return cmd is null ? "" : "**" + cmd.Title + "** (" + cmd.Category + ")";
    }

    /// <summary>Where a control that isn't on screen went: a toolbar part shed for lack of room
    /// is behind the ⋯ key; the docked Mixer's or Detail panel's own header buttons stand for
    /// the Mixer or the Detail panel in its window when that is popped out.</summary>
    internal Control? ShedFallback(Control target)
    {
        if (target.IsEffectivelyVisible) return null;
        if (TransportBar.IsVisualAncestorOf(target)) return OverflowBtn;
        if (_mixerWindow is not null && MixerPanel.IsVisualAncestorOf(target)) return _mixer;
        if (_detailWindow is not null && DetailPanel.IsVisualAncestorOf(target)) return _detailWindow.Content as Control;
        return null;
    }

    internal bool HasSelectedTrack => Timeline.SelectedTrackId > 0;

    internal Control SelectTrackHint => Timeline;

    /// <summary>Make one reveal step true; false when it can't be (no track for Detail).</summary>
    internal bool Reveal(RevealStep step)
    {
        switch (step.Kind)
        {
            case RevealKind.View:
                SetView(step.Arg switch { "session" => MainView.Session, "modular" => MainView.Modular, _ => MainView.Arrangement });
                return true;
            case RevealKind.Panel when step.Arg == "mixer":
                if (!IsMixerVisible) ShowMixer();
                if (_mixerDocked && _detailMaximized) SetDetailMaximized(false);
                return true;
            case RevealKind.Panel when step.Arg == "detail":
                if (DetailPanel.IsVisible || DetailFloating) return true;
                if (Timeline.SelectedTrackId <= 0) return false;
                ShowDevices(Timeline.SelectedTrackId);
                return true;
            case RevealKind.Panel when step.Arg == "browser":
                Browser.RevealBrowser();
                return true;
            case RevealKind.BrowserTab:
                Browser.RevealTab(BrowserTabIndex(step.Arg));
                return true;
            case RevealKind.DetailTab when step.Arg == "devices":
                if (Timeline.SelectedTrackId <= 0) return false;
                if (!(DetailPanel.IsVisible || DetailFloating) || !ReferenceEquals(DeviceHost.Content, _deviceChain) || _deviceChain?.TrackId != Timeline.SelectedTrackId)
                    ShowDevices(Timeline.SelectedTrackId);
                return true;
            case RevealKind.DetailTab:
                return DetailPanel.IsVisible || DetailFloating;
            default:
                return false;
        }
    }

    /// <summary>What a step needs first and isn't there: the words for it and where to do it, or
    /// null when the requirement is met (or there is none).</summary>
    internal (string Text, Control? Where)? Unmet(string? requires)
    {
        if (requires is null) return null;
        if (requires == "track")
            return HasSelectedTrack ? null : (Loc.T("assistant.requires_track"), Timeline);
        if (requires.StartsWith("device:", StringComparison.Ordinal))
        {
            if (!HasSelectedTrack) return (Loc.T("assistant.requires_track"), Timeline);
            var page = requires["device:".Length..];
            var d = System.Linq.Enumerable.FirstOrDefault(DeviceSlugs.All(), x => x.Page == page);
            if (d is null) return null;
            if (!ReferenceEquals(DeviceHost.Content, _deviceChain) || _deviceChain?.TrackId != Timeline.SelectedTrackId)
                ShowDevices(Timeline.SelectedTrackId);
            if (_deviceChain?.CardOf(d.Group, d.EngineKind) is not null) return null;
            string name = BuiltinDeviceCatalog.Find((BuiltinDeviceType)(int)d.Group, d.EngineKind)?.Name ?? page;
            int tab = d.Group switch { DeviceGroup.Instrument => 0, DeviceGroup.AudioEffect => 1, _ => 2 };
            Browser.RevealTab(tab);
            return (Loc.F("assistant.requires_device", name), Browser.RailTabControl(tab));
        }
        return requires switch
        {
            "clip:midi" when ClipDetailHost.Content is not ClipEditorView => (Loc.T("assistant.requires_midi_clip"), Timeline),
            "clip:audio" when ClipDetailHost.Content is not AudioClipEditorView => (Loc.T("assistant.requires_audio_clip"), Timeline),
            "clip:pattern" when ClipDetailHost.Content is not DrumPatternView => (Loc.T("assistant.requires_pattern"), Timeline),
            _ => null,
        };
    }

    /// <summary>The view an anchor lives in, shown first.</summary>
    internal void RevealView(AnchorView view)
    {
        if (view == AnchorView.Arrangement && !Timeline.IsVisible) SetView(MainView.Arrangement);
        else if (view == AnchorView.Session && _session is { IsVisible: false }) SetView(MainView.Session);
        else if (view == AnchorView.Modular && _modular is { IsVisible: false }) SetView(MainView.Modular);
    }

    private static int BrowserTabIndex(string name) => name switch
    {
        // AnchorRegistry.BrowserTabs order = the browser's tab indices, except the last two.
        "track" => 8,
        "modular" => 9,
        _ => Math.Max(-1, IndexOf(AnchorRegistry.BrowserTabs, name)),
    };

    private static int IndexOf(System.Collections.Generic.IReadOnlyList<string> list, string s)
    {
        for (int i = 0; i < list.Count; i++) if (list[i] == s) return i;
        return -1;
    }
}
