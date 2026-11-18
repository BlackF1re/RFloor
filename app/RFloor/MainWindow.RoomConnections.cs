using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private bool SnapActive => SnapEnabled.IsChecked == true && !PointerContext.Modifiers.HasFlag(KeyModifiers.Shift);

    private Point GridPosition(Point point)
    {
        double step = GridSize;
        return new Point(Math.Round(point.X / step) * step, Math.Round(point.Y / step) * step);
    }

    private (Point Start, Point End) SnapRoomCorners(Point start, Point end)
    {
        if (!SnapActive) return (start, end);
        Point first = GridPosition(start), last = GridPosition(end);
        double tolerance = 11 / (Scale * PlanZoom.ScaleX);
        double best = tolerance * tolerance;
        foreach (var room in Rooms.OfType<JsonObject>())
        foreach (var (a, b) in RoomEdges(room))
        {
            // A room joins the boundary of the existing room, independent of the painted wall width.
            bool vertical = a.X == b.X;
            double Normal(Point p) => vertical ? p.X : p.Y;
            double Along(Point p) => vertical ? p.Y : p.X;
            double roomMidpoint = vertical ? Number(room["x"]) + Number(room["w"]) / 2
                : Number(room["y"]) + Number(room["h"]) / 2;
            double outward = Math.Sign(Normal(a) - roomMidpoint);
            double Depth(Point p) => (Normal(p) - Normal(a)) * outward;
            bool Outside(Point p) => Depth(p) > 0;
            double DistanceToWall(Point p)
            {
                double depth = Depth(p);
                double normal = depth - Math.Clamp(depth, 0, WallStroke / Scale);
                double along = Along(p) - Along(OnEdge(p, a, b));
                return normal * normal + along * along;
            }
            double from = DistanceToWall(start), to = DistanceToWall(end);
            bool firstTouches = Outside(end), lastTouches = Outside(start);
            double distance = Math.Min(firstTouches ? from : double.PositiveInfinity, lastTouches ? to : double.PositiveInfinity);
            if (distance >= best) continue;
            double EdgeAlignment(Point raw, Point snapped)
            {
                double da = Math.Abs(Along(raw) - Along(a)), db = Math.Abs(Along(raw) - Along(b));
                return Math.Min(da, db) < tolerance ? Along(da < db ? a : b) : Along(snapped);
            }
            Point Align(Point raw, bool touching)
            {
                var grid = GridPosition(raw);
                double tangent = EdgeAlignment(raw, grid);
                return vertical ? new Point(touching ? a.X : grid.X, tangent)
                    : new Point(tangent, touching ? a.Y : grid.Y);
            }
            bool lockStart = firstTouches && (!lastTouches || from <= to);
            first = Align(start, lockStart);
            last = Align(end, !lockStart);
            best = distance;
        }
        return (first, last);
    }

    private (JsonObject Room, Point A, Point B)? FindRoomWallRepair(Point start, Point end)
    {
        if(!SnapActive) return null;
        double tolerance=Math.Max(GridSize*.55,12/(Scale*PlanZoom.ScaleX));
        (JsonObject, Point, Point)? best=null;
        double bestDistance=double.PositiveInfinity;
        foreach(var room in Rooms.OfType<JsonObject>())
        foreach(var (a,b) in RoomEdges(room))
        foreach(var (from,to) in UncoveredSegments(a,b))
        {
            double distance=Math.Min(
                Math.Max(((Vector)(start-from)).Length,((Vector)(end-to)).Length),
                Math.Max(((Vector)(start-to)).Length,((Vector)(end-from)).Length));
            if(distance>tolerance || distance>=bestDistance || ((Vector)(to-from)).Length<.15)continue;
            bestDistance=distance;
            best=(room,from,to);
        }
        return best;
    }

    private (Point Start,Point End) SnapDrawnWall(Point start,Point end) =>
        SnapActive?AlignConstructionLine(SurfaceSnap(start)??GridPosition(start),SurfaceSnap(end)??GridPosition(end),WallStroke):(start,end);

    private static bool BelongsToRoom(JsonObject segment, JsonObject room) =>
        (room["id"] is {} id && segment["roomId"]?.ToString() == id.ToString()) ||
        RoomEdges(room).Any(edge => OverlapInterval(edge.A, edge.B, End(segment, "a"), End(segment, "b")) is not null);

    private bool HasRoomWalls(JsonObject room) => Walls.OfType<JsonObject>().Any(wall => BelongsToRoom(wall, room));
}
