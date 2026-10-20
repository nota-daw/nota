// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginHost — in-process plugin hosting module . Owns the JUCE
// GUI lifetime for editor windows, the Linux message pump, and the catalog
// factories behind bridge/PluginHostBridge.h. Scanning and catalog storage
// live in scanner/ (abi_pluginscanner.cpp); adapters live in
// PluginInstrument.h / PluginEffect.h. This TU keeps only module lifetime,
// the test window, the pump, and the factories.

#pragma once

// Factory API is declared JUCE-free in bridge/PluginHostBridge.h; this header
// marks module ownership. Hosting TUs include the adapter headers directly.
#include "bridge/PluginHostBridge.h"
