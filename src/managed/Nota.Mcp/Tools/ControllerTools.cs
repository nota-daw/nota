// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — controller auto-mapping (specs/051). See which controllers are connected and which
// profile drives each, switch profiles, reload them after editing a file. With
// NOTA_CONTROLLER_TEST=1 two more tools stand in for hardware: controller_inject plays raw MIDI
// "from" a named virtual port and controller_sent reads what Nota sent back to it.

using System.ComponentModel;
using System.Globalization;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class ControllerTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IControllerAccess controllers)
    : EngineTools(engine, dispatch, refresh)
{
    private static bool TestPortsEnabled => Environment.GetEnvironmentVariable("NOTA_CONTROLLER_TEST") == "1";

    public sealed record ProfilesResult(
        [property: Description("Loaded profiles")] ControllerProfileInfo[] Profiles,
        [property: Description("Why profile files or controls were skipped (file name first)")] string[] Warnings);

    [McpServerTool(Name = "list_controllers"), Description("List connected MIDI controller ports: the profile Nota recognised each by (or none = plain MIDI), whether it is on, its bank (first track index of the eight its faders move), knob mode and page.")]
    public Task<ControllerInfo[]> ListControllers() => Read(() => controllers.Controllers().ToArray());

    [McpServerTool(Name = "set_controller"), Description("Choose how Nota uses a controller port (portUid from list_controllers): profileId = a profile id, \"auto\" (recognise automatically) or \"none\" (plain MIDI); enabled and pickup switch it on/off and soft takeover. Omitted values stay as they are.")]
    public Task<ControllerInfo> SetController(string portUid, string? profileId = null, bool? enabled = null, bool? pickup = null) => Mutate(() =>
    {
        string? id = profileId == "auto" ? null : profileId;
        bool keepProfile = profileId is null;
        var current = controllers.Controllers().FirstOrDefault(c => c.PortUid == portUid);
        if (current.PortUid is null) throw new InvalidOperationException($"No controller port \"{portUid}\" — see list_controllers.");
        if (!controllers.SetController(portUid, keepProfile ? current.ProfileId : id, enabled, pickup))
            throw new InvalidOperationException($"No profile \"{profileId}\" — see list_controller_profiles.");
        return controllers.Controllers().First(c => c.PortUid == portUid);
    });

    [McpServerTool(Name = "list_controller_profiles"), Description("List the controller profiles Nota has loaded (shipped and the user's) and any profile file problems.")]
    public Task<ProfilesResult> ListControllerProfiles() => Read(() =>
        new ProfilesResult(controllers.Profiles().ToArray(), controllers.ProfileWarnings().ToArray()));

    [McpServerTool(Name = "reload_controller_profiles"), Description("Re-read the controller profile files (after editing one in the user profiles folder) and re-match connected controllers.")]
    public Task<ProfilesResult> ReloadControllerProfiles() => Mutate(() =>
    {
        controllers.ReloadProfiles();
        return new ProfilesResult(controllers.Profiles().ToArray(), controllers.ProfileWarnings().ToArray());
    });

    [McpServerTool(Name = "get_controller_focus"), Description("The device a controller's knobs follow (the selected device, else the selected track's instrument, else its first device), its type key, the current page and the eight knobs' parameters with their values (0..1). portUid from list_controllers; omit it for the first active controller with knobs.")]
    public Task<ControllerFocusInfo?> GetControllerFocus(string? portUid = null) => Read(() => controllers.Focus(portUid));

    [McpServerTool(Name = "step_controller_device"), Description("Move the controller's knobs to the next (direction 1) or previous (-1) device on the focused track, as the Next / Previous device command: MIDI effects, then the instrument, then the audio effects, a rack as one device; it stops at the ends. Returns the focus after the step (unchanged at an end). portUid as for get_controller_focus.")]
    public Task<ControllerFocusInfo?> StepControllerDevice(int direction, string? portUid = null) => Mutate(() =>
    {
        if (direction is not (1 or -1)) throw new InvalidOperationException("direction must be 1 (next) or -1 (previous).");
        controllers.StepDevice(direction);
        return controllers.Focus(portUid);
    });

    [McpServerTool(Name = "get_session_selection"), Description("What the Session view has selected — what a controller's launch button acts on: kind \"slot\" (track, scene), \"scene\" (a scene row) or \"none\" (the Session isn't showing).")]
    public Task<SessionSelectionInfo> GetSessionSelection() => Read(() => controllers.SessionSelection());

    [McpServerTool(Name = "controller_inject"), Description("Test only (NOTA_CONTROLLER_TEST=1): send raw MIDI bytes (hex, e.g. \"90 5E 7F\") as if from a controller port named portName — a virtual port is created on first use and recognised like a real one. Returns the port's uid.")]
    public Task<string> ControllerInject(string portName, string bytes) => Mutate(() =>
    {
        if (!TestPortsEnabled) throw new InvalidOperationException("controller_inject needs NOTA_CONTROLLER_TEST=1.");
        var data = Hex(bytes);
        int source = E.MidiTestPortAdd(portName);
        if (source < 0) throw new InvalidOperationException("No free MIDI source for a test port.");
        controllers.Rescan();
        if (data.Length > 0 && !E.MidiTestInject(source, data)) throw new InvalidOperationException("The bytes were not accepted.");
        return "test:" + portName;
    });

    [McpServerTool(Name = "controller_sent"), Description("Test only (NOTA_CONTROLLER_TEST=1): the bytes Nota sent to the virtual port portName since the last call (hex), e.g. LED feedback or an identity request.")]
    public Task<string> ControllerSent(string portName) => Read(() =>
    {
        if (!TestPortsEnabled) throw new InvalidOperationException("controller_sent needs NOTA_CONTROLLER_TEST=1.");
        int source = E.MidiTestPortAdd(portName);   // the existing port's source
        var buf = new byte[64 * 1024];
        int n = E.MidiTestTakeSent(source, buf);
        return string.Join(' ', buf.Take(n).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
    });

    private static byte[] Hex(string s)
    {
        var t = (s ?? "").Replace(" ", "").Replace(",", "");
        if (t.Length % 2 != 0) throw new InvalidOperationException("bytes must be hex pairs, e.g. \"90 5E 7F\".");
        var b = new byte[t.Length / 2];
        for (int i = 0; i < b.Length; i++)
            if (!byte.TryParse(t.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b[i]))
                throw new InvalidOperationException($"\"{t.Substring(i * 2, 2)}\" is not a hex byte.");
        return b;
    }
}
