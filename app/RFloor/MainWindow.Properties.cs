using System.Globalization;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private const string HighlightTag = "selection-highlight";
    private double shownSegmentLength;
    private static string Display(double value) => value.ToString("0.########", CultureInfo.CurrentCulture);

    private void ClearSelectionHighlight()
    {
        for (int i = Plan.Children.Count - 1; i >= 0; i--)
            if (Plan.Children[i] is FrameworkElement element && Equals(element.Tag, HighlightTag)) Plan.Children.RemoveAt(i);
    }

    private void ShowProperties(ObjectEntry selected)
    {
        var obj = selected.Item;
        bool room = selected.Kind == "room", point = selected.Kind == "point", wall = selected.Kind == "wall", segment = IsPlanSegment(selected.Kind);
        PropertiesEditor.IsVisible = true;
        PropertyNamePanel.IsVisible = true;
        PropertySizePanel.IsVisible = !point;
        PropertyLengthPanel.IsVisible = segment;
        PropertyThicknessPanel.IsVisible = false;
        PropertyLengthLabel.Text=L("Длина, м");
        PropertyName.Text = ObjectName(obj, room ? L("Комната") : point ? L("Точка") : wall ? L("Стена") : L("Проём"));
        bool ownedWall = wall && obj["roomId"] is not null;
        PropertyX.IsReadOnly = PropertyY.IsReadOnly = PropertyW.IsReadOnly = PropertyH.IsReadOnly = false;
        PropertyXLabel.Text = segment ? L("Начало X, м") : L("X, м"); PropertyYLabel.Text = segment ? L("Начало Y, м") : L("Y, м");
        PropertyWLabel.Text = segment ? L("Конец X, м") : L("Ширина, м"); PropertyHLabel.Text = segment ? L("Конец Y, м") : L("Высота, м");
        PropertyX.Text = Display(Number(segment ? obj["a"]!["x"] : obj["x"]));
        PropertyY.Text = Display(Number(segment ? obj["a"]!["y"] : obj["y"]));
        if (room || segment || selected.Kind=="decoration")
        {
            var bounds=selected.Kind=="decoration"?DecorationBounds(obj):default;
            PropertyW.Text = Display(selected.Kind=="decoration"?bounds.Width:Number(segment ? obj["b"]!["x"] : obj["w"]));
            PropertyH.Text = Display(selected.Kind=="decoration"?bounds.Height:Number(segment ? obj["b"]!["y"] : obj["h"]));
        }
        if(segment){shownSegmentLength=Length(obj);PropertyLength.Text=Display(shownSegmentLength);}
        if (wall) PropertyThickness.Text = Display(obj["thickness"] is null ? .14 : Number(obj["thickness"]));
        bool roomHasWalls = room && HasRoomWalls(obj);
        PropertyHint.Text = room && !roomHasWalls ? L("Стены не связаны с этой комнатой и не изменятся вместе с ней.") : ownedWall ? L("Концы стены должны оставаться на границе комнаты.") : room ? L("Стены комнаты перемещаются вместе с ней. Ширина и положение проёмов сохраняются при изменении размеров.") : point ? L("При переносе точки результаты замера сохраняются.") : selected.Kind == "door" && obj["freeOpening"] is null ? L("Задайте концы проёма на той же стене; соседние участки стены изменятся вместе с ним.") : "";
        LoadObjectLabelProperties(selected);
    }

    private void ApplyProperties_Click(object sender, RoutedEventArgs e)
    {
        if(measurementCancellation is not null)return;
        if (Objects.SelectedItem is not ObjectEntry selected) return;
        try
        {
            var obj = selected.Item;
            double x = Parse(PropertyX, "X"), y = Parse(PropertyY, "Y");
            double w = 0, h = 0;
            bool segment=IsPlanSegment(selected.Kind);
            if (segment || selected.Kind is "room" or "decoration")
            {
                w = Parse(PropertyW, PropertyWLabel.Text!, selected.Kind=="room");
                h = Parse(PropertyH, PropertyHLabel.Text!, selected.Kind=="room");
            }
            var a=new Point(x,y);var b=new Point(w,h);
            if(segment)
            {
                Vector axis=b-a;
                double length=Parse(PropertyLength,L("Длина, м"),true);
                if(Math.Abs(length-shownSegmentLength)>1e-7)
                {
                    if(axis.Length<1e-8)axis=End(obj,"b")-End(obj,"a");
                    b=a+axis*(length/axis.Length);
                }
                bool moved=((Vector)(a-End(obj,"a"))).Length>1e-7 || ((Vector)(b-End(obj,"b"))).Length>1e-7;
                if(moved)
                {
                    if(!double.IsFinite(b.X) || !double.IsFinite(b.Y) || Math.Abs(b.X)>=10000 || Math.Abs(b.Y)>=10000 ||
                        ((Vector)(b-a)).Length<(selected.Kind=="tape"?.001:.15)-1e-8)
                        throw new FormatException(L("Проверьте координаты и длину отрезка."));
                    if(selected.Kind=="door" && obj["freeOpening"] is null)EditOpening(obj,a,b);
                    else
                    {
                        if(selected.Kind=="wall")ValidateWallResize(obj,a,b);
                        if(selected.Kind!="tape")EnsureSegmentFree(a,b,obj);
                        Remember();
                        SetSegmentEnds(obj,a,b);
                    }
                }
                else Remember();
            }
            else if (selected.Kind == "point" && !CanPlaceSurveyPoint(new Point(x,y)))
                throw new FormatException(L("Разместите точку внутри комнаты или вплотную к стене."));
            else if (selected.Kind == "room")
            {
                double ox = Number(obj["x"]), oy = Number(obj["y"]), ow = Number(obj["w"]), oh = Number(obj["h"]);
                if (w < .2 || h < .2 || Math.Abs(x+w) >= 10000 || Math.Abs(y+h) >= 10000) throw new FormatException(L("Размер комнаты должен быть не меньше 0,2 м."));
                EnsureRoomFree(x, y, w, h, obj);
                string? id = obj["id"]?.GetValue<string>();
                var walls = Walls.Select(n => n!.AsObject()).Where(v => id is not null && v["roomId"]?.GetValue<string>() == id).ToList();
                var doors = Doors.Select(n => n!.AsObject()).Where(v => id is not null && v["roomId"]?.GetValue<string>() == id).ToList();
                bool changed = x != ox || y != oy || w != ow || h != oh;
                bool hadWalls=HasRoomWalls(obj);
                if (changed && Points.Any(n => { var p = n!.AsObject(); double px = Number(p["x"]), py = Number(p["y"]); return px >= ox && px <= ox+ow && py >= oy && py <= oy+oh && !(px >= x && px <= x+w && py >= y && py <= y+h) && !Rooms.Any(other => !ReferenceEquals(other, obj) && px >= Number(other!["x"]) && px <= Number(other["x"])+Number(other["w"]) && py >= Number(other["y"]) && py <= Number(other["y"])+Number(other["h"])); }))
                    throw new FormatException(L("После изменения некоторые точки замера окажутся вне комнат. Сначала переместите точки."));
                double Map(double value, double origin, double oldSize, double newOrigin, double newSize)
                {
                    double offset = value-origin;
                    if (Math.Abs(offset-oldSize) < 1e-6) return newOrigin+newSize;
                    return newOrigin+offset;
                }
                var mapped = walls.Concat(doors).Select(segment => (segment, a: new Point(Map(Number(segment["a"]!["x"]),ox,ow,x,w), Map(Number(segment["a"]!["y"]),oy,oh,y,h)), b: new Point(Map(Number(segment["b"]!["x"]),ox,ow,x,w), Map(Number(segment["b"]!["y"]),oy,oh,y,h)))).ToList();
                if (mapped.Any(m => m.a.X < x-1e-6 || m.a.X > x+w+1e-6 || m.b.X < x-1e-6 || m.b.X > x+w+1e-6 || m.a.Y < y-1e-6 || m.a.Y > y+h+1e-6 || m.b.Y < y-1e-6 || m.b.Y > y+h+1e-6 || ((Vector)(m.b-m.a)).Length < .08))
                    throw new FormatException(L("Новый размер пересекает проём или слишком сильно укорачивает стену."));
                foreach (var m in mapped) EnsureSegmentFree(m.a, m.b, m.segment, walls.Concat(doors));
                Remember();
                if (changed) RetainSharedSegments(obj, mapped.Select(m => (m.a,m.b)));
                foreach (var m in mapped) { m.segment["a"]!["x"] = m.a.X; m.segment["a"]!["y"] = m.a.Y; m.segment["b"]!["x"] = m.b.X; m.segment["b"]!["y"] = m.b.Y; }
                obj["x"] = x; obj["y"] = y; obj["w"] = w; obj["h"] = h; obj["name"] = string.IsNullOrWhiteSpace(PropertyName.Text) ? L("Комната") : PropertyName.Text.Trim();
                if (changed && hadWalls) CompleteRoomWalls(obj);
            }
            else if (selected.Kind == "point") { EnsurePointFree(x, y, obj); Remember(); obj["x"] = x; obj["y"] = y; obj["name"] = string.IsNullOrWhiteSpace(PropertyName.Text) ? L("Точка") : PropertyName.Text.Trim(); }
            else if(selected.Kind=="decoration")
            {
                bool line=obj["kind"]?.ToString()=="line";
                if(w<0 || h<0 || (line?w+h<.001:w<.001 || h<.001))
                    throw new FormatException(L("Проверьте ширину и высоту объекта."));
                var bounds=DecorationBounds(obj);
                Remember();obj["x"]=x;obj["y"]=y;
                if(obj["kind"]?.ToString()=="text")
                {
                    if(Math.Abs(w-bounds.Width)>1e-7 || Math.Abs(h-bounds.Height)>1e-7){obj["boxWidth"]=w;obj["boxHeight"]=h;}
                }
                else {obj["w"]=w;obj["h"]=h;}
            }
            else return;
            if(!string.IsNullOrWhiteSpace(PropertyName.Text) && PropertyName.Text.Trim()!=ObjectName(obj,selected.Kind))obj.Remove("copyBaseName");
            obj["name"] = string.IsNullOrWhiteSpace(PropertyName.Text) ? ObjectName(obj,selected.Kind) : PropertyName.Text.Trim();
            SaveObjectLabelProperties(obj);
            dirty = true; RenderPlan(); Objects.SelectedItem = Objects.Items.Cast<ObjectEntry>().FirstOrDefault(item => ReferenceEquals(item.Item, obj)); Status.Text = L("Изменения применены. Сохраните проект.");
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте свойства"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void ValidateWallResize(JsonObject wall,Point a,Point b)
    {
        if(wall["roomId"] is not {} id)return;
        var room=Rooms.OfType<JsonObject>().FirstOrDefault(r=>r["id"]?.ToString()==id.ToString());
        if(room is not null && !RoomEdges(room).Any(edge=>((Vector)(OnEdge(a,edge.A,edge.B)-a)).Length<1e-6 &&
            ((Vector)(OnEdge(b,edge.A,edge.B)-b)).Length<1e-6))
            throw new FormatException(L("Концы стены должны оставаться на границе комнаты."));
        foreach(var opening in Doors.OfType<JsonObject>().Where(d=>d["roomId"]?.ToString()==id.ToString()))
        foreach(var end in new[]{"a","b"})
        {
            var point=End(opening,end);
            if((((Vector)(point-End(wall,"a"))).Length<1e-6 || ((Vector)(point-End(wall,"b"))).Length<1e-6) &&
                ((Vector)(point-a)).Length>=1e-6 && ((Vector)(point-b)).Length>=1e-6)
                throw new FormatException(L("Измените ширину проёма в его свойствах, чтобы передвинуть общий конец."));
        }
    }
    private void EditOpening(JsonObject opening, Point newA, Point newB)
    {
        var oldA = new Point(Number(opening["a"]!["x"]), Number(opening["a"]!["y"]));
        var oldB = new Point(Number(opening["b"]!["x"]), Number(opening["b"]!["y"]));
        Vector direction = oldB-oldA; double originalLength = direction.Length;
        if (originalLength < 1e-6) throw new FormatException(L("Некорректный проём."));
        direction=direction/direction.Length; Vector normal = new(-direction.Y, direction.X);
        if (Math.Abs(Vectors.Dot(newA-oldA, normal)) > 1e-5 || Math.Abs(Vectors.Dot(newB-oldA, normal)) > 1e-5 || Vectors.Dot(newB-newA, direction) < .15)
            throw new FormatException(L("Концы проёма должны находиться на прежней стене; ширина — не меньше 0,15 м."));
        string? roomId = opening["roomId"]?.GetValue<string>();
        var matches = new List<(JsonObject Wall, string Endpoint, Point Other, bool Start)>();
        foreach (var node in Walls)
        {
            var wall = node!.AsObject();
            if (wall["roomId"]?.GetValue<string>() != roomId) continue;
            var a = new Point(Number(wall["a"]!["x"]), Number(wall["a"]!["y"]));
            var b = new Point(Number(wall["b"]!["x"]), Number(wall["b"]!["y"]));
            if (Math.Abs(Vectors.Dot(b-a, normal)) > 1e-5) continue;
            if (((Vector)(a-oldA)).Length < 1e-5) matches.Add((wall,"a",b,true));
            if (((Vector)(b-oldA)).Length < 1e-5) matches.Add((wall,"b",a,true));
            if (((Vector)(a-oldB)).Length < 1e-5) matches.Add((wall,"a",b,false));
            if (((Vector)(b-oldB)).Length < 1e-5) matches.Add((wall,"b",a,false));
        }
        var before = matches.Where(m => m.Start).ToList(); var after = matches.Where(m => !m.Start).ToList();
        if (before.Count != 1 || after.Count != 1 || Vectors.Dot(newA-before[0].Other,direction) < .08 || Vectors.Dot(after[0].Other-newB,direction) < .08 ||
            WallJunctions(newA-direction*.08,newB+direction*.08).Any())
            throw new FormatException(L("Недостаточно места на соседних участках стены или геометрия неоднозначна."));
        var neighbors = new[] { opening, before[0].Wall, after[0].Wall };
        EnsureSegmentFree(newA, newB, opening, neighbors);
        EnsureSegmentFree(before[0].Other, newA, before[0].Wall, neighbors);
        EnsureSegmentFree(newB, after[0].Other, after[0].Wall, neighbors);
        Remember();
        before[0].Wall[before[0].Endpoint]!["x"] = newA.X; before[0].Wall[before[0].Endpoint]!["y"] = newA.Y;
        after[0].Wall[after[0].Endpoint]!["x"] = newB.X; after[0].Wall[after[0].Endpoint]!["y"] = newB.Y;
        opening["a"]!["x"] = newA.X; opening["a"]!["y"] = newA.Y; opening["b"]!["x"] = newB.X; opening["b"]!["y"] = newB.Y;
    }

}
