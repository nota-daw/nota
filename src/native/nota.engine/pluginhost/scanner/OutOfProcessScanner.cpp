// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner implementation. Mirrors scanOne() in the former
// PluginHost.cpp; adds workerAvailable() and a timeout that actually fires.
//
// juce::ChildProcess::readProcessOutput blocks until the requested byte count is
// read or the child exits (Windows: a PeekNamedPipe/Sleep loop; POSIX: fread), so
// a deadline checked between reads never ran while a worker hung — one stuck
// plugin hung the whole scan (bug plugin-scan-hang-windows). Now:
//  - POSIX: our own posix_spawn + poll(). JUCE's POSIX ActiveProcess is unusable
//    with probes in parallel: its destructor fclose()s the pipe and then close()s
//    the same fd number again, closing whatever another thread opened meanwhile
//    (a sibling probe's pipe, or any file of the app). Close-on-exec everywhere,
//    so a worker never holds another probe's pipe open.
//  - Windows: juce::ChildProcess (its handles are closed once each), with the pipe
//    drained on a reader thread one byte at a time — so the end sentinel is seen as
//    soon as it is written — while this thread waits with the deadline.
// Either way a worker that answered is not waited for: a plugin that deadlocks
// on unload after answering costs nothing.

#include "OutOfProcessScanner.h"

#include <juce_audio_processors/juce_audio_processors.h>

#include <chrono>
#include <cstring>
#include <mutex>
#include <string>

#if JUCE_WINDOWS
#include <condition_variable>
#include <thread>
#else
#include <cerrno>
#include <csignal>
#include <fcntl.h>
#include <poll.h>
#include <spawn.h>
#include <sys/wait.h>
#include <unistd.h>
#if JUCE_MAC
#include <crt_externs.h>
#endif
#endif

#if !JUCE_WINDOWS && !JUCE_MAC
// Global scope on purpose: declared inside the namespaces below it would name
// nota::scanner::(anonymous)::environ, which nothing defines (the .so fails to link).
extern char** environ;
#endif

namespace nota {
namespace scanner {

namespace {

constexpr const char* kBegin = "<<<NOTA_PLUGINS_BEGIN>>>";
constexpr const char* kEnd = "<<<NOTA_PLUGINS_END>>>";
using Outcome = OutOfProcessScanner::Outcome;
using clock = std::chrono::steady_clock;

// What one worker run produced, before parsing.
struct Run {
    Outcome outcome = Outcome::StartFailed;
    std::string output;
    std::string detail;
};

bool endsWithSentinel(const std::string& s, size_t from) {
    const size_t len = std::char_traits<char>::length(kEnd);
    return s.find(kEnd, from > len ? from - len : 0) != std::string::npos;
}

#if JUCE_WINDOWS

// Shared with the reader thread; outlives the call if the reader must be detached.
struct Probe {
    juce::ChildProcess proc;
    std::mutex m;
    std::condition_variable cv;
    std::string output;
    bool answered = false; // end sentinel seen
    bool drained = false;  // pipe closed / child gone — reader finished
};

void drain(std::shared_ptr<Probe> p) {
    char c = 0;
    for (;;) {
        const int n = p->proc.readProcessOutput(&c, 1);
        std::lock_guard lock(p->m);
        if (n <= 0) {
            p->drained = true;
            p->cv.notify_all();
            return;
        }
        p->output.push_back(c);
        if (!p->answered && c == '>' && endsWithSentinel(p->output, p->output.size() - 1)) {
            p->answered = true;
            p->cv.notify_all();
        }
    }
}

Run runWorker(const std::string& worker, const std::string& format, const std::string& id, int timeoutMs) {
    Run run;
    auto probe = std::make_shared<Probe>();
    const juce::StringArray cmd{juce::String::fromUTF8(worker.c_str()), juce::String::fromUTF8(format.c_str()),
        juce::String::fromUTF8(id.c_str())};
    {
        // Probes run in parallel; one CreateProcess at a time keeps a child from
        // inheriting another probe's pipe mid-start.
        static std::mutex startMutex;
        std::lock_guard startLock(startMutex);
        if (!probe->proc.start(cmd, juce::ChildProcess::wantStdOut)) return run;
    }

    std::thread reader(drain, probe);
    {
        const auto deadline = clock::now() + std::chrono::milliseconds(timeoutMs);
        std::unique_lock lock(probe->m);
        probe->cv.wait_until(lock, deadline, [&] { return probe->answered || probe->drained; });
        run.outcome = probe->answered ? Outcome::Ok : probe->drained ? Outcome::NoAnswer : Outcome::Timeout;
        run.output = probe->output;
    }
    const bool killed = probe->proc.isRunning();
    if (killed) probe->proc.kill();
    bool drained = false;
    {
        std::unique_lock lock(probe->m);
        drained = probe->cv.wait_for(lock, std::chrono::seconds(2), [&] { return probe->drained; });
    }
    // Pipe still open (a grandchild inherited it): leave the reader blocked on its
    // own copy of the probe rather than hang here.
    if (drained) reader.join();
    else reader.detach();
    if (!killed) run.detail = "exit code " + std::to_string((int)probe->proc.getExitCode());
    return run;
}

#else

char** currentEnvironment() {
#if JUCE_MAC
    return *_NSGetEnviron(); // `environ` isn't reachable from a dylib on macOS
#else
    return ::environ;
#endif
}

// waitpid without blocking. True once the child is gone; `status` is valid when
// `known`. ECHILD (someone else reaped it) counts as gone, status unknown.
bool reaped(pid_t pid, int& status, bool& known) {
    for (;;) {
        const pid_t r = ::waitpid(pid, &status, WNOHANG);
        if (r == pid) {
            known = true;
            return true;
        }
        if (r == 0) return false;
        if (errno == EINTR) continue;
        known = false;
        return true;
    }
}

Run runWorker(const std::string& worker, const std::string& format, const std::string& id, int timeoutMs) {
    Run run;
    int fds[2] = {-1, -1};
    pid_t pid = 0;
    {
        // One spawn at a time: between pipe() and FD_CLOEXEC no sibling may fork.
        static std::mutex startMutex;
        std::lock_guard startLock(startMutex);
        if (::pipe(fds) != 0) return run;
        for (int fd : fds) ::fcntl(fd, F_SETFD, FD_CLOEXEC);

        posix_spawn_file_actions_t actions;
        posix_spawn_file_actions_init(&actions);
        posix_spawn_file_actions_addopen(&actions, 0, "/dev/null", O_RDONLY, 0);
        posix_spawn_file_actions_adddup2(&actions, fds[1], 1);
        posix_spawn_file_actions_addopen(&actions, 2, "/dev/null", O_WRONLY, 0);

        posix_spawnattr_t attr;
        posix_spawnattr_init(&attr);
        short flags = POSIX_SPAWN_SETSIGMASK | POSIX_SPAWN_SETSIGDEF;
#if JUCE_MAC
        flags |= POSIX_SPAWN_CLOEXEC_DEFAULT; // the worker gets stdin/out/err and nothing else
#endif
        posix_spawnattr_setflags(&attr, flags);
        sigset_t none, defaults;
        sigemptyset(&none);
        posix_spawnattr_setsigmask(&attr, &none);
        sigemptyset(&defaults); // the host may ignore these; the worker shouldn't inherit that
        sigaddset(&defaults, SIGPIPE);
        sigaddset(&defaults, SIGCHLD);
        posix_spawnattr_setsigdefault(&attr, &defaults);

        std::string a0 = worker, a1 = format, a2 = id;
        char* argv[] = {a0.data(), a1.data(), a2.data(), nullptr};
        const int err = ::posix_spawn(&pid, worker.c_str(), &actions, &attr, argv, currentEnvironment());
        posix_spawn_file_actions_destroy(&actions);
        posix_spawnattr_destroy(&attr);
        ::close(fds[1]);
        if (err != 0) {
            ::close(fds[0]);
            run.detail = std::string("spawn failed: ") + std::strerror(err);
            return run;
        }
    }

    const auto deadline = clock::now() + std::chrono::milliseconds(timeoutMs);
    int status = 0;
    bool known = false, gone = false, eof = false;
    char buf[4096];
    for (;;) {
        const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - clock::now()).count();
        if (left <= 0) {
            run.outcome = Outcome::Timeout;
            break;
        }
        if (eof) {
            // stdout closed without the answer: wait for the worker to exit (a crash).
            if ((gone = reaped(pid, status, known))) {
                run.outcome = Outcome::NoAnswer;
                break;
            }
            ::usleep(10000);
            continue;
        }
        pollfd p{fds[0], POLLIN, 0};
        const int r = ::poll(&p, 1, (int)std::min<long long>(left, 100));
        if (r < 0 && errno != EINTR) {
            eof = true;
            continue;
        }
        if (r <= 0) continue;
        const ssize_t n = ::read(fds[0], buf, sizeof buf);
        if (n > 0) {
            const size_t before = run.output.size();
            run.output.append(buf, (size_t)n);
            if (endsWithSentinel(run.output, before)) {
                run.outcome = Outcome::Ok;
                break;
            }
        } else if (n == 0 || (errno != EINTR && errno != EAGAIN)) {
            eof = true;
        }
    }
    ::close(fds[0]);

    // Answered or hung: not waited for. Kill only while it is provably ours and
    // unreaped (a reaped pid may already belong to another process).
    bool killed = false;
    if (!gone && !(gone = reaped(pid, status, known))) {
        ::kill(pid, SIGKILL);
        killed = true;
        // Reap it: a thousand probes must not leave a thousand zombies behind.
        for (int i = 0; i < 200 && !(gone = reaped(pid, status, known)); ++i) ::usleep(10000);
    }
    if (!killed && known) {
        if (WIFEXITED(status)) run.detail = "exit code " + std::to_string(WEXITSTATUS(status));
        else if (WIFSIGNALED(status)) run.detail = "crashed, signal " + std::to_string(WTERMSIG(status));
    }
    return run;
}

#endif

} // namespace

OutOfProcessScanner::OutOfProcessScanner(std::string workerPath, int timeoutMs) noexcept
    : workerPath_(std::move(workerPath)), timeoutMs_(timeoutMs > 0 ? timeoutMs : kDefaultTimeoutMs) {}

bool OutOfProcessScanner::workerAvailable() const noexcept {
    if (workerPath_.empty()) return false;
    return juce::File(juce::CharPointer_UTF8(workerPath_.c_str())).existsAsFile();
}

std::vector<std::unique_ptr<juce::PluginDescription>> OutOfProcessScanner::scanDescriptions(
    const std::string& formatName, const std::string& fileOrId, Outcome* outcome, std::string* detail) const noexcept {
    std::vector<std::unique_ptr<juce::PluginDescription>> out;
    Run run;
    try {
        run = runWorker(workerPath_, formatName, fileOrId, timeoutMs_);
        if (run.outcome == Outcome::Ok) {
            const juce::String text = juce::String::fromUTF8(run.output.data(), (int)run.output.size());
            const int b = text.indexOf(kBegin);
            const int e = text.lastIndexOf(kEnd);
            if (b >= 0 && e > b) {
                if (auto xml = juce::parseXML(text.substring(b + (int)juce::String(kBegin).length(), e))) {
                    for (auto* child : xml->getChildIterator()) {
                        auto desc = std::make_unique<juce::PluginDescription>();
                        if (desc->loadFromXml(*child)) out.push_back(std::move(desc));
                    }
                }
            }
        } else {
            run.detail = std::to_string(run.output.size()) + " bytes of output" +
                         (run.detail.empty() ? "" : ", " + run.detail);
        }
    } catch (...) {
        // A crashing/hanging plugin must never unwind into the caller (AR-7:
        // no exceptions cross the C ABI; abi_pluginscanner is noexcept).
        out.clear();
        run.outcome = Outcome::NoAnswer;
        run.detail = "exception in the scanner";
    }
    if (outcome != nullptr) *outcome = run.outcome;
    if (detail != nullptr) *detail = run.outcome == Outcome::Ok ? std::string() : run.detail;
    return out;
}

} // namespace scanner
} // namespace nota
