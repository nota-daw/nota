# Research: Plug-in card preview

## R1 — How to capture a plug-in editor window on macOS

**Decision**: `CGWindowListCreateImage(CGRectNull, kCGWindowListOptionIncludingWindow,
windowNumber, kCGWindowImageBoundsIgnoreFraming | kCGWindowImageBestResolution)` on the
editor's own `NSWindow`, resolved at runtime with `dlsym` (the macOS 15+ SDK marks it
unavailable at compile time), then crop off the native title bar.

**Rationale**: Measured on macOS 26.6 / SDK 26.5 with a throwaway test program (window
holding a `drawRect` view and a `CAMetalLayer` view):

| Method | `drawRect` content | Metal content | Permission prompt |
|---|---|---|---|
| `NSView cacheDisplayInRect:` | yes | **blank** | no |
| `CALayer renderInContext:` | yes | **blank** | no |
| `CGWindowListCreateImage` (own window, via `dlsym`) | yes | **yes** | no (`CGPreflightScreenCaptureAccess` = 0 and still captured) |

Modern plug-in UIs, JUCE 8 ones included, often draw through Metal/OpenGL layers, so the
view-drawing APIs would come back blank for exactly the plug-ins people use most. A
process may capture its own windows without the Screen Recording permission. The call
reads the window's backing store, so windows overlapping it don't matter. It must run
while the window is still ordered in, i.e. **before** it is hidden.

**Alternatives considered**: ScreenCaptureKit (`SCScreenshotManager`) asks for the Screen
Recording permission (rejected: a prompt for a cosmetic feature). JUCE
`Component::createComponentSnapshot` paints JUCE components only, so a hosted
VST3/AU view (a native child view) comes out blank.

**Risk**: Apple may remove the symbol in a later macOS. `dlsym` returning null degrades
to "no preview" (FR-014), so it never fails the build or crashes.

## R2 — Windows and Linux

**Decision**: On Windows, `PrintWindow(hwnd, hdc, PW_RENDERFULLCONTENT)` into a DIB of
the client area (`PW_CLIENTONLY`), which includes DirectX/GL child content on Windows 8.1
and later. On Linux (X11), no capture in this change: the function returns no image and
the card stays plain.

**Rationale**: `PrintWindow` is the documented way to capture a window's own content
regardless of overlap. On X11, `XGetImage` only returns correct pixels for unobscured
windows without a compositor, and a separate offscreen path would need per-plug-in
testing we can't do here. FR-014 allows the plain card.

## R3 — Where the preview lives, and how it follows copies, moves and undo

**Decision**: On the device object in the engine, next to the fold flag (specs/022): an
`EditorPreview { shared_ptr<const vector<uint8_t>> jpeg; uint32_t serial; }` on `Device`
(effects) and `Instrument`. `cloneDevice` and the hosted-plug-in branch of
`cloneInstrumentFrom` copy it (shared, not deep-copied). The managed layer reads and
writes it through the C ABI and saves it as a project blob.

**Rationale**: The fold flag already proves the pattern: moves reorder the same
`shared_ptr`, undo snapshots keep the old device objects, copy/paste/duplicate go
through `cloneDevice`, grouping into a rack moves the device object
(`Engine_Devices.cpp` `keepSource ? clone : sd`). The capture itself happens in
`pluginhost/` where the adapter *is* the device, so it can store the result without any
engine round trip. Replacing the plug-in creates a new device object, so the preview is
dropped as FR-008 asks.

**Alternatives considered**: A managed-side cache keyed by device (by track/index) would
have to be remapped on every move, copy, paste, undo and rack grouping. Rejected.

**Threading**: Written by the capture (message thread), read and written through the
ABI (UI thread = message thread on macOS/Windows; Linux pumps JUCE from the UI thread),
and saved on the UI thread. The audio thread never reads it (AR-6 unaffected).

## R4 — Image size and format

**Decision**: Scale so the **shorter** side is at most 520 px (the 260 pt card at 2×),
capping the longer side at 1040 px. Never upscale at capture time. Encode JPEG,
quality 0.8, through JUCE's `JPEGImageFormat` (already part of `juce_graphics`, which
`pluginhost` links; no new dependency).

**Rationale**: The card shows it under a darkening gradient, so JPEG artefacts are
invisible. A typical 867 × 520 capture is about 40–90 KB (SC-003 ≤ 100 KB). Scaling
happens in the platform layer (CoreGraphics `kCGInterpolationHigh` / GDI `HALFTONE`
`StretchBlt`) while drawing the captured pixels into the target bitmap, so there is one
pass and no large intermediate image.

**Timing**: capture + scale + encode is one synchronous step on the message thread at
window close (measured order: ~10–30 ms). That is a one-off at a user gesture, not
in a render loop, so a background thread isn't worth the lifetime complexity (the
device could be deleted mid-encode). Audio is unaffected.

## R5 — Blank-capture detection

**Decision**: After scaling, sample a 16 × 16 grid. If the luma range of the samples is
under 6/255 (a uniform image: all black, all clear, all one colour), reject the capture
and keep the previous preview (FR-010).

## R6 — Drawing on the card

**Decision**: A small custom control `PluginPreviewBackdrop` draws the bitmap with
"cover" fitting (uniform scale = max(w/iw, h/ih), anchored top, centred horizontally,
clipped), then a vertical `LinearGradient` in `NotaPalette.SurfaceCard`:
alpha 0.80 at the top (name, vendor, facts), 0.62 in the middle, 0.90 at the bottom
(buttons). It redraws on `NotaPalette.Changed`, so the gradient follows theme switches
in both dark and light themes. On S it fills the card body (behind the padding); on L
it sits behind the info column only.

**Rationale**: "Cover" never distorts (SC-007). Top anchoring keeps the plug-in's logo
and header, which is what makes it recognisable. Using the card's own surface colour
for the wash keeps text contrast close to the plain card in every theme (SC-006),
whereas a black scrim would break Studio Light / Ember Paper.

**Decoding**: Avalonia `Bitmap` from the JPEG bytes on the UI thread (~1–2 ms for
80 KB). A small static cache keyed by the preview serial (last 32) avoids decoding again
on each card rebuild.

**Picking up a new capture**: the card's device refresher polls
`nota_plugin_preview_serial` (an int compare) and swaps the bitmap when it changes. That
covers the window's own close button, which hides the window without telling the app.

## R7 — Offscreen capture on add (Story 4, P3)

**Decision**: Deferred, not in this change.

**Rationale**: Every option means creating the plug-in's editor without the user asking
for it: an off-screen window, or an editor never attached to a window, which most
VST3/AU views can't render. Plug-ins also show licence or login dialogs when their
editor opens. A window flashing, or a dialog appearing on add, is worse than a plain
card until the first open. Spec FR-012 is a SHOULD and the Assumptions allow deferring.
Recorded as a follow-up.
