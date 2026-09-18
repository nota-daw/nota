// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — built-in EQ (kind 0) body: the interactive response curve, which
// also runs the FFT analyzer on the UI tick.

using Avalonia.Controls;

namespace Nota.App;

internal sealed class EqDeviceBody : IDeviceBody
{
    public double Width => 700;   // the almanac device format: 700 × 260
    public string? Subtitle => "EQUALIZER";   // the processing type, shown as the header badge

    public Control Build(DeviceCardContext ctx, int deviceIndex)
    {
        var eq = new EqCurve(ctx.Engine, ctx.TrackId, deviceIndex);
        ctx.AddDeviceRefresher(eq.Tick);
        return eq;
    }
}
