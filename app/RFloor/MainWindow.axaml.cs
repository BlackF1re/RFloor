using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow : SurfaceWindow
{
    private const double Scale = 70;
    private ScaleTransform PlanZoom => (ScaleTransform)ZoomHost.LayoutTransform!;
    private double origin = 50;
    private JsonObject document = NewDocument();
    private string? path;
    private bool dirty;
    private bool updating;
    private string tool = "select";
    private Point? drawingStart;
    private Point? drawingEnd;
    private Point? dragStart;
    private ObjectEntry? dragItem;
    private Vector dragDelta;
    private readonly Stack<string> undo = new();
    private readonly Stack<string> redo = new();
    private JsonObject Geometry => document["geometry"]!.AsObject();
    private JsonArray Rooms => Geometry["rooms"]!.AsArray();
    private JsonArray Walls => Geometry["walls"]!.AsArray();
    private JsonArray Doors => Geometry["doors"]!.AsArray();
    private sealed record ObjectEntry(string Label, JsonObject Item, string Kind);

    public MainWindow() { Localization.SetLanguage(Localization.Language); InitializeComponent(); RouterToolImage.Source=RouterToolbarImage; IsInitialized=true; LoadDocumentSettings(); RenderPlan(); SetTool("select"); StartNetworkStatus(); InitializeWorkspace(); InitializeInput(); InitializeResponsiveLayout(); }
    private static JsonObject NewDocument() => new()
    {
        ["format"] = "rfloor.plan", ["schema"] = 1,
        ["createdAt"] = DateTime.UtcNow.ToString("O"), ["name"] = L("Новый план"),
        ["geometry"] = new JsonObject { ["rooms"] = new JsonArray(), ["walls"] = new JsonArray(), ["doors"] = new JsonArray() },
        ["settings"] = DefaultSurveySettings(), ["points"] = new JsonArray()
    };
    private static double Number(JsonNode? node) => Numeric(node, out double value) ? value : throw new FormatException(L("Отсутствуют координаты объекта."));
    private static string Id() => Guid.NewGuid().ToString();
    private static JsonObject Point(double x, double y) => new() { ["x"] = x, ["y"] = y };
    private static double Parse(TextBox field, string label, bool positive = false)
    {
        var value = field.Text.Trim().Replace(',', '.');
        if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result) || !double.IsFinite(result) || Math.Abs(result) >= 10000 || (positive && result <= 0))
            throw new FormatException(L("Проверьте поле «{0}».", label));
        return result;
    }
    private void AddRoom_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            double x = Parse(RoomX, "X"), y = Parse(RoomY, "Y"), w = Parse(RoomWidth, L("Ширина"), true), h = Parse(RoomHeight, L("Высота"), true), t = Parse(Thickness, L("Толщина стен"), true);
            if (w < .2 || h < .2 || t < .05 || t > 1 || Math.Abs(x+w) >= 10000 || Math.Abs(y+h) >= 10000)
                throw new FormatException(L("Размер комнаты должен быть не меньше 0,2 м, толщина стены — от 0,05 до 1 м."));
            EnsureRoomFree(x, y, w, h, null);
            Remember();
            var id = Id();
            Rooms.Add(new JsonObject { ["id"] = id, ["name"] = string.IsNullOrWhiteSpace(RoomName.Text) ? L("Комната") : RoomName.Text.Trim(), ["x"] = x, ["y"] = y, ["w"] = w, ["h"] = h });
            AddWall(id, x, y, x+w, y, t); AddWall(id, x+w, y, x+w, y+h, t);
            AddWall(id, x+w, y+h, x, y+h, t); AddWall(id, x, y+h, x, y, t);
            dirty = true; RenderPlan(); Status.Text = L("Комната добавлена. Сохраните изменения.");
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте размеры"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void AddWall(string roomId, double ax, double ay, double bx, double by, double thickness)
    {
        foreach (var (a, b) in UncoveredSegments(new Point(ax, ay), new Point(bx, by)))
            Walls.Add(new JsonObject { ["id"] = Id(), ["roomId"] = roomId, ["a"] = Point(a.X, a.Y), ["b"] = Point(b.X, b.Y), ["thickness"] = thickness });
    }
    private static void Validate(JsonObject doc)
    {
        var format = doc["format"]?.GetValue<string>();
        if ((format != "rfloor.plan" && format != "rfloor.survey") || doc["schema"]?.GetValue<int>() != 1 || doc["name"] is not JsonValue || doc["geometry"] is not JsonObject geometry)
            throw new FormatException(L("Неизвестный формат или версия файла RFloor."));
        foreach (var key in new[] { "rooms", "walls", "doors" }) if (geometry[key] is not JsonArray) throw new FormatException(L("В файле отсутствует геометрия плана."));
        if (format == "rfloor.survey" && (doc["points"] is not JsonArray || doc["settings"] is not JsonObject)) throw new FormatException(L("В файле отсутствуют данные измерений."));
        if (geometry["rooms"]!.AsArray().Count > 500 || geometry["walls"]!.AsArray().Count > 3000 || geometry["doors"]!.AsArray().Count > 3000 || (doc["points"] as JsonArray)?.Count > 2000) throw new FormatException(L("Проект слишком большой."));
        if (doc["points"] is JsonArray points) foreach (var node in points)
        {
            double px = Number(node?["x"]), py = Number(node?["y"]);
            if (!double.IsFinite(px) || !double.IsFinite(py) || Math.Abs(px)>=10000 || Math.Abs(py)>=10000 || node?["band"] is not JsonValue || !node["band"]!.AsValue().TryGetValue<string>(out _)) throw new FormatException(L("Некорректные данные точки замера."));
        }
        foreach (var node in geometry["rooms"]!.AsArray())
        {
            var room = node!.AsObject(); double x=Number(room["x"]), y=Number(room["y"]), w=Number(room["w"]), h=Number(room["h"]);
            if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h) || w<=0 || h<=0 || Math.Abs(x)>=10000 || Math.Abs(y)>=10000 || w>=10000 || h>=10000) throw new FormatException(L("Некорректные размеры комнаты."));
        }
        foreach (var key in new[] { "walls", "doors" }) foreach (var node in geometry[key]!.AsArray()) foreach (var endpoint in new[] { "a", "b" }) foreach (var axis in new[] { "x", "y" })
        {
            double value = Number(node![endpoint]![axis]);
            if (!double.IsFinite(value) || Math.Abs(value)>=10000) throw new FormatException(L("Некорректные координаты объекта."));
        }
    }
    private async Task<bool> ConfirmDiscard()
    {
        if (!dirty) return true;
        var answer = await MessageBox.Ask(this, L("Сохранить изменения перед закрытием плана?"), L("Несохранённые изменения"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (answer == MessageBoxResult.Cancel) return false;
        return answer == MessageBoxResult.No || await Save();
    }
    private void New_Click(object sender, RoutedEventArgs e) { if (measurementCancellation is null) CreateNamedPlan(); }
    private async void Open_Click(object sender,RoutedEventArgs e) => await OpenProject();
    private async Task OpenProject()
    {
        if(measurementCancellation is not null)return;
        var storage=TopLevel.GetTopLevel(this)!.StorageProvider;
        var files=await storage.OpenFilePickerAsync(new FilePickerOpenOptions {Title=L("Открыть"),AllowMultiple=false,FileTypeFilter=[PlanFileType]});
        if(files.Count==0)return;
        try {
            var file=files[0];
            await using var input=await file.OpenReadAsync();
            if(input.CanSeek && input.Length>8_000_000)throw new FormatException(L("Файл слишком велик."));
            using var reader=new StreamReader(input);string json=await reader.ReadToEndAsync();
            if(json.Length>8_000_000)throw new FormatException(L("Файл слишком велик."));
            var loaded=JsonNode.Parse(json)?.AsObject()??throw new FormatException(L("Пустой файл."));Validate(loaded);NormalizeProject(loaded);
            storageFiles[file.Path.ToString()]=file;AddDocument(loaded,file.Path.ToString());Fit_Click(this,new());
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or FormatException or InvalidOperationException or NullReferenceException) {MessageBox.Show(this,ex.Message,"RFloor");}
    }
    private static readonly FilePickerFileType PlanFileType=new("RFloor") {Patterns=["*.rfplan"],MimeTypes=["application/json","application/octet-stream"],AppleUniformTypeIdentifiers=["public.json"]};
    private readonly Dictionary<string,IStorageFile> storageFiles=[];
    private async Task<bool> Save(bool saveAs=false)
    {
        if(activeSession is null)return false;CommitInlineRename();if(!CommitDocumentSettings())return false;
        try {
            IStorageFile? file=path is not null && !saveAs?storageFiles.GetValueOrDefault(path):null;
            file??=await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {Title=L("Сохранить"),SuggestedFileName=SafePlanName(),DefaultExtension="rfplan",FileTypeChoices=[PlanFileType],ShowOverwritePrompt=true});
            if(file is null)return false;
            string local=file.TryGetLocalPath()??"";string json=document.ToJsonString(new JsonSerializerOptions {WriteIndented=true})+Environment.NewLine;
            if(local.Length>0) {string temp=local+"."+Guid.NewGuid().ToString("N")+".tmp";try {await File.WriteAllTextAsync(temp,json);File.Move(temp,local,true);}finally {if(File.Exists(temp))File.Delete(temp);}}
            else {await using var output=await file.OpenWriteAsync();if(output.CanSeek)output.SetLength(0);await using var writer=new StreamWriter(output);await writer.WriteAsync(json);}
            path=file.Path.ToString();storageFiles[path]=file;dirty=false;RefreshTabHeaders();Status.Text=L("Сохранено: {0}",file.Name);return true;
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){MessageBox.Show(this,ex.Message,L("Не удалось сохранить"));return false;}
    }
    private async void Save_Click(object sender,RoutedEventArgs e)=>await Save();
    private async void SaveAs_Click(object sender,RoutedEventArgs e)=>await Save(true);
    public async Task<bool> RequestClose()
    {
        if(measurementCancellation is not null){measurementCancellation.Cancel();return false;}
        CommitInlineRename();CaptureSession();foreach(var session in sessions.ToArray()){ActivateSession(session);if(!await ConfirmDiscard())return false;}
        EndPan();networkTimer.Stop();trayIcon?.Dispose();return true;
    }
    private double Screen(double value) => origin + value * Scale;
    private void RenderPlan()
    {
        CommitInlineRename();
        wallFaces=null;
        openingFaces=null;
        roomJunctionWalls=null;
        foreach (var (items, prefix) in new[] { (Rooms, L("Комната")), (Walls, L("Стена")), (Doors, L("Проём")), (Points, L("Точка")) })
            for (int i=0; i<items.Count; i++)
                if (string.IsNullOrWhiteSpace(items[i]?["name"]?.GetValue<string>())) items[i]!["name"] = $"{prefix} {i+1}";
        var previousSelection = (Objects.SelectedItem as ObjectEntry)?.Item;
        var selectedIds = Objects.SelectedItems.Cast<ObjectEntry>().Select(e=>e.Item).ToList();
        updating = true; Objects.Items.Clear(); Plan.Children.Clear();
        UpdateEmptyPlan();
        if (drawingStart is null && dragStart is null)
        {
            double min = 0, maxX = 0, maxY = 0;
            void Include(double x, double y) { min = Math.Min(min, Math.Min(x,y)); maxX = Math.Max(maxX,x); maxY = Math.Max(maxY,y); }
            foreach (var node in Rooms) { var room = node!.AsObject(); double x=Number(room["x"]), y=Number(room["y"]); Include(x,y); Include(x+Number(room["w"]),y+Number(room["h"])); }
            foreach (var node in Walls.Concat(Doors).Concat(Tapes)) foreach (var endpoint in new[] { "a", "b" }) Include(Number(node![endpoint]!["x"]), Number(node[endpoint]!["y"]));
            foreach(var obj in Decorations.Select(n=>n!.AsObject())){var bounds=DecorationBounds(obj);Include(bounds.Left,bounds.Top);Include(bounds.Right,bounds.Bottom);}
            foreach (var node in Points) Include(Number(node!["x"]), Number(node["y"]));
            double nextOrigin = 50-Math.Min(0,min)*Scale, shift = nextOrigin-origin; origin = nextOrigin;
            Plan.Width = Math.Max(2400,Screen(maxX)+250); Plan.Height = Math.Max(1800,Screen(maxY)+250);
            if (Math.Abs(shift)>1e-8 && Viewport.IsLoaded) { Viewport.ScrollToHorizontalOffset(Math.Max(0,Viewport.HorizontalOffset+shift*PlanZoom.ScaleX)); Viewport.ScrollToVerticalOffset(Math.Max(0,Viewport.VerticalOffset+shift*PlanZoom.ScaleY)); }
        }
        double step=GridSize*Scale;
        double half=step/2, phase=(origin+half)%step;
        var grid = new DrawingBrush {TileMode=TileMode.Tile,DestinationRect=new RelativeRect(phase,phase,step,step,RelativeUnit.Absolute),SourceRect=new RelativeRect(0,0,step,step,RelativeUnit.Absolute),
            Drawing=gridView==1?new GeometryDrawing {Brush=Brushes.Gray,Geometry=new EllipseGeometry(new Rect(half-.7,half-.7,1.4,1.4))}:new GeometryDrawing {Pen=new Pen(Brushes.LightGray,.5),Geometry=Avalonia.Media.Geometry.Parse(FormattableString.Invariant($"M {half},0 L {half},{step} M 0,{half} L {step},{half}"))}};
        Plan.Background = showGrid && !surveyMode ? grid : Brushes.White;
        foreach (var node in Rooms)
        {
            var room = node!.AsObject(); double x=Number(room["x"]), y=Number(room["y"]), w=Number(room["w"]), h=Number(room["h"]);
            var rect = new Rectangle { Width=w*Scale, Height=h*Scale, Fill=Brushes.Transparent, Tag=room, IsHitTestVisible=false };
            Canvas.SetLeft(rect, Screen(x)); Canvas.SetTop(rect, Screen(y)); Plan.Children.Add(rect);
            var name = room["name"]?.GetValue<string>() ?? L("Комната");

            if (!surveyMode) Objects.Items.Add(new ObjectEntry(L("Комната · {0}", name), room, "room"));
        }
        if (surveyMode) {
            UpdateAccessPoints();
            RenderHeatmap();
            if (showGrid) Plan.Children.Add(new Rectangle {Width=Plan.Width,Height=Plan.Height,Fill=grid,IsHitTestVisible=false});
        }
        DrawWallMaterial();
        foreach (var node in Walls) { var wall=node!.AsObject(); if (!surveyMode) Objects.Items.Add(new ObjectEntry(L("{0} · {1:0.##} м", ObjectName(wall,L("Стена")), Length(wall)), wall, "wall")); }
        foreach (var node in Doors) { var door=node!.AsObject(); DrawOpening(door); if (!surveyMode) Objects.Items.Add(new ObjectEntry(L("{0} · {1:0.##} м", ObjectName(door,L("Проём")), Length(door)), door, "door")); }
        if (surveyMode) RenderPoints();
        DrawConstructionPreview();
        DrawDecorations();
        DrawRouterPreview();
        DrawTouchAnchor();
        DrawObjectLabels();
        DrawEditorOverlay();
        updating = false;
        Objects.SelectedItem = Objects.Items.Cast<ObjectEntry>().FirstOrDefault(entry => ReferenceEquals(entry.Item, previousSelection));
        foreach(var entry in Objects.Items.Cast<ObjectEntry>().Where(e=>selectedIds.Contains(e.Item))) if (!Objects.SelectedItems.Contains(entry)) Objects.SelectedItems.Add(entry);
        Objects_SelectionChanged(Objects, null!);
        RefreshObjectSearch();
        DrawPointPreview();
        RefreshTabHeaders();
        RefreshRulers();
        UpdateCoverageHover();
    }
    private static double Length(JsonObject obj) { double dx=Number(obj["b"]!["x"])-Number(obj["a"]!["x"]), dy=Number(obj["b"]!["y"])-Number(obj["a"]!["y"]); return Math.Sqrt(dx*dx+dy*dy); }
    private void DrawSegment(JsonObject obj, IBrush color, double thickness, bool dashed = false)
    {
        var line = new Line { StrokeLineCap=PenLineCap.Flat, X1=Screen(Number(obj["a"]!["x"])), Y1=Screen(Number(obj["a"]!["y"])), X2=Screen(Number(obj["b"]!["x"])), Y2=Screen(Number(obj["b"]!["y"])), Stroke=color, StrokeThickness=thickness };
        if (dashed) line.StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 3, 2 }; Plan.Children.Add(line);
    }
    private void AddLabel(string text, double x, double y) { var label=new TextBlock { Text=text, FontSize=12, Background=new SolidColorBrush(Color.FromArgb(190,255,255,255)), Opacity=labelOpacity, Padding=new Thickness(4), IsHitTestVisible=false }; Canvas.SetLeft(label,x); Canvas.SetTop(label,y); Ui.SetZIndex(label,100); Plan.Children.Add(label); }
    private void Objects_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (updating) return;
        RefreshSelectionHighlight();
        DeleteButton.IsEnabled = Objects.SelectedItem is ObjectEntry;
        if (Objects.SelectedItems.Count>1) { PropertiesEditor.IsVisible=false; SelectionInfo.Text=surveyMode ? L("Выбрано точек: {0}", Objects.SelectedItems.Count) : L("Выбрано объектов: {0}. Ctrl — добавить или убрать объект; Shift и протягивание — рамка.", Objects.SelectedItems.Count); return; }
        if (Objects.SelectedItem is not ObjectEntry selected) { PropertiesEditor.IsVisible = false; SelectionInfo.Text = surveyMode ? L("Выберите точку для просмотра результатов") : L("Выберите объект для просмотра размеров"); return; }
        ShowProperties(selected);
        var obj = selected.Item;
        if (selected.Kind == "point") { SelectionInfo.Text = DescribePoint(obj); return; }
        if(selected.Kind=="decoration"){SelectionInfo.Text=L("Графический ориентир или подпись");return;}
        SelectionInfo.Text = selected.Kind == "room" ? L("Площадь: {0:0.##} м²", Number(obj["w"])*Number(obj["h"])) : L("Длина: {0:0.##} м", Length(obj));
    }
    private void Remember() { undo.Push(document.ToJsonString()); redo.Clear(); }
    private void Restore(Stack<string> source, Stack<string> destination)
    {
        if(measurementCancellation is not null)return;
        CommitInlineRename();ResetPointerTools();
        if (source.Count == 0) return;
        destination.Push(document.ToJsonString());
        document = JsonNode.Parse(source.Pop())!.AsObject(); dirty = true; LoadDocumentSettings(); RenderPlan();
    }
    private void Undo_Click(object sender, RoutedEventArgs e) => Restore(undo, redo);
    private void Redo_Click(object sender, RoutedEventArgs e) => Restore(redo, undo);
    private void SetTool(string value)
    {
        if (measurementCancellation is not null) return;
        CommitInlineRename(); ResetPointerTools(); if (surveyMode) SwitchMode(false); tool = value;
        foreach (var button in new[] { SelectTool, RoomTool, WallTool, DoorTool, TapeTool, RectangleTool,RoundedRectangleTool,RouterTool, EllipseTool, LineTool, TextTool })
        {
            bool active = (string)button.Tag == value;
            button.Background = new SolidColorBrush((Color)Color.Parse(active ? "#FFE5CC" : "#F2F2F2"));
        }
        ToolHint.Text = value switch { "rect" or "roundRect" or "ellipse" or "line" => L("ЛКМ — с заливкой, ПКМ — без заливки. Протяните ориентир на плане."), "text"=>L("Укажите место подписи и введите текст. Enter — сохранить."), "room" => L("Протяните прямоугольник на плане или введите размеры ниже."), "wall" => L("Протяните стену между двумя точками."), "door" => L("Протяните проём вдоль стены от начала до конца."), "tape" => L("Отметьте начало и конец. ПКМ по рулетке удаляет её."), _ => "" };
        RenderPlan();
    }
    private void Tool_Click(object sender, RoutedEventArgs e) => SetTool((string)((Button)sender).Tag);
    private void Zoom(double factor)
    {
        Point cursor = PointerContext.LastPosition;
        if (cursor.X<0 || cursor.Y<0 || cursor.X>Viewport.ViewportWidth || cursor.Y>Viewport.ViewportHeight) cursor=new Point(Viewport.ViewportWidth/2,Viewport.ViewportHeight/2);
        ZoomAt(factor,cursor);
    }
    private void ZoomAt(double factor, Point cursor, Point? destination = null)
    {
        double old=PlanZoom.ScaleX, next=Math.Clamp(old*factor,.4,5);
        if (next==old && destination is null) return;
        Point target=destination ?? cursor;
        var padding=PlanSurface.Padding;
        double x=(Viewport.HorizontalOffset+cursor.X-padding.Left)/old;
        double y=(Viewport.VerticalOffset+cursor.Y-padding.Top)/old;
        // Leave room to scroll on either side, including when zooming out at zero offset.
        double left=Math.Max(padding.Left,target.X-x*next), top=Math.Max(padding.Top,target.Y-y*next);
        PlanSurface.Padding=new Thickness(left,top,Viewport.ViewportWidth,Viewport.ViewportHeight);
        PlanZoom.ScaleX=PlanZoom.ScaleY=next; Viewport.UpdateLayout();
        Viewport.ScrollToHorizontalOffset(x*next+left-target.X); Viewport.ScrollToVerticalOffset(y*next+top-target.Y);
        ZoomLabel.Text=$"{next*100:0} %"; RefreshRulers(); UpdateCoverageHover();
        if(pointPreview is not null)DrawPointPreview();
    }
    private void ZoomIn_Click(object sender, RoutedEventArgs e) => Zoom(zoomFactor);
    private void ZoomOut_Click(object sender, RoutedEventArgs e) => Zoom(1/zoomFactor);
    private Point World(Point point, bool snap = true)
    {
        var p = new Point((point.X-origin)/Scale, (point.Y-origin)/Scale);
        if (!snap || !SnapActive || tool == "room") return p;
        if (tool == "router") return SnapRouterPosition(p);
        if(SurfaceSnap(p) is Point surface)return surface;
        return GridPosition(p);
    }
    private async void Plan_MouseDown(object sender, PointerPressedEventArgs e)
    {
        if(e.Button()!=MouseButton.Left && (e.Button()!=MouseButton.Right || surveyMode || tool is not ("rect" or "roundRect" or "ellipse" or "line")))return;
        if(e.Pointer.Type==PointerType.Touch)return;
        if (inlineNameEditor is not null) { CommitInlineRename(); e.Handled=true; return; }
        if (!surveyMode && tool == "tape") return;
        var local = e.GetPosition(Plan);
        if (surveyMode) {
            if (measurementCancellation is not null) return;
            double radius=8/(Scale*PlanZoom.ScaleX);
            if(surveyTool==SurveyTool.Add) {
                placingSurveyPoint=true;
                surveyPlacementOrigin=e.GetPosition(Viewport);
                pointPreview=SurveyPosition(local);
                Plan.CapturePointer();
                DrawPointPreview();
                e.Handled=true;
                return;
            }
            if (surveyTool == SurveyTool.Select && HitObject(World(local,false),radius) is null) {
                marqueeStart=marqueeEnd=World(local,false);
                Plan.CapturePointer();
                return;
            }
            if (surveyTool != SurveyTool.None) {
                e.Handled=true;
                await SurveyClick(local,radius);
                return;
            }
        }
        if (surveyMode || tool == "select")
        {
            if (PointerContext.Modifiers.HasFlag(KeyModifiers.Shift)) { marqueeStart=marqueeEnd=World(local,false); Plan.CapturePointer(); return; }
            SelectAt(World(local, false));
            if (e.Clicks()==2 && Objects.SelectedItem is not null) { Rename_Click(sender,e); return; }
            if (PointerContext.Modifiers.HasFlag(KeyModifiers.Control)) return;
            if (Objects.SelectedItem is null) { marqueeStart=marqueeEnd=World(local,false); Plan.CapturePointer(); return; }
            if (Objects.SelectedItem is ObjectEntry selected) { dragItem = selected; dragStart = World(local, false); Plan.CapturePointer(); }
            return;
        }
        var point = World(local);
        if(tool=="router"){AddRouter(point);return;}

        drawingFilled=e.Button()==MouseButton.Left;drawingButton=e.Button();e.Handled=true;
        drawingStart = drawingEnd = point; Plan.CapturePointer(); DrawConstructionPreview();
    }
    private void Plan_MouseMove(object sender, PointerEventArgs e)
    {
        if(e.Pointer.Type==PointerType.Touch)return;
        if(!surveyMode && tool=="router") {var point=World(e.GetPosition(Plan));if(routerPreview!=point){routerPreview=point;DrawRouterPreview();}return;}
        if(surveyMode && surveyTool == SurveyTool.Add && measurementCancellation is null) {var point=SurveyPosition(e.GetPosition(Plan));if(pointPreview!=point){pointPreview=point;DrawPointPreview();}return;}
        if (marqueeStart is not null && Plan.IsPointerCaptured()) { marqueeEnd=World(e.GetPosition(Plan),false); DrawMarquee(); return; }
        if (dragStart is Point from && dragItem is ObjectEntry item && Plan.IsPointerCaptured())
        {
            var delta=AdjustDrag(item,World(e.GetPosition(Plan),false)-from);
            if(dragPreview.Count>0 && dragDelta==delta)return;
            dragDelta=delta;
            Status.Text=DrawDragPreview() ? L("Отпустите кнопку мыши, чтобы переместить объект.") : L("Здесь объект пересекается с другим или выходит за допустимые границы.");
            return;
        }
        if (!surveyMode && tool == "tape") return;
        if (drawingStart is null || !Plan.IsPointerCaptured()) return;
        var end=World(e.GetPosition(Plan));
        if(drawingEnd!=end){drawingEnd=end;DrawConstructionPreview();}
    }
    private async void Plan_MouseUp(object sender, PointerReleasedEventArgs e)
    {
        if(e.Pointer.Type==PointerType.Touch)return;
        if(e.Button()!=MouseButton.Left && (drawingStart is null || e.Button()!=drawingButton))return;
        if(placingSurveyPoint) {
            placingSurveyPoint=false;
            Plan.ReleasePointer();
            var local=e.GetPosition(Plan);
            pointPreview=null;
            DrawPointPreview();
            e.Handled=true;
            var viewportPoint=e.GetPosition(Viewport);
            await FinishSurveyPlacement(local,viewportPoint,8,((Vector)(viewportPoint-surveyPlacementOrigin)).Length>=4);
            return;
        }
        if(drawingStart is not null && e.Button()!=drawingButton)return;
        if (marqueeStart is Point first) { Plan.ReleasePointer(); SelectMarquee(first,World(e.GetPosition(Plan),false)); return; }
        if (dragStart is Point from && dragItem is ObjectEntry item && Plan.IsPointerCaptured())
        {
            Point to = World(e.GetPosition(Plan), false); var delta = AdjustDrag(item, to-from); bool valid = CanMove(item, delta); Plan.ReleasePointer(); dragStart = null; dragItem = null; dragDelta = new Vector();
            if (((Vector)(to-from)).Length * Scale * PlanZoom.ScaleX >= 4 && delta.Length>=1e-8 && valid) MoveSelected(item, delta);
            else if(dragPreview.Count>0) RenderPlan();
            else if(((Vector)(to-from)).Length*Scale*PlanZoom.ScaleX<4 && Objects.SelectedItems.Count==1)
                ShowTouchCard(item,e.GetPosition(Viewport));
            return;
        }
        if (drawingStart is not Point start || !Plan.IsPointerCaptured()) return;
        e.Handled=true;
        var end = World(e.GetPosition(Plan)); Plan.ReleasePointer(); FinishDrawing(start,end);
    }
    private void FinishDrawing(Point start,Point end)
    {
        drawingStart = drawingEnd = null;
        ClearConstructionPreview();
        if (tool == "room") (start, end) = SnapRoomCorners(start, end);
        if(tool is "wall" or "line")(start,end)=AlignConstructionLine(start,end,tool=="wall"?WallStroke:2);
        if(tool is "rect" or "roundRect" or "ellipse" or "line" or "text"){AddDecoration(start,end);if(tool!="text")RenderPlan();return;}
        if(tool=="door"){CutDrawnOpening(start,end);RenderPlan();return;}
        try
        {
            double t = Parse(Thickness, L("Толщина стен"), true);
            if (t < .05 || t > 1) throw new FormatException(L("Толщина стены должна быть от 0,05 до 1 м."));
            if (tool == "room" && Math.Abs(end.X-start.X) >= .2 && Math.Abs(end.Y-start.Y) >= .2)
            {
                double x=Math.Min(start.X,end.X), y=Math.Min(start.Y,end.Y), w=Math.Abs(end.X-start.X), h=Math.Abs(end.Y-start.Y); var id=Id();
                EnsureRoomFree(x, y, w, h, null);
                Remember(); Rooms.Add(new JsonObject { ["id"]=id, ["name"]=L("Комната"), ["x"]=x, ["y"]=y, ["w"]=w, ["h"]=h });
                AddWall(id,x,y,x+w,y,t); AddWall(id,x+w,y,x+w,y+h,t); AddWall(id,x+w,y+h,x,y+h,t); AddWall(id,x,y+h,x,y,t); dirty=true;
            }
            else if (tool == "wall" && ((Vector)(end-start)).Length >= .15)
            { EnsureSegmentFree(start, end, null); Remember(); Walls.Add(new JsonObject { ["id"]=Id(), ["a"]=Point(start.X,start.Y), ["b"]=Point(end.X,end.Y), ["thickness"]=t }); dirty=true; }
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте размеры"), MessageBoxButton.OK, MessageBoxImage.Warning); }
        RenderPlan();
    }
    private static (double Distance, double T) SegmentDistance(Point p, JsonObject segment)
    {
        var a=new Point(Number(segment["a"]!["x"]),Number(segment["a"]!["y"])); var b=new Point(Number(segment["b"]!["x"]),Number(segment["b"]!["y"]));
        Vector d=b-a; double t=d.SquaredLength < 1e-12 ? 0 : Math.Clamp(Vectors.Dot(p-a,d)/d.SquaredLength,0,1);
        return (((Vector)(p-(a+d*t))).Length,t);
    }
    private ObjectEntry? HitObject(Point point, double radius)
    {
        ObjectEntry? best=null; double distance=radius;
        foreach (ObjectEntry entry in Objects.Items)
        {
            if (entry.Kind is "room" or "decoration") continue;
            var d=entry.Kind == "point"
                ? ((Vector)(point - new Point(Number(entry.Item["x"]), Number(entry.Item["y"])))).Length
                : SegmentDistance(point,entry.Item).Distance;
            if (d < distance) { distance=d; best=entry; }
        }
        if(!surveyMode)foreach(var entry in Objects.Items.Cast<ObjectEntry>().Where(e=>e.Kind=="decoration").Reverse())if(DecorationContains(entry.Item,point)){best=entry;break;}
        if (best is null) foreach (ObjectEntry entry in Objects.Items)
        {
            if (entry.Kind != "room") continue;
            var r=entry.Item; double x=Number(r["x"]), y=Number(r["y"]);
            if (point.X>=x && point.X<=x+Number(r["w"]) && point.Y>=y && point.Y<=y+Number(r["h"])) best=entry;
        }
        return best;
    }
    private void SelectAt(Point point)
    {
        var best=HitObject(point,.2);
        if (PointerContext.Modifiers.HasFlag(KeyModifiers.Control) && best is not null) { if(Objects.SelectedItems.Contains(best))Objects.SelectedItems.Remove(best);else Objects.SelectedItems.Add(best); } else Objects.SelectedItem=best;
    }
    private void CutOpening(Point point)
    {
        try
        {
            double width=Parse(DoorWidth,L("Ширина проёма"),true);
            if (width < .4 || width > 2.5) throw new FormatException(L("Ширина проёма должна быть от 0,4 до 2,5 м."));
            JsonObject? selected=null; double nearest=.25, t=0;
            foreach (var node in Walls) { var wall=node!.AsObject(); var hit=SegmentDistance(point,wall); if (hit.Distance < nearest) { nearest=hit.Distance; selected=wall; t=hit.T; } }
            if (selected is null) { Status.Text=L("Нажмите ближе к стене."); return; }
            double length=Length(selected), margin=.08;
            var (before,after)=OpeningBay(selected,t);
            if ((after-before)*length < width+2*margin) { Status.Text=L("На выбранном участке стены недостаточно места для проёма."); return; }
            double half=width/(2*length), center=Math.Clamp(t,before+margin/length+half,after-margin/length-half);
            double ax=Number(selected["a"]!["x"]), ay=Number(selected["a"]!["y"]), bx=Number(selected["b"]!["x"]), by=Number(selected["b"]!["y"]);
            JsonObject At(double n) => Point(ax+(bx-ax)*n,ay+(by-ay)*n);
            Remember(); var first=selected.DeepClone().AsObject(); var second=selected.DeepClone().AsObject();
            first["id"]=Id(); first["b"]=At(center-half); second["id"]=Id(); second["a"]=At(center+half);
            var opening=new JsonObject { ["id"]=Id(), ["a"]=At(center-half), ["b"]=At(center+half), ["thickness"]=selected["thickness"]?.DeepClone() };
            if (selected["roomId"] is not null) opening["roomId"]=selected["roomId"]!.DeepClone();
            Walls.Remove(selected); Walls.Add(first); Walls.Add(second); Doors.Add(opening); dirty=true; RenderPlan(); Status.Text=L("Проём добавлен.");
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте размеры"), MessageBoxButton.OK, MessageBoxImage.Warning); }
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Objects.SelectedItem is not ObjectEntry selected) return;
        var obj=selected.Item;
        Remember();
        if (selected.Kind == "point") { document["points"]!.AsArray().Remove(obj); }
        else if (selected.Kind == "room")
        {
            RetainSharedSegments(obj);
            var id=obj["id"]?.GetValue<string>();
            foreach (var array in new[] { Walls, Doors }) for (int i=array.Count-1;i>=0;i--) if (id is not null && array[i]?["roomId"]?.GetValue<string>()==id) array.RemoveAt(i);
            Rooms.Remove(obj);
        }
        else if(selected.Kind=="decoration") Decorations.Remove(obj);
        else if (selected.Kind == "wall") Walls.Remove(obj);
        else { var wall=obj.DeepClone().AsObject(); wall["id"]=Id(); Walls.Add(wall); Doors.Remove(obj); }
        dirty=true; RenderPlan();
    }
    private void Window_KeyDown(object sender, KeyEventArgs e) {PointerContext.Modifiers=e.KeyModifiers;HandleShortcut(e);}

}

