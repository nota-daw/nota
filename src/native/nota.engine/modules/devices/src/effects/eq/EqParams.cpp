// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-8 parameter metadata, text descriptions, and UTF-8 band-label state serialization.

#include "Eq.h"

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstddef>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>

namespace nota {

void Eq::setBand(int b, float on, int type, float freq, float gain, float q) {
    p_[b * kPerBand + On].store(on);
    p_[b * kPerBand + Type].store(static_cast<float>(type));
    p_[b * kPerBand + Freq].store(freq);
    p_[b * kPerBand + Gain].store(gain);
    p_[b * kPerBand + Q].store(q);
}

const char* Eq::paramName(int32_t i) const {
    if (i == Scale) return "Scale";
    if (i == Output) return "Output";
    if (i == AutoGain) return "Auto Gain";
    if (i == Analyzer) return "Analyzer";
    if (i == AnaRange) return "Analyzer Range";
    if (i == AnaPeak) return "Analyzer Peak";
    if (i == Labels) return "Labels";
    if (i == View) return "View";
    static thread_local char buf[16];
    if (i >= SlopeBase && i < ChannelBase) {
        std::snprintf(buf, sizeof(buf), "%d Slope", i - SlopeBase + 1);
        return buf;
    }
    if (i >= ChannelBase && i < Scale) {
        std::snprintf(buf, sizeof(buf), "%d Channel", i - ChannelBase + 1);
        return buf;
    }
    if (i < 0 || i >= kBandParams) return "";
    const int b = i / kPerBand, f = i % kPerBand;
    const char* fld = f == On ? "On" : f == Type ? "Type" : f == Freq ? "Freq" : f == Gain ? "Gain" : "Q";
    std::snprintf(buf, sizeof(buf), "%d %s", b + 1, fld);
    return buf;
}

float Eq::paramMin(int32_t i) const {
    if (i == Output) return -12.0f;
    if (i < 0 || i >= kBandParams) return 0.0f; // Slope, Channel, Scale, Auto Gain, Analyzer…
    switch (i % kPerBand) {
    case On: return 0.0f;
    case Type: return 0.0f;
    case Freq: return 20.0f;
    case Gain: return -18.0f;
    default: return 0.1f;
    }
}

float Eq::paramMax(int32_t i) const {
    if (i >= SlopeBase && i < ChannelBase) return 2.0f;
    if (i >= ChannelBase && i < Scale) return kNumChannels - 1;
    if (i == Scale) return 200.0f;
    if (i == Output) return 12.0f;
    if (i == AutoGain) return 1.0f;
    if (i == Analyzer) return 2.0f;
    if (i == AnaRange) return 2.0f;
    if (i == AnaPeak) return 1.0f;
    if (i == Labels) return 1.0f;
    if (i == View) return 2.0f; // 0 = L, 1 = S, 2 = XL (#540)
    switch (i % kPerBand) {
    case On: return 1.0f;
    case Type: return kNumTypes - 1;
    case Freq: return 20000.0f;
    case Gain: return 18.0f;
    default: return 18.0f;
    }
}

float Eq::getParam(int32_t i) const {
    return (i >= 0 && i < kNumParams) ? p_[i].load(std::memory_order_relaxed) : 0.0f;
}

void Eq::setParam(int32_t i, float v) {
    if (i >= 0 && i < kNumParams && std::isfinite(v))
        p_[i].store(std::clamp(v, paramMin(i), paramMax(i)), std::memory_order_relaxed);
    wake();
}

std::string Eq::deviceText(int32_t id) const {
    static const char* types[kNumTypes] = {"Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut"};
    static const char* chans[kNumChannels] = {"stereo", "mid", "side", "left", "right"};
    static const char* anas[3] = {"pre", "post", "off"};
    char b[256];
    if (id == 0) {
        int nOn = 0;
        for (int k = 0; k < kBands; ++k)
            if (bandOn(k)) ++nOn;
        std::snprintf(b, sizeof b, "%d bands on - scale %.0f %%", nOn, get(Scale));
        std::string s = b;
        for (int k = 0; k < kBands; ++k) {
            if (!bandOn(k)) continue;
            const int ty = typeOf(k);
            std::snprintf(b, sizeof b, " | B%d %s %.0f Hz", k + 1, types[ty], bp(k, Freq));
            s += b;
            if (hasGain(ty)) {
                std::snprintf(b, sizeof b, " %+.1f dB", bp(k, Gain));
                s += b;
            }
            std::snprintf(b, sizeof b, " Q %.2f", bp(k, Q));
            s += b;
            if (ty == LowCut || ty == HighCut) {
                std::snprintf(b, sizeof b, " %d dB/oct", 12 << slopeOf(k));
                s += b;
            }
            if (channelOf(k) != ChStereo) {
                std::snprintf(b, sizeof b, " %s", chans[channelOf(k)]);
                s += b;
            }
        }
        std::snprintf(b, sizeof b, " | output %+.1f dB%s | analyzer %s", get(Output),
            get(AutoGain) >= 0.5f ? " + auto gain" : "",
            anas[std::clamp(static_cast<int>(std::lround(get(Analyzer))), 0, 2)]);
        s += b;
        // The user's band labels and notes (specs/049): what each band is for.
        for (int k = 0; k < kBands; ++k) {
            if (label_[k].empty() && note_[k].empty()) continue;
            std::snprintf(b, sizeof b, " | B%d", k + 1);
            s += b;
            if (!label_[k].empty()) s += " label \"" + label_[k] + "\"";
            if (!note_[k].empty()) s += " note \"" + note_[k] + "\"";
        }
        return s;
    }
    if (id == 1) {
        float sc[kTele];
        scopeRead(sc, kTele);
        auto lv = [](float v) {
            char t[24];
            if (v <= -119.0f) std::snprintf(t, sizeof t, "-inf");
            else
                std::snprintf(t, sizeof t, "%.1f", v);
            return std::string(t);
        };
        std::snprintf(b, sizeof b, "in %s dBFS - out %s dBFS - auto gain %+.1f dB%s - CPU %.2f %%",
            lv(sc[S_InPeak]).c_str(), lv(sc[S_OutPeak]).c_str(), sc[S_AutoGain], get(AutoGain) >= 0.5f ? "" : " (off)",
            sc[S_Cpu] * 100.0f);
        return b;
    }
    if (id == 2) {
        return "Raw units. Band n (1..8) params are named \"n <field>\": at index (n-1)*5 + field On (0/1), Type (0 "
               "Low cut, "
               "1 Low shelf, 2 Bell, 3 Notch, 4 High shelf, 5 High cut), Freq 20..20000 Hz, Gain -18..+18 dB (shelves "
               "and bells "
               "only), Q 0.1..18 (the resonance on the cuts, 0.71 = flat); 40..47 \"n Slope\" 0 = 12, 1 = 24, 2 = 48 "
               "dB/oct "
               "(cuts only); 48..55 \"n Channel\" 0 Stereo, 1 Mid, 2 Side, 3 Left, 4 Right. 56 Scale 0..200 % "
               "multiplies every "
               "shelf and bell gain. 57 Output -12..+12 dB. 58 Auto Gain 0/1 adds the opposite of the average level of "
               "the "
               "shelves and bells (cuts and notches do not count; max +-12 dB). 59 Analyzer 0 Pre, 1 Post, 2 Off "
               "(display only). 60 Analyzer Range 0 = 60, 1 = 90, 2 = 120 dB (display only). 61 Analyzer Peak "
               "0/1 peak hold (display only). 62 Labels 0/1 shows the band labels in the graph (display only). "
               "Band labels and notes are text, not params: set them with the band-text tool.";
    }
    return {};
}

namespace {

// Keep at most `max` UTF-8 code points, drop control characters (line breaks too unless
// `multiline`) and trim a label's surrounding spaces (a blank note is empty). Invalid bytes are dropped.
std::string cleanText(const std::string& in, size_t max, bool multiline) {
    std::string out;
    size_t cps = 0;
    for (size_t i = 0; i < in.size() && cps < max;) {
        const auto c = static_cast<unsigned char>(in[i]);
        const size_t len = c < 0x80 ? 1 : (c >> 5) == 0x6 ? 2 : (c >> 4) == 0xE ? 3 : (c >> 3) == 0x1E ? 4 : 0;
        if (len == 0 || i + len > in.size()) {
            ++i;
            continue;
        }
        bool ok = true;
        for (size_t k = 1; k < len; ++k)
            if ((static_cast<unsigned char>(in[i + k]) & 0xC0) != 0x80) ok = false;
        if (!ok) {
            ++i;
            continue;
        }
        if (len == 1 && (c < 0x20 || c == 0x7F)) { // control characters: only a note keeps line breaks
            i += 1;
            if (c == '\t' || c == '\n') { // a tab, or a line break in a label, reads as a space
                out += multiline && c == '\n' ? '\n' : ' ';
                ++cps;
            }
            continue;
        }
        out.append(in, i, len);
        i += len;
        ++cps;
    }
    const auto first = out.find_first_not_of(" \n");
    if (first == std::string::npos) return {};
    if (multiline) return out; // a note is kept exactly
    const auto last = out.find_last_not_of(" \n");
    return out.substr(first, last - first + 1);
}

} // namespace

void Eq::setBandText(int b, const std::string& label, const std::string& note) {
    if (b < 0 || b >= kBands) return;
    label_[b] = cleanText(label, kLabelMax, false);
    note_[b] = cleanText(note, kNoteMax, true);
}

// "NEQ1", then per band: u16 label length, label bytes, u16 note length, note bytes (UTF-8).
std::vector<uint8_t> Eq::getState() const {
    bool any = false;
    for (int b = 0; b < kBands; ++b) any |= !label_[b].empty() || !note_[b].empty();
    if (!any) return {};
    std::vector<uint8_t> v{'N', 'E', 'Q', '1'};
    auto put = [&v](const std::string& s) {
        const auto n = static_cast<uint16_t>(std::min<size_t>(s.size(), 0xFFFF));
        v.push_back(static_cast<uint8_t>(n & 0xFF));
        v.push_back(static_cast<uint8_t>(n >> 8));
        v.insert(v.end(), s.begin(), s.begin() + n);
    };
    for (int b = 0; b < kBands; ++b) {
        put(label_[b]);
        put(note_[b]);
    }
    return v;
}

void Eq::setState(const uint8_t* data, int32_t size) {
    std::string lab[kBands], nt[kBands];
    if (data && size >= 4 && std::memcmp(data, "NEQ1", 4) == 0) {
        size_t at = 4;
        const auto n = static_cast<size_t>(size);
        auto get = [&](std::string& out) {
            if (at + 2 > n) return false;
            const size_t len = data[at] | (static_cast<size_t>(data[at + 1]) << 8);
            at += 2;
            if (at + len > n) return false;
            out.assign(reinterpret_cast<const char*>(data + at), len);
            at += len;
            return true;
        };
        for (int b = 0; b < kBands; ++b)
            if (!get(lab[b]) || !get(nt[b])) {
                lab[b].clear();
                nt[b].clear();
                break;
            }
    }
    // Anything else (an empty blob, an unknown format) reads as no annotations.
    for (int b = 0; b < kBands; ++b) setBandText(b, lab[b], nt[b]);
}

} // namespace nota
