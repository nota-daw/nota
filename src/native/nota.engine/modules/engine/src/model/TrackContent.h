// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// TrackContent — Copy-on-Write slice wrapper for heavy Track snapshot containers.
// Cloning a Track shares immutable vector backing storage in O(1) across snapshots;
// mutations detach only the modified slice via .write() while read-only access and
// audio-thread traversal stay allocation- and lock-free (AR-6).

#pragma once

#include <cstddef>
#include <memory>
#include <utility>
#include <vector>

namespace nota::detail {

template <typename T> class CowSlice {
  public:
    CowSlice() = default;
    explicit CowSlice(std::vector<T> init) {
        if (!init.empty()) storage_ = std::make_shared<std::vector<T>>(std::move(init));
    }

    // Copy clones only the storage handle and marks the copy sealed so the next mutation detaches.
    CowSlice(const CowSlice& other) noexcept : storage_(other.storage_), sealed_(true) {}
    CowSlice& operator=(const CowSlice& other) noexcept {
        if (this != &other) {
            storage_ = other.storage_;
            sealed_ = true;
        }
        return *this;
    }

    CowSlice(CowSlice&& other) noexcept : storage_(std::move(other.storage_)), sealed_(other.sealed_) {
        other.sealed_ = false;
    }
    CowSlice& operator=(CowSlice&& other) noexcept {
        if (this != &other) {
            storage_ = std::move(other.storage_);
            sealed_ = other.sealed_;
            other.sealed_ = false;
        }
        return *this;
    }

    // Allocation-free, thread-safe read access across live/historical snapshots.
    const std::vector<T>& read() const noexcept {
        static const std::vector<T> kEmpty;
        return storage_ ? *storage_ : kEmpty;
    }

    bool empty() const noexcept { return !storage_ || storage_->empty(); }
    size_t size() const noexcept { return storage_ ? storage_->size() : 0; }
    const T& operator[](size_t i) const { return (*storage_)[i]; }
    const T& at(size_t i) const { return storage_->at(i); }
    const T& front() const { return storage_->front(); }
    const T& back() const { return storage_->back(); }
    auto begin() const noexcept { return read().begin(); }
    auto end() const noexcept { return read().end(); }
    auto cbegin() const noexcept { return read().cbegin(); }
    auto cend() const noexcept { return read().cend(); }
    const T* data() const noexcept { return storage_ ? storage_->data() : nullptr; }

    // Stable identity of the underlying vector storage (used by TimelineIndex CoW matching).
    const void* storageToken() const noexcept { return storage_.get(); }
    bool sharesStorageWith(const CowSlice<T>& other) const noexcept { return storage_.get() == other.storage_.get(); }

    // Explicit Copy-on-Write detach: copies elements only when shared with another snapshot or sealed.
    std::vector<T>& write() {
        if (!storage_) {
            storage_ = std::make_shared<std::vector<T>>();
            sealed_ = false;
        } else if (sealed_ || storage_.use_count() > 1) {
            storage_ = std::make_shared<std::vector<T>>(*storage_);
            sealed_ = false;
        }
        return *storage_;
    }

    void replace(std::vector<T> next) {
        if (next.empty()) {
            storage_.reset();
            sealed_ = false;
            return;
        }
        storage_ = std::make_shared<std::vector<T>>(std::move(next));
        sealed_ = false;
    }

    void clear() noexcept {
        storage_.reset();
        sealed_ = false;
    }

    // Mark sealed on graph publish so any future mutation detaches cleanly without refcount churn.
    void seal() noexcept { sealed_ = true; }

  private:
    std::shared_ptr<std::vector<T>> storage_;
    bool sealed_ = false;
};

} // namespace nota::detail
