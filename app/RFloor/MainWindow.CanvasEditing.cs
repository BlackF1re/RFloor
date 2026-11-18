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
        if(entry.Kind=="point") {
            var position=new Point(Number(obj["x"])+delta.X,Number(obj["y"])+delta.Y);
            Plan.Children.Add(CreatePointMarker(position, tint, tag, tag is HighlightTag or SearchHighlightTag?11:6, opacity));
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
            element.IsVisible=!chosen.Contains(obj) || surveyMode && element is Ellipse;
        }
    }

    private void DrawObjectLabels()
    {
        if (!showObjectLabels || touchLabels) return;
        foreach(var (array,kind) in new[]{(Rooms,"room"),(Walls,"wall"),(Doors,"door"),(Points,"point"),(Decorations,"decoration")})
        foreach(var obj in array.Select(n=>n!.AsObject())) {
            if(kind=="decoration" && obj["kind"]?.ToString()=="text")continue;
            if(kind=="point" && !surveyMode)continue;
            var entry=new ObjectEntry(ObjectName(obj,L(LabelRules.First(r=>r.Kind==kind).Title)),obj,kind);
            if(!IsLabelVisible(entry))continue;
            Point position=LabelPosition(entry);
            var label=new Border {Tag=entry,Child=LabelText(entry),CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromArgb(190,255,255,255)),Opacity=labelOpacity,Padding=new Thickness(5,3,5,3),Tip=L("Перетащите подпись; двойной щелчок — переименовать")};
            Point? start=null; Point original=position; bool moved=false;
            label .PointerPressed+=(_,e)=> {
                if(surveyMode || tool!="select" || measurementCancellation is not null)return;
                e.Handled=true;
                if(e.Clicks()==2){BeginInlineRename(entry);return;}
                start=e.GetPosition(Plan);original=new Point(Canvas.GetLeft(label),Canvas.GetTop(label));moved=false;label.CapturePointer();
            };
            label.PointerMoved+=(_,e)=>{
                if(start is not Point first || !label.IsPointerCaptured())return;
                Vector delta=e.GetPosition(Plan)-first;
                if(!moved && delta.Length*PlanZoom.ScaleX<4)return;
                moved=true;Canvas.SetLeft(label,original.X+delta.X);Canvas.SetTop(label,original.Y+delta.Y);e.Handled=true;
            };
            label.PointerReleased+=(_,e)=>{
                if(start is null)return;
                start=null;e.Handled=true;
                if(moved){Remember();Vector delta=new(Canvas.GetLeft(label)-original.X,Canvas.GetTop(label)-original.Y);
                    var offset=obj["labelOffset"] as JsonObject;
                    obj["labelOffset"]=Point((offset?["x"] is null?0:Number(offset["x"]))+delta.X/Scale,(offset?["y"] is null?0:Number(offset["y"]))+delta.Y/Scale);
                    dirty=true;
                }
                else Objects.SelectedItem=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(v=>ReferenceEquals(v.Item,obj));
                label.ReleasePointer();RefreshTabHeaders();
            };
            label.PointerCaptureLost+=(_,_)=>{if(start is not null){Canvas.SetLeft(label,original.X);Canvas.SetTop(label,original.Y);start=null;}};
            var menu=new ContextMenu();var hide=new MenuItem {Header=L("Скрывать подпись")};hide.Click+=(_,_)=>{Remember();SetObjectLabelVisibility(obj,false);dirty=true;RenderPlan();};menu.Items.Add(hide);label.ContextMenu=menu;
            Canvas.SetLeft(label,position.X);Canvas.SetTop(label,position.Y);Ui.SetZIndex(label,100);Plan.Children.Add(label);
        }
    }

    private Point LabelPosition(ObjectEntry entry)
    {
        Point position=entry.Kind is "wall" or "door"
        ? new Point(Screen((End(entry.Item,"a").X+End(entry.Item,"b").X)/2)+8,Screen((End(entry.Item,"a").Y+End(entry.Item,"b").Y)/2)+8)
        : new Point(Screen(Number(entry.Item["x"]))+8,Screen(Number(entry.Item["y"]))+8);
        if(entry.Item["kind"]?.ToString()=="router")position=new Point(Screen(Number(entry.Item["x"]))+RouterSize+6,Screen(Number(entry.Item["y"])));
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
        string name=field.Text.Trim();inlineNameEditor=null;inlineNameTarget=null;
        Plan.Children.Remove(field);
        if(accept && name.Length>0 && name!=ObjectName(entry.Item,entry.Label)) {Remember();entry.Item["name"]=name;dirty=true;RenderPlan();}
    }
}
