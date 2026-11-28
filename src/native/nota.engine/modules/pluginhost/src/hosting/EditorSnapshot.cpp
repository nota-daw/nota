// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

#include "hosting/EditorSnapshot.h"

#include "hosting/MacBridges.h"

#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>
#include <vector>

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

namespace nota::pluginhost::detail {
namespace {

constexpr int kShortSide = 520;
constexpr int kLongSide = 1040;

[[maybe_unused]] juce::Point<int> targetSize(int w, int h) {
    double s = std::min(1.0, static_cast<double>(kShortSide) / std::max(1, std::min(w, h)));
    if (std::max(w, h) * s > kLongSide) s = static_cast<double>(kLongSide) / std::max(1, std::max(w, h));
    return {std::max(1, static_cast<int>(std::lround(w * s))), std::max(1, static_cast<int>(std::lround(h * s)))};
}

[[maybe_unused]] juce::Image blankImage(juce::Point<int> size) {
    return juce::Image(juce::Image::ARGB, size.x, size.y, true, juce::SoftwareImageType());
}

bool looksBlank(const juce::Image& img) {
    float lo = 1.0f;
    float hi = 0.0f;
    for (int j = 0; j < 16; ++j) {
        for (int i = 0; i < 16; ++i) {
            const auto c = img.getPixelAt((2 * i + 1) * img.getWidth() / 32, (2 * j + 1) * img.getHeight() / 32);
            const float l =
                (0.299f * c.getFloatRed() + 0.587f * c.getFloatGreen() + 0.114f * c.getFloatBlue()) * c.getFloatAlpha();
            lo = std::min(lo, l);
            hi = std::max(hi, l);
        }
    }
    return hi - lo < 6.0f / 255.0f;
}

std::vector<uint8_t> encode(const juce::Image& img) {
    juce::MemoryOutputStream out;
    juce::JPEGImageFormat jpeg;
    jpeg.setQuality(0.8f);
    if (!jpeg.writeImageToStream(img, out)) return {};
    const auto* p = static_cast<const uint8_t*>(out.getData());
    return {p, p + out.getDataSize()};
}

juce::Image capture(juce::DocumentWindow& w) {
#if defined(__APPLE__)
    auto* content = w.getContentComponent();
    if (content == nullptr) return {};
    const auto b = content->getBounds();
    std::vector<uint8_t> bgra;
    int width = 0;
    int height = 0;
    if (!mac::captureEditorImage(w.getWindowHandle(), b.getX(), b.getY(), b.getWidth(), b.getHeight(), kShortSide,
            kLongSide, bgra, width, height) ||
        width <= 0 || height <= 0) {
        return {};
    }
    auto img = blankImage({width, height});
    juce::Image::BitmapData bd(img, juce::Image::BitmapData::writeOnly);
    const size_t rowBytes = static_cast<size_t>(width) * 4u;
    for (int y = 0; y < height; ++y) {
        std::memcpy(bd.getLinePointer(y), bgra.data() + static_cast<size_t>(y) * rowBytes, rowBytes);
    }
    return img;
#elif defined(_WIN32)
    auto hwnd = static_cast<HWND>(w.getWindowHandle());
    auto* content = w.getContentComponent();
    if (hwnd == nullptr || content == nullptr || ::IsIconic(hwnd) || !::IsWindowVisible(hwnd) || w.getWidth() <= 0) {
        return {};
    }
    RECT rc{};
    ::GetClientRect(hwnd, &rc);
    const int cw = rc.right - rc.left;
    const int ch = rc.bottom - rc.top;
    if (cw <= 0 || ch <= 0) return {};

    const double k = static_cast<double>(cw) / w.getWidth();
    const auto b = content->getBounds();
    const int bx = std::clamp(static_cast<int>(std::lround(b.getX() * k)), 0, cw - 1);
    const int by = std::clamp(static_cast<int>(std::lround(b.getY() * k)), 0, ch - 1);
    const int bw = std::min(cw - bx, static_cast<int>(std::lround(b.getWidth() * k)));
    const int bh = std::min(ch - by, static_cast<int>(std::lround(b.getHeight() * k)));
    if (bw <= 0 || bh <= 0) return {};

#ifndef PW_RENDERFULLCONTENT
    constexpr UINT PW_RENDERFULLCONTENT = 0x00000002;
#endif
    juce::Image img;
    HDC screen = ::GetDC(nullptr);
    HDC full = ::CreateCompatibleDC(screen);
    HBITMAP fullBmp = ::CreateCompatibleBitmap(screen, cw, ch);
    HGDIOBJ oldFull = ::SelectObject(full, fullBmp);
    if (::PrintWindow(hwnd, full, PW_CLIENTONLY | PW_RENDERFULLCONTENT)) {
        const auto t = targetSize(bw, bh);
        BITMAPINFO bi{};
        bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bi.bmiHeader.biWidth = t.x;
        bi.bmiHeader.biHeight = -t.y;
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HDC scaledDc = ::CreateCompatibleDC(screen);
        HBITMAP scaledBmp = ::CreateDIBSection(scaledDc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (scaledBmp != nullptr && bits != nullptr) {
            HGDIOBJ oldScaled = ::SelectObject(scaledDc, scaledBmp);
            ::SetStretchBltMode(scaledDc, HALFTONE);
            ::SetBrushOrgEx(scaledDc, 0, 0, nullptr);
            ::StretchBlt(scaledDc, 0, 0, t.x, t.y, full, bx, by, bw, bh, SRCCOPY);
            ::GdiFlush();
            img = blankImage(t);
            juce::Image::BitmapData bd(img, juce::Image::BitmapData::writeOnly);
            for (int y = 0; y < t.y; ++y) {
                const auto* src =
                    static_cast<const uint8_t*>(bits) + static_cast<size_t>(y) * static_cast<size_t>(t.x) * 4u;
                auto* dst = bd.getLinePointer(y);
                for (int x = 0; x < t.x; ++x) {
                    dst[x * 4 + 0] = src[x * 4 + 0];
                    dst[x * 4 + 1] = src[x * 4 + 1];
                    dst[x * 4 + 2] = src[x * 4 + 2];
                    dst[x * 4 + 3] = 255;
                }
            }
            ::SelectObject(scaledDc, oldScaled);
        }
        if (scaledBmp != nullptr) ::DeleteObject(scaledBmp);
        ::DeleteDC(scaledDc);
    }
    ::SelectObject(full, oldFull);
    ::DeleteObject(fullBmp);
    ::DeleteDC(full);
    ::ReleaseDC(nullptr, screen);
    return img;
#else
    (void)w;
    return {};
#endif
}

} // namespace

std::vector<uint8_t> snapshotEditor(juce::DocumentWindow& window) {
    const auto img = capture(window);
    if (!img.isValid() || looksBlank(img)) return {};
    return encode(img);
}

} // namespace nota::pluginhost::detail
