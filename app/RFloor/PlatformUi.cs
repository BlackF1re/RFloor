namespace RFloor;

public class SurfaceWindow : UserControl
{
    protected new bool IsInitialized {get;set;}
    protected string Title {get=>TopLevel.GetTopLevel(this) is Window w?w.Title??"RFloor":"RFloor";set{if(TopLevel.GetTopLevel(this) is Window w)w.Title=value;}}
    protected object? FindName(string name)=>this.FindControl<Control>(name);
}
public enum PointerButton {Left,Right,Middle}
internal static class PointerContext
{
    internal static IPointer? Pointer;
    internal static KeyModifiers Modifiers;
    internal static Point LastPosition;
}
internal static class Ui
{
    internal static void SetZIndex(Control control,int index)=>control.ZIndex=index;
    internal static void CapturePointer(this Control control)=>PointerContext.Pointer?.Capture(control);
    internal static void ReleasePointer(this Control control){if(control.IsPointerCaptured())PointerContext.Pointer?.Capture(null);}
    internal static bool IsPointerCaptured(this Control control)=>PointerContext.Pointer?.Captured==control;
    internal static PointerButton Button(this PointerEventArgs e)=>e.GetCurrentPoint(null).Properties.PointerUpdateKind switch {
        PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased=>PointerButton.Right,
        PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased=>PointerButton.Middle,_=>PointerButton.Left};
    internal static bool MiddlePressed(this PointerEventArgs e)=>e.GetCurrentPoint(null).Properties.IsMiddleButtonPressed;
    internal static int Clicks(this PointerEventArgs e)=>e is PointerPressedEventArgs p?p.ClickCount:1;
}
internal static class Vectors {internal static double Dot(Vector a,Vector b)=>a.X*b.X+a.Y*b.Y;internal static Vector WithX(this Vector v,double x)=>new(x,v.Y);internal static Vector WithY(this Vector v,double y)=>new(v.X,y);}
internal static class Rects {internal static Rect Between(Point a,Point b)=>new(Math.Min(a.X,b.X),Math.Min(a.Y,b.Y),Math.Abs(a.X-b.X),Math.Abs(a.Y-b.Y));}

// Non-blocking in-place dialogs work with both desktop windows and Android's single view.
internal sealed class OverlayDialog(MainWindow owner) : UserControl
{
    private readonly TaskCompletionSource<bool> completion=new();
    internal string Title {get;set;}="RFloor";
    internal SizeToContent SizeToContent {get;set;}
    internal bool ShowCloseButton {get;init;}=true;
    internal void Complete(bool accepted){if(!completion.Task.IsCompleted){owner.CloseOverlay(this);completion.TrySetResult(accepted);}}
    internal Task<bool> ShowAsync(){owner.ShowOverlay(this,Title);return completion.Task;}
    internal void Cancel()=>Complete(false);

    internal static Grid Actions(Button? cancel,Button confirm,Button? neutral=null)
    {
        var row=new Grid {ColumnDefinitions=new ColumnDefinitions(neutral is null?"Auto,*,Auto":"*,Auto,*"),
            ColumnSpacing=6,Margin=new Thickness(0,12,0,0)};
        void Place(Button button,int column,HorizontalAlignment alignment)
        {
            button.MinHeight=44;
            button.Padding=new Thickness(8,6);
            button.Margin=new Thickness(0);
            button.HorizontalAlignment=alignment;
            Grid.SetColumn(button,column);
            row.Children.Add(button);
        }
        if(cancel is not null)Place(cancel,0,HorizontalAlignment.Left);
        if(neutral is not null)Place(neutral,1,HorizontalAlignment.Center);
        confirm.Classes.Add("dialogAccent");
        Place(confirm,2,HorizontalAlignment.Right);
        return row;
    }
}
internal enum MessageBoxButton {OK,YesNoCancel}
internal enum MessageBoxImage {Error,Warning,Question}
internal enum MessageBoxResult {OK,Yes,No,Cancel}
internal static class MessageBox
{
    internal static async void Show(MainWindow owner,string text,string title="RFloor",MessageBoxButton buttons=MessageBoxButton.OK,MessageBoxImage image=MessageBoxImage.Warning)=>await Ask(owner,text,title,buttons,image);
    internal static async Task<MessageBoxResult> Ask(MainWindow owner,string text,string title,MessageBoxButton buttons,MessageBoxImage image)
    {
        MessageBoxResult result=MessageBoxResult.Cancel;
        bool save=buttons==MessageBoxButton.YesNoCancel;
        var dialog=new OverlayDialog(owner) {Title=title,Width=save?380:360};
        Button Choice(string label,MessageBoxResult value)
        {
            var button=new Button {Content=Localization.L(label)};
            button.Click+=(_,_)=>{result=value;dialog.Complete(value!=MessageBoxResult.Cancel);};
            return button;
        }
        var cancel=save?Choice("Отмена",MessageBoxResult.Cancel):null;
        if(cancel is not null)cancel.IsCancel=true;
        var confirm=save?Choice("Сохранить",MessageBoxResult.Yes):Choice("OK",MessageBoxResult.OK);
        confirm.IsDefault=true;
        var actions=OverlayDialog.Actions(cancel,confirm,
            save?Choice("Не сохранять",MessageBoxResult.No):null);
        DockPanel.SetDock(actions,Dock.Bottom);
        dialog.Content=new DockPanel {Margin=new Thickness(20,4,20,20),MaxWidth=480,
            Children={actions,new ScrollViewer {Content=new TextBlock {Text=text,TextWrapping=TextWrapping.Wrap},
                VerticalScrollBarVisibility=ScrollBarVisibility.Auto}}};
        await dialog.ShowAsync();
        return result;
    }
}
