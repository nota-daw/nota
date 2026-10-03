// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Hosts Nota Remote: Kestrel on every interface (a phone on the Wi-Fi must reach it — unlike the
// MCP server, which is loopback-only), serving the phone app from embedded resources and a
// WebSocket at /ws. Nothing on the computer's disk is served; the hub accepts nothing from a
// socket until it pairs or presents a device token.

using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Nota.Remote;

public sealed class RemoteServer : IAsyncDisposable
{
    private WebApplication? _app;
    private CancellationTokenSource? _cts;
    private static readonly Assembly Asm = typeof(RemoteServer).Assembly;

    public bool Running => _app is not null;
    public int Port { get; private set; }

    /// <summary>Listen on <paramref name="port"/> on all interfaces. Throws when the port is taken.</summary>
    public async Task StartAsync(RemoteHub hub, int port)
    {
        if (_app is not null) return;
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(k =>
        {
            k.Listen(IPAddress.Any, port);
            k.AddServerHeader = false;
        });
        var app = builder.Build();
        var cts = new CancellationTokenSource();

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(5) });
        app.Map("/ws", async (HttpContext ctx) =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
            using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
            string addr = ctx.Connection.RemoteIpAddress?.ToString() ?? "?";
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, ctx.RequestAborted);
            await hub.RunClientAsync(ws, addr, linked.Token);
        });
        // After /ws: everything else is the phone app. An explicit pattern, because the
        // parameterless MapFallback matches "{*path:nonfile}" only — every dotted path
        // (index.html, the JS, the fonts) would fall past the pipeline into a bare 404.
        app.MapFallback("/{**path}", ServeStaticAsync);

        await app.StartAsync();
        _app = app;
        _cts = cts;
        Port = port;
    }

    public async Task StopAsync()
    {
        if (_app is not { } a) return;
        _app = null;
        _cts?.Cancel();
        try { await a.StopAsync(TimeSpan.FromSeconds(1)); } catch { }
        await a.DisposeAsync();
        _cts?.Dispose();
        _cts = null;
    }

    public async ValueTask DisposeAsync() => await StopAsync();

    // ---- the phone app, from embedded resources ---------------------------------------

    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".webmanifest"] = "application/manifest+json",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".woff2"] = "font/woff2",
        [".mp4"] = "video/mp4",
        [".ico"] = "image/x-icon",
    };

    private static readonly Lazy<Dictionary<string, string>> Resources = new(() =>
        Asm.GetManifestResourceNames()
            .Where(n => n.StartsWith("wwwroot", StringComparison.Ordinal))
            .ToDictionary(n => n["wwwroot".Length..].Replace('\\', '/').TrimStart('/'), n => n, StringComparer.Ordinal));

    private static async Task ServeStaticAsync(HttpContext ctx)
    {
        if (!HttpMethods.IsGet(ctx.Request.Method) && !HttpMethods.IsHead(ctx.Request.Method)) { ctx.Response.StatusCode = 405; return; }
        string path = (ctx.Request.Path.Value ?? "/").TrimStart('/');
        if (path.Length == 0) path = "index.html";
        if (path.Contains("..")) { ctx.Response.StatusCode = 400; return; }

        if (!Resources.Value.TryGetValue(path, out var res))
        {
            if (path == "index.html") { await NotBuiltAsync(ctx); return; }
            // An unknown route is the app (it has none of its own), so a bookmarked deep link still opens.
            if (!path.Contains('.') && Resources.Value.TryGetValue("index.html", out var index)) res = index;
            else { ctx.Response.StatusCode = 404; return; }
            path = "index.html";
        }

        string ext = Path.GetExtension(path);
        ctx.Response.ContentType = Types.TryGetValue(ext, out var ct) ? ct : "application/octet-stream";
        // Vite fingerprints everything under assets/; the shell, the worker and the manifest
        // must be re-checked so an updated Nota serves its updated page.
        ctx.Response.Headers.CacheControl = path.StartsWith("assets/", StringComparison.Ordinal)
            ? "public, max-age=31536000, immutable" : "no-cache";
        ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
        if (path == "index.html") ctx.Response.Headers["Referrer-Policy"] = "no-referrer";

        await using var s = Asm.GetManifestResourceStream(res)!;
        ctx.Response.ContentLength = s.Length;
        if (HttpMethods.IsHead(ctx.Request.Method)) return;
        await s.CopyToAsync(ctx.Response.Body);
    }

    private static Task NotBuiltAsync(HttpContext ctx)
    {
        ctx.Response.ContentType = "text/html; charset=utf-8";
        return ctx.Response.WriteAsync("<!doctype html><meta name=viewport content='width=device-width'>" +
            "<body style='background:#0B0A09;color:#E9E4D8;font:15px system-ui;padding:28px'>" +
            "<h2>Nota Remote</h2><p>This build of Nota was made without the phone app (Node.js wasn't available). " +
            "Build src/web/remote and rebuild Nota.</p>");
    }
}
