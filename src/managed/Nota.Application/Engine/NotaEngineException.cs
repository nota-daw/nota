// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.

namespace Nota.Application;

/// <summary>Thrown when the native engine returns a non-OK result code. Declared in
/// Application so callers of <see cref="IAudioEngine"/> can catch it without a
/// dependency on Infrastructure.</summary>
public sealed class NotaEngineException(string message) : Exception(message);
