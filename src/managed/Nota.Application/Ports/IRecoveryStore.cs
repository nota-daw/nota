// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Details of a recoverable snapshot from a crashed session.</summary>
public sealed record RecoveryInfo(string BundlePath, string? OriginalPath, string SavedAt);

/// <summary>Crash-recovery: a session marker + periodic autosave snapshot, offered
/// back after an unclean shutdown (M7-7).</summary>
public interface IRecoveryStore
{
    /// <summary>The recoverable snapshot if the previous session crashed, else null.
    /// Call BEFORE <see cref="BeginSession"/>.</summary>
    RecoveryInfo? PendingRecovery();
    /// <summary>Marks this session running (once at launch, after the crash check).</summary>
    void BeginSession();
    /// <summary>Clears the marker on a clean shutdown.</summary>
    void EndSessionClean();
    /// <summary>Removes the snapshot after the user restores or discards it.</summary>
    void ClearRecovery();
    /// <summary>Autosaves a snapshot if the project changed since the last one.</summary>
    void Autosave(IAudioEngine engine, TransportState transport, string? originalPath);
}
