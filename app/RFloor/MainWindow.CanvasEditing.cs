using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private double labelOpacity = .85;
    private TextBox? inlineNameEditor;
    private ObjectEntry? inlineNameTarget;
    private const string SearchHighlightTag="search-highlight";

    private void ObjectSearch_Changed(object? sender, TextChangedEventArgs e)
    {
        if (IsInitialized && !switchingSession) RefreshObjectSearch();
    }

    private void ClearObjectSearch_Click(object sender, RoutedEventArgs e)
    {
        ObjectSearch.Clear();
        ObjectSearch.Focus();
    }

    private void RefreshObjectSearch()
    {
        foreach (var element in Plan.Children.OfType<Control>().Where(c=>Equals(c.Tag,SearchHighlightTag)).ToArray())
            Plan.Children.Remove(element);
        string query=ObjectSearch.Text?.Trim()??"";
        ClearObjectSearch.IsEnabled=!string.IsNullOrEmpty(ObjectSearch.Text);
        if (query.Length==0) return;
        foreach (var entry in Objects.Items.OfType<ObjectEntry>())
            if (ObjectName(entry.Item,entry.Label).Contains(query,StringComparison.CurrentCultureIgnoreCase))
                DrawObjectShape(entry,default,.72,Brushes.DarkOrange,SearchHighlightTag);
    }

    private void ResetPointerTools(bool keepSurveyTool = false)
    {
        CancelTouchGesture();
        touchDrawingAnchor=null;
        DrawTouchAnchor();
        HideTouchCard();
        if (Plan.IsPointerCaptured()) Plan.ReleasePointer();
        tapeStart=tapeCursor=routerPreview=drawingStart=drawingEnd=dragStart=marqueeStart=marqueeEnd=null;
        dragItem=null; dragDelta=new Vector(); pointPreview=null;
        placingSurveyPoint=false;
        ClearMarquee();
        if (!keepSurveyTool) SetSurveyTool(SurveyTool.None);
    }


    private Point TapeSnap(Point p) => SnapEnabled.IsChecked==true && !PointerContext.Modifiers.HasFlag(KeyModifiers.Shift) ? SurfaceSnap(p)??p : p;

    private void DrawOpening(JsonObject opening)
    {
        var shape=new Avalonia.Controls.Shapes.Path {
            Data=OpeningGeometry(opening),
            Fill=Brushes.Gray, IsHitTestVisible=false,
            Tag=opening };
        // Shared boundaries use identical pixel coverage, without expanding either object.
        RenderOptions.SetEdgeMode(shape,EdgeMode.Aliased);
        Plan.Children.Add(shape);
    }

    // Selection and drag previews use the same geometry as the permanent objects.
    private void DrawObjectShape(ObjectEntry entry,Vector delta,double opacity,IBrush tint,string? tag)
    {
        var obj=entry.Item;
        if(entry.Kind=="decoration"){DrawDecoration(obj,delta,tint,opacity,tag);return;}
        if(entry.Kind=="tape")
        {
            var a=End(obj,"a")+delta;var b=End(obj,"b")+delta;
            Plan.Children.Add(new Line {X1=Screen(a.X),Y1=Screen(a.Y),X2=Screen(b.X),Y2=Screen(b.Y),
                Stroke=tint,Opacity=opacity,StrokeThickness=1,Tag=tag,ZIndex=92,IsHitTestVisible=false});
            return;
        }
        if(entry.Kind=="point") {
            if(ReferenceEquals(animatedPoint,obj))return;
            var position=new Point(Number(obj["x"])+delta.X,Number(obj["y"])+delta.Y);
            bool selected=tag is HighlightTag;
            Plan.Children.Add(CreatePointMarker(position, selected && PointHasFailure(obj,MapReading(obj))?Brushes.Firebrick:tint, tag,
                selected || tag is SearchHighlightTag?SurveyFocusSize:SurveyMarkerSize, selected?1:opacity));
            return;
        }
        var walls=Walls.OfType<JsonObject>().ToList();
        if(entry.Kind=="room") {
            foreach(var wall in walls.Where(w=>BelongsToRoom(w,obj)))
                DrawSegmentHighlight(WallGeometry(wall,walls),delta,opacity,tint,tag);
            foreach(var door in Doors.OfType<JsonObject>().Where(w=>BelongsToRoom(w,obj)))
                DrawSegmentHighlight(OpeningGeometry(door),delta,opacity,tint,tag);
        }
        else DrawSegmentHighlight(entry.Kind=="door"?OpeningGeometry(obj):WallGeometry(obj,walls),delta,opacity,tint,tag);
    }

    private void DrawSegmentHighlight(Geometry geometry, Vector delta, double opacity, IBrush tint, string? tag)
    {
        var shape = new Avalonia.Controls.Shapes.Path {
            Data=geometry, Fill=tint, Opacity=opacity, IsHitTestVisible=false, Tag=tag, ZIndex=92
        };
        if (delta != default) shape.RenderTransform = new TranslateTransform(delta.X*Scale, delta.Y*Scale);
        RenderOptions.SetEdgeMode(shape, EdgeMode.Aliased);
        Plan.Children.Add(shape);
    }

    private void RefreshSelectionHighlight()
    {
        ClearSelectionHighlight();
        var selected=Objects.SelectedItems!.Cast<ObjectEntry>().ToList();
        var chosen=new HashSet<JsonObject>(selected.Select(e=>e.Item),ReferenceEqualityComparer.Instance);
        var rooms=selected.Where(e=>e.Kind=="room").Select(e=>e.Item).ToArray();
        foreach (var entry in Objects.Items.OfType<ObjectEntry>().Where(e=>e.Kind is "wall" or "door"))
            if (rooms.Any(room=>BelongsToRoom(entry.Item,room))) chosen.Add(entry.Item);

        foreach (var entry in Objects.Items.OfType<ObjectEntry>())
            if (entry.Kind!="room" && chosen.Contains(entry.Item))
                DrawObjectShape(entry,default,SelectionOpacity,Brushes.DarkOrange,HighlightTag);

        foreach(var element in Plan.Children.OfType<FrameworkElement>())if(element.Tag is JsonObject obj) {
            if(ReferenceEquals(animatedPoint,obj))continue;
            element.IsVisible=!chosen.Contains(obj) || surveyMode && element is Ellipse;
        }
        foreach(var line in Plan.Children.OfType<Line>().Where(l=>Equals(l.Tag,"tape-overlay") && l.DataContext is JsonObject))
            line.IsVisible=!chosen.Contains((JsonObject)line.DataContext!);
        RefreshSurveyHoverRing();
    }

    private void DrawObjectLabels()
    {
        if (!showObjectLabels || touchLabels && surveyMode) return;
        foreach(var (array,kind) in new[]{(Rooms,"room"),(Walls,"wall"),(Doors,"door"),(Points,"point"),(Decorations,"decoration"),(Tapes,"tape")})
        foreach(var obj in array.Select(n=>n!.AsObject())) {
            if(kind=="decoration" && obj["kind"]?.ToString()=="text")continue;
            if(kind=="point" && !surveyMode)continue;
            if(kind=="tape" && surveyMode)continue;
            var entry=new ObjectEntry(ObjectName(obj,L(LabelRules.First(r=>r.Kind==kind).Title)),obj,kind);
            if(!IsLabelVisible(entry))continue;
            Point position=LabelPosition(entry);
            var label=new Border {Tag=entry,Child=LabelText(entry),CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromArgb(190,255,255,255)),Opacity=ObjectLabelOpacity(obj),Padding=new Thickness(5,3,5,3),Tip=L("Перетащите подпись; двойной щелчок — переименовать")};
            var leader=new Polyline {Stroke=Brushes.DimGray,StrokeThickness=.8,Opacity=label.Opacity,IsHitTestVisible=false,ZIndex=99};
            label.Measure(Size.Infinity);
            void Position(Point at)
            {
                Canvas.SetLeft(label,at.X);Canvas.SetTop(label,at.Y);
                PositionLabelLeader(leader,entry,new Rect(at,label.DesiredSize));
            }
            Point? start=null; Point original=position; bool moved=false;
            label .PointerPressed+=(_,e)=> {
                if(surveyMode || tool!="select" || measurementCancellation is not null || e.Pointer.Type==PointerType.Touch ||
                    !e.GetCurrentPoint(label).Properties.IsLeftButtonPressed)return;
                e.Handled=true;
                Plan.Focus();
                if(e.Clicks()==2){BeginInlineRename(entry);return;}
                start=e.GetPosition(Plan);original=new Point(Canvas.GetLeft(label),Canvas.GetTop(label));moved=false;label.CapturePointer();
            };
            label.PointerMoved+=(_,e)=>{
                if(start is not Point first || !label.IsPointerCaptured())return;
                Vector delta=e.GetPosition(Plan)-first;
                if(!moved && delta.Length*PlanZoom.ScaleX<4)return;
                moved=true;Position(original+delta);e.Handled=true;
            };
            label.PointerReleased+=(_,e)=>{
                if(start is null)return;
                start=null;e.Handled=true;
                if(moved)SaveLabelOffset(obj,new Vector(Canvas.GetLeft(label)-original.X,Canvas.GetTop(label)-original.Y));
                if(Objects.Items.Cast<ObjectEntry>().FirstOrDefault(v=>ReferenceEquals(v.Item,obj)) is {} selected) {
                    Objects.SelectedItem=selected;
                    selectedCallout=obj;
                    if(!moved)ShowTouchCard(selected,e.GetPosition(Viewport));
                }
                label.ReleasePointer();RefreshTabHeaders();
            };
            label.PointerCaptureLost+=(_,_)=>{if(start is not null){Position(original);start=null;}};
            var menu=new ContextMenu();var hide=new MenuItem {Header=L("Скрывать подпись")};hide.Click+=(_,_)=>{Remember();SetObjectLabelVisibility(obj,false);dirty=true;RenderPlan();};menu.Items.Add(hide);label.ContextMenu=menu;
            label.ContextMenu.Opening+=(_,_)=>{ if(tool!="select" || surveyMode)label.ContextMenu.Close(); };
            leader.Tag=new LabelLeader(obj);
            Position(position);Plan.Children.Add(leader);Ui.SetZIndex(label,100);Plan.Children.Add(label);
        }
    }

    private sealed record LabelLeader(JsonObject Item);

    private void SaveLabelOffset(JsonObject obj,Vector pixels)
    {
        if(pixels.Length<1e-8)return;
        Remember();
        var offset=obj["labelOffset"] as JsonObject;
        obj["labelOffset"]=Point((Numeric(offset?["x"],out var x)?x:0)+pixels.X/Scale,(Numeric(offset?["y"],out var y)?y:0)+pixels.Y/Scale);
        dirty=true;
    }

    private void PositionLabelLeader(Polyline leader,ObjectEntry entry,Rect label)
    {
        var obj=entry.Item;
        Point center=label.Center;
        Point Pixels(Point p)=>new(Screen(p.X),Screen(p.Y));
        Point WorldPoint=new((center.X-origin)/Scale,(center.Y-origin)/Scale);
        Point anchor;
        if(IsPlanSegment(entry.Kind))
            anchor=Pixels(OnEdge(WorldPoint,End(obj,"a"),End(obj,"b")))+RoomOutward(obj)*(entry.Kind=="tape"?0:WallStroke/2);
        else if(entry.Kind=="point")
            anchor=Pixels(new Point(Number(obj["x"]),Number(obj["y"])));
        else if(obj["kind"]?.ToString()=="router")
            anchor=Pixels(DecorationBounds(obj).Center);
        else
        {
            var bounds=entry.Kind=="room"
                ?new Rect(Number(obj["x"]),Number(obj["y"]),Number(obj["w"]),Number(obj["h"]))
                :DecorationBounds(obj);
            anchor=Pixels(new[]{OnEdge(WorldPoint,bounds.TopLeft,bounds.TopRight),OnEdge(WorldPoint,bounds.TopRight,bounds.BottomRight),
                OnEdge(WorldPoint,bounds.BottomRight,bounds.BottomLeft),OnEdge(WorldPoint,bounds.BottomLeft,bounds.TopLeft)}
                .MinBy(p=>((Vector)(p-WorldPoint)).SquaredLength));
        }
        Vector delta=anchor-center;
        leader.IsVisible=!label.Contains(anchor) && delta.Length>1e-6;
        if(!leader.IsVisible)return;
        double factor=1/Math.Max(Math.Abs(delta.X)/Math.Max(.01,label.Width/2),Math.Abs(delta.Y)/Math.Max(.01,label.Height/2));
        Point end=center+delta*factor;
        Vector stem=end-anchor;
        double elbow=Math.Min(Math.Abs(stem.X),Math.Abs(stem.Y))*.55;
        Point bend=Math.Abs(stem.X)>Math.Abs(stem.Y)
            ?new Point(end.X-Math.Sign(stem.X)*elbow,anchor.Y)
            :new Point(anchor.X,end.Y-Math.Sign(stem.Y)*elbow);
        if(((Vector)(bend-anchor)).Length<.1 || ((Vector)(bend-end)).Length<.1)
            bend=anchor+stem*.55+(Math.Abs(stem.X)>Math.Abs(stem.Y)?new Vector(0,Math.Min(6,stem.Length*.15)):new Vector(Math.Min(6,stem.Length*.15),0));
        leader.Points=[anchor,bend,end];
    }

    private Point LabelPosition(ObjectEntry entry)
    {
        Point position=IsPlanSegment(entry.Kind)
        ? new Point(Screen((End(entry.Item,"a").X+End(entry.Item,"b").X)/2)+8,Screen((End(entry.Item,"a").Y+End(entry.Item,"b").Y)/2)+8)
        : new Point(Screen(Number(entry.Item["x"]))+8,Screen(Number(entry.Item["y"]))+8);
        if(entry.Item["kind"]?.ToString()=="router")position=new Point(Screen(DecorationBounds(entry.Item).Right)+6,Screen(Number(entry.Item["y"])));
        if(entry.Item["kind"]?.ToString()=="text")position=new Point(Screen(Number(entry.Item["x"])),Screen(Number(entry.Item["y"])));
        if(entry.Item["labelOffset"] is JsonObject offset)position+=new Vector(Number(offset["x"])*Scale,Number(offset["y"])*Scale);
        return position;
    }

    private void BeginInlineRename(ObjectEntry entry)
    {
        CommitInlineRename(); ResetPointerTools();
        inlineNameTarget=entry;
        var field=new TextBox {Text=ObjectName(entry.Item,entry.Label),MinWidth=160,MaxWidth=420,Padding=new Thickness(5),Margin=new Thickness(),Tip=L("Enter — сохранить; Esc — отменить")};
        inlineNameEditor=field; Point position=LabelPosition(entry);
        Canvas.SetLeft(field,position.X);Canvas.SetTop(field,position.Y);Ui.SetZIndex(field,200);
        field.KeyDown+=(_,e)=> { if(e.Key==Key.Enter){CommitInlineRename();e.Handled=true;}else if(e.Key==Key.Escape){EndInlineRename(false);e.Handled=true;} };
        field.LostFocus+=(_,_)=>CommitInlineRename();
        Plan.Children.Add(field); field.BringIntoView();field.Focus();field.SelectAll();
    }
    private void CommitInlineRename() => EndInlineRename(true);
    private void EndInlineRename(bool accept)
    {
        if(inlineNameEditor is not {} field || inlineNameTarget is not {} entry)return;
        string name=field.Text.Trim();bool restoreFocus=field.IsFocused;inlineNameEditor=null;inlineNameTarget=null;
        Plan.Children.Remove(field);
        if(accept && name.Length>0 && name!=ObjectName(entry.Item,entry.Label)) {Remember();entry.Item["name"]=name;entry.Item.Remove("copyBaseName");dirty=true;RenderPlan();}
        if(restoreFocus)Plan.Focus();
    }
}
