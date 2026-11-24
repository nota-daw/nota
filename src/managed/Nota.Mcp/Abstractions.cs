// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Mcp;

/// <summary>Marshals engine access onto the UI/message thread that owns <c>IAudioEngine</c>.
/// MCP tool handlers run on Kestrel request threads; every engine call must go through this.
/// Also serializes concurrent MCP calls (single UI thread) — no engine re-entrancy.
/// Implemented by Nota.App via Avalonia's Dispatcher; the tests use a synchronous stub.</summary>
public interface IEngineDispatch
{
    Task<T> InvokeAsync<T>(Func<T> func);
    Task InvokeAsync(Action action);
}

/// <summary>Raised after a mutating tool so the arrangement + device panel redraw and the user
/// sees AI edits live. Nota.App wires this to Timeline/DeviceChain refresh; no-op in tests.</summary>
public interface IArrangementRefresh
{
    void Refresh();
}

/// <summary>One learned MIDI mapping, flattened for the MCP tools.</summary>
public readonly record struct MidiMappingInfo(
    int Index, string Control, string SourceKind, int Channel, int Number,
    double RangeMin, double RangeMax, bool Invert, string Source);

/// <summary>A distinct controller source seen recently (for discovery), value 0..127.</summary>
public readonly record struct MidiControlSeen(string Kind, int Channel, int Number, int LastValue, int Count);

/// <summary>Facade over the app's MIDI-learn service so the MCP tools can inspect connected
/// controllers, view/manage learned mappings, and arm learn — without referencing Nota.App.
/// Implemented in Nota.App over MidiLearnService; every call runs on the engine's UI thread.</summary>
public interface IMidiLearnAccess
{
    /// <summary>Learn mode: mappable controls highlight and a click selects a target to bind.</summary>
    bool Armed { get; set; }
    IReadOnlyList<MidiMappingInfo> Mappings();
    bool RemoveMapping(int index);
    void ClearMappings();
    bool SetMappingRange(int index, double rangeMin, double rangeMax, bool invert);
    IReadOnlyList<MidiControlSeen> RecentControls();
    void ClearRecentControls();
}

/// <summary>A connected controller port as the controller tools report it (specs/051).</summary>
public readonly record struct ControllerInfo(
    string PortUid, string PortName, string OutputUid, string? ProfileId, string? ProfileName,
    string Protocol, string Stage, bool Enabled, bool Pickup, int BankOrigin, string KnobMode, int Page);

/// <summary>A loaded controller profile (specs/051).</summary>
public readonly record struct ControllerProfileInfo(string Id, string Name, string Origin, string Protocol, string Source);

/// <summary>Facade over the app's controller auto-mapping (specs/051) for the MCP tools, without
/// referencing Nota.App. Every call runs on the UI thread.</summary>
public interface IControllerAccess
{
    IReadOnlyList<ControllerInfo> Controllers();
    /// <summary>Profile override (null = automatic, "none" = plain MIDI, or a profile id), on / off
    /// and pickup for a port; null leaves that setting as it is. False for an unknown port or profile.</summary>
    bool SetController(string portUid, string? profileId, bool? enabled, bool? pickup);
    IReadOnlyList<ControllerProfileInfo> Profiles();
    IReadOnlyList<string> ProfileWarnings();
    void ReloadProfiles();
    /// <summary>Look at the port list now (after a test port was added) instead of on the next scan.</summary>
    void Rescan();
    /// <summary>The device a controller's knobs follow (the first active one with knobs when
    /// <paramref name="portUid"/> is null), or null when nothing is focused.</summary>
    ControllerFocusInfo? Focus(string? portUid);
    /// <summary>Next (+1) / previous (−1) device on the focused track, as the command (specs/107).
    /// False at an end of the chain or with no track.</summary>
    bool StepDevice(int direction);
    /// <summary>The Session view's selection (specs/107).</summary>
    SessionSelectionInfo SessionSelection();
}

/// <summary>What the Session view has selected: kind "none" | "slot" | "scene" (specs/107, contracts/mcp.md).</summary>
public readonly record struct SessionSelectionInfo(string Kind, int? Track, int? Scene);

/// <summary>One knob of the focused page: its parameter (null name = empty knob) and value 0..1.</summary>
public readonly record struct ControllerKnobInfo(int Knob, string? Name, double? Value);

/// <summary>What a controller's knobs follow (specs/051, contracts/mcp.md).</summary>
public readonly record struct ControllerFocusInfo(
    int Track, string Section, int Index, int? RackChain, int? RackSlot, string Device, string TypeKey,
    string KnobMode, int Page, int PageCount, string PageName, ControllerKnobInfo[] Params);

/// <summary>Facade over the open project for the version-history tools: what is open, whether
/// it has unsaved edits, and the two actions that go through the window (save, switch) —
/// without referencing Nota.App. Implemented in Nota.App over MainWindow; every member runs on
/// the UI thread (call it inside <see cref="IEngineDispatch.InvokeAsync{T}"/>).</summary>
public interface IProjectVersionsAccess
{
    /// <summary>The open <c>.nota</c> bundle, or null while the project has never been saved.</summary>
    string? ProjectPath { get; }
    bool HasUnsavedChanges { get; }
    /// <summary>Settings → Version history.</summary>
    bool HistoryEnabled { get; }
    /// <summary>Saves the project in place, recording a version (with the note, if any).
    /// False when it couldn't be saved.</summary>
    Task<bool> SaveVersionAsync(string? note);
    /// <summary>Restores a version into the bundle and reopens the project, discarding unsaved
    /// edits (the caller checked). False if it couldn't switch.</summary>
    Task<bool> SwitchToVersionAsync(string versionId);
    /// <summary>A tool changed the history directly (a label, note or star): redraw the History tab.</summary>
    void NotifyHistoryChanged();
}

/// <summary>The project key (lives on the app's transport, not in the engine), for the
/// smart-sample tools. A <c>MusicalKey.Code</c>, −1 = none. Runs on the UI thread.</summary>
public interface IProjectKeyAccess
{
    int KeyCode { get; set; }
}

/// <summary>Facade over the app's colour themes for the theme tools (spec 087): list, read,
/// validate, preview, save, wear and try on themes — without referencing Nota.App. Implemented
/// in Nota.App over NotaThemes / NotaThemeService; every member runs on the UI thread (call it
/// inside <see cref="IEngineDispatch.InvokeAsync{T}"/>).</summary>
public interface IThemeAccess
{
    ThemeListing List();
    /// <summary>A listed theme's file text (format 1–3), or null for an unknown id. With
    /// <paramref name="look"/> "classic", a built-in's classic palette (spec 091).</summary>
    string? GetFile(string id, string? look = null);
    ThemeCatalogue Catalogue();
    IReadOnlyList<string> SystemFonts();
    Nota.Application.Themes.ThemeValidation Validate(string json);
    /// <summary>Images of <paramref name="json"/> worn on the named surfaces (all when null),
    /// rendered offscreen; the live window does not change. Nothing is rendered for an invalid theme.</summary>
    Task<ThemePreviews> PreviewAsync(string json, IReadOnlyList<string>? surfaces, bool themeAccent, string? look = null);
    ThemeSaveResult Save(string json, bool overwrite);
    /// <summary>Wear a listed theme and store it, as a pick in Preferences. Null, or why not.</summary>
    string? Apply(string id);
    /// <summary>Wear <paramref name="json"/> live without storing it. Whether it is worn.</summary>
    bool TryOn(string json, out Nota.Application.Themes.ThemeValidation validation);
    /// <summary>End a try-on; the id worn afterwards.</summary>
    string RevertTryOn();
    /// <summary>Wear and store a look ("new" | "classic", spec 091), as Preferences does; the id
    /// worn afterwards. An unknown look throws ArgumentException naming the allowed ones.</summary>
    string SetLook(string look);
}

public sealed record ThemeListing(ThemeListEntry[] Themes, string Worn, bool TryOn, ThemeChoiceInfo Choice, ThemeFileProblem[] Problems);
public sealed record ThemeListEntry(string Id, string Name, string Kind, string Source, int Warnings, bool FontMissing,
    string? FontFamily, double Roundness);
public sealed record ThemeChoiceInfo(bool FollowSystem, string Single, string Dark, string Light, string Look);
public sealed record ThemeFileProblem(string File, string Reason);
public sealed record ThemeTokenInfo(string Name, string Group, string Role, bool Translucent);
public sealed record ThemeRadiusStepInfo(string Name, double Design);
/// <summary>The accepted limits, each as [min, max]: radius scale, a radius step in px, the font family name's length.</summary>
public sealed record ThemeRanges(double[] Scale, double[] Step, int[] FontFamilyLength);
public sealed record ThemeCatalogue(ThemeTokenInfo[] Tokens, ThemeRadiusStepInfo[] RadiusSteps, ThemeRanges Ranges, int[] FormatVersions);
public sealed record ThemePreviewImage(string Name, string Description, byte[] Png);
public sealed record ThemePreviews(Nota.Application.Themes.ThemeValidation Validation, ThemePreviewImage[] Images,
    string[] Rendered, string[] Unknown);
public sealed record ThemeSaveResult(Nota.Application.Themes.ThemeValidation Validation, bool Saved, string? File, string? Reason);
