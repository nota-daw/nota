// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Lightweight diagnostic log. Infrastructure's LogSink keeps an in-memory ring
// buffer plus a rolling on-disk file. Deliberately
// tiny — this is not a general logging framework, just enough context to make a
// a bug report actionable.

namespace Nota.Application;

public enum LogLevel { Info, Warn, Error }

/// <summary>App diagnostic log: a bounded ring buffer of recent events, mirrored to a
/// daily file. Thread-safe. Use the <see cref="LogSinkExtensions"/> helpers to write.</summary>
public interface ILogSink
{
    void Write(LogLevel level, string message);

    /// <summary>Recent log lines, oldest first, already formatted (timestamp + level).</summary>
    IReadOnlyList<string> Recent();

    /// <summary>The recent buffer as one newline-joined string.</summary>
    string Snapshot();
}

public static class LogSinkExtensions
{
    public static void Info(this ILogSink log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this ILogSink log, string message) => log.Write(LogLevel.Warn, message);

    public static void Error(this ILogSink log, string message, Exception? ex = null)
        => log.Write(LogLevel.Error, ex is null ? message : $"{message}\n{ex}");
}
