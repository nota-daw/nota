// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using Avalonia;
using Avalonia.Logging;

namespace Nota.App;

/// <summary>Drops known-benign Linux IME errors on the floor before they reach the log
/// sink, so a harmless D-Bus quirk doesn't fill the user's system log. Install with
/// <see cref="WithImeLogNoiseFiltered"/> right after <c>LogToTrace</c>.</summary>
public static class ImeLogNoiseFilter
{
    /// <summary>The log area Avalonia's FreeDesktop D-Bus IME code logs under.</summary>
    private const string ImeArea = "IME";

    private const string DestroyContextMessage = "Error while destroying the context:";

    /// <summary>Wraps whatever sink Avalonia is currently logging to so the benign IME
    /// dispose errors are filtered out; everything else passes through untouched.</summary>
    public static AppBuilder WithImeLogNoiseFiltered(this AppBuilder builder)
    {
        if (Logger.Sink is not null)
            Logger.Sink = new FilteringSink(Logger.Sink);
        return builder;
    }

    private sealed class FilteringSink : ILogSink
    {
        private readonly ILogSink _inner;

        public FilteringSink(ILogSink inner) => _inner = inner;

        public bool IsEnabled(LogEventLevel level, string area) => _inner.IsEnabled(level, area);

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate)
        {
            if (!IsBenignNoise(area, messageTemplate))
                _inner.Log(level, area, source, messageTemplate);
        }

        public void Log(LogEventLevel level, string area, object? source, string messageTemplate,
            params object?[] propertyValues)
        {
            if (!IsBenignNoise(area, messageTemplate))
                _inner.Log(level, area, source, messageTemplate, propertyValues);
        }

        // Some ibus builds don't implement Destroy on org.freedesktop.IBus.Service for
        // portal-created input contexts, replying UnknownMethod (or UnknownObject when the
        // context is already gone). Avalonia logs that failure at Error level every time a
        // popup, tooltip or focus change disposes its IME client (AvaloniaUI/Avalonia#15551,
        // nota-daw/nota#36), which floods stderr and with it the system log. The message
        // carries no diagnostic value, so it is dropped rather than forwarded.
        private static bool IsBenignNoise(string area, string? message) =>
            area == ImeArea
            && message is not null
            && message.StartsWith(DestroyContextMessage, StringComparison.Ordinal)
            && (message.Contains("org.freedesktop.DBus.Error.UnknownMethod", StringComparison.Ordinal)
                || message.Contains("org.freedesktop.DBus.Error.UnknownObject", StringComparison.Ordinal));
    }
}
