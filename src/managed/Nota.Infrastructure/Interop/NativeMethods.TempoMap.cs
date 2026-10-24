// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Runtime.InteropServices;

namespace Nota.Infrastructure;

internal static partial class NativeMethods
{
    [LibraryImport("nota_engine", EntryPoint = "nota_transport_set_tempo_map")]
    internal static partial NotaResult SetTempoMap(IntPtr engine, IntPtr beats, IntPtr bpms, IntPtr curves, int count);

    [LibraryImport("nota_engine", EntryPoint = "nota_transport_get_tempo_map")]
    internal static partial int GetTempoMap(IntPtr engine, IntPtr beats, IntPtr bpms, IntPtr curves, int capacity);
}
