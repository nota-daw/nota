// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — maps a track's instrument kind to its editor card: the built-in
// synths (Synth/Physical/Aurora/Volt) and the Sampler. The racks (Instrument Rack 3 /
// Drum Rack 4) are still built inline by the view.

using System.Collections.Generic;

namespace Nota.App;

internal sealed class InstrumentCardFactory
{
    private readonly IInstrumentCard _generic = new GenericInstrumentCard();
    private readonly IInstrumentCard _sampler = new SamplerInstrumentCard();
    private readonly Dictionary<int, IInstrumentCard> _byKind = new()
    {
        [0] = new SynthInstrumentCard(),
        [2] = new PhysicalInstrumentCard(),
        [5] = new AuroraInstrumentCard(),
        [6] = new VoltInstrumentCard(),
        [7] = new BassInstrumentCard(),
        [8] = new PendulumInstrumentCard(),
        [9] = new OperatorInstrumentCard(),
        [10] = new GrainInstrumentCard(),
        [11] = new FluxInstrumentCard(),
        [12] = new RhythmInstrumentCard(),
        [13] = new MonolithInstrumentCard(),
        [14] = new PentadInstrumentCard(),
        [15] = new ConsortInstrumentCard(),
    };

    /// <summary>Resolve the editor for a built-in instrument kind. The Sampler (1) always
    /// renders; the synths render only when the instrument exposes plugin-params;
    /// otherwise (and for any unmapped kind, including hosted plugins) the generic
    /// Open-GUI card is used.</summary>
    public IInstrumentCard Resolve(int instrumentKind, bool hasParams)
        => instrumentKind == 1 ? _sampler
         : hasParams && _byKind.TryGetValue(instrumentKind, out var card) ? card
         : _generic;
}
