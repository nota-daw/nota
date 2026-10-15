// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Paces the Linux JUCE message pump (see App.axaml.cs): fast while hosted plugins
/// are posting messages, slow once the queue has stayed empty for a while — so a session
/// with no plugin (or a quiet one) isn't woken 100×/s for nothing (#55). One handled
/// message snaps it back to the fast rate.</summary>
public sealed class PumpPacer
{
    public static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(10);
    public static readonly TimeSpan Slow = TimeSpan.FromMilliseconds(100);
    /// <summary>Empty pumps in a row (at the fast rate) before backing off: ~0.25 s.</summary>
    public const int IdlePumpsBeforeSlow = 25;

    private int _idle;

    /// <summary>The current interval.</summary>
    public TimeSpan Interval { get; private set; } = Fast;

    /// <summary>Feed the number of messages the last pump handled; returns the next interval.</summary>
    public TimeSpan Next(int handled)
    {
        if (handled > 0) _idle = 0;
        else if (_idle < IdlePumpsBeforeSlow) _idle++;
        return Interval = _idle >= IdlePumpsBeforeSlow ? Slow : Fast;
    }
}
