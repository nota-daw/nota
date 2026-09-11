// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — maps a built-in device kind (or -1 for a hosted plugin) to the
// body strategy that renders it, replacing the switch that used to live in
// DeviceChainView.DeviceCard. Adding a new built-in effect body is a one-line
// registration here plus a new IDeviceBody — no switch to edit (open/closed).
//
// This lives in the App layer as a plain registry the view news up itself; it is not
// registered in the MS.DI container, matching how the views are wired today.

using System.Collections.Generic;

namespace Nota.App;

internal sealed class DeviceCardFactory
{
    private readonly IDeviceBody _plugin = new PluginDeviceBody();
    private readonly IDeviceBody _generic = new GenericParamDeviceBody();
    private readonly Dictionary<int, IDeviceBody> _byKind = new()
    {
        [0] = new EqDeviceBody(),
        [1] = new CompressorDeviceBody(),
        [2] = new ReverbDeviceBody(),
        [3] = new DelayDeviceBody(),
        [4] = new UtilityDeviceBody(),
        [6] = new AmpDeviceBody(),
        [7] = new AutoFilterDeviceBody(),
        [8] = new VintageDeviceBody(),
        [9] = new AutoPanDeviceBody(),
        [10] = new AutoShiftDeviceBody(),
        [11] = new BeatRepeatDeviceBody(),
        [12] = new CrushDeviceBody(),
        [13] = new DynamicEqDeviceBody(),
        [14] = new CeilingDeviceBody(),
        [15] = new StrataDeviceBody(),
        [16] = new Eq3DeviceBody(),
        [17] = new ForgeDeviceBody(),
        [18] = new AutoGainDeviceBody(),
        [19] = new ShutterDeviceBody(),
        [20] = new ChamberDeviceBody(),
    };

    /// <summary>Resolve the body for a device: -1 = hosted plugin, a mapped built-in kind,
    /// or the generic param-bar fallback for any built-in without a bespoke body.
    /// Note: the Audio Effect Rack (kind 5) is still handled inline by the view.</summary>
    public IDeviceBody Resolve(int builtinKind)
        => builtinKind < 0 ? _plugin
         : _byKind.TryGetValue(builtinKind, out var body) ? body
         : _generic;
}
