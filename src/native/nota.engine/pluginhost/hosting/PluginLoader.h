// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// PluginLoader — hosted plug-ins load without blocking the UI (#979, specs/113).
//
// A plug-in's binary used to load on the message thread, inside the insert call. On macOS that
// is the process main thread, so a binary that blocks while loading — a copy-protection wrapper
// (PACE / iLok) waiting for the main run loop, a licence check, a dialog — froze Nota until
// force-quit. Now an adapter (PluginEffect / PluginInstrument) is created unloaded and hands its
// catalog description here: one loader thread loads the binary (the step where such wrappers
// run) while the main thread keeps servicing its run loop; service(), on the message thread,
// then creates the instance from the already-loaded binary and attaches it. A load past its
// deadline is reported "not responding" until the user keeps waiting or gives up.
//
// A main-thread step that still blocks is caught next time: its plug-in is written to
// plugin-loading.txt for the step's duration, and a leftover file at start marks that plug-in
// as having frozen Nota (plugins-froze-nota.txt) — a project opens it as "couldn't load".

#pragma once

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
#include <vector>

#include <juce_audio_processors/juce_audio_processors.h>

namespace nota {
namespace hosting {

// pluginLoadState() values, as the C ABI reports them (nota_engine.h).
enum class LoadState : int32_t {
    Loaded = 0,        // or not a hosted plug-in
    Waiting = 1,       // in line behind another plug-in's binary load
    Loading = 2,       // binary loading / instance being created
    NotResponding = 3, // still loading past its deadline
    Failed = 4,        // couldn't load (reason says why)
};

// pluginLoadAction() values. Abandon is the engine's, not the card's: the device left the
// project while it loaded (removed, its track deleted, the project closed).
enum class LoadAction : int32_t { KeepWaiting = 1, GiveUp = 2, TryAgain = 3, Abandon = 4 };

// pluginLoadReasonCode() values, as the C ABI reports them: the app words them (Loc), with
// pluginLoadReasonArg() filled in where it says.
enum class LoadReason : int32_t {
    None = 0,
    WaitingFor = 1,      // arg: the plug-in whose binary is loading now
    NotResponding = 2,   // arg: this plug-in's name
    GaveUp = 3,          // stopped responding; the user gave up on it
    RestartRequired = 4, // a load that never returned blocks loading until a restart
    FrozeNota = 5,       // it was loading when Nota last ended; held back on project open
    Failed = 6,          // arg: the format's message (may be empty)
    Removed = 7,         // it left the project while it loaded
};

class PluginLoader;

// One plug-in being loaded for one adapter. The adapter owns it; the loader keeps it while the
// binary loads. Everything but the fields marked shared is message-thread only.
class LoadTicket {
  public:
    // Creates the instance from the loaded binary and attaches it; false + `error` on failure.
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
    LoadState reported_ = LoadState::Waiting; // the state service() last counted as a change
    LoadReason reason_ = LoadReason::None;
    std::string reasonArg_;
    std::chrono::steady_clock::time_point started_{}, deadline_{};
    // Shared with the loader thread, under the loader's mutex:
    bool cancelled_ = false;  // the adapter is gone: drop the result
    bool gaveUp_ = false;     // the user gave up on it
    bool binaryDone_ = false; // the loader thread is through with it
    std::string binaryError_;
    // Bumped by every enqueue. Try again on a ticket whose earlier binary load is still in
    // flight (given up or removed, then undone) queues it anew: the earlier load's result is
    // stale when it returns and is dropped, so only one attempt ever reaches finish().
    uint32_t attempt_ = 0;
};

class PluginLoader {
  public:
    // Process-lifetime, never destroyed: a loader thread stuck in a binary must not be joined
    // at exit.
    static PluginLoader& instance();

    // Message thread. Starts loading `desc` for an adapter; the ticket is already Failed when the
    // plug-in froze Nota before and an autoload scope (project open) is active, or when a stuck
    // load blocks loading altogether.
    std::shared_ptr<LoadTicket> request(const juce::PluginDescription& desc, LoadTicket::Finish finish);

    // Message thread. Finishes loaded binaries (instance creation), turns overdue loads to
    // "not responding". Returns how many tickets changed state, and how many attached.
    struct Serviced {
        int32_t changed = 0;
        int32_t attached = 0;
    };
    Serviced service();

    // Message thread: tickets not yet loaded or failed.
    int32_t pending() const;

    // Message thread: what the device card's buttons do.
    void act(const std::shared_ptr<LoadTicket>& t, LoadAction a);

    // Message thread: the adapter is going away; its result, if any, is dropped.
    void cancel(const std::shared_ptr<LoadTicket>& t);

    // Project open: inside the scope, a plug-in that froze Nota last time isn't loaded.
    void setAutoloadScope(bool on) { autoloadScope_ += on ? 1 : (autoloadScope_ > 0 ? -1 : 0); }

    // The plug-in a leftover plugin-loading.txt named (it froze Nota last time), once; empty
    // otherwise.
    std::string takeFrozenNotice();

    // The deadline for one load (30 s; NOTA_PLUGIN_LOAD_TIMEOUT_MS overrides it).
    static std::chrono::milliseconds timeout();

  private:
    PluginLoader();
    void run(); // the loader thread
    void enqueue(const std::shared_ptr<LoadTicket>& t);
    void fail(LoadTicket& t, LoadReason reason, std::string arg = {});
    void finishOne(const std::shared_ptr<LoadTicket>& t, Serviced& s);
    // Under mx_: the load in progress is abandoned (given up, its device gone) and past its
    // deadline; nothing behind it can load until it returns, so stop and hand back what waits.
    std::vector<std::shared_ptr<LoadTicket>> poisonLocked();

    // Freeze guard.
    void readLeftoverRecord();
    bool frozen(const std::string& id) const { return frozenIds_.count(id) > 0; }
    void setFrozen(const std::string& id, bool on);
    void beginMainThreadStep(const LoadTicket& t);
    void endMainThreadStep();

    // Every live ticket, for deadlines and pending() (message thread).
    std::vector<std::weak_ptr<LoadTicket>> active_;

    mutable std::mutex mx_; // the queue and the binary-load hand-off with the loader thread
    std::condition_variable cv_;
    std::deque<std::shared_ptr<LoadTicket>> queue_;
    std::shared_ptr<LoadTicket> current_;            // binary load in progress
    std::chrono::steady_clock::time_point currentStarted_{}; // when the loader thread took it
    std::vector<std::shared_ptr<LoadTicket>> loaded_; // binary done, waiting for service()
    bool stuck_ = false; // an abandoned load is past its deadline and hasn't returned

    int autoloadScope_ = 0;
    std::set<std::string> frozenIds_;
    std::string frozenNotice_;
    std::thread thread_;
};

// Loads the binary `desc` names into the process, the way its format will (no plug-in entry
// point, no instance), and keeps it loaded. Any thread; best effort: false + `error` when it
// can't, and the main-thread step then loads it itself.
bool preloadBinary(const juce::PluginDescription& desc, std::string& error);

} // namespace hosting
} // namespace nota
