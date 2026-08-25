// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — hosted AU/VST3 plugins: browse the scanned catalog, add a plugin as a track
// instrument or an insert effect, open/close its native editor, and read/write its parameters and
// opaque state. Plugin parameters use the same normalized 0..1 model as built-ins, addressed by
// (trackId, deviceIndex): deviceIndex -1 = the track's instrument, >= 0 = an insert device.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class PluginTools(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, IPluginCatalog catalog)
    : EngineTools(engine, dispatch, refresh)
{
    private readonly IPluginCatalog _cat = catalog;

    public sealed record PluginEntry(int Index, string Id, string Description);
    public sealed record Param(int Index, string Id, string Name, float Value);

    [McpServerTool(Name = "list_plugins"), Description(
        "List the scanned AU/VST3 plugins (catalog index, stable id, description \"Name | Format | inst|fx | Vendor\"). "
        + "Use the index with add_plugin_instrument / add_plugin_effect.")]
    public Task<PluginEntry[]> ListPlugins() => Read(() =>
    {
        int n = _cat.Count;
        var list = new List<PluginEntry>(n);
        for (int i = 0; i < n; i++)
            list.Add(new PluginEntry(i, _cat.Id(i) ?? "", _cat.Description(i) ?? ""));
        return list.ToArray();
    });

    [McpServerTool(Name = "find_plugin"), Description("Find a plugin's catalog index by its stable id (-1 if not installed).")]
    public Task<int> FindPlugin(string identifier) => Read(() => _cat.IndexOfId(identifier));

    [McpServerTool(Name = "add_plugin_instrument"), Description("Add a new track whose instrument is the hosted plugin at the given catalog index. Returns the track id (0 on failure).")]
    public Task<int> AddPluginInstrument(int catalogIndex) => Mutate(() => E.AddPluginInstrumentTrack(catalogIndex));

    [McpServerTool(Name = "set_track_instrument_plugin"), Description("Replace a track's instrument with the hosted plugin at the given catalog index.")]
    public Task SetTrackInstrumentPlugin(int trackId, int catalogIndex) => Mutate(() => E.SetTrackInstrumentPlugin(trackId, catalogIndex));

    [McpServerTool(Name = "add_plugin_effect"), Description("Add a hosted plugin as an insert effect on a track. Returns the new device index (-1 on failure).")]
    public Task<int> AddPluginEffect(int trackId, int catalogIndex) => Mutate(() => E.AddTrackEffectPlugin(trackId, catalogIndex));

    [McpServerTool(Name = "open_plugin_editor"), Description("Open the plugin's native editor window (deviceIndex -1 = the track instrument).")]
    public Task OpenPluginEditor(int trackId, int deviceIndex) => Mutate(() => E.OpenPluginEditor(trackId, deviceIndex));

    [McpServerTool(Name = "close_plugin_editor"), Description("Close the plugin's native editor window (deviceIndex -1 = the track instrument).")]
    public Task ClosePluginEditor(int trackId, int deviceIndex) => Mutate(() => E.ClosePluginEditor(trackId, deviceIndex));

    [McpServerTool(Name = "get_plugin_params"), Description(
        "List a hosted plugin's parameters (index, stable id, name, normalized value 0..1). "
        + "deviceIndex -1 = the track instrument, >= 0 = an insert device.")]
    public Task<Param[]> GetPluginParams(int trackId, int deviceIndex) => Read(() =>
    {
        int pc = E.PluginParamCount(trackId, deviceIndex);
        var ps = new Param[pc];
        for (int i = 0; i < pc; i++)
            ps[i] = new Param(i, E.PluginParamId(trackId, deviceIndex, i), E.PluginParamName(trackId, deviceIndex, i), E.PluginParamGet(trackId, deviceIndex, i));
        return ps;
    });

    [McpServerTool(Name = "set_plugin_param"), Description("Set a hosted plugin parameter by index to a normalized value 0..1 (deviceIndex -1 = the track instrument).")]
    public Task SetPluginParam(int trackId, int deviceIndex, int paramIndex, [Description("Normalized 0..1")] float value)
        => Mutate(() => E.PluginParamSet(trackId, deviceIndex, paramIndex, Math.Clamp(value, 0f, 1f)));

    [McpServerTool(Name = "get_plugin_state"), Description("Get a hosted plugin's opaque state as base64 (deviceIndex -1 = the track instrument). Round-trips with set_plugin_state.")]
    public Task<string> GetPluginState(int trackId, int deviceIndex) => Read(() => Convert.ToBase64String(E.GetPluginState(trackId, deviceIndex) ?? Array.Empty<byte>()));

    [McpServerTool(Name = "set_plugin_state"), Description("Restore a hosted plugin's opaque state from base64 (from get_plugin_state).")]
    public Task SetPluginState(int trackId, int deviceIndex, string base64State) => Mutate(() => E.SetPluginState(trackId, deviceIndex, Convert.FromBase64String(base64State)));
}
