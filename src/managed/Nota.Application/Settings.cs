// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App settings contract. The interface + data live in Application (ViewModels
// depend on them); the JSON-on-disk implementation is SettingsService in
// Infrastructure.

using System.Collections.Generic;

namespace Nota.Application;

public enum ToolbarSide { Left, Right }

/// <summary>Which palette the UI wears. <see cref="System"/> follows the OS appearance
/// and flips live when the user changes it.</summary>
public enum AppTheme { Dark, Light, System }

public sealed class Settings
{
    public ToolbarSide ToolbarSide { get; set; } = ToolbarSide.Left;
    /// <summary>Palette variant: Ember Graphite (dark), Ember Paper (light), or the OS
    /// appearance. Dark by default — a DAW is used in dark rooms for long sessions.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    /// <summary>Browser sample library folder ("" = default ~/Music/Nota Samples). M7-4.</summary>
    public string SamplesFolder { get; set; } = "";
    /// <summary>Browser projects folder ("" = default ~/Documents/Nota Projects). M7-4.</summary>
    public string ProjectsFolder { get; set; } = "";
    /// <summary>Last app version whose "What's New" the user has already seen ("" = never).
    /// Compared against the running app version on launch to show the changelog once.</summary>
    public string LastSeenVersion { get; set; } = "";
    /// <summary>Recently opened/saved project bundle paths, most-recent first. Shown on the
    /// welcome screen; capped and pruned of missing folders when displayed.</summary>
    public List<string> RecentProjects { get; set; } = new();
    /// <summary>Show the welcome screen (recent projects launcher) on startup. On by default;
    /// toggled from the welcome screen's "Show on startup" checkbox.</summary>
    public bool ShowWelcomeOnStartup { get; set; } = true;
    /// <summary>Record a project version on every save (the History tab). On by default.</summary>
    public bool KeepVersionHistory { get; set; } = true;
    /// <summary>Run the built-in MCP server so an AI (Claude Desktop / Claude Code) can drive the
    /// live app. Off by default; loopback-only. Toggled in Preferences.</summary>
    public bool McpEnabled { get; set; }
    /// <summary>Loopback TCP port for the MCP HTTP server.</summary>
    public int McpPort { get; set; } = 3900;
    /// <summary>Nota Remote: serve the phone controller on the local network. Off by default;
    /// phones pair with a code from the QR. See Settings → Remote.</summary>
    public bool RemoteEnabled { get; set; }
    /// <summary>TCP port Nota Remote listens on (all interfaces).</summary>
    public int RemotePort { get; set; } = 7788;
    /// <summary>What a phone may do: 0 play notes only, 1 also control the project (mixer,
    /// transport, macros, XY, Session).</summary>
    public int RemoteAccess { get; set; } = 1;

    /// <summary>Use a connected gamepad as a live note source. Off by default; the
    /// pads play through the armed/audition instrument track like the computer
    /// keyboard. See Preferences → Gamepads.</summary>
    public bool GamepadEnabled { get; set; }
    /// <summary>Base-octave shift for gamepad notes, in semitones ÷ 12 (d-pad
    /// up/down live-shifts this)</summary>
    public int GamepadOctave { get; set; }

    // --- browser view options (the ⋮ button next to the browser search) ------
    /// <summary>Show each row's type as a quiet tag on the right edge of the browser list.</summary>
    public bool BrowserShowTypeTags { get; set; } = true;
    /// <summary>Group the device tabs into BUILT-IN and PLUG-INS sections with counts.</summary>
    public bool BrowserGroupBySource { get; set; } = true;
    /// <summary>Float favorited devices to the top of their section.</summary>
    public bool BrowserFavoritesFirst { get; set; } = true;
    /// <summary>The browser is folded down to its icon rail.</summary>
    public bool BrowserCollapsed { get; set; }
    /// <summary>Files tab player: selecting a sample auditions it.</summary>
    public bool BrowserPreviewAuto { get; set; } = true;
    /// <summary>Files tab player: loop the audition until stopped.</summary>
    public bool BrowserPreviewLoop { get; set; }
    /// <summary>Files tab player: fader position 0..1 (gain = v², so 0.7 ≈ −6 dB, 1 = 0 dB).</summary>
    public double BrowserPreviewVolume { get; set; } = 0.7;
    /// <summary>Browser player: the demo track effect presets are heard through — a track id
    /// ("drums", "house" …), "auto" (chosen per effect) or "sample" (the Files tab's sample).</summary>
    public string BrowserPreviewFxTrack { get; set; } = "auto";
    /// <summary>Browser player on the Instr / FX / MIDI / Presets tabs: selecting a preset or
    /// device plays it. Off by default (Files has its own, BrowserPreviewAuto).</summary>
    public bool BrowserPresetPreviewAuto { get; set; }
    /// <summary>Browser player: the well shows the live spectrum instead of the waveform.</summary>
    public bool BrowserPreviewSpectrum { get; set; }

    // --- smart samples (Files tab ⋮ menu) ------------------------------------
    /// <summary>A loop dropped on the arrangement is warped to the project tempo from the tempo
    /// the sample index knows; a one-shot never is.</summary>
    public bool SamplesWarpLoops { get; set; } = true;
    /// <summary>A loop with a known key dropped on the arrangement is transposed to the project
    /// key (the shorter way, ±6 semitones; one-shots keep their pitch). Off by default — it
    /// changes the sound.</summary>
    public bool SamplesMatchKey { get; set; }

    // --- arrangement view options (View menu) --------------------------------
    /// <summary>How many clips print their name on the lane: 0 every clip, 1 the head of each
    /// run (default — a repeated pattern then reads as one block), 2 none.</summary>
    public int ArrangementClipLabels { get; set; } = 1;
    /// <summary>Show the song-structure (sections) lane over the ruler.</summary>
    public bool ArrangementShowSections { get; set; } = true;
}

public interface ISettingsService
{
    Settings Current { get; }
    void Save();
    /// <summary>Fired after Save() so views can re-apply settings live.</summary>
    event Action? Changed;

    /// <summary>The sample library folder to browse (resolved default, created). M7-4.</summary>
    string ResolvedSamplesFolder();
    /// <summary>The projects folder to browse (resolved default, created). M7-4.</summary>
    string ResolvedProjectsFolder();
    /// <summary>App-managed presets folder (created). M7-4.</summary>
    string PresetsFolder();
}
