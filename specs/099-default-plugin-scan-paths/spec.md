# Feature Specification: Default plugin scan paths

**Feature Branch**: `099-default-plugin-scan-paths`

**Created**: 2026-11-15

**Status**: Draft

**Input**: User description: "Default standardized VST3 scan paths per OS (extensible toward CLAP/LV2). Nota scans VST3 in the OS default locations implicitly, but Settings → Plug-ins lists only user-added folders plus the Nota store folder. Show the standardized OS VST3 locations as defaults alongside the store folder: listed always even if missing on disk; removable with a 'Restore defaults' action that re-adds the missing ones (OS + store) without touching user paths; existing installs backfilled once with no duplicates; no change to what the scan finds. Build it per plugin format as groundwork for CLAP/LV2 (no CLAP/LV2 scanning in this feature). Issue: nota-daw/nota#622 (approved)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Fresh install shows where VST3s come from (Priority: P1)

A new user opens Settings → Plug-ins and sees the standardized OS VST3
locations plus the Nota store folder already listed as defaults, without
adding anything. Each default is badged as a default.

**Why this priority**: This is the core complaint in #622 — a fresh
install gives no hint where VST3s are looked for.

**Independent Test**: Create a fresh profile, open Settings → Plug-ins,
and confirm the OS VST3 defaults plus the store folder are listed and
badged, with no user action.

**Acceptance Scenarios**:

1. **Given** a fresh profile, **When** the user opens Settings → Plug-ins, **Then** the OS-standard VST3 locations and the Nota store folder are listed as defaults.
2. **Given** a fresh profile on any supported OS, **When** the user compares the listed defaults to that OS's standardized VST3 locations, **Then** every standardized location is present.

---

### User Story 2 - Remove a default, then restore it (Priority: P1)

A user deletes a default they don't use (e.g. a location that doesn't
exist on their machine), then later uses "Restore defaults" to bring it
back. Folders they added themselves are never removed or duplicated by
either action.

**Why this priority**: Removability with a safe way back was the
explicit decision for this feature — defaults must not be a trap.

**Independent Test**: Remove one default, add one custom folder, run
"Restore defaults", and confirm the default is back while the custom
folder is untouched and nothing is duplicated.

**Acceptance Scenarios**:

1. **Given** the plug-in folder list, **When** the user removes a default folder, **Then** it disappears from the list and nothing else changes.
2. **Given** one default removed and one custom folder added, **When** the user runs "Restore defaults", **Then** the missing default returns, the custom folder stays, and no entry appears twice.
3. **Given** nothing missing, **When** the user runs "Restore defaults", **Then** the list is unchanged (no-op, no duplicates).

---

### User Story 3 - Existing install gains the defaults on upgrade (Priority: P2)

A user who installed Nota before this feature opens Settings → Plug-ins
after upgrading and finds the OS defaults plus the store folder added to
their existing list — once, without duplicates, keeping their custom
folders and order.

**Why this priority**: Backfill was explicitly requested so current
users benefit, not just fresh installs.

**Independent Test**: Start from a pre-feature folder list (custom
folders + store), upgrade, and confirm the defaults appear exactly once
with custom entries preserved.

**Acceptance Scenarios**:

1. **Given** a pre-feature folder list with custom entries, **When** the app starts after upgrade, **Then** every missing default is appended exactly once and custom entries are unchanged.
2. **Given** an already-backfilled list, **When** the app starts again, **Then** the list is unchanged (backfill does not duplicate).

---

### Edge Cases

- A listed default folder does not exist on disk: it is still shown (no filtering, no error).
- A default folder is unreadable (permissions): it stays listed; scanning simply finds nothing there.
- The user removes every default and runs restore: all defaults return, custom entries stay.
- The same folder is both a default and user-added: it appears once, badged as a default.
- A duplicated entry can never be created by backfill or restore.
- A removed default stays removed across restarts until restored or re-added by hand.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The plug-in folder list MUST include the OS-standardized VST3 locations as defaults on every supported OS.
- **FR-002**: The plug-in folder list MUST include the Nota store folder as a default.
- **FR-003**: Default folders MUST be listed even when they do not exist on disk or are unreadable.
- **FR-004**: The user MUST be able to remove any default folder from the list.
- **FR-005**: A "Restore defaults" action MUST re-add every missing default (OS locations and store folder) without removing or duplicating any user-added folder.
- **FR-006**: Upgrading with a pre-existing folder list MUST backfill every missing default exactly once, preserving custom entries.
- **FR-007**: Backfill and restore MUST be idempotent — running them twice MUST NOT create duplicates or reorder existing entries.
- **FR-008**: The scan MUST find the same plug-ins for the same on-disk state as before this feature (listing defaults changes nothing about what is searched).
- **FR-009**: The defaults mechanism MUST work per plug-in format, so a future format (CLAP, LV2) can supply its own defaults through the same list / remove / restore / backfill behavior with no change to that behavior.
- **FR-010**: This feature MUST NOT add CLAP or LV2 scanning — VST3 only.
- **FR-011**: Removing a default folder MUST persist across restarts: startup backfill and the store ensure MUST NOT re-add a removed default; only "Restore defaults" (or re-adding the folder by hand) brings it back.

### Key Entities

- **Default scan folder**: a folder Nota searches for plug-ins by default; has a plug-in format (VST3 today), a location, and an origin (OS standard vs. Nota store). Shown badged in Settings.
- **Scan folder list**: the ordered set of folders searched for plug-ins; mixes defaults and user-added folders, never contains duplicates.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A new user finds every standardized OS VST3 location plus the store folder in Settings → Plug-ins without adding anything (verified on each supported OS).
- **SC-002**: Remove-then-restore returns 100% of missing defaults while 100% of user-added folders survive unduplicated; a removed default stays removed across a restart until restored.
- **SC-003**: Upgrading a pre-feature install adds every missing default exactly once — zero duplicates across repeated starts.
- **SC-004**: A reviewer can add defaults for a new plug-in format using the same mechanism with no change to remove / restore / backfill behavior.

## Assumptions

- Each supported OS defines standardized VST3 locations that installers use; matching them is what "standard" means here.
- The Nota store folder always exists as a concept (it holds installed open-source plug-ins), even when empty.
- One shared folder list applies to all file-based plug-in formats, as today.
- The smoke test drives the engine end-to-end and is extended to cover fresh / backfill / remove / restore for VST3.
- A user-visible change gets a `CHANGELOG.md` `[Unreleased]` entry.
