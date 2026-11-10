// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The user's articulation maps outside any project (specs/085, FR-008): what the manager lists and
// a track can be given. One map per file, addressed by its stable map id; projects embed their own
// copy, so a project opens without the library.

namespace Nota.Application.Articulations;

public interface IArticulationLibrary
{
    /// <summary>Every readable map, by name. A file that can't be read is skipped (and listed in
    /// <see cref="Unreadable"/>), never thrown.</summary>
    IReadOnlyList<ArticulationMap> List();

    /// <summary>Files the last <see cref="List"/> could not read.</summary>
    IReadOnlyList<string> Unreadable { get; }

    ArticulationMap? Get(string mapId);

    /// <summary>Adds or replaces the map with this id. Written atomically: a failure leaves the
    /// previous file as it was.</summary>
    void Save(ArticulationMap map);

    /// <summary>False when there was no such map.</summary>
    bool Delete(string mapId);
}
