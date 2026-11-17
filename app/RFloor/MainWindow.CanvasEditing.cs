using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private double labelOpacity = .85;
    private TextBox? inlineNameEditor;
    private ObjectEntry? inlineNameTarget;

    private void ResetPointerTools()
    {
        if (Plan.IsPointerCaptured()) Plan.ReleasePointer();
        tapeStart=tapeCursor=routerPreview=drawingStart=drawingEnd=dragStart=marqueeStart=marqueeEnd=null;
        dragItem=null; dragDelta=new Vector(); addingPoint=false; pointPreview=null;
    }


    private Point TapeSnap(Point p) => SnapEnabled.IsChecked==true && !PointerContext.Modifiers.HasFlag(KeyModifiers.Shift) ? SurfaceSnap(p)??p : p;

    private bool IsDragging(JsonObject obj) => dragItem is {} item && dragStart is not null &&
        (ReferenceEquals(item.Item,obj) || (item.Kind=="room" && obj["roomId"] is not null && obj["roomId"]!.ToString()==item.Item["id"]?.ToString()));

    private void DrawOpening(JsonObject opening)
    {
        var shape=new Avalonia.Controls.Shapes.Path {
            Data=OpeningGeometry(opening),
            Fill=Brushes.Gray, IsHitTestVisible=false,
            Tag=opening, Opacity=IsDragging(opening)?.18:1 };
        // Shared boundaries use identical pixel coverage, without expanding either object.
        RenderOptions.SetEdgeMode(shape,EdgeMode.Aliased);
        Plan.Children.Add(shape);
    }

    // Selection and drag previews use the same geometry as the permanent objects.
    private void DrawObjectShape(ObjectEntry entry,Vector delta,double opacity,IBrush tint,string? tag)
    {
        var obj=entry.Item;var walls=Walls.Select(n=>n!.AsObject()).ToList();
        bool invalid=tint==Brushes.Crimson;
        if(entry.Kind=="decoration"){DrawDecoration(obj,delta,tint,opacity,tag);return;}
        void Shape(Avalonia.Media.Geometry geometry) {
            var moved=geometry.Clone();moved.Transform=new TranslateTransform(delta.X*Scale,delta.Y*Scale);
            Plan.Children.Add(new Avalonia.Controls.Shapes.Path {Data=moved,Fill=invalid?Brushes.Crimson:tint,Opacity=opacity,IsHitTestVisible=false,Tag=tag});
        }
        if(entry.Kind=="room") {
            Avalonia.Media.Geometry ring=new GeometryGroup();
            foreach(var wall in walls.Where(w=>w["roomId"]?.ToString()==obj["id"]?.ToString()))ring=GeometryOps.Combine(ring,WallGeometry(wall,walls),GeometryCombineMode.Union,null);
            Shape(ring);
            foreach(var door in Doors.Select(n=>n!.AsObject()).Where(w=>w["roomId"]?.ToString()==obj["id"]?.ToString()))Shape(OpeningGeometry(door));
        }
        else if(entry.Kind=="point")Shape(new EllipseGeometry(new Rect(Screen(Number(obj["x"]))-1.25,Screen(Number(obj["y"]))-1.25,2.5,2.5)));
        else Shape(entry.Kind=="door"?OpeningGeometry(obj):WallGeometry(obj,walls));
    }
    private void UpdateObjectOpacity()
    {
        var selected=Objects.SelectedItems.Cast<ObjectEntry>().ToList();
        foreach(var element in Plan.Children.OfType<FrameworkElement>())if(element.Tag is JsonObject obj) {
            bool chosen=selected.Any(e=>ReferenceEquals(e.Item,obj) || e.Kind=="room" && obj["roomId"] is not null && obj["roomId"]?.ToString()==e.Item["id"]?.ToString());
            element.Opacity=IsDragging(obj) || chosen?0:1;
        }
    }

    private void DrawObjectLabels()
    {
        if (!showObjectLabels) return;
        foreach(var (array,kind) in new[]{(Rooms,"room"),(Walls,"wall"),(Doors,"door"),(Points,"point"),(Decorations,"decoration")})
        foreach(var obj in array.Select(n=>n!.AsObject())) {
            if(kind=="decoration" && obj["kind"]?.ToString()!="router")continue;
            if(kind=="point" && (!surveyMode || (obj["band"]?.ToString()!=Band || SelectedAccessPoint is not null && !string.Equals(AccessPointId(obj),SelectedAccessPoint,StringComparison.OrdinalIgnoreCase))))continue;
            if(obj["hideLabel"]?.GetValue<bool>()==true)continue;
            var entry=new ObjectEntry(ObjectName(obj,kind),obj,kind); Point position=LabelPosition(entry);
            string text=entry.Label;
            if(kind is "wall" or "door")text+=L("\n{0:0.##} м", Length(obj));
            if(kind=="room")text+=L("\n{0:0.##} м × {1:0.##} м = {2:0.##} м²", Number(obj["w"]), Number(obj["h"]), Number(obj["w"])*Number(obj["h"]));
            var label=new Border {Child=new TextBlock {Text=text,FontSize=11},CornerRadius=new CornerRadius(6),Background=new SolidColorBrush(Color.FromArgb(190,255,255,255)),Opacity=labelOpacity,Padding=new Thickness(5,3,5,3),Tip=L("Перетащите подпись; двойной щелчок — переименовать")};
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
            var menu=new ContextMenu();var hide=new MenuItem {Header=L("Удалить подпись")};hide.Click+=(_,_)=>{Remember();obj["hideLabel"]=true;dirty=true;RenderPlan();};menu.Items.Add(hide);label.ContextMenu=menu;
            Canvas.SetLeft(label,position.X);Canvas.SetTop(label,position.Y);Ui.SetZIndex(label,100);Plan.Children.Add(label);
        }
    }

    private Point LabelPosition(ObjectEntry entry)
    {
        Point position=entry.Kind is "wall" or "door"
        ? new Point(Screen((End(entry.Item,"a").X+End(entry.Item,"b").X)/2)+8,Screen((End(entry.Item,"a").Y+End(entry.Item,"b").Y)/2)+8)
        : new Point(Screen(Number(entry.Item["x"]))+8,Screen(Number(entry.Item["y"]))+8);
        if(entry.Item["kind"]?.ToString()=="router")position=new Point(Screen(Number(entry.Item["x"]))+RouterSize+6,Screen(Number(entry.Item["y"])));
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
