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
    /// <summary>Run the built-in MCP server so an AI (Claude Desktop / Claude Code) can drive the
    /// live app. Off by default; loopback-only. Toggled in Preferences.</summary>
    public bool McpEnabled { get; set; }
    /// <summary>Loopback TCP port for the MCP HTTP server.</summary>
    public int McpPort { get; set; } = 3900;
    /// <summary>Use a connected gamepad as a live note source. Off by default; the
    /// pads play through the armed/audition instrument track like the computer
    /// keyboard. See Preferences → Gamepads.</summary>
    public bool GamepadEnabled { get; set; }
    /// <summary>Base-octave shift for gamepad notes, in semitones ÷ 12 (d-pad
    /// up/down live-shifts this)</summary>
    public int GamepadOctave { get; set; }
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
