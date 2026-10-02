// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Project version history in the window: every save records a version (Save As carries
// the history to the new bundle), "Save Version with Note…" attaches a note, and a version
// can be switched to in place or opened as a copy. Switching writes the version's files
// into the bundle and reopens it, so undo starts fresh — as after Open.

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Nota.Application;

namespace Nota.App;

public partial class MainWindow : IHistoryHost
{
    private IProjectHistory _history = default!;
    private HistoryView? _historyView;

    string? IHistoryHost.ProjectPath => _projectPath;
    bool IHistoryHost.KeepVersionHistory => KeepVersionHistory;
    Task<bool> IHistoryHost.SwitchToVersionAsync(string versionId) => SwitchToVersionAsync(versionId);
    Task<bool> IHistoryHost.OpenVersionAsCopyAsync(string versionId) => OpenVersionAsCopyAsync(versionId);
    void IHistoryHost.ReportStatus(string text) { if (_vm is not null) _vm.StatusText = text; }

    /// <summary>Builds the browser's History tab and keeps it in step with the project.</summary>
    private void SetUpHistoryTab()
    {
        _historyView = new HistoryView(_history, this);
        Browser.SetHistory(_historyView);
        HistoryChanged += _historyView.Refresh;
    }

    /// <summary>The open project's history changed (a version was recorded, switched to, or
    /// the project itself changed). The History tab listens.</summary>
    internal event Action? HistoryChanged;

    private bool KeepVersionHistory
        => App.Services.GetRequiredService<ISettingsService>().Current.KeepVersionHistory;

    // Called by the save after the manifest and every sidecar are written. Returns a
    // status-bar suffix when the version couldn't be recorded (the save itself stands).
    private string RecordVersion(string dir, string? previousPath, string? note)
    {
        if (!KeepVersionHistory) return "";
        try
        {
            // Save As: the new bundle gets the whole history and the audio its versions use.
            if (previousPath is not null && !SamePath(previousPath, dir)) _history.CopyTo(previousPath, dir);
            var version = _history.Commit(dir, note);
            // Nothing changed since the current version: the note goes onto that version.
            if (version is null && note is not null && _history.Read(dir).HeadVersion is { } head)
                _history.SetNote(dir, head.Id, head.Note is { Length: > 0 } old ? old + "\n" + note : note);
            HistoryChanged?.Invoke();
            return "";
        }
        catch (Exception ex) when (ex is ProjectHistoryException or IOException or UnauthorizedAccessException)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Recording a project version failed", ex);
            return $" · version not recorded: {ex.Message}";
        }
    }

    // Save As over an existing project replaces it, and its history with it — the new
    // project's own history (if any) is carried over after the save.
    private void DropReplacedHistory(string dir)
    {
        if (!Directory.Exists(dir)) return;
        try { _history.Erase(dir); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Clearing a replaced project's history failed", ex);
        }
    }

    private async Task SaveVersionWithNoteAsync()
    {
        if (_vm is null) return;
        if (!KeepVersionHistory)
        {
            _vm.StatusText = "Version history is turned off in Settings.";
            return;
        }
        var note = await new TextPromptWindow("Save version", "What changed in this version?")
            .ShowDialog<string?>(this);
        if (note is null) return;
        await DoSaveAsync(saveAs: false, note);
    }

    /// <summary>Switches the open project to <paramref name="versionId"/>: offers to save
    /// unsaved changes first, restores the version's files and reopens the bundle.</summary>
    internal async Task<bool> SwitchToVersionAsync(string versionId)
    {
        if (_vm is null || _projectPath is not { } dir) return false;
        if (!await ConfirmLeaveChangesAsync("Switch version",
                "Save your changes as a new version before switching?")) return false;
        try
        {
            _history.Checkout(dir, versionId);
        }
        catch (Exception ex) when (ex is ProjectHistoryException or IOException or UnauthorizedAccessException)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Switching project version failed", ex);
            _vm.StatusText = $"Couldn't switch version: {ex.Message}";
            return false;
        }
        OpenProject(dir);
        if (_history.Read(dir).HeadVersion is { } v) _vm.StatusText = $"Switched to {DescribeVersion(v)}";
        return true;
    }

    /// <summary>Opens <paramref name="versionId"/> as a new project next to this one — a full
    /// copy (history included) whose current version is the chosen one.</summary>
    internal async Task<bool> OpenVersionAsCopyAsync(string versionId)
    {
        if (_vm is null || _projectPath is not { } src) return false;
        if (!await ConfirmLeaveChangesAsync("Open version as copy",
                "Save your changes before opening the copy?")) return false;
        var version = _history.Read(src).Versions.FirstOrDefault(v => v.Id == versionId);
        if (version is null) return false;

        string suggested = $"{Path.GetFileNameWithoutExtension(src)} ({version.CreatedAt.LocalDateTime:yyyy-MM-dd HH.mm}).nota";
        var dst = await PickProjectPathAsync("Open version as a new project", suggested);
        if (dst is null) return false;
        if (SamePath(src, dst) || (Directory.Exists(dst) && Directory.EnumerateFileSystemEntries(dst).Any()))
        {
            _vm.StatusText = "Choose a new name for the copy — it can't replace an existing project.";
            return false;
        }
        try
        {
            _history.CopyTo(src, dst);
            _history.Checkout(dst, versionId);
        }
        catch (Exception ex) when (ex is ProjectHistoryException or IOException or UnauthorizedAccessException)
        {
            App.Services.GetRequiredService<ILogSink>().Error("Opening a project version as a copy failed", ex);
            _vm.StatusText = $"Couldn't open the version: {ex.Message}";
            return false;
        }
        OpenProject(dst);
        _vm.StatusText = $"Opened {DescribeVersion(version)} as {Path.GetFileName(dst)}";
        return true;
    }

    // Save / Don't Save / Cancel when leaving a project with unsaved edits. True to go on.
    private async Task<bool> ConfirmLeaveChangesAsync(string title, string message)
    {
        if (!HasUnsavedChanges()) return true;
        var choice = await new SaveChangesWindow(title, message, ProjectDisplayName(), UnsavedAgeText(),
                () => DoSaveAsync(saveAs: false))
            .ShowDialog<SaveChoice>(this);
        return choice != SaveChoice.Cancel;
    }

    // "the version “With bass” (12 Oct, 14:32)" / "the version of 12 Oct, 14:32".
    private static string DescribeVersion(ProjectVersion v)
    {
        string when = v.CreatedAt.LocalDateTime.ToString("d MMM, HH:mm", NotaNum.Culture);
        return v.Label is { Length: > 0 } label ? $"the version “{label}” ({when})" : $"the version of {when}";
    }

    private static bool SamePath(string a, string b)
        => string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                         Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                         OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
