# Contract: PianoRollView ↔ host (MainWindow) and panels

In-process C# contract (no C ABI change). Existing members keep their meaning for a
one-clip set.

## New / changed on `PianoRollView`

```csharp
// Load a set (replaces SetNotes for arrangement clips). notes[i] are clip-local per clip.
void SetClips(ClipSet set, IReadOnlyList<ClipNotes> notes);
ClipSet? Clips { get; }
OpenClip? ActiveClip { get; }
event Action? ActiveClipChanged;          // header, inspector, envelope overlay rebind
void SetActiveClip(int slot);
void SetReference(int slot, bool on);      // refused for the active slot

// Commit paths. When CommitClips is set it is used instead of Commit; only clips whose
// notes changed since the last commit are passed (except the drag seed: all editable).
Action<IReadOnlyList<ClipNotes>>? CommitClips { get; set; }
Action<IReadOnlyList<ClipNotes>>? CommitClipsLive { get; set; }

// Clip tools (R6)
IReadOnlyList<ClipNotes> SourceClipNotes { get; }      // ignoring a preview in flight
IReadOnlyList<(int Slot, int Index)> SelectionByClip { get; } // clip-local indices
void ShowPreview(IReadOnlyList<ClipNotes> perClip);

// Coordinates
double Origin { get; }                     // arrangement beat of editor beat 0
// SetPlayhead and Seek take/give editor beats; host converts with Origin.
```

Unchanged: `Commit`, `CommitLive`, `SetNotes(notes, length)` stay for the Session-slot
editor (one clip, no `ClipSet` commit wiring).

## Host obligations (`MainWindow.Detail.cs`)

- `CommitClips`: `BeginUndoGroup`; for each entry `TryGetClipInfo(...).IsMidi` else drop
  the clip from the set; `SetClipNotes`; `EndUndoGroup` (in `finally`); `Timeline.Refresh()`.
- `CommitClipsLive`: same validation, `SetClipNotesLive` per entry, no group.
- Before replacing the set or saving/exporting: `SettlePlacement()` (as today).
- `OnTrackSelected` / `OnDetailClip`: no reset or re-point while `Clips.IsMulti`.

## `ArrangementView`

```csharp
// The selected MIDI clips, in time order; used by double-click and "Edit selected clips".
IReadOnlyList<(int TrackId, int ClipIndex)> SelectedMidiClips();
event Action<int, int>? MidiClipActivated;   // unchanged; host checks membership
```

## `NoteOverwrite` (Nota.Application/Editing)

```csharp
static void Settle(IReadOnlyList<NotaNote> notes, IReadOnlyList<int> placed,
                   List<NotaNote> outNotes, List<int> outPlaced,
                   IReadOnlyList<int>? owners = null, List<int>? outOwners = null);
static NoteFate Fate(int i, IReadOnlyList<NotaNote> notes, IReadOnlyList<int> placed,
                     IReadOnlyList<int>? owners = null);
```
`owners == null` ⇒ identical to today.
