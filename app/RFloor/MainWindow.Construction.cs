using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private (JsonObject Wall, Point A, Point B)? DrawnOpening(Point start,Point end)
    {
        var wall=Walls.Select(n=>n!.AsObject()).OrderBy(w=>SegmentDistance(start,w).Distance).FirstOrDefault();
        if(wall is null || SegmentDistance(start,wall).Distance>.25)return null;
        Point a=End(wall,"a"),b=End(wall,"b");Vector axis=b-a;double length=axis.Length;
        if(length<.31)return null;
        double first=Math.Clamp(SegmentDistance(start,wall).T,.08/length,1-.08/length);
        double last=Math.Clamp(SegmentDistance(end,wall).T,.08/length,1-.08/length);
        return (wall,a+axis*Math.Min(first,last),a+axis*Math.Max(first,last));
    }
    private void CutDrawnOpening(Point start,Point end)
    {
        var span=DrawnOpening(start,end);
        if(span is not {} cut || ((Vector)(cut.B-cut.A)).Length<.15){Status.Text=L("Протяните проём вдоль одного участка стены (не менее 0,15 м).");return;}
        Remember();
        var first=cut.Wall.DeepClone().AsObject();var second=cut.Wall.DeepClone().AsObject();
        first["id"]=Id();first["b"]=Point(cut.A.X,cut.A.Y);
        second["id"]=Id();second["a"]=Point(cut.B.X,cut.B.Y);
        var opening=new JsonObject {["id"]=Id(),["name"]=L("Проём"),["a"]=Point(cut.A.X,cut.A.Y),["b"]=Point(cut.B.X,cut.B.Y)};
        if(cut.Wall["roomId"] is {} roomId)opening["roomId"]=roomId.DeepClone();
        Walls.Remove(cut.Wall);Walls.Add(first);Walls.Add(second);Doors.Add(opening);dirty=true;Status.Text=L("Проём добавлен.");
    }
    private void DrawConstruction(Point start,Point end)
    {
        if(tool is "wall" or "line")(start,end)=AlignConstructionLine(start,end,tool=="wall"?WallStroke:2);
        if(tool is "rect" or "roundRect" or "ellipse" or "line" or "text") {
            var obj=new JsonObject {["kind"]=tool,["filled"]=drawingFilled,["name"]=L("Подпись"),["x"]=Math.Min(start.X,end.X),["y"]=Math.Min(start.Y,end.Y),["w"]=Math.Abs(end.X-start.X),["h"]=Math.Abs(end.Y-start.Y),["reverse"]=(end.X-start.X)*(end.Y-start.Y)<0};
            AlignDecoration(obj);DrawDecoration(obj,new Vector(),Brushes.DarkOrange,SelectionOpacity);
            DrawConstructionParameters(start,end);return;
        }
        if(tool=="room") {
            var rect=new Rectangle {Width=Math.Abs(end.X-start.X)*Scale+2*WallStroke,Height=Math.Abs(end.Y-start.Y)*Scale+2*WallStroke,Stroke=Brushes.DarkOrange,StrokeThickness=WallStroke,Opacity=SelectionOpacity,IsHitTestVisible=false};
            Avalonia.Controls.Canvas.SetLeft(rect,Screen(Math.Min(start.X,end.X))-WallStroke);Avalonia.Controls.Canvas.SetTop(rect,Screen(Math.Min(start.Y,end.Y))-WallStroke);Plan.Children.Add(rect);
        }
        else {
            if(tool=="door" && DrawnOpening(start,end) is {} span) {
                start=span.A;end=span.B;
                var opening=new JsonObject {["a"]=Point(start.X,start.Y),["b"]=Point(end.X,end.Y),["roomId"]=span.Wall["roomId"]?.DeepClone()};
                Plan.Children.Add(new Avalonia.Controls.Shapes.Path {Data=OpeningGeometry(opening),Fill=Brushes.DarkOrange,Opacity=SelectionOpacity,IsHitTestVisible=false});
            } else Plan.Children.Add(new Line {X1=Screen(start.X),Y1=Screen(start.Y),X2=Screen(end.X),Y2=Screen(end.Y),Stroke=Brushes.DarkOrange,StrokeThickness=tool=="door"?OpeningStroke:WallStroke,Opacity=SelectionOpacity,IsHitTestVisible=false});
        }
        DrawConstructionParameters(start,end);
    }
    private void DrawConstructionParameters(Point start,Point end)
    {
        if(tool is "room" or "rect" or "roundRect" or "ellipse") {
            double w=Math.Abs(end.X-start.X),h=Math.Abs(end.Y-start.Y);
            string text=tool=="ellipse"?L("Ø {0:0.##} × {1:0.##} м · {2:0.##} м²", w, h, Math.PI*w*h/4):L("{0:0.##} м × {1:0.##} м = {2:0.##} м²", w, h, w*h);
            var label=new Avalonia.Controls.Border {CornerRadius=new CornerRadius(6),Background=Brushes.White,Padding=new Thickness(6,3,6,3),IsHitTestVisible=false,Child=new Avalonia.Controls.TextBlock {Text=text}};
            label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            Avalonia.Controls.Canvas.SetLeft(label,Screen((start.X+end.X)/2)-label.DesiredSize.Width/2);Avalonia.Controls.Canvas.SetTop(label,Screen((start.Y+end.Y)/2)-label.DesiredSize.Height/2);Ui.SetZIndex(label,160);Plan.Children.Add(label);
        } else AddLabel(tool=="text"?L("X {0:0.##} м · Y {1:0.##} м", start.X, start.Y):L("{0:0.##} м", ((Vector)(end-start)).Length),Screen(end.X)+8,Screen(end.Y)+8);
    }
}
