// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Real pointer/key events against the production lane, without an audio device.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Application.Editing;
using Nota.Application.Input;
using Nota.Infrastructure;
using SkiaSharp;

string scratch = Path.Combine(Path.GetTempPath(), "nota-tempo-ui-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("NOTA_DATA_DIR", scratch);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false }).WithNotaFonts().SetupWithoutStarting();
using var engine = new NotaEngine();
var original = new[] { new TempoPoint(0, 120), new TempoPoint(4, 80), new TempoPoint(8, 160), new TempoPoint(16, 120) };
engine.SetTempoPoints(original);
engine.ClearUndoHistory();
var timeline = new ArrangementView { Engine = engine };
var window = new Window { Width = 1100, Height = 600, Content = timeline };
window.Show(); Pump();
var lane = timeline.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "TempoLane");
int failures = 0;
void Check(bool value, string label) { Console.WriteLine($"[{(value ? "PASS" : "FAIL")}] {label}"); if (!value) ++failures; }
T Get<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(obj)!;
object? Invoke(object obj, string name, params object[] values) => obj.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)!.Invoke(obj, values);
Point At(double x, double y) => lane.TranslatePoint(new Point(x, y), window)!.Value;
void Move(double x, double y, RawInputModifiers mods = RawInputModifiers.None) { window.MouseMove(At(x, y), mods); Pump(); }
void Down(double x, double y, MouseButton button, RawInputModifiers mods = RawInputModifiers.None) { window.MouseDown(At(x, y), button, mods); Pump(); }
void Up(double x, double y, MouseButton button, RawInputModifiers mods = RawInputModifiers.None) { window.MouseUp(At(x, y), button, mods); Pump(); }
void SendKey(Key key, RawInputModifiers mods = RawInputModifiers.None) { window.KeyPress(key, mods, PhysicalKey.None, null); window.KeyRelease(key, mods, PhysicalKey.None, null); Pump(); }
double X(double beat) => (double)Invoke(timeline, "BeatToX", beat)!;
double Y(double bpm) => (double)Invoke(lane, "Y", bpm)!;
void Reset() { engine.SetTempoPoints(original); engine.ClearUndoHistory(); timeline.Refresh(); Pump(); }

engine.SetBpm(135); timeline.Refresh(rebuildHeaders: false); Pump();
Check(Math.Abs(Get<List<TempoPoint>>(lane, "_points")[0].Bpm - 135) < 1e-9,
    "BPM-only refresh reloads tempo-lane points");
Down(X(4), Y(80), MouseButton.Left); Move(X(4), Y(100), RawInputModifiers.LeftMouseButton); Up(X(4), Y(100), MouseButton.Left);
Check(Math.Abs(engine.TempoPoints[0].Bpm - 135) < 1e-9,
    "a subsequent lane gesture preserves a transport BPM edit");
Reset();
var savedGrid = EditorGrids.Shared.Get(EditorKind.Arrangement);
EditorGrids.Shared.Set(EditorKind.Arrangement, new GridSetting { Adaptive = false, FixedIndex = 5 });
foreach (var (num, den, beats) in new[] { (6, 8, 3.0), (3, 8, 1.5) })
{
    timeline.BeatsPerBar = num; timeline.TimeSignatureDenominator = den;
    Check(Math.Abs((double)Invoke(timeline, "Snap", beats - .1)! - beats) < 1e-9,
        $"{num}/{den} one-bar snapping matches the mapped ruler");
}
timeline.BeatsPerBar = 4; timeline.TimeSignatureDenominator = 4;
EditorGrids.Shared.Set(EditorKind.Arrangement, savedGrid); Reset();

Check(lane.Bounds.Height >= 64 && lane.Bounds.Width > 400, "tempo lane lays out at a usable height");
var stepEdge = ((Point HoldEnd, Point JumpEnd))Invoke(lane, "StepEdge", 1)!;
Check(stepEdge.HoldEnd.X == X(4) && stepEdge.JumpEnd.X == X(4), "stepped transition uses the exact event X for both endpoints");
Check(stepEdge.HoldEnd.Y == Y(120) && stepEdge.JumpEnd.Y == Y(80), "stepped transition holds the old BPM then jumps to the new BPM");
Move(160, 50, RawInputModifiers.Control);
Check(Get<bool>(lane, "_ctrl") && lane.Cursor?.ToString()?.Contains("SizeWestEast") == true, "Ctrl hover shows time-scale cursor");
window.KeyRelease(Key.LeftCtrl, RawInputModifiers.None, PhysicalKey.ControlLeft, null); Pump();
Check(!Get<bool>(lane, "_ctrl"), "Ctrl release clears indicator without moving pointer");

engine.SetTempoPoints(Array.Empty<TempoPoint>()); engine.SetBpm(60);
engine.RenderOffline(new float[2], 1, 48000); timeline.Refresh(); Pump();
double legacyBarX = X(4);
double legacyExpectedSeconds = engine.BeatToSeconds(4) + 20 * 60 / (60 * Get<double>(timeline, "_pixelsPerBeat"));
Down(legacyBarX, 35, MouseButton.Left, RawInputModifiers.Control);
Move(legacyBarX + 20, 35, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
Check(Math.Abs(Get<double>(lane, "_scaleTargetSeconds") - legacyExpectedSeconds) < .02,
    "first time-scale drag uses the legacy 60 BPM seconds axis");
SendKey(Key.Escape, RawInputModifiers.Control); Up(legacyBarX + 20, 35, MouseButton.Left);
Reset();

Down(20, 20, MouseButton.Right); Move(420, lane.Bounds.Height - 8, RawInputModifiers.RightMouseButton); Up(420, lane.Bounds.Height - 8, MouseButton.Right);
Check(Get<HashSet<int>>(lane, "_selection").SetEquals(new[] { 1, 2 }), "right drag selects every enclosed point");
Check(lane.IsFocused, "marquee drag leaves no context menu");
SendKey(Key.Delete);
Check(engine.TempoPoints.Count == 2 && engine.TempoPoints[0].Beat == 0, "Delete removes selected tempo points, retaining initial point");
Check(engine.Undo() && engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo, "multi-point deletion is one undo step");
Check(engine.Redo() && engine.TempoPoints.Count == 2, "multi-point deletion redoes");
Reset();

// The merge with editable shortcuts must preserve the shared Delete/Cancel bindings.
var previousBindings = KeyBindings.Instance;
KeyBindings.Instance = new KeyBindings(KeyActionCatalog.Create(), new Settings());
try
{
    KeyBindings.Instance.Set("edit.delete", KeySlot.Primary, Shortcut.None("F8"));
    KeyBindings.Instance.Set("edit.delete", KeySlot.Alternative, null);
    KeyBindings.Instance.Set("edit.cancel", KeySlot.Primary, Shortcut.None("F9"));
    Down(20, 20, MouseButton.Right); Move(420, lane.Bounds.Height - 8, RawInputModifiers.RightMouseButton);
    Up(420, lane.Bounds.Height - 8, MouseButton.Right);
    SendKey(Key.Delete);
    Check(engine.TempoPoints.SequenceEqual(original), "tempo deletion ignores an unbound default key");
    Nota.App.Input.Keys.Capturing = true;
    SendKey(Key.F8);
    Check(engine.TempoPoints.SequenceEqual(original), "shortcut capture cannot delete tempo points");
    Nota.App.Input.Keys.Capturing = false;
    SendKey(Key.F8);
    Check(engine.TempoPoints.Count == 2 && engine.TempoPoints[0].Beat == 0,
        "tempo selection uses the rebound shared Delete action");
    Reset();
    Down(X(12), 35, MouseButton.Left, RawInputModifiers.Control);
    Move(X(12) + 50, 35, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
    SendKey(Key.F9); Up(X(12) + 50, 35, MouseButton.Left);
    Check(engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo,
        "tempo mapping uses the rebound shared Cancel action");
    KeyBindings.Instance.Set("edit.cancel", KeySlot.Primary, Shortcut.Alt("F9"));
    Down(X(12), 35, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Alt);
    Move(X(12) + 50, 35, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
    SendKey(Key.F9, RawInputModifiers.Control | RawInputModifiers.Alt); Up(X(12) + 50, 35, MouseButton.Left);
    Check(engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo,
        "rebound Cancel keeps its own modifiers while the mapping modifiers are held");
    var tempoGroup = ShortcutLayout.Build(KeyBindings.Instance.Actions, false).Single(g => g.Title == "Tempo track");
    Check(tempoGroup.Rows.Count == 5 && tempoGroup.Rows.All(r => r.IsFixed),
        "Shortcuts retains all five tempo mouse gestures after the merge");
}
finally
{
    Nota.App.Input.Keys.Capturing = false;
    KeyBindings.Instance = previousBindings;
    Reset();
}

Down(X(4), Y(80), MouseButton.Left); Move(X(5), Y(100), RawInputModifiers.LeftMouseButton); Up(X(5), Y(100), MouseButton.Left);
Check(Math.Abs(engine.TempoPoints[1].Bpm - 100) < 1 && engine.CanUndo, "point drag edits BPM and commits once");
engine.Undo(); timeline.Refresh(); Pump();
Down(X(4), Y(80), MouseButton.Left); Move(X(5), Y(140), RawInputModifiers.LeftMouseButton); SendKey(Key.Escape); Up(X(5), Y(140), MouseButton.Left);
Check(engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo, "Escape cancels point drag without history");

Reset();
double startX = X(12), targetX = startX + 50;
Down(startX, 35, MouseButton.Left, RawInputModifiers.Control);
Move(targetX, 35, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
Check(Get<ITempoPreview?>(timeline, "_tempoPreview") is not null && engine.TempoPoints.SequenceEqual(original), "bar drag previews grid without committing project data");
Up(targetX, 35, MouseButton.Left, RawInputModifiers.Control);
Check(engine.TempoPoints.Any(p => p.Beat == 12) && engine.TempoPoints.Count == original.Length + 1, "Ctrl drag inserts ending tempo marker");
Check(engine.Undo() && engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo, "mapping drag undoes in one step");
timeline.Refresh(); Pump();
Down(X(12), 35, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Alt);
Move(X(12) + 50, 35, RawInputModifiers.Control | RawInputModifiers.Alt | RawInputModifiers.LeftMouseButton);
Up(X(12) + 50, 35, MouseButton.Left, RawInputModifiers.Control | RawInputModifiers.Alt);
Check(engine.TempoPoints.Count == original.Length && !engine.TempoPoints.Any(p => p.Beat == 12), "Ctrl+Alt drag omits marker insertion");
Reset();
Down(X(12), 35, MouseButton.Left, RawInputModifiers.Control);
Move(X(12) + 50, 35, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
SendKey(Key.Escape, RawInputModifiers.Control); Up(X(12) + 50, 35, MouseButton.Left);
Check(engine.TempoPoints.SequenceEqual(original) && !engine.CanUndo, "Escape cancels time-scale mapping");

engine.SetTempoPoints(new[] { original[0] with { Shape = TempoShape.Smooth }, original[1], original[2], original[3] });
engine.ClearUndoHistory(); timeline.Refresh(); Pump();
double oldTempo = engine.TempoAt(2);
Down(6, Y(120), MouseButton.Left); Up(6, Y(120), MouseButton.Left);
var handle = (Point)Invoke(lane, "Handle", 0, false)!;
Down(handle.X, handle.Y, MouseButton.Left); Move(handle.X + 14, handle.Y + 12, RawInputModifiers.LeftMouseButton); Up(handle.X + 14, handle.Y + 12, MouseButton.Left);
Check(engine.TempoPoints[0].Shape == TempoShape.Smooth && Math.Abs(engine.TempoAt(2) - oldTempo) > .01 && engine.CanUndo,
    "Bezier handle drag changes the audible ramp");
Check(engine.Undo() && !engine.CanUndo, "curve-handle gesture is one undo step");
Reset();
double snappedTime = engine.BeatToSeconds(16);
Down(X(12), 35, MouseButton.Left, RawInputModifiers.Control);
Move(X(16) - 3, 35, RawInputModifiers.Control | RawInputModifiers.LeftMouseButton);
Up(X(16) - 3, 35, MouseButton.Left, RawInputModifiers.Control);
Check(Math.Abs(engine.BeatToSeconds(12) - snappedTime) < 1e-8, "bar mapping snaps to an existing marker");
Reset();

Invoke(timeline, "ZoomAtBeat", 2.0, 8.0); Invoke(timeline, "ScrollByBeats", 3.0); Pump();
bool inverse = new[] { 4.0, 8, 12, 18 }.All(b => Math.Abs((double)Invoke(timeline, "XToBeat", X(b))! - b) < 1e-8);
Check(inverse, "forward/inverse coordinates agree after zoom and scroll");
stepEdge = ((Point HoldEnd, Point JumpEnd))Invoke(lane, "StepEdge", 2)!;
Check(stepEdge.HoldEnd.X == X(8) && stepEdge.JumpEnd.X == X(8), "stepped transition remains vertical after zoom and scroll");
Invoke(timeline, "ScrollByBeats", -10000.0); Invoke(timeline, "ScrollByBeats", 2.0); Pump();
Down(Math.Max(2, X(4) - 8), 20, MouseButton.Right);
Move(X(8) + 8, lane.Bounds.Height - 8, RawInputModifiers.RightMouseButton);
Up(X(8) + 8, lane.Bounds.Height - 8, MouseButton.Right);
Check(Get<HashSet<int>>(lane, "_selection").SetEquals(new[] { 1, 2 }), "marquee stays accurate after zoom and scroll");
SendKey(Key.Escape);
Check(Get<HashSet<int>>(lane, "_selection").Count == 0, "Escape clears tempo selection");
double beforeHeight = lane.Bounds.Height;
Down(100, beforeHeight - 2, MouseButton.Left); Move(100, beforeHeight + 35, RawInputModifiers.LeftMouseButton); Up(100, beforeHeight + 35, MouseButton.Left);
Check(lane.Bounds.Height > beforeHeight + 20, "tempo track resizes vertically");
var toggle = timeline.GetVisualDescendants().OfType<Button>().Single(b => b.Content as string == "Tempo track");
toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(!lane.IsVisible, "tempo track collapses");
toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Pump();
Check(lane.IsVisible && lane.Bounds.Height > beforeHeight, "tempo track restores expanded height");

string pulsePath = Path.Combine(scratch, "waveform-pulse.wav");
using (var writer = new BinaryWriter(File.Create(pulsePath)))
{
    const int frames = 4 * 48000, bytes = frames * 2;
    writer.Write("RIFF"u8.ToArray()); writer.Write(36 + bytes); writer.Write("WAVEfmt "u8.ToArray());
    writer.Write(16); writer.Write((short)1); writer.Write((short)1);
    writer.Write(48000); writer.Write(96000); writer.Write((short)2); writer.Write((short)16);
    writer.Write("data"u8.ToArray()); writer.Write(bytes);
    for (int i = 0; i < frames; ++i)
        writer.Write((short)(Math.Abs(i - 48000) < 2800 ? 28000 * Math.Sin(2 * Math.PI * 440 * i / 48000) : 0));
}
using (var waveEngine = new NotaEngine())
{
    waveEngine.RenderOffline(new float[2], 1, 48000);
    int track = waveEngine.AddAudioTrack(); waveEngine.AddAudioClip(track, pulsePath, 0);
    waveEngine.SetClipWarp(track, 0, true, 0); waveEngine.SetClipWarpLength(track, 0, 8);
    waveEngine.SetTempoPoints(new[] { new TempoPoint(0, 120), new TempoPoint(4, 60) });
    foreach (bool reverse in new[] { false, true })
    foreach (bool detail in new[] { false, true })
    foreach (var style in new[] { WaveformStyle.Smooth, WaveformStyle.Solid })
    {
        waveEngine.SetClipReverse(track, 0, reverse);
        var view = new ArrangementView { Engine = waveEngine, Waveform = style };
        var waveWindow = new Window { Width = 1100, Height = 600, Content = view };
        waveWindow.Show(); view.Refresh(); Pump();
        double peakBeat = reverse ? 6 : 2;
        if (detail) { Invoke(view, "ZoomAtBeat", 20.0, 0.0); Invoke(view, "ScrollByBeats", peakBeat - .2); Pump(); }
        var canvas = view.GetVisualDescendants().OfType<Control>().Single(c => c.GetType().Name == "LaneControl");
        double px = (double)Invoke(view, "BeatToX", peakBeat)!;
        var at = canvas.TranslatePoint(new Point(px, 0), waveWindow)!.Value;
        using var frame = waveWindow.CaptureRenderedFrame();
        using var encoded = new MemoryStream(); frame!.Save(encoded, PngBitmapEncoderOptions.Default); encoded.Position = 0;
        using var bitmap = SKBitmap.Decode(encoded);
        int ColorCount(int cx)
        {
            int count = 0;
            for (int x = Math.Max(0, cx - 5); x < Math.Min(bitmap.Width, cx + 6); ++x)
            for (int y = (int)at.Y; y < Math.Min(bitmap.Height, (int)at.Y + 90); ++y)
            {
                var color = bitmap.GetPixel(x, y);
                if (Math.Max(color.Red, Math.Max(color.Green, color.Blue)) - Math.Min(color.Red, Math.Min(color.Green, color.Blue)) > 45)
                    ++count;
            }
            return count;
        }
        double x0 = (double)Invoke(view, "BeatToX", 0.0)!, x8 = (double)Invoke(view, "BeatToX", 8.0)!;
        int wrong = (int)(at.X - px + x0 + (x8 - x0) * (reverse ? .75 : .25));
        int correctPixels = ColorCount((int)at.X), linearPixels = wrong >= 0 && wrong < bitmap.Width ? ColorCount(wrong) : 0;
        Check(correctPixels > linearPixels + 12,
            $"mapped {style} waveform {(detail ? "detail" : "overview")} {(reverse ? "reversed" : "forward")} peak follows its audible beat ({correctPixels}/{linearPixels})");
        if (!detail && !reverse && style == WaveformStyle.Smooth)
        {
            Directory.CreateDirectory("out"); frame.Save("out/tempo-waveform-mapped.png", PngBitmapEncoderOptions.Default);
        }
        waveWindow.Close();
    }
}

// The viewport has a fixed duration, while its beat span changes as it crosses the map.
foreach (var (first, last, mapped) in new[] { (240.0, 20.0, true), (20.0, 240.0, true),
    (120.0, 120.0, true), (60.0, 60.0, false) })
{
    using var scrollEngine = new NotaEngine();
    scrollEngine.SetBpm(first); scrollEngine.RenderOffline(new float[2], 1, 48000);
    if (mapped) scrollEngine.SetTempoPoints(new[] { new TempoPoint(0, first), new TempoPoint(100, last) });
    int track = scrollEngine.AddInstrumentTrack();
    scrollEngine.AddMidiClip(track, 136, 4);
    var view = new ArrangementView { Engine = scrollEngine };
    var scrollWindow = new Window { Width = 1100, Height = 600, Content = view };
    scrollWindow.Show(); view.Refresh(); Pump();
    void SetField(string name, object value) => typeof(ArrangementView)
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(view, value);
    SetField("_pixelsPerBeat", 20.0); SetField("_scrollBeats", 0.0);
    var scroll = Get<object>(view, "_hScroll");
    double Maximum() => (double)scroll.GetType().GetProperty("Maximum")!.GetValue(scroll)!;
    double end = Get<double>(view, "_totalBeats");
    double expected = mapped ? Math.Max(0, scrollEngine.SecondsToBeat(Math.Max(0,
        scrollEngine.BeatToSeconds(end) - 800.0 / 40))) : Math.Max(0, end - 800.0 / 20);
    Invoke(view, "SyncScroll", 800.0);
    Check(Math.Abs(Maximum() - expected) < 1e-7,
        $"scroll limit uses mapped project-end time ({first} → {last})");
    Invoke(view, "ScrollByBeats", 10000.0);
    double maximum = Maximum();
    Invoke(view, "SyncScroll", 800.0);
    Check(Math.Abs(Maximum() - maximum) < 1e-7,
        $"scroll limit stays stable after scrolling ({first} → {last})");
    // The padded ending can itself be wider than the viewport. Navigate to the
    // final clip's end and verify it is on screen, rather than accepting negative X.
    double revealStart = mapped ? scrollEngine.SecondsToBeat(Math.Max(0,
        scrollEngine.BeatToSeconds(140) - 800.0 / 40)) : Math.Max(0, 140 - 800.0 / 20);
    Invoke(view, "ScrollByBeats", revealStart - Get<double>(view, "_scrollBeats"));
    double clipLeft = (double)Invoke(view, "BeatToX", 136.0)!, clipRight = (double)Invoke(view, "BeatToX", 140.0)!;
    Check(clipLeft >= -1e-6 && clipRight <= 800 + 1e-6 && clipRight > clipLeft,
        $"last clip is reachable through the scrollbar ({first} → {last})");
    if (first == 240 && last == 20)
    {
        Pump();
        string shot = Path.ChangeExtension(args.Length > 0 ? args[0] : "out/tempo-ui.png", null) + "-scroll.png";
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(shot))!);
        using var image = scrollWindow.CaptureRenderedFrame(); image?.Save(shot, PngBitmapEncoderOptions.Default);
    }
    SetField("_pixelsPerBeat", 40.0);
    Invoke(view, "SyncScroll", 800.0); Invoke(view, "ScrollByBeats", 10000.0);
    Invoke(view, "SyncScroll", 800.0);
    Check(Math.Abs((double)Invoke(view, "BeatToX", end)! - 800) < 1e-6,
        $"scroll reaches the padded ending after zoom ({first} → {last})");
    Invoke(view, "SyncScroll", 100000.0);
    Check(Maximum() == 0 && Get<double>(view, "_scrollBeats") == 0,
        $"content shorter than the viewport needs no scrolling ({first} → {last})");
    Invoke(view, "SyncScroll", 0.0);
    Check(Maximum() == 0 && Get<double>(view, "_scrollBeats") == 0,
        $"zero-width viewport safely resets scrolling ({first} → {last})");
    if (first == 240 && last == 20)
    {
        var previewPoints = new[] { new TempoPoint(0, 20), new TempoPoint(100, 240) };
        using var preview = scrollEngine.CreateTempoPreview(previewPoints);
        Invoke(view, "PreviewTempo", previewPoints);
        Invoke(view, "SyncScroll", 800.0);
        double previewMaximum = preview.SecondsToBeat(Math.Max(0, preview.BeatToSeconds(end) - 800.0 / 80));
        Check(Math.Abs(Maximum() - previewMaximum) < 1e-7,
            "scroll extent uses the preview map during tempo editing");
        Invoke(view, "EndTempoPreview", false, previewPoints);
        Check(scrollEngine.TempoPoints[0].Bpm == first,
            "scrolling with a preview leaves the committed tempo map unchanged");
    }
    scrollWindow.Close();
}

string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("out/tempo-ui.png");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var image = window.CaptureRenderedFrame()) image?.Save(output, PngBitmapEncoderOptions.Default);
Console.WriteLine("Screenshot: " + output);
var preferences = new PreferencesWindow(null!);
typeof(PreferencesWindow).GetField("_shortcutFilter", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(preferences, "tempo");
preferences.ShowPage("Shortcuts"); preferences.Show(); Pump();
Check(preferences.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == "Tempo track" && t.IsEffectivelyVisible),
    "Settings → Shortcuts renders the tempo gesture group in its editable pane");
using (var image = preferences.CaptureRenderedFrame())
    image?.Save(Path.ChangeExtension(output, null) + "-shortcuts.png", PngBitmapEncoderOptions.Default);
preferences.Close();
window.Close();
return failures == 0 ? 0 : 1;

static void Pump() { for (int i = 0; i < 3; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); } }
