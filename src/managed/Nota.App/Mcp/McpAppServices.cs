// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App-side glue for the MCP server (Nota.Mcp is Avalonia-free). AvaloniaEngineDispatch marshals
// tool calls onto the UI thread that owns the engine; ArrangementRefresh lets MCP edits redraw the
// arrangement/device panel; McpService owns the server's start/stop from the settings toggle.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;
using Nota.Mcp;

namespace Nota.App;

/// <summary>Runs engine access on Avalonia's UI thread for the MCP tools.</summary>
public sealed class AvaloniaEngineDispatch : IEngineDispatch
{
    public Task<T> InvokeAsync<T>(Func<T> func) => Dispatcher.UIThread.InvokeAsync(func).GetTask();
    public Task InvokeAsync(Action action) => Dispatcher.UIThread.InvokeAsync(action).GetTask();
}

/// <summary>Refresh hook the MCP tools raise after an edit; MainWindow points it at the arrangement
/// + device panel. Called on the UI thread (inside the dispatch), so it can touch controls directly.</summary>
public sealed class ArrangementRefresh : IArrangementRefresh
{
    public Action? OnRefresh { get; set; }
    public void Refresh() => OnRefresh?.Invoke();
}

/// <summary>Adapts the app's <see cref="MidiLearnService"/> to the Avalonia-free MCP facade so
/// the MIDI tools can inspect controllers and manage mappings. Called on the UI thread.</summary>
public sealed class MidiLearnAccess(MidiLearnService svc) : IMidiLearnAccess
{
    public bool Armed { get => svc.Armed; set => svc.Armed = value; }

    public IReadOnlyList<MidiMappingInfo> Mappings()
    {
        var src = svc.Mappings;
        var list = new List<MidiMappingInfo>(src.Count);
        for (int i = 0; i < src.Count; i++)
        {
            var m = src[i];
            list.Add(new MidiMappingInfo(i, m.DisplayName, m.SourceKind.ToString(), m.Channel, m.Number,
                m.RangeMin, m.RangeMax, m.Invert, m.SourceLabel));
        }
        return list;
    }

    public bool RemoveMapping(int index) => svc.RemoveMappingAt(index);
    public void ClearMappings() => svc.Clear();
    public bool SetMappingRange(int index, double min, double max, bool invert) => svc.SetMappingRange(index, min, max, invert);

    public IReadOnlyList<MidiControlSeen> RecentControls()
    {
        var src = svc.RecentControls();
        var list = new List<MidiControlSeen>(src.Count);
        foreach (var (isNote, ch, num, last, count) in src)
            list.Add(new MidiControlSeen(isNote ? "Note" : "CC", ch, num, last, count));
        return list;
    }

    public void ClearRecentControls() => svc.ClearRecentControls();
}

public sealed class ProjectKeyAccess(Nota.Presentation.TransportViewModel transport) : IProjectKeyAccess
{
    public int KeyCode { get => transport.KeyCode; set => transport.KeyCode = value; }
}

/// <summary>Hands the version-history tools the main window, which registers itself once it
/// exists (the MCP server may start first). Until then every action reports Nota isn't ready.</summary>
public sealed class ProjectVersionsBridge : IProjectVersionsAccess
{
    public IProjectVersionsAccess? Target { get; set; }
    private IProjectVersionsAccess T => Target ?? throw new InvalidOperationException("Nota's window isn't ready yet.");

    public string? ProjectPath => T.ProjectPath;
    public bool HasUnsavedChanges => T.HasUnsavedChanges;
    public bool HistoryEnabled => T.HistoryEnabled;
    public Task<bool> SaveVersionAsync(string? note) => T.SaveVersionAsync(note);
    public Task<bool> SwitchToVersionAsync(string versionId) => T.SwitchToVersionAsync(versionId);
    public void NotifyHistoryChanged() => Target?.NotifyHistoryChanged();
}

/// <summary>Starts/stops the loopback MCP HTTP server to match the current settings.</summary>
public sealed class McpService(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, ISettingsService settings, ILogSink log,
    IPluginCatalog pluginCatalog, IFactoryPresets factoryPresets, IPresetStore presetStore, IPresetLibrary presetLibrary,
    IAudioExporter exporter, IMidiDeviceService midiDevices, MidiLearnService midiLearn, IDrumKits drumKits,
    ProjectVersionsBridge projectVersions, IProjectHistory projectHistory, IModelStore models, IClipAi clipAi,
    ISampleIndex sampleIndex, Nota.Presentation.TransportViewModel transport, IMosaicPacks mosaicPacks,
    IProcessCpu processCpu, Nota.App.Controllers.ControllerService controllers, Nota.App.Controllers.ControlApplier applier)
{
    private readonly NotaMcpServer _server = new();

    public bool Running => _server.Running;
    public string? Url => _server.Url;

    // Extra app singletons the Phase 2/3 tool classes need by constructor injection.
    private void ShareServices(IServiceCollection s)
    {
        s.AddSingleton(settings);
        s.AddSingleton(pluginCatalog);
        s.AddSingleton(factoryPresets);
        s.AddSingleton(drumKits);
        s.AddSingleton(presetStore);
        s.AddSingleton(presetLibrary);
        s.AddSingleton(exporter);
        s.AddSingleton(midiDevices);
        s.AddSingleton<IMidiLearnAccess>(new MidiLearnAccess(midiLearn));
        s.AddSingleton<IControllerAccess>(new ControllerAccess(controllers, applier));
        s.AddSingleton<IProjectVersionsAccess>(projectVersions);
        s.AddSingleton(projectHistory);
        s.AddSingleton(models);
        s.AddSingleton(clipAi);
        s.AddSingleton(sampleIndex);
        s.AddSingleton(mosaicPacks);
        s.AddSingleton(processCpu);
        s.AddSingleton<IProjectKeyAccess>(new ProjectKeyAccess(transport));
        s.AddSingleton<IThemeAccess>(new ThemeAccess());
    }

    /// <summary>Reconcile the server with <see cref="Settings.McpEnabled"/> / <see cref="Settings.McpPort"/>.</summary>
    public async Task ApplyAsync()
    {
        var s = settings.Current;
        try
        {
            if (s.McpEnabled && !_server.Running)
            {
                await _server.StartAsync(engine, dispatch, refresh, s.McpPort, ShareServices);
                log.Info($"MCP server listening on {_server.Url} (loopback)");
            }
            else if (!s.McpEnabled && _server.Running)
            {
                await _server.StopAsync();
                log.Info("MCP server stopped");
            }
        }
        catch (Exception e)
        {
            log.Error($"MCP server: {e.Message}");
        }
    }
}

/// <summary>Adapts <see cref="Nota.App.Controllers.ControllerService"/> to the MCP facade
/// (specs/051). Called on the UI thread.</summary>
public sealed class ControllerAccess(Nota.App.Controllers.ControllerService svc, Nota.App.Controllers.ControlApplier applier) : IControllerAccess
{
    public IReadOnlyList<ControllerInfo> Controllers() => svc.Controllers
        .Where(c => c.Stage != Nota.App.Controllers.ControllerStage.Gone)
        .Select(c =>
        {
            var choice = svc.ChoiceFor(c.Uid);
            return new ControllerInfo(c.Uid, c.Name, c.OutputUid, c.Profile?.Id, c.Profile?.Name,
                c.Profile?.Protocol.ToString().ToLowerInvariant() ?? "none", c.Stage.ToString(), choice.Enabled,
                choice.PickupOn, c.State.BankOrigin, c.State.KnobMode.ToString(), c.State.Page);
        }).ToList();

    public bool SetController(string portUid, string? profileId, bool? enabled, bool? pickup)
    {
        // "none", the forced Mackie Control / HUI choices (as Preferences offers them) or a loaded profile.
        if (profileId is not null && profileId is not ("none" or Nota.App.Controllers.ControllerService.ForcedMcu or Nota.App.Controllers.ControllerService.ForcedHui)
            && svc.Profiles.All(p => p.Id != profileId)) return false;
        var choice = svc.ChoiceFor(portUid);
        svc.SetChoice(portUid, profileId, enabled ?? choice.Enabled, pickup);
        return true;
    }

    public IReadOnlyList<ControllerProfileInfo> Profiles() => svc.Profiles
        .Select(p => new ControllerProfileInfo(p.Id, p.Name, p.Origin.ToString().ToLowerInvariant(),
            p.Protocol.ToString().ToLowerInvariant(), p.Source ?? "")).ToList();

    public IReadOnlyList<string> ProfileWarnings() => svc.ProfileWarnings;
    public void ReloadProfiles() => svc.ReloadProfiles();
    public void Rescan() => svc.RescanNow();

    public bool StepDevice(int direction) => svc.StepDevice?.Invoke(Math.Sign(direction)) == true;

    public SessionSelectionInfo SessionSelection()
    {
        var (kind, track, scene) = svc.SessionSelection?.Invoke() ?? ("none", 0, 0);
        return kind switch
        {
            "slot" => new SessionSelectionInfo(kind, track, scene),
            "scene" => new SessionSelectionInfo(kind, null, scene),
            _ => new SessionSelectionInfo("none", null, null),
        };
    }

    public ControllerFocusInfo? Focus(string? portUid)
    {
        if (svc.Focused is not { } f) return null;
        var c = svc.Controllers.FirstOrDefault(x => x.Stage == Nota.App.Controllers.ControllerStage.Active
                                                     && (portUid is null ? x.HasKnobs : x.Uid == portUid));
        int page = Math.Clamp(c?.State.Page ?? 0, 0, Math.Max(0, f.PageCount - 1));
        var knobs = f.Knobs(page);
        var list = new ControllerKnobInfo[knobs.Length];
        for (int k = 0; k < knobs.Length; k++)
            list[k] = new ControllerKnobInfo(k + 1, knobs[k].Target is null ? null : knobs[k].Name,
                knobs[k].Target is { } t && Nota.App.Controllers.ControllerService.ToMidiTarget(t) is { } mt ? applier.CurrentNormalized(mt) : null);
        var a = f.Address;
        return new ControllerFocusInfo(a.TrackId, a.Section.ToString().ToLowerInvariant(), a.Index, a.Rack?.Chain, a.Rack?.Slot,
            f.DeviceName, f.TypeKey, (c?.State.KnobMode ?? Nota.Application.Controllers.KnobMode.Device).ToString(), page, f.PageCount, f.PageName(page), list);
    }
}
