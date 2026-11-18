using System.Text.Json.Nodes;
namespace RFloor;
public partial class MainWindow {
    private const double SelectionOpacity=.45;
    private const double WallStroke=5, OpeningStroke=1;
    private const double WallFaceTolerance=1e-8;
    private WallFace[][]? wallFaces;
    private ILookup<(bool Vertical, double Axis), JsonObject>? roomJunctionWalls;
    private readonly record struct WallFace(Point A, Point B, Vector Inward)
    {
        public double Depth(Point point) => Vectors.Dot(point-A, Inward);
    }

    private WallFace[][] WallFaces()
    {
        if (wallFaces is not null) return wallFaces;
        var walls = Walls.OfType<JsonObject>().Where(w => Length(w)>WallFaceTolerance).ToArray();
        return wallFaces = walls.Select(wall =>
        {
            var edges = GeometryEdges(WallGeometry(wall,walls)).ToArray();
            var center = new Point(edges.Average(edge=>edge.A.X),edges.Average(edge=>edge.A.Y));
            return edges.Select(edge =>
            {
                Vector axis = edge.B-edge.A;
                var normal = new Vector(-axis.Y,axis.X)/axis.Length;
                return new WallFace(edge.A,edge.B,Vectors.Dot(center-edge.A,normal)>0?normal:-normal);
            }).ToArray();
        }).ToArray();
    }

    private static bool InsideWall(Point point, WallFace[] wall) =>
        wall.All(face=>face.Depth(point)>WallFaceTolerance);

    private Geometry? SurveyMarkerClip(Point center, double size)
    {
        double radius = size / (2 * Scale);
        var origin = center - new Vector(radius,radius);
        Geometry? clip = null;
        foreach (var wall in WallFaces())
        {
            if (wall.Any(face=>face.Depth(center)+radius*(Math.Abs(face.Inward.X)+Math.Abs(face.Inward.Y))<=WallFaceTolerance)) continue;
            Point Local(Point point) => new((point.X-origin.X)*Scale,(point.Y-origin.Y)*Scale);
            var polygon = new StreamGeometry();
            using (var path = polygon.Open())
            {
                path.BeginFigure(Local(wall[0].A),true);
                foreach (var face in wall) path.LineTo(Local(face.B));
                path.EndFigure(true);
            }
            clip = GeometryOps.Combine(clip ?? new RectangleGeometry(new Rect(0,0,size,size)),polygon,GeometryCombineMode.Exclude,null);
        }
        return clip;
    }

    private static double WallEntryDistance(WallFace[] wall, Point start, Vector direction)
    {
        double enter=0, exit=double.PositiveInfinity;
        foreach (var face in wall)
        {
            double depth=face.Depth(start), speed=Vectors.Dot(direction,face.Inward);
            if (Math.Abs(speed)<WallFaceTolerance)
            {
                // Moving along a face does not enter the wall.
                if (depth<=WallFaceTolerance) return double.PositiveInfinity;
            }
            else if (speed>0) enter=Math.Max(enter,-depth/speed);
            else exit=Math.Min(exit,-depth/speed);
        }
        return exit-enter>WallFaceTolerance ? enter : double.PositiveInfinity;
    }
    private static double WallHalfWidth(JsonObject wall)=>0;
    // Room coordinates describe the clear interior. The visible stroke grows outwards.
    private Vector RoomOutward(JsonObject segment) {
        var room=Rooms.Select(n=>n!.AsObject()).FirstOrDefault(r=>r["id"]?.ToString()==segment["roomId"]?.ToString());
        if(room is null)return new Vector();
        Point a=End(segment,"a"),b=End(segment,"b");Vector direction=b-a;
        if(direction.Length<1e-8)return new Vector();
        direction=direction/direction.Length;Vector normal=new(-direction.Y,direction.X);
        Point center=new(Number(room["x"])+Number(room["w"])/2,Number(room["y"])+Number(room["h"])/2);
        return Vectors.Dot(normal,center-a)>0?-normal:normal;
    }
    private Geometry OpeningGeometry(JsonObject opening) {
        Vector offset=RoomOutward(opening)*(WallStroke/2);
        return SurfaceLine(new Point(Screen(End(opening,"a").X)+offset.X,Screen(End(opening,"a").Y)+offset.Y),new Point(Screen(End(opening,"b").X)+offset.X,Screen(End(opening,"b").Y)+offset.Y),OpeningStroke);
    }
    private Geometry WallGeometry(JsonObject wall,IReadOnlyList<JsonObject> walls) {
        Point a=End(wall,"a"),b=End(wall,"b");
        Point sa=new(Screen(a.X),Screen(a.Y)),sb=new(Screen(b.X),Screen(b.Y));
        Vector normal=RoomOutward(wall);
        if(normal.SquaredLength<.5)return SurfaceLine(sa,sb,WallStroke);
        var room=Rooms.Select(n=>n!.AsObject()).First(r=>r["id"]?.ToString()==wall["roomId"]?.ToString());
        return RoomWallGeometry(a,b,room,normal);
    }

    private Geometry RoomWallGeometry(Point a,Point b,JsonObject room,Vector normal)
    {
        Point sa=new(Screen(a.X),Screen(a.Y)),sb=new(Screen(b.X),Screen(b.Y));
        Vector along=sb-sa;
        double length=along.Length;
        along/=length;

        (Point Inner,Point Outer) EndCap(Point vertex, Point pixel, Vector intoWall)
        {
            Point outer=pixel+normal*WallStroke;
            if (!RoomEdges(room).Any(e=>((Vector)(e.A-vertex)).Length<1e-6)) return (pixel,outer);
            if (ForeignWallAtCorner(room,vertex,normal,intoWall))
            {
                // The common wall already fills this corner. Butt the returning wall into its face.
                var inset=intoWall*Math.Min(WallStroke,length*.49);
                return (pixel+inset,outer+inset);
            }
            return (pixel,outer-intoWall*WallStroke);
        }
        var (ia,oa)=EndCap(a,sa,along);
        var (ib,ob)=EndCap(b,sb,-along);
        var polygon=new StreamGeometry();
        using(var ctx=polygon.Open()){ctx.BeginFigure(ia,true);ctx.LineTo(ib);ctx.LineTo(ob);ctx.LineTo(oa);ctx.EndFigure(true);}
        return RegisterContour(polygon,[ia,ib,ob,oa]);
    }

    private bool ForeignWallAtCorner(JsonObject room,Point vertex,Vector normal,Vector intoWall)
    {
        var roomId=room["id"]?.ToString();
        roomJunctionWalls ??= Walls.OfType<JsonObject>().Where(w=>w["roomId"] is not null)
            .ToLookup(w=>WallAxis(End(w,"a"),End(w,"b")));
        // The adjoining wall follows the perpendicular edge toward the current room's interior.
        var inward=vertex-normal/Scale;
        return roomJunctionWalls[WallAxis(vertex,inward)].Any(w =>
            w["roomId"]?.ToString()!=roomId &&
            OverlapInterval(vertex,inward,End(w,"a"),End(w,"b")) is not null &&
            SegmentDistance(vertex,w).Distance<1e-6 &&
            Vectors.Dot(RoomOutward(w),intoWall)>.99);
    }

    private static (bool Vertical,double Axis) WallAxis(Point a,Point b)
    {
        bool vertical=Math.Abs(b.X-a.X)<1e-6;
        return (vertical,Math.Round(vertical?a.X:a.Y,6));
    }
}
