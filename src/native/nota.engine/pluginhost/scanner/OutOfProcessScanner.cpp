// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner implementation. Mirrors scanOne() in the former
// PluginHost.cpp so the ABI move is behavior-preserving; adds workerAvailable().

#include "OutOfProcessScanner.h"

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace scanner {

OutOfProcessScanner::OutOfProcessScanner(std::string workerPath, int timeoutMs) noexcept
    : workerPath_(std::move(workerPath)), timeoutMs_(timeoutMs > 0 ? timeoutMs : kDefaultTimeoutMs) {}

bool OutOfProcessScanner::workerAvailable() const noexcept {
    if (workerPath_.empty()) return false;
    return juce::File(juce::CharPointer_UTF8(workerPath_.c_str())).existsAsFile();
}

std::vector<std::unique_ptr<juce::PluginDescription>> OutOfProcessScanner::scanDescriptions(
    const std::string& formatName, const std::string& fileOrId) const noexcept {
    std::vector<std::unique_ptr<juce::PluginDescription>> out;
    try {
        juce::ChildProcess proc;
        juce::StringArray cmd{juce::String::fromUTF8(workerPath_.c_str()), juce::String::fromUTF8(formatName.c_str()),
            juce::String::fromUTF8(fileOrId.c_str())};
        if (!proc.start(cmd, juce::ChildProcess::wantStdOut)) return out;

        juce::MemoryOutputStream captured;
        char buf[8192];
        const auto deadline = juce::Time::getMillisecondCounter() + (juce::uint32)timeoutMs_;
        while (proc.isRunning()) {
            const auto n = proc.readProcessOutput(buf, (int)sizeof(buf));
            if (n > 0) captured.write(buf, (size_t)n);
            if (juce::Time::getMillisecondCounter() > deadline) {
                proc.kill();
                return out;
            }
            if (n <= 0) juce::Thread::sleep(5);
        }
        for (;;) {
            const auto n = proc.readProcessOutput(buf, (int)sizeof(buf));
            if (n <= 0) break;
            captured.write(buf, (size_t)n);
        }

        const juce::String output = captured.toString();
        juce::String xmlText;
        const int b = output.indexOf("<<<NOTA_PLUGINS_BEGIN>>>");
        const int e = output.lastIndexOf("<<<NOTA_PLUGINS_END>>>");
        if (b >= 0 && e > b) {
            xmlText = output.substring(b + (int)juce::String("<<<NOTA_PLUGINS_BEGIN>>>").length(), e);
        } else {
            int x = output.indexOf("<?xml");
            if (x < 0) x = output.indexOf("<plugins");
            if (x >= 0) xmlText = output.substring(x);
        }
        auto xml = juce::parseXML(xmlText);
        if (xml == nullptr) return out;
        for (auto* child : xml->getChildIterator()) {
            auto desc = std::make_unique<juce::PluginDescription>();
            if (desc->loadFromXml(*child)) out.push_back(std::move(desc));
        }
    } catch (...) {
        // A crashing/hanging plugin must never unwind into the caller (AR-7:
        // no exceptions cross the C ABI; abi_pluginscanner is noexcept).
        out.clear();
    }
    return out;
}

} // namespace scanner
} // namespace nota
