// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// MCP tools — the open project's version history: list versions (with what each changed),
// save a version with a note before an experiment, switch back to one, and name / note /
// star versions. Deleting versions is left to the user in the History tab.

using System.ComponentModel;
using ModelContextProtocol.Server;
using Nota.Application;

namespace Nota.Mcp.Tools;

[McpServerToolType]
public sealed class HistoryTools(
    IAudioEngine engine, IEngineDispatch dispatch, IArrangementRefresh refresh,
    IProjectVersionsAccess project, IProjectHistory history)
    : EngineTools(engine, dispatch, refresh)
{
    public sealed record VersionInfo(
        [property: Description("Version id, for switch_version / annotate_version")] string Id,
        [property: Description("The version this one was saved on top of (null for the first)")] string? Parent,
        [property: Description("When it was saved (ISO 8601, UTC)")] DateTimeOffset SavedAt,
        string? Label, string? Note, bool Starred,
        [property: Description("The project is at this version (its last save or switch)")] bool Current,
        [property: Description("False when a newer Nota saved it and this build can't open it")] bool CanOpen,
        [property: Description("What it changed against its parent: tracks added/removed/renamed, tempo, meter, "
                               + "tracks whose clips (edited), devices (sound) or mixer (mix) changed, new audio files")]
        VersionChanges? Changes);

    public sealed record VersionsResult(
        [property: Description("The open project's folder, or null if it was never saved (no history yet)")] string? ProjectPath,
        [property: Description("Settings → Version history; when off, saves record no versions")] bool HistoryEnabled,
        [property: Description("The project has edits made since its last save or switch")] bool UnsavedChanges,
        [property: Description("Oldest first; follow Parent links for branches")] VersionInfo[] Versions);

    [McpServerTool(Name = "list_versions"), Description(
        "List the open project's saved versions (its history tree): ids, parents, when, labels/notes, which one is "
        + "current, and what each changed. Every save records a version; saving on top of an older one branches.")]
    public async Task<VersionsResult> ListVersions()
    {
        var (path, enabled, dirty) = await Read(() => (project.ProjectPath, project.HistoryEnabled, project.HasUnsavedChanges));
        if (path is null) return new VersionsResult(null, enabled, dirty, []);
        var state = history.Read(path);
        return new VersionsResult(path, enabled, dirty, state.Versions.Select(v => Info(v, state.Head)).ToArray());
    }

    [McpServerTool(Name = "save_version"), Description(
        "Save the open project in place and record a version, with an optional note (\"before trying a new chorus\"). "
        + "Use it before risky edits so switch_version can bring the project back. If nothing changed since the current "
        + "version, the note is added to it. Fails for a project that was never saved (the user must pick a location in the app).")]
    public async Task<VersionInfo?> SaveVersion(
        [Description("What this version is or what changed; optional")] string? note = null)
    {
        var path = await Read(() => project.ProjectPath)
                   ?? throw new InvalidOperationException("The project has never been saved — ask the user to save it in Nota first.");
        if (!await await Read(() => project.SaveVersionAsync(string.IsNullOrWhiteSpace(note) ? null : note.Trim())))
            throw new InvalidOperationException("The project couldn't be saved (see Nota's status bar).");
        if (!await Read(() => project.HistoryEnabled)) return null;   // saved, but versions are off
        var state = history.Read(path);
        return state.HeadVersion is { } head ? Info(head, state.Head) : null;
    }

    [McpServerTool(Name = "switch_version"), Description(
        "Switch the open project to a saved version (by id from list_versions): its files are restored and the project "
        + "reopens (undo history starts fresh). Refuses while there are unsaved changes unless discardUnsavedChanges is "
        + "true — call save_version first to keep them as a version. Saving after switching to an older version branches.")]
    public async Task<VersionInfo> SwitchVersion(
        [Description("Version id from list_versions")] string versionId,
        [Description("Throw away edits made since the last save instead of refusing")] bool discardUnsavedChanges = false)
    {
        var (path, dirty) = await Read(() => (project.ProjectPath, project.HasUnsavedChanges));
        if (path is null) throw new InvalidOperationException("The project has no saved versions yet.");
        var target = history.Read(path).Versions.FirstOrDefault(v => v.Id == versionId)
                     ?? throw new ArgumentException($"No version '{versionId}' — see list_versions.");
        if (!target.CanOpen) throw new InvalidOperationException("That version was saved by a newer Nota and can't be opened here.");
        if (dirty && !discardUnsavedChanges)
            throw new InvalidOperationException("The project has unsaved changes — save_version first, or pass discardUnsavedChanges: true.");
        if (!await await Read(() => project.SwitchToVersionAsync(versionId)))
            throw new InvalidOperationException("Couldn't switch to that version (see Nota's status bar).");
        var state = history.Read(path);
        return Info(state.Versions.First(v => v.Id == versionId), state.Head);
    }

    [McpServerTool(Name = "annotate_version"), Description(
        "Name, note or star a version (by id). Pass only what should change; an empty string clears a label or note.")]
    public async Task<VersionInfo> AnnotateVersion(
        [Description("Version id from list_versions")] string versionId,
        [Description("New name; \"\" clears it")] string? label = null,
        [Description("New note; \"\" clears it")] string? note = null,
        [Description("Star (true) or unstar (false)")] bool? starred = null)
    {
        var path = await Read(() => project.ProjectPath) ?? throw new InvalidOperationException("The project has no saved versions yet.");
        if (history.Read(path).Versions.All(v => v.Id != versionId))
            throw new ArgumentException($"No version '{versionId}' — see list_versions.");
        if (label is not null) history.SetLabel(path, versionId, label);
        if (note is not null) history.SetNote(path, versionId, note);
        if (starred is { } s) history.SetStarred(path, versionId, s);
        await Read(() => { project.NotifyHistoryChanged(); return true; });   // the History tab redraws
        var state = history.Read(path);
        return Info(state.Versions.First(v => v.Id == versionId), state.Head);
    }

    private static VersionInfo Info(ProjectVersion v, string? head)
        => new(v.Id, v.Parent, v.CreatedAt, v.Label, v.Note, v.Starred, v.Id == head, v.CanOpen, v.Changes);
}
