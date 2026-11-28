// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// nota-scanworker — out-of-process plugin scanner entry point.
// Usage: nota-scanworker <formatName> <fileOrIdentifier>

#include "nota/pluginscanner/PluginScannerModule.h"

int main(int argc, char** argv) {
    return nota::pluginscanner::runScannerWorker(argc, argv);
}
