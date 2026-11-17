// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
// Headless checks for specs/101 (#656): the cable-drop parameter picker, Modular device cards,
// node sizes and their persistence — the real ModularView against a real engine, no audio device.

using System.Collections;
using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Infrastructure;
using Nota.Presentation;
using static Nota.App.ModularView;

string scratch = Path.Combine(Path.GetTempPath(), "nota-modular-ui-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("NOTA_DATA_DIR", scratch);
AppBuilder.Configure<App>().UseSkia().UseHeadless(new() { UseHeadlessDrawing = false }).WithNotaFonts().SetupWithoutStarting();

int failures = 0;
void Check(bool value, string label) { Console.WriteLine($"[{(value ? "PASS" : "FAIL")}] {label}"); if (!value) ++failures; }
const BindingFlags Any = BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance;
object? Invoke(object obj, string name, params object?[] values) => obj.GetType().GetMethod(name, Any)!.Invoke(obj, values);
T Prop<T>(object obj, string name) => (T)(obj.GetType().GetProperty(name, Any)?.GetValue(obj) ?? obj.GetType().GetField(name, Any)!.GetValue(obj))!;

// ---- picker model (T006) -----------------------------------------------------------------
{
    var seen = ParamPickerModel.NewSeen();
    string Fb(int n) => $"Parameter {n}";
    var entries = new List<ParamPickerEntry>
    {
        ParamPickerEntry.Heading("Delay"),
        ParamPickerEntry.Item(ParamPickerModel.Label("Time", 0, seen, Fb), 0, "Delay"),
        ParamPickerEntry.Item(ParamPickerModel.Label("Feedback", 1, seen, Fb), 1, "Delay"),
        ParamPickerEntry.Item(ParamPickerModel.Label("", 2, seen, Fb), 2, "Delay"),
        ParamPickerEntry.Item(ParamPickerModel.Label("time", 3, seen, Fb), 3, "Delay"),
        ParamPickerEntry.Heading("Auto Filter"),
        ParamPickerEntry.Item("Frequency", 10, "Auto Filter"),
        ParamPickerEntry.Item("Resonance", 11, "Auto Filter"),
    };
    Check(entries[3].Label == "Parameter 3" && entries[4].Label == "time (2)", "fallback label for an empty name, suffix for a duplicate");
    var seen2 = ParamPickerModel.NewSeen();
    var gains = new[] { "Gain", "Gain", "Gain (2)", "Gain (2)" }.Select((g, i) => ParamPickerModel.Label(g, i, seen2, Fb)).ToList();
    Check(gains.Distinct().Count() == 4, $"generated labels never collide with raw names ({string.Join(" | ", gains)})");
    var m = new ParamPickerModel(entries, grouped: true);
    Check(m.Visible.Count == 8 && m.Highlight == 1, "unfiltered list shows headings; highlight starts on the first entry, not a heading");
    m.Query = "FREQ";
    Check(m.Visible.Count == 2 && m.Visible[0].IsHeading && m.Visible[0].Label == "Auto Filter" && m.Highlight == 1, "case-insensitive filter keeps the match under its heading, empty headings hidden");
    m.Query = "delay";
    Check(m.Visible.Count(v => !v.IsHeading) == 4, "grouped search also matches the group name");
    m.Query = "";
    m.MoveHighlight(4);
    Check(m.Highlight == 6 && !m.Visible[m.Highlight].IsHeading, "moving the highlight skips the heading between groups");
    m.MoveHighlight(100);
    Check(m.Highlight == 7, "highlight clamps at the last entry");
    m.MoveHighlight(-100);
    Check(m.Highlight == 1, "highlight clamps at the first entry");
    m.Query = "zzz";
    Check(m.Highlight == -1 && m.Pick() is null && m.EmptyKey == ParamPickerModel.NoMatchesKey, "no matches: nothing pickable, 'no matching parameters'");
    Check(new ParamPickerModel(Array.Empty<ParamPickerEntry>()).EmptyKey == ParamPickerModel.NoParamsKey, "no entries: 'no parameters'");

    var big = new List<ParamPickerEntry>(5000);
    for (int i = 0; i < 5000; i++) big.Add(ParamPickerEntry.Item($"Osc {i % 7} Param {i}", i));
    var bm = new ParamPickerModel(big);
    var sw = Stopwatch.StartNew(); double worst = 0;
    foreach (var q in new[] { "o", "os", "osc 3", "param 4", "param 49", "x", "", "Osc", "99", "param 4999" })
    { sw.Restart(); bm.Query = q; worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds); }
    Check(worst < 50, $"5,000 entries filter in < 50 ms per keystroke (worst {worst:0.0} ms)");
}

// ---- Modular against a real engine -----------------------------------------------------------
using var engine = new NotaEngine();
var view = new ModularView(engine);
var window = new Window { Width = 1400, Height = 900, Content = view };
window.Show(); Pump();

IList Nodes() => Prop<IList>(view, "Nodes");
object NodeById(string id) => Nodes().Cast<object>().First(n => Prop<string>(n, "Id") == id);
(ParamPickerModel Model, Action<object> Pick, Flyout Flyout) LastPicker() => ParamPickerPanel.Last!.Value;
bool HasLink(int owner, int targetTrack, int kind, int dev, int param)
{
    for (int i = 0; i < engine.CvLinkCount(owner); i++)
        if (engine.CvLinkTargetTrack(owner, i) == targetTrack && engine.CvLinkTargetKind(owner, i) == kind
            && engine.CvLinkDevice(owner, i) == dev && engine.CvLinkParam(owner, i) == param) return true;
    return false;
}
int ParamIndex(int track, int dev, string name)
{ for (int p = 0; p < engine.DeviceParamCount(track, dev); p++) if (engine.DeviceParamName(track, dev, p) == name) return p; return -1; }

int ta = engine.AddAudioTrack();
int comp = engine.AddBuiltinDevice(ta, 1);   // Compressor
int lfo = engine.ModulatorAdd(ta, 0);
view.Show(ta); Pump();

// US1 (T010): a cable dropped on the compressor's node opens the picker; "thr" + Enter links Threshold.
Invoke(view, "ShowTargetPicker", NodeById($"dev{comp}"), lfo); Pump();
{
    var (model, pick, _) = LastPicker();
    Check(model.Visible.Count == engine.DeviceParamCount(ta, comp) - (ParamIndex(ta, comp, DeviceView.ParamName) >= 0 ? 1 : 0),
        "node picker lists the device's parameters, without the card-size param");
    model.Query = "thr";
    int thr = ParamIndex(ta, comp, "Thresh");
    Check(thr >= 0 && model.Pick() is not null, "typing 'thr' leaves Thresh highlighted");
    pick(model.Pick()!); Pump();
    Check(HasLink(ta, -1, 0, comp, thr), "picking links the LFO to Thresh");
    Invoke(view, "ShowTargetPicker", NodeById($"dev{comp}"), lfo); Pump();
    var (m2, p2, _) = LastPicker(); m2.Query = "thr"; int before = engine.CvLinkCount(ta); p2(m2.Pick()!); Pump();
    Check(engine.CvLinkCount(ta) == before, "picking an already-linked parameter adds no duplicate link");
}

// SC-001: the picker opens with 5,000 entries well under 150 ms (layout included).
{
    var many = new List<ParamPickerEntry>(5000);
    for (int i = 0; i < 5000; i++) many.Add(ParamPickerEntry.Item($"Param {i}", i));
    var osw = Stopwatch.StartNew();
    ParamPickerPanel.Show(view, "Big plug-in", new ParamPickerModel(many), _ => { });
    Dispatcher.UIThread.RunJobs();
    double openMs = osw.Elapsed.TotalMilliseconds;
    Check(openMs < 150, $"the picker opens with 5,000 entries in < 150 ms ({openMs:0} ms)");
    var fl = LastPicker().Flyout;
    var lb = (fl.Content as Control)!.GetVisualDescendants().OfType<ListBox>().First();
    int realized = lb.GetVisualDescendants().OfType<ListBoxItem>().Count();
    Check(realized < 60, $"only the visible rows are built ({realized} of 5,000)");
    Check((fl.Content as Control)!.Bounds.Height <= ParamPickerPanel.PanelMaxH, $"the panel stays within {ParamPickerPanel.PanelMaxH} px ({(fl.Content as Control)!.Bounds.Height:0})");
    // T038: hovering a row moves the highlight, so Enter picks the hovered row.
    var rowGrid = lb.GetVisualDescendants().OfType<ListBoxItem>().Skip(3).First().GetVisualDescendants().OfType<Border>().First(b => b.Child is Grid);
    rowGrid.RaiseEvent(new Avalonia.Input.PointerEventArgs(Avalonia.Input.InputElement.PointerEnteredEvent, rowGrid,
        new Avalonia.Input.Pointer(1, Avalonia.Input.PointerType.Mouse, true), rowGrid, new Point(2, 2), 0,
        new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None));
    Pump();
    Check(LastPicker().Model.Highlight == 3 && Equals(LastPicker().Model.Pick(), 3), "hovering a row moves the highlight to it (Enter picks it)");
    // A click on the row the pointer is on (already highlighted, so already selected) picks it.
    object? clicked = null;
    fl.Hide(); Pump();
    ParamPickerPanel.Show(view, "Big plug-in", new ParamPickerModel(many), p => clicked = p); Pump();
    var lb2 = (LastPicker().Flyout.Content as Control)!.GetVisualDescendants().OfType<ListBox>().First();
    var row0 = lb2.GetVisualDescendants().OfType<Border>().First(b => b.Child is Grid);
    var mouse = new Avalonia.Input.Pointer(2, Avalonia.Input.PointerType.Mouse, true);
    row0.RaiseEvent(new Avalonia.Input.PointerEventArgs(Avalonia.Input.InputElement.PointerEnteredEvent, row0, mouse, row0, new Point(2, 2), 0,
        new Avalonia.Input.PointerPointProperties(), Avalonia.Input.KeyModifiers.None));
    row0.RaiseEvent(new Avalonia.Input.PointerPressedEventArgs(row0, mouse, row0, new Point(2, 2), 0,
        new Avalonia.Input.PointerPointProperties(Avalonia.Input.RawInputModifiers.LeftMouseButton, Avalonia.Input.PointerUpdateKind.LeftButtonPressed),
        Avalonia.Input.KeyModifiers.None));
    Pump();
    Check(Equals(clicked, 0) && ParamPickerPanel.Last is null, "a click on the hovered, already-highlighted row picks it and closes the panel");
    // T036: a rebuild (the chain changed) closes an open picker without linking.
    int linksBefore = engine.CvLinkCount(ta);
    view.Show(ta); Pump();
    Check(ParamPickerPanel.Last is null && engine.CvLinkCount(ta) == linksBefore, "a rebuild closes the open picker without linking");
}

// US2 (T014): from track A's LFO, a drop on track B's header opens a grouped picker of B's devices.
int tb = engine.AddAudioTrack();
int delay = engine.AddBuiltinDevice(tb, 3);
int filt = engine.AddBuiltinDevice(tb, 7);
view.Show(ta); Pump();
Invoke(view, "ShowCrossTrackPicker", view, tb, lfo); Pump();
{
    var (model, pick, _) = LastPicker();
    Check(model.Grouped && model.Visible[0].IsHeading && model.Visible[0].Label == engine.DeviceName(tb, delay), "cross-track picker is grouped, first heading is B's first device");
    model.Query = "freq";
    bool underRight = true; string? head = null;
    foreach (var v in model.Visible) { if (v.IsHeading) head = v.Label; else underRight &= v.Group == head; }
    Check(model.Highlight >= 0 && underRight, "search keeps each match under its own device heading");
    var payload = model.Pick()!;
    pick(payload); Pump();
    bool linked = false;
    for (int i = 0; i < engine.CvLinkCount(ta); i++) linked |= engine.CvLinkTargetTrack(ta, i) == tb;
    Check(linked, "picking links into track B (link owned by track A)");
}
int tc = engine.AddAudioTrack();
Invoke(view, "ShowCrossTrackPicker", view, tc, lfo); Pump();
Check(LastPicker().Model.EmptyKey == ParamPickerModel.NoParamsKey && LastPicker().Model.NoParamsText is not null, "a track with nothing to modulate shows the empty state");
LastPicker().Flyout.Hide(); Pump();

// US2 (T013): right-click "Modulate from another track" lists modulators grouped by track.
view.Show(tb); Pump();
Invoke(view, "ShowCrossSourcePicker", view, 0, delay, 0, "Time"); Pump();
{
    var (model, pick, _) = LastPicker();
    Check(model.Grouped && model.Visible.Any(v => v.IsHeading) && model.Visible.Any(v => !v.IsHeading && v.Label.EndsWith(" " + lfo)), "cross-source picker lists other tracks' modulators");
    pick(model.Pick()!); Pump();
    Check(HasLink(ta, tb, 0, delay, 0), "picking a cross-track source creates the link owned by the source track");
}

// ---- cards, sizes, drop on a knob (US3 / US4) — appended below as they land ----------------
CardChecks.Run(engine, view, Check, Pump);

// ---- a real big plug-in (T035 / SC-001, SC-003): NOTA_MODULAR_BIG_PLUGIN=<path to .vst3> ------
if (Environment.GetEnvironmentVariable("NOTA_MODULAR_BIG_PLUGIN") is { Length: > 0 } bundle && Directory.Exists(bundle))
{
    // Scan just that bundle (a folder holding a link to it), with the engine's scan worker.
    string only = Path.Combine(scratch, "vst3-only"); Directory.CreateDirectory(only);
    string link = Path.Combine(only, Path.GetFileName(bundle));
    if (!Directory.Exists(link)) Directory.CreateSymbolicLink(link, bundle);
    string worker = Environment.GetEnvironmentVariable("NOTA_SCAN_WORKER")
        ?? Path.GetFullPath("src/native/nota.engine/build/nota-scanworker");
    NotaEngine.AddScanPath(only);
    int found = NotaEngine.ScanPlugins(worker, only, true);
    string want = Path.GetFileNameWithoutExtension(bundle);
    int ci = -1;
    for (int i = 0; i < found; i++) if (NotaEngine.PluginDescription(i)?.StartsWith(want, StringComparison.OrdinalIgnoreCase) == true) { ci = i; break; }
    Check(ci >= 0, $"{want} is in the catalog ({found} scanned)");
    if (ci >= 0)
    {
        int pt = engine.AddPluginInstrumentTrack(ci);
        int pl = engine.ModulatorAdd(pt, 0);
        int pc = engine.PluginParamCount(pt, -1);
        var bsw = Stopwatch.StartNew();
        view.Show(pt); Dispatcher.UIThread.RunJobs();
        double showMs = bsw.Elapsed.TotalMilliseconds;
        var pnode = NodeById("inst");
        Console.WriteLine($"  ({want}: {pc} parameters; Modular with its card in {showMs:0} ms; node at {Prop<object>(pnode, "Size")})");
        bsw.Restart();
        Invoke(view, "ShowTargetPicker", pnode, pl);
        Dispatcher.UIThread.RunJobs();
        double pickMs = bsw.Elapsed.TotalMilliseconds;
        var (bm2, bpick, bfl) = LastPicker();
        Check(pickMs < 150, $"{want}: the picker opens with {bm2.Entries.Count} parameters in < 150 ms ({pickMs:0} ms)");
        string? target = bm2.Entries.Skip(bm2.Entries.Count / 2).FirstOrDefault(x => !x.IsHeading && x.Label.Length > 3)?.Label;
        if (target is not null)
        {
            bsw.Restart();
            bm2.Query = target;
            double typeMs = bsw.Elapsed.TotalMilliseconds;
            var payloadBig = bm2.Pick();
            Check(payloadBig is not null && bm2.Visible[bm2.Highlight].Label == target && typeMs < 50,
                $"{want}: typing '{target}' leaves it highlighted ({typeMs:0.0} ms)");
            int lcBefore = engine.CvLinkCount(pt);
            bpick(payloadBig!); Pump();
            Check(engine.CvLinkCount(pt) == lcBefore + 1, $"{want}: Enter links the LFO to '{target}'");
        }
        else bfl.Hide();
    }
}

// ---- screenshots for the PR / docs (NOTA_MODULAR_SHOTS=<dir>) -----------------------------
if (Environment.GetEnvironmentVariable("NOTA_MODULAR_SHOTS") is { Length: > 0 } shots)
{
    Directory.CreateDirectory(shots);
    int st = engine.AddAudioTrack();
    engine.SetTrackName(st, "Drums bus");
    int c1 = engine.AddBuiltinDevice(st, 1);   // Compressor → S
    int c2 = engine.AddBuiltinDevice(st, 7);   // Auto Filter → set to XS
    int c3 = engine.AddBuiltinDevice(st, 2);   // Reverb → set to L
    int sl = engine.ModulatorAdd(st, 0);
    engine.CvLinkAddToTarget(st, sl, 0, -1, c2, 0);
    foreach (var theme in new[] { "studio-dark", "studio-light", "ember-graphite", "ember-paper" })
    {
        NotaThemeService.Set(new AppearanceChoice(false, theme, theme, theme));
        var sv = new ModularView(engine);
        var sw2 = new Window { Width = 1700, Height = 900, Content = sv }; sw2.Show();
        sv.Show(st); Pump(); Pump();
        sv.SetNodeSize(sv.Nodes.First(n => n.Id == $"dev{c2}"), ModularNodeSize.XS); Pump(); Pump();
        sv.SetNodeSize(sv.Nodes.First(n => n.Id == $"dev{c3}"), ModularNodeSize.L); Pump(); Pump();
        sv.Tick(false); Pump();
        using (var img = sw2.CaptureRenderedFrame()) img?.Save(Path.Combine(shots, $"modular-cards-{theme}.png"));
        // The picker, rendered on its own the way the flyout shows it.
        var host = new Border { Width = 360, Height = 470, Background = NotaPalette.CanvasBg };
        var pw = new Window { Width = 360, Height = 470, Content = host }; pw.Show(); Pump();
        var bigEntries = new List<ParamPickerEntry>();
        string[] heads = { "Compressor", "Auto Filter", "Reverb" };
        foreach (var h in heads)
        {
            bigEntries.Add(ParamPickerEntry.Heading(h));
            for (int i = 0; i < 12; i++) bigEntries.Add(ParamPickerEntry.Item($"{h} param {i + 1}", i, h));
        }
        var pm = new ParamPickerModel(bigEntries, grouped: true);
        ParamPickerPanel.Show(host, "Drums bus", pm, _ => { }, atPointer: false); Pump(); Pump();
        if (ParamPickerPanel.Last?.Flyout.Content is Control pc && TopLevel.GetTopLevel(pc) is { } top)
            using (var img = top.CaptureRenderedFrame()) img?.Save(Path.Combine(shots, $"picker-{theme}.png"));
        else Console.WriteLine("  (picker popup not found for a capture)");
        pm.Query = "freq"; Pump();
        ParamPickerPanel.Last?.Flyout.Hide(); Pump();
        pw.Close(); sw2.Close(); Pump();
    }
    Console.WriteLine($"Screenshots in {shots}");
}

Console.WriteLine(failures == 0 ? "All Modular UI checks passed." : $"{failures} Modular UI check(s) FAILED.");
window.Close();
try { Directory.Delete(scratch, true); } catch { }
return failures == 0 ? 0 : 1;

static void Pump() { for (int i = 0; i < 3; ++i) { Dispatcher.UIThread.RunJobs(); Thread.Sleep(15); } }

static class CardChecks
{
    public static void Run(NotaEngine engine, ModularView view, Action<bool, string> check, Action pump)
    {
        ModularView.GNode Node(string id) => view.Nodes.First(n => n.Id == id);
        IEnumerable<Control> Bound(ModularView.GNode n, MidiTargetKind kind, int dev, int param)
            => n.View.GetVisualDescendants().OfType<Control>().Where(c => MidiLearn.GetBinding(c) is { } b
                && b.Target.Kind == kind && b.Target.DeviceIndex == dev && b.Target.ParamIndex == param);
        int ParamIndex(int track, int dev, string name)
        { for (int p = 0; p < engine.DeviceParamCount(track, dev); p++) if (engine.DeviceParamName(track, dev, p) == name) return p; return -1; }

        int t = engine.AddAudioTrack();
        int comp = engine.AddBuiltinDevice(t, 1);    // Compressor: has S
        int rack = engine.AddBuiltinDevice(t, 5);    // Audio Effect Rack: L only
        int lfo = engine.ModulatorAdd(t, 0);
        var detailBefore = DeviceView.Size(engine, t, comp);
        view.Show(t); pump(); pump();

        // US3 (T022): default sizes, a live card, Detail untouched, a drop on a knob.
        var cn = Node($"dev{comp}");
        check(cn.Size == ModularNodeSize.S && cn.HasSmall, "a device whose card has S opens at S");
        check(Node($"dev{rack}").Size == ModularNodeSize.L, "a rack (L only) opens at L");
        int ratio = ParamIndex(t, comp, "Ratio");
        var knob = Bound(cn, MidiTargetKind.DeviceParam, comp, ratio).FirstOrDefault();
        check(knob is not null, "the S card on the node carries the device's Ratio control");
        float min = engine.DeviceParamMin(t, comp, ratio), max = engine.DeviceParamMax(t, comp, ratio);
        double v0 = (knob as Knob)?.Value ?? -1;
        engine.DeviceSetParam(t, comp, ratio, engine.DeviceGetParam(t, comp, ratio) < (min + max) / 2 ? max : min);
        view.Tick(false); pump();
        check(knob is Knob k && Math.Abs(k.Value - v0) > 0.05, "the card follows the engine value on the tick (as in Detail)");
        check(knob is not null && view.TryLinkAtControl(knob, lfo), "a cable dropped on the Ratio knob links it directly");
        bool linked = false;
        for (int i = 0; i < engine.CvLinkCount(t); i++) linked |= engine.CvLinkDevice(t, i) == comp && engine.CvLinkParam(t, i) == ratio;
        check(linked, "the direct link targets Ratio");
        var plain = cn.View.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c is TextBlock && MidiLearn.GetBinding(c) is null
            && !c.GetVisualAncestors().OfType<Control>().Any(a => MidiLearn.GetBinding(a) is not null));
        check(plain is null || !view.TryLinkAtControl(plain, lfo), "a control that drives nothing doesn't link (the picker opens instead)");

        // A real cable drag with the mouse, on a zoomed canvas: from the LFO's CV out onto the
        // card's Release knob — the drop links it and no picker opens.
        var win = (Window)TopLevel.GetTopLevel(view)!;
        typeof(ModularView).GetMethod("ZoomAround", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object[] { new Point(300, 300), 0.8 });
        pump();
        cn = Node($"dev{comp}");
        int release = ParamIndex(t, comp, "Release");   // on the S card
        var attackKnob = Bound(cn, MidiTargetKind.DeviceParam, comp, release).First();
        var lfoNode = view.Nodes.First(n => n.Slot == Slot.Modulator && n.ModId == lfo);
        var cvOut = lfoNode.View.GetVisualDescendants().OfType<Border>().First(b => b.Cursor?.ToString()?.Contains("Cross") == true);
        Point Center(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), win)!.Value;
        var from = Center(cvOut); var to = Center(attackKnob);
        win.MouseDown(from, Avalonia.Input.MouseButton.Left); pump();
        for (int i = 1; i <= 6; i++) { win.MouseMove(from + (to - from) * (i / 6.0), Avalonia.Input.RawInputModifiers.LeftMouseButton); pump(); }
        win.MouseUp(to, Avalonia.Input.MouseButton.Left); pump();
        bool attackLinked = false;
        for (int i = 0; i < engine.CvLinkCount(t); i++) attackLinked |= engine.CvLinkDevice(t, i) == comp && engine.CvLinkParam(t, i) == release;
        check(attackLinked && ParamPickerPanel.Last is null, "a mouse drag from CV out onto a card knob (canvas zoomed) links it, no picker");
        // …and released on the node's empty header area instead, the picker opens.
        cn = Node($"dev{comp}");
        var headerSpot = cn.View.TranslatePoint(new Point(cn.View.Bounds.Width * 0.45, 17), win)!.Value;
        lfoNode = view.Nodes.First(n => n.Slot == Slot.Modulator && n.ModId == lfo);
        cvOut = lfoNode.View.GetVisualDescendants().OfType<Border>().First(b => b.Cursor?.ToString()?.Contains("Cross") == true);
        from = Center(cvOut);
        win.MouseDown(from, Avalonia.Input.MouseButton.Left); pump();
        win.MouseMove(headerSpot, Avalonia.Input.RawInputModifiers.LeftMouseButton); pump();
        win.MouseUp(headerSpot, Avalonia.Input.MouseButton.Left); pump();
        check(ParamPickerPanel.Last is not null, "a mouse drop elsewhere on the node opens the picker");
        ParamPickerPanel.Last?.Flyout.Hide(); pump();
        typeof(ModularView).GetMethod("ZoomAround", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(view, new object[] { new Point(300, 300), 1 / 0.8 });
        pump();

        // Right-click a card knob: Modular's parameter menu (with Expose as CV output), not only Type value….
        Popup? menuPopup = null;
        Popup.IsOpenProperty.Changed.AddClassHandler<Popup>((p, _) => { if (p.IsOpen) menuPopup = p; });
        cn = Node($"dev{comp}");
        var ratioKnob = Bound(cn, MidiTargetKind.DeviceParam, comp, ratio).First();
        ratioKnob.RaiseEvent(new Avalonia.Input.ContextRequestedEventArgs { RoutedEvent = Control.ContextRequestedEvent, Source = ratioKnob });
        pump();
        var headers = menuPopup?.Child is Control mc ? mc.GetVisualDescendants().OfType<MenuItem>().Select(m => m.Header?.ToString() ?? "").ToList() : new List<string>();
        check(headers.Contains(Nota.Application.Localization.Loc.T("modular.expose_as_cv_output")),
            $"right-clicking a card knob opens Modular's menu ({string.Join(", ", headers.Take(6))})");
        if (menuPopup is not null) { menuPopup.IsOpen = false; pump(); }

        // A cable dropped on an XS row's name or value links that row's parameter.
        view.SetNodeSize(cn, ModularNodeSize.XS); pump(); pump();
        cn = Node($"dev{comp}");
        var rowGrid = cn.View.GetVisualDescendants().OfType<Grid>().First(g => g.Tag is SliderTrack);
        var rowValue = rowGrid.Children.OfType<TextBlock>().Last();
        int linksBeforeRow = engine.CvLinkCount(t);
        int lfo2 = engine.ModulatorAdd(t, 0); view.Show(t); pump(); pump();
        cn = Node($"dev{comp}");
        rowGrid = cn.View.GetVisualDescendants().OfType<Grid>().First(g => g.Tag is SliderTrack);
        rowValue = rowGrid.Children.OfType<TextBlock>().Last();
        check(view.TryLinkAtControl(rowValue, lfo2) && engine.CvLinkCount(t) == linksBeforeRow + 1, "a drop on an XS row's value text links the row's parameter");
        view.SetNodeSize(cn, ModularNodeSize.S); pump(); pump();

        // EQ-8 behind the size proxy keeps its band-label / note controls (only rack proxies drop them).
        int eqT = engine.AddAudioTrack(); int eq = engine.AddBuiltinDevice(eqT, 0);
        DeviceCardContext Ctx(Nota.Application.IAudioEngine e) => new(e, eqT, _ => { }, () => { }, (_, _, _, _) => { }, _ => { }, _ => { },
            () => 0, _ => { }, _ => { }, () => { }, () => { }, _ => { }, () => { });
        // The text controls are built either way but hidden without text support: count the hidden ones.
        int Count(Control c) => Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(c).OfType<Control>().Count(x => !x.IsVisible);
        var direct = new EqDeviceBody().Build(Ctx(engine), eq);
        var viaProxy = new EqDeviceBody().Build(Ctx(CardSizeEngineProxy.Create(engine, eqT, CardFamily.Effect, eq, () => false, _ => { })), eq);
        check(Count(direct) == Count(viaProxy), $"EQ-8 through the size proxy shows the same controls as direct (hidden: {Count(viaProxy)} vs {Count(direct)})");

        // US4 (T026): XS ↔ L in place, cables re-anchored, neighbours still.
        cn = Node($"dev{comp}");
        var at = cn.World;
        var others = view.Nodes.Where(n => n != cn).ToDictionary(n => n.Id, n => n.World);
        view.SetNodeSize(cn, ModularNodeSize.XS); pump(); pump();
        cn = Node($"dev{comp}");
        check(cn.Size == ModularNodeSize.XS && Math.Abs(cn.Width - 214) < 0.5 && cn.World == at, "XS: today's node width, same top-left");
        view.SetNodeSize(cn, ModularNodeSize.L); pump(); pump();
        cn = Node($"dev{comp}");
        check(cn.Size == ModularNodeSize.L && cn.Width > 214 && cn.World == at, "L: the full card, wider, same top-left");
        check(Math.Abs(cn.Anchor(PortKind.Audio, output: true).X - (cn.World.X + cn.View.Bounds.Width)) < 1, "the audio-out port sits on the card's right edge");
        var row = view.Nodes.Where(n => Math.Abs(n.World.Y - cn.World.Y) < 0.5).OrderBy(n => n.World.X).ToList();
        check(row.Zip(row.Skip(1)).All(p => p.Second.World.X >= p.First.World.X + p.First.View.Bounds.Width - 0.5), "the chain row re-flows: no node overlaps the wider card");
        check(view.Nodes.Where(n => n.World.X < cn.World.X).All(n => others.TryGetValue(n.Id, out var w) && w == n.World), "nodes before it don't move");
        check(DeviceView.Size(engine, t, comp) == detailBefore, "Detail's card size is untouched by Modular sizes");

        // Persistence: the sidecar keeps the size; an old [x, y] sidecar opens with defaults.
        string dir = Path.Combine(Path.GetTempPath(), "nota-modular-layout-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        view.SaveLayout(dir);
        var view2 = new ModularView(engine);
        var w2 = new Window { Width = 1400, Height = 900, Content = view2 }; w2.Show();
        view2.LoadLayout(dir); view2.Show(t); pump(); pump();
        var cn2 = view2.Nodes.First(n => n.Id == $"dev{comp}");
        check(cn2.Size == ModularNodeSize.L && cn2.World == at, "a saved size and position come back on reopen");
        int doc = -1;
        for (int i = 0; i < engine.TrackCount; i++) if (engine.TryGetTrackInfo(i, out var ti) && ti.Id == t) doc = i;
        File.WriteAllText(Path.Combine(dir, "modular-layout.json"), $"{{\"{doc}/dev{comp}\":[10,20]}}");
        view2.LoadLayout(dir); view2.Show(t); pump(); pump();
        cn2 = view2.Nodes.First(n => n.Id == $"dev{comp}");
        check(cn2.Size == ModularNodeSize.S && cn2.World == new Point(10, 20), "a legacy [x, y] sidecar loads with the default size");
        w2.Close();
        try { Directory.Delete(dir, true); } catch { }

        // A card that fails to build falls back to XS with its parameter rows (not an empty node).
        view.FailCardForTest = n => n.Id == $"dev{comp}";
        view.Show(t); pump(); pump();
        var failed = Node($"dev{comp}");
        check(failed.Size == ModularNodeSize.XS && failed.Params.Count > 0 && failed.View.GetVisualDescendants().OfType<SliderTrack>().Any(),
            $"a card that can't be built falls back to XS with its rows ({failed.Params.Count})");
        view.FailCardForTest = null;

        // A card moved a little below the row still pushes the auto-placed rest of the chain past it.
        int t2 = engine.AddAudioTrack();
        int a1 = engine.AddBuiltinDevice(t2, 1), a2 = engine.AddBuiltinDevice(t2, 7), a3 = engine.AddBuiltinDevice(t2, 2);
        view.Show(t2); pump(); pump();
        var moved = Node($"dev{a1}");
        var savedPos = (Dictionary<string, Point>)typeof(ModularView).GetField("_savedPos", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(view)!;
        savedPos[$"{t2}/dev{a1}"] = new Point(moved.World.X + 30, moved.World.Y + 40);
        view.SetNodeSize(moved, ModularNodeSize.XS); pump(); pump();
        moved = Node($"dev{a1}");
        view.SetNodeSize(moved, ModularNodeSize.L); pump(); pump();
        moved = Node($"dev{a1}");
        var next = Node($"dev{a2}");
        bool vOverlap = next.World.Y < moved.World.Y + moved.View.Bounds.Height && next.World.Y + next.View.Bounds.Height > moved.World.Y;
        check(!vOverlap || next.World.X >= moved.World.X + moved.View.Bounds.Width - 0.5,
            $"a grown card moved off the row doesn't cover the next node (next at {next.World.X:0}, card ends {moved.World.X + moved.View.Bounds.Width:0})");

        // Every built-in effect, the default instrument and the Arpeggiator host their card (no XS fallback).
        int sweep = engine.AddAudioTrack();
        var kinds = new List<int>();
        for (int bk = 0; bk <= 26; bk++) if (engine.AddBuiltinDevice(sweep, bk) >= 0) kinds.Add(bk);
        var dsw = System.Diagnostics.Stopwatch.StartNew();
        view.Show(sweep); Dispatcher.UIThread.RunJobs();
        double drawMs = dsw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"  (Modular built {kinds.Count} card nodes and laid them out in {drawMs:0} ms)");
        pump(); pump();
        var fallen = view.Nodes.Where(n => n.Slot == Slot.Device && n.Size == ModularNodeSize.XS).Select(n => n.Title).ToList();
        check(kinds.Count > 20 && fallen.Count == 0, $"all {kinds.Count} built-in effects show a card" + (fallen.Count > 0 ? " — XS: " + string.Join(", ", fallen) : ""));
        int it = engine.AddInstrumentTrack();
        engine.AddMidiEffect(it, 0);
        view.Show(it); pump(); pump();
        var inst = view.Nodes.First(n => n.Slot == Slot.Instrument);
        var arp = view.Nodes.First(n => n.Slot == Slot.MidiFx);
        check(inst.Size != ModularNodeSize.XS && arp.Size != ModularNodeSize.XS, $"the instrument ({inst.Title}, {inst.Size}) and the Arpeggiator ({arp.Size}) show cards");
        check(arp.HasSmall && arp.Size == ModularNodeSize.S, "the Arpeggiator (View param) opens at S");
    }
}
