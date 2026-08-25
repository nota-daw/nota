// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// MCP tools — presets: the shipped factory library for built-in instruments/effects (apply as a
// new track or in place) and the user's saved presets on disk (list / apply / capture-and-save).

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class PresetTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh,
    IFactoryPresets factory, IPresetStore presetStore, IPresetLibrary presetLibrary, ISettingsService settings)
    : EngineTools(engine, dispatch, refresh)
{
    private readonly IFactoryPresets _factory = factory;
    private readonly IPresetStore _store = presetStore;
    private readonly IPresetLibrary _library = presetLibrary;
    private readonly ISettingsService _settings = settings;

    public sealed record FactoryPreset(string Id, string DisplayName, bool IsInstrument, int BuiltinKind, bool IsMidiEffect);
    public sealed record UserPreset(string Path, string DisplayName, string Type);

    [McpServerTool(Name = "list_factory_presets"), Description(
        "List the shipped factory presets (stable id, display name, whether it's an instrument or a MIDI effect, "
        + "and the parent built-in kind). Apply them with apply_factory_preset.")]
    public Task<FactoryPreset[]> ListFactoryPresets() => Read(() =>
    {
        var all = _factory.All();
        var list = new List<FactoryPreset>(all.Count);
        foreach (var p in all) list.Add(new FactoryPreset(p.Id, p.DisplayName, p.IsInstrument, p.BuiltinKind, p.IsMidiEffect));
        return list.ToArray();
    });

    [McpServerTool(Name = "apply_factory_preset"), Description(
        "Apply a factory preset by id. Instrument presets create a NEW track (targetTrackId ignored); "
        + "effect/MIDI presets are added to targetTrackId. Returns \"\" on success or a warning.")]
    public Task<string> ApplyFactoryPreset(string id, int targetTrackId = 0) => Mutate(() => _factory.Apply(E, id, targetTrackId));

    [McpServerTool(Name = "apply_factory_preset_in_place"), Description(
        "Apply a factory preset onto an EXISTING instrument/device (deviceIndex -1 = the track instrument). "
        + "Returns \"\" on success or a warning.")]
    public Task<string> ApplyFactoryPresetInPlace(string id, int trackId, int deviceIndex)
        => Mutate(() => _factory.ApplyInPlace(E, id, trackId, deviceIndex));

    [McpServerTool(Name = "list_user_presets"), Description("List the user's saved presets from the app presets folder (path, display name, type).")]
    public Task<UserPreset[]> ListUserPresets() => Read(() =>
    {
        var list = new List<UserPreset>();
        foreach (var p in _library.List(_settings.PresetsFolder())) list.Add(new UserPreset(p.Path, p.DisplayName, p.Type));
        return list.ToArray();
    });

    [McpServerTool(Name = "apply_user_preset"), Description("Load a saved preset file and apply it to a track. Returns \"\" on success or a warning.")]
    public Task<string> ApplyUserPreset(string path, int targetTrackId) => Mutate(() => _store.ApplyFromFile(E, path, targetTrackId));

    [McpServerTool(Name = "save_preset"), Description(
        "Capture a track's device/instrument (deviceIndex -1 = the instrument) as a named preset in the app presets folder. "
        + "Returns false if there was nothing to capture (e.g. a stateless built-in).")]
    public Task<bool> SavePreset(int trackId, int deviceIndex, string displayName)
        => Mutate(() => _store.Save(E, trackId, deviceIndex, displayName, _settings.PresetsFolder()));
}
