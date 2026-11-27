// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceMath — private math/SIMD/denormal adapters for modules/devices implementations.

#pragma once

#include "nota/media/AudioCodecTypes.h"
#include "nota/dsp/rt/AudioSpan.h"
#include <nota/dsp/Device.h>
#include <nota/dsp/EditorPreview.h>
#include <nota/dsp/Instrument.h>
#include <nota/dsp/MidiDevice.h>
#include <nota/dsp/MidiTypes.h>
#include <nota/dsp/capabilities/IWavetableInstrument.h>
#include <nota/dsp/primitives/BiquadCascade.h>
#include <nota/dsp/primitives/CompensationDelay.h>
#include <nota/dsp/primitives/Convolution.h>
#include <nota/dsp/primitives/DelayLine.h>
#include <nota/dsp/primitives/Envelope.h>
#include <nota/dsp/primitives/Hadamard.h>
#include <nota/dsp/primitives/LadderFilter.h>
#include <nota/dsp/primitives/LevelHistory.h>
#include <nota/dsp/primitives/Oscillator.h>
#include <nota/dsp/primitives/Oversampler.h>
#include <nota/dsp/primitives/SpectrumAnalyzer.h>
#include <nota/dsp/primitives/SteinerFilter.h>
#include <nota/dsp/primitives/SvfFilter.h>
#include <nota/dsp/primitives/TruePeakMeter.h>
#include <nota/dsp/poly/PolyVoiceManager.h>
#include <nota/dsp/poly/PolyVoiceScheduler.h>
#include <nota/dsp/poly/SynthVoice.h>
#include <nota/dsp/rt/AutomationTypes.h>
#include <nota/dsp/rt/DeviceTelemetry.h>
#include <nota/dsp/rt/ExpressionTypes.h>
#include <nota/dsp/rt/MidiControllerTypes.h>
#include <nota/dsp/rt/Parameter.h>
#include <nota/dsp/rt/PlanarStereoBuffer.h>
#include <nota/dsp/rt/RtAttributes.h>
#include <nota/dsp/rt/ScratchArena.h>
#include <nota/dsp/rt/SnapshotHistory.h>
#include <nota/dsp/rt/TransportInfo.h>
#include <nota/math/BufferOps.h>
#include <nota/math/ComplexOps.h>
#include <nota/math/CpuFeatures.h>
#include <nota/math/FastMath.h>
#include <nota/math/Fft.h>
#include <nota/math/Random.h>
#include <nota/math/SimdVec.h>
#include <nota/math/StereoOps.h>
#include <nota/media/AudioFile.h>
#include <nota/media/SampleBuffer.h>

#include <cstdint>

namespace nota {

namespace fast_math {

using nota::math::cosPhase;
using nota::math::dbToLin;
using nota::math::exp2f;
using nota::math::linToDb;
using nota::math::log2f;
using nota::math::sinPhase;
using nota::math::tanh;
using nota::math::tanhPrecise;
using nota::math::tanhRatio;
using nota::math::tanPrewarp;

NOTA_ALWAYS_INLINE float sin(float phase01) NOTA_RT_SAFE {
    return nota::math::sinPhase(phase01);
}
NOTA_ALWAYS_INLINE float cos(float phase01) NOTA_RT_SAFE {
    return nota::math::cosPhase(phase01);
}

} // namespace fast_math

namespace simd {

using nota::math::add;
using nota::math::addScaled;
using nota::math::clear;
using nota::math::clip;
using nota::math::complexMul;
using nota::math::complexMulAdd;
using nota::math::copy;
using nota::math::cpuPause;
using nota::math::dot;
using nota::math::dot16;
using nota::math::fill;
using nota::math::fma;
using nota::math::isSilent;
using nota::math::maxAbs;
using nota::math::mul;
using nota::math::peakAndSumSquares;
using nota::math::scale;
using nota::math::squashNonFinite;
using nota::math::sub;
using nota::math::sumSquares;

} // namespace simd

namespace simd_audio {

using nota::math::addStereoScaled;
using nota::math::isSilent;
using nota::math::mixPlanarWetDry;
using nota::math::peakAbs;
using nota::math::peakAndSumSquares;
using nota::math::scaleStereo;
using nota::math::StereoMeterAccumulator;

NOTA_ALWAYS_INLINE void deinterleaveStereo(float* l, float* r, const float* interleaved, int32_t frames) noexcept {
    nota::math::deinterleave(l, r, interleaved, frames);
}

NOTA_ALWAYS_INLINE void interleaveStereo(float* interleaved, const float* l, const float* r, int32_t frames) noexcept {
    nota::math::interleave(interleaved, l, r, frames);
}

NOTA_ALWAYS_INLINE void interleaveStereoAdd(
    float* interleaved, const float* l, const float* r, int32_t frames) noexcept {
    nota::math::interleaveAdd(interleaved, l, r, frames);
}

NOTA_ALWAYS_INLINE void interleaveStereoWetDryMix(
    float* interleaved, const float* wetL, const float* wetR, int32_t frames, float wetMix, float dryMix) noexcept {
    nota::math::interleaveWetDryMix(interleaved, wetL, wetR, frames, wetMix, dryMix);
}

NOTA_ALWAYS_INLINE void applyLinearGainRampStereo(float* buf, int32_t frames, float startGain, float endGain) noexcept {
    nota::math::applyLinearGainRamp(buf, startGain, endGain, frames);
}

} // namespace simd_audio

using nota::dsp::AutomationLane;
using nota::dsp::AutomationPoint;
using nota::dsp::AutomationTarget;
using nota::dsp::BlockRamp;
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
using nota::dsp::Instrument;
using nota::dsp::isMixTarget;
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
using nota::dsp::normalizeCtlPoints;
using nota::dsp::OnePoleSmoother;
using nota::dsp::paramDenormalize;
using nota::dsp::ParamLaneSteps;
using nota::dsp::ParamMapping;
using nota::dsp::paramNormalize;
using nota::dsp::PlanarAudioSpan;
using nota::dsp::sliceCtlPoints;
using nota::dsp::SnapshotHistory;
using nota::dsp::StereoScratchArena;
using nota::dsp::thinCtlCurve;
using nota::dsp::TrackLane;
using nota::dsp::TransportInfo;
using nota::dsp::VoiceExpr;
using nota::dsp::WavetableAsset;
using nota::dsp::WavetableSet;
using nota::media::AudioDecodeOptions;
using nota::media::AudioStreamInfo;
using nota::media::decodeAudioFile;
using nota::media::probeAudioFile;
using nota::media::SampleBuffer;
using nota::simd_audio::addStereoScaled;
using nota::simd_audio::applyLinearGainRampStereo;
using nota::simd_audio::deinterleaveStereo;
using nota::simd_audio::interleaveStereo;
using nota::simd_audio::interleaveStereoAdd;
using nota::simd_audio::interleaveStereoWetDryMix;
using nota::simd_audio::mixPlanarWetDry;
using nota::simd_audio::peakAbs;
using nota::simd_audio::scaleStereo;
using nota::simd_audio::StereoMeterAccumulator;
using AuroraTable = nota::dsp::WavetableAsset;
using AuroraTableSet = nota::dsp::WavetableSet;

namespace dsp {
using nota::math::ScopedNoDenormals;
} // namespace dsp

} // namespace nota

namespace nota::devices {

using nota::AuroraTable;
using nota::AutomationLane;
using nota::Device;
using nota::EditorPreview;
using nota::Instrument;
using nota::MidiDevice;
using nota::MidiEv;
using nota::ParamLaneSteps;
using nota::PlanarAudioSpan;
using nota::SampleBuffer;
using nota::SnapshotHistory;
using nota::StereoScratchArena;
using nota::TransportInfo;
using nota::WavetableAsset;
namespace fast_math = nota::fast_math;
namespace simd = nota::simd;
namespace simd_audio = nota::simd_audio;

} // namespace nota::devices
