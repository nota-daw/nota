// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Nota.App;
using Nota.Application;

internal static class Program
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    [STAThread] static int Main(string[] args)
    {
        string output=Path.GetFullPath(args[0]);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        Environment.SetEnvironmentVariable("NOTA_DATA_DIR",output+"-data");
        var lifetime=new ClassicDesktopStyleApplicationLifetime {ShutdownMode=ShutdownMode.OnExplicitShutdown};
        AppBuilder.Configure<Nota.App.App>().UseWin32().UseSkia().UseHarfBuzz().WithNotaFonts().SetupWithLifetime(lifetime);
        var settings=(ISettingsService)App.Services.GetService(typeof(ISettingsService))!;
        settings.Current.ShowWelcomeOnStartup=false;settings.Current.CheckForUpdates=false;settings.Current.LastSeenVersion=AppInfo.Version;
        using var stop=new CancellationTokenSource();Exception? failure=null;bool done=false;
        DispatcherTimer.RunOnce(()=>
        {
            try
            {
                typeof(MainWindow).Assembly.GetType("Nota.App.NotaThemeService")!.GetMethod("Set",BindingFlags.Public|BindingFlags.Static)!
                    .Invoke(null,[new AppearanceChoice(false,"studio-dark","studio-dark","studio-light")]);
                var roll=new PianoRollView();
                roll.SetNotes([new NotaNote(60,0,2,.55f),new NotaNote(64,2,1,.8f),new NotaNote(67,4,2,.7f),new NotaNote(62,6,1,.6f)],8);
                var data=new Dictionary<int,AutomationPoint[]>
                {
                    [1]=[new(0,.15f),new(2,.7f),new(4,.9f),new(6,.3f),new(8,.5f)],
                    [11]=[new(0,.3f),new(2,.8f),new(4,.6f),new(6,.95f),new(8,.4f)],
                    [64]=[new(0,1),new(1.5,0),new(4,1),new(5.5,0)],
                };
                roll.ControllersInClip=()=>data.Keys.ToArray();roll.GetController=c=>data.GetValueOrDefault(c,[]);
                var window=new Window {Content=roll,Width=1100,Height=850,Background=(IBrush)Avalonia.Application.Current!.Resources["Brush.BgApp"]!};
                window.Show();window.UpdateLayout();
                var add=typeof(PianoRollView).GetMethod("AddControllerLane",Hidden);
                if(add is not null)
                {
                    for(int i=0;i<3;i++)add.Invoke(roll,null);
                    typeof(PianoRollView).GetMethod("ResizeLane",Hidden)!.Invoke(roll,["Velocity",122.0]);
                }
                typeof(PianoRollView).GetField("_pixelsPerBeat",Hidden)!.SetValue(roll,125.0);
                typeof(PianoRollView).GetMethod("SetScrollBeats",Hidden)!.Invoke(roll,[0.0]);
                window.UpdateLayout();
                double pitchY=(double)typeof(PianoRollView).GetMethod("PitchY",Hidden)!.Invoke(roll,[60])!;
                var scroll=(ScrollViewer)typeof(PianoRollView).GetField("_bodyScroll",Hidden)!.GetValue(roll)!;
                scroll.Offset=new Vector(0,Math.Max(0,pitchY-150));window.UpdateLayout();
                using var bitmap=new RenderTargetBitmap(new PixelSize(1100,850));bitmap.Render(window);bitmap.Save(output);
                window.Close();
                Dispatcher.UIThread.Post(()=>
                {
                    typeof(MainWindow).GetField("_closeConfirmed",Hidden)!.SetValue(lifetime.MainWindow,true);
                    lifetime.MainWindow!.Close();done=true;stop.Cancel();
                },DispatcherPriority.Background);
            }
            catch(Exception error){failure=error;stop.Cancel();}
        },TimeSpan.FromMilliseconds(900));
        using var watchdog=new Timer(_=>Dispatcher.UIThread.Post(()=>stop.Cancel()),null,15000,Timeout.Infinite);
        Dispatcher.UIThread.MainLoop(stop.Token);
        if(failure is not null){Console.Error.WriteLine(failure);return 1;}
        if(!done){Console.Error.WriteLine("Capture timed out");return 1;}
        Console.WriteLine(output);return 0;
    }
}
