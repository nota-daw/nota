# Feature Specification: Update checks outside the start screen

**Feature Branch**: `033-update-checks`

**Created**: 2026-10-20

**Status**: Implemented

**Input**: User description: "Self-update is easy to miss: check outside the start screen and
add 'Check for updates…'" (nota-daw/nota#58). Users ask for a built-in updater because they
never see the one Nota has.

## Nota Context

**Feature area**: UI (Avalonia) — an app-wide update state, the status bar, the Help and macOS
app menus, Settings, the start screen banner. No engine change. Self-update itself
(`AppUpdater`: find the release, download, verify, swap in on quit) is unchanged; until now
it was only asked from the start screen, and its banner only showed if the launcher was still
open when the check came back, so turning the launcher off, opening a project directly or
keeping Nota open for days meant never hearing about a new version.

## Clarifications

### Session 2026-10-20

- Q: When does Nota check on its own? → A: On every launch (when the main window opens) and then once a day while it runs: a 30-minute poll asks whether 24 hours have passed since the last answered check, so a computer that slept through it checks soon after waking. A failed check doesn't count, so the next poll tries again.
- Q: Where does a new version show outside the launcher? → A: In the status bar, beside the version number: an accent dot and "Nota X is available". A click opens a menu: Update to Nota X (Download Nota X… for a copy that can't replace itself), Release notes, Not now. While downloading it shows a bar, the percentage and a cross to cancel; then "Restart to update to Nota X" (click restarts; it also installs on quit).
- Q: One download for the launcher and the status bar? → A: Yes. One app-wide state (`AppUpdates`) drives the start screen banner, the status bar, Settings → Updates and Check for updates…; "Not now" / Dismiss / Later in any of them hides the notice everywhere until a newer release turns up or the user checks again.
- Q: Check for updates… where? → A: Help menu (all platforms, the in-window menu bar on Windows / Linux) and the macOS Nota menu under About Nota; also in the command palette. It always answers: up to date, couldn't reach GitHub, or the update offer (Update / Later), or Restart now when one is already downloaded.
- Q: Settings? → A: A new Settings → Updates page: "Check for updates automatically" (on by default) and the state of this version with Check now / Update. A copy that can't replace itself says so. Background download (the issue's optional idea) is left out: nothing downloads until the user clicks Update.
- Q: Telling "up to date" from "couldn't check"? → A: `IAppUpdater.LastCheckFailed`, set when the feed couldn't be reached or read.

## Requirements

- **FR-001**: With automatic checks on, Nota MUST check on launch whether the start screen is shown or not, and again when 24 hours have passed since the last answered check.
- **FR-002**: With automatic checks off, Nota MUST NOT contact GitHub unless the user asks (Check for updates…, Check now).
- **FR-003**: A newer release MUST show in the main window's status bar until the user dismisses it, and in the start screen banner while it is open.
- **FR-004**: Check for updates… MUST say when the running version is the latest, and MUST say so differently when the check failed.
- **FR-005**: Nothing MUST download without a click on Update (or Try again).
- **FR-006**: A check MUST NOT run while an update downloads or waits to install (it would clear the updates folder).

## Out of scope

- Downloading in the background before the user asks.
- Release channels (pre-releases), and Linux packages other than the AppImage, which keep the release-page link.
