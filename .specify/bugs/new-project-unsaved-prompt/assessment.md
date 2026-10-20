# Bug Assessment: New project (Ctrl+N) discards unsaved changes and leaves a blank detail panel

- **Slug**: new-project-unsaved-prompt
- **Created**: 2026-10-20
- **Source**: https://github.com/nota-daw/nota/issues/215 (host: github.com — allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

Issue #215 — "New project (Ctrl+N) discards unsaved changes without prompting and leaves a
blank detail panel". With unsaved edits and the detail panel open, Ctrl+N opens a new project
at once (edits lost) and the detail panel stays open but blank. Expected: a Save / Don't save /
Cancel prompt (Cancel keeps the current project), and after the switch the detail panel is
closed or shows valid content — never empty.

## Symptom

File → New replaces the project without the unsaved-changes prompt, losing work; afterwards
an empty strip remains where the docked detail panel was.

## Reproduction

1. Open a project, make an edit, don't save.
2. Open the detail panel (Devices or Clip tab).
3. Press Ctrl+N (Cmd+N on macOS).

## Suspected Code Paths

- `src/managed/Nota.App/MainWindow.Files.cs:OnMenuNew()` — resets the engine and applies the
  template with no `HasUnsavedChanges()` check.
- `src/managed/Nota.App/MainWindow.Files.cs:DoOpenAsync()` and
  `src/managed/Nota.App/MainWindow.Browser.cs` (`BrowserItemKind.Project` → `OpenProject`) —
  the same gap for File → Open and opening a project from the browser.
- `src/managed/Nota.App/MainWindow.History.cs:ConfirmLeaveChangesAsync()` — the existing
  Save / Don't save / Cancel helper, used only by version switching; it always saves in place,
  which for an untitled project would open the file panel under the dialog.
- `src/managed/Nota.App/MainWindow.Recovery.cs:ConfirmCloseAsync()` — the quit prompt, which
  handles the untitled case correctly (dialog closes first, then the file panel).
- `src/managed/Nota.App/MainWindow.Files.cs:RefreshAfterLoad()` — sets only
  `DetailPanel.IsVisible = false`; the `DetailSplitter` stays visible and `BodyGrid` row 2
  keeps its height (MinHeight 120), so a blank band remains. Compare `OnCloseDetail()` in
  `MainWindow.Detail.cs`, which also hides the splitter and collapses the row. With the panel
  popped out (`DetailWindow`), the old project's device chain / clip editor stay in the window.

## Root Cause Hypothesis

Confidence: high. The unsaved-changes guard was added for quit and version switching but never
wired into the New / Open entry points. The blank panel is an incomplete close in
`RefreshAfterLoad` (panel hidden, its grid row not collapsed), which also affects Open.

## Proposed Remediation

**Preferred**: Make `ConfirmLeaveChangesAsync` handle untitled projects the way the quit prompt
does (no in-place save; on Save, run `DoSaveAsync` after the dialog closes and go on only if
it wrote the bundle), and reuse it in `ConfirmCloseAsync`. Call it at the start of `OnMenuNew`
(made async), in `DoOpenAsync` after a folder is picked, and before opening a project from the
browser. In `RefreshAfterLoad`, use `ResetClipEditor()` and close the docked panel completely
(the `OnCloseDetail` body, factored into `CloseDetail()`); when popped out, point the window's
chain at the new project's instrument track instead of the old chain.

**Files likely to change**:
- `src/managed/Nota.App/MainWindow.Files.cs`
- `src/managed/Nota.App/MainWindow.History.cs`
- `src/managed/Nota.App/MainWindow.Recovery.cs`
- `src/managed/Nota.App/MainWindow.Browser.cs`
- `src/managed/Nota.App/MainWindow.Detail.cs`
- `CHANGELOG.md`

**Tests to add or update**:
- UI-level behaviour (dialog, grid row) isn't covered by the smoke test; verify manually /
  with the headless harness: New with unsaved edits → prompt; Cancel keeps the project; Don't
  save → new project, detail row collapsed.

## Risks & Considerations

- The welcome screen also calls `OnMenuNew`; the project is clean there, so no prompt appears.
- Crash-recovery restore (`ApplyPendingRecovery`) calls `OpenProject` directly and must not prompt — unchanged.
- MCP `history` tools already refuse on unsaved changes — unaffected.

## Open Questions

- None.
