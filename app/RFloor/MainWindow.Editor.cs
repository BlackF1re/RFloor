using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private Point? tapeStart, tapeCursor;
    private static Point End(JsonObject segment, string end) => new(Number(segment[end]!["x"]), Number(segment[end]!["y"]));
    private static double Cross(Vector a, Vector b) => a.X*b.Y-a.Y*b.X;
    // Returns the interval of an existing collinear segment on the proposed segment.
    private static (double Start, double End)? OverlapInterval(Point a, Point b, Point c, Point d)
    {
        Vector axis = b-a; double length = axis.Length;
        if (length < 1e-8) return null;
        axis=axis/axis.Length;
        if (Math.Abs(Cross(axis,c-a)) > 1e-5 || Math.Abs(Cross(axis,d-a)) > 1e-5) return null;
        double x = Vectors.Dot(c-a,axis), y = Vectors.Dot(d-a,axis);
        double start = Math.Max(0,Math.Min(x,y)), end = Math.Min(length,Math.Max(x,y));
        return end-start > 1e-5 ? (start,end) : null;
    }
    private IEnumerable<(Point A, Point B)> UncoveredSegments(Point a, Point b)
    {
        Vector axis = b-a; double length = axis.Length;
        if (length < 1e-8) yield break;
        axis=axis/axis.Length; double cursor = 0;
        var intervals = Walls.Concat(Doors).Select(n => OverlapInterval(a,b,End(n!.AsObject(),"a"),End(n.AsObject(),"b"))).Where(v => v.HasValue).Select(v => v!.Value).OrderBy(v => v.Start);
        foreach (var interval in intervals)
        {
            if (interval.Start-cursor > 1e-5) yield return (a+axis*cursor,a+axis*interval.Start);
            cursor = Math.Max(cursor,interval.End);
        }
        if (length-cursor > 1e-5) yield return (a+axis*cursor,b);
    }
    private void EnsureSegmentFree(Point a, Point b, JsonObject? ignored, IEnumerable<JsonObject>? group = null)
    {
        if (Walls.Concat(Doors).Select(n => n!.AsObject()).Any(s => !ReferenceEquals(s,ignored) && !(group?.Contains(s) ?? false) && OverlapInterval(a,b,End(s,"a"),End(s,"b")) is not null))
            throw new FormatException(L("Участок стены или проёма уже занят другим объектом."));
    }
    private void EnsureRoomFree(double x, double y, double w, double h, JsonObject? ignored)
    {
        if (Rooms.Any(n => !ReferenceEquals(n,ignored) && x < Number(n!["x"])+Number(n["w"])-1e-6 && x+w > Number(n["x"])+1e-6 && y < Number(n["y"])+Number(n["h"])-1e-6 && y+h > Number(n["y"])+1e-6))
            throw new FormatException(L("Комнаты не должны перекрывать друг друга."));
    }
    private static IEnumerable<(Point A, Point B)> RoomEdges(JsonObject room)
    {
        double x=Number(room["x"]), y=Number(room["y"]), w=Number(room["w"]), h=Number(room["h"]);
        yield return (new Point(x,y),new Point(x+w,y)); yield return (new Point(x+w,y),new Point(x+w,y+h));
        yield return (new Point(x+w,y+h),new Point(x,y+h)); yield return (new Point(x,y+h),new Point(x,y));
    }
    // Preserve portions of this room's walls and openings that also bound another room.
    private void RetainSharedSegments(JsonObject room, IEnumerable<(Point A, Point B)>? future = null)
    {
        string? id=room["id"]?.GetValue<string>(); if (id is null) return;
        foreach (var array in new[] { Walls,Doors })
        foreach (var segment in array.Select(n=>n!.AsObject()).Where(n=>n["roomId"]?.GetValue<string>()==id).ToList())
        {
            Point a=End(segment,"a"),b=End(segment,"b"); Vector axis=b-a; if (axis.Length<1e-8) continue; axis=axis/axis.Length;
            foreach (var other in Rooms.Select(n=>n!.AsObject()).Where(n=>!ReferenceEquals(n,room)))
            foreach (var edge in RoomEdges(other))
            {
                var interval=OverlapInterval(a,b,edge.A,edge.B); if (interval is null) continue;
                Point first=a+axis*interval.Value.Start,last=a+axis*interval.Value.End;
                double length=((Vector)(last-first)).Length, cursor=0;
                var occupied=(future ?? Enumerable.Empty<(Point A,Point B)>()).Select(f=>OverlapInterval(first,last,f.A,f.B)).Where(v=>v.HasValue).Select(v=>v!.Value).OrderBy(v=>v.Start).ToList();
                occupied.Add((length,length));
                foreach (var used in occupied)
                {
                    if (used.Start-cursor>1e-5) { var copy=segment.DeepClone().AsObject(); copy["id"]=Id(); copy["roomId"]=other["id"]?.DeepClone(); Point c=first+axis*cursor,d=first+axis*used.Start; copy["a"]=Point(c.X,c.Y); copy["b"]=Point(d.X,d.Y); array.Add(copy); }
                    cursor=Math.Max(cursor,used.End);
                }
            }
        }
    }
    private void CompleteRoomWalls(JsonObject room)
    {
        string? id=room["id"]?.GetValue<string>(); if (id is null) return;
        double thickness=double.TryParse(Thickness.Text.Replace(',','.'),System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var t) && t>=.05 && t<=1 ? t : .14;
        foreach (var edge in RoomEdges(room)) AddWall(id,edge.A.X,edge.A.Y,edge.B.X,edge.B.Y,thickness);
    }
    private void EnsurePointFree(double x, double y, JsonObject? ignored)
    {
        if (Points.Any(n => !ReferenceEquals(n,ignored) && ((Vector)(new Point(Number(n!["x"]),Number(n["y"]))-new Point(x,y))).SquaredLength <= 1e-12))
            throw new FormatException(L("В этом месте уже есть точка замера."));
    }
    private Vector AdjustDrag(ObjectEntry item, Vector delta)
    {
        if (item.Kind == "point") {
            var start = new Point(Number(item.Item["x"]), Number(item.Item["y"]));
            return SnapSurveyPosition(start + delta) - start;
        }
        if (SnapEnabled.IsChecked == true && !PointerContext.Modifiers.HasFlag(KeyModifiers.Shift)) { double step=GridSize; delta=delta.WithX(Math.Round(delta.X/step)*step); delta=delta.WithY(Math.Round(delta.Y/step)*step); }
        if (item.Kind == "door") { Vector axis=End(item.Item,"b")-End(item.Item,"a"); if (axis.Length > 1e-8) { axis=axis/axis.Length; delta=axis*Vectors.Dot(axis,delta); } }
        if(SnapEnabled.IsChecked==true && !PointerContext.Modifiers.HasFlag(KeyModifiers.Shift))delta=SnapMovedSurface(item,delta);
        return delta;
    }
    private (Point Before, Point After)? OpeningBounds(JsonObject opening)
    {
        Point a=End(opening,"a"), b=End(opening,"b"); Vector direction=b-a;
        if (direction.Length < 1e-6) return null;
        direction=direction/direction.Length; string? roomId=opening["roomId"]?.GetValue<string>();
        var before=new List<Point>(); var after=new List<Point>();
        foreach (var node in Walls)
        {
            var wall=node!.AsObject(); if (wall["roomId"]?.GetValue<string>() != roomId) continue;
            Point c=End(wall,"a"), d=End(wall,"b"); if (Math.Abs(Cross(direction,d-c))>1e-5) continue;
            if (((Vector)(c-a)).Length<1e-5) before.Add(d); if (((Vector)(d-a)).Length<1e-5) before.Add(c);
            if (((Vector)(c-b)).Length<1e-5) after.Add(d); if (((Vector)(d-b)).Length<1e-5) after.Add(c);
        }
        return before.Count==1 && after.Count==1 ? (before[0],after[0]) : null;
    }
    private bool CanMove(ObjectEntry item, Vector delta)
    {
        var obj=item.Item;
        try
        {
            if (item.Kind == "wall") { if (obj["roomId"] is not null) return false; EnsureSegmentFree(End(obj,"a")+delta,End(obj,"b")+delta,obj); }
            else if (item.Kind == "room")
            {
                double x=Number(obj["x"])+delta.X, y=Number(obj["y"])+delta.Y, w=Number(obj["w"]), h=Number(obj["h"]);
                EnsureRoomFree(x,y,w,h,obj);
                string? id=obj["id"]?.GetValue<string>(); var group=Walls.Concat(Doors).Select(n=>n!.AsObject()).Where(s=>id is not null && s["roomId"]?.GetValue<string>()==id).ToList();
                foreach (var segment in group) EnsureSegmentFree(End(segment,"a")+delta,End(segment,"b")+delta,segment,group);
            }
            else if (item.Kind == "point") { double x=Number(obj["x"])+delta.X,y=Number(obj["y"])+delta.Y; EnsurePointFree(x,y,obj); if (!Rooms.Any(r=>x>=Number(r!["x"]) && x<=Number(r["x"])+Number(r["w"]) && y>=Number(r["y"]) && y<=Number(r["y"])+Number(r["h"]))) return false; }
            else if (item.Kind == "door") { var bounds=OpeningBounds(obj); if (bounds is null) return false; Vector axis=End(obj,"b")-End(obj,"a"); axis=axis/axis.Length; if (Vectors.Dot(End(obj,"a")+delta-bounds.Value.Before,axis)<.08 || Vectors.Dot(bounds.Value.After-End(obj,"b")-delta,axis)<.08) return false; }
            return true;
        }
        catch (FormatException) { return false; }
    }
    private void TapeClick(Point p)
    {
        if (tapeStart is null) { tapeStart=p; tapeCursor=p; }
        else {
            if(((Vector)(p-tapeStart.Value)).Length*Scale<1)return;
            Remember();Tapes.Add(new JsonObject {["id"]=Id(),["a"]=Point(tapeStart.Value.X,tapeStart.Value.Y),["b"]=Point(p.X,p.Y)});
            tapeStart=tapeCursor=null;dirty=true;RefreshTabHeaders();
        }
        DrawTape();
    }
    private void OverlayLine(Point a, Point b, IBrush color, double width, bool dashed=false)
    {
        var line=new Line { X1=Screen(a.X), Y1=Screen(a.Y), X2=Screen(b.X), Y2=Screen(b.Y), Stroke=color, StrokeThickness=width, IsHitTestVisible=false };
        if (dashed) line.StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> { 4,3 }; Plan.Children.Add(line);
    }
    private void Dimension(Point a, Point b, Vector offset)
    {
        Point c=a+offset,d=b+offset; OverlayLine(a,c,Brushes.DarkOrange,1,true); OverlayLine(b,d,Brushes.DarkOrange,1,true); OverlayLine(c,d,Brushes.DarkOrange,1,true);
        AddLabel(L("{0:0.##} м", ((Vector)(b-a)).Length),Screen((c.X+d.X)/2)+3,Screen((c.Y+d.Y)/2)+3);
    }
    private void DrawEditorOverlay()
    {
        DrawMarquee();
        DrawTape();
        DrawDragPreview();
    }

    private readonly List<Control> dragPreview=[];
    private bool DrawDragPreview()
    {
        foreach(var control in dragPreview) Plan.Children.Remove(control);
        dragPreview.Clear();
        if (dragStart is null || dragItem is not ObjectEntry item) return true;
        int firstPreview=Plan.Children.Count;
        bool valid=CanMove(item,dragDelta);
        var obj=item.Item; IBrush color=valid?Brushes.DarkOrange:Brushes.Crimson;
        DrawObjectShape(item, dragDelta, SelectionOpacity, color, null);
        if (item.Kind=="door" && OpeningBounds(obj) is {} bounds) {
            Point a=End(obj,"a")+dragDelta,b=End(obj,"b")+dragDelta;
            Vector axis=b-a; axis=axis/axis.Length; Vector offset=new(-axis.Y*.25,axis.X*.25);
            Dimension(bounds.Before,a,offset); Dimension(b,bounds.After,offset);
        }
        if (item.Kind=="wall") {
            Point a=End(obj,"a")+dragDelta,b=End(obj,"b")+dragDelta;
            var target=Walls.Select(n=>n!.AsObject()).Where(w=>!ReferenceEquals(w,obj))
                .OrderBy(w=>Math.Min(SegmentDistance(a,w).Distance,SegmentDistance(b,w).Distance)).FirstOrDefault();
            if(target is not null && Math.Min(SegmentDistance(a,target).Distance,SegmentDistance(b,target).Distance)<.2) {
                Point start=End(target,"a"),end=End(target,"b"); Vector axis=end-start;
                if(axis.Length>1e-8) {
                    axis=axis/axis.Length;
                    Point contact=SegmentDistance(a,target).Distance<=SegmentDistance(b,target).Distance?a:b;
                    Point projected=start+axis*Vectors.Dot(contact-start,axis);
                    Vector offset=new(-axis.Y*.25,axis.X*.25);
                    Dimension(start,projected,offset); Dimension(projected,end,offset);
                    OverlayLine(contact,projected+offset,Brushes.DarkOrange,1,true);
                }
            }
        }
        dragPreview.AddRange(Plan.Children.Skip(firstPreview));
        return valid;
    }
}
