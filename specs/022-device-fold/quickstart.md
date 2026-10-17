# Quickstart: verify device fold

1. `CI=true dotnet run --project tests/Nota.SmokeTest -c Release -- --fold` → `FOLD PASSED`.
2. Run the app, add three effects to a track. Double-click the first card's header (on the
   name, not the switch or the preset picker): it folds into a strip; the others move left.
3. Click the strip's switch: the device bypasses, the strip stays folded.
4. Drag the strip to the end of the chain; right-click it → Unfold.
5. Fold an effect and the instrument, save, reopen: both are still folded.
