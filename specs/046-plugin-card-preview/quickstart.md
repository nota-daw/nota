# Quickstart: validating the plug-in card preview

## Automated

```bash
scripts/build.sh
```

The smoke test's `PluginPreviewTests` covers the ABI round trip, duplicate and
copy/paste carrying the preview, clearing, save/load, and an old project without
previews.

## By hand (macOS, needs an installed AU/VST3 with an editor)

1. Start Nota, make an instrument track with a hosted plug-in (e.g. Massive), and add
   a hosted effect (e.g. Supercharger) after it.
2. Without opening anything: both cards look as they did before this change (Story 3).
3. Click **Open editor** on the effect, turn a visible knob, click **Close editor**.
   Within a second the S card's background is the window's picture, darkened, with text
   and buttons readable; the image isn't stretched (Story 1, SC-001, SC-007).
4. Open it again, change something, close it with the **window's own close button**.
   The card picks up the new look (FR-004).
5. Switch the card to **L**: the picture sits behind the info column only; the knob grid
   is unchanged.
6. Preferences → theme → Studio Light, then Ember Paper: the wash follows the theme and
   text stays readable (FR-003, SC-006).
7. Duplicate the effect (⌘D) and copy/paste it to another track: copies show the same
   picture (FR-008).
8. Save, close the project, reopen: previews are back without opening any window
   (Story 2).
9. During playback, open and close editors several times: no dropouts (SC-002).
10. Check the bundle size grows by ≤ ~100 KB per preview (SC-003).
