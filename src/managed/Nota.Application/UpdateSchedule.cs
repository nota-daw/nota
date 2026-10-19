// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>When Nota looks for a newer release on its own (#58): on every launch and then
/// once a day while it runs, unless Settings → Updates turned that off. A check the user asks
/// for (Help → Check for updates…) runs regardless.</summary>
public static class UpdateSchedule
{
    /// <summary>How long a running Nota waits between automatic checks.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);

    /// <summary>How often a running Nota asks <see cref="IsDue"/>. Short against the interval,
    /// so a computer that slept through the due time checks soon after it wakes.</summary>
    public static readonly TimeSpan Poll = TimeSpan.FromMinutes(30);

    /// <summary>True when an automatic check should run now: checking is on and the last
    /// successful check is <see cref="Interval"/> old (or there hasn't been one yet).</summary>
    public static bool IsDue(bool enabled, DateTime? lastChecked, DateTime now)
        => enabled && (lastChecked is not { } last || now - last >= Interval || now < last);
}
