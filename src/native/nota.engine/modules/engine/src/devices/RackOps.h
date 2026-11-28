// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// RackOps — Instrument, Drum, Audio-Effect, and MIDI-Effect Rack editing,
// chain management, grouping/ungrouping, nested reference remapping, and macros.

#pragma once

#include "devices/DeviceTypes.h"

#include <cstdint>
#include <functional>
#include <memory>
#include <string>
#include <utility>

namespace nota::dsp {
class MidiDevice;
}

namespace nota::devices {
class RackCore;
}

namespace nota::engine {
struct MosaicStatus;
struct SamplerInfo;
} // namespace nota::engine

namespace nota::detail {

class EngineRuntime;
struct Graph;
class Track;

class RackOps {
  public:
    explicit RackOps(EngineRuntime& rt) noexcept : rt_(rt) {}

    void installPluginFactory();
    void syncCcRoutes(Graph& g);
    void reapplyCcRoutes();
    bool selfTest();
    bool deviceSelfTest();
    bool drumRackSelfTest();

    // --- Instrument / Audio-Effect Rack (nested device containers) ---
    // A rack (RackInstrument on an instrument track, or RackDevice in a device
    // chain) hosts N parallel chains + 8 macros. Address it with (trackId,
    // deviceIndex): deviceIndex < 0 = the track's instrument rack, >= 0 = the
    // effect-rack device at that chain position. Structural edits publish the
    // rack's own snapshot (thread-safe); they're outside engine undo in v1.
    int32_t addInstrumentTrack(); // new track, one default Synth chain
    int32_t addDrumTrack();       // new track, empty Drum Rack (pads added in UI)
    // Adds a chain whose instrument is a Sampler loaded from `path` (decoded here —
    // RackCore can't decode audio; the sample rides the rack blob). Returns the
    // chain index or -1. Used for drum pads / sampler chains dropped from the browser.
    int32_t addSamplerChain(int32_t trackId, int32_t deviceIndex, const std::string& path, int32_t rootNote, bool loop);
    bool setChainSamplerSample(
        int32_t trackId, int32_t deviceIndex, int32_t chain, const std::string& path, int32_t rootNote);
    // Hosted plugins inside a rack (VST3/AU): instrument as a new chain, or effect
    // into an existing chain. catalogIndex is the browser catalog entry. Returns
    // the new chain / device index, or -1.
    int32_t addPluginInstrumentChain(int32_t trackId, int32_t deviceIndex, int32_t catalogIndex);
    int32_t addPluginChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t catalogIndex);
    RackCore* coreAt(int32_t trackId, int32_t deviceIndex) const; // null if not a rack
    // specs/044: lanes, CV links and Arp CC targets that point into the rack at (trackId, host)
    // follow an edit of its chain children. map(chainUid, slot) gives the new (chainUid, slot);
    // a chainUid of 0 drops the reference. No undo step of its own: part of the rack edit.
    void remapNestedAfterRackEdit(int32_t trackId, int32_t host, const NestedMap& map);
    // The rack to apply an undoable edit to (#236): a fork of the rack at (trackId,
    // deviceIndex), republished in its track as one undo step. Null if not a rack.
    RackCore* forEdit(int32_t trackId, int32_t deviceIndex);
    // ⌘G on a contiguous run of devices (#257, #328): one new rack in their place holding the
    // instances themselves (nothing restarts), lanes / CV links / Arp CC targets / sidechain
    // keys following them into its chain, one undo step. Audio effects only → Audio Effect Rack
    // at fxFirst; MIDI effects only → MIDI Effect Rack at midiFirst; with the instrument → an
    // Instrument Rack as the track's instrument, chain 0 = MIDI effects → instrument → audio
    // effects (the MIDI run must end at the last MIDI effect, the audio run start at 0).
    // Returns the rack's address (effect index, -1 instrument, kMidiRackBase - i), or a
    // reason: -2 bad run, -3 not contiguous / MIDI + audio without the instrument, -4 a rack in
    // the run, -5 a plug-in the rack can't host, -6 an instrument a chain can't hold,
    // -7 not an instrument track.
    int32_t groupDevices(
        int32_t trackId, int32_t midiFirst, int32_t midiCount, bool withInstrument, int32_t fxFirst, int32_t fxCount);
    // ⌘⇧G: a one-chain rack (family 0 effect at index, 1 MIDI effect at index, 2 the
    // instrument) back into its chain's devices, references following them. Returns how many
    // came out, or -2 not a rack, -3 two or more chains, -4 a Drum Rack, -5 an Instrument
    // Rack chain without an instrument. *outDropped: bit 0 macro mappings, bit 1 chain mix.
    int32_t ungroupRack(int32_t trackId, int32_t family, int32_t index, int32_t* outDropped);
    int32_t wrapDeviceInRack(int32_t trackId, int32_t deviceIndex);
    int32_t unwrapRack(int32_t trackId, int32_t deviceIndex);
    // A reference to a parameter owner on one track, one shape for every kind of pointer
    // (automation lane, CV link source / target, Arp CC target): fam 0 audio effect, 1 MIDI
    // effect, 2 the instrument (idx -1); a chain uid + slot when it points into the rack at
    // (fam, idx). fam -1 = not a device reference; a map returning fam -2 drops it.
    // Group / Ungroup: rewrite every reference into trackId through map and publish nt with
    // the rewritten links of every track as one undo step.
    // fresh: a rack Group just built, whose Arps were rewritten already.
    void publishWithRefMap(
        int32_t trackId, std::shared_ptr<Track> nt, const DeviceRefMap& map, const RackCore* fresh = nullptr);
    // An Arp's CC target through map; a changed Arp becomes a clone (shared with undo snapshots).
    // Retarget an Arp's authoring copy; the route goes live with the next published track.
    void rewriteArpRef(MidiDevice& m, const DeviceRefMap& map);
    // The same for Arps inside the track's racks (a MIDI Effect Rack, the Instrument Rack's
    // chains): a rack holding one to change is forked into nt with clones in its chains.
    void rewriteRackArps(Track& nt, const DeviceRefMap& map, const RackCore* fresh = nullptr);
    // Drum Rack pad note per chain (-1 = all notes; instrument/effect racks ignore it).
    int32_t chainTriggerNote(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainTriggerNote(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t note);
    std::string chainName(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainName(int32_t trackId, int32_t deviceIndex, int32_t chain, const std::string& name);
    int32_t chainCount(int32_t trackId, int32_t deviceIndex) const;
    int32_t duplicateChain(int32_t trackId, int32_t deviceIndex, int32_t chain);
    // One undo step for edits the rack takes in place (a chain mix, the mode, the selector, a
    // macro value): fork the rack now, sharing its children and held notes, so the edits that
    // follow land on the fork and Undo brings back the rack as it was.
    bool checkpoint(int32_t trackId, int32_t deviceIndex);
    // Rack chain devices on the shared device clipboard (specs/042). Copy fills clipDevice_ /
    // clipMidi_; Paste inserts a fresh clone at `at` (−1 = the end) and returns its index or
    // −1 nothing / bad address, −2 wrong family for the section, −3 a rack inside a rack,
    // −5 a MIDI effect past the Instrument Rack's 16 MIDI chains. A pasted audio effect loses
    // its sidechain key. Duplicate inserts a clone right after; MoveTo moves a device to the end
    // of another chain of the same rack, mappings and all. Each edit is one rack fork.
    bool chainDeviceCopy(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index);
    bool chainMidiCopy(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index);
    int32_t chainDevicePaste(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t at);
    int32_t chainMidiPaste(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t at);
    int32_t chainPasteDevices(int32_t trackId, int32_t rack, int32_t chain, int32_t midiAt, int32_t fxAt,
        int32_t* outSkipped, int32_t* outFirstMidi, int32_t* outFirstFx);
    bool chainMidiCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t* dev, int32_t* destChain,
        int32_t* destSlot, int32_t* param, float* depth) const;
    bool chainMidiSetCcDest(int32_t t, int32_t host, int32_t chain, int32_t idx, int32_t dev, int32_t destChain,
        int32_t destSlot, int32_t param, float depth);
    bool chainAcceptsSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot) const;
    bool chainSidechain(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* src, int32_t* tapPre,
        float* gainDb, float* mix) const;
    int32_t chainSetSidechain(
        int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t src, int32_t tapPre, float gainDb, float mix);
    bool chainSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t* part) const;
    int32_t chainSetSidechainPart(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t part);
    int32_t chainSetKey(int32_t t, int32_t host, int32_t chain, int32_t slot, int32_t src, int32_t part, int32_t tapPre,
        float gainDb, float mix);
    int32_t chainDeviceDuplicate(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index);
    int32_t chainMidiDuplicate(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index);
    int32_t chainDeviceMoveTo(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index, int32_t toChain);
    int32_t chainMidiMoveTo(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index, int32_t toChain);
    // A chain's identity for a window that must follow it while chains move: a number every new
    // chain takes and every fork of the rack keeps (nested edits too), changed when the chain's
    // instrument is swapped for another; 0 for a bad address.
    int64_t chainId(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    // Load a sample into a chain's Nota Grain (a factory Grain preset's source), one undo step.
    bool chainGrainSetSample(
        int32_t trackId, int32_t deviceIndex, int32_t chain, const std::string& path, int32_t rootNote);
    // Whether a chain of the rack at (trackId, deviceIndex) may take instrument `kind`.
    bool kindAllowed(int32_t trackId, int32_t deviceIndex, int32_t kind) const;
    // Nota Mosaic as a chain instrument (specs/041), mirroring mosaicSetProgram & co. for a
    // track: checkpoint = an undoable edit (a fresh Mosaic swaps into a fork of the rack).
    bool chainMosaicSetProgram(
        int32_t trackId, int32_t deviceIndex, int32_t chain, const std::string& text, bool checkpoint);
    std::string chainMosaicProgram(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    bool chainMosaicStatus(int32_t trackId, int32_t deviceIndex, int32_t chain, MosaicStatus* out) const;
    int64_t chainMosaicZoneSampleId(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t zone) const;
    // Chain MIDI effects (MIDI Effect Rack chains; Instrument Rack chains, #329).
    int32_t chainMidiCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t addChainMidi(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t kind);
    bool removeChainMidi(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index);
    bool moveChainMidi(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t from, int32_t to);
    MidiDevice* chainMidiAt(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index) const;
    // A chain MIDI effect's param, moving a CV link's base on it (specs/044).
    void chainMidiParamSet(int32_t trackId, int32_t rack, int32_t chain, int32_t index, int32_t param, float value);
    void setChainMidiBypassed(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t index, bool b);
    void chainSelectRange(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t& lo, int32_t& hi) const;
    void setChainSelectRange(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t lo, int32_t hi);
    int32_t chainTranspose(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainTranspose(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t st);
    float chainVelScale(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainVelScale(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    bool merge(int32_t trackId, int32_t deviceIndex) const;
    void setMerge(int32_t trackId, int32_t deviceIndex, bool on);
    bool thru(int32_t trackId, int32_t deviceIndex) const;
    void setThru(int32_t trackId, int32_t deviceIndex, bool on);
    bool mono(int32_t trackId) const;
    void setMono(int32_t trackId, bool on);
    int32_t addChain(int32_t trackId, int32_t deviceIndex, int32_t instKind); // 0=Synth, 2=Physical, <0=none
    bool removeChain(int32_t trackId, int32_t deviceIndex, int32_t chain);
    bool setChainInstrument(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t instKind);
    int32_t chainInstrumentKind(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    const char* chainInstrumentName(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t chainInstrumentParamCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    std::string chainInstrumentParamName(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    std::string chainInstrumentParamId(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    float chainInstrumentParamGet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    void chainInstrumentParamSet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param, float normalized);
    float chainInstrumentParamDefault(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t param) const;
    // Live telemetry of a chain instrument (same surface as instrumentScope/… for a track
    // instrument) — lets its pop-out editor animate its graphs/keys.
    int32_t chainInstrumentScope(int32_t trackId, int32_t deviceIndex, int32_t chain, float* out, int32_t maxN) const;
    int32_t chainInstrumentVoiceCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t chainInstrumentHeldNotes(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t* out, int32_t maxN) const;
    void chainInstrumentAction(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t id, int32_t iarg, float farg);
    // Chain instrument GUI + preset capture (hosted plugins): open the native editor,
    // read the stable identifier + opaque state. Message thread.
    void openChainInstrumentEditor(int32_t trackId, int32_t deviceIndex, int32_t chain);
    void openChainDeviceEditor(int32_t trackId, int32_t deviceIndex, int32_t chain,
        int32_t dev); // hosted-plugin effect GUI
    std::string chainInstrumentPluginId(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t chainInstrumentGetState(
        int32_t trackId, int32_t deviceIndex, int32_t chain, uint8_t* out, int32_t cap) const;
    // Built-in Sampler in a chain (false / -1 if the chain instrument isn't a Sampler).
    bool chainSamplerInfo(int32_t trackId, int32_t deviceIndex, int32_t chain, SamplerInfo* out) const;
    bool setChainSamplerRoot(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t rootNote);
    float chainSamplerPlayPosition(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t chainDeviceCount(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    int32_t addChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain,
        int32_t deviceKind); // 0=EQ..4=Utility
    bool removeChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev);
    bool moveChainDevice(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t from, int32_t to);
    const char* chainDeviceName(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    int32_t chainDeviceBuiltinKind(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    int32_t chainDeviceParamCount(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    const char* chainDeviceParamName(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float chainDeviceParamMin(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float chainDeviceParamMax(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    float chainDeviceParamGet(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param) const;
    void chainDeviceParamSet(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t param, float value);
    void setChainDeviceBypassed(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, bool bypassed);
    bool chainDeviceBypassed(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    // Live telemetry of a chain device (same surface as deviceScope/deviceText/… for a top-level
    // device) — lets the chain device's pop-out card draw its meters/curves.
    float chainDeviceGainReduction(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev) const;
    int32_t chainDeviceScope(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, float* out, int32_t maxSamples) const;
    int32_t chainDeviceLayerWave(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t layer,
        float* out, int32_t maxSamples) const;
    std::string chainDeviceText(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t id) const;
    void chainDeviceAction(
        int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t dev, int32_t id, int32_t iarg, float farg);
    void setChainGain(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    void setChainPan(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    void setChainMute(int32_t trackId, int32_t deviceIndex, int32_t chain, bool b);
    void setChainSolo(int32_t trackId, int32_t deviceIndex, int32_t chain, bool b);
    float chainGain(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    float chainPan(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    bool chainMute(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    bool chainSolo(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    float macroGet(int32_t trackId, int32_t deviceIndex, int32_t macro) const;
    void macroSet(int32_t trackId, int32_t deviceIndex, int32_t macro, float v);
    int32_t addMacroMapping(int32_t trackId, int32_t deviceIndex, int32_t macro, int32_t chain, int32_t targetDevice,
        int32_t paramIndex, float rangeMin, float rangeMax);
    int32_t mappingCount(int32_t trackId, int32_t deviceIndex) const;
    bool mappingInfo(int32_t trackId, int32_t deviceIndex, int32_t index, int32_t& macro, int32_t& chain,
        int32_t& targetDevice, int32_t& paramIndex, float& rangeMin, float& rangeMax) const;
    bool removeMapping(int32_t trackId, int32_t deviceIndex, int32_t index);
    // Instrument-Rack extras (mockup 2p): key/velocity zones, per-chain meter, named
    // macros, rack output (volume/glide), macro-map range/curve editing.
    void chainZone(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t& keyLo, int32_t& keyHi, int32_t& velLo,
        int32_t& velHi) const;
    void setChainZone(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t keyLo, int32_t keyHi, int32_t velLo,
        int32_t velHi);
    float chainMeter(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    std::string macroName(int32_t trackId, int32_t deviceIndex, int32_t macro) const;
    void setMacroName(int32_t trackId, int32_t deviceIndex, int32_t macro, const std::string& name);
    float volume(int32_t trackId, int32_t deviceIndex) const;
    void setVolume(int32_t trackId, int32_t deviceIndex, float v);
    float glide(int32_t trackId, int32_t deviceIndex) const;
    void setGlide(int32_t trackId, int32_t deviceIndex, float v);
    bool setMappingRange(int32_t trackId, int32_t deviceIndex, int32_t index, float rangeMin, float rangeMax);
    int32_t mappingCurve(int32_t trackId, int32_t deviceIndex, int32_t index) const;
    bool setMappingCurve(int32_t trackId, int32_t deviceIndex, int32_t index, int32_t curve);
    // Drum Rack per-pad shaping (choke group / tune semitones / decay 0..1) + kit swing/humanize.
    void setChainChoke(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t group);
    int32_t chainChoke(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainTune(int32_t trackId, int32_t deviceIndex, int32_t chain, int32_t semitones);
    int32_t chainTune(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setChainDecay(int32_t trackId, int32_t deviceIndex, int32_t chain, float v);
    float chainDecay(int32_t trackId, int32_t deviceIndex, int32_t chain) const;
    void setSwing(int32_t trackId, int32_t deviceIndex, float v);
    float swing(int32_t trackId, int32_t deviceIndex) const;
    void setHumanize(int32_t trackId, int32_t deviceIndex, float v);
    float humanize(int32_t trackId, int32_t deviceIndex) const;
    // Audio Effect Rack routing + output stage (mode / dry-wet / gain=volume / pdc / chain selector).
    void setMode(int32_t trackId, int32_t deviceIndex, int32_t mode);
    int32_t mode(int32_t trackId, int32_t deviceIndex) const;
    void setDryWet(int32_t trackId, int32_t deviceIndex, float v);
    float dryWet(int32_t trackId, int32_t deviceIndex) const;
    void setPdc(int32_t trackId, int32_t deviceIndex, bool on);
    bool pdc(int32_t trackId, int32_t deviceIndex) const;
    void setChainSelect(int32_t trackId, int32_t deviceIndex, float v);
    float chainSelect(int32_t trackId, int32_t deviceIndex) const;
    void setSelFollow(int32_t trackId, int32_t deviceIndex, bool on);
    bool selFollow(int32_t trackId, int32_t deviceIndex) const;
    float liveSelector(int32_t trackId, int32_t deviceIndex) const;

  private:
    EngineRuntime& rt_;
};

} // namespace nota::detail
