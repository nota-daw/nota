// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// What key handlers ask (specs/096): "is this press action X?" against the user's bindings,
// instead of comparing Key and KeyModifiers by hand. The handlers keep their own focus gates
// and order; only the comparison moved here. Also the capture latch the Shortcuts editor
// holds while it records a key, and tooltips that name a key and follow it when it changes.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Nota.Application;
using Nota.Application.Input;

namespace Nota.App.Input;

public static class Keys
{
    private static KeyBindings? _fallback;

    /// <summary>The app's bindings; the shipped defaults where the app didn't create any
    /// (headless harnesses, tests).</summary>
    public static KeyBindings Bindings =>
        KeyBindings.Instance ?? (_fallback ??= new KeyBindings(KeyActionCatalog.Create(), new Settings()));

    /// <summary>The Shortcuts editor is recording a key: no window runs a shortcut meanwhile.</summary>
    public static bool Capturing { get; set; }

    // The press a handler last recognised as one of its actions — whether or not it then had
    // anything to act on. MainWindow's command fallback leaves such a press alone, so a key a
    // view owns (⇧↑ with no notes selected) never falls through to a command that does more.
    private static KeyEventArgs? _claimed;

    /// <summary>True when a handler on this press's route recognised it as one of its actions.</summary>
    public static bool Claimed(KeyEventArgs e) => ReferenceEquals(_claimed, e);

    /// <summary>True when the press is one of action <paramref name="id"/>'s keys.</summary>
    public static bool Is(KeyEventArgs e, string id)
    {
        if (KeyTokens.Chord(e) is not { } c || !Bindings.Matches(id, c)) return false;
        _claimed = e;
        return true;
    }

    /// <summary>True when the press is any of the actions' keys.</summary>
    public static bool IsAny(KeyEventArgs e, params string[] ids) => Which(e, ids) is not null;

    /// <summary>The first of <paramref name="ids"/> the press belongs to, or null.</summary>
    public static string? Which(KeyEventArgs e, params string[] ids)
    {
        if (KeyTokens.Chord(e) is not { } c) return null;
        foreach (var id in ids)
            if (Bindings.Matches(id, c)) { _claimed = e; return id; }
        return null;
    }

    /// <summary>The note-map action the press plays, or null.</summary>
    public static int NoteIndex(KeyEventArgs e)
    {
        if (KeyTokens.Chord(e) is not { } c) return -1;
        var notes = KeyActionCatalog.NoteKeys;
        for (int i = 0; i < notes.Length; i++) if (Bindings.Matches(notes[i].Id, c)) return i;
        return -1;
    }

    // ---- tooltips that name a key (#60, FR-022) ------------------------------------------

    private static readonly ConditionalWeakTable<Control, Tip> Tips = new();
    private static readonly List<WeakReference<Control>> TipControls = new();
    private static bool _listening;

    private sealed record Tip(string Text, string Id);

    /// <summary>Sets a tooltip whose text names action <paramref name="id"/>'s default key
    /// ("Record (⌘R)"); the key follows the user's binding and is spelled for this platform.</summary>
    public static void SetTip(Control c, string text, string id)
    {
        if (!Tips.TryGetValue(c, out _)) TipControls.Add(new WeakReference<Control>(c));
        Tips.AddOrUpdate(c, new Tip(text, id));
        ToolTip.SetTip(c, TipText(text, id));
        if (_listening) return;
        _listening = true;
        Bindings.Changed += RefreshTips;
    }

    /// <summary>The text with the action's default key swapped for its current key (dropped,
    /// with its brackets, when the action has none), spelled for this platform.</summary>
    public static string TipText(string text, string id)
    {
        if (Bindings.Get(id) is { } a && a.DefaultPrimary is { } d)
        {
            string def = d.ToGlyphs();
            if (Bindings.Shown(id) is { } now) text = text.Replace(def, now.ToGlyphs(), StringComparison.Ordinal);
            else text = text.Replace(" (" + def + ")", "", StringComparison.Ordinal).Replace(" (" + def, " (", StringComparison.Ordinal)
                .Replace(def, "", StringComparison.Ordinal);
        }
        return Shortcut.SpellText(text);
    }

    private static void RefreshTips()
    {
        TipControls.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var w in TipControls)
            if (w.TryGetTarget(out var c) && Tips.TryGetValue(c, out var t))
                ToolTip.SetTip(c, TipText(t.Text, t.Id));
    }
}
