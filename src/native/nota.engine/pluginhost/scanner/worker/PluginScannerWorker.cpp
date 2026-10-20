// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginScannerWorker implementation. JUCE lives only in this worker process
// and the hosting module — never in the core engine.

#include "PluginScannerWorker.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <iostream>

namespace nota {
namespace scanner {

int runScannerWorker(int argc, char** argv) {
    if (argc < 3) {
        std::cerr << "usage: nota-scanworker <formatName> <fileOrIdentifier>\n";
        return 2;
    }

    // AU/VST3 probing touches Core Foundation / message-loop machinery.
    juce::ScopedJuceInitialiser_GUI juceInit;

    const juce::String formatName{juce::CharPointer_UTF8(argv[1])};
    const juce::String fileOrId{juce::CharPointer_UTF8(argv[2])};

    juce::AudioPluginFormatManager formats;
    juce::addDefaultFormatsToManager(formats);

    for (auto* format : formats.getFormats()) {
        if (format->getName() != formatName) continue;

        juce::OwnedArray<juce::PluginDescription> found;
        format->findAllTypesForFile(found, fileOrId); // may crash for a bad plugin — that's the point

        juce::XmlElement root("plugins");
        for (auto* desc : found) root.addChildElement(desc->createXml().release());

        std::cout << "<<<NOTA_PLUGINS_BEGIN>>>\n" << root.toString() << "\n<<<NOTA_PLUGINS_END>>>" << std::endl;
        return 0;
    }

    std::cerr << "unknown format: " << formatName << "\n";
    return 3;
}

} // namespace scanner
} // namespace nota
