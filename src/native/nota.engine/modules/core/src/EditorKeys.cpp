// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "nota/core/EditorKeys.h"

#include <cstdint>
#include <deque>
#include <mutex>
#include <vector>

namespace nota::core::editorkeys {
namespace {

struct State {
    std::mutex m;
    std::vector<int32_t> claimed;
    std::deque<EditorKeyEvent> queue;
};

State& state() {
    static State s;
    return s;
}

} // namespace

void setClaimed(const int32_t* chars, int32_t count) {
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    s.claimed.clear();
    for (int32_t i = 0; chars && i < count; ++i) s.claimed.push_back(tokenOf(chars[i]));
}

bool claims(int32_t ch) {
    if (ch <= 0) return false;
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    const int32_t t = tokenOf(ch);
    for (int32_t c : s.claimed) {
        if (c == t) return true;
    }
    return false;
}

void push(int32_t ch, bool down) {
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    if (s.queue.size() < 256u) s.queue.push_back({tokenOf(ch), down});
}

int32_t drain(int32_t* outChars, uint8_t* outDown, int32_t maxCount) {
    if (!outChars || !outDown || maxCount <= 0) return 0;
    auto& s = state();
    std::lock_guard<std::mutex> lock(s.m);
    int32_t n = 0;
    while (n < maxCount && !s.queue.empty()) {
        outChars[n] = s.queue.front().ch;
        outDown[n] = s.queue.front().down ? 1u : 0u;
        s.queue.pop_front();
        ++n;
    }
    return n;
}

} // namespace nota::core::editorkeys
