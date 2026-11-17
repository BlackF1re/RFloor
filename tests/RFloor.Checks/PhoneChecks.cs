using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RFloor;

internal static class PhoneChecks {
    public static void Run(){
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var view=new MainWindow();var host=new Window {Content=view,Width=412,Height=820};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(view,args);
        object? Get(string name)=>typeof(MainWindow).GetField(name,flags)!.GetValue(view);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Layout(){Dispatcher.UIThread.RunJobs();host.UpdateLayout();}
        Call("AddDocument",Get("document"),null);host.Show();Layout();Call("SwitchMode",false);Layout();
        var plan=view.FindControl<Control>("Plan")!;
        Point At(double x,double y)=>plan.TranslatePoint(new Point(x,y),host)!.Value;
        void Tap(Point p){using var finger=host.TouchBegin(p);host.TouchEnd(finger,p);Layout();}
        var document=(JsonObject)Get("document")!;
        Call("SetTool","tape");Tap(At(100,100));Require(Get("tapeStart") is not null,"Touch did not set tape origin.");
        Tap(At(220,180));Require(document["geometry"]!["tapes"]!.AsArray().Count==1,"Touch did not finish tape.");
        Call("SetTool","rect");using(var finger=host.TouchBegin(At(100,250))){host.TouchMove(finger,At(220,320));host.TouchEnd(finger,At(220,320));}Layout();
        Require(document["geometry"]!["decorations"]!.AsArray().Count==1,"Phone could not draw a primitive.");
        var zoom=(ScaleTransform)view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform!;double before=zoom.ScaleX;
        using(var first=host.TouchBegin(At(100,100)))using(var second=host.TouchBegin(At(200,100))){host.TouchMove(second,At(270,100));host.TouchEnd(second,At(270,100));host.TouchEnd(first,At(100,100));}Layout();
        Require(zoom.ScaleX>before,"Two-finger pinch did not zoom.");
        Require(document["geometry"]!["decorations"]!.AsArray().Count==1,"Pinch created an accidental primitive.");
        Call("ShowPreferences",0);Layout();
        var tabs=view.GetVisualDescendants().OfType<TabControl>().Single(t=>t.Classes.Contains("settingsTabs"));
        var dialog=(Control)tabs.GetVisualAncestors().First(c=>c.GetType().Name=="OverlayDialog");
        foreach(int index in new[]{0,1,2,3}){tabs.SelectedIndex=index;Layout();Require(dialog.Bounds.Width<=host.Bounds.Width-24 && dialog.Bounds.Height<host.Bounds.Height,"Phone settings exceed the screen.");}
        host.Width=360;Layout();Require(dialog.Width<=312,"Open settings did not adapt to a narrower screen.");
        Require(view.HandleBack(),"Phone Back did not close settings.");Layout();
        host.Width=412;Layout();Call("ToggleDrawer",view.FindControl<Control>("RightSidebar"));Layout();host.Width=300;Layout();
        Require(view.FindControl<Control>("RightSidebar")!.Width<=268,"Open drawer did not adapt to rotation or split screen.");
        view.HandleBack();host.Width=1440;host.Height=900;Layout();
        var dock=view.FindControl<DockPanel>("CanvasDock")!;
        Require(dock.Bounds.Height==28,"Desktop dock did not restore its height.");
        foreach(var button in dock.GetVisualDescendants().OfType<Button>())Require(button.MinHeight<40,"Desktop retained phone button sizes.");
        Require(((StackPanel)dock.Children[1]).Children.OfType<Button>().First().Content?.ToString()!="⛶","Desktop retained phone fit icon.");
        host.Close();Console.WriteLine("Phone checks passed: touch drawing, persistent tape, pinch without accidental objects, settings tabs, split-screen drawers and desktop restoration.");
    }
}
