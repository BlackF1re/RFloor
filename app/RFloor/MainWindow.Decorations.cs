using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private int gridView;
    private bool drawingFilled=true;
    private MouseButton drawingButton;
    private JsonArray Decorations => Geometry["decorations"] as JsonArray ?? (JsonArray)(Geometry["decorations"]=new JsonArray())!;
    private void ModeSwitch_Click(object sender,RoutedEventArgs e) {SwitchMode(ModeSwitch.IsChecked==true);ModeSwitch.IsChecked=surveyMode;}
    private void GridView_Click(object sender,RoutedEventArgs e) {gridView=(gridView+1)%3;showGrid=gridView!=2;GridViewButton.Content=gridView switch {0=>"▦",1=>"⠿",_=>"□"};RenderPlan();SavePreferences();}
    private void AddDecoration(Point a,Point b)
    {
        Remember();
        var obj=new JsonObject {["id"]=Id(),["filled"]=drawingFilled,["kind"]=tool,["name"]=tool=="text"?L("Новая подпись"):L("Ориентир"),["x"]=Math.Min(a.X,b.X),["y"]=Math.Min(a.Y,b.Y),["w"]=Math.Max(.1,Math.Abs(b.X-a.X)),["h"]=Math.Max(.1,Math.Abs(b.Y-a.Y)),["reverse"]=(b.X-a.X)*(b.Y-a.Y)<0};
        if(tool=="line"){obj["w"]=Math.Abs(b.X-a.X);obj["h"]=Math.Abs(b.Y-a.Y);}
        AlignDecoration(obj);Decorations.Add(obj);dirty=true;
        if(tool=="text"){RenderPlan();BeginInlineRename(new ObjectEntry(L("Подпись"),obj,"decoration"));}
    }
    private Rect DecorationBounds(JsonObject obj)
    {
        double w=Number(obj["w"]),h=Number(obj["h"]);
        if(obj["kind"]?.ToString()=="router")w=h=RouterSize/Scale;
        if(obj["kind"]?.ToString()=="text") {
            var text=new TextBlock {Text=ObjectName(obj,L("Подпись")),FontSize=13,Padding=new Thickness(5,3,5,3)};
            text.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            w=text.DesiredSize.Width/Scale;h=text.DesiredSize.Height/Scale;
        }
        return new Rect(Number(obj["x"]),Number(obj["y"]),w,h);
    }
    private bool DecorationContains(JsonObject obj,Point point)
    {
        var bounds=DecorationBounds(obj);
        if(obj["kind"]?.ToString()=="line") {
            bool reverse=obj["reverse"]?.GetValue<bool>()==true;
            Point a=new(bounds.Left,reverse?bounds.Bottom:bounds.Top),b=new(bounds.Right,reverse?bounds.Top:bounds.Bottom);
            Vector axis=b-a;
            double t=axis.SquaredLength<1e-12?0:Math.Clamp(Vectors.Dot(point-a,axis)/axis.SquaredLength,0,1);
            return ((Vector)(point-(a+axis*t))).Length<8/(Scale*PlanZoom.ScaleX);
        }
        if(obj["kind"]?.ToString()=="ellipse") {
            if(bounds.Width<=0 || bounds.Height<=0)return false;
            double x=(point.X-bounds.X)/bounds.Width*2-1,y=(point.Y-bounds.Y)/bounds.Height*2-1;
            return x*x+y*y<=1;
        }
        return bounds.Contains(point);
    }
    private void DrawDecoration(JsonObject obj,Vector delta,IBrush? accent=null,double opacity=1,string? tag=null)
    {
        var bounds=DecorationBounds(obj);double w=bounds.Width*Scale,h=bounds.Height*Scale;
        var stroke=accent??Brushes.Gray;
        IBrush? fill=accent??new SolidColorBrush(Color.FromRgb(212,212,212));
        if(obj["filled"]?.GetValue<bool>()==false)fill=null;
        FrameworkElement visual=obj["kind"]?.ToString() switch {
            "router"=>new Image {Source=RouterImage(accent),Width=w,Height=h},
            "ellipse"=>new Ellipse {Width=w,Height=h,Stroke=stroke,StrokeThickness=1.5,Fill=fill},
            "line"=>new Line {X1=0,Y1=obj["reverse"]?.GetValue<bool>()==true?h:0,X2=w,Y2=obj["reverse"]?.GetValue<bool>()==true?0:h,Stroke=stroke,StrokeThickness=2},
            "text"=>new Border {CornerRadius=new CornerRadius(6),Background=accent is null?Brushes.White:fill,Padding=new Thickness(5,3,5,3),Child=new TextBlock {Text=ObjectName(obj,L("Подпись")),Foreground=accent??Brushes.Black}},
            _=>new Rectangle {Width=w,Height=h,RadiusX=obj["kind"]?.ToString()=="roundRect"?4:0,RadiusY=obj["kind"]?.ToString()=="roundRect"?4:0,Stroke=stroke,StrokeThickness=1.5,Fill=fill}
        };
        var host=new Canvas {Width=Math.Max(w,1),Height=Math.Max(h,1),IsHitTestVisible=false,Tag=(object?)tag??obj,Opacity=opacity};host.Children.Add(visual);
        Canvas.SetLeft(host,Screen(bounds.X+delta.X));Canvas.SetTop(host,Screen(bounds.Y+delta.Y));Ui.SetZIndex(host,accent is null?90:95);Plan.Children.Add(host);
    }
    private void DrawDecorations()
    {
        foreach(var obj in Decorations.Select(n=>n!.AsObject())) {
            DrawDecoration(obj,new Vector());
            if(!surveyMode)Objects.Items.Add(new ObjectEntry(ObjectName(obj,L("Ориентир")),obj,"decoration"));
        }
    }
}
