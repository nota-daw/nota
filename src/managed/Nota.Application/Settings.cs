// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App settings contract. The interface + data live in Application (ViewModels
// depend on them); the JSON-on-disk implementation is SettingsService in
// Infrastructure.

using System.Collections.Generic;

namespace Nota.Application;

public enum ToolbarSide { Left, Right }

/// <summary>The appearance before colour themes (spec 011). Still written, in step with
/// the chosen theme's kind, so an older Nota opening this settings file gets a sensible
/// variant; read only to migrate (see <see cref="Appearance"/>).</summary>
public enum AppTheme { Dark, Light, System }

public sealed class Settings
{
    public ToolbarSide ToolbarSide { get; set; } = ToolbarSide.Left;
    /// <summary>Legacy appearance (Dark / Light / System). Kept in step with
    /// <see cref="ColourTheme"/> for older versions; read only when ColourTheme is empty.</summary>
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    /// <summary>The colour theme the UI wears — a theme id such as "studio-dark", or
    /// "system" to follow the OS with <see cref="SystemDarkTheme"/> / <see cref="SystemLightTheme"/>.
    /// Empty until first resolved from the legacy <see cref="Theme"/>. Studio Dark by default —
    /// a DAW is used in dark rooms for long sessions.</summary>
    public string ColourTheme { get; set; } = "";
    /// <summary>Theme worn while following the OS and it is in dark mode (a dark theme).</summary>
    public string SystemDarkTheme { get; set; } = Appearance.StudioDark;
    /// <summary>Theme worn while following the OS and it is in light mode (a light theme).</summary>
    public string SystemLightTheme { get; set; } = Appearance.StudioLight;
    /// <summary>Whether Session view shows its inspector column (toolbar toggle). On by default;
    /// folding it gives the clip grid its width back on small windows.</summary>
    public bool SessionInspectorOpen { get; set; } = true;
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
    /// <summary>The Mixer window's view: "strips" or "map" (the Stereo Map, specs/008).
    /// A per-user preference, remembered between openings.</summary>
    public string MixerView { get; set; } = "strips";
    /// <summary>Record a project version on every save (the History tab). On by default.</summary>
    public bool KeepVersionHistory { get; set; } = true;
    /// <summary>Bars of metronome count-in before Play / Record from stop (0 = off). Set from
    /// the metronome button's right-click menu.</summary>
    public int CountInBars { get; set; }
    /// <summary>Loop record mode (#29): false = Overdub (every loop pass adds to the MIDI take),
    /// true = Replace (a new pass you play on replaces the earlier ones). Set from the transport
    /// toggle next to Loop, ⌘⇧R, or the Transport menu.</summary>
    public bool LoopRecordReplace { get; set; }
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

    // --- snap grid (specs/003-adaptive-snap-grid) ------------------------------
    // App-wide, one per editor, never written to project files: opening a project
    // doesn't change how the user likes to snap.
    /// <summary>The arrangement's grid: Auto · Medium, snap on by default.</summary>
    public Nota.Application.Editing.GridSetting ArrangementGrid { get; set; } = Nota.Application.Editing.GridSetting.DefaultArrangement;
    /// <summary>The piano roll's grid, independent of the arrangement's.</summary>
    public Nota.Application.Editing.GridSetting PianoRollGrid { get; set; } = Nota.Application.Editing.GridSetting.DefaultPianoRoll;
    /// <summary>The piano roll's Quantize (and clip tools') step — its own value, so the
    /// result never depends on the zoom. 1/16 by default.</summary>
    public Nota.Application.Editing.GridStep QuantizeStep { get; set; } = Nota.Application.Editing.GridStep.Of(1, false);

    // --- command palette (⌘⇧P) ----------------------------------------------
    /// <summary>How often and when each palette / browser item was last applied, by palette
    /// id — frecency for the ranking (CP-15).</summary>
    public Dictionary<string, Nota.Application.Palette.PaletteUse> PaletteUsage { get; set; } = new();
    /// <summary>The last items applied from the palette, most recent first (its Recent section).</summary>
    public List<string> PaletteRecent { get; set; } = new();

    // --- browser view options (the ⋮ button next to the browser search) ------
    /// <summary>Show each row's type as a quiet tag on the right edge of the browser list.</summary>
    public bool BrowserShowTypeTags { get; set; } = true;
    /// <summary>Group the device tabs into BUILT-IN and PLUG-INS sections with counts.</summary>
    public bool BrowserGroupBySource { get; set; } = true;
    /// <summary>Show the category chips above the device lists (Synths, Dynamics…).</summary>
    public bool BrowserCategoryChips { get; set; } = true;
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
    /// <summary>How audio clips draw their waveform on the lanes (Settings → Appearance):
    /// 0 bars — spaced peak strokes (default), 1 solid — one column per pixel.</summary>
    public int ArrangementWaveform { get; set; }
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
