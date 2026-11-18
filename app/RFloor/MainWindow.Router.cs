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
            var center = new Point(N("cx"),N("cy"));
            var radius = N("r");
            EllipseGeometry Circle(double r) => new(new Rect(center.X-r,center.Y-r,2*r,2*r));
            Geometry shape = Circle(radius);
            if (stroke is null)
                shape = new GeometryGroup { FillRule=FillRule.EvenOdd, Children={shape,Circle(radius*.58)} };
            group.Children.Add(new GeometryDrawing {Brush=B("fill"),Pen=stroke is null?null:new Pen(stroke,N("stroke-width")),Geometry=shape});
        }
        var image=new DrawingImage(group);routerImages[key]=image;return image;
    }

    private Point SnapRouterPosition(Point point)
    {
        if (!SnapActive) return point;
        var grid = GridPosition(point);
        var result = grid;
        double range = 8 / (Scale * PlanZoom.ScaleX);
        double closest = range * range;
        foreach (var wall in WallFaces())
        {
            double length = wall.Max(face => ((Vector)(face.B - face.A)).Length);
            foreach (var face in wall)
            {
                Vector axis = face.B - face.A;
                if (axis.Length < length * .5 || face.Depth(point) > range) continue;
                double position = Vectors.Dot(grid - face.A, axis) / axis.SquaredLength;
                if (position < -WallFaceTolerance || position > 1 + WallFaceTolerance) continue;
                double distance = ((Vector)(OnEdge(point, face.A, face.B) - point)).SquaredLength;
                if (distance >= closest) continue;
                var candidate = face.A + axis * position;
                if (wall.Any(test => test.Depth(candidate) < -WallFaceTolerance)) continue;
                result = candidate;
                closest = distance;
            }
        }
        return result;
    }
    private JsonObject RouterAt(Point center)=>new() {["id"]=Id(),["kind"]="router",["name"]=L("Роутер"),["x"]=center.X-RouterSize/Scale/2,["y"]=center.Y-RouterSize/Scale/2,["w"]=RouterSize/Scale,["h"]=RouterSize/Scale};
    private void AddRouter(Point center)
    {
        var obj=RouterAt(center);
        Remember();Decorations.Add(obj);dirty=true;routerPreview=null;RenderPlan();
    }
    private void DrawRouterPreview()
    {
        foreach(var child in Plan.Children.OfType<FrameworkElement>().Where(e=>Equals(e.Tag,"router-preview")).ToList())Plan.Children.Remove(child);
        if(routerPreview is not Point center || surveyMode || tool!="router")return;
        var obj=RouterAt(center);DrawDecoration(obj,new Vector(),Brushes.DarkOrange,SelectionOpacity,"router-preview");
        var label=new Border {Tag="router-preview",IsHitTestVisible=false,Background=Brushes.White,CornerRadius=new CornerRadius(4),Padding=new Thickness(4,2,4,2),Child=new TextBlock {Text=L("X {0:0.##} м · Y {1:0.##} м", Number(obj["x"])+RouterSize/Scale/2, Number(obj["y"])+RouterSize/Scale/2)}};
        Canvas.SetLeft(label,Screen(Number(obj["x"]))+RouterSize+6);Canvas.SetTop(label,Screen(Number(obj["y"])));Ui.SetZIndex(label,160);Plan.Children.Add(label);
    }

    private Geometry? RouterClip(Rect bounds)
    {
        var center=bounds.Center;
        double radius=bounds.Width*373.5/1024;
        List<Point> polygon=[bounds.TopLeft,bounds.TopRight,bounds.BottomRight,bounds.BottomLeft];
        bool clipped=false;
        foreach (var wall in WallFaces())
        {
            WallFace? contact=null;
            double closest=radius*radius;
            foreach (var face in wall)
            {
                if (face.Depth(center)>WallFaceTolerance) continue;
                double distance=((Vector)(OnEdge(center,face.A,face.B)-center)).SquaredLength;
                if (distance>=closest) continue;
                closest=distance;
                contact=face;
            }
            if (contact is not WallFace boundary) continue;

            var vertices=new List<Point>();
            Point previous=polygon[^1];
            foreach (var next in polygon)
            {
                double from=boundary.Depth(previous), to=boundary.Depth(next);
                if ((from>0)!=(to>0)) vertices.Add(previous+(next-previous)*(from/(from-to)));
                if (to<=0) vertices.Add(next);
                previous=next;
            }
            clipped=true;
            polygon=vertices;
            if (polygon.Count<3) return new GeometryGroup();
        }
        if (!clipped) return null;
        Point Local(Point point)=>new((point.X-bounds.X)*Scale,(point.Y-bounds.Y)*Scale);
        var geometry=new StreamGeometry();
        using var path=geometry.Open();
        path.BeginFigure(Local(polygon[0]),true);
        foreach (var point in polygon.Skip(1)) path.LineTo(Local(point));
        path.EndFigure(true);
        return geometry;
    }
}
