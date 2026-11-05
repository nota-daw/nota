# Data Model: Open the Detail panel on double-click

## Setting

| Field | Type | Default | Where | Notes |
|-------|------|---------|-------|-------|
| `OpenDetailOnSelect` | bool | `false` | `Nota.Application/Settings.cs` (user settings file) | `true` restores the pre-#308 behaviour: a selection opens the closed panel. Missing in old settings files → `false`. |

No project-file change: the panel state is per user, not per project.

## Detail panel visibility rules

| Trigger | Panel hidden, setting off | Panel hidden, setting on | Panel visible (docked / maximised) | Popped out |
|---------|---------------------------|--------------------------|------------------------------------|------------|
| Select track / clip / slot (click, keyboard, side effect) | stays hidden; content follows | opens as before #308 | follows as today | follows as today |
| Double-click track header (arrangement) | opens on Devices | opens on Devices | switches to Devices | window shows Devices |
| Double-click clip / filled Session slot | opens on Clip | opens on Clip | switches to Clip | window shows Clip |
| View → toggle / "Show devices" / tab button | opens | opens | as today | as today |
