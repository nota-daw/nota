// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// EditorSnapshot implementation (specs/046, research R1–R5). One source, ObjC++ on macOS like
// PluginEditorWindow.cpp. Each platform captures the editor's content and scales it to card
// size in the same pass (CoreGraphics / GDI), so no full-size copy is kept; the blank check
// and the JPEG encode are shared.

#ifdef _WIN32
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#endif

#include "EditorSnapshot.h"

#include <algorithm>
#include <cmath>

#if JUCE_MAC
#import <AppKit/AppKit.h>
#include <dlfcn.h>
#endif

namespace nota {
namespace hosting {
namespace {

// The S card is 260 pt square: 520 px covers it at 2x. A very wide or tall editor keeps its
// longer side under 1040 px so a preview stays well under 100 KB.
constexpr int kShortSide = 520, kLongSide = 1040;

[[maybe_unused]] juce::Point<int> targetSize(int w, int h) {
    double s = std::min(1.0, static_cast<double>(kShortSide) / std::min(w, h)); // never upscale
    if (std::max(w, h) * s > kLongSide) s = static_cast<double>(kLongSide) / std::max(w, h);
    return {std::max(1, static_cast<int>(std::lround(w * s))), std::max(1, static_cast<int>(std::lround(h * s)))};
}

[[maybe_unused]] juce::Image blankImage(juce::Point<int> size) {
    return juce::Image(juce::Image::ARGB, size.x, size.y, true, juce::SoftwareImageType());
}

// A window that hasn't drawn yet, or a GPU view the capture can't see, comes back as one
// flat colour: 16 x 16 samples whose luma spans less than 6/255.
bool looksBlank(const juce::Image& img) {
    float lo = 1.0f, hi = 0.0f;
    for (int j = 0; j < 16; ++j)
        for (int i = 0; i < 16; ++i) {
            const auto c = img.getPixelAt((2 * i + 1) * img.getWidth() / 32, (2 * j + 1) * img.getHeight() / 32);
            const float l = (0.299f * c.getFloatRed() + 0.587f * c.getFloatGreen() + 0.114f * c.getFloatBlue()) *
                            c.getFloatAlpha();
            lo = std::min(lo, l);
            hi = std::max(hi, l);
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

#if JUCE_MAC
// CGWindowListCreateImage is marked unavailable from the macOS 15 SDK on, but still works for
// a process's own windows (no permission prompt) and, unlike the NSView/CALayer drawing APIs,
// sees Metal and OpenGL content. Looked up at run time; if Apple removes it we simply have no
// preview.
using WindowImageFn = CGImageRef (*)(CGRect, uint32_t, uint32_t, uint32_t);

juce::Image capture(juce::DocumentWindow& w) {
    static const auto windowImage = reinterpret_cast<WindowImageFn>(dlsym(RTLD_DEFAULT, "CGWindowListCreateImage"));
    auto* view = static_cast<NSView*>(w.getWindowHandle());
    auto* content = w.getContentComponent();
    NSWindow* win = view != nil ? view.window : nil;
    if (windowImage == nullptr || win == nil || content == nullptr || !win.isVisible || win.isMiniaturized) return {};

    constexpr uint32_t kIncludingWindow = 1u << 3, kIgnoreFraming = 1u << 0, kBestResolution = 1u << 3;
    CGImageRef shot = windowImage(CGRectNull, kIncludingWindow, static_cast<uint32_t>(win.windowNumber),
        kIgnoreFraming | kBestResolution);
    if (shot == nullptr) return {};

    // The shot is the whole window, title bar included: crop to the editor component. The
    // peer view is the window's content view, so the component's bounds are relative to it.
    const NSRect frame = win.frame, inner = [win contentRectForFrameRect:frame];
    const double sx = CGImageGetWidth(shot) / frame.size.width, sy = CGImageGetHeight(shot) / frame.size.height;
    const auto b = content->getBounds();
    const double ox = (inner.origin.x - frame.origin.x) * sx, oy = (NSMaxY(frame) - NSMaxY(inner)) * sy;
    const CGRect crop = CGRectIntersection(
        CGRectMake(std::round(ox + b.getX() * sx), std::round(oy + b.getY() * sy), std::round(b.getWidth() * sx),
            std::round(b.getHeight() * sy)),
        CGRectMake(0, 0, CGImageGetWidth(shot), CGImageGetHeight(shot)));
    CGImageRef editor = CGRectIsEmpty(crop) ? nullptr : CGImageCreateWithImageInRect(shot, crop);
    CGImageRelease(shot);
    if (editor == nullptr) return {};

    auto img = blankImage(targetSize(static_cast<int>(CGImageGetWidth(editor)), static_cast<int>(CGImageGetHeight(editor))));
    {
        juce::Image::BitmapData bd(img, juce::Image::BitmapData::writeOnly);
        CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
        // juce ARGB is premultiplied BGRA in memory on little-endian.
        CGContextRef ctx = CGBitmapContextCreate(bd.data, static_cast<size_t>(img.getWidth()),
            static_cast<size_t>(img.getHeight()), 8, static_cast<size_t>(bd.lineStride), cs,
            static_cast<uint32_t>(kCGImageAlphaPremultipliedFirst) | static_cast<uint32_t>(kCGBitmapByteOrder32Little));
        if (ctx != nullptr) {
            CGContextSetInterpolationQuality(ctx, kCGInterpolationHigh);
            CGContextDrawImage(ctx, CGRectMake(0, 0, img.getWidth(), img.getHeight()), editor);
            CGContextRelease(ctx);
        }
        CGColorSpaceRelease(cs);
    }
    CGImageRelease(editor);
    return img;
}

#elif JUCE_WINDOWS
// PrintWindow with PW_RENDERFULLCONTENT asks the window to render itself, DirectX/GL children
// included, whatever overlaps it on screen.
juce::Image capture(juce::DocumentWindow& w) {
    auto hwnd = static_cast<HWND>(w.getWindowHandle());
    auto* content = w.getContentComponent();
    if (hwnd == nullptr || content == nullptr || IsIconic(hwnd) || !IsWindowVisible(hwnd) || w.getWidth() <= 0) return {};
    RECT rc{};
    GetClientRect(hwnd, &rc);
    const int cw = rc.right - rc.left, ch = rc.bottom - rc.top;
    if (cw <= 0 || ch <= 0) return {};

    // Client pixels per component unit (per-monitor DPI), then the editor's part of the client.
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
    HDC screen = GetDC(nullptr);
    HDC full = CreateCompatibleDC(screen);
    HBITMAP fullBmp = CreateCompatibleBitmap(screen, cw, ch);
    HGDIOBJ oldFull = SelectObject(full, fullBmp);
    if (PrintWindow(hwnd, full, PW_CLIENTONLY | PW_RENDERFULLCONTENT)) {
        const auto t = targetSize(bw, bh);
        BITMAPINFO bi{};
        bi.bmiHeader.biSize = sizeof(BITMAPINFOHEADER);
        bi.bmiHeader.biWidth = t.x;
        bi.bmiHeader.biHeight = -t.y; // top-down rows
        bi.bmiHeader.biPlanes = 1;
        bi.bmiHeader.biBitCount = 32;
        bi.bmiHeader.biCompression = BI_RGB;
        void* bits = nullptr;
        HDC scaledDc = CreateCompatibleDC(screen);
        HBITMAP scaledBmp = CreateDIBSection(scaledDc, &bi, DIB_RGB_COLORS, &bits, nullptr, 0);
        if (scaledBmp != nullptr && bits != nullptr) {
            HGDIOBJ oldScaled = SelectObject(scaledDc, scaledBmp);
            SetStretchBltMode(scaledDc, HALFTONE);
            SetBrushOrgEx(scaledDc, 0, 0, nullptr);
            StretchBlt(scaledDc, 0, 0, t.x, t.y, full, bx, by, bw, bh, SRCCOPY);
            GdiFlush();
            img = blankImage(t);
            juce::Image::BitmapData bd(img, juce::Image::BitmapData::writeOnly);
            for (int y = 0; y < t.y; ++y) {
                const auto* src = static_cast<const uint8_t*>(bits) + static_cast<size_t>(y) * t.x * 4;
                auto* dst = bd.getLinePointer(y);
                for (int x = 0; x < t.x; ++x) { // GDI leaves alpha undefined: opaque BGRA
                    dst[x * 4 + 0] = src[x * 4 + 0];
                    dst[x * 4 + 1] = src[x * 4 + 1];
                    dst[x * 4 + 2] = src[x * 4 + 2];
                    dst[x * 4 + 3] = 255;
                }
            }
            SelectObject(scaledDc, oldScaled);
        }
        if (scaledBmp != nullptr) DeleteObject(scaledBmp);
        DeleteDC(scaledDc);
    }
    SelectObject(full, oldFull);
    DeleteObject(fullBmp);
    DeleteDC(full);
    ReleaseDC(nullptr, screen);
    return img;
}

#else
// X11: XGetImage only sees unobscured pixels without a compositor; no capture yet, the card
// stays plain.
juce::Image capture(juce::DocumentWindow&) {
    return {};
}
#endif

} // namespace

std::vector<uint8_t> snapshotEditor(juce::DocumentWindow& window) {
    const auto img = capture(window);
    if (!img.isValid() || looksBlank(img)) return {};
    return encode(img);
}

} // namespace hosting
} // namespace nota
