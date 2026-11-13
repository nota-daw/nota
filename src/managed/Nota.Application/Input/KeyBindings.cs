// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The user's keys (specs/096, #61): the defaults of KeyActionCatalog with the overrides from
// settings on top (only changed slots are stored, so new defaults still arrive with updates).
// Key handlers ask Matches(id, chord); the Shortcuts editor asks FindConflicts / FindShadows
// before it applies a change. The conflict rule (research R3): two actions answer one chord at
// the same time when they share a scope, both are window-layer, or either is carried by a
// menu item — unless both are their shipped defaults, which are curated and smoke-tested.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Nota.Application.Input;

/// <summary>An action slot that would answer the same chord.</summary>
public readonly record struct KeyUse(KeyAction Action, KeySlot Slot);

/// <summary>What capture makes of a pressed chord before it is proposed.</summary>
public enum KeyCheck { Ok, Reserved, Navigation, Fixed }

public sealed class KeyBindings
{
    private readonly Dictionary<string, KeyAction> _byId = new(StringComparer.Ordinal);
    private readonly List<KeyAction> _actions;
    private readonly Settings _settings;
    private readonly Action? _save;
    private readonly KeyPlatform _platform;
    private readonly Action<string>? _warn;
    // Effective keys per action: [0] primary, [1] alternative; null = none.
    private readonly Dictionary<string, Shortcut?[]> _current = new(StringComparer.Ordinal);
    private bool _warned;
    private readonly Dictionary<string, KeySlot> _needsDecision = new(StringComparer.Ordinal);

    public KeyBindings(IEnumerable<KeyAction> actions, Settings settings, Action? save = null, KeyPlatform? platform = null,
        Action<string>? warn = null)
    {
        _warn = warn;
        _actions = actions.ToList();
        foreach (var a in _actions) _byId[a.Id] = a;
        _settings = settings;
        _settings.KeyBindings ??= new();   // "KeyBindings": null in a hand-edited file
        _save = save;
        _platform = platform ?? ReservedKeys.Current;
        Rebuild();
    }

    /// <summary>The app's bindings, once the app has created them (null in tests that don't).</summary>
    public static KeyBindings? Instance { get; set; }

    public IReadOnlyList<KeyAction> Actions => _actions;
    public KeyAction? Get(string id) => _byId.GetValueOrDefault(id);
    public KeyPlatform Platform => _platform;

    /// <summary>Raised after every applied change (menus, tooltips and the palette re-read keys).</summary>
    public event Action? Changed;

    /// <summary>Defaults that a user key took over after an update — listed under "Needs a decision".</summary>
    public IReadOnlyCollection<string> NeedsDecision => _needsDecision.Keys;

    /// <summary>Which slot of a "Needs a decision" action lost its default.</summary>
    public KeySlot NeedsDecisionSlot(string id) => _needsDecision.GetValueOrDefault(id);

    /// <summary>Marks the actions a menu item carries (MainWindow reads them from its menu).</summary>
    public void SetMenuCommands(IEnumerable<string> ids)
    {
        var set = ids.ToHashSet(StringComparer.Ordinal);
        foreach (var a in _actions) a.IsMenuCommand = set.Contains(a.Id);
        Rebuild();
    }

    // ---- reading -----------------------------------------------------------------------

    public Shortcut? Current(string id, KeySlot slot = KeySlot.Primary) =>
        _current.TryGetValue(id, out var k) ? k[(int)slot] : null;

    /// <summary>The primary key, else the alternative — what a menu, tooltip or hint shows.</summary>
    public Shortcut? Shown(string id) => Current(id) ?? Current(id, KeySlot.Alternative);

    /// <summary>The shown key in glyph notation ("⌘T"), or "" when the action has none.</summary>
    public string Glyphs(string id) => Shown(id)?.ToGlyphs() ?? "";

    public bool Matches(string id, Shortcut chord) =>
        _current.TryGetValue(id, out var k) && (k[0] == chord || k[1] == chord);

    public bool IsEdited(string id) =>
        _byId.TryGetValue(id, out var a) && (Override(id, KeySlot.Primary, a) != a.DefaultPrimary
                                             || Override(id, KeySlot.Alternative, a) != a.DefaultAlternative);

    public bool AnyEdited => _actions.Any(a => IsEdited(a.Id));

    /// <summary>The action in <paramref name="scope"/> that answers the chord, if any.</summary>
    public KeyAction? Resolve(KeyScope scope, Shortcut chord) =>
        _actions.FirstOrDefault(a => a.Scope == scope && Matches(a.Id, chord));

    // ---- checking ----------------------------------------------------------------------

    /// <summary>Keys fixed for list navigation inside a scope (FR-011): a binding there would
    /// never fire, so capture refuses them.</summary>
    private static readonly Dictionary<KeyScope, Shortcut[]> NavigationKeys = new()
    {
        [KeyScope.Session] = [Shortcut.None("←"), Shortcut.None("→"), Shortcut.None("↑"), Shortcut.None("↓")],
        [KeyScope.Browser] = [Shortcut.None("Return"), Shortcut.None("↑"), Shortcut.None("↓")],
        [KeyScope.Modular] = [Shortcut.None("Space")],
    };

    public KeyCheck Check(string id, Shortcut chord)
    {
        if (_byId.TryGetValue(id, out var a) && a.Fixed) return KeyCheck.Fixed;
        if (ReservedKeys.Reason(chord, _platform) is not null) return KeyCheck.Reserved;
        if (a is not null && NavigationKeys.TryGetValue(a.Scope, out var nav) && nav.Contains(chord)) return KeyCheck.Navigation;
        // The Session grid walks its selection on an arrow with any modifier but ⌘ / ⌃.
        if (a?.Scope == KeyScope.Session && chord.Key is "←" or "→" or "↑" or "↓"
            && (chord.Modifiers & (ShortcutModifiers.Primary | ShortcutModifiers.Ctrl)) == 0) return KeyCheck.Navigation;
        return KeyCheck.Ok;
    }

    /// <summary>Every other action slot that would fire on <paramref name="chord"/> together with
    /// <paramref name="id"/> (rule R3).</summary>
    public List<KeyUse> FindConflicts(string id, KeySlot slot, Shortcut chord, bool exemptDefaults = true)
    {
        var result = new List<KeyUse>();
        if (!_byId.TryGetValue(id, out var a)) return result;
        bool aDefault = a.Default(slot) == chord;
        foreach (var b in _actions)
            for (int s = 0; s < 2; s++)
            {
                var bs = (KeySlot)s;
                if (b.Id == id && bs == slot) continue;
                if (Current(b.Id, bs) != chord) continue;
                if (b.Id == id) { result.Add(new(b, bs)); continue; }   // the same key in both slots
                if (!Overlap(a, b, chord)) continue;
                if (exemptDefaults && aDefault && b.Default(bs) == chord && Override(b.Id, bs, b) == chord) continue;
                result.Add(new(b, bs));
            }
        return result;
    }

    /// <summary>Other uses of the chord that don't conflict — another layer, where the focused
    /// view wins (FR-014). Shown as a note.</summary>
    public List<KeyUse> FindShadows(string id, Shortcut chord)
    {
        var result = new List<KeyUse>();
        if (!_byId.TryGetValue(id, out var a)) return result;
        foreach (var b in _actions)
        {
            if (b.Id == id) continue;
            for (int s = 0; s < 2; s++)
                if (Current(b.Id, (KeySlot)s) == chord && !Overlap(a, b, chord)
                    && a.Scope.IsWindowLayer() != b.Scope.IsWindowLayer())
                    result.Add(new(b, (KeySlot)s));
        }
        return result;
    }

    private static bool MenuChord(KeyAction a, Shortcut chord) => a.IsMenuCommand && !a.NoMenuKey && chord.CanMenuGesture;

    private static bool Overlap(KeyAction a, KeyAction b, Shortcut chord) =>
        a.Scope == b.Scope
        || a.Scope.IsWindowLayer() && b.Scope.IsWindowLayer()
        || MenuChord(a, chord) || MenuChord(b, chord);

    // ---- changing ----------------------------------------------------------------------

    /// <summary>Sets one slot (null chord = unassigned) and, for Replace, clears every slot in
    /// <paramref name="replace"/> — one change, saved once.</summary>
    public void Set(string id, KeySlot slot, Shortcut? chord, IEnumerable<KeyUse>? replace = null)
    {
        if (!_byId.ContainsKey(id)) return;
        foreach (var u in replace ?? []) Write(u.Action.Id, u.Slot, null);
        Write(id, slot, chord);
        Commit();
    }

    /// <summary>The conflicts that resetting <paramref name="id"/> to its defaults would cause.</summary>
    public List<KeyUse> ResetConflicts(string id)
    {
        var result = new List<KeyUse>();
        if (!_byId.TryGetValue(id, out var a)) return result;
        foreach (var slot in new[] { KeySlot.Primary, KeySlot.Alternative })
            if (a.Default(slot) is { } d && Current(id, slot) != d)
                result.AddRange(FindConflicts(id, slot, d).Where(u => u.Action.Id != id));
        return result;
    }

    public void Reset(string id, IEnumerable<KeyUse>? replace = null)
    {
        foreach (var u in replace ?? []) Write(u.Action.Id, u.Slot, null);
        _settings.KeyBindings.Remove(id);
        Commit();
    }

    public void ResetAll()
    {
        _settings.KeyBindings.Clear();
        Commit();
    }

    // Writes an override slot; a slot equal to its default is dropped, and an entry with both
    // slots on their defaults goes away (FR-020).
    private void Write(string id, KeySlot slot, Shortcut? chord)
    {
        var a = _byId[id];
        _settings.KeyBindings.TryGetValue(id, out var o);
        o ??= new KeyBindingOverride();
        string? text = chord == a.Default(slot) ? null : chord?.ToGlyphs() ?? "";
        if (slot == KeySlot.Primary) o.Primary = text; else o.Alternative = text;
        if (o.Primary is null && o.Alternative is null) _settings.KeyBindings.Remove(id);
        else _settings.KeyBindings[id] = o;
    }

    private void Commit()
    {
        Rebuild();
        _save?.Invoke();
        Changed?.Invoke();
    }

    // ---- effective map -----------------------------------------------------------------

    // The slot as the settings say: an override, else the default.
    private Shortcut? Override(string id, KeySlot slot, KeyAction a)
    {
        if (!_settings.KeyBindings.TryGetValue(id, out var o) || o is null) return a.Default(slot);
        string? text = slot == KeySlot.Primary ? o.Primary : o.Alternative;
        if (text is null) return a.Default(slot);
        if (text.Length == 0) return null;
        return TryParse(text, out var s) ? s : a.Default(slot);
    }

    public static bool TryParse(string glyphs, out Shortcut shortcut)
    {
        shortcut = Shortcut.Parse(glyphs);
        return shortcut.Key.Length > 0 && KeyActionCatalog.IsKnownToken(shortcut.Key);
    }

    private void Rebuild()
    {
        _current.Clear();
        _needsDecision.Clear();
        // Drop what can't be used: unknown ids and keys that don't parse (FR-021).
        if (!_warned) foreach (var (id, o) in _settings.KeyBindings.ToList())
        {
            if (o is null) { _warn?.Invoke($"Nota shortcuts: ignoring an empty entry for '{id}'"); continue; }
            if (!_byId.ContainsKey(id)) { _warn?.Invoke($"Nota shortcuts: ignoring unknown action '{id}'"); continue; }
            foreach (var t in new[] { o.Primary, o.Alternative })
                if (!string.IsNullOrEmpty(t) && !TryParse(t, out _))
                    _warn?.Invoke($"Nota shortcuts: ignoring unreadable key '{t}' for '{id}'");
        }
        _warned = true;
        foreach (var a in _actions)
            _current[a.Id] = [Override(a.Id, KeySlot.Primary, a), Override(a.Id, KeySlot.Alternative, a)];

        // A default that now collides with a user's key (a new default after an update, or a
        // hand-edited file): the user's key wins, the default rests until the user decides.
        var userKeys = new List<(KeyAction A, KeySlot S, Shortcut K)>();
        foreach (var a in _actions)
            if (_settings.KeyBindings.GetValueOrDefault(a.Id) is not null)
                for (int s = 0; s < 2; s++)
                    if (_current[a.Id][s] is { } k && k != a.Default((KeySlot)s)) userKeys.Add((a, (KeySlot)s, k));
        if (userKeys.Count == 0) return;
        foreach (var a in _actions)
            for (int s = 0; s < 2; s++)
            {
                if (_current[a.Id][s] is not { } k) continue;
                bool user = userKeys.Any(u => u.A == a && u.S == (KeySlot)s);
                foreach (var u in userKeys)
                {
                    if (u.A == a && u.S == (KeySlot)s) continue;
                    if (u.K != k || !Overlap(a, u.A, k)) continue;
                    // Two user keys on one chord: the first one listed keeps it.
                    if (user && userKeys.FindIndex(x => x.A == a && x.S == (KeySlot)s) < userKeys.IndexOf(u)) continue;
                    _current[a.Id][s] = null;
                    _needsDecision.TryAdd(a.Id, (KeySlot)s);
                    break;
                }
            }
    }
}
