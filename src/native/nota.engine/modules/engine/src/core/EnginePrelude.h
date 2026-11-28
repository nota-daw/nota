// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#pragma once

#include "nota/devices/DeviceIds.h"
#include "nota/dsp/Device.h"
#include "nota/dsp/DeviceRegistry.h"
#include "nota/dsp/EditorPreview.h"
#include "nota/dsp/Instrument.h"
#include "nota/dsp/MidiDevice.h"
#include "nota/dsp/MidiTypes.h"
#include "nota/dsp/capabilities/IWavetableInstrument.h"
#include "nota/dsp/rt/AutomationTypes.h"
#include "nota/dsp/rt/ExpressionTypes.h"
#include "nota/dsp/rt/MidiControllerTypes.h"
#include "nota/dsp/rt/PlanarStereoBuffer.h"
#include "nota/dsp/rt/RenderExecutor.h"
#include "nota/dsp/rt/RtAttributes.h"
#include "nota/dsp/rt/SilenceGate.h"
#include "nota/dsp/rt/SnapshotHistory.h"
#include "nota/dsp/rt/SpscRingBuffer.h"
#include "nota/dsp/rt/TransportInfo.h"
#include "nota/engine/Diagnostics.h"
#include "nota/engine/ModelTypes.h"
#include "nota/engine/Transport.h"
#include "nota/engine/Types.h"
#include "nota/io/audio/AudioIoTypes.h"
#include "nota/io/audio/IAudioInputStream.h"
#include "nota/io/audio/IAudioOutputStream.h"
#include "nota/io/audio/OutputRouter.h"
#include "nota/io/gamepad/GamepadInput.h"
#include "nota/io/midi/IMidiInput.h"
#include "nota/io/midi/IMidiOutput.h"
#include "nota/io/midi/MidiParser.h"
#include "nota/io/midi/MidiTypes.h"
#include "nota/io/midi/MpeInput.h"
#include "nota/math/BufferOps.h"
#include "nota/math/CpuFeatures.h"
#include "nota/math/Random.h"
#include "nota/math/StereoOps.h"
#include "nota/media/AudioCodecTypes.h"
#include "nota/media/AudioFile.h"
#include "nota/media/AudioSource.h"
#include "nota/media/SampleBuffer.h"
#include "nota/media/StreamingAudioSource.h"
#include "nota/media/TempoDetect.h"
#include "nota/media/Warp.h"
#include <cstdint>

namespace nota::devices {
class RackCore;
class RackInstrument;
class RackDevice;
class DrumRack;
class MidiRack;
} // namespace nota::devices

namespace nota::detail {

namespace capabilities = nota::dsp::capabilities;

using AuroraTable = nota::dsp::WavetableAsset;
using AudioBackend = nota::io::audio::IAudioOutputStream;
using AudioConfig = nota::io::audio::AudioConfig;
using AudioDeviceInfo = nota::io::audio::AudioDeviceInfo;
using AudioInput = nota::io::audio::IAudioInputStream;
using AudioSystem = nota::io::audio::AudioSystem;
using BackendConfig = nota::io::audio::OutputStreamConfig;
using GamepadInput = nota::io::gamepad::GamepadInput;
using MidiConfig = nota::io::midi::MidiConfig;
using MidiDeviceInfo = nota::io::midi::MidiDeviceInfo;
using MidiInput = nota::io::midi::IMidiInput;
using MidiOutput = nota::io::midi::IMidiOutput;
using MidiParser = nota::io::midi::MidiParser;
using MpeInput = nota::io::midi::MpeInput;
using OutputRouting = nota::io::audio::OutputRouting;

using nota::devices::AudioEffectKind;
using nota::devices::DrumRack;
using nota::devices::InstrumentKind;
using nota::devices::MidiEffectKind;
using nota::devices::MidiRack;
using nota::devices::RackCore;
using nota::devices::RackDevice;
using nota::devices::RackInstrument;

using nota::dsp::AutomationLane;
using nota::dsp::AutomationPoint;
using nota::dsp::AutomationTarget;
using nota::dsp::Command;
using nota::dsp::CommandQueue;
using nota::dsp::CommandType;
using nota::dsp::ControllerCurve;
using nota::dsp::ctlClamp;
using nota::dsp::ctlIsReserved;
using nota::dsp::ctlIsSwitch;
using nota::dsp::ctlMin;
using nota::dsp::ctlPieceCurve;
using nota::dsp::ctlThinTolerance;
using nota::dsp::dedupeSwitch;
using nota::dsp::Device;
using nota::dsp::EditorPreview;
using nota::dsp::ExprBend;
using nota::dsp::ExprDim;
using nota::dsp::ExprPressure;
using nota::dsp::ExprSlide;
using nota::dsp::exprSmoothCoef;
using nota::dsp::ExprSustain;
using nota::dsp::GlobalExpr;
using nota::dsp::HeldMidiKeyReader;
using nota::dsp::heldMidiKeysRead;
using nota::dsp::Instrument;
using nota::dsp::isMixTarget;
using nota::dsp::kCacheLineSize;
using nota::dsp::kCtlChannelPressure;
using nota::dsp::kCtlCount;
using nota::dsp::kCtlDimBase;
using nota::dsp::kCtlJunction;
using nota::dsp::kCtlModWheel;
using nota::dsp::kCtlPitchWheel;
using nota::dsp::kCtlRest;
using nota::dsp::kCtlSustain;
using nota::dsp::kExprDims;
using nota::dsp::kExprNeutral;
using nota::dsp::kExprWireDims;
using nota::dsp::MidiDevice;
using nota::dsp::MidiEv;
using nota::dsp::MidiKeyBlock;
using nota::dsp::MidiKeyNotes;
using nota::dsp::nextDeviceLineage;
using nota::dsp::nextInstrumentUid;
using nota::dsp::nextMidiDeviceLineage;
using nota::dsp::normalizeCtlPoints;
using nota::dsp::ParamLaneSteps;
using nota::dsp::PlanarStereoBuffer;
using nota::dsp::PluginSleepMode;
using nota::dsp::RenderExecutorHost;
using nota::dsp::sliceCtlPoints;
using nota::dsp::SnapshotHistory;
using nota::dsp::SpscRingBuffer;
using nota::dsp::thinCtlCurve;
using nota::dsp::TrackLane;
using nota::dsp::TransportInfo;
using nota::dsp::VoiceExpr;

using nota::io::audio::kMaxInputChannels;

using nota::media::AudioDecodeOptions;
using nota::media::AudioSource;
using nota::media::ClipWarpStream;
using nota::media::decodeAudioFile;
using nota::media::detectTempo;
using nota::media::detectTransients;
using nota::media::kDefaultStreamThresholdBytes;
using nota::media::MemoryAudioSource;
using nota::media::probeAudioFile;
using nota::media::SampleBuffer;
using nota::media::SampleLoadPolicy;
using nota::media::StreamingAudioSource;
using nota::media::StreamingIoScheduler;
using nota::media::StreamRuntimeStats;
using nota::media::WarpCache;
using nota::media::WarpInputs;
using nota::media::WarpMarker;
using nota::media::WarpMode;

using nota::math::Rng;
using nota::math::ScopedNoDenormals;
using nota::math::SplitMix64;

namespace simd_audio {
using nota::math::addStereoScaled;
using nota::math::isSilent;
using nota::math::mixPlanarWetDry;
using nota::math::peakAbs;
using nota::math::peakAndSumSquares;
using nota::math::scaleStereo;
using nota::math::StereoMeterAccumulator;

inline void deinterleaveStereo(float* l, float* r, const float* interleaved, int32_t frames) noexcept {
    nota::math::deinterleave(l, r, interleaved, frames);
}
inline void interleaveStereo(float* interleaved, const float* l, const float* r, int32_t frames) noexcept {
    nota::math::interleave(interleaved, l, r, frames);
}
inline void interleaveStereoAdd(float* interleaved, const float* l, const float* r, int32_t frames) noexcept {
    nota::math::interleaveAdd(interleaved, l, r, frames);
}
inline void interleaveStereoWetDryMix(
    float* interleaved, const float* wetL, const float* wetR, int32_t frames, float wetMix, float dryMix) noexcept {
    nota::math::interleaveWetDryMix(interleaved, wetL, wetR, frames, wetMix, dryMix);
}
inline void applyLinearGainRampStereo(float* buf, int32_t frames, float startGain, float endGain) noexcept {
    nota::math::applyLinearGainRamp(buf, startGain, endGain, frames);
}
} // namespace simd_audio

using simd_audio::addStereoScaled;
using simd_audio::applyLinearGainRampStereo;
using simd_audio::deinterleaveStereo;
using simd_audio::interleaveStereo;
using simd_audio::interleaveStereoAdd;
using simd_audio::interleaveStereoWetDryMix;
using simd_audio::mixPlanarWetDry;
using simd_audio::peakAbs;
using simd_audio::scaleStereo;
using simd_audio::StereoMeterAccumulator;

using nota::engine::ArticulationActionData;
using nota::engine::ArticulationEntryData;
using nota::engine::AudioClipInfo;
using nota::engine::AuditionNoteSpec;
using nota::engine::AutomationPointData;
using nota::engine::ClipInfo;
using nota::engine::ClipMoveSpec;
using nota::engine::ExprPointData;
using nota::engine::MidiClaimSpec;
using nota::engine::MosaicStatus;
using nota::engine::NoteData;
using nota::engine::PassSpan;
using nota::engine::RecordHeldNote;
using nota::engine::SampleInfo;
using nota::engine::SamplerInfo;
using nota::engine::SceneProps;
using nota::engine::SessionAudioSlotInfo;
using nota::engine::SessionClipProps;
using nota::engine::StereoMeasure;
using nota::engine::StreamingStats;
using nota::engine::TakeInfo;
using nota::engine::TrackInfo;
using nota::engine::TrackMeterEntry;
using nota::engine::TransportSnapshot;

} // namespace nota::detail
