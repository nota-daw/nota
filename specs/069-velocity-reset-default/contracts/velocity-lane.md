# Contract: velocity lane gestures

The piano roll's lane, **Vel** tab only. Other tabs are unchanged.

| Gesture | Where | Effect |
|---|---|---|
| Click / drag a stem | a stem within reach | unchanged: sets the velocity where it lands; a selection moves by the drag |
| ⇧-click a stem | a stem | unchanged: adds it to the selection |
| **Double-click a stem** | a stem | resets the stem's note — or the whole selection when the stem belongs to it — to 0.8; one undo step with the first click |
| **Right-click a stem** | a stem | selects it as a press does, then opens a menu with **Reset velocity** |
| Right-click empty lane | no stem within reach | nothing |

## Menu

| Item | Enabled | Effect |
|---|---|---|
| **Reset velocity** | when at least one target is not already at 0.8 | same as the double-click |

Targets: the selection when the stem is selected, else the stem's note; locked and ghost notes never.

## Texts (English key → French)

| Key | French |
|---|---|
| `Reset velocity` | `Réinitialiser la vélocité` |
| `Double-click a velocity stem` / `Right-click a velocity stem` | rows of Preferences → Shortcuts → Piano roll |

(The two Shortcuts descriptions are written in the implementation, in the style of the neighbouring rows, and added to `fr.json`.)
