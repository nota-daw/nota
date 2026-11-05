# French glossary (draft — to be confirmed by the maintainers before the first French text)

Spec 068, task T002. One choice per term, so the whole interface reads the same
(FR-013). Copy the confirmed table into the `notes` of `Languages/fr.json`.

## Register

- Actions use the **infinitive**, as most French software does: "Enregistrer", "Supprimer
  la piste", "Ouvrir le projet".
- Explanatory text (tooltips, messages) avoids addressing the user directly where it can
  ("Le projet n'a pas été enregistré." rather than "Vous n'avez pas enregistré…"), so no
  tu/vous choice is needed string by string. Where a direct address is unavoidable, use
  **vous**.
- Sentence case, as in English (DESIGN.md § Labels and copy): "Nouvelle piste", not
  "Nouvelle Piste". No space-before-colon exception: French puts a thin non-breaking space
  before `:`, `;`, `?`, `!`; use U+202F (narrow no-break space) in the value.
- Ellipsis is `…` (as in English).

## Terms

| English | Français | Note |
|---|---|---|
| Project | Projet | |
| Track | Piste | "Audio track" → "Piste audio", "MIDI track" → "Piste MIDI" |
| Clip | Clip | kept, standard in French DAWs |
| Scene | Scène | Session view |
| Arrangement | Arrangement | view name |
| Session | Session | view name |
| Modular | Modulaire | view name |
| Mixer | Mixeur | |
| Browser | Navigateur | |
| Send | Envoi | |
| Return | Retour | |
| Master | Master | kept |
| Bus | Bus | kept |
| Group (tracks) | Groupe | |
| Rack | Rack | kept |
| Device | Appareil | the chain of devices on a track |
| Instrument | Instrument | |
| Effect | Effet | |
| Preset | Préréglage | |
| Take | Prise | take lanes / comping (spec 067) |
| Comp / comping | Compilation de prises | short label: "Comp" stays "Comp" if space is tight |
| Section | Section | |
| Automation | Automation | |
| Envelope | Enveloppe | |
| Loop | Boucle | |
| Metronome | Métronome | |
| Count-in | Décompte | |
| Punch | Punch | kept |
| Overdub / Replace (loop recording) | Superposer / Remplacer | action labels; "superposition" as a noun |
| MIDI learn | Apprentissage MIDI | |
| Pattern | Motif | |
| Seed (random) | Graine | |
| Quantize | Quantifier | action; noun "Quantification" |
| Snap / Snap grid | Magnétisme / Grille magnétique | |
| Warp | Warp | kept |
| Freeze | Geler | action; "Piste gelée" |
| Bounce | Rendre / rendu | an internal render into the project (a file is "Exporter"); "Bounce" kept in tight spaces |
| Mute / Solo / Arm | Muet / Solo / Armer | |
| Record | Enregistrer | the action of recording; **Save** is also "Enregistrer": context tag `record` vs `save` where both appear as the same English word |
| Save | Enregistrer | |
| Open | Ouvrir | |
| Undo / Redo | Annuler / Rétablir | |
| Cut / Copy / Paste | Couper / Copier / Coller | |
| Delete | Supprimer | |
| Duplicate | Dupliquer | |
| Rename | Renommer | |
| Preferences | Préférences | |
| Appearance | Apparence | |
| Colour theme | Thème de couleurs | |
| Interface scale | Échelle de l'interface | |
| Language | Langue | |
| Shortcuts | Raccourcis | |
| Plug-in | Plug-in | |
| Sample | Échantillon | |
| Tempo | Tempo | |
| CPU | CPU | not "processeur" |
| Fold (clip editor, piano roll, Session) | Replier | the Scale / Chord device's own "Fold" parameters keep their English names (FR-004) |
| Time signature | Signature rythmique | |
| Velocity | Vélocité | |
| Piano roll | Piano roll | kept |
| Waveform | Forme d'onde | |
| Meter | Vumètre | levels; "Niveau" for a level reading |
| Gain / Volume / Pan | Gain / Volume / Panoramique | |
| Cutoff, Attack, Reverb … | (unchanged) | device and parameter names stay English (FR-004) |

## Kept in English (never translated)

Device, effect and parameter names and their value labels; product and brand names
(Nota, Studio Dark, VST3, MIDI, CLAP, AU); unit symbols (dB, Hz, kHz, BPM, ms, %);
note names and bar/beat notation; keyboard key names as the keyboard spells them where the
platform shows them in English.
