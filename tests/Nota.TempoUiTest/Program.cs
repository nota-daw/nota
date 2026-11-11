// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Real pointer/key events against the production lane, without an audio device.

using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Infrastructure;

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

string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("out/tempo-ui.png");
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (var image = window.CaptureRenderedFrame()) image?.Save(output);
Console.WriteLine("Screenshot: " + output);
window.Close();
return failures == 0 ? 0 : 1;

static void Pump() { for (int i = 0; i < 3; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); } }
