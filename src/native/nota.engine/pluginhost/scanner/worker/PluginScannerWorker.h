// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginScannerWorker — out-of-process probe for one plugin id.
// Instantiates the requested format's type(s) for a fileOrIdentifier and
// returns their PluginDescriptions as XML bracketed by the
// <<<NOTA_PLUGINS_BEGIN>>>..<<<NOTA_PLUGINS_END>>> sentinels (some plugins
// print log lines to stdout while loading — the parent slices the bracketed
// text). A crash here is just a non-zero child exit; a hang is killed by the
// parent's timeout. May crash for a bad plugin — that's the point.

#pragma once

#include <string>

namespace nota {
namespace scanner {

// Probe one plugin; returns process exit code (0 = ok, 2 = usage, 3 = unknown format).
int runScannerWorker(int argc, char** argv);

} // namespace scanner
} // namespace nota
