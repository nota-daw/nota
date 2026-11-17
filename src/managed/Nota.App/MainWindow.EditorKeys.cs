// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The computer keyboard over a plug-in's editor window (#221). Those windows are native, so
// their keys never reach this window's handlers: the engine's editor windows catch the keys
// named here — the note map, octave and velocity, as the user bound them — and queue them,
// and each UI tick plays them the way OnKeyDown / OnKeyUp play the same keys here. Every
// other key, every chord and any typing into the plug-in's own fields stays with the plug-in.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Input;
using Nota.App.Input;
using Nota.Application.Input;

namespace Nota.App;

public partial class MainWindow
{
    private static readonly string[] TypingControlIds = { "notes.octaveDown", "notes.octaveUp", "notes.velocityDown", "notes.velocityUp" };
    private readonly int[] _editorKeyChars = new int[64];
    private readonly byte[] _editorKeyDown = new byte[64];
    private readonly Dictionary<Key, int> _editorHeldPitch = new();
    private readonly HashSet<Key> _editorHeld = new();

    private void InitEditorKeys()
    {
        if (_vm is null) return;
        PushEditorTypingKeys();
        Keys.Bindings.Changed += PushEditorTypingKeys;   // a re-bound key follows (specs/096)
        _vm.PlayheadUpdated += _ => DrainEditorKeys();
    }

    // The bare keys of the note map and the octave / velocity actions, as editor key codes —
    // every bare key a binding can name, F2 or an arrow as well as a letter.
    private void PushEditorTypingKeys()
    {
        var ids = KeyActionCatalog.NoteKeys.Select(n => n.Id).Concat(TypingControlIds);
        var chars = new List<int>();
        foreach (var id in ids)
            foreach (var slot in new[] { KeySlot.Primary, KeySlot.Alternative })
                if (Keys.Bindings.Current(id, slot) is { Modifiers: ShortcutModifiers.None, Key: { Length: > 0 } k }
                    && EditorKeyCodes.Of(k) is > 0 and var code)
                    chars.Add(code);
        Engine.SetEditorTypingKeys(chars.Distinct().ToArray());
    }

    private void DrainEditorKeys()
    {
        int n;
        while ((n = Engine.DrainEditorKeys(_editorKeyChars, _editorKeyDown)) > 0)
        {
            for (int i = 0; i < n; i++)
            {
                if (EditorKeyCodes.TokenOf(_editorKeyChars[i]) is not { } token) continue;
                var key = KeyTokens.ToKey(token);
                if (key == Key.None) continue;
                if (_editorKeyDown[i] != 0) EditorKeyDown(key);
                else EditorKeyUp(key);
            }
            if (n < _editorKeyChars.Length) break;
        }
    }

    // As HandleKeyDown's typing keys: a note at the current octave and velocity, or one step
    // of octave / velocity.
    private void EditorKeyDown(Key key)
    {
        if (!_editorHeld.Add(key)) return;
        var chord = KeyTokens.Chord(key, KeyModifiers.None, OperatingSystem.IsMacOS());
        if (chord is not { } c) return;
        switch (TypingControlIds.FirstOrDefault(id => Keys.Bindings.Matches(id, c)))
        {
            case "notes.octaveDown": ShiftTypingOctave(-1); return;
            case "notes.octaveUp": ShiftTypingOctave(+1); return;
            case "notes.velocityDown": ShiftTypingVelocity(-10); return;
            case "notes.velocityUp": ShiftTypingVelocity(+10); return;
        }
        var notes = KeyActionCatalog.NoteKeys;
        for (int i = 0; i < notes.Length; i++)
        {
            if (!Keys.Bindings.Matches(notes[i].Id, c)) continue;
            int pitch = Math.Clamp(TypingBasePitch + i + _typingOctave * 12, 0, 127);
            _editorHeldPitch[key] = pitch;   // released at this pitch even if the octave moves
            Engine.NoteOn(pitch, _typingVelocity / 127f);
            return;
        }
    }

    private void EditorKeyUp(Key key)
    {
        _editorHeld.Remove(key);
        if (_editorHeldPitch.Remove(key, out int pitch)) Engine.NoteOff(pitch);
    }
}
