// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The searchable parameter picker behind a CV cable drop and Modular's "Modulate from…" items
// (specs/101, #656). A plug-in can have thousands of parameters, so the old menus grew taller
// than the screen; this is the list the panel shows instead: entries (optionally grouped under
// device / track headings), a case-insensitive filter, and a highlight that only ever sits on
// a pickable entry. Kept free of UI types so the filtering and keyboard rules are checked
// headlessly (tests/Nota.ModularUiTest).

using System;
using System.Collections.Generic;

namespace Nota.Presentation;

/// <summary>One row of the picker: a group heading (not pickable) or an entry carrying what to link.</summary>
public sealed record ParamPickerEntry(bool IsHeading, string Label, string? Group, object? Payload)
{
    public static ParamPickerEntry Heading(string text) => new(true, text, text, null);
    public static ParamPickerEntry Item(string label, object payload, string? group = null) => new(false, label, group, payload);
}

public sealed class ParamPickerModel
{
    /// <summary>Localisation key for "no entries at all".</summary>
    public const string NoParamsKey = "modular.picker_no_params";
    /// <summary>Localisation key for "the search filtered everything out".</summary>
    public const string NoMatchesKey = "modular.picker_no_matches";

    private readonly IReadOnlyList<ParamPickerEntry> _entries;
    private readonly bool _grouped;
    private readonly List<ParamPickerEntry> _visible = new();
    private string _query = "";
    private bool _anyItem;

    public ParamPickerModel(IReadOnlyList<ParamPickerEntry> entries, bool grouped = false)
    {
        _entries = entries;
        _grouped = grouped;
        foreach (var e in entries) if (!e.IsHeading) { _anyItem = true; break; }
        Refilter();
    }

    /// <summary>Called whenever <see cref="Visible"/> or <see cref="Highlight"/> changes.</summary>
    public event Action? Changed;

    public IReadOnlyList<ParamPickerEntry> Entries => _entries;
    public IReadOnlyList<ParamPickerEntry> Visible => _visible;
    public bool Grouped => _grouped;

    /// <summary>Rows a PageUp / PageDown moves by.</summary>
    public int PageSize { get; set; } = 12;

    /// <summary>Index into <see cref="Visible"/> of the highlighted entry; never a heading; −1 when nothing is pickable.</summary>
    public int Highlight { get; private set; } = -1;

    /// <summary>Overrides the text shown when the list has no entries at all (e.g. a cross-track
    /// drop on a track with nothing to modulate).</summary>
    public string? NoParamsText { get; init; }

    public string Query
    {
        get => _query;
        set
        {
            value ??= "";
            if (value == _query) return;
            _query = value;
            Refilter();
        }
    }

    /// <summary>The localisation key of the empty state, or null when there is something to show.</summary>
    public string? EmptyKey => Highlight >= 0 ? null : _anyItem ? NoMatchesKey : NoParamsKey;

    private void Refilter()
    {
        _visible.Clear();
        string q = _query.Trim();
        ParamPickerEntry? pendingHeading = null;
        foreach (var e in _entries)
        {
            if (e.IsHeading) { pendingHeading = e; continue; }
            bool match = q.Length == 0
                || e.Label.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (_grouped && e.Group is { } g && g.Contains(q, StringComparison.OrdinalIgnoreCase));
            if (!match) continue;
            // A heading shows only above at least one visible entry of its group.
            if (pendingHeading is not null) { _visible.Add(pendingHeading); pendingHeading = null; }
            _visible.Add(e);
        }
        Highlight = FirstItemFrom(0, +1);
        Changed?.Invoke();
    }

    private int FirstItemFrom(int start, int dir)
    {
        for (int i = start; i >= 0 && i < _visible.Count; i += dir)
            if (!_visible[i].IsHeading) return i;
        return -1;
    }

    /// <summary>Moves the highlight by <paramref name="delta"/> pickable entries (headings are
    /// skipped), clamped to the first / last entry.</summary>
    public void MoveHighlight(int delta)
    {
        if (Highlight < 0 || delta == 0) return;
        int dir = Math.Sign(delta), steps = Math.Abs(delta), at = Highlight;
        while (steps-- > 0)
        {
            int next = FirstItemFrom(at + dir, dir);
            if (next < 0) break;
            at = next;
        }
        if (at == Highlight) return;
        Highlight = at;
        Changed?.Invoke();
    }

    /// <summary>Highlights a visible row (hover); headings are ignored.</summary>
    public void SetHighlight(int visibleIndex)
    {
        if (visibleIndex < 0 || visibleIndex >= _visible.Count || _visible[visibleIndex].IsHeading || visibleIndex == Highlight) return;
        Highlight = visibleIndex;
        Changed?.Invoke();
    }

    /// <summary>The highlighted entry's payload, or null when nothing is pickable.</summary>
    public object? Pick() => Highlight >= 0 ? _visible[Highlight].Payload : null;

    /// <summary>A display label for a raw parameter name: "Parameter N" (1-based) when the name
    /// is empty, and a " (2)", " (3)" … suffix for a name already used in the same group, so
    /// every entry can be told apart — a generated label is never handed out twice, even against a
    /// raw name that looks like one. <paramref name="seenInGroup"/> is per group (case-insensitive).</summary>
    public static string Label(string? raw, int index, Dictionary<string, int> seenInGroup, Func<int, string> fallback)
    {
        string name = string.IsNullOrWhiteSpace(raw) ? fallback(index + 1) : raw.Trim();
        if (!seenInGroup.TryGetValue(name, out int n)) { seenInGroup[name] = 1; return name; }
        // Every label handed out is reserved — the suffixed ones too — so "Gain", "Gain",
        // "Gain (2)" read "Gain", "Gain (2)", "Gain (2) (2)": never two the same.
        int k = n + 1;
        while (seenInGroup.ContainsKey($"{name} ({k})")) k++;
        seenInGroup[name] = k;
        string label = $"{name} ({k})";
        seenInGroup[label] = 1;
        return label;
    }

    /// <summary>A fresh per-group "seen" set for <see cref="Label"/>.</summary>
    public static Dictionary<string, int> NewSeen() => new(StringComparer.OrdinalIgnoreCase);
}
