// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>One saved version of a project — a node of the history tree.</summary>
/// <param name="Parent">The version this one was saved on top of (null for the root).</param>
/// <param name="AddedBytes">Disk space this version added when it was saved (new audio,
/// plugin states and manifests no earlier version had).</param>
/// <param name="CanOpen">False when the version was saved by a newer Nota whose project
/// format this build can't read.</param>
public sealed record ProjectVersion(
    string Id, string? Parent, DateTimeOffset CreatedAt,
    string? Label, string? Note, bool Starred,
    string AppVersion, int ProjectFormat, long AddedBytes, bool CanOpen);

/// <summary>A project's history: every version (oldest first) and the one the working
/// copy was last saved as or switched to.</summary>
public sealed record ProjectHistoryState(IReadOnlyList<ProjectVersion> Versions, string? Head)
{
    public static readonly ProjectHistoryState Empty = new([], null);
    public ProjectVersion? HeadVersion => Versions.FirstOrDefault(v => v.Id == Head);
}

/// <summary>Disk use of a project: <see cref="Total"/> is the whole bundle's audio, states
/// and history; <see cref="HistoryOnly"/> is what only older versions need (freed by
/// erasing the history).</summary>
public readonly record struct ProjectHistorySize(long Total, long HistoryOnly);

/// <summary>History is unreadable (damaged, or from a newer Nota) or a version can't be
/// restored (its files are missing). The working copy is never touched when this is thrown.</summary>
public sealed class ProjectHistoryException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Version history of a <c>.nota</c> bundle, kept inside the bundle. A version is
/// recorded on each explicit save; switching restores a version's files into the bundle
/// (the caller then reopens the project). Saving on top of an older version branches.</summary>
public interface IProjectHistory
{
    /// <summary>All versions and the head. Empty when the bundle has no history yet.</summary>
    ProjectHistoryState Read(string bundleDir);

    /// <summary>Records the bundle's current (just saved) state as a child of the head.
    /// Returns null when nothing changed since the head.</summary>
    ProjectVersion? Commit(string bundleDir, string? note = null);

    /// <summary>Restores <paramref name="versionId"/>'s files into the bundle and makes it
    /// the head. The caller reloads the project afterwards.</summary>
    void Checkout(string bundleDir, string versionId);

    void SetLabel(string bundleDir, string versionId, string? label);
    void SetNote(string bundleDir, string versionId, string? note);
    void SetStarred(string bundleDir, string versionId, bool starred);

    /// <summary>Deletes a version (not the head); its children move up to its parent.
    /// Returns the bytes freed.</summary>
    long Delete(string bundleDir, string versionId);

    /// <summary>Deletes the whole history and every file only it needed.</summary>
    long Erase(string bundleDir);

    /// <summary>Carries the history, with every file its versions need, from one bundle to
    /// another (Save As). No-op when <paramref name="fromBundle"/> has no history.</summary>
    void CopyTo(string fromBundle, string toBundle);

    ProjectHistorySize Size(string bundleDir);
}
