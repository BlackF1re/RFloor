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
        if(Get("surveyMode") is not false)throw new Exception("New documents must start in plan mode.");
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
        var contact=(Point)Call("SnapSurveyPosition",new Point(3.98,1.51))!;
        Near(contact.X,4,"Survey point missed the clear wall face");
        var snapCheckbox=window.FindControl<CheckBox>("SnapEnabled")!;
        foreach (bool enabled in new[]{true,false})
        {
            snapCheckbox.IsChecked=enabled;
            var insideWall=new Point(4+4/scale,1.51);
            var snappedPoint=(Point)Call("SnapSurveyPosition",insideWall)!;
            Near(snappedPoint.X,4,"Survey point did not reach the room face from the wall body");
            Require(Call("CanPlaceSurveyPoint",snappedPoint) is true && Call("CanPlaceSurveyPoint",insideWall) is false,
                "The wall face or its solid interior was classified incorrectly");
            Near(snappedPoint.Y,enabled?1.5:1.51,"Wall contact unexpectedly changed the position along the wall");
        }
        snapCheckbox.IsChecked=true;
        Call("DrawMeasurementRays",contact);
        var rays=plan.Children.OfType<Avalonia.Controls.Shapes.Line>().Where(v=>Equals(v.Tag,"point-preview")).ToArray();
        Require(rays.Length==3 && plan.Children.OfType<TextBlock>().Any(v=>Equals(v.Tag,"point-preview") && v.Text=="0 м"),"Wall contact skipped zero and measured to the wall behind");
        Near(rays.Single(ray=>ray.StartPoint.Y==ray.EndPoint.Y).EndPoint.X,50,"Ray did not end on the opposite interior wall face");
        var search=window.FindControl<TextBox>("ObjectSearch")!;
        search.Text="КОМНАТА";
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Require(plan.Children.OfType<Control>().Count(v=>Equals(v.Tag,"search-highlight"))==4,"Search did not highlight the room walls.");
        window.FindControl<Avalonia.Controls.Button>("ClearObjectSearch")!.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Require(!plan.Children.OfType<Control>().Any(v=>Equals(v.Tag,"search-highlight")),"Clearing search left highlights.");
        Require(((Avalonia.Controls.Shapes.Shape)primitiveHost.Children[0]).Fill is DrawingBrush { Drawing: DrawingGroup outline }
            && outline.Children[0] is GeometryDrawing { Brush: ISolidColorBrush { Color.A: 0 } }
            && outline.Children[1] is GeometryDrawing { Pen.Thickness: < 1 },"Outline primitive lacks fine hatching over a transparent background");
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
            if(kind!="line")Require(shape.Fill is DrawingBrush { Drawing: DrawingGroup filled }
                && filled.Children[0] is GeometryDrawing { Brush: ISolidColorBrush { Color.A: 255 } }
                && filled.Children[1] is GeometryDrawing { Pen.Thickness: < 1 },"Filled primitive lacks fine hatching over an opaque background");
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
        Require(!dot.Geometry.FillContains(dot.Geometry.Bounds.Center),"Router dot hides the grid crossing");
        Require(router["name"]?.ToString()=="Роутер","Router has no automatic name");
        var routerEntry=Activator.CreateInstance(entryType,["Роутер",router,"decoration"])!;
        Call("BeginInlineRename",routerEntry);((TextBox)Get("inlineNameEditor")!).Text="Router office";Call("CommitInlineRename");
        Require(router["name"]?.ToString()=="Router office","Router name cannot be edited");
        geometry["decorations"]!.AsArray().Remove(router);
        Call("AddRouter",new Point(4,1.5));
        var wallRouter=geometry["decorations"]!.AsArray().Last()!.AsObject();
        var wallImage=(Image)plan.Children.OfType<Canvas>().Single(v=>ReferenceEquals(v.Tag,wallRouter)).Children[0];
        Require(wallImage.Clip is Geometry clip && clip.FillContains(new Point(wallImage.Width/2-1,wallImage.Height/2)) &&
            !clip.FillContains(new Point(wallImage.Width/2+1,wallImage.Height/2)),"Router clip crossed the interior wall face.");
        var wallRouterEntry=Activator.CreateInstance(entryType,["Роутер",wallRouter,"decoration"])!;
        var routerDrag=(Vector)Call("AdjustDrag",wallRouterEntry,new Vector(.02,.24))!;
        var routerGrid=(Point)Call("GridPosition",new Point(4,1.74))!;
        Near(routerDrag.X,0,"Router dragged away from the wall face");
        Near(1.5+routerDrag.Y,routerGrid.Y,"Router slid freely along the wall instead of stepping on the grid");
        geometry["decorations"]!.AsArray().Remove(wallRouter);
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
        Require(labels.Length==geometry["rooms"]!.AsArray().Count+geometry["tapes"]!.AsArray().Count,"Only rooms and completed tapes should have visible labels here");
        Require(labels.All(x=>x.ZIndex==100 && x.Opacity>0 && x.Opacity<1),"Labels are not translucent and above objects");
        Require(labels.All(x=>x.CornerRadius.TopLeft>0),"Object labels must have rounded corners");
        var labelDefaults=(JsonObject)Get("labelDefaults")!;
        labelDefaults["room"]=new JsonObject {["format"]="{area} / {width}",["typography"]=new JsonObject {["size"]=16,["italic"]=true}};
        var currentRoom=Doc()["geometry"]!["rooms"]![0]!.AsObject();
        var currentRoomEntry=Activator.CreateInstance(entryType,["Room",currentRoom,"room"])!;
        var styled=Call("LabelText",currentRoomEntry) as TextBlock;
        Require(styled is {Text:"12 м² / 4 м",FontSize:16,FontStyle:FontStyle.Italic},"Room label format or default typography was lost");
        var currentWall=Doc()["geometry"]!["walls"]![0]!.AsObject();
        currentWall["label"]=new JsonObject {["visible"]=true,["typography"]=new JsonObject {["size"]=18,["bold"]=true,["underline"]=true,["font"]="Inter"}};
        var currentWallEntry=Activator.CreateInstance(entryType,["Wall",currentWall,"wall"])!;
        styled=Call("LabelText",currentWallEntry) as TextBlock;
        Require(Call("IsLabelVisible",currentWallEntry) is true && styled is {FontSize:18,FontWeight:FontWeight.Bold,TextDecorations:not null} &&
            styled.FontFamily.Name=="Inter" && styled.Text.Contains("4 м"),"Object typography or wall visibility override was lost");

        var fresh=(JsonObject)type.GetMethod("NewDocument",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
        Call("AddDocument",fresh,null);
        Call("AddRoom_Click",window,new RoutedEventArgs());
        Call("SetTool","room");
        Call("FinishDrawing",new Point(8,3.08),new Point(4.09,-.03));
        var joined=fresh["geometry"]!.AsObject();
        var secondRoom=joined["rooms"]![1]!.AsObject();
        Near(secondRoom["x"]!.GetValue<double>(),4,"Adjacent room missed the shared wall");
        Near(secondRoom["y"]!.GetValue<double>(),0,"Adjacent room missed the start of the wall");
        Near(secondRoom["h"]!.GetValue<double>(),3,"Adjacent room missed the end of the wall");
        var joinedWalls=joined["walls"]!.AsArray().OfType<JsonObject>().ToList();
        Require(joinedWalls.Count==7,"Rooms stored overlapping walls instead of sharing a single wall");
        var joinedShapes=joinedWalls.Select(w=>(Geometry)Call("WallGeometry",w,joinedWalls)!).ToArray();
        Require(joinedShapes.SelectMany((shape,i)=>joinedShapes.Skip(i+1).Select(other=>GeometryOps.IntersectionEmpty(shape,other))).All(clear=>clear),
            "Wall material still overlaps at the joined corners");
        var common=joinedWalls.Single(w=>w["a"]!["x"]!.GetValue<double>()==4 && w["b"]!["x"]!.GetValue<double>()==4);
        Call("SetTool","door");
        Call("FinishDrawing",new Point(4.02,1),new Point(4.02,2));
        Require(joined["doors"]!.AsArray().Count==1 && joined["walls"]!.AsArray().Count==8 &&
            !joined["walls"]!.AsArray().Contains(common),"The shared wall is still present underneath the opening");
        var door=joined["doors"]![0]!.AsObject();
        Near(((Geometry)Call("OpeningGeometry",door)!).Bounds.Center.X, 50+4*scale+2.5,"Shared opening is off the wall center");
        Call("DrawMeasurementRays",new Point(2,1.5));
        var doorRay=canvas.Children.OfType<Avalonia.Controls.Shapes.Line>().Single(v=>Equals(v.Tag,"point-preview") && v.EndPoint.X>v.StartPoint.X);
        Near(doorRay.EndPoint.X,50+4*scale,"The preview ray crossed the inner face of the doorway");
        Near(doorRay.StrokeThickness*((ScaleTransform)((LayoutTransformControl)window.FindControl<Control>("ZoomHost")!).LayoutTransform!).ScaleX,
            1,"The preview ray changed thickness with zoom");
        var roomEntry=objects.Items.Cast<object>().Single(x=>ReferenceEquals(x.GetType().GetProperty("Item")!.GetValue(x),secondRoom));
        var doorEntry=objects.Items.Cast<object>().Single(x=>ReferenceEquals(x.GetType().GetProperty("Item")!.GetValue(x),door));
        objects.SelectedItem=roomEntry;
        objects.SelectedItems!.Add(doorEntry);
        var highlight=canvas.Children.OfType<Avalonia.Controls.Shapes.Path>().Where(v=>Equals(v.Tag,"selection-highlight")).ToArray();
        Require(highlight.Length==6 && highlight.All(path=>path is {IsVisible:true,Opacity:.45} && Equals(path.Fill,Brushes.DarkOrange)),
            "Multi-selection lost or duplicated the translucent room and opening geometry");
        var partial=(JsonObject)type.GetMethod("NewDocument",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
        Call("AddDocument",partial,null);
        Call("AddRoom_Click",window,new RoutedEventArgs());
        Call("SetTool","room");
        Call("FinishDrawing",new Point(4.06,0.7),new Point(8,2.2));
        var partialWalls=partial["geometry"]!["walls"]!.AsArray().OfType<JsonObject>().ToList();
        Require(partialWalls.Count==7 && partialWalls.Count(w=>w["a"]!["x"]!.GetValue<double>()==4 &&
            w["b"]!["x"]!.GetValue<double>()==4)==1,"A partially shared wall was duplicated");
        Call("SetTool","door");
        Call("FinishDrawing",new Point(4,1),new Point(4,2.7));
        var partialDoor=partial["geometry"]!["doors"]![0]!;
        Near(partialDoor["b"]!["y"]!.GetValue<double>(),2.12,"Door crossed a return wall at a T-junction");
        string? fixture=Environment.GetEnvironmentVariable("RFLOOR_ROOM_REPAIR_FIXTURE");
        var repair=fixture is not null?JsonNode.Parse(File.ReadAllText(fixture))!.AsObject():
            (JsonObject)type.GetMethod("NewDocument",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,null)!;
        Call("AddDocument",repair,null);
        Call("SwitchMode",false);
        if(fixture is null)Call("AddRoom_Click",window,new RoutedEventArgs());
        var repairGeometry=repair["geometry"]!.AsObject();
        var repairRoom=repairGeometry["rooms"]!.AsArray().OfType<JsonObject>().First();
        double right=repairRoom["x"]!.GetValue<double>()+repairRoom["w"]!.GetValue<double>();
        double top=repairRoom["y"]!.GetValue<double>(), bottom=top+repairRoom["h"]!.GetValue<double>();
        var repairWalls=repairGeometry["walls"]!.AsArray();
        var repairDoors=repairGeometry["doors"]!.AsArray();
        JsonObject[] RightWallParts()=>repairWalls.OfType<JsonObject>().Where(w=>
            Math.Abs(w["a"]!["x"]!.GetValue<double>()-right)<1e-6 &&
            Math.Abs(w["b"]!["x"]!.GetValue<double>()-right)<1e-6).ToArray();
        foreach(var side in RightWallParts())repairWalls.Remove(side);
        Call("RenderPlan");
        Call("SetTool","wall");
        Call("FinishDrawing",new Point(right+.015,top+.025),new Point(right-.02,bottom-.025));
        void RequireContinuousWall()
        {
            Require(RightWallParts() is [{ }], "The room side is still split");
            var wall=RightWallParts().Single();
            double a=wall["a"]!["y"]!.GetValue<double>(),b=wall["b"]!["y"]!.GetValue<double>();
            Near(Math.Min(a,b),top,"The repaired wall did not meet the top corner");
            Near(Math.Max(a,b),bottom,"The repaired wall did not meet the bottom corner");
            Require(wall["roomId"]?.ToString()==repairRoom["id"]?.ToString(),"The repaired wall was detached from the room");
        }
        RequireContinuousWall();
        int originalDoorCount=repairDoors.Count;
        for(int attempt=0;attempt<2;attempt++)
        {
            Call("SetTool","door");
            Call("FinishDrawing",new Point(right,top+.6),new Point(right,top+1.4));
            Require(repairDoors.Count==originalDoorCount+1 && RightWallParts().Length==2,"The opening cannot be drawn into the repaired wall");
            var cut=repairDoors[^1];
            Call("SetTool","select");
            objects.SelectedItem=objects.Items.Cast<object>().Single(e=>ReferenceEquals(e.GetType().GetProperty("Item")!.GetValue(e),cut));
            Call("DeleteSelection_Click",window,new RoutedEventArgs());
            Require(repairDoors.Count==originalDoorCount,"Deleting an opening did not remove it");
            RequireContinuousWall();
        }
        Call("TapeClick",new Point(right+1,top));Call("TapeClick",new Point(right+2,top));
        var tape=repairGeometry["tapes"]!.AsArray().Last()!.AsObject();
        object Entry(JsonObject item)=>objects.Items.Cast<object>().Single(value=>ReferenceEquals(value.GetType().GetProperty("Item")!.GetValue(value),item));
        objects.SelectedItem=Entry(tape);
        Field("PropertyLength").Text="2";
        Call("ApplyProperties_Click",window,new RoutedEventArgs());
        Near(tape["b"]!["x"]!.GetValue<double>(),right+3,"Tape length was not editable in properties");
        type.GetField("selectedCallout",flags)!.SetValue(window,tape);
        Call("CopyPlan_Click",window,new RoutedEventArgs());
        Call("PastePlan_Click",window,new RoutedEventArgs());
        Call("PastePlan_Click",window,new RoutedEventArgs());
        var copies=repairGeometry["decorations"]!.AsArray().OfType<JsonObject>().TakeLast(2).ToArray();
        Require(copies.Length==2 && copies[0]["name"]!.ToString().EndsWith("КОПИЯ1") && copies[1]["name"]!.ToString().EndsWith("КОПИЯ2"),
            "Copying a label did not create independently numbered text copies");
        objects.SelectedItem=Entry(tape);
        type.GetField("selectedCallout",flags)!.SetValue(window,tape);
        Call("DeleteSelection_Click",window,new RoutedEventArgs());
        Require(tape["label"]?["visible"]?.GetValue<bool>()==false && repairGeometry["tapes"]!.AsArray().Contains(tape),
            "Deleting a tape label also deleted the tape");
        if(fixture is not null)Console.WriteLine("Supplied room plan passed wall repair, opening deletion and recutting.");
        Console.WriteLine("Editor logic passed: shared walls, joined corners, openings, orange multi-selection, label styles, grid-aligned routers and persistent tapes.");
    }
}


