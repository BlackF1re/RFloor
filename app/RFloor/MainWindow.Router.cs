using System.Globalization;
using System.Text.Json.Nodes;



using System.Xml.Linq;

namespace RFloor;
public partial class MainWindow
{
    // SVG dot diameter is 104/1024 of its view box; measurement dots are 2.5 DIP.
    private const double RouterSize=2.5*1024/104;
    private Point? routerPreview;
    private static readonly Dictionary<string,DrawingImage> routerImages=new();
    public DrawingImage RouterToolbarImage => RouterImage();
    private static DrawingImage RouterImage(IBrush? tint=null)
    {
        string key=tint?.ToString()??"original";
        if(routerImages.TryGetValue(key,out var cached))return cached;
        using var stream=AssetLoader.Open(new Uri("avares://RFloor/Assets/rfloor_waves_symbol.svg"));
        var svg=XDocument.Load(stream);var group=new DrawingGroup();
        group.Children.Add(new GeometryDrawing {Brush=Brushes.Transparent,Geometry=new RectangleGeometry(new Rect(0,0,1024,1024))});
        foreach(var circle in svg.Descendants().Where(n=>n.Name.LocalName=="circle")) {
            double N(string name)=>double.Parse(circle.Attribute(name)!.Value,CultureInfo.InvariantCulture);
            IBrush? B(string name)=>circle.Attribute(name) is {} attribute && attribute.Value!="none" ? tint??new SolidColorBrush(Color.Parse(attribute.Value)):null;
            var stroke=B("stroke");
            group.Children.Add(new GeometryDrawing {Brush=B("fill"),Pen=stroke is null?null:new Pen(stroke,N("stroke-width")),Geometry=new EllipseGeometry(new Rect(N("cx")-N("r"),N("cy")-N("r"),2*N("r"),2*N("r")))});
        }
        var image=new DrawingImage(group);routerImages[key]=image;return image;
    }
    private JsonObject RouterAt(Point center)=>new() {["id"]=Id(),["kind"]="router",["name"]=L("Роутер"),["x"]=center.X-RouterSize/Scale/2,["y"]=center.Y-RouterSize/Scale/2,["w"]=RouterSize/Scale,["h"]=RouterSize/Scale};
    private void AddRouter(Point center)
    {
        var obj=RouterAt(center);AlignDecoration(obj);
        Remember();Decorations.Add(obj);dirty=true;routerPreview=null;RenderPlan();
    }
    private void DrawRouterPreview()
    {
        foreach(var child in Plan.Children.OfType<FrameworkElement>().Where(e=>Equals(e.Tag,"router-preview")).ToList())Plan.Children.Remove(child);
        if(routerPreview is not Point center || surveyMode || tool!="router")return;
        var obj=RouterAt(center);AlignDecoration(obj);DrawDecoration(obj,new Vector(),Brushes.DarkOrange,SelectionOpacity,"router-preview");
        var label=new Border {Tag="router-preview",IsHitTestVisible=false,Background=Brushes.White,CornerRadius=new CornerRadius(4),Padding=new Thickness(4,2,4,2),Child=new TextBlock {Text=L("X {0:0.##} м · Y {1:0.##} м", Number(obj["x"])+RouterSize/Scale/2, Number(obj["y"])+RouterSize/Scale/2)}};
        Canvas.SetLeft(label,Screen(Number(obj["x"]))+RouterSize+6);Canvas.SetTop(label,Screen(Number(obj["y"])));Ui.SetZIndex(label,160);Plan.Children.Add(label);
    }
}
