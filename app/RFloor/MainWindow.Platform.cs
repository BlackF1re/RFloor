using System.Runtime.CompilerServices;
namespace RFloor;
internal static class GeometryOps {
    internal static Geometry Combine(Geometry a,Geometry b,GeometryCombineMode mode,Transform? transform)=>new CombinedGeometry(mode,a,b,transform);
    internal static bool IntersectionEmpty(Geometry a,Geometry b) {var bounds=Combine(a,b,GeometryCombineMode.Intersect,null).Bounds;return bounds.Width<.001 || bounds.Height<.001;}
}
public partial class MainWindow {
    private static readonly ConditionalWeakTable<Geometry,List<Point>> contours=new();
    private static Geometry RegisterContour(Geometry shape,IEnumerable<Point> points){contours.Add(shape,points.ToList());return shape;}
    private static Geometry SurfaceLine(Point a,Point b,double width){Vector d=b-a; if(d.Length<1e-9)return new GeometryGroup();Vector n=new(-d.Y/d.Length*width/2,d.X/d.Length*width/2);return RegisterContour(new LineGeometry(a,b).GetWidenedGeometry(new Pen(Brushes.Black,width)),[a+n,b+n,b-n,a-n]);}
    private static IReadOnlyList<Point> Contour(Geometry shape){
        if(contours.TryGetValue(shape,out var points))return points;
        Rect r=shape.Bounds;
        if(shape is EllipseGeometry)return Enumerable.Range(0,96).Select(i=>new Point(r.Center.X+Math.Cos(i*Math.PI/48)*r.Width/2,r.Center.Y+Math.Sin(i*Math.PI/48)*r.Height/2)).ToList();
        if(shape is RectangleGeometry rectangle && rectangle.RadiusX>0){var result=new List<Point>();double rx=Math.Min(rectangle.RadiusX,r.Width/2),ry=Math.Min(rectangle.RadiusY,r.Height/2);for(int corner=0;corner<4;corner++){Point c=corner switch {0=>new(r.Right-rx,r.Bottom-ry),1=>new(r.Left+rx,r.Bottom-ry),2=>new(r.Left+rx,r.Top+ry),_=>new(r.Right-rx,r.Top+ry)};for(int i=0;i<=12;i++){double angle=(corner+i/12.0)*Math.PI/2;result.Add(new(c.X+Math.Cos(angle)*rx,c.Y+Math.Sin(angle)*ry));}}return result;}
        return [r.TopLeft,r.TopRight,r.BottomRight,r.BottomLeft];
    }
    private readonly Dictionary<OverlayDialog,Control> overlays=new();
    private readonly Dictionary<OverlayDialog,Size> overlaySizes=new();
    private Avalonia.Controls.Platform.IInputPane? overlayInputPane;
    private void OverlayKeyboardChanged(object? sender,Avalonia.Controls.Platform.InputPaneStateEventArgs e) => RefreshOverlayBounds(e.EndRect);
    private void RefreshOverlayBounds(Rect? keyboard=null){
        double available=Bounds.Height;
        if((keyboard ?? overlayInputPane?.OccludedRect) is Rect occluded && occluded.Height>0 &&
            TopLevel.GetTopLevel(this)?.TranslatePoint(occluded.TopLeft,this) is Point top && top.Y>0)
            available=Math.Min(available,top.Y);
        foreach(var (dialog,shade) in overlays){
            var desired=overlaySizes[dialog];
            dialog.MaxWidth=Math.Max(160,Math.Min(780,Bounds.Width-48));
            dialog.MaxHeight=Math.Max(80,available-72);
            if(!double.IsNaN(desired.Width))dialog.Width=Math.Min(desired.Width,dialog.MaxWidth);
            if(!double.IsNaN(desired.Height))dialog.Height=Math.Min(desired.Height,dialog.MaxHeight);
            if(shade is Border {Child:Border card} overlay) {
                overlay.Padding=new Thickness(12,12,12,12+Math.Max(0,Bounds.Height-available));
                card.MaxHeight=Math.Max(50,available-24);
            }
        }
    }
    internal void ShowOverlay(OverlayDialog dialog,string title){
        var root=(Grid)Content!;dialog.MinWidth=0;dialog.MinHeight=0;
        overlaySizes.Add(dialog,new Size(dialog.Width,dialog.Height));
        dialog.KeyDown+=(_,e)=>{if(e.Key==Key.Escape){dialog.Cancel();e.Handled=true;}};
        if(overlays.Count==0 && TopLevel.GetTopLevel(this)?.InputPane is {} inputPane) {
            overlayInputPane=inputPane;inputPane.StateChanged+=OverlayKeyboardChanged;
        }
        var header=new DockPanel {Margin=new Thickness(16,8)};
        if(dialog.ShowCloseButton) {
            var close=new Button {Content="×",MinWidth=44,MinHeight=44};
            close.Click+=(_,_)=>dialog.Cancel();DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
        }
        header.Children.Add(new TextBlock {Text=title,FontSize=20,VerticalAlignment=VerticalAlignment.Center});
        var panel=new DockPanel();DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);panel.Children.Add(dialog);
        var shade=new Border {Background=new SolidColorBrush(Color.FromArgb(110,0,0,0)),ZIndex=1000+overlays.Count,Padding=new Thickness(12),Child=new Border {Background=Brushes.White,CornerRadius=new CornerRadius(12),MaxWidth=800,MaxHeight=Math.Max(300,Bounds.Height-24),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,Child=panel}};
        overlays.Add(dialog,shade);RefreshOverlayBounds();root.Children.Add(shade);
        foreach(var cancel in dialog.GetVisualDescendants().OfType<Avalonia.Controls.Button>().Where(b=>b.IsCancel))cancel.Click+=(_,_)=>dialog.Cancel();
        dialog.Focus();
    }
    internal void CloseOverlay(OverlayDialog dialog){
        overlaySizes.Remove(dialog);
        if(overlays.Remove(dialog,out var shade))((Grid)Content!).Children.Remove(shade);
        if(overlays.Count==0 && overlayInputPane is {} inputPane) {
            inputPane.StateChanged-=OverlayKeyboardChanged;overlayInputPane=null;
        }
    }
}
