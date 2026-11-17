using System.Globalization;
using System.Reflection;
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
        var modePoint=mode.TranslatePoint(new Point(mode.Bounds.Width/2,mode.Bounds.Height/2),host)!.Value;
        host.MouseDown(modePoint,MouseButton.Left);host.MouseUp(modePoint,MouseButton.Left);Layout();
        Require(Get("surveyMode") is false,"Mode switch did not enter plan mode.");
        host.MouseDown(modePoint,MouseButton.Left);host.MouseUp(modePoint,MouseButton.Left);Layout();
        Require(Get("surveyMode") is true,"Mode switch did not return to survey mode.");
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
    }
}
