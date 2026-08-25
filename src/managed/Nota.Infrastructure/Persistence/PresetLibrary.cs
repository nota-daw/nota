// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Infrastructure;

/// <summary>IPresetLibrary over PresetService, mapping the on-disk DTO to the
/// browser-facing PresetInfo so callers never see the persistence model.</summary>
public sealed class PresetLibrary : IPresetLibrary
{
    public IReadOnlyList<PresetInfo> List(string folder)
    {
        var list = new List<PresetInfo>();
        foreach (var (path, doc) in PresetService.List(folder))
            list.Add(new PresetInfo(path, doc.DisplayName, doc.Type, doc.DeviceName));
        return list;
    }
}
