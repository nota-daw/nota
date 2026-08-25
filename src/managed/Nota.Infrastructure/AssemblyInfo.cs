// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

using System.Runtime.CompilerServices;

// All native entry points use [LibraryImport] (source-generated marshalling), and
// the engine contract structs they exchange (NotaNote, NotaTrackInfo, …) now live
// in the Application assembly. Disabling runtime marshalling lets the generator
// treat those cross-assembly blittable structs as blittable — the companion
// opt-in that LibraryImport is designed for.
[assembly: DisableRuntimeMarshalling]
