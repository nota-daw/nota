# Contract: footer (return / master) gestures and menu

| Where | Gesture | Result |
|-------|---------|--------|
| Bottom edge of a return / master row (header or lane, ±3 px band) | hover | vertical-resize cursor; the seam line lights with `Brush.BorderStrong` |
| same | drag | that row's height follows the pointer within [30, 256], snapping to 30 / 64 / 128; if the dragged return is selected along with other returns, they all take the height |
| same | Alt/Option + drag | same as a plain drag; footer rows never resize "all tracks" |
| same | double-click | back to Compact (30) |
| Return / master header | right-click | context menu adds a **Height** segmented row: Compact · Standard · Large, current one marked, no all-tracks variant |
| Track row edge | Alt + drag / "all tracks" preset | regular tracks only; footer heights unchanged |
| Return lane, automation mode | target pill | opens the same target menu as a track: Volume, Pan, devices on the return's chain (built-in, plugin, nested) |
| Return lane, automation mode | double-click / drag / right-click / Alt-drag / Shift-drag | add / move / remove or menu / bend / range select, exactly as on a track lane |
| Master lane, automation mode | click / drag / right-click / double-click | master-volume points as today, mapped to the row's current height |
| Footer, content taller than its cap | wheel / scrollbar | footer scrolls vertically; track list unaffected |
