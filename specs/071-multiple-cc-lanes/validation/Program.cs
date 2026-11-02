// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System.Collections;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Nota.App;
using Nota.Application;
using Nota.Presentation;

internal static class Program
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Get(object o, string n) => o.GetType().GetField(n, Hidden)!.GetValue(o);
    static object? Call(object o, string n, params object[] a) => o.GetType().GetMethod(n, Hidden)!.Invoke(o,a);
    [STAThread] static int Main(string[] args)
    {
        if(args[0]=="--read-lanes")
        {
            string root=Path.GetFullPath(args[1]);
            Environment.SetEnvironmentVariable("NOTA_DATA_DIR",Path.Combine(root,"probe-data"));
            var expected=JsonSerializer.Deserialize<Dictionary<string,double>>(File.ReadAllText(Path.Combine(root,"lane-height-expected.json")))!;
            var actual=new Nota.Infrastructure.SettingsService().Current.PianoRollLaneHeights;
            bool restored=expected.Count>=3 && expected.All(pair=>actual.GetValueOrDefault(pair.Key)==pair.Value);
            Console.WriteLine(restored?"Fresh process restores independent lane heights":"Lane-height restart failed");
            return restored?0:1;
        }
        try { Run(Path.GetFullPath(args[0])); return 0; } catch(Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    static void Run(string root)
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("NOTA_DATA_DIR", Path.Combine(root,"probe-data"));
        var lifetime = new ClassicDesktopStyleApplicationLifetime { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        AppBuilder.Configure<Nota.App.App>().UseWin32().UseSkia().UseHarfBuzz().WithNotaFonts().SetupWithLifetime(lifetime);
        var settings = (ISettingsService)App.Services.GetService(typeof(ISettingsService))!;
        settings.Current.ShowWelcomeOnStartup=false; settings.Current.CheckForUpdates=false; settings.Current.LastSeenVersion=AppInfo.Version;
        settings.Current.PianoRollLaneHeights.Clear();
        using var stop = new CancellationTokenSource(); Exception? failure=null; bool done=false;
        var checks = new List<string>();
        void Require(bool ok,string label) { if(!ok) throw new Exception(label); checks.Add(label); }
        DispatcherTimer.RunOnce(()=> {
            try {
                var roll = new PianoRollView { LaneSettings=settings };
                var notes = new[] { new NotaNote(60,0,1,.8f), new NotaNote(64,2,1,.7f) };
                roll.SetNotes(notes,16);
                var data = new Dictionary<int,AutomationPoint[]> {
                    [1]=[new(0,.2f),new(4,.8f)], [11]=[new(0,.4f),new(8,.9f)], [64]=[new(1,1),new(3,0)] };
                int commits=0;
                roll.ControllersInClip=()=>data.Keys.ToArray();
                roll.GetController=id=>data.GetValueOrDefault(id,[]);
                roll.SetController=(id,p)=>{ data[id]=p; commits++; };
                var window = new Window { Content=roll,Width=1000,Height=720,Title="CC lane validation",
                    Background=(IBrush)Avalonia.Application.Current!.Resources["Brush.BgApp"]! };
                window.Show(); window.UpdateLayout();
                var add = roll.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="AddControllerLane");
                void Add()=>add.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Add();Add();Add(); window.UpdateLayout();
                var lanes=(IList)Get(roll,"_controllerLanes")!;
                Require(lanes.Count==3,"plus adds three independent lane views");
                object Lane(int i)=>lanes[i]!.GetType().GetProperty("Lane")!.GetValue(lanes[i])!;
                int Ctl(int i)=>(int)Lane(i).GetType().GetProperty("Controller",Hidden)!.GetValue(Lane(i))!;
                Require(new[]{Ctl(0),Ctl(1),Ctl(2)}.Distinct().Count()==3,"new lanes choose different controllers");
                Require(new[]{Ctl(0),Ctl(1),Ctl(2)}.Contains(64),"switch-type sustain shares the existing block editor");
                Grid Row(int i)=>(Grid)lanes[i]!.GetType().GetProperty("Row")!.GetValue(lanes[i])!;
                var primary=(Grid)Get(roll,"_laneRow")!;
                void Drag(Grid row,double growth)
                {
                    var divider=((DockPanel)row.Parent!).Children.OfType<Border>().Single();
                    var p=divider.TranslatePoint(new Point(20,3),window)!.Value;
                    using var pointer=new Avalonia.Input.Pointer(42,PointerType.Mouse,true);
                    var down=new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed);
                    divider.RaiseEvent(new PointerPressedEventArgs(divider,pointer,window,p,1,down,KeyModifiers.None,1));
                    divider.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,divider,pointer,window,p.WithY(p.Y-growth),2,down,KeyModifiers.None));
                    divider.RaiseEvent(new PointerReleasedEventArgs(divider,pointer,window,p.WithY(p.Y-growth),3,
                        new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
                    window.UpdateLayout();
                }
                Drag(primary,50);Drag(Row(0),45);Drag(Row(1),85);
                Require(primary.Height==122 && Row(0).Height==157 && Row(1).Height==197,
                    "actual pointer drags resize Velocity and two CC lanes independently");
                var second=Lane(1);
                second.GetType().GetProperty("Controller",Hidden)!.SetValue(second,Ctl(0));
                Call(roll,"FitControllerLanes");Drag(Row(1),20);
                Require(Row(0).Height==157 && Row(1).Height==132,"duplicate controller views retain independent heights");
                second.GetType().GetProperty("Controller",Hidden)!.SetValue(second,11);Call(roll,"FitControllerLanes");
                var reopened=new PianoRollView {LaneSettings=settings,ControllersInClip=roll.ControllersInClip,GetController=roll.GetController};
                reopened.SetNotes(notes,16);
                var reopenedWindow=new Window {Content=reopened,Width=1000,Height=720};reopenedWindow.Show();reopenedWindow.UpdateLayout();
                var reopenedAdd=reopened.GetVisualDescendants().OfType<Button>().Single(b=>b.Name=="AddControllerLane");
                reopenedAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));reopenedAdd.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));reopenedWindow.UpdateLayout();
                var restored=(IList)Get(reopened,"_controllerLanes")!;
                Require(((Grid)Get(reopened,"_laneRow")!).Height==122
                    && ((Grid)restored[0]!.GetType().GetProperty("Row")!.GetValue(restored[0])!).Height==157
                    && ((Grid)restored[1]!.GetType().GetProperty("Row")!.GetValue(restored[1])!).Height==197,
                    "editor reopening restores Velocity and corresponding CC lane heights");
                reopenedWindow.Close();
                File.WriteAllText(Path.Combine(root,"lane-height-expected.json"),JsonSerializer.Serialize(settings.Current.PianoRollLaneHeights));
                Require(commits==0,"resizing and editor reopening do not commit MIDI edits");
                string Snapshot()=>JsonSerializer.Serialize(data,new JsonSerializerOptions{IncludeFields=true});
                string original=Snapshot();
                var remove=roll.GetVisualDescendants().OfType<Button>().First(b=>ToolTip.GetTip(b)?.ToString()?.StartsWith("Hide controller lane")==true);
                remove.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(lanes.Count==2 && Snapshot()==original && commits==0,"minus removes only a view and preserves all CC points");
                Add(); Require(lanes.Count==3 && Snapshot()==original,"readding a lane preserves controller data");
                int target=Ctl(0); var untouched=data.Where(p=>p.Key!=target).ToDictionary(p=>p.Key,p=>p.Value);
                Call(roll,"StoreController",target,new List<AutomationPoint>{new(0,.1f),new(2,.7f)});
                Require(commits==1 && untouched.All(p=>data[p.Key].SequenceEqual(p.Value)),"existing commit boundary edits only its target controller");
                var lane=Lane(0); var drag=lane.GetType().GetField("_drag",Hidden)!;
                drag.SetValue(lane,Enum.Parse(drag.FieldType,"Point"));
                lane.GetType().GetField("_work",Hidden)!.SetValue(lane,new List<AutomationPoint>{new(1,.5f)});
                roll.SetNotes(notes,16);
                Require(drag.GetValue(lane)!.ToString()=="None" && Get(lane,"_work") is null && lanes.Count==3,
                    "reload cancels stale controller gestures and keeps lane layout");
                for(int i=0;i<7;i++)Add();
                Call(roll,"ZoomAt",2.0,4.0); Call(roll,"SetScrollBeats",2.0); window.UpdateLayout();
                var sc=roll.GetVisualDescendants().OfType<ScrollViewer>().Single(s=>s.Name=="ControllerLanes");
                Require(sc.Extent.Height>sc.Viewport.Height && sc.Bounds.Height<=window.Bounds.Height-120,"many lanes scroll in a bounded area");
                Require(((Control)Get(roll,"_gridControl")!).Bounds.Height>0,"note grid remains available");
                window.Height=300; window.UpdateLayout(); window.UpdateLayout();
                Require(((ScrollViewer)Get(roll,"_bodyScroll")!).Viewport.Height>=40,
                    "short editors retain a visible note viewport with many CC lanes");
                window.Height=720; window.UpdateLayout();
                Require(lanes.Cast<object>().All(x=>((Grid)x.GetType().GetProperty("Row")!.GetValue(x)!).ColumnDefinitions[0].Width.Value
                    ==((Grid)((Control)Get(roll,"_gridControl")!).Parent!).ColumnDefinitions[0].Width.Value),"all lane gutters follow the note-grid column");
                while(lanes.Count>3)Call(roll,"RemoveControllerLane",lanes[lanes.Count-1]!);
                window.UpdateLayout();
                foreach(string theme in new[]{"studio-dark","studio-light"}) {
                    typeof(MainWindow).Assembly.GetType("Nota.App.NotaThemeService")!.GetMethod("Set",BindingFlags.Public|BindingFlags.Static)!
                        .Invoke(null,new object[]{new AppearanceChoice(false,theme,"studio-dark","studio-light")});
                    using var bitmap=new RenderTargetBitmap(new PixelSize(1000,720)); bitmap.Render(window); bitmap.Save(Path.Combine(root,theme+"-cc.png"));
                }
                Require(commits==1,"view, theme and zoom operations never write controller data");
                CheckFoldedLanes(root, Require);
                CheckPrimaryLaneTabs(root, settings, Require);
                File.WriteAllText(Path.Combine(root,"cc-results.json"),JsonSerializer.Serialize(checks,new JsonSerializerOptions{WriteIndented=true}));
                window.Close();
                // Drain theme-change work before disposing the real app's engine.
                Dispatcher.UIThread.Post(()=> {
                    typeof(MainWindow).GetField("_closeConfirmed",Hidden)!.SetValue(lifetime.MainWindow,true);
                    lifetime.MainWindow!.Close(); done=true; stop.Cancel();
                },DispatcherPriority.Background);
            }catch(Exception e){failure=e;stop.Cancel();}
        },TimeSpan.FromMilliseconds(900));
        using var watchdog=new Timer(_=>Dispatcher.UIThread.Post(()=>stop.Cancel()),null,15000,Timeout.Infinite);
        Dispatcher.UIThread.MainLoop(stop.Token);
        if(failure is not null)throw failure; if(!done)throw new Exception("Probe timed out");
        Console.WriteLine(string.Join(Environment.NewLine,checks));
    }

    static void CheckPrimaryLaneTabs(string root, ISettingsService settings, Action<bool,string> require)
    {
        var previous=new Dictionary<string,double>(settings.Current.PianoRollLaneHeights);
        var shortHeights=new Dictionary<string,double> {
            ["Velocity"]=48,["Bend"]=48,["Pressure"]=48,["Slide"]=48,["cc:1"]=48,["extra:0:cc:1"]=48 };
        settings.Current.PianoRollLaneHeights=shortHeights;settings.Save();
        int commits=0;
        PianoRollView NewRoll(ISettingsService saved)
        {
            var view=new PianoRollView {LaneSettings=saved,ControllersInClip=()=>[1],GetController=_=>[],
                SetController=(_,_)=>commits++};
            view.SetNotes([new NotaNote(60,2,1,.8f)],8);
            return view;
        }
        var roll=NewRoll(settings);
        var window=new Window {Content=roll,Width=1000,Height=720};window.Show();window.UpdateLayout();
        var row=(Grid)Get(roll,"_laneRow")!;
        var divider=((DockPanel)row.Parent!).Children.OfType<Border>().Single();
        var start=divider.TranslatePoint(new Point(20,3),window)!.Value;
        using(var pointer=new Avalonia.Input.Pointer(44,PointerType.Mouse,true))
        {
            var down=new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed);
            divider.RaiseEvent(new PointerPressedEventArgs(divider,pointer,window,start,1,down,KeyModifiers.None,1));
            divider.RaiseEvent(new PointerEventArgs(InputElement.PointerMovedEvent,divider,pointer,window,start.WithY(start.Y+200),2,down,KeyModifiers.None));
            divider.RaiseEvent(new PointerReleasedEventArgs(divider,pointer,window,start.WithY(start.Y+200),3,
                new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        }
        window.UpdateLayout();
        using(var bitmap=new RenderTargetBitmap(new PixelSize(1000,720)))
        { bitmap.Render(window);bitmap.Save(Path.Combine(root,"primary-tabs.png")); }
        void ClickTab(int index,string expected)
        {
            // Let the new window's compositor publish its hit-test scene.
            var frame=new DispatcherFrame();
            DispatcherTimer.RunOnce(()=>frame.Continue=false,TimeSpan.FromMilliseconds(100));
            Dispatcher.UIThread.PushFrame(frame);
            var tabs=(Control)Get(roll,"_laneTabs")!;
            var point=tabs.TranslatePoint(new Point(20,8+13*index),window)!.Value;
            var target=window.InputHitTest(point);
            if(!ReferenceEquals(target,tabs)) Console.WriteLine($"Tab hit mismatch: expected {expected}, hit {target?.GetType().Name}, tabs {tabs.Bounds}, row {((Grid)Get(roll,"_laneRow")!).Height}, point {point}");
            require(ReferenceEquals(target,tabs),$"{expected} tab receives pointer hit after shrinking primary lane");
            using var pointer=new Avalonia.Input.Pointer(45,PointerType.Mouse,true);
            tabs.RaiseEvent(new PointerPressedEventArgs(tabs,pointer,window,point,1,
                new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed),KeyModifiers.None,1));
            window.UpdateLayout();
            require(Get(roll,"_laneMode")!.ToString()==expected,$"pointer click selects {expected} after shrinking primary lane");
        }
        ClickTab(4,"Controller");ClickTab(0,"Velocity");ClickTab(1,"Bend");ClickTab(2,"Pressure");ClickTab(3,"Slide");
        require(row.Height==72,"all primary modes retain a 72-pixel minimum");
        Call(roll,"AddControllerLane");window.UpdateLayout();
        var lanes=(IList)Get(roll,"_controllerLanes")!;
        require(((Grid)lanes[0]!.GetType().GetProperty("Row")!.GetValue(lanes[0])!).Height==48,
            "extra CC lanes retain their independent 48-pixel minimum");
        window.Close();
        roll=NewRoll(new Nota.Infrastructure.SettingsService());
        window=new Window {Content=roll,Width=1000,Height=300};window.Show();window.UpdateLayout();
        require(((Grid)Get(roll,"_laneRow")!).Height==72,"reopened short editor clamps saved 48-pixel primary height to 72");
        ClickTab(4,"Controller");
        require(((ScrollViewer)Get(roll,"_bodyScroll")!).Viewport.Height>=40,"short editor keeps note viewport with reachable primary tabs");
        require(commits==0,"shrinking and switching primary tabs never commit controller data");
        window.Close();settings.Current.PianoRollLaneHeights=previous;settings.Save();
    }

    static void CheckFoldedLanes(string root, Action<bool,string> require)
    {
        var roll = new PianoRollView { FoldPads=()=>new Dictionary<int,string> { [36]="Kick", [38]="Snare" } };
        roll.SetNotes([new NotaNote(36,2,1,.8f),new NotaNote(38,4,1,.7f)],8);
        var points = new[] { new AutomationPoint(0,.2f), new AutomationPoint(6,.8f) };
        roll.ControllersInClip=()=>[1]; roll.GetController=_=>points;
        int commits=0;
        roll.SetController=(_,updated)=>{points=updated;commits++;};
        var window=new Window {Content=roll,Width=1000,Height=720};
        window.Show();window.UpdateLayout();
        roll.SetFold(true);
        Call(roll,"AddControllerLane");window.UpdateLayout();
        var lanes=(IList)Get(roll,"_controllerLanes")!;
        Control Lane()=> (Control)lanes[0]!.GetType().GetProperty("Lane")!.GetValue(lanes[0])!;
        var grid=(Control)Get(roll,"_gridControl")!;
        bool Aligned()=>Math.Abs(Lane().TranslatePoint(default,window)!.Value.X
            - grid.TranslatePoint(default,window)!.Value.X)<.01;
        using(var bitmap=new RenderTargetBitmap(new PixelSize(1000,720)))
        {
            bitmap.Render(window);bitmap.Save(Path.Combine(root,"folded-cc.png"));
        }
        double noteX=grid.TranslatePoint(new Point((double)Call(roll,"BeatToX",2.0)!,0),window)!.Value.X;
        var click=new Point(noteX,Lane().TranslatePoint(new Point(0,50),window)!.Value.Y);
        using(var pointer=new Avalonia.Input.Pointer(43,PointerType.Mouse,true))
        {
            var down=new PointerPointProperties(RawInputModifiers.LeftMouseButton,PointerUpdateKind.LeftButtonPressed);
            Lane().RaiseEvent(new PointerPressedEventArgs(Lane(),pointer,window,click,1,down,KeyModifiers.None,2));
            Lane().RaiseEvent(new PointerReleasedEventArgs(Lane(),pointer,window,click,2,
                new PointerPointProperties(RawInputModifiers.None,PointerUpdateKind.LeftButtonReleased),KeyModifiers.None,MouseButton.Left));
        }
        require(commits==1 && points.Any(p=>Math.Abs(p.Beat-2)<1e-6),
            "click directly below a folded drum note writes CC at the same beat");
        require(Aligned(),"CC lane added after Drum Rack folding starts aligned with notes");
        roll.RefreshFold();window.UpdateLayout();
        require(Aligned(),"unchanged folded pad rows preserve new CC lane alignment");
        roll.SetFold(false);window.UpdateLayout();
        require(Aligned() && grid.TranslatePoint(default,roll)!.Value.X==48,
            "unfolding restores the normal aligned 48-pixel gutter");
        roll.SetFold(true);window.UpdateLayout();
        require(Aligned() && grid.TranslatePoint(default,roll)!.Value.X==96,
            "refolding updates existing CC lanes to the 96-pixel pad gutter");
        Call(roll,"RemoveControllerLane",lanes[0]!);Call(roll,"AddControllerLane");window.UpdateLayout();
        require(Aligned(),"CC lane removed and readded while folded keeps alignment");
        window.Close();
    }
}
