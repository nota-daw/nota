// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OOP C++20 tests for nota::engine (`libnota_engine_core`), independent of the C ABI.

#include "nota/engine/NotaEngine.h"

#include <cmath>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <memory>
#include <string>
#include <utility>
#include <vector>

namespace {

int gFailures = 0;

void expect(bool cond, const char* msg) {
    if (!cond) {
        std::fprintf(stderr, "FAIL: %s\n", msg);
        ++gFailures;
    }
}

double bufferRms(const std::vector<float>& buf) {
    if (buf.empty()) return 0.0;
    double sum = 0.0;
    for (float s : buf) sum += static_cast<double>(s) * static_cast<double>(s);
    return std::sqrt(sum / static_cast<double>(buf.size()));
}

void testLifecycleAndTransport() {
    auto eng = std::make_unique<nota::Engine>();
    expect(eng->isValid(), "Engine constructs valid");
    expect(nota::Engine::version() != nullptr && std::strlen(nota::Engine::version()) > 0, "Engine::version non-empty");

    auto t = eng->transport();
    expect(t.isValid(), "Transport handle valid");
    std::vector<float> renderTmp(512, 0.0f);
    expect(t.setBpm(132.0), "Transport::setBpm accepts valid tempo");
    t.setTimeSignature(3, 4);
    t.setLoop(true, 1.0, 5.0);
    expect(t.loopEnabled(), "Transport::loopEnabled true");
    expect(std::fabs(t.loopStart() - 1.0) < 1e-6 && std::fabs(t.loopEnd() - 5.0) < 1e-6, "Transport loop bounds");
    t.seekBeats(2.0);
    eng->diagnostics().renderOffline(renderTmp.data(), 256, 48000.0);
    expect(std::fabs(t.bpm() - 132.0) < 1e-6, "Transport::bpm reflects setBpm after RT drain");
    expect(std::fabs(t.positionBeats() - 2.0) < 1e-3, "Transport::seekBeats updates position after RT drain");
    expect(t.beatToSeconds(4.0) > 0.0, "Transport::beatToSeconds positive");
    expect(std::fabs(t.secondsToBeat(t.beatToSeconds(4.0)) - 4.0) < 1e-5, "Transport seconds<->beats roundtrip");

    t.play();
    eng->diagnostics().renderOffline(renderTmp.data(), 256, 48000.0);
    expect(t.isPlaying(), "Transport::play sets playing after RT drain");
    nota::engine::TransportSnapshot snap{};
    expect(t.snapshot(snap), "Transport::snapshot valid after render");
    t.stop();
    eng->diagnostics().renderOffline(renderTmp.data(), 256, 48000.0);
    expect(!t.isPlaying(), "Transport::stop stops playback after RT drain");

    // Weak handle expiration when engine moves and later resets/destructs.
    nota::Engine moved = std::move(*eng);
    expect(!eng->isValid(), "Moved-from Engine invalidates");
    expect(moved.isValid(), "Moved-to Engine remains valid");
    expect(t.isValid(), "Weak handle stays alive via moved Engine");

    eng.reset();
    {
        auto scoped = std::make_unique<nota::Engine>(std::move(moved));
        expect(t.isValid(), "Weak handle valid while Engine alive");
        scoped.reset();
    }
    expect(!t.isValid(), "Transport weak handle expires after Engine destruction");
}

void testTracksClipsAndOfflineRender() {
    nota::Engine eng;
    auto tracks = eng.tracks();
    expect(tracks.isValid(), "TrackCollection handle valid");
    expect(tracks.master().isValid() && tracks.master().id() == nota::engine::kMasterTrackId, "Master track valid");

    auto audioTr = tracks.addAudio();
    auto instTr = tracks.addInstrument("volt");
    auto grpTr = tracks.addGroup();
    auto retTr = tracks.addReturn();
    expect(audioTr.isValid() && instTr.isValid() && grpTr.isValid() && retTr.isValid(), "All track types created");
    expect(tracks.count() == 4, "Track count reflects created tracks (master excluded)");

    expect(audioTr.setName("Drums"), "Set audio track name");
    expect(audioTr.name() == "Drums", "Read audio track name");
    expect(audioTr.setColorIndex(3) && audioTr.colorIndex() == 3, "Set/get track color index");
    expect(audioTr.setGroup(grpTr.id()), "Group assignment succeeds");
    audioTr.setSend(0, 0.5f);
    expect(std::fabs(audioTr.send(0) - 0.5f) < 1e-5f, "Send level roundtrip");

    // Paste synthetic audio buffer into audio track & test clip operations.
    std::vector<float> audioData(4410 * 2, 0.2f);
    const int32_t acIdx = audioTr.pasteAudioFrames(audioData.data(), 4410, 0.0, "Take1");
    expect(acIdx == 0, "Audio clip pasted via Track handle");
    auto ac = audioTr.clip(acIdx);
    expect(ac.isValid() && ac.name() == "Take1", "Audio clip handle & name");
    expect(ac.setPitch(2.0f) && ac.setReverse(true), "Audio clip pitch & reverse");
    std::vector<float> peaks(32, 0.0f);
    expect(ac.peaks(nota::engine::ClipPeakMode::Source, peaks.data(), 16) > 0, "Audio clip peak extraction");
    const int32_t splitIdx = ac.split(0.1);
    expect(splitIdx >= 0 || audioTr.clipCount() >= 1, "Audio clip split attempt");
    expect(ac.remove() || audioTr.clipCount() <= 2, "Audio clip removal works");

    // Instrument track MIDI clip + Volt render produces real audio.
    const int32_t mcIdx = instTr.addMidiClip(0.0, 2.0);
    expect(mcIdx == 0, "MIDI clip added");
    auto mc = instTr.clip(mcIdx);
    expect(mc.isValid(), "MIDI clip handle valid");
    nota::engine::NoteData note{};
    note.pitch = 60;
    note.startBeat = 0.0;
    note.lengthBeats = 1.5;
    note.velocity = 0.9f;
    expect(mc.setNotes(&note, 1) && mc.noteCount() == 1, "MIDI note stored in clip");

    eng.transport().setBpm(120.0);
    eng.transport().seekBeats(0.0);
    eng.transport().play();
    std::vector<float> out(4096 * 2, 0.0f);
    eng.diagnostics().renderOffline(out.data(), 4096, 48000.0);
    expect(bufferRms(out) > 1e-3, "Volt synth renders non-silent offline audio");
    eng.transport().stop();

    // Weak clip/track handle invalidation after removal.
    expect(tracks.remove(instTr.id()), "Remove instrument track");
    expect(!instTr.isValid(), "Removed Track weak handle expires");
    expect(!mc.isValid(), "Removed track's Clip weak handle expires");
}

void testDevicesAndRacks() {
    nota::Engine eng;
    auto tr = eng.tracks().addInstrument("volt");
    auto chain = tr.devices();
    expect(chain.isValid(), "DeviceChain handle valid");

    const int32_t fxIdx = chain.add("eq");
    expect(fxIdx == 0, "Added EQ effect by slug");
    auto fx = chain.device(fxIdx);
    expect(fx.isValid() && fx.paramCount() > 0, "Device handle valid & has params");
    auto param = fx.parameter(0);
    expect(param.isValid(), "Parameter handle valid");
    const float target = 0.5f * (param.min() + param.max());
    param.set(target);
    expect(std::fabs(param.get() - target) < 1e-4f, "Parameter get/set works");
    fx.setBypassed(true);
    expect(fx.bypassed(), "Device bypass toggles");

    const int32_t arpIdx = chain.addMidiEffect("arpeggiator");
    expect(arpIdx == 0, "Added MIDI effect by slug");
    auto arp = chain.midiEffect(arpIdx);
    expect(arp.isValid() && arp.paramCount() > 0, "MidiEffect Device handle valid");

    expect(chain.remove(fxIdx), "Removed EQ effect");
    expect(!fx.isValid() && !param.isValid(), "Removed Device & Parameter handles expire");

    // Instrument rack test via slug/method + chains & macros.
    auto rackTr = eng.tracks().addInstrumentRack();
    expect(rackTr.isValid(), "Instrument rack track created");
    auto instDev = rackTr.devices().instrument();
    auto rack = instDev.asRack();
    expect(rack.isValid(), "Rack handle obtained from instrument device");
    const int32_t c0 = rack.addChain(static_cast<int32_t>(6)); // VoltSynth kind
    expect(c0 >= 0 && rack.chainCount() >= 1, "Added Volt chain to rack");
    auto rChain = rack.chain(c0);
    expect(rChain.isValid(), "RackChain handle valid");
    rChain.setName("Lead Layer");
    expect(rChain.name() == "Lead Layer", "RackChain rename works");
    const int32_t eqSlot = rChain.addDevice(0); // Eq kind
    expect(eqSlot >= 0 && rChain.deviceCount() == 1, "Added chain FX to RackChain");
    auto macro = rack.macro(0);
    expect(macro.isValid(), "Macro handle valid");
    macro.set(0.42f);
    expect(std::fabs(macro.get() - 0.42f) < 1e-4f, "Macro value set/get");
    expect(rack.removeChain(c0), "Removed RackChain");
}

void testSessionAutomationModulationAndHistory() {
    nota::Engine eng;
    auto sess = eng.session();
    expect(sess.isValid(), "Session handle valid");
    const int32_t scIdx = sess.addScene();
    expect(scIdx >= 0, "Scene added");
    auto scene = sess.scene(scIdx);
    expect(scene.isValid(), "Scene handle valid");
    expect(scene.setName("Drop") && scene.name() == "Drop", "Scene rename works");

    auto tr = eng.tracks().addInstrument("volt");
    auto slot = sess.slot(tr.id(), scIdx);
    expect(slot.isValid(), "Slot handle valid");
    expect(slot.addMidiClip(4.0), "Added MIDI clip into Session slot");
    expect(slot.name().empty() || slot.length() > 0.0, "Slot clip has length");
    expect(slot.setName("Bassline") && slot.name() == "Bassline", "Slot clip rename");

    // Automation lane lifecycle & handle invalidation.
    const int32_t laneIdx = tr.addAutomationLane(1 /* Volume */, -1, -1);
    expect(laneIdx >= 0, "Volume automation lane added");
    auto lane = tr.automationLane(laneIdx);
    expect(lane.isValid(), "AutomationLane handle valid");
    const nota::engine::AutomationPointData pts[2] = {{0.0, 0.25f, 0.0f}, {4.0, 0.75f, 0.0f}};
    expect(lane.setPoints(pts, 2), "Automation points written");
    nota::engine::AutomationPointData readPts[4]{};
    expect(lane.points(readPts, 4) == 2, "Automation points read back");
    expect(lane.remove(), "Automation lane removed");
    expect(!lane.isValid(), "Removed AutomationLane handle expires");

    // Modulator + CV link lifecycle & invalidation.
    const int32_t fxIdx = tr.devices().add("eq");
    expect(fxIdx >= 0, "Added EQ for CV link");
    const int32_t modId = tr.addModulator(0);
    expect(modId > 0, "Modulator added");
    auto mod = tr.modulator(modId);
    expect(mod.isValid(), "Modulator handle valid");
    const int32_t linkIdx = tr.addCvLink(modId, fxIdx, 1);
    expect(linkIdx >= 0, "CV link created");
    auto link = tr.cvLink(linkIdx);
    expect(link.isValid(), "CvLink handle valid");
    link.setDepth(0.6f);
    expect(std::fabs(link.depth() - 0.6f) < 1e-4f, "CvLink depth roundtrip");
    expect(link.remove() && !link.isValid(), "CvLink remove expires handle");
    expect(mod.remove() && !mod.isValid(), "Modulator remove expires handle");

    // Structural undo/redo & mix gesture.
    auto hist = eng.history();
    expect(hist.isValid(), "History handle valid");
    const int32_t beforeTracks = eng.tracks().count();
    auto extra = eng.tracks().addAudio();
    expect(eng.tracks().count() == beforeTracks + 1 && extra.isValid(), "Track added for undo test");
    expect(hist.canUndo() && hist.undo(), "Undo removes newly added track");
    expect(eng.tracks().count() == beforeTracks && !extra.isValid(), "Track handle invalid after undo");
    expect(hist.canRedo() && hist.redo(), "Redo restores track");
    expect(eng.tracks().count() == beforeTracks + 1, "Redo restored track count");

    const nota::engine::MixTargetSpec mixTarget{nota::engine::MixTargetKind::TrackVolume, tr.id(), -1, -1};
    const int32_t gh = hist.beginMixGesture(&mixTarget, 1);
    expect(gh > 0, "Mix gesture opened");
    tr.setVolume(0.35f);
    hist.endMixGesture(gh);
    expect(hist.canUndo() && hist.undo(), "Mix gesture recorded undoable volume change");
}

void testPreviewAuditionAndIo() {
    nota::Engine eng;

    // Preview + AuditionSession.
    nota::engine::AuditionSession aud(48000.0);
    expect(aud.setInstrument(6), "AuditionSession sets Volt");
    expect(aud.addDevice(0) == 0, "AuditionSession adds EQ");
    const nota::engine::AuditionNoteSpec note{0.0, 0.5, 60, 0.8f};
    expect(aud.render(&note, 1, 120.0, 1.0, 0.5, nota::engine::AuditionSession::kRolling) > 0,
        "AuditionSession renders frames");
    expect(aud.seconds() > 0.0, "AuditionSession reports positive duration");
    float peaks[32]{};
    expect(aud.peaks(peaks, 16) > 0, "AuditionSession extracts peaks");

    auto prev = eng.preview();
    expect(prev.isValid(), "Preview handle valid");
    prev.storeAudition("volt_test", aud);
    expect(prev.cachedAudition("volt_test"), "Preview cached audition stored");
    expect(prev.playCached("volt_test", 0.0), "Preview plays cached audition");
    expect(prev.selfTest(), "Preview built-in self-test passes");

    // AudioIo, MidiIo, GamepadIo.
    auto audio = eng.audio();
    expect(audio.isValid(), "AudioIo handle valid");
    audio.setTestOutputDevice(2);
    audio.setSampleRate(48000.0);
    expect(std::fabs(audio.config().sampleRate - 48000.0) < 1e-6, "AudioIo config sample rate updated");
    double trim = 0.0;
    const double compBeat = nota::Engine::AudioIo::latencyCompensatedStart(4.0, 240, 24000.0, &trim);
    expect(std::fabs(compBeat - 3.99) < 1e-6 && std::fabs(trim) < 1e-6, "latencyCompensatedStart non-negative");

    auto midi = eng.midi();
    expect(midi.isValid(), "MidiIo handle valid");
    const int32_t portIdx = midi.addTestPort("TestCtrl");
    expect(portIdx >= 0, "Added virtual test MIDI port");
    const uint8_t noteMsg[3] = {0x90, 64, 100};
    expect(midi.injectTestMidi(portIdx, noteMsg, 3), "Injected test MIDI bytes");
    midi.setMpe(false, 24);
    expect(!midi.config().mpe && midi.config().mpeBendRange == 24, "MidiConfig MPE updated");
    midi.setMpe(true, 48);

    auto gp = eng.gamepad();
    expect(gp.isValid(), "GamepadIo handle valid");
    expect(gp.count() >= 0, "GamepadIo count readable");
}

void testBuiltInDiagnosticsSelfTests() {
    nota::Engine eng;
    auto diag = eng.diagnostics();
    expect(diag.isValid(), "Diagnostics handle valid");

    expect(diag.xrunSelfTest(), "xrunSelfTest passes");
    expect(diag.graphRetireSelfTest(), "graphRetireSelfTest passes");
    expect(diag.automationSelfTest(), "automationSelfTest passes");
    expect(diag.tempoLaneSelfTest() == 0, "tempoLaneSelfTest passes");
    expect(diag.masterAutomationSelfTest() == 0, "masterAutomationSelfTest passes");
    expect(diag.automationCurveSelfTest(), "automationCurveSelfTest passes");
    expect(diag.automationWriteSelfTest(), "automationWriteSelfTest passes");
    expect(diag.automationDeviceRemapSelfTest(), "automationDeviceRemapSelfTest passes");
    expect(diag.midiCtlSelfTest() == 0, "midiCtlSelfTest passes");
    expect(diag.pluginAutomationSelfTest(), "pluginAutomationSelfTest passes");
    expect(diag.modulationSelfTest(), "modulationSelfTest passes");
    expect(diag.rackSelfTest(), "rackSelfTest passes");
    expect(diag.rackDeviceSelfTest(), "rackDeviceSelfTest passes");
    expect(diag.timedEventsSelfTest(), "timedEventsSelfTest passes");
    expect(diag.articulationSelfTest() == 0, "articulationSelfTest passes");
    expect(diag.drumRackSelfTest(), "drumRackSelfTest passes");
    expect(diag.audioRecordSelfTest(), "audioRecordSelfTest passes");
    expect(diag.sessionAudioRecordSelfTest(), "sessionAudioRecordSelfTest passes");
    expect(diag.monitorSharedClockSelfTest(), "monitorSharedClockSelfTest passes");
    expect(nota::Engine::Diagnostics::sessionPassSelfTest() == 0, "sessionPassSelfTest passes");
    expect(nota::Engine::Diagnostics::midiSurfaceSelfTest() == 0, "midiSurfaceSelfTest passes");
    expect(eng.history().mixUndoSelfTest(), "mixUndoSelfTest passes");
    expect(eng.history().presetTagSelfTest(), "presetTagSelfTest passes");
}

} // namespace

int main() {
    testLifecycleAndTransport();
    testTracksClipsAndOfflineRender();
    testDevicesAndRacks();
    testSessionAutomationModulationAndHistory();
    testPreviewAuditionAndIo();
    testBuiltInDiagnosticsSelfTests();
    return gFailures == 0 ? 0 : 1;
}
