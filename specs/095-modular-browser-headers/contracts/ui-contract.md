# UI contract: Modular tab, headers in Modular, Show in Modular

Internal seams between views (all in `Nota.App` unless noted). Names are binding for tasks.

## ArrangementView

```csharp
bool HeadersOnly { get; set; }               // hides overview, sections, ruler/zoom, lanes,
                                             // overlay, footer lanes, h-scroll; width = header column
double HeaderColumnWidth { get; }
event Action? HeaderColumnWidthChanged;      // seam drag / settings
(int TrackId, Control Anchor)? TrackAtHeaderPoint(Point topLevelPoint);   // tracks, groups, returns; not master
void SetColumnHint(bool on);                 // brass divider while a CV cable drags in Modular
Func<int>? ModularTrackId { get; set; }      // asks which track Modular shows (-1 none) when a menu opens
event Action<int>? ShowInModularRequested;   // track menu / footer menu (returns) item
```

Show in Modular item: in `BuildTrackMenu` (tracks, groups) and `ShowFooterMenu` (returns only),
in the naming / inputs group, glyph `GlyphKind.Modular`, disabled when
`HeadersOnly && trackId == ModularTrackId()`.

## SessionView

```csharp
event Action<int>? ShowInModularRequested;   // ShowHeaderMenu, not on the master column
```

## ModularView

```csharp
void ShowTrack(int trackId);                 // leaves Global view, then Show(trackId)
int AddModulatorAt(int kind, Point? viewportPoint = null);   // null → pointer or centre
Func<Point, (int TrackId, Control Anchor)?>? TrackAtPoint { get; set; }  // host: header column
event Action<bool>? ConnectHintChanged;      // cable drag started / ended
event Action<string>? StatusRequested;       // "Open a track in Modular" etc.
bool CanAddNodes { get; }                    // Track view with a track shown
```

`Show(masterTrackId)` is a no-op. Groups are accepted. The "Tracks" rail and **+ Add** are gone.

## BrowserView

```csharp
void SetModularContext(bool inModular);      // shows / hides the Modular tab, applies BrowserTabMemory
static bool IsModularNodeDrag { get; }       // CurrentDrag is a ModularNode row
// IsAcceptableDrag(e) returns false for a ModularNode drag (only the Modular canvas takes it).
// ItemActivated fires for a ModularNode row as for any row.
```

## Nota.Presentation

```csharp
public static class ModularBrowserItems
{
    IReadOnlyList<BrowserItem> Build(IReadOnlyList<ModularNode> nodes);
    bool Matches(BrowserItem item, string query);
}
public sealed class BrowserTabMemory
{
    BrowserTabMemory(int startTab, int modularTab);
    void Picked(int tab);
    int Enter();   // → tab to show in Modular
    int Leave();   // → tab to show outside
}
```

## MainWindow

- `SetView(Modular)`: `Timeline.IsVisible = true; Timeline.HeadersOnly = true;` dock it on the
  header side; `_modular.Margin` = column width on that side; `Browser.SetModularContext(true)`.
  Other views: `HeadersOnly = false`, margin 0, `SetModularContext(false)`.
- `ArrangementShown => Timeline.IsVisible && !Timeline.HeadersOnly` replaces
  `Timeline.IsVisible` where it means "the arrangement is the view".
- `ShowInModularRequested(t)` → `SetView(Modular)`, select `t`, `_modular.ShowTrack(t)`.
- Browser `ItemActivated` with a ModularNode → `_modular.AddModulatorAt(kind)` when
  `CanAddNodes`, else the status line reason.
