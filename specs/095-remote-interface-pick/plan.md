# Implementation Plan: Nota Remote — choose the network interface for the link and QR

**Branch**: `095-remote-interface-pick` | **Date**: 2026-11-11 | **Spec**: [spec.md](spec.md)

## Summary

Links carry their interface's name; a pure `RemoteLinks.Pick` chooses the link for the QR from
the `RemoteInterface` setting. Settings → Remote gets an **Address in the QR** list; Connect
phone makes the other addresses clickable and adds open-in-browser and copy keys.

## Technical Context

**Language/Version**: C# / .NET 10, Avalonia.

**Primary Dependencies**: none new.

**Audio-Thread Impact**: none.

**Testing**: `RemoteTests` (`-- --remote` alone): Automatic, a chosen interface, an interface
with two addresses, a chosen interface that is down, no network.

## Design

- `RemoteLink` gains `Interface` (`NetworkInterface.Name`), filled by `RemoteLinks.All`.
- `RemoteLinks.Pick(links, chosen)`: the first link of `chosen`, else `links[0]`, else null.
- `Settings.RemoteInterface` (string?, null = Automatic).
- `RemoteService.Link` = `Pick(Links, RemoteInterface)`; `Url` (and so `QrText`) use it;
  `ChooseInterface(iface)` stores the setting, saves and raises `Changed`, which rebuilds the
  popup and refreshes the Settings page.
- `ConnectPhoneView`: the main address is `Link`'s, with the USB pill when it is the cable
  link and two glyph keys — `PopOut` (launch the URL through the main window's `Launcher`) and
  `Copy` (the main window's clipboard; a `Check` for 1.5 s). Every other link is a dim row with
  a hand cursor that chooses its interface; an **Automatic** row appears once a choice is set.
  The rebuild signature includes the choice.
- `PreferencesWindow.Remote`: an **Address in the QR** row under the port — a `Combo` of
  Automatic, one entry per interface and, when the chosen one is down, a disabled "not
  connected" entry (like Settings → Outputs' missing pairs). Rebuilt only when the addresses or
  the choice change, so an open list survives the page's other refreshes.

## Constitution Check

UI and settings only; no engine, ABI or format change. Texts through `Loc`.
