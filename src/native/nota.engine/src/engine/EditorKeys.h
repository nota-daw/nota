// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The computer keyboard inside a plug-in's editor window (#221). Native editor windows never
// reach the app's key handling, so the window catches the keys the app plays with — the note
// map, octave and velocity, as the user bound them — and queues them here; the app drains the
// queue on its UI tick and plays them exactly as it plays keys pressed over its own window.
// Keys travel as codes (Nota.Application.Input.EditorKeyCodes): a character key's character
// ('A'–'Z', '0'–'9', punctuation), a named key's AppKit function-key code (F1 = 0xF704, the
// arrows 0xF700…). The app says which ones it claims; everything else stays with the plug-in. Message / UI thread only
// (a mutex guards it: the editor windows and the app's tick are not one thread everywhere).

#pragma once

#include <cstdint>
#include <deque>
#include <mutex>
#include <vector>

namespace nota {
namespace editorkeys {

struct Event {
    int32_t ch;
    bool down;
};

struct State {
    std::mutex m;
    std::vector<int32_t> claimed;
    std::deque<Event> queue;
};

inline State& state() {
    static State s;
    return s;
}

// The code a typed character stands for: letters upper-case, the rest as they are.
inline int32_t tokenOf(int32_t ch) { return (ch >= 'a' && ch <= 'z') ? ch - 'a' + 'A' : ch; }

inline void setClaimed(const int32_t* chars, int32_t count) {
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    s.claimed.clear();
    for (int32_t i = 0; chars && i < count; ++i) s.claimed.push_back(tokenOf(chars[i]));
}

inline bool claims(int32_t ch) {
    if (ch <= 0) return false;
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    const int32_t t = tokenOf(ch);
    for (int32_t c : s.claimed)
        if (c == t) return true;
    return false;
}

inline void push(int32_t ch, bool down) {
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    if (s.queue.size() < 256) s.queue.push_back({tokenOf(ch), down});
}

inline int32_t drain(int32_t* outChars, uint8_t* outDown, int32_t max) {
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    int32_t n = 0;
    while (n < max && !s.queue.empty()) {
        outChars[n] = s.queue.front().ch;
        outDown[n] = s.queue.front().down ? 1 : 0;
        s.queue.pop_front();
        ++n;
    }
    return n;
}

} // namespace editorkeys
} // namespace nota
