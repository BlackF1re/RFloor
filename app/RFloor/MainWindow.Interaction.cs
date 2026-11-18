using System.Globalization;

namespace RFloor;

public partial class MainWindow
{
    private Point? panAnchor;
    private static Cursor PanCursor => new Cursor(StandardCursorType.SizeAll);
    private void EndPan() {panAnchor=null;if(Viewport.IsPointerCaptured())Viewport.ReleasePointer();Viewport.Cursor=null;}
    private void PanLostCapture(object sender,PointerEventArgs e) {if(panAnchor is not null && !Viewport.IsPointerCaptured())EndPan();}
    private void PanStart(object sender,PointerPressedEventArgs e)
    {
        if(e.Handled)return;
        if(e.Button()==MouseButton.Right && !surveyMode) {
            if(DeleteTapeAt(World(e.GetPosition(Plan),false))){e.Handled=true;return;}
            if(tool=="tape"){tapeStart=tapeCursor=null;DrawTape();e.Handled=true;return;}
        }
        if(e.Button()==MouseButton.Left && !surveyMode && tool=="tape") {
            TapeClick(TapeSnap(World(e.GetPosition(Plan),false)));
            e.Handled=true;return;
        }
        if(e.Button()!=MouseButton.Middle)return;
        var a=tapeStart;var c=tapeCursor;
        ResetPointerTools(keepSurveyTool:true);tapeStart=a;tapeCursor=c;
        panAnchor=e.GetPosition(Viewport);Viewport.CapturePointer();Viewport.Cursor=PanCursor;e.Handled=true;
    }
    private void PanEnd(object sender,PointerReleasedEventArgs e){if(e.Button()!=MouseButton.Middle)return;EndPan();e.Handled=true;}
    private void PanMove(PointerEventArgs e)
    {
        if (panAnchor is not Point a) return;
        if (e.MiddlePressed() != true) { EndPan(); return; }
        Point b=e.GetPosition(Viewport);
        PanBy(a-b);
        panAnchor=b; e.Handled=true;
    }
    private void PanBy(Vector delta)
    {
        var pad=PlanSurface.Padding;
        double x=Viewport.HorizontalOffset+delta.X, y=Viewport.VerticalOffset+delta.Y;
        double left=Math.Max(0,-x), top=Math.Max(0,-y);
        double right=Math.Max(0,x+Viewport.ViewportWidth-Viewport.ExtentWidth);
        double bottom=Math.Max(0,y+Viewport.ViewportHeight-Viewport.ExtentHeight);
        if(left>0 || top>0 || right>0 || bottom>0)
        {
            PlanSurface.Padding=new Thickness(pad.Left+left,pad.Top+top,pad.Right+right,pad.Bottom+bottom);
            Viewport.UpdateLayout();
        }
        Viewport.Offset=new Vector(x+left,y+top);
    }
    private double GridSize => double.TryParse((GridPicker.SelectedItem as ComboBoxItem)?.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : .1;
    private void GridChanged(object sender, SelectionChangedEventArgs e) { if (IsInitialized && !loadingSettings) {dirty = true;RenderPlan();} }
    private void Viewport_MouseWheel(object sender, PointerWheelEventArgs e)
    {
        if(e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
            if(e.Delta.Y!=0)ZoomAt(Math.Pow(zoomFactor,e.Delta.Y),e.GetPosition(Viewport));
        } else if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)) {
            Viewport.Offset=new Vector(Viewport.Offset.X-(e.Delta.Y+e.Delta.X)*48,Viewport.Offset.Y);
        } else {
            Viewport.Offset=new Vector(Viewport.Offset.X-e.Delta.X*48,Viewport.Offset.Y-e.Delta.Y*48);
        }
        e.Handled = true;
    }
    private void Fit_Click(object sender, RoutedEventArgs e)
    {
        PlanSurface.Padding=new Thickness(0);
        if (Rooms.Count == 0) { PlanZoom.ScaleX = PlanZoom.ScaleY = 1; Viewport.ScrollToHorizontalOffset(0); Viewport.ScrollToVerticalOffset(0); }
        else
        {
            double minX = Rooms.Min(n => Number(n!["x"])), minY = Rooms.Min(n => Number(n!["y"]));
            double maxX = Rooms.Max(n => Number(n!["x"]) + Number(n["w"])), maxY = Rooms.Max(n => Number(n!["y"]) + Number(n["h"]));
            double zoom = Math.Clamp(Math.Min((Viewport.ViewportWidth-80)/Math.Max(1,(maxX-minX)*Scale), (Viewport.ViewportHeight-80)/Math.Max(1,(maxY-minY)*Scale)), .4, 5);
            PlanZoom.ScaleX = PlanZoom.ScaleY = zoom; Viewport.UpdateLayout();
            Viewport.ScrollToHorizontalOffset(Math.Max(0, Screen(minX)*zoom-40)); Viewport.ScrollToVerticalOffset(Math.Max(0, Screen(minY)*zoom-40));
        }
        ZoomLabel.Text = $"{PlanZoom.ScaleX*100:0} %";
        RefreshRulers();
    }
    private void MoveSelected(ObjectEntry item, Vector delta)
    {
        if (delta.Length < 1e-8) return;
        var obj = item.Item;
        if (item.Kind == "wall" && obj["roomId"] is not null) { Status.Text = L("Переместите комнату целиком или измените её размеры."); return; }
        if (item.Kind == "door")
        {
            Vector axis = new(Number(obj["b"]!["x"])-Number(obj["a"]!["x"]), Number(obj["b"]!["y"])-Number(obj["a"]!["y"]));
            if (axis.Length < 1e-8) return; axis=axis/axis.Length; delta = axis*Vectors.Dot(axis,delta);
        }
        if(item.Kind=="room") {
            if(!CanMove(item,delta))return;
            bool hadWalls=HasRoomWalls(obj);
            Remember();string? id=obj["id"]?.ToString();
            var segments=Walls.Concat(Doors).Select(n=>n!.AsObject()).Where(v=>v["roomId"]?.ToString()==id).ToList();
            RetainSharedSegments(obj,segments.Select(v=>(End(v,"a")+delta,End(v,"b")+delta)));
            foreach(var p in Points.Select(n=>n!.AsObject()).Where(p=>Number(p["x"])>=Number(obj["x"]) && Number(p["x"])<=Number(obj["x"])+Number(obj["w"]) && Number(p["y"])>=Number(obj["y"]) && Number(p["y"])<=Number(obj["y"])+Number(obj["h"]))) {p["x"]=Number(p["x"])+delta.X;p["y"]=Number(p["y"])+delta.Y;}
            foreach(var seg in segments)
                foreach(var end in new[]{"a","b"}) {seg[end]!["x"]=Number(seg[end]!["x"])+delta.X;seg[end]!["y"]=Number(seg[end]!["y"])+delta.Y;}
            obj["x"]=Number(obj["x"])+delta.X;obj["y"]=Number(obj["y"])+delta.Y;
            if(hadWalls)CompleteRoomWalls(obj);
            dirty=true;RenderPlan();return;
        }
        if(item.Kind=="decoration"){Remember();obj["x"]=Number(obj["x"])+delta.X;obj["y"]=Number(obj["y"])+delta.Y;dirty=true;RenderPlan();return;}
        bool segment = item.Kind is "wall" or "door";
        PropertyX.Text = Display(Number(segment ? obj["a"]!["x"] : obj["x"])+delta.X);
        PropertyY.Text = Display(Number(segment ? obj["a"]!["y"] : obj["y"])+delta.Y);
        if (segment) { PropertyW.Text = Display(Number(obj["b"]!["x"])+delta.X); PropertyH.Text = Display(Number(obj["b"]!["y"])+delta.Y); }
        ApplyProperties_Click(this, new RoutedEventArgs());
    }
}
