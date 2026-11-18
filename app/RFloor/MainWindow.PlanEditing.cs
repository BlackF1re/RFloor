using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private JsonObject? selectedCallout;
    private (string Kind,JsonObject Item)[] planClipboard=[];

    private static bool IsPlanSegment(string kind) => kind is "wall" or "door" or "tape";

    private void CopyPlan_Click(object? sender,RoutedEventArgs e)
    {
        if(surveyMode || measurementCancellation is not null)return;
        var selection=Objects.SelectedItems!.Cast<ObjectEntry>().ToArray();
        if(selection.Length==0)return;
        if(selection is [var entry] && ReferenceEquals(entry.Item,selectedCallout))
        {
            Point location=LabelPosition(entry);
            planClipboard=[("decoration",new JsonObject
            {
                ["kind"]="text",["name"]=ObjectLabel(entry),
                ["x"]=(location.X-origin)/Scale,["y"]=(location.Y-origin)/Scale,["w"]=.1,["h"]=.1,
                ["label"]=new JsonObject {["visible"]=true,["opacity"]=ObjectLabelOpacity(entry.Item),["typography"]=ObjectTypography(entry).ToJson()}
            })];
            return;
        }
        var rooms=selection.Where(e=>e.Kind=="room").Select(e=>e.Item).ToArray();
        var included=new List<(string Kind,JsonObject Item)>();
        var roomIds=rooms.Select(r=>r["id"]?.ToString()).ToHashSet();
        foreach(var item in Objects.Items.OfType<ObjectEntry>())
        {
            if(selection.Contains(item) || (item.Kind is "wall" or "door" && roomIds.Contains(item.Item["roomId"]?.ToString())))
                included.Add((item.Kind,item.Item.DeepClone().AsObject()));
            if(item.Kind is not ("wall" or "door"))continue;
            Point a=End(item.Item,"a"),b=End(item.Item,"b");
            foreach(var room in rooms)
            {
                // A shared wall already included with its original owner must be pasted only once.
                if(roomIds.Contains(item.Item["roomId"]?.ToString()))continue;
                foreach(var edge in RoomEdges(room))
                {
                    if(OverlapInterval(a,b,edge.A,edge.B) is not {} interval)continue;
                    Vector direction=(b-a)/((Vector)(b-a)).Length;
                    var part=item.Item.DeepClone().AsObject();
                    part["roomId"]=room["id"]?.DeepClone();
                    SetSegmentEnds(part,a+direction*interval.Start,a+direction*interval.End);
                    if(!included.Any(v=>v.Kind==item.Kind && IsPlanSegment(v.Kind) &&
                        OverlapInterval(End(part,"a"),End(part,"b"),End(v.Item,"a"),End(v.Item,"b")) is not null))
                        included.Add((item.Kind,part));
                }
            }
        }
        planClipboard=included.ToArray();
    }

    private void PastePlan_Click(object? sender,RoutedEventArgs e)
    {
        if(activeSession is null || surveyMode || measurementCancellation is not null || planClipboard.Length==0)return;
        var copies=planClipboard.Select(v=>(v.Kind,Item:v.Item.DeepClone().AsObject())).ToArray();
        var roomIds=copies.Where(v=>v.Kind=="room" && v.Item["id"] is not null)
            .ToDictionary(v=>v.Item["id"]!.ToString(),_=>Id());
        foreach(var opening in copies.Where(c=>c.Kind=="door").Select(c=>c.Item))
            if(!copies.Any(c=>c.Kind=="wall" && OpeningTouchesWall(opening,c.Item)))
                opening["freeOpening"]=true;
        Vector offset=FindPasteOffset(copies);
        var names=Objects.Items.OfType<ObjectEntry>().Select(e=>ObjectName(e.Item,e.Label)).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        Remember();
        foreach(var (kind,item) in copies)
        {
            var oldId=item["id"]?.ToString();
            item["id"]=kind=="room" && oldId is not null && roomIds.TryGetValue(oldId,out var newId)?newId:Id();
            string? owner=item["roomId"]?.ToString();
            if(owner is not null && roomIds.TryGetValue(owner,out var newOwner))item["roomId"]=newOwner;
            else item.Remove("roomId");
            if(IsPlanSegment(kind))SetSegmentEnds(item,End(item,"a")+offset,End(item,"b")+offset);
            else {item["x"]=Number(item["x"])+offset.X;item["y"]=Number(item["y"])+offset.Y;}
            var baseName=item["copyBaseName"]?.ToString()??ObjectName(item,L("Объект"));
            int index=1;
            string name;
            do name=baseName+" "+L("КОПИЯ{0}",index++);while(!names.Add(name));
            item["name"]=name;
            item["copyBaseName"]=baseName;
            var destination=kind switch {"room"=>Rooms,"wall"=>Walls,"door"=>Doors,"tape"=>Tapes,_=>Decorations};
            destination.Add(item);
        }
        selectedCallout=null;
        dirty=true;
        RenderPlan();
        Objects.SelectedItems!.Clear();
        foreach(var entry in Objects.Items.OfType<ObjectEntry>())
            if(copies.Any(c=>ReferenceEquals(c.Item,entry.Item)))Objects.SelectedItems.Add(entry);
        Plan.Focus();
    }

    private Vector FindPasteOffset((string Kind,JsonObject Item)[] copies)
    {
        var offset=new Vector(GridSize,GridSize);
        try
        {
            foreach(var (kind,item) in copies)
            {
                if(kind=="room")EnsureRoomFree(Number(item["x"])+offset.X,Number(item["y"])+offset.Y,Number(item["w"]),Number(item["h"]),null);
                if(kind is "wall" or "door")EnsureSegmentFree(End(item,"a")+offset,End(item,"b")+offset,null);
            }
            if(!copies.Any(c=>Objects.Items.OfType<ObjectEntry>().Any(e=>e.Kind==c.Kind &&
                (IsPlanSegment(c.Kind)
                    ? End(e.Item,"a")==End(c.Item,"a")+offset && End(e.Item,"b")==End(c.Item,"b")+offset
                    : Math.Abs(Number(e.Item["x"])-Number(c.Item["x"])-offset.X)<1e-6 &&
                      Math.Abs(Number(e.Item["y"])-Number(c.Item["y"])-offset.Y)<1e-6))))
                return offset;
        }
        catch(FormatException) { }
        double right=Objects.Items.OfType<ObjectEntry>().Max(e=>IsPlanSegment(e.Kind)
            ?Math.Max(End(e.Item,"a").X,End(e.Item,"b").X)
            :Number(e.Item["x"])+(Numeric(e.Item["w"],out var w)?w:0));
        double left=copies.Min(e=>IsPlanSegment(e.Kind)?Math.Min(End(e.Item,"a").X,End(e.Item,"b").X):Number(e.Item["x"]));
        return new Vector(Math.Max(GridSize,Math.Ceiling((right-left)/GridSize)*GridSize+GridSize),0);
    }

    private static void SetSegmentEnds(JsonObject segment,Point a,Point b)
    {
        segment["a"]=Point(a.X,a.Y);
        segment["b"]=Point(b.X,b.Y);
    }

    private static bool OpeningTouchesWall(JsonObject opening,JsonObject wall)
    {
        Vector openingAxis=End(opening,"b")-End(opening,"a");
        Vector wallAxis=End(wall,"b")-End(wall,"a");
        return Math.Abs(Cross(openingAxis,wallAxis))<1e-6 &&
            (SegmentDistance(End(opening,"a"),wall).Distance<1e-6 ||
             SegmentDistance(End(opening,"b"),wall).Distance<1e-6);
    }

    private void DeletePlanEntries(IEnumerable<ObjectEntry> entries)
    {
        var selection=entries.ToArray();
        if(selection.Length==0)return;
        Remember();
        if(selection is [var callout] && ReferenceEquals(callout.Item,selectedCallout))
        {
            SetObjectLabelVisibility(callout.Item,false);
            selectedCallout=null;
        }
        else
        {
            foreach(var room in selection.Where(e=>e.Kind=="room").Select(e=>e.Item))
            {
                RetainSharedSegments(room);
                var id=room["id"]?.ToString();
                foreach(var array in new[]{Walls,Doors})
                    foreach(var part in array.OfType<JsonObject>().Where(p=>id is not null && p["roomId"]?.ToString()==id).ToArray())
                        array.Remove(part);
                Rooms.Remove(room);
            }
            foreach(var entry in selection)
                switch(entry.Kind)
                {
                    case "wall": Walls.Remove(entry.Item);break;
                    case "decoration": Decorations.Remove(entry.Item);break;
                    case "tape": Tapes.Remove(entry.Item);break;
                }
            foreach(var opening in selection.Where(e=>e.Kind=="door").Select(e=>e.Item))
                if(Doors.Remove(opening) && opening["freeOpening"] is null)RestoreOpeningWall(Walls,opening);
        }
        dirty=true;
        RenderPlan();
    }

    private static void RestoreOpeningWall(JsonArray walls,JsonObject opening,bool legacy=false)
    {
        Point a=End(opening,"a"),b=End(opening,"b");
        Vector direction=b-a;
        if(direction.Length<1e-8)return;
        direction/=direction.Length;
        var owner=opening["roomId"]?.ToString();
        var neighbors=walls.OfType<JsonObject>().Where(w=>!ReferenceEquals(w,opening) && w["roomId"]?.ToString()==owner).ToArray();
        var joined=new List<JsonObject>(2);
        Point Extend(Point vertex,Vector outward)
        {
            bool Match(Point next)
            {
                Vector axis=next-vertex;
                return Math.Abs(Cross(axis,outward))<1e-5 && Vectors.Dot(axis,outward)>1e-5;
            }
            var matches=neighbors.Select(w=>(Wall:w,A:End(w,"a"),B:End(w,"b")))
                .Where(w=>(((Vector)(w.A-vertex)).Length<1e-5 && Match(w.B)) || (((Vector)(w.B-vertex)).Length<1e-5 && Match(w.A))).ToArray();
            if(matches is not [var match])return vertex;
            if(Math.Abs(WallThickness(match.Wall)-WallThickness(opening))>1e-5 && opening["thickness"] is not null)
                return vertex;
            joined.Add(match.Wall);
            return ((Vector)(match.A-vertex)).Length<1e-5?match.B:match.A;
        }
        var start=Extend(a,-direction);
        var end=Extend(b,direction);
        if(legacy)
        {
            // Older builds stored the deleted opening as a middle wall with its original name.
            if(joined.Count!=2 || joined[0]["name"]?.ToString()!=joined[1]["name"]?.ToString() ||
                joined.Any(w=>w["name"]?.ToString() is "Проём" or "Opening"))return;
            walls.Remove(opening);
        }
        var wall=joined.FirstOrDefault();
        if(wall is null)
        {
            wall=new JsonObject {["id"]=Id(),["thickness"]=WallThickness(opening)};
            if(owner is not null)wall["roomId"]=owner;
            walls.Add(wall);
        }
        else foreach(var neighbor in joined.Skip(1))walls.Remove(neighbor);
        wall["a"]=Point(start.X,start.Y);
        wall["b"]=Point(end.X,end.Y);
    }

    private static double WallThickness(JsonObject item) => Numeric(item["thickness"],out var thickness)?thickness:.14;
}
