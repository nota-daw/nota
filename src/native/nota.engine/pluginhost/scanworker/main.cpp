// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// nota-scanworker — out-of-process plugin scanner (M3-1, FR-33/NFR-5).
//
// The host (PluginHost) launches this once per candidate plugin so a crashing
// or hanging plugin cannot take down the DAW: a crash here is just a non-zero
// child exit, a hang is killed by the parent's timeout. We instantiate the
// requested plugin's type(s) and print their PluginDescription(s) as XML to
// stdout; the parent parses that back into its KnownPluginList.
//
// Usage: nota-scanworker <formatName> <fileOrIdentifier>
//   formatName       e.g. "AudioUnit" or "VST3"
//   fileOrIdentifier the format-specific plugin id/path from searchPathsForPlugins

#include <juce_audio_processors/juce_audio_processors.h>

#include <iostream>

int main(int argc, char** argv) {
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
        if (format->getName() != formatName)
            continue;

        juce::OwnedArray<juce::PluginDescription> found;
        format->findAllTypesForFile(found, fileOrId); // may crash for a bad plugin — that's the point

        juce::XmlElement root("plugins");
        for (auto* desc : found)
            root.addChildElement(desc->createXml().release());

        // Bracket the XML with sentinels: some plugins (e.g. Maschine) write their
        // own log lines to stdout while loading, which would otherwise corrupt the
        // document the parent parses. The parent slices out the text between these.
        std::cout << "<<<NOTA_PLUGINS_BEGIN>>>\n"
                  << root.toString()
                  << "\n<<<NOTA_PLUGINS_END>>>" << std::endl;
        return 0;
    }

    std::cerr << "unknown format: " << formatName << "\n";
    return 3;
}
