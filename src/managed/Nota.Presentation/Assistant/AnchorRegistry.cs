// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Every control the manual's Show me steps may point at (specs/098, contracts/anchors.md), as
// data: its anchor id ("ui:transport.mixer"), the locator name the app binds to the live control,
// the view it lives in, and what has to be shown first to reach it — switch the view, open the
// Mixer or the Detail panel, pick a browser tab. Reveal steps only change what is on screen; they
// never create, select or edit anything, and never touch the transport (FR-018).
// Command anchors ("cmd:view.mixer") name a command; one with a button here is shown as that
// button, the rest as their key and menu path. The smoke test checks every anchor in the
// shipped manual against this registry (FR-019) and #607 reuses it to point at controls.

namespace Nota.Presentation.Assistant;

public enum AnchorView { Any, Arrangement, Session, Modular }

public enum RevealKind
{
    View,        // arrangement | session | modular
    Panel,       // mixer | detail | browser
    DetailTab,   // devices (the selected track's chain) | pattern | clip
    BrowserTab,  // instruments | audio-effects | midi-effects | files | presets | projects | midi-map | history | track | modular
}

public sealed record RevealStep(RevealKind Kind, string Arg);

/// <summary>A control Show me can point at.</summary>
/// <param name="Requires">What must exist first, or null: "track" (a selected track),
/// "device:&lt;page&gt;" (that built-in device on the selected track), "clip:midi" / "clip:audio" /
/// "clip:pattern" (that editor open in the Detail panel).</param>
public sealed record AnchorDefinition(string Id, string Locator, AnchorView View, IReadOnlyList<RevealStep> Reveal, string? Requires);

/// <summary>Locator name prefixes the app resolves itself (not control names).</summary>
public static class LocatorPrefix
{
    public const string Device = "dev:";
}

public static class AnchorRegistry
{
    public const string UiPrefix = "ui:", CommandPrefix = "cmd:", None = "none";

    public static readonly IReadOnlyList<string> BrowserTabs =
        ["instruments", "audio-effects", "midi-effects", "files", "presets", "projects", "midi-map", "history", "track", "modular"];

    private static readonly List<AnchorDefinition> _all = new();
    private static readonly Dictionary<string, AnchorDefinition> _byId = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, string> _commands = new(StringComparer.Ordinal);

    public static IReadOnlyList<AnchorDefinition> All => _all;

    /// <summary>Command id → the ui: anchor of its button.</summary>
    public static IReadOnlyDictionary<string, string> CommandButtons => _commands;

    public static AnchorDefinition? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The ui: anchor a command anchor stands for, if it has a button.</summary>
    public static AnchorDefinition? ForCommand(string commandId) =>
        _commands.TryGetValue(commandId, out var ui) ? Find(ui) : null;

    /// <summary>Whether an anchor id names something: a registered ui: id, or a cmd: id among
    /// <paramref name="commandIds"/>.</summary>
    public static bool IsKnown(string anchor, ISet<string> commandIds) =>
        anchor.StartsWith(UiPrefix, StringComparison.Ordinal) ? _byId.ContainsKey(anchor)
        : anchor.StartsWith(CommandPrefix, StringComparison.Ordinal) && commandIds.Contains(anchor[CommandPrefix.Length..]);

    static AnchorRegistry()
    {
        // ---- the view switch and the transport bar -----------------------------------------
        A("ui:view.arrangement", "ArrangeBtn");
        A("ui:view.session", "SessionBtn");
        A("ui:view.modular", "ModularBtn");
        A("ui:transport.menu", "MenuBtn");
        A("ui:transport.stop", "StopBtn");
        A("ui:transport.play", "PlayBtn");
        A("ui:transport.record", "RecordBtn");
        A("ui:transport.loop", "LoopCell");
        A("ui:transport.loop-record", "LoopRecordCell");
        A("ui:transport.tempo", "BpmHost");
        A("ui:transport.tap", "TapChip");
        A("ui:transport.time-signature", "TimeSigCell");
        A("ui:transport.key", "KeyChip");
        A("ui:transport.grid", "SnapChip");
        A("ui:transport.metronome", "MetronomeBtn");
        A("ui:transport.follow", "FollowBtn");
        A("ui:transport.snap", "SnapToggle");
        A("ui:transport.automation", "AutomationToggle");
        A("ui:transport.mixer", "MixerToggle");
        A("ui:transport.assistant", "AssistantToggle");
        A("ui:transport.midi-learn", "MidiLearnBtn");
        A("ui:transport.remote", "RemoteBtn");
        A("ui:transport.dsp", "CpuMeter");
        A("ui:transport.master", "MasterWell");
        A("ui:transport.overflow", "OverflowBtn");

        // ---- status bar ----------------------------------------------------------------------
        A("ui:status.downloads", "DownloadHost");
        A("ui:status.dropouts", "DropoutButton");
        A("ui:status.audio-format", "AudioFormatButton");
        A("ui:status.version", "VersionButton");

        // ---- browser -------------------------------------------------------------------------
        A("ui:browser", "Main.Browser", reveal: [new(RevealKind.Panel, "browser")]);
        A("ui:browser.search", "Browser.Search", reveal: [new(RevealKind.Panel, "browser")]);
        A("ui:browser.collapse", "Browser.Collapse");
        foreach (var tab in BrowserTabs)
            A("ui:browser.tab." + tab, "Browser.Tab." + tab,
              tab == "modular" ? AnchorView.Modular : AnchorView.Any,
              [new(RevealKind.BrowserTab, tab)]);

        // ---- main views ------------------------------------------------------------------------
        A("ui:arrangement", "Main.Timeline", AnchorView.Arrangement);
        A("ui:session", "Main.Session", AnchorView.Session);
        A("ui:modular", "Main.Modular", AnchorView.Modular);

        // ---- Mixer -----------------------------------------------------------------------------
        A("ui:mixer", "MixerPanel", reveal: [new(RevealKind.Panel, "mixer")]);
        A("ui:mixer.strips", "MixerStripsBtn", reveal: [new(RevealKind.Panel, "mixer")]);
        A("ui:mixer.stereo-map", "MixerMapBtn", reveal: [new(RevealKind.Panel, "mixer")]);
        A("ui:mixer.pop-out", "MixerPopOut", reveal: [new(RevealKind.Panel, "mixer")]);

        // ---- Detail panel ----------------------------------------------------------------------
        A("ui:detail", "DetailPanel", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.devices", "DetailDevicesBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.pattern", "DetailPatternBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.clip", "DetailClipBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.freeze", "FreezeBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.live-freeze", "LiveFreezeBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.maximize", "DetailMaximizeBtn", reveal: [new(RevealKind.Panel, "detail")], requires: "track");
        A("ui:detail.pop-out", "DetailPopOut", reveal: [new(RevealKind.Panel, "detail")], requires: "track");

        // ---- built-in devices: each device's card in the selected track's chain -----------------
        foreach (var d in DeviceSlugs.All())
            A(d.Anchor, LocatorPrefix.Device + d.Anchor, reveal: [new(RevealKind.DetailTab, "devices")], requires: "device:" + d.Page);

        // ---- the editors in the Detail panel -------------------------------------------------------
        A("ui:clip-editor", "Detail.ClipEditor", requires: "clip:midi");
        A("ui:audio-editor", "Detail.AudioEditor", requires: "clip:audio");
        A("ui:pattern-editor", "Detail.Pattern", requires: "clip:pattern");

        // ---- the Assistant itself --------------------------------------------------------------
        A("ui:assistant.clear", "AssistantClear");

        // ---- commands with a button --------------------------------------------------------------
        Cmd("view.arrangement", "ui:view.arrangement");
        Cmd("view.session", "ui:view.session");
        Cmd("view.modular", "ui:view.modular");
        Cmd("view.mixer", "ui:transport.mixer");
        Cmd("view.assistant", "ui:transport.assistant");
        Cmd("view.automation", "ui:transport.automation");
        Cmd("view.follow", "ui:transport.follow");
        Cmd("view.midiLearn", "ui:transport.midi-learn");
        Cmd("view.connectPhone", "ui:transport.remote");
        Cmd("view.toggleBrowser", "ui:browser.collapse");
        Cmd("view.history", "ui:browser.tab.history");
        Cmd("view.maximizeDetail", "ui:detail.maximize");
        Cmd("view.popOutDetail", "ui:detail.pop-out");
        Cmd("view.mixerFloat", "ui:mixer.pop-out");
    }

    private static void A(string id, string locator, AnchorView view = AnchorView.Any,
                          IReadOnlyList<RevealStep>? reveal = null, string? requires = null)
    {
        var def = new AnchorDefinition(id, locator, view, reveal ?? [], requires);
        _all.Add(def);
        _byId.Add(id, def);
    }

    private static void Cmd(string command, string ui) => _commands.Add(command, ui);
}
