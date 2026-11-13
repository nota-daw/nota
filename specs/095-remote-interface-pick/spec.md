# Feature Specification: Nota Remote — choose the network interface for the link and QR

**Feature Branch**: `095-remote-interface-pick`

**Created**: 2026-11-11

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#85: "Connect a phone to Nota Remote on a machine with several
network interfaces (Wi-Fi + Ethernet, a VPN, a VM/Docker bridge, USB tethering, a second LAN),
where the address Nota picks for the QR code is not the one the phone can actually reach. I want
to choose which interface/address the link and QR are built from. I'd also like to open the
Remote page on the computer itself […] without retyping the address." Suggested: a
`RemoteInterface` setting (interface name or "auto"), remembered across restarts with a fallback
to Automatic; clickable addresses in Connect phone; an open-in-browser and a copy action; the
same picker in Settings → Remote.

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — Nota Remote's links, the app's Remote service, settings
- [x] UI (Avalonia) — Connect phone and Settings → Remote, follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: `RemoteServer` listens on every interface; only what Nota
shows is chosen. `RemoteLinks.All()` ranks the computer's IPv4 addresses (USB first, then
interfaces with a gateway, Wi-Fi before Ethernet) and drops some virtual adapters by name.
`RemoteService.Url` and `QrText` always take the first. Connect phone shows that address as
text, with the others dimmed under it; Settings → Remote lists them all on its status line.

## User Scenarios & Testing

### User Story 1 - Pick the address the phone can reach (Priority: P1)

A laptop on Wi-Fi also runs a VPN that wins the ranking. The user opens Settings → Remote and
picks the Wi-Fi interface under **Address in the QR**; the Connect phone QR and link now carry
the Wi-Fi address, and still do after a restart.

**Independent Test**: with links from two interfaces, choosing the second makes it the one in
the QR and the link; choosing Automatic returns to the first.

**Acceptance Scenarios**:

1. **Given** several interfaces, **Then** Settings → Remote lists Automatic and one entry per
   interface (its kind, first address and system name), the current choice selected.
2. **When** an interface is chosen, **Then** the link and the QR use its first address.
3. **Given** a chosen interface that is down, **Then** the link and QR use the Automatic
   address, and the list shows the interface as not connected; **when** it is back, it is used
   again.
4. **Given** a restart, **Then** the choice is kept.

### User Story 2 - Switch from the Connect phone popup (Priority: P2)

**Acceptance Scenarios**:

1. **Given** the popup with several addresses, **When** the user clicks one of the dim
   addresses under the main one, **Then** it becomes the address in the link and QR.
2. **Given** an address chosen by hand, **Then** an **Automatic** row under the others goes back
   to the best address.

### User Story 3 - Open or copy the link (Priority: P2)

**Acceptance Scenarios**:

1. **When** the user clicks **Open in the browser** beside the address, **Then** the default
   browser opens the Remote page at that address (it asks for the code, as a phone does).
2. **When** the user clicks **Copy the link**, **Then** the address is on the clipboard and the
   key shows a tick for a moment.

### Edge Cases

- **No network**: unchanged — the popup says the computer isn't on a network.
- **An interface with two IPv4 addresses**: its first address is used; the popup lists both,
  and clicking either chooses the interface.
- **Remote off**: the Address row is dimmed with the rest of the page, as the port is.
- **The opened page** carries no pairing code: opening Nota Remote on the computer does not
  trust the browser without the code.

## Requirements

### Functional Requirements

- **FR-001**: Every link MUST carry its interface's system name.
- **FR-002**: The link and QR MUST use the chosen interface's first address while it is up, and
  the Automatic address otherwise.
- **FR-003**: The choice MUST be a setting (`RemoteInterface`, null = Automatic), kept across
  restarts.
- **FR-004**: Settings → Remote MUST offer Automatic and every interface, and show a chosen
  interface that is down as not connected.
- **FR-005**: Connect phone MUST let the user choose another address with a click, go back to
  Automatic, open the link in the browser and copy it.

## Success Criteria

- **SC-001**: On a computer where the ranked address is unreachable from the phone, connecting
  takes one choice in Settings or one click in the popup, instead of typing an address by hand.

## Assumptions

- Interfaces are remembered by their system name, as the issue suggests; an address changes
  with DHCP, the interface name doesn't.
- The opened and copied link is the plain address; the code stays something the user enters.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header (no new source files)
- [x] No new dependency
- [x] Audio-thread impact: none — UI and settings only
- [x] No new managed/native crossings
- [x] Smoke-test impact: `RemoteTests` checks the address choice (`RemoteLinks.Pick`)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (existing rows, ComboBox, glyph keys; texts through `Loc`)
