# Implementation Plan: Delete several versions at once

**Branch**: `069-history-multi-select` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

## Summary

Switch the History tab's list to multiple selection and give a multiple selection its own
footer and context menu, both offering **Delete N versions…**. Deleting reuses
`IProjectHistory.DeleteMany`, which **Delete unnamed versions…** already uses, with one
confirmation.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: None. The History tab runs on the UI thread, and deleting versions
edits files in the project bundle the way the existing delete commands do.

**Testing**: `HistoryTests` already cover `DeleteMany` (a batch holding the current version is
refused whole; deleting a run links the survivors up and re-describes them); the design checks
scan the new labels and menu; manual check in the running app (Ctrl/Shift-click, the
selection's menu, the footer, the confirmation and the result).

## Design

- `HistoryView` — `_list` gets `SelectionMode.Multiple`. `Selected()` returns the selected
  rows in list order and `Single()` the one row of a single selection.
- `ShowSelection()` replaces the direct `ShowDetails` call. With more than one row selected,
  `ShowMany()` builds the footer: the count, the saved time span, "The current version
  stays." when the head is in the selection, and the delete button.
- `ContextRequested` — a row inside a multiple selection opens `BuildManyMenu()` (MenuKit
  header "N versions", a danger delete item); any other row is selected alone and opens its
  usual `BuildMenu()`.
- `DeleteSelected()` — a single version goes through the existing `Delete()`; several go
  through one `ConfirmWindow` and one `DeleteMany()`, then the status bar reports the bytes
  freed. The head is filtered out up front (`Deletable()`), so `DeleteMany` never refuses.
- `Refresh()` — keeps every selected version that still exists selected.

## Constitution Check

UI-only, no engine change, no dependency, MenuKit header, sentence-case labels, destructive
item in the danger ink.
