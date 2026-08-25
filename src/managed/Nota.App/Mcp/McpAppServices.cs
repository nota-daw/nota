// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// App-side glue for the MCP server (Nota.Mcp is Avalonia-free). AvaloniaEngineDispatch marshals
// tool calls onto the UI thread that owns the engine; ArrangementRefresh lets MCP edits redraw the
// arrangement/device panel; McpService owns the server's start/stop from the settings toggle.

using System;
using System.Collections.Generic;
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

/// <summary>Starts/stops the loopback MCP HTTP server to match the current settings.</summary>
public sealed class McpService(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, ISettingsService settings, ILogSink log,
    IPluginCatalog pluginCatalog, IFactoryPresets factoryPresets, IPresetStore presetStore, IPresetLibrary presetLibrary,
    IAudioExporter exporter, IMidiDeviceService midiDevices, MidiLearnService midiLearn)
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
        s.AddSingleton(presetStore);
        s.AddSingleton(presetLibrary);
        s.AddSingleton(exporter);
        s.AddSingleton(midiDevices);
        s.AddSingleton<IMidiLearnAccess>(new MidiLearnAccess(midiLearn));
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
