// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Which groups are folded (collapsed). One state for the Arrangement and the Mixer
// (specs/070): folding a group in either view folds it in both. It is view state — never in
// the engine or undo — and is saved with the project in the track-view sidecar.

using System;
using System.Collections.Generic;

namespace Nota.App;

internal sealed class GroupFold
{
    private readonly HashSet<int> _folded = new();

    /// <summary>Raised once per call that changed the set.</summary>
    public event Action? Changed;

    public IReadOnlyCollection<int> Folded => _folded;

    public bool IsFolded(int groupId) => _folded.Contains(groupId);

    public void Toggle(int groupId)
    {
        if (!_folded.Remove(groupId)) _folded.Add(groupId);
        Changed?.Invoke();
    }

    public void Remove(int groupId)
    {
        if (_folded.Remove(groupId)) Changed?.Invoke();
    }

    public void Set(IEnumerable<int> groupIds)
    {
        var next = new HashSet<int>(groupIds);
        if (next.SetEquals(_folded)) return;
        _folded.Clear();
        _folded.UnionWith(next);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_folded.Count == 0) return;
        _folded.Clear();
        Changed?.Invoke();
    }
}
