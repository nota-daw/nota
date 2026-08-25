// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Detail · Devices — fallback body for any built-in device without a bespoke editor:
// a wrap grid of its generic params.

using Avalonia.Controls;

namespace Nota.App;

internal sealed class GenericParamDeviceBody : IDeviceBody
{
    public double Width => 190;

    public Control Build(DeviceCardContext ctx, int deviceIndex)
        => DeviceParamControls.ParamBars(ctx, deviceIndex);
}
