using System.Text.Json.Nodes;
namespace RFloor;
public partial class MainWindow {
    private const double SelectionOpacity=.45;
    private const double WallStroke=5, OpeningStroke=1;
    private const double WallFaceTolerance=1e-8;
    private WallFace[][]? wallFaces;
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
        bool Corner(Point q)=>RoomEdges(room).Any(e=>((Vector)(e.A-q)).Length<1e-6);
        Vector along=sb-sa;along=along/along.Length;
        Point oa=sa+normal*WallStroke-(Corner(a)?along*WallStroke:new Vector()),ob=sb+normal*WallStroke+(Corner(b)?along*WallStroke:new Vector());
        var polygon=new StreamGeometry();using(var ctx=polygon.Open()){ctx.BeginFigure(sa,true);ctx.LineTo(sb);ctx.LineTo(ob);ctx.LineTo(oa);ctx.EndFigure(true);}return RegisterContour(polygon,[sa,sb,ob,oa]);
    }
}
