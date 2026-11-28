// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceOps — instruments, built-in/plugin devices, Aurora wavetables, Mosaic,
// Grain/Sampler/Rhythm helpers, clipboard/transfer, sidechain routing, PDC, and preset tags.

#pragma once

#include "core/EnginePrelude.h"
#include "devices/DeviceTypes.h"
#include "model/EngineTypes.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"

#include <cstdint>
#include <memory>
#include <string>
#include <vector>

namespace nota::dsp {
class Device;
class Instrument;
class MidiDevice;
} // namespace nota::dsp

namespace nota::detail {

class EngineRuntime;
struct Graph;
class Track;

class DeviceOps {
  public:
    explicit DeviceOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    int32_t trackInstrumentKind(int32_t trackId) const;
    int64_t trackInstrumentId(int32_t trackId) const;       // nota_track_instrument_id (specs/043)
    bool trackInstrumentSupportsMpe(int32_t trackId) const; // per-note expression (MPE)
    int32_t trackDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex) const;
    int64_t trackDeviceId(int32_t trackId, int32_t deviceIndex) const; // nota_track_device_id (#540)
    std::string trackInstrumentPluginId(int32_t trackId) const;
    std::string trackDevicePluginId(int32_t trackId, int32_t deviceIndex) const;
    int32_t addInstrumentTrack();     // built-in Nota Synth
    int32_t addPhysicalSynthTrack();  // built-in Nota Physical
    int32_t addWavetableSynthTrack(); // built-in Nota Aurora
    int32_t addVoltSynthTrack();      // built-in Nota Volt (virtual analog)
    int32_t addBassSynthTrack();      // built-in Nota Bass (bass synthesizer)
    int32_t addPendulumSynthTrack();  // built-in Nota Pendulum (generative keys)
    int32_t addOperatorSynthTrack();  // built-in Nota Operator (4-op FM)
    int32_t addGrainSynthTrack();     // built-in Nota Grain (granular)
    int32_t addFluxSynthTrack();      // built-in Nota Flux (vector-morph, sidechain React)
    int32_t addRhythmSynthTrack();    // built-in Nota Rhythm (drum machine, kind 12)
    int32_t addMonolithTrack();       // built-in Nota Monolith (mono Model-D synth, kind 13)
    int32_t addPentadTrack();         // built-in Nota Pentad (5-voice Prophet-5 poly, kind 14)
    int32_t addConsortTrack();        // built-in Nota Consort (paraphonic semi-modular, kind 15)
    int32_t addKeysTrack();           // built-in Nota Keys (modelled electric piano, kind 16)
    int32_t addMosaicTrack();         // built-in Nota Mosaic (multisample instrument, kind 17)
    int32_t addTwinTrack();           // built-in Nota Twin (two-part morphing analog poly, kind 18)
    int32_t addBuiltinTrack(int32_t kind);
    bool mosaicSetProgram(int32_t trackId, const std::string& text, bool checkpoint);
    std::string mosaicProgram(int32_t trackId) const;
    bool mosaicStatus(int32_t trackId, MosaicStatus* out) const;
    int64_t mosaicZoneSampleId(int32_t trackId, int32_t zone) const;
    void instrumentAction(int32_t trackId, int32_t id, int32_t iarg, float farg); // UI editing channel
    bool auroraSetTable(int32_t trackId, int32_t osc, std::shared_ptr<const AuroraTable> t, bool checkpoint);
    bool auroraClearTable(int32_t trackId, int32_t osc);
    bool auroraSetSource(int32_t trackId, int32_t osc, bool custom, bool checkpoint);
    bool auroraSetMissing(int32_t trackId, int32_t osc, const std::string& name);
    std::shared_ptr<const dsp::capabilities::IWavetableInstrument> auroraOf(
        int32_t trackId) const; // null unless an Aurora
    void instrumentActionUndoable(Instrument& inst, int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t id,
        int32_t iarg, float farg); // the action, plus its undo step when the edit state changed
    bool instrumentSetStateUndoable(int32_t trackId, int32_t deviceIndex, int32_t chain, const uint8_t* data,
        int32_t size); // chain -1: the track's instrument; else that chain of the rack at deviceIndex
    void pushInstrumentEdit(int32_t trackId, int32_t deviceIndex, int32_t chain, std::vector<uint8_t> before,
        std::vector<uint8_t> after, MixKind kind = MixKind::InstrumentEdit);
    bool recordInstrumentState(
        int32_t trackId, int32_t deviceIndex, int32_t chain, const uint8_t* before, int32_t size);
    bool setRhythmVoiceSample(int32_t trackId, int32_t voice, const std::string& path); // Phase 2
    bool setTrackGrainSample(int32_t trackId, const std::string& path, int32_t rootNote);
    bool setTrackGrainRoot(int32_t trackId, int32_t rootNote);
    int32_t addSamplerTrack(const std::string& path, int32_t rootNote, bool loop);
    int32_t addSamplerInstrumentTrack(); // empty Sampler (sample loaded later)
    bool setTrackSamplerSample(int32_t trackId, const std::string& path, int32_t rootNote);
    bool setTrackSamplerRoot(int32_t trackId, int32_t rootNote);
    float samplerPlayPosition(int32_t trackId) const;       // 0..1 of the sample, -1 = silent
    int32_t addPluginInstrumentTrack(int32_t catalogIndex); // new track, plugin as instrument
    bool setTrackInstrumentPlugin(int32_t trackId, int32_t catalogIndex);
    bool setTrackBuiltinInstrument(int32_t trackId, int32_t kind);       // replace with a built-in synth
    int32_t addTrackEffectPlugin(int32_t trackId, int32_t catalogIndex); // returns device index or -1
    int32_t servicePluginLoads();
    int32_t waitPluginLoads(int32_t timeoutMs);
    int32_t devicePluginLoadState(
        int32_t trackId, int32_t deviceIndex, int32_t* reasonCode, std::string* reasonArg) const;
    bool devicePluginLoadAction(int32_t trackId, int32_t deviceIndex, int32_t action);
    int32_t trackDeviceCount(int32_t trackId) const;
    int32_t clipAudioMono(int32_t trackId, int32_t clipIndex, float* out, int32_t maxFrames, double* outSr) const;
    int32_t addTrackBuiltinDevice(int32_t trackId, int32_t kind); // 0=EQ, 1=Compressor
    bool moveDevice(int32_t trackId, int32_t fromIndex, int32_t toIndex);
    bool removeDevice(int32_t trackId, int32_t deviceIndex);
    bool copyDevice(int32_t trackId, int32_t deviceIndex);
    bool copyMidiEffect(int32_t trackId, int32_t index);
    int32_t deviceClipboardKind() const;
    int32_t pasteDevice(int32_t trackId, int32_t atIndex);
    int32_t pasteMidiEffect(int32_t trackId, int32_t atIndex);
    int32_t duplicateDevice(int32_t trackId, int32_t deviceIndex);
    int32_t duplicateMidiEffect(int32_t trackId, int32_t index);
    // other two families; deviceClipboardKind() is 3 then. Paste replaces the track's instrument
    bool copyInstrument(int32_t trackId);
    int32_t pasteInstrument(int32_t trackId);
    int32_t copyDevices(
        int32_t trackId, const int32_t* midiIdx, int32_t midiN, bool withInstrument, const int32_t* fxIdx, int32_t fxN);
    int32_t deviceClipboardCount() const;
    int32_t pasteDevices(int32_t trackId, int32_t midiAt, int32_t fxAt, int32_t* outSkipped);
    bool replaceTrackInstrument(int32_t trackId, std::shared_ptr<Instrument> inst);
    int32_t transferDevice(int32_t srcTrackId, int32_t family, int32_t index, int32_t dstTrackId, int32_t atIndex,
        bool keepSource, int32_t* outFlags);
    const char* deviceName(int32_t trackId, int32_t deviceIndex) const;
    int32_t deviceScope(int32_t trackId, int32_t deviceIndex, float* out, int32_t maxSamples) const;
    void deviceAction(int32_t trackId, int32_t deviceIndex, int32_t id, int32_t iarg, float farg);
    int32_t deviceLayerWave(int32_t trackId, int32_t deviceIndex, int32_t layer, float* out, int32_t maxSamples) const;
    int32_t deviceGetState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const;
    void deviceSetState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size);
    bool deviceLoadFile(int32_t trackId, int32_t deviceIndex, const std::string& path);
    std::string deviceText(int32_t trackId, int32_t deviceIndex, int32_t id) const;
    bool setEqBandText(
        int32_t trackId, int32_t deviceIndex, int32_t band, const std::string& label, const std::string& note);
    std::string eqBandText(int32_t trackId, int32_t deviceIndex, int32_t band, int32_t which) const;
    int32_t deviceParamCount(int32_t trackId, int32_t deviceIndex) const;
    const char* deviceParamName(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float deviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float deviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float deviceGetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    void deviceSetParam(int32_t trackId, int32_t deviceIndex, int32_t paramIndex, float value);
    float deviceParamDefault(int32_t trackId, int32_t deviceIndex, int32_t paramIndex) const;
    float instrumentParamDefault(int32_t trackId, int32_t paramIndex) const;
    float builtinDeviceParamDefault(int32_t kind, int32_t paramIndex) const;
    float midiEffectParamDefault(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float deviceGainReduction(int32_t trackId, int32_t deviceIndex) const;
    void setDeviceSidechainSource(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId);
    int32_t deviceSidechainSource(int32_t trackId, int32_t deviceIndex) const;
    bool deviceAcceptsSidechain(int32_t trackId, int32_t deviceIndex) const; // Phase C
    void setDeviceSidechainGain(int32_t trackId, int32_t deviceIndex, float db);
    float deviceSidechainGain(int32_t trackId, int32_t deviceIndex) const;
    void setDeviceSidechainMix(int32_t trackId, int32_t deviceIndex, float mix);
    float deviceSidechainMix(int32_t trackId, int32_t deviceIndex) const;
    void setDeviceSidechainTapPre(int32_t trackId, int32_t deviceIndex, int32_t pre);
    int32_t deviceSidechainTapPre(int32_t trackId, int32_t deviceIndex) const;
    void setInstrumentSidechainSource(int32_t trackId, int32_t sourceTrackId);
    int32_t instrumentSidechainSource(int32_t trackId) const;
    bool instrumentAcceptsSidechain(int32_t trackId) const;
    void recomputeRouting(); // message thread: assign route-bus slots to source tracks
    int32_t trackPartCount(int32_t trackId) const;
    std::string trackPartName(int32_t trackId, int32_t index) const;
    int32_t setDeviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t sourceTrackId, int32_t part);
    bool deviceSidechainKey(int32_t trackId, int32_t deviceIndex, int32_t* source, int32_t* part) const;
    int32_t setInstrumentSidechainKey(int32_t trackId, int32_t sourceTrackId, int32_t part);
    bool instrumentSidechainKey(int32_t trackId, int32_t* source, int32_t* part) const;
    int32_t sidechainRoutesFree() const;
    bool sidechainRouteInUse(int32_t sourceTrackId, int32_t part) const;
    int32_t partIndexOf(int32_t sourceTrackId, uint64_t part) const;
    bool partIdAt(int32_t sourceTrackId, int32_t index, uint64_t* part) const;
    template <typename Consumer> int32_t applySidechainKey(Consumer& c, int32_t src, uint64_t part, bool routed = true);
    void openTrackEditor(int32_t trackId, int32_t deviceIndex);
    void closeTrackEditor(int32_t trackId, int32_t deviceIndex);
    bool trackEditorOpen(int32_t trackId, int32_t deviceIndex) const;
    EditorPreview trackEditorPreview(int32_t trackId, int32_t deviceIndex) const;
    bool setTrackEditorPreview(int32_t trackId, int32_t deviceIndex, EditorPreview p);
    int32_t getPluginState(int32_t trackId, int32_t deviceIndex, uint8_t* out, int32_t cap) const;
    bool setPluginState(int32_t trackId, int32_t deviceIndex, const uint8_t* data, int32_t size);
    void setTrackDeviceBypassed(int32_t trackId, int32_t deviceIndex, bool bypassed);
    bool trackDeviceBypassed(int32_t trackId, int32_t deviceIndex) const;
    bool setPluginSleepMode(int32_t trackId, int32_t deviceIndex, int32_t mode);
    int32_t pluginSleepMode(int32_t trackId, int32_t deviceIndex) const;
    static int32_t pluginEditorSleepSelftest(int32_t catalogIndex);
    void setTrackDeviceFolded(int32_t trackId, int32_t deviceIndex, bool folded);
    bool trackDeviceFolded(int32_t trackId, int32_t deviceIndex) const;
    int32_t trackLatencySamples(int32_t trackId) const;
    int32_t addTrackMidiEffect(int32_t trackId, int32_t kind); // 0 = Arp; returns index or -1
    bool moveMidiEffect(int32_t trackId, int32_t fromIndex, int32_t toIndex);
    bool removeMidiEffect(int32_t trackId, int32_t index);
    int32_t trackMidiEffectCount(int32_t trackId) const;
    int32_t midiEffectKind(int32_t trackId, int32_t index) const; // midiKind(), -1 if none
    int32_t midiEffectLastIn(int32_t trackId,
        int32_t index) const; // last remapped note-on IN value (Scale pitch / Velocity vel), -1 = none
    int32_t midiEffectLastOut(int32_t trackId, int32_t index) const; // last remapped note-on OUT value, -1 = none
    int32_t midiEffectScope(int32_t trackId, int32_t index, float* out,
        int32_t maxN) const;                                             // float scope (Nota Velocity in/out pairs)
    void midiEffectCommand(int32_t trackId, int32_t index, int32_t cmd); // editor command (Nota Arp: 1 = restart)
    std::vector<uint8_t> midiEffectGetState(int32_t trackId, int32_t index) const;
    bool midiEffectSetState(int32_t trackId, int32_t index, const uint8_t* data, int32_t size, bool checkpoint);
    const char* midiEffectName(int32_t trackId, int32_t index) const;
    int32_t midiEffectParamCount(int32_t trackId, int32_t index) const;
    const char* midiEffectParamName(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float midiEffectParamMin(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float midiEffectParamMax(int32_t trackId, int32_t index, int32_t paramIndex) const;
    float midiEffectGetParam(int32_t trackId, int32_t index, int32_t paramIndex) const;
    void midiEffectSetParam(int32_t trackId, int32_t index, int32_t paramIndex, float value);
    void setMidiEffectBypassed(int32_t trackId, int32_t index, bool bypassed);
    bool midiEffectBypassed(int32_t trackId, int32_t index) const;
    void setMidiEffectFolded(int32_t trackId, int32_t index, bool folded);
    bool midiEffectFolded(int32_t trackId, int32_t index) const;
    void setMidiEffectCcDest(int32_t trackId, int32_t index, int32_t destDevice, int32_t destParam);
    void setMidiEffectCcDepth(int32_t trackId, int32_t index, float depth);
    int32_t midiEffectCcDestDevice(int32_t trackId, int32_t index) const;
    int32_t midiEffectCcDestParam(int32_t trackId, int32_t index) const;
    float midiEffectCcDepth(int32_t trackId, int32_t index) const;
    bool midiEffectCcDestPath(int32_t trackId, int32_t index, int32_t* chainIndex, int32_t* slot) const;
    bool setMidiEffectCcDestPath(int32_t trackId, int32_t index, int32_t chainIndex, int32_t slot);
    bool deviceClipboardSidechain() const;
    void resolveLoadedPluginLanes(); // plug-in-param lanes made while their plug-in loaded (specs/113)
    void giveUpVanishedPluginLoads(const Graph& before, const Graph& after);
    static int32_t trackLatency(const Track& t);
    std::shared_ptr<Instrument> cloneInstrument(const Track& src) const; // duplicateTrack helper
    std::shared_ptr<Instrument> cloneInstrumentFrom(const Instrument& src) const;
    std::shared_ptr<Device> cloneDevice(const Device& src) const;
    std::shared_ptr<MidiDevice> cloneMidiDevice(const MidiDevice& src) const;
    void recomputePdc(bool resetChangedDelays = false); // message thread: realign all tracks
    Device* deviceAt(int32_t trackId, int32_t deviceIndex) const;
    MidiDevice* midiDeviceAt(int32_t trackId, int32_t index) const;
    int32_t presetTagOf(int32_t trackId, int32_t surface, int32_t index) const; // -1 no device
    bool setPresetTagIndex(int32_t trackId, int32_t surface, int32_t index, int32_t tag);
    int32_t setPresetTag(int32_t trackId, int32_t surface, int32_t index, const char* id, const char* name,
        const float* base, int32_t n);
    const PresetTagData* presetTagData(int32_t tag) const;
    int32_t presetModified(int32_t trackId, int32_t surface, int32_t index) const;
    bool presetTagSelfTest();
    Instrument* editStateInstrument(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void republishCcRoutes(int32_t trackId); // after a live CC target change: publish its route
    int32_t insertDeviceClone(int32_t trackId, std::shared_ptr<Device> dev, int32_t atIndex);
    int32_t insertMidiEffectClone(int32_t trackId, std::shared_ptr<MidiDevice> md,
        int32_t atIndex); // ... or of the copied MIDI effect (one of the two at a time)
    void setLatencyConfigurationParam(Device& device, int32_t param, float value);
    void compactNoteStates(bool force = false);

  private:
    template <typename Fn> void forEachRouteKey(Fn&& fn, bool meters = true) const;
    std::shared_ptr<Instrument> cloneInstrumentBody(const Instrument& src) const;
    bool presetSurfaceValues(int32_t trackId, int32_t surface, int32_t index, std::vector<float>& out) const;

    EngineRuntime& rt_;
};

} // namespace nota::detail
