// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Infrastructure;

/// <summary>IProjectStore over ProjectService — hides the on-disk document and the
/// capture/apply mechanics behind the use-case surface.</summary>
public sealed class ProjectStore : IProjectStore
{
    public IReadOnlyList<string> Save(IAudioEngine engine, TransportState transport, string dir)
    {
        var warnings = new List<string>();
        var doc = ProjectService.Capture(engine, transport, warnings);
        ProjectService.Save(doc, dir, engine);
        return warnings;
    }

    public ProjectLoadResult Load(IAudioEngine engine, string dir)
    {
        var doc = ProjectService.Load(dir);
        var warnings = ProjectService.Apply(doc, engine, dir);
        var transport = new TransportState(
            doc.Transport.Bpm, doc.Transport.MasterVolume,
            doc.Transport.MetronomeOn, doc.Transport.LoopOn,
            doc.Transport.TimeSigNumerator, doc.Transport.TimeSigDenominator);
        return new ProjectLoadResult(transport, warnings);
    }
}
