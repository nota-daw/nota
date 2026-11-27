// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginLoader — background binary preloader + freeze guard so plugin loads never block the UI.

#pragma once

#include <juce_audio_processors/juce_audio_processors.h>

#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <deque>
#include <functional>
#include <memory>
#include <mutex>
#include <set>
#include <string>
#include <thread>
#include <utility>
#include <vector>

namespace nota::pluginhost::detail {

enum class LoadState : int32_t {
    Loaded = 0,
    Waiting = 1,
    Loading = 2,
    NotResponding = 3,
    Failed = 4,
};

enum class LoadAction : int32_t {
    KeepWaiting = 1,
    GiveUp = 2,
    TryAgain = 3,
    Abandon = 4,
};

enum class LoadReason : int32_t {
    None = 0,
    WaitingFor = 1,
    NotResponding = 2,
    GaveUp = 3,
    RestartRequired = 4,
    FrozeNota = 5,
    Failed = 6,
    Removed = 7,
};

class PluginLoader;

class LoadTicket {
  public:
    using Finish = std::function<bool(std::string& error)>;

    LoadTicket(const juce::PluginDescription& d, Finish f) : desc(d), finish(std::move(f)) {}

    LoadState state() const { return state_; }
    LoadReason reason() const { return reason_; }
    const std::string& reasonArg() const { return reasonArg_; }

    const juce::PluginDescription desc;

  private:
    friend class PluginLoader;
    Finish finish;
    LoadState state_ = LoadState::Waiting;
    LoadState reported_ = LoadState::Waiting;
    LoadReason reason_ = LoadReason::None;
    std::string reasonArg_;
    std::chrono::steady_clock::time_point started_{};
    std::chrono::steady_clock::time_point deadline_{};
    bool cancelled_ = false;
    bool gaveUp_ = false;
    bool binaryDone_ = false;
    std::string binaryError_;
    uint32_t attempt_ = 0;
};

class PluginLoader {
  public:
    static PluginLoader& instance();

    std::shared_ptr<LoadTicket> request(const juce::PluginDescription& desc, LoadTicket::Finish finish);

    struct Serviced {
        int32_t changed = 0;
        int32_t attached = 0;
    };
    Serviced service();

    int32_t pending() const;
    void act(const std::shared_ptr<LoadTicket>& t, LoadAction a);
    void cancel(const std::shared_ptr<LoadTicket>& t);

    void setAutoloadScope(bool on) { autoloadScope_ += on ? 1 : (autoloadScope_ > 0 ? -1 : 0); }
    std::string takeFrozenNotice();

    static std::chrono::milliseconds timeout();

  private:
    PluginLoader();
    void run();
    void enqueue(const std::shared_ptr<LoadTicket>& t);
    void fail(LoadTicket& t, LoadReason reason, std::string arg = {});
    void finishOne(const std::shared_ptr<LoadTicket>& t, Serviced& s);
    std::vector<std::shared_ptr<LoadTicket>> poisonLocked();

    void readLeftoverRecord();
    bool frozen(const std::string& id) const { return frozenIds_.count(id) > 0; }
    void setFrozen(const std::string& id, bool on);
    void beginMainThreadStep(const LoadTicket& t);
    void endMainThreadStep();

    std::vector<std::weak_ptr<LoadTicket>> active_;

    mutable std::mutex mx_;
    std::condition_variable cv_;
    std::deque<std::shared_ptr<LoadTicket>> queue_;
    std::shared_ptr<LoadTicket> current_;
    std::chrono::steady_clock::time_point currentStarted_{};
    std::vector<std::shared_ptr<LoadTicket>> loaded_;
    bool finishing_ = false;
    bool stuck_ = false;

    int autoloadScope_ = 0;
    std::set<std::string> frozenIds_;
    std::string frozenNotice_;
    std::thread thread_;
};

} // namespace nota::pluginhost::detail
