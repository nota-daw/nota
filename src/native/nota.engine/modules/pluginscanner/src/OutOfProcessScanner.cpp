// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// OutOfProcessScanner implementation — JUCE-free POSIX spawn/poll & Win32 child probe runner.

#include "nota/pluginscanner/OutOfProcessScanner.h"

#include "nota/dsp/PluginContracts.h"
#include "nota/pluginscanner/wire/ScanWireCodec.h"

#include <algorithm>
#include <chrono>
#include <cstring>
#include <filesystem>
#include <mutex>
#include <string>
#include <string_view>
#include <system_error>
#include <utility>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#ifndef WIN32_LEAN_AND_MEAN
#define WIN32_LEAN_AND_MEAN
#endif
#include <condition_variable>
#include <memory>
#include <thread>
#include <windows.h>
#else
#include <cerrno>
#include <fcntl.h>
#include <poll.h>
#include <signal.h>
#include <spawn.h>
#include <sys/fcntl.h>
#include <sys/poll.h>
#include <sys/signal.h>
#if defined(__APPLE__)
#include <sys/spawn.h>
#endif
#include <sys/types.h>
#include <sys/wait.h>
#include <unistd.h>
#if defined(__APPLE__)
#include <crt_externs.h>
#endif
#endif

#if !defined(_WIN32) && !defined(__APPLE__)
extern char** environ;
#endif

namespace nota::pluginscanner {
namespace {

using Outcome = OutOfProcessScanner::Outcome;
using Clock = std::chrono::steady_clock;

struct Run {
    Outcome outcome = Outcome::StartFailed;
    std::string output;
    std::string detail;
};

bool endsWithSentinel(const std::string& s, size_t from) {
    constexpr size_t kEndLen = kScanWireEndSentinel.size();
    const size_t start = from > kEndLen ? from - kEndLen : 0;
    return s.find(kScanWireEndSentinel, start) != std::string::npos;
}

#if defined(_WIN32)

std::wstring utf8ToWide(const std::string& s) {
    if (s.empty()) return {};
    const int n = ::MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), nullptr, 0);
    if (n <= 0) return {};
    std::wstring w(static_cast<size_t>(n), L'\0');
    ::MultiByteToWideChar(CP_UTF8, 0, s.data(), static_cast<int>(s.size()), w.data(), n);
    return w;
}

std::wstring quoteArg(const std::wstring& arg) {
    std::wstring out = L"\"";
    for (const wchar_t ch : arg) {
        if (ch == L'"') out += L"\\\"";
        else
            out.push_back(ch);
    }
    out.push_back(L'"');
    return out;
}

struct WinProbe {
    HANDLE process = nullptr;
    HANDLE stdoutRead = nullptr;
    std::mutex m;
    std::condition_variable cv;
    std::string output;
    bool answered = false;
    bool drained = false;
};

void drainWinPipe(std::shared_ptr<WinProbe> p) {
    char buf[4096];
    for (;;) {
        DWORD readBytes = 0;
        const BOOL ok = ::ReadFile(p->stdoutRead, buf, sizeof(buf), &readBytes, nullptr);
        std::lock_guard<std::mutex> lock(p->m);
        if (!ok || readBytes == 0) {
            p->drained = true;
            p->cv.notify_all();
            return;
        }
        const size_t before = p->output.size();
        p->output.append(buf, static_cast<size_t>(readBytes));
        if (!p->answered && endsWithSentinel(p->output, before)) {
            p->answered = true;
            p->cv.notify_all();
        }
    }
}

Run runWorker(const std::string& worker, const std::string& format, const std::string& id, int timeoutMs) {
    Run run;
    SECURITY_ATTRIBUTES sa{};
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;

    HANDLE readPipe = nullptr;
    HANDLE writePipe = nullptr;
    HANDLE nulIn = nullptr;
    HANDLE nulErr = nullptr;
    PROCESS_INFORMATION pi{};

    {
        static std::mutex startMutex;
        std::lock_guard<std::mutex> startLock(startMutex);
        if (!::CreatePipe(&readPipe, &writePipe, &sa, 0)) return run;
        ::SetHandleInformation(readPipe, HANDLE_FLAG_INHERIT, 0);

        nulIn = ::CreateFileW(L"NUL", GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, &sa, OPEN_EXISTING, 0, nullptr);
        nulErr =
            ::CreateFileW(L"NUL", GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, &sa, OPEN_EXISTING, 0, nullptr);

        STARTUPINFOW si{};
        si.cb = sizeof(si);
        si.dwFlags = STARTF_USESTDHANDLES;
        si.hStdInput = nulIn;
        si.hStdOutput = writePipe;
        si.hStdError = nulErr;

        std::wstring cmdLine =
            quoteArg(utf8ToWide(worker)) + L" " + quoteArg(utf8ToWide(format)) + L" " + quoteArg(utf8ToWide(id));
        const BOOL started = ::CreateProcessW(
            nullptr, cmdLine.data(), nullptr, nullptr, TRUE, CREATE_NO_WINDOW, nullptr, nullptr, &si, &pi);

        ::CloseHandle(writePipe);
        if (nulIn != INVALID_HANDLE_VALUE && nulIn != nullptr) ::CloseHandle(nulIn);
        if (nulErr != INVALID_HANDLE_VALUE && nulErr != nullptr) ::CloseHandle(nulErr);

        if (!started) {
            ::CloseHandle(readPipe);
            run.detail = "CreateProcessW failed (" + std::to_string(::GetLastError()) + ")";
            return run;
        }
        ::CloseHandle(pi.hThread);
    }

    auto probe = std::make_shared<WinProbe>();
    probe->process = pi.hProcess;
    probe->stdoutRead = readPipe;

    std::thread reader(drainWinPipe, probe);
    {
        const auto deadline = Clock::now() + std::chrono::milliseconds(timeoutMs);
        std::unique_lock<std::mutex> lock(probe->m);
        probe->cv.wait_until(lock, deadline, [&] { return probe->answered || probe->drained; });
        run.outcome = probe->answered ? Outcome::Ok : probe->drained ? Outcome::NoAnswer : Outcome::Timeout;
        run.output = probe->output;
    }

    const bool running = (::WaitForSingleObject(pi.hProcess, 0) == WAIT_TIMEOUT);
    if (running) { ::TerminateProcess(pi.hProcess, 1); }
    bool drained = false;
    {
        std::unique_lock<std::mutex> lock(probe->m);
        drained = probe->cv.wait_for(lock, std::chrono::seconds(2), [&] { return probe->drained; });
    }
    if (drained) {
        reader.join();
    } else {
        reader.detach();
    }
    if (!running) {
        DWORD code = 0;
        if (::GetExitCodeProcess(pi.hProcess, &code)) { run.detail = "exit code " + std::to_string(code); }
    }
    ::CloseHandle(readPipe);
    ::CloseHandle(pi.hProcess);
    return run;
}

#else

char** currentEnvironment() {
#if defined(__APPLE__)
    return *_NSGetEnviron();
#else
    return ::environ;
#endif
}

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
        std::lock_guard<std::mutex> startLock(startMutex);
        if (::pipe(fds) != 0) return run;
        for (int fd : fds) ::fcntl(fd, F_SETFD, FD_CLOEXEC);

        posix_spawn_file_actions_t actions;
        posix_spawn_file_actions_init(&actions);
        posix_spawn_file_actions_addopen(&actions, 0, "/dev/null", O_RDONLY, 0);
        posix_spawn_file_actions_adddup2(&actions, fds[1], 1);
        posix_spawn_file_actions_addopen(&actions, 2, "/dev/null", O_WRONLY, 0);

        posix_spawnattr_t attr;
        posix_spawnattr_init(&attr);
        short flags = static_cast<short>(POSIX_SPAWN_SETSIGMASK | POSIX_SPAWN_SETSIGDEF);
#if defined(__APPLE__)
        flags = static_cast<short>(flags | POSIX_SPAWN_CLOEXEC_DEFAULT);
#endif
        posix_spawnattr_setflags(&attr, flags);
        sigset_t none;
        sigset_t defaults;
        sigemptyset(&none);
        posix_spawnattr_setsigmask(&attr, &none);
        sigemptyset(&defaults);
        sigaddset(&defaults, SIGPIPE);
        sigaddset(&defaults, SIGCHLD);
        posix_spawnattr_setsigdefault(&attr, &defaults);

        std::string a0 = worker;
        std::string a1 = format;
        std::string a2 = id;
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

    const auto deadline = Clock::now() + std::chrono::milliseconds(timeoutMs);
    int status = 0;
    bool known = false;
    bool gone = false;
    bool eof = false;
    char buf[4096];
    for (;;) {
        const auto left = std::chrono::duration_cast<std::chrono::milliseconds>(deadline - Clock::now()).count();
        if (left <= 0) {
            run.outcome = Outcome::Timeout;
            break;
        }
        if (eof) {
            if ((gone = reaped(pid, status, known))) {
                run.outcome = Outcome::NoAnswer;
                break;
            }
            ::usleep(10000);
            continue;
        }
        pollfd p{fds[0], POLLIN, 0};
        const int pollMs = static_cast<int>(std::min<long long>(left, 100));
        const int r = ::poll(&p, 1, pollMs);
        if (r < 0 && errno != EINTR) {
            eof = true;
            continue;
        }
        if (r <= 0) continue;
        const ssize_t n = ::read(fds[0], buf, sizeof(buf));
        if (n > 0) {
            const size_t before = run.output.size();
            run.output.append(buf, static_cast<size_t>(n));
            if (endsWithSentinel(run.output, before)) {
                run.outcome = Outcome::Ok;
                break;
            }
        } else if (n == 0 || (errno != EINTR && errno != EAGAIN)) {
            eof = true;
        }
    }
    ::close(fds[0]);

    bool killed = false;
    if (!gone && !(gone = reaped(pid, status, known))) {
        ::kill(pid, SIGKILL);
        killed = true;
        for (int i = 0; i < 200 && !(gone = reaped(pid, status, known)); ++i) ::usleep(10000);
    }
    if (!killed && known) {
        if (WIFEXITED(status)) {
            run.detail = "exit code " + std::to_string(WEXITSTATUS(status));
        } else if (WIFSIGNALED(status)) {
            run.detail = "crashed, signal " + std::to_string(WTERMSIG(status));
        }
    }
    return run;
}

#endif

} // namespace

OutOfProcessScanner::OutOfProcessScanner(std::string workerPath, int timeoutMs) noexcept
    : workerPath_(std::move(workerPath)), timeoutMs_(timeoutMs > 0 ? timeoutMs : kDefaultTimeoutMs) {}

bool OutOfProcessScanner::workerAvailable() const noexcept {
    if (workerPath_.empty()) return false;
    std::error_code ec;
    return std::filesystem::is_regular_file(std::filesystem::path(workerPath_), ec);
}

std::vector<dsp::PluginDescriptor> OutOfProcessScanner::scanDescriptions(
    std::string_view formatId, std::string_view candidateId, Outcome* outcome, std::string* detail) const noexcept {
    std::vector<dsp::PluginDescriptor> out;
    Run run;
    try {
        run = runWorker(workerPath_, std::string(formatId), std::string(candidateId), timeoutMs_);
        if (run.outcome == Outcome::Ok) {
            (void)decodeScanResults(run.output, out);
        } else {
            run.detail =
                std::to_string(run.output.size()) + " bytes of output" + (run.detail.empty() ? "" : ", " + run.detail);
        }
    } catch (...) {
        out.clear();
        run.outcome = Outcome::NoAnswer;
        run.detail = "exception in the scanner";
    }
    if (outcome != nullptr) *outcome = run.outcome;
    if (detail != nullptr) *detail = run.outcome == Outcome::Ok ? std::string() : run.detail;
    return out;
}

} // namespace nota::pluginscanner
