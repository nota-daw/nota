// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

namespace Nota.Application;

/// <summary>Static build identity of the native engine (library version).
/// Captured once at composition and injected, so ViewModels need not reach for the
/// engine's static helpers.</summary>
public sealed record EngineBuildInfo(string Version);
