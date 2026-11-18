using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Controls;
using TextBox=RFloor.Controls.TextBox;
using TextBlock=Avalonia.Controls.TextBlock;
using Border=Avalonia.Controls.Border;
using Canvas=RFloor.Controls.Canvas;
using RFloor;

internal static class EditorLogic
{
    public static void Run(MainWindow window)
    {
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var type=typeof(MainWindow);
        object? Call(string name,params object?[] args)=>type.GetMethod(name,flags)!.Invoke(window,args);
        object? Get(string name)=>type.GetField(name,flags)!.GetValue(window);
        JsonObject Doc()=>(JsonObject)Get("document")!;
        Call("AddDocument",Doc(),null);
        if(Get("surveyMode") is not true)throw new Exception("New documents must start in survey mode.");
        Call("SwitchMode",false);
        TextBox Field(string name)=>(TextBox)window.FindControl<Control>(name);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Near(double a,double b,string message)=>Require(Math.Abs(a-b)<1e-6,message+$" ({a} != {b})");
        ((CheckBox)window.FindControl<Control>("SnapEnabled")).IsChecked=true;type.GetField("showObjectLabels",flags)!.SetValue(window,true);
        Call("AddRoom_Click",window,new RoutedEventArgs());
        var original=Doc();var geometry=original["geometry"]!.AsObject();
        double scale=(double)type.GetField("Scale",BindingFlags.Static|BindingFlags.NonPublic)!.GetRawConstantValue()!;
        var roomWalls=geometry["walls"]!.AsArray().Select(n=>n!.AsObject()).ToList();
        double Area(Avalonia.Media.Geometry shape) {var points=(IReadOnlyList<Point>)type.GetMethod("Contour",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[shape])!;return Math.Abs(points.Select((p,i)=>p.X*points[(i+1)%points.Count].Y-p.Y*points[(i+1)%points.Count].X).Sum()/2);}
        void CheckRing() {
            var shapes=roomWalls.Select(w=>(Avalonia.Media.Geometry)Call("WallGeometry",w,roomWalls)!).ToList();
            Avalonia.Media.Geometry ring=new GeometryGroup();
            for(int i=0;i<shapes.Count;i++) {
                for(int j=i+1;j<shapes.Count;j++)Near(GeometryOps.IntersectionEmpty(shapes[i],shapes[j])?0:1,0,"Room corner overlaps");
                ring=GeometryOps.Combine(ring,shapes[i],Avalonia.Media.GeometryCombineMode.Union,null);
            }
            Require(Math.Abs(shapes.Sum(Area)-((4*scale+10)*(3*scale+10)-4*3*scale*scale))<.01,"Room corners have gaps or incorrect inner dimensions");
        }
        CheckRing();var oldA=roomWalls[0]["a"]!.DeepClone();roomWalls[0]["a"]=roomWalls[0]["b"]!.DeepClone();roomWalls[0]["b"]=oldA;CheckRing();
        Point Snap(Point p)=>(Point)Call("TapeSnap",p)!;
        Near(Snap(new Point(2,-.09)).Y,-5/scale,"Tape did not snap to the room boundary");
        Near(Snap(new Point(2,.09)).Y,0,"Tape did not snap to the room boundary");
        Near(Snap(new Point(4.1,1.5)).X,4+5/scale,"Tape changed interior dimensions");
        Point corner=Snap(new Point(-.06,-.06));
        Require(Math.Abs(corner.X+5/scale)<1e-6 || Math.Abs(corner.Y+5/scale)<1e-6,"Tape missed the room boundary: "+corner);
        // A thick standalone segment must expose its actual surface as well.
        var thick=new JsonObject {["id"]="thick",["a"]=new JsonObject {["x"]=6,["y"]=0},["b"]=new JsonObject {["x"]=6,["y"]=3},["thickness"]=.6};
        geometry["walls"]!.AsArray().Add(thick);Near(Snap(new Point(6.1,1)).X,6+2.5/scale,"Legacy thickness affected tape snapping");
        Call("SetTool","rect");type.GetField("drawingFilled",flags)!.SetValue(window,false);Call("AddDecoration",new Point(1,1),new Point(2,2));
        var primitive=geometry["decorations"]![0]!.AsObject();Require(primitive["filled"]?.GetValue<bool>()==false,"Outline primitive lost its fill setting");
        Near(Snap(new Point(1.5,.95)).Y,1,"Tape did not snap to primitive surface");
        Call("RenderPlan");
        var plan=(Canvas)window.FindControl<Control>("Plan");var primitiveHost=plan.Children.OfType<Canvas>().First(v=>ReferenceEquals(v.Tag,primitive));
        Require(((Avalonia.Controls.Shapes.Shape)primitiveHost.Children[0]).Fill is null,"Outline primitive is filled");
        var aligned=((Point A,Point B))Call("AlignConstructionLine",new Point(1,1.2),new Point(1,1.8),3d)!;
        Near(aligned.A.X,1-1.5/scale,"Wall construction overlaps a primitive surface");
        var entryType=type.GetNestedType("ObjectEntry",BindingFlags.NonPublic)!;
        var movingEntry=Activator.CreateInstance(entryType,["Primitive",primitive,"decoration"])!;
        var snapped=(Vector)Call("SnapMovedSurface",movingEntry,new Vector(5,0))!;
        Near(snapped.X,5+2.5/scale,"Moving primitive snapped through the wall surface");
        geometry["decorations"]!.AsArray().Remove(primitive);
        // Solid/outline settings, shape geometry and dimensions use the same renderer as previews.
        foreach(string kind in new[]{"rect","roundRect","ellipse","line"}) {
            Call("SetTool",kind);type.GetField("drawingFilled",flags)!.SetValue(window,true);
            Call("AddDecoration",new Point(10,10),new Point(12,13));Call("RenderPlan");
            var obj=geometry["decorations"]!.AsArray().Last()!.AsObject();
            var host=plan.Children.OfType<Canvas>().First(v=>ReferenceEquals(v.Tag,obj));
            var shape=(Avalonia.Controls.Shapes.Shape)host.Children[0];
            if(kind!="line")Require(shape.Fill is Avalonia.Media.SolidColorBrush fill && fill.Color.A==255,"Filled primitive is not opaque");
            if(shape is Avalonia.Controls.Shapes.Rectangle rectangle)Near(rectangle.RadiusX,kind=="rect"?0:4,"Rectangle corner style is wrong");
            Call("DrawConstruction",new Point(10,10),new Point(12,13));
            if(kind is "rect" or "roundRect")Require(plan.Children.OfType<Border>().Any(v=>v.Child is TextBlock t && t.Text=="2 м × 3 м = 6 м²"),"Rectangle preview lacks dimensions and area");
            geometry["decorations"]!.AsArray().Remove(obj);
        }
        Call("SetTool","router");Call("AddRouter",new Point(12,12));
        var router=geometry["decorations"]!.AsArray().Last()!.AsObject();
        var routerHost=plan.Children.OfType<Canvas>().First(v=>ReferenceEquals(v.Tag,router));
        var image=(Avalonia.Controls.Image)routerHost.Children[0];
        var vector=(Avalonia.Media.DrawingImage)image.Source;
        var dot=(Avalonia.Media.GeometryDrawing)((Avalonia.Media.DrawingGroup)vector.Drawing).Children.Last();
        Near(dot.Geometry.Bounds.Width/1024*image.Width,2.5,"Router dot differs from measurement dot");
        Require(router["name"]?.ToString()=="Роутер","Router has no automatic name");
        var routerEntry=Activator.CreateInstance(entryType,["Роутер",router,"decoration"])!;
        Call("BeginInlineRename",routerEntry);((TextBox)Get("inlineNameEditor")!).Text="Router office";Call("CommitInlineRename");
        Require(router["name"]?.ToString()=="Router office","Router name cannot be edited");
        geometry["decorations"]!.AsArray().Remove(router);
        Call("ZoomAt",100d,new Point(100,100),null);Near(((Avalonia.Media.ScaleTransform)((LayoutTransformControl)window.FindControl<Control>("ZoomHost")!).LayoutTransform).ScaleX,5,"Zoom cannot reach 500%");
        Call("ZoomAt",.2d,new Point(100,100),null);
        Call("SetTool","room");Call("DrawConstruction",new Point(0,0),new Point(2,3));
        Require(plan.Children.OfType<Border>().Any(v=>v.Child is TextBlock t && t.Text=="2 м × 3 м = 6 м²"),"Room preview lacks dimensions and area");
        Call("SetTool","tape");Call("TapeClick",new Point(1,1));Call("TapeClick",new Point(2,1));Require(geometry["tapes"]!.AsArray().Count==1 && Get("tapeStart") is null,"Tape was not completed");
        Require(!plan.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Any(v=>Equals(v.Tag,"tape-overlay")),"Tape still has endpoint dots");
        Call("TapeClick",new Point(2,2));Require(Get("tapeStart") is Point && geometry["tapes"]!.AsArray().Count==1,"Third click did not start another tape");
        Call("TapeClick",new Point(1,2));Call("SetTool","wall");Require(Get("tapeStart") is null && geometry["tapes"]!.AsArray().Count==2,"Switching tools lost completed tapes");
        Require(Call("DeleteTapeAt",new Point(1.5,1)) is true && geometry["tapes"]!.AsArray().Count==1,"Individual tape deletion failed");
        Call("Undo_Click",window,new RoutedEventArgs());
        Require(Doc()["geometry"]!["tapes"]!.AsArray().Count==2,"Tape deletion cannot be undone");
        Call("Redo_Click",window,new RoutedEventArgs());
        geometry=Doc()["geometry"]!.AsObject();original=Doc();
        Call("SetTool","select");
        var room=geometry["rooms"]![0]!.AsObject();var firstSession=Get("activeSession")!;
        int undoCount=((System.Collections.ICollection)Get("undo")!).Count;
        Field("RoomWidth").Text="7.25";
        Call("NewSurvey_Click",window,new RoutedEventArgs());
        var survey=Doc();Require(survey["format"]?.ToString()=="rfloor.plan","Mode switch changed the unified format");
        Require(ReferenceEquals(survey,original),"Mode switch copied the document");
        Call("SwitchMode",false);Require(ReferenceEquals(Doc(),original),"Returning to plan replaced the document");
        Require(Field("RoomWidth").Text=="7.25","Tab switch lost an uncommitted editor field");
        Require(((System.Collections.ICollection)Get("undo")!).Count==undoCount,"Tab switch lost undo history");
        var objects=(ListBox)window.FindControl<Control>("Objects");var entry=objects.Items.Cast<object>().First(x=>ReferenceEquals(x.GetType().GetProperty("Item")!.GetValue(x),room));
        Call("BeginInlineRename",entry);var input=(TextBox)Get("inlineNameEditor")!;
        Require(input.Text==room["name"]?.ToString(),"Inline rename did not preload the name");input.Text="Новая комната";Call("CommitInlineRename");
        Require(room["name"]?.ToString()=="Новая комната" && Get("inlineNameEditor") is null,"Inline rename did not commit");
        Call("Undo_Click",window,new RoutedEventArgs());Require(Doc()["geometry"]!["rooms"]![0]!["name"]?.ToString()!="Новая комната","Inline rename was not undoable");
        Require(Doc()["format"]?.ToString()=="rfloor.plan","Creating a map changed the plan format");
        var normalize=type.GetMethod("NormalizeShortcut",BindingFlags.Static|BindingFlags.NonPublic)!;
        Require((string)normalize.Invoke(null,["shift+ctrl+s"])! == "Ctrl+Shift+S","Shortcut normalization failed");
        bool rejected=false;try{normalize.Invoke(null,["Alt+F4"]);}catch(TargetInvocationException ex)when(ex.InnerException is FormatException){rejected=true;}
        Require(rejected,"Windows reserved shortcut was accepted");
        var canvas=(Canvas)window.FindControl<Control>("Plan");var labels=canvas.Children.OfType<Border>().Where(x=>x.Child is TextBlock && x.IsHitTestVisible).ToArray();
        Require(labels.Length>=geometry["rooms"]!.AsArray().Count+geometry["walls"]!.AsArray().Count,"Object labels are missing");
        Require(labels.All(x=>x.ZIndex==100 && x.Opacity>0 && x.Opacity<1),"Labels are not translucent and above objects");
        Require(labels.All(x=>x.CornerRadius.TopLeft>0),"Object labels must have rounded corners");
        Require(labels.Any(x=>x.Child is TextBlock t && t.Text.Contains("4 м")),"Wall labels lack their length");
        Console.WriteLine("Editor logic passed: physical tape surfaces and corners; multiple persistent tapes and individual deletion; unified modes, drafts and undo; inline rename; shortcut validation; rounded label layering. No windows were shown.");
    }
}


