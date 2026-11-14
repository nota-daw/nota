# Tasks: Nota Remote — choose the network interface for the link and QR

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Story 1 — pick the address (P1)

- [x] T001 `RemoteLink.Interface`, `RemoteLinks.Pick` in `src/managed/Nota.Remote/RemoteLinks.cs` (FR-001, FR-002)
- [x] T002 `Settings.RemoteInterface`; `RemoteService.Link`, `Url` from it, `ChooseInterface` (FR-003)
- [x] T003 **Address in the QR** in `PreferencesWindow.Remote.cs`, a down interface listed as not connected (FR-004)
- [x] T004 `RemoteTests`: Automatic, chosen, two addresses, down, no network

## Phase 2: User Stories 2 and 3 — the popup (P2)

- [x] T005 Clickable addresses and Automatic in `ConnectPhoneView` (FR-005)
- [x] T006 Open in the browser and Copy the link keys (FR-005)

## Phase 3: Polish

- [x] T007 `en.json` keys; `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T008 `Nota.sln` builds on Linux; the smoke test's `--remote` address checks and `--localization` run pass
