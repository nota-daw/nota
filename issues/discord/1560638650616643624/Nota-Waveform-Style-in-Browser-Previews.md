**Apply the Waveform Style Preference to Browser Previews**

The Waveform setting under Settings → Appearance offers “Bars” and “Solid.” However, when Solid is selected, the waveform in the browser’s preview player still appears as separate bars.

The setting’s description currently refers specifically to audio clips in the Arrangement, so this may be its intended scope rather than a rendering bug. Nevertheless, I would expect the chosen waveform style to apply consistently to sample and preset-audio previews as well.

**Suggested improvement**
- Apply the selected Bars/Solid preference to the browser preview waveform.
- Update an already visible preview immediately when the preference changes.
- Use the selected style for subsequently loaded previews.
- Preserve the choice after restarting Nota.
- Review other waveform displays for consistency wherever the same visual preference is meaningful.

**If independent styles are useful**
Provide an explicit browser-preview override with “Follow global setting” as the default, rather than silently ignoring the preference.

**Expected behavior**
- Bars displays separated waveform bars.
- Solid displays a continuous waveform representation.
- Switching style changes presentation only, without altering the audio, playback position or preview volume.
- Keep the separate spectrum view unaffected.
- Preserve readability at different preview sizes, display scales and themes.

**Goal**
Make waveform appearance predictable across Nota and ensure the preference’s label and description clearly explain which views it affects.

Observed in Nota v0.33.1 on macOS, with Solid selected and the browser preview still using bars.
