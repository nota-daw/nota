// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/pluginscanner/PluginFormatRegistry.h"
#include "nota/pluginscanner/IPluginFormatBackend.h"
#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/PluginScannerModule.h"
#include "nota/pluginscanner/wire/ScanWireCodec.h"

#include "nota/core/ConfigFile.h"

#include <chrono>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <filesystem>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace {

#define NOTA_CHECK(cond)                                                                                               \
    do {                                                                                                               \
        if (!(cond)) {                                                                                                 \
            std::fprintf(stderr, "Check failed: %s (%s:%d)\n", #cond, __FILE__, __LINE__);                             \
            std::abort();                                                                                              \
        }                                                                                                              \
    } while (false)

class MockClapBackend final : public nota::pluginscanner::IPluginFormatBackend {
  public:
    explicit MockClapBackend(std::filesystem::path pluginDir, std::vector<std::string> existingBundles)
        : pluginDir_(std::move(pluginDir)), existing_(std::move(existingBundles)) {}

    void setExisting(std::vector<std::string> existingBundles) { existing_ = std::move(existingBundles); }

    nota::dsp::PluginFormatDescriptor descriptor() const override {
        return nota::dsp::PluginFormatDescriptor{
            nota::dsp::PluginFormatKind::Clap,
            "CLAP",
            "CLAP",
            true,
            true,
        };
    }

    bool hasDefaultScanPathBackfill() const override { return true; }

    std::vector<std::string> defaultSearchPaths() const override { return {pluginDir_.string()}; }

    std::vector<std::string> discoverCandidates(
        const std::vector<std::string>& searchPaths, const std::string& onlyDir) const override {
        (void)searchPaths;
        std::vector<std::string> out;
        if (!onlyDir.empty() && std::filesystem::path(onlyDir) != pluginDir_) return out;
        for (const auto& bundle : existing_) out.push_back(bundle);
        return out;
    }

    bool candidateExists(const std::string& candidateId) const override {
        for (const auto& bundle : existing_) {
            if (bundle == candidateId) return true;
        }
        return false;
    }

    int64_t candidateTimestampMs(const std::string& candidateId) const override {
        for (const auto& bundle : existing_) {
            if (bundle == candidateId) return 123456789LL;
        }
        return 0;
    }

    std::string candidateDisplayName(const std::string& candidateId) const override {
        return std::filesystem::path(candidateId).stem().string();
    }

    bool probeInWorker(
        const std::string& candidateId, std::vector<nota::dsp::PluginDescriptor>& outDescriptors) const override {
        nota::dsp::PluginDescriptor d;
        d.identifier = "CLAP-" + candidateDisplayName(candidateId) + "-deadbeef-1234";
        d.name = candidateDisplayName(candidateId);
        d.descriptiveName = d.name + " CLAP";
        d.formatName = "CLAP";
        d.format = nota::dsp::PluginFormatKind::Clap;
        d.category = "Synth|Analog";
        d.manufacturerName = "MockAudio";
        d.version = "1.0.0";
        d.fileOrIdentifier = candidateId;
        d.uniqueId = 0x1234;
        d.isInstrument = true;
        d.numInputChannels = 0;
        d.numOutputChannels = 2;
        outDescriptors.push_back(std::move(d));
        return true;
    }

  private:
    std::filesystem::path pluginDir_;
    std::vector<std::string> existing_;
};

std::filesystem::path makeTempDir(const char* tag) {
    const auto stamp = std::chrono::steady_clock::now().time_since_epoch().count();
    const auto dir = std::filesystem::temp_directory_path() / (std::string(tag) + "-" + std::to_string(stamp));
    std::filesystem::create_directories(dir);
    return dir;
}

void testMockFormatRegistryAndDiscovery() {
    const auto tmp = makeTempDir("nota-test-pluginscanner-reg");
    const std::string clapBundle = (tmp / "MockSynth.clap").string();
    auto mock = std::make_shared<MockClapBackend>(tmp, std::vector<std::string>{clapBundle});

    nota::pluginscanner::PluginFormatRegistry registry;
    registry.registerBackend(mock);

    const auto found = registry.find("CLAP");
    NOTA_CHECK(found != nullptr);
    NOTA_CHECK(found->descriptor().kind == nota::dsp::PluginFormatKind::Clap);
    NOTA_CHECK(found->candidateDisplayName(clapBundle) == "MockSynth");
    NOTA_CHECK(registry.orderedBackends().size() == 1);
    NOTA_CHECK(registry.listFormats().size() == 1);

    std::vector<nota::dsp::PluginDescriptor> probed;
    NOTA_CHECK(found->probeInWorker(clapBundle, probed));
    NOTA_CHECK(probed.size() == 1);
    NOTA_CHECK(probed[0].format == nota::dsp::PluginFormatKind::Clap);
    NOTA_CHECK(probed[0].identifier == "CLAP-MockSynth-deadbeef-1234");

    NOTA_CHECK(registry.unregisterBackend("CLAP"));
    NOTA_CHECK(registry.find("CLAP") == nullptr);
    std::filesystem::remove_all(tmp);
}

void testScanWireCodecRoundTripAndNoisyOutput() {
    nota::dsp::PluginDescriptor vst3;
    vst3.identifier = "VST3-Test Gain & EQ-aabbccdd-2a";
    vst3.name = "Test Gain & EQ";
    vst3.descriptiveName = "Test Gain & EQ \"Pro\"";
    vst3.formatName = "VST3";
    vst3.format = nota::dsp::PluginFormatKind::Vst3;
    vst3.category = "Fx|Dynamics";
    vst3.manufacturerName = "Nota & Co";
    vst3.version = "2.1";
    vst3.fileOrIdentifier = "/Library/Audio/Plug-Ins/VST3/TestGain.vst3";
    vst3.uniqueId = 0x2a;
    vst3.isInstrument = false;
    vst3.numInputChannels = 2;
    vst3.numOutputChannels = 2;
    vst3.serializedXml =
        "<PLUGIN name=\"Test Gain &amp; EQ\" format=\"VST3\" category=\"Fx|Dynamics\" "
        "manufacturer=\"Nota &amp; Co\" version=\"2.1\" file=\"/Library/Audio/Plug-Ins/VST3/TestGain.vst3\" "
        "uniqueId=\"2a\" isInstrument=\"0\" numInputs=\"2\" numOutputs=\"2\" isShell=\"0\" uid=\"0\" "
        "fileTime=\"18c4a00\" infoUpdateTime=\"18c4a01\" hasARAExtension=\"0\"/>";

    nota::dsp::PluginDescriptor clap;
    clap.identifier = "CLAP-Surge-custom-99";
    clap.name = "Surge";
    clap.descriptiveName = "Surge XT";
    clap.formatName = "CLAP";
    clap.format = nota::dsp::PluginFormatKind::Clap;
    clap.category = "Instrument|Synth";
    clap.manufacturerName = "Surge Synth Team";
    clap.version = "1.3.0";
    clap.fileOrIdentifier = "/tmp/Surge XT.clap";
    clap.uniqueId = 0x99;
    clap.isInstrument = true;
    clap.numOutputChannels = 2;

    const std::string framed = "noisy stdout line 1\nnoisy stdout line 2\n" +
                               nota::pluginscanner::encodeScanResults({vst3, clap}) + "\ntrailer\n";
    std::vector<nota::dsp::PluginDescriptor> decoded;
    NOTA_CHECK(nota::pluginscanner::decodeScanResults(framed, decoded));
    NOTA_CHECK(decoded.size() == 2);
    NOTA_CHECK(decoded[0].name == vst3.name);
    NOTA_CHECK(decoded[0].serializedXml == vst3.serializedXml);
    NOTA_CHECK(decoded[1].identifier == clap.identifier);
    NOTA_CHECK(decoded[1].format == nota::dsp::PluginFormatKind::Clap);
    NOTA_CHECK(decoded[1].isInstrument);
}

void testCatalogMigrationMissingCleanupAndScanPaths() {
    const auto tmp = makeTempDir("nota-test-pluginscanner-cat");
    const std::string clapBundle = (tmp / "MockClap.clap").string();
    const std::string legacyFile = "/nonexistent/legacy.vst3";

    // Write legacy JUCE KNOWNPLUGINS XML with arbitrary metadata to verify lossless preservation.
    const std::string legacyXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n\n"
        "<KNOWNPLUGINS>\n"
        "  <PLUGIN name=\"LegacySynth\" format=\"VST3\" category=\"Instrument|Synth\" manufacturer=\"LegacyCo\" "
        "version=\"1.0\" file=\"" +
        legacyFile +
        "\" uniqueId=\"42\" isInstrument=\"1\" numInputs=\"0\" numOutputs=\"2\" isShell=\"0\" "
        "uid=\"0\" fileTime=\"1234\" infoUpdateTime=\"5678\" hasARAExtension=\"1\"/>\n"
        "  <PLUGIN name=\"MockClap\" format=\"CLAP\" category=\"Synth\" manufacturer=\"MockAudio\" version=\"1.0\" "
        "file=\"" +
        clapBundle +
        "\" uniqueId=\"1234\" isInstrument=\"1\" numInputs=\"0\" numOutputs=\"2\" isShell=\"0\" uid=\"0\" "
        "notaId=\"CLAP-MockClap-deadbeef-1234\"/>\n"
        "</KNOWNPLUGINS>\n";
    NOTA_CHECK(nota::core::cfgfile::writeFileAtomic((tmp / "plugins.xml").string(), legacyXml));

    auto mockClap = std::make_shared<MockClapBackend>(tmp, std::vector<std::string>{clapBundle});
    nota::pluginscanner::PluginFormatRegistry registry;
    registry.registerBackend(mockClap);

    auto catalog = nota::pluginscanner::createCatalog(&registry, tmp.string());
    catalog->loadOnce();
    NOTA_CHECK(catalog->count() == 2);

    // Check legacy ID generation and lookup fallback across hash changes.
    const std::string legacyId = catalog->idAt(0);
    NOTA_CHECK(legacyId.starts_with("VST3-LegacySynth-"));
    NOTA_CHECK(legacyId.ends_with("-42"));
    NOTA_CHECK(catalog->indexOfId(legacyId.c_str()) == 0);
    NOTA_CHECK(catalog->indexOfId("VST3-LegacySynth-deadbeef-42") == 0);
    NOTA_CHECK(catalog->indexOfId("CLAP-MockClap-deadbeef-1234") == 1);

    nota::dsp::PluginDescriptor outLegacy;
    NOTA_CHECK(catalog->descriptorAt(0, outLegacy));
    NOTA_CHECK(outLegacy.serializedXml.find("hasARAExtension=\"1\"") != std::string::npos);

    // Backfill default scan paths from mock CLAP backend.
    catalog->loadScanPathsOnce();
    NOTA_CHECK(catalog->scanPathCount() == 1);
    NOTA_CHECK(catalog->scanPathAt(0) == tmp.string());

    // Simulate bundle deletion on disk for the mock backend and run backend-agnostic cleanup.
    mockClap->setExisting({});
    catalog->removeMissingByBackends();
    // Legacy VST3 has no registered backend in this test registry -> untouched; MockClap reported missing -> removed.
    NOTA_CHECK(catalog->count() == 1);
    NOTA_CHECK(catalog->idAt(0) == legacyId);

    // Failed tracking and save/reload round-trip.
    catalog->markFailed("CLAP", clapBundle);
    NOTA_CHECK(catalog->failedCount() == 1);
    catalog->save();
    catalog->saveFailed();

    auto reloaded = nota::pluginscanner::createCatalog(&registry, tmp.string());
    reloaded->loadOnce();
    reloaded->loadFailedOnce();
    NOTA_CHECK(reloaded->count() == 1);
    NOTA_CHECK(reloaded->failedCount() == 1);
    nota::dsp::PluginDescriptor reloadedLegacy;
    NOTA_CHECK(reloaded->descriptorAt(0, reloadedLegacy));
    NOTA_CHECK(reloadedLegacy.serializedXml.find("fileTime=\"1234\"") != std::string::npos);

    std::filesystem::remove_all(tmp);
}

void testBuiltinFormatRegistrationAndScannerState() {
    nota::pluginscanner::registerBuiltinFormatBackends();
    const auto formats = nota::pluginscanner::PluginFormatRegistry::instance().listFormats();
    bool hasVst3 = false;
    for (const auto& f : formats) {
        if (f.id == "VST3" && f.kind == nota::dsp::PluginFormatKind::Vst3) hasVst3 = true;
    }
    NOTA_CHECK(hasVst3);

    const auto tmp = makeTempDir("nota-test-pluginscanner-state");
    nota::pluginscanner::PluginFormatRegistry emptyReg;
    auto cat = nota::pluginscanner::createCatalog(&emptyReg, tmp.string());
    auto scanner = nota::pluginscanner::createScanner(cat, &emptyReg, "/nonexistent/worker");
    NOTA_CHECK(!scanner->isScanning());
    NOTA_CHECK(scanner->scanEx("", nullptr, 0) == -1);
    NOTA_CHECK(scanner->scan(false) == -2); // missing worker binary
    std::filesystem::remove_all(tmp);
}

} // namespace

int main() {
    testMockFormatRegistryAndDiscovery();
    testScanWireCodecRoundTripAndNoisyOutput();
    testCatalogMigrationMissingCleanupAndScanPaths();
    testBuiltinFormatRegistrationAndScannerState();
    return 0;
}
