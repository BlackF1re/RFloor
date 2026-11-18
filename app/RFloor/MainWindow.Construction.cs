using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private readonly List<Control> constructionPreview=[];

    private void UpdateEmptyPlan() =>
        EmptyPlan.IsVisible = drawingStart is null && Rooms.Count == 0 && Walls.Count == 0 &&
            Decorations.Count == 0 && Tapes.Count == 0;

    private void ClearConstructionPreview()
    {
        foreach(var control in constructionPreview)Plan.Children.Remove(control);
        constructionPreview.Clear();
        UpdateEmptyPlan();
    }

    private void DrawConstructionPreview()
    {
        ClearConstructionPreview();
        if(drawingStart is not Point start || drawingEnd is not Point end)return;
        int first=Plan.Children.Count;
        DrawConstruction(start,end);
        constructionPreview.AddRange(Plan.Children.Skip(first));
    }

    private IEnumerable<double> WallJunctions(Point start, Point end)
    {
        Vector axis=end-start;
        foreach (var wall in Walls.OfType<JsonObject>())
        {
            Point a=End(wall,"a");
            Vector other=End(wall,"b")-a;
            double cross=Cross(axis,other);
            if(Math.Abs(cross)<1e-8)continue;
            double t=Cross(a-start,other)/cross;
            double u=Cross(a-start,axis)/cross;
            if(t>1e-6 && t<1-1e-6 && u>=-1e-6 && u<=1+1e-6)yield return t;
        }
    }

    private (double Before,double After) OpeningBay(JsonObject wall,double at)
    {
        double before=0,after=1;
        foreach(double joint in WallJunctions(End(wall,"a"),End(wall,"b")))
        {
            if(joint<=at)before=Math.Max(before,joint);
            else after=Math.Min(after,joint);
        }
        return (before,after);
    }

    private (JsonObject Wall, Point A, Point B)? DrawnOpening(Point start,Point end)
    {
        var wall=Walls.Select(n=>n!.AsObject()).OrderBy(w=>SegmentDistance(start,w).Distance).FirstOrDefault();
        if(wall is null || SegmentDistance(start,wall).Distance>.25)return null;
        Point a=End(wall,"a"),b=End(wall,"b");Vector axis=b-a;double length=axis.Length;
        double from=SegmentDistance(start,wall).T;
        var (before,after)=OpeningBay(wall,from);
        if((after-before)*length<.31)return null;
        double first=Math.Clamp(from,before+.08/length,after-.08/length);
        double last=Math.Clamp(SegmentDistance(end,wall).T,before+.08/length,after-.08/length);
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
            (start,end)=SnapRoomCorners(start,end);
            var bounds=Rects.Between(start,end);
            var room=new JsonObject {["x"]=bounds.X,["y"]=bounds.Y,["w"]=bounds.Width,["h"]=bounds.Height};
            foreach (var (a,b) in RoomEdges(room))
            {
                Vector axis=b-a;
                if (axis.Length<1e-8) continue;
                Vector outward=new Vector(axis.Y,-axis.X)/axis.Length;
                foreach (var (from,to) in UncoveredSegments(a,b))
                    DrawSegmentHighlight(RoomWallGeometry(from,to,room,outward),default,SelectionOpacity,Brushes.DarkOrange,null);
            }
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
