using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RFloor;

internal static class LayoutChecks {
    public static void Run() {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var view=new MainWindow();var host=new Window {Width=1440,Height=900,Content=view};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(view,args);
        object? Get(string name)=>typeof(MainWindow).GetField(name,flags)!.GetValue(view);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Layout(){Dispatcher.UIThread.RunJobs();host.UpdateLayout();}
        Call("AddDocument",Get("document"),null);host.Show();Layout();
        var mode=view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("ModeSwitch")!;
        var surveyLabel=view.FindControl<TextBlock>("ModeSurveyText")!;
        var planLabel=view.FindControl<TextBlock>("ModePlanText")!;
        Require(Grid.GetColumn(surveyLabel)<Grid.GetColumn(planLabel),"Survey mode is not on the left.");
        Require(Get("surveyMode") is false,"New plan selected the wrong mode.");
        var modePoint=mode.TranslatePoint(new Point(mode.Bounds.Width/2,mode.Bounds.Height/2),host)!.Value;
        host.MouseDown(modePoint,MouseButton.Left);host.MouseUp(modePoint,MouseButton.Left);Layout();
        Require(Get("surveyMode") is true,"Mode switch did not enter survey mode.");
        host.MouseDown(modePoint,MouseButton.Left);host.MouseUp(modePoint,MouseButton.Left);Layout();
        Require(Get("surveyMode") is false,"Mode switch did not return to plan mode.");
        var search=view.FindControl<TextBox>("ObjectSearch")!;
        var clear=view.FindControl<Button>("ClearObjectSearch")!;
        Require(search.Bounds.Height==clear.Bounds.Height && clear.Bounds.Width==clear.Bounds.Height,
            "Search clear button is not square and aligned to the field.");
        var viewport=view.FindControl<ScrollViewer>("Viewport")!;
        var zoom=(ScaleTransform)view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform!;
        var cursor=viewport.TranslatePoint(new Point(200,200),host)!.Value;
        viewport.Offset=new Vector(100,150);Layout();double scale=zoom.ScaleX;
        host.MouseWheel(cursor,new Vector(0,-1));Layout();
        Require(viewport.Offset.Y>150 && zoom.ScaleX==scale,"Wheel did not scroll vertically without zooming.");
        var offset=viewport.Offset;
        host.MouseWheel(cursor,new Vector(0,-1),RawInputModifiers.Shift);Layout();
        Require(viewport.Offset.X>offset.X && viewport.Offset.Y==offset.Y && zoom.ScaleX==scale,"Shift+wheel did not scroll horizontally.");
        offset=viewport.Offset;var anchor=(offset+new Vector(200,200))/scale;
        host.MouseWheel(cursor,new Vector(0,1),RawInputModifiers.Control);Layout();
        Require(zoom.ScaleX>scale,"Ctrl+wheel was swallowed by scrolling.");
        var newAnchor=(viewport.Offset+new Vector(200,200))/zoom.ScaleX;
        Require((anchor-newAnchor).Length<1,"Zoom moved the point under the cursor.");
        var culture=CultureInfo.CurrentCulture;
        try {foreach(string name in new[]{"ru-RU","en-US"}) {CultureInfo.CurrentCulture=new CultureInfo(name);view.FindControl<ComboBox>("GridPicker")!.SelectedIndex=0;Call("RenderPlan");}}
        finally {CultureInfo.CurrentCulture=culture;}
        Call("ShowPreferences",0);Layout();
        var tabs=view.GetVisualDescendants().OfType<TabControl>().Single(t=>t.Classes.Contains("settingsTabs"));
        var dialog=(Control)tabs.GetVisualAncestors().First(c=>c.GetType().Name=="OverlayDialog");
        var size=dialog.Bounds.Size;
        foreach(var slider in dialog.GetVisualDescendants().OfType<Slider>()) {
            Require(slider.Bounds.Width>100 && slider.Bounds.Height>=24,"Slider has collapsed bounds.");
            Require(slider.GetVisualDescendants().OfType<Avalonia.Controls.RepeatButton>().All(b=>b.Padding.Left<10),"Ordinary button padding leaked into slider tracks.");
        }
        var signal=view.FindControl<CheckBox>("RunSignal")!;var originalSignal=signal.IsChecked;
        signal.IsChecked=!originalSignal;
        var ping=view.FindControl<TextBox>("PingCount")!;var originalPing=ping.Text;ping.Text="10";
        var checks=dialog.GetVisualDescendants().OfType<CheckBox>().Where(c=>c.IsEffectivelyVisible).ToArray();
        Require(checks.Length>=3,"Settings lost checkboxes.");
        Require(checks.All(c=>c.GetVisualDescendants().OfType<Border>().Any()),"Checkbox templates are missing.");
        foreach(int index in new[]{1,2,3,0}) {tabs.SelectedIndex=index;Layout();Require(dialog.Bounds.Size==size,"Settings changed size when switching tabs.");}
        var buttons=dialog.GetVisualDescendants().OfType<Button>().Where(b=>b.IsDefault||b.IsCancel).ToArray();
        Require(buttons.Length==2,"Settings action buttons are missing.");
        var apply=buttons.Single(b=>b.IsDefault);var cancel=buttons.Single(b=>b.IsCancel);
        Require(apply.TranslatePoint(default,dialog)!.Value.X>cancel.TranslatePoint(default,dialog)!.Value.X,"Apply must be the rightmost action.");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        Require(signal.IsChecked==originalSignal && ping.Text==originalPing,"Cancel did not roll back measurement settings.");
        Require(!view.GetVisualDescendants().Any(c=>c.GetType().Name=="OverlayDialog"),"Cancel did not close settings.");
        Require(view.FindControl<Control>("MeasurementSettingsPanel")!.Parent is Panel,"Cancel did not restore measurement settings.");
        host.Close();Console.WriteLine("Layout checks passed: wheel modifiers, cursor anchored zoom, decimal grid cultures, checkbox templates, fixed settings tabs and Cancel.");
        CheckCanvasInput();
    }

    private static void CheckCanvasInput()
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var view=new MainWindow();
        var host=new Window {Width=1440,Height=900,Content=view};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(view,args);
        void Set(string name,object value)=>typeof(MainWindow).GetField(name,flags)!.SetValue(view,value);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Layout(){Dispatcher.UIThread.RunJobs();host.UpdateLayout();}
        var sample=JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sample.rfplan")))!.AsObject();
        Call("AddDocument",sample,null);
        Call("SwitchMode",false);
        Set("rulerMode",2);
        host.Show();Layout();

        var plan=view.FindControl<Canvas>("Plan")!;
        Point At(double x,double y)=>plan.TranslatePoint(new Point(x,y),host)!.Value;
        host.MouseMove(At(180,180));Layout();
        var ruler=view.FindControl<Canvas>("RulerTop")!;
        var projection=view.FindControl<Canvas>("ProjectionOverlay")!;
        var button=view.FindControl<Button>("RulerModeButton")!;
        var marks=ruler.Children.OfType<Canvas>().Single();
        var ticks=marks.Children.ToArray();
        var icon=button.Content;
        var lines=projection.Children.OfType<Avalonia.Controls.Shapes.Line>().ToArray();
        Require(lines.Length==2 && lines.All(line=>line.IsVisible),"Cursor projection did not appear.");
        double originalX=((TranslateTransform)lines[0].RenderTransform!).X;
        host.MouseMove(At(210,190));Layout();
        Require(ticks.Length>0 && marks.Children.SequenceEqual(ticks) && ReferenceEquals(icon,button.Content) &&
            projection.Children.SequenceEqual(lines),"Pointer movement rebuilt rulers.");
        Require(Math.Abs(((TranslateTransform)lines[0].RenderTransform!).X-originalX-30)<1,"Projection did not follow the pointer.");
        var viewport=view.FindControl<ScrollViewer>("Viewport")!;
        var tickPosition=((TranslateTransform)marks.RenderTransform!).X;
        viewport.Offset=new Vector(2,0);Layout();
        Require(marks.Children.SequenceEqual(ticks) && Math.Abs(((TranslateTransform)marks.RenderTransform!).X-tickPosition+2)<1,
            "Small pan did not translate existing ruler marks.");
        viewport.Offset=default;Layout();

        Call("SetTool","rect");Layout();
        var permanent=plan.Children[0];
        var objects=view.FindControl<ListBox>("Objects")!;
        var firstObject=objects.Items[0];
        var from=At(470,270);
        var to=At(560,355);
        host.MouseDown(from,MouseButton.Left);
        int childrenAtPress=plan.Children.Count;
        host.MouseMove(to,RawInputModifiers.LeftMouseButton);Layout();
        Require(ReferenceEquals(permanent,plan.Children[0]) && ReferenceEquals(firstObject,objects.Items[0]) &&
            plan.Children.Count==childrenAtPress,"Construction preview rebuilt the plan or accumulated controls.");
        int decorations=sample["geometry"]!["decorations"]!.AsArray().Count;
        host.MouseUp(to,MouseButton.Left);Layout();
        Require(sample["geometry"]!["decorations"]!.AsArray().Count==decorations+1,"Construction did not commit on release.");

        Call("SetTool","tape");Layout();
        Call("TapeClick",new Point(1,1));Call("TapeClick",new Point(2,1));Call("TapeClick",new Point(2,2));
        var tapes=plan.Children.OfType<Control>().Where(control=>Equals(control.Tag,"tape-overlay")).ToArray();
        host.MouseMove(At(350,300));Layout();
        Require(tapes.Length>=4 && plan.Children.OfType<Control>().Where(control=>Equals(control.Tag,"tape-overlay")).SequenceEqual(tapes),
            "Moving a pending tape rebuilt completed tapes or replaced its preview.");
        host.Close();
        Console.WriteLine("Canvas input checks passed: persistent rulers, scrolling, cursor projection, and isolated construction and tape previews.");
    }
}
