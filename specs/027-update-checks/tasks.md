# Tasks: Update checks outside the start screen

**Input**: [spec.md](spec.md)

- [x] T001 `IAppUpdater.LastCheckFailed`; `UpdateSchedule` (launch + daily, `IsDue`); `Settings.CheckForUpdates`
- [x] T002 `AppUpdates`: the app-wide state (check, download, dismiss, restart) started when the main window opens; cancelled on exit
- [x] T003 Start screen banner follows `AppUpdates` instead of running its own check
- [x] T004 Status bar notice (`MainWindow.Updates.cs`): available menu, download bar + cancel, Restart to update
- [x] T005 `help.checkUpdates` (Help menu, macOS app menu, palette) with up-to-date / failed / offer answers; `prefs.updates`
- [x] T006 Settings → Updates page (automatic checks switch, status, Check now / Update)
- [x] T007 Smoke tests (`--updater`): schedule, up to date vs failed check; CHANGELOG, FEATURES.md, nota-docs EN + RU
- [x] T008 Verified in the app against a local release feed (`NOTA_UPDATE_API`), `scripts/build-linux.sh` green; macOS check on the Mac
