// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// DeviceIds — canonical numeric kinds and stable string slugs for built-in devices.

#include "nota/devices/DeviceIds.h"

#include <array>
#include <string_view>
#include <cstdint>
#include <cstddef>

namespace nota::devices {
namespace {

struct SlugEntry {
    int32_t kind;
    std::string_view slug;
};

constexpr std::array<SlugEntry, 28> kAudioEffects{{
    {0, "eq"},
    {1, "compressor"},
    {2, "reverb"},
    {3, "delay"},
    {4, "utility"},
    {5, "effect-rack"},
    {6, "amp"},
    {7, "auto-filter"},
    {8, "vintage"},
    {9, "auto-pan"},
    {10, "auto-shift"},
    {11, "beat-repeat"},
    {12, "crush"},
    {13, "dynamic-eq"},
    {14, "ceiling"},
    {15, "strata"},
    {16, "eq3"},
    {17, "forge"},
    {18, "auto-gain"},
    {19, "shutter"},
    {20, "chamber"},
    {21, "prism"},
    {22, "lens"},
    {23, "flanger"},
    {24, "phaser"},
    {25, "chorus"},
    {26, "vocoder"},
    {27, "color"},
}};

constexpr std::array<SlugEntry, 19> kInstruments{{
    {0, "synth"},
    {1, "sampler"},
    {2, "physical"},
    {3, "instrument-rack"},
    {4, "drum-rack"},
    {5, "aurora"},
    {6, "volt"},
    {7, "bass"},
    {8, "pendulum"},
    {9, "operator"},
    {10, "grain"},
    {11, "flux"},
    {12, "rhythm"},
    {13, "monolith"},
    {14, "pentad"},
    {15, "consort"},
    {16, "keys"},
    {17, "mosaic"},
    {18, "twin"},
}};

constexpr std::array<SlugEntry, 7> kMidiEffects{{
    {0, "arpeggiator"},
    {1, "chord"},
    {2, "scale"},
    {3, "note-length"},
    {4, "velocity"},
    {5, "random"},
    {6, "midi-rack"},
}};

template <std::size_t N>
constexpr std::string_view slugFor(const std::array<SlugEntry, N>& table, int32_t kind) noexcept {
    for (const auto& entry : table) {
        if (entry.kind == kind) return entry.slug;
    }
    return {};
}

template <std::size_t N>
constexpr int32_t kindFor(const std::array<SlugEntry, N>& table, std::string_view slug) noexcept {
    for (const auto& entry : table) {
        if (entry.slug == slug) return entry.kind;
    }
    return -1;
}

} // namespace

std::string_view audioEffectSlug(int32_t kind) noexcept {
    return slugFor(kAudioEffects, kind);
}

std::string_view instrumentSlug(int32_t kind) noexcept {
    return slugFor(kInstruments, kind);
}

std::string_view midiEffectSlug(int32_t kind) noexcept {
    return slugFor(kMidiEffects, kind);
}

int32_t audioEffectKindFromSlug(std::string_view slug) noexcept {
    return kindFor(kAudioEffects, slug);
}

int32_t instrumentKindFromSlug(std::string_view slug) noexcept {
    return kindFor(kInstruments, slug);
}

int32_t midiEffectKindFromSlug(std::string_view slug) noexcept {
    return kindFor(kMidiEffects, slug);
}

} // namespace nota::devices
