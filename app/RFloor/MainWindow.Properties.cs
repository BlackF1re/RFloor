using System.Globalization;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private const string HighlightTag = "selection-highlight";
    private static string Display(double value) => value.ToString("0.########", CultureInfo.CurrentCulture);

    private void ClearSelectionHighlight()
    {
        for (int i = Plan.Children.Count - 1; i >= 0; i--)
            if (Plan.Children[i] is FrameworkElement element && Equals(element.Tag, HighlightTag)) Plan.Children.RemoveAt(i);
    }

    private void ShowProperties(ObjectEntry selected)
    {
        var obj = selected.Item;
        bool room = selected.Kind == "room", point = selected.Kind == "point", wall = selected.Kind == "wall", segment = selected.Kind is "wall" or "door";
        PropertiesEditor.IsVisible = true;
        PropertyNamePanel.IsVisible = true;
        PropertySizePanel.IsVisible = point || obj["kind"]?.ToString()=="router" ? false : true;
        PropertyThicknessPanel.IsVisible = false;
        PropertyName.Text = ObjectName(obj, room ? L("Комната") : point ? L("Точка") : wall ? L("Стена") : L("Проём"));
        bool ownedWall = wall && obj["roomId"] is not null;
        PropertyX.IsReadOnly = PropertyY.IsReadOnly = PropertyW.IsReadOnly = PropertyH.IsReadOnly = ownedWall;
        PropertyXLabel.Text = segment ? L("Начало X, м") : L("X, м"); PropertyYLabel.Text = segment ? L("Начало Y, м") : L("Y, м");
        PropertyWLabel.Text = segment ? L("Конец X, м") : L("Ширина, м"); PropertyHLabel.Text = segment ? L("Конец Y, м") : L("Высота, м");
        PropertyX.Text = Display(Number(segment ? obj["a"]!["x"] : obj["x"]));
        PropertyY.Text = Display(Number(segment ? obj["a"]!["y"] : obj["y"]));
        if (room || segment || selected.Kind=="decoration")
        {
            PropertyW.Text = Display(Number(segment ? obj["b"]!["x"] : obj["w"]));
            PropertyH.Text = Display(Number(segment ? obj["b"]!["y"] : obj["h"]));
        }
        if (wall) PropertyThickness.Text = Display(obj["thickness"] is null ? .14 : Number(obj["thickness"]));
        bool roomHasWalls = room && HasRoomWalls(obj);
        PropertyHint.Text = room && !roomHasWalls ? L("Стены не связаны с этой комнатой и не изменятся вместе с ней.") : ownedWall ? L("Положение стены связано с комнатой. Измените размеры комнаты.") : room ? L("Стены комнаты перемещаются вместе с ней. Ширина и положение проёмов сохраняются при изменении размеров.") : point ? L("При переносе точки результаты замера сохраняются.") : selected.Kind == "door" ? L("Задайте концы проёма на той же стене; соседние участки стены изменятся вместе с ним.") : "";
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
            double w = 0, h = 0, thickness = 0;
            if (selected.Kind is "room" or "wall" or "door" or "decoration") { w = Parse(PropertyW, L("Ширина или конец X"), selected.Kind == "room"); h = Parse(PropertyH, L("Высота или конец Y"), selected.Kind == "room"); }
            if (selected.Kind == "wall")
            {
                thickness = Parse(PropertyThickness, L("Толщина"), true);
                if (thickness < .05 || thickness > 1 || Math.Sqrt((w-x)*(w-x)+(h-y)*(h-y)) < .15) throw new FormatException(L("Длина стены должна быть не меньше 0,15 м, толщина — от 0,05 до 1 м."));
            }
            if (selected.Kind == "point" && !Rooms.Any(n => { var r = n!.AsObject(); return x >= Number(r["x"]) && x <= Number(r["x"]) + Number(r["w"]) && y >= Number(r["y"]) && y <= Number(r["y"]) + Number(r["h"]); }))
                throw new FormatException(L("Точка замера должна находиться внутри комнаты."));
            if (selected.Kind == "room")
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
            else if (selected.Kind == "door") { EditOpening(obj, new Point(x, y), new Point(w, h)); }
            else if (selected.Kind == "wall") { EnsureSegmentFree(new Point(x,y), new Point(w,h), obj); Remember(); obj["a"]!["x"] = x; obj["a"]!["y"] = y; obj["b"]!["x"] = w; obj["b"]!["y"] = h; obj["thickness"] = thickness; }
            else if(selected.Kind=="decoration"){Remember();obj["x"]=x;obj["y"]=y;obj["w"]=Math.Max(0,w);obj["h"]=Math.Max(0,h);}
            else return;
            obj["name"] = string.IsNullOrWhiteSpace(PropertyName.Text) ? ObjectName(obj,selected.Kind) : PropertyName.Text.Trim();
            SaveObjectLabelProperties(obj);
            dirty = true; RenderPlan(); Objects.SelectedItem = Objects.Items.Cast<ObjectEntry>().FirstOrDefault(item => ReferenceEquals(item.Item, obj)); Status.Text = L("Изменения применены. Сохраните проект.");
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте свойства"), MessageBoxButton.OK, MessageBoxImage.Warning); }
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
