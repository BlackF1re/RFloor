using System.Text.Json.Nodes;
namespace RFloor;
public partial class MainWindow {
    private Geometry DecorationSurface(JsonObject obj) {
        Rect r=DecorationBounds(obj);r=new Rect(Screen(r.X),Screen(r.Y),r.Width*Scale,r.Height*Scale);
        if(obj["kind"]?.ToString()=="line") {
            bool reverse=obj["reverse"]?.GetValue<bool>()==true;
            return SurfaceLine(new Point(r.Left,reverse?r.Bottom:r.Top),new Point(r.Right,reverse?r.Top:r.Bottom),2);
        }
        if(obj["kind"]?.ToString()=="router")return new EllipseGeometry(new Rect(r.X+r.Width/2-r.Width*373.5/1024,r.Y+r.Height/2-r.Height*373.5/1024,r.Width*747/1024,r.Height*747/1024));
        if(obj["kind"]?.ToString()=="text")return new RectangleGeometry(r,6,6);

        Geometry body=obj["kind"]?.ToString()=="ellipse"?new EllipseGeometry(r):new RectangleGeometry(r,obj["kind"]?.ToString()=="roundRect"?4:0,obj["kind"]?.ToString()=="roundRect"?4:0);
        return body;
    }
    private IEnumerable<Geometry> SurfaceBodies(JsonObject? ignored=null,string? roomId=null) {
        var walls=Walls.Select(n=>n!.AsObject()).ToList();

        foreach(var obj in walls.Concat(Doors.Select(n=>n!.AsObject()))) {
            if(ReferenceEquals(obj,ignored) || roomId is not null && obj["roomId"]?.ToString()==roomId)continue;
            Geometry shape=walls.Contains(obj)?WallGeometry(obj,walls):OpeningGeometry(obj);
            yield return shape;
        }

        foreach(var obj in Decorations.Select(n=>n!.AsObject()).Where(o=>!ReferenceEquals(o,ignored)))
            yield return DecorationSurface(obj);
        foreach(var obj in Points.Select(n=>n!.AsObject()).Where(o=>!ReferenceEquals(o,ignored)))
            yield return new EllipseGeometry(new Rect(Screen(Number(obj["x"]))-1.25,Screen(Number(obj["y"]))-1.25,2.5,2.5));
    }
    private IEnumerable<(Point A,Point B)> SurfaceEdges(JsonObject? ignored=null,string? roomId=null) {
        var bodies=SurfaceBodies(ignored,roomId).ToList();
        foreach(var edge in bodies.SelectMany(GeometryEdges)) {
            Vector axis=edge.B-edge.A;if(axis.Length<1e-9)continue;
            var midpoint=new Point(Screen((edge.A.X+edge.B.X)/2),Screen((edge.A.Y+edge.B.Y)/2));
            Vector normal=new(-axis.Y/axis.Length*.05,axis.X/axis.Length*.05);
            if(bodies.Any(b=>b.FillContains(midpoint+normal)) && bodies.Any(b=>b.FillContains(midpoint-normal)))continue;
            yield return edge;
        }
    }
    private IEnumerable<(Point A,Point B)> GeometryEdges(Geometry geometry) {
        Point WorldPixel(Point p)=>new((p.X-origin)/Scale,(p.Y-origin)/Scale);
        var points=Contour(geometry);
        for(int i=0;i<points.Count;i++)yield return (WorldPixel(points[i]),WorldPixel(points[(i+1)%points.Count]));
    }
    private static Point OnEdge(Point p,Point a,Point b) {Vector d=b-a;return a+d*(d.SquaredLength<1e-16?0:Math.Clamp(Vectors.Dot(p-a,d)/d.SquaredLength,0,1));}
    private Point? SurfaceSnap(Point point) {
        double best=11/(Scale*PlanZoom.ScaleX);Point? result=null;
        foreach(var edge in SurfaceEdges()) {
            Point candidate=OnEdge(point,edge.A,edge.B);double distance=((Vector)(candidate-point)).Length;
            if(distance<best){best=distance;result=candidate;}
        }
        return result;
    }
    private void AlignDecoration(JsonObject obj)
    {
        if(SnapEnabled.IsChecked!=true || PointerContext.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))return;
        var entry=new ObjectEntry("",obj,"decoration");
        Vector shift=SnapMovedSurface(entry,new Vector());
        obj["x"]=Number(obj["x"])+shift.X;obj["y"]=Number(obj["y"])+shift.Y;
    }
    // Parallel lines meet the contacted face with their own stroke edge.
    private (Point A,Point B) AlignConstructionLine(Point a,Point b,double width) {
        if(SnapEnabled.IsChecked!=true || PointerContext.Modifiers.HasFlag(Avalonia.Input.KeyModifiers.Shift))return (a,b);
        Vector axis=b-a;if(axis.Length<1e-8)return (a,b);axis=axis/axis.Length;
        foreach(var shape in SurfaceBodies()) {
            foreach(var edge in GeometryEdges(shape)) {
                Vector face=edge.B-edge.A;if(face.Length<1e-8)continue;face=face/face.Length;
                if(Math.Abs(Cross(axis,face))>1e-6 || ((Vector)(OnEdge(a,edge.A,edge.B)-a)).Length>1e-6)continue;
                Vector normal=new(-face.Y,face.X);Point middle=OnEdge(a,edge.A,edge.B);
                if(shape.FillContains(new Point(Screen(middle.X)+normal.X*.1,Screen(middle.Y)+normal.Y*.1)))normal=-normal;
                Vector offset=normal*(width/2/Scale);return (a+offset,b+offset);
            }
        }
        return (a,b);
    }
    private Vector SnapMovedSurface(ObjectEntry entry,Vector delta) {
        if(entry.Kind is not ("wall" or "decoration" or "room" or "point"))return delta;
        var walls=Walls.Select(n=>n!.AsObject()).ToList();
        var shapes=(entry.Kind=="room"?walls.Where(w=>w["roomId"]?.ToString()==entry.Item["id"]?.ToString()).Select(w=>WallGeometry(w,walls)):
            new[]{entry.Kind=="point"?new EllipseGeometry(new Rect(Screen(Number(entry.Item["x"]))-1.25,Screen(Number(entry.Item["y"]))-1.25,2.5,2.5)):entry.Kind=="decoration"?DecorationSurface(entry.Item):WallGeometry(entry.Item,walls)}).ToList();
        var moving=shapes.SelectMany(GeometryEdges).Select(e=>(A:e.A+delta,B:e.B+delta)).ToList();
        var targets=SurfaceBodies(entry.Item,entry.Kind=="room"?entry.Item["id"]?.ToString():null).ToList();
        double tolerance=11/(Scale*PlanZoom.ScaleX);var corrections=new List<Vector>();
        void Consider(Vector v){if(v.Length<tolerance)corrections.Add(v);}
        foreach(var target in targets.SelectMany(GeometryEdges))
        foreach(var source in moving) {
            Consider(OnEdge(source.A,target.A,target.B)-source.A);Consider(OnEdge(source.B,target.A,target.B)-source.B);
            Consider(target.A-OnEdge(target.A,source.A,source.B));Consider(target.B-OnEdge(target.B,source.A,source.B));
        }
        // Combine nearby horizontal and vertical contacts to lock a corner on both axes.
        var xs=corrections.Where(v=>Math.Abs(v.Y)<1e-8 && Math.Abs(v.X)>1e-8).OrderBy(v=>v.SquaredLength).Take(4).ToList();
        var ys=corrections.Where(v=>Math.Abs(v.X)<1e-8 && Math.Abs(v.Y)>1e-8).OrderBy(v=>v.SquaredLength).Take(4).ToList();
        var corners=xs.SelectMany(x=>ys.Select(y=>x+y)).OrderBy(v=>v.SquaredLength);
        foreach(var correction in corners.Concat(corrections.OrderBy(v=>v.SquaredLength))) {
            Vector shift=delta+correction;
            bool clear=shapes.All(shape=> {
                var moved=shape.Clone();moved.Transform=new TranslateTransform(shift.X*Scale,shift.Y*Scale);
                return targets.All(target=>GeometryOps.IntersectionEmpty(moved,target));
            });
            if(clear)return shift;
        }
        return delta;
    }
}

