// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Hosts the MCP tools over a loopback HTTP (Streamable HTTP / SSE) transport inside the running
// Nota process, so an MCP client (Claude Desktop / Claude Code) drives the live session. Bound to
// 127.0.0.1 only. The engine + UI-thread dispatch + refresh hook are handed in from Nota.App and
// shared into the Kestrel DI so the tool classes get them by constructor injection.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nota.Application;

namespace Nota.Mcp;

public sealed class NotaMcpServer : IAsyncDisposable
{
    private WebApplication? _app;

    public bool Running => _app is not null;
    public string? Url { get; private set; }

    /// <summary>Starts the loopback HTTP MCP server on <paramref name="port"/>. Idempotent.
    /// <paramref name="configureServices"/> shares extra app singletons (plugin catalog, presets,
    /// exporter, settings…) that tool classes need by constructor injection.</summary>
    public async Task StartAsync(IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh, int port,
        Action<IServiceCollection>? configureServices = null)
    {
        if (_app is not null) return;

        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();                    // don't spam the app's console
        builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(port));   // loopback only

        // Share the app's live singletons with the tool classes.
        builder.Services.AddSingleton(engine);
        builder.Services.AddSingleton(dispatch);
        builder.Services.AddSingleton(refresh);
        configureServices?.Invoke(builder.Services);

        builder.Services
            .AddMcpServer(o => { o.ServerInfo = new() { Name = "Nota", Version = "1" }; })
            .WithHttpTransport()
            .WithToolsFromAssembly();                        // discovers [McpServerToolType] in this assembly

        var app = builder.Build();
        app.MapMcp();                                        // Streamable HTTP endpoint at "/"

        await app.StartAsync();
        _app = app;
        Url = $"http://127.0.0.1:{port}";
    }

    public async Task StopAsync()
    {
        if (_app is { } a)
        {
            _app = null; Url = null;
            await a.StopAsync();
            await a.DisposeAsync();
        }
    }

    public async ValueTask DisposeAsync() => await StopAsync();
}
