using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RFloor;

internal static class PhoneChecks {
    public static void Run(){
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        var view=new MainWindow();var host=new Window {Content=view,Width=412,Height=820};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(view,args);
        object? Get(string name)=>typeof(MainWindow).GetField(name,flags)!.GetValue(view);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        void Layout(){Dispatcher.UIThread.RunJobs();host.UpdateLayout();}
        void WaitUntil(Func<bool> condition){
            using var done=new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var poll=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(15)};
            poll.Tick+=(_,_)=>{if(condition())done.Cancel();};
            if(!condition()) {
                poll.Start();
                Dispatcher.UIThread.MainLoop(done.Token);
                poll.Stop();
            }
            Layout();
            Require(condition(),"Timed out waiting for UI operation.");
        }
        Call("AddDocument",Get("document"),null);host.Show();Layout();Call("SwitchMode",false);Layout();
        var plan=view.FindControl<Control>("Plan")!;
        Point At(double x,double y)=>plan.TranslatePoint(new Point(x,y),host)!.Value;
        void Tap(Point p){using var finger=host.TouchBegin(p);host.TouchEnd(finger,p);Layout();}
        var document=(JsonObject)Get("document")!;
        Call("SetTool","tape");Tap(At(100,100));Require(Get("tapeStart") is not null,"Touch did not set tape origin.");
        Tap(At(220,180));Require(document["geometry"]!["tapes"]!.AsArray().Count==1,"Touch did not finish tape.");
        Call("SetTool","rect");
        using(var finger=host.TouchBegin(At(100,250))){
            WaitUntil(()=>Get("touchGesture")?.ToString()=="Drawing");
            host.TouchMove(finger,At(220,320));
            Layout();
            Require(Get("drawingStart") is not null && document["geometry"]!["decorations"]!.AsArray().Count==0,"Construction did not preview before release.");
            host.TouchEnd(finger,At(220,320));
        }
        Layout();
        var decoration=document["geometry"]!["decorations"]![0]!;
        var viewport=view.FindControl<ScrollViewer>("Viewport")!;
        var zoomBeforePan=((ScaleTransform)view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform!).ScaleX;
        using(var finger=host.TouchBegin(At(230,230))){
            host.TouchMove(finger,At(180,180));host.TouchEnd(finger,At(180,180));
        }
        Layout();
        Require(viewport.Offset.X>0 && viewport.Offset.Y>0,"Single-finger pan did not scroll the plan.");
        Require(((ScaleTransform)view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform!).ScaleX==zoomBeforePan &&
            document["geometry"]!["decorations"]!.AsArray().Count==1,"Panning changed zoom or created an object.");
        viewport.Offset=default;Layout();
        Call("SetTool","select");Tap(At(150,280));
        Require(Get("touchCard") is not null,"An object with a hidden label did not offer editing actions.");
        decoration["label"]=new JsonObject {["visible"]=true};
        Call("RenderPlan");Layout();
        Tap(At(150,280));
        Require(Get("touchCard") is not null,"Tapping an object did not open its label.");
        Tap(At(50,220));Require(Get("touchCard") is null,"Tapping away did not dismiss the label.");
        var rightDrawer=view.FindControl<Border>("RightSidebar")!;
        Call("TogglePanel",rightDrawer);
        WaitUntil(()=>rightDrawer.IsEnabled && rightDrawer.RenderTransform is null);
        var planObjectList=view.FindControl<ListBox>("Objects")!;
        var row=planObjectList.GetVisualDescendants().OfType<ListBoxItem>().Single(item=>item.IsEffectivelyVisible &&
            ReferenceEquals(item.Content?.GetType().GetProperty("Item")?.GetValue(item.Content),decoration));
        Tap(row.TranslatePoint(new Point(22,row.Bounds.Height/2),host)!.Value);
        Require(Get("touchCard") is Border {Parent:Grid root,ZIndex:303} && ReferenceEquals(root,view.Content),
            "The object list did not open a card above the mobile drawer.");
        view.HandleBack();
        WaitUntil(()=>!rightDrawer.IsVisible);
        var oldFeedback=PlatformServices.HapticFeedback;
        bool haptic=false;PlatformServices.HapticFeedback=(pulses,strength)=>haptic=pulses==1 && strength>0;
        try {
            var oldX=decoration["x"]!.ToString();
            using var finger=host.TouchBegin(At(104,280));
            Require(Get("touchHold") is not null,"Touch on an object did not schedule long press.");
            WaitUntil(()=>haptic);
            host.TouchMove(finger,At(134,290));Layout();
            Require(decoration["x"]!.ToString()==oldX && Get("dragItem") is not null,"Long press did not start a preview.");
            host.TouchEnd(finger,At(134,290));Layout();
            Require(decoration["x"]!.ToString()!=oldX,"Long press movement did not commit.");
        } finally {PlatformServices.HapticFeedback=oldFeedback;}
        using(var finger=host.TouchBegin(At(75,230))){
            WaitUntil(()=>Get("touchGesture")?.ToString()=="Selection");
            host.TouchMove(finger,At(265,370));host.TouchEnd(finger,At(265,370));Layout();
        }
        Require(view.FindControl<ListBox>("Objects")!.SelectedItems!.Count==1 && Get("touchCard") is not null,
            "Plan long press did not open the action card for the single object in its marquee.");
        Call("SetTool","rect");
        var zoom=(ScaleTransform)view.FindControl<LayoutTransformControl>("ZoomHost")!.LayoutTransform!;double before=zoom.ScaleX;
        using(var first=host.TouchBegin(At(100,100)))using(var second=host.TouchBegin(At(200,100))){host.TouchMove(second,At(270,100));host.TouchEnd(second,At(270,100));host.TouchEnd(first,At(100,100));}Layout();
        Require(zoom.ScaleX>before,"Two-finger pinch did not zoom.");
        Require(document["geometry"]!["decorations"]!.AsArray().Count==1,"Pinch created an accidental primitive.");
        viewport.Offset=default;
        zoom.ScaleX=zoom.ScaleY=1;Layout();
        Call("SetTool","select");Tap(At(180,290));
        var objectCard=(Border)Get("touchCard")!;
        var deleteObject=objectCard.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,Localization.L("Удалить")));
        deleteObject.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        Require(document["geometry"]!["decorations"]!.AsArray().Count==0 &&
            !view.GetVisualDescendants().Any(v=>v.GetType().Name=="OverlayDialog"),"Plan deletion was not immediate.");
        Call("AddRoom_Click",view,new RoutedEventArgs());Layout();
        Call("SwitchMode",true);
        var wifi=PlatformServices.WifiReader;string band="2.4";
        var feedback=new List<int>();
        var savedHaptics=PlatformServices.HapticFeedback;
        PlatformServices.HapticFeedback=(pulses,_)=>feedback.Add(pulses);
        PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject {["band"]=band,["bssid"]=band,["rssi"]=-50});
        try {
            foreach(string name in new[]{"RunPing","RunIperf","RunUpload"})view.FindControl<CheckBox>(name)!.IsChecked=false;
            var add = view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("AddPointTool")!;
            var repeat = view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("RepeatPointTool")!;
            var select = view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("SelectPointsTool")!;
            var delete = view.FindControl<Avalonia.Controls.Primitives.ToggleButton>("DeletePointsTool")!;
            void Use(Avalonia.Controls.Primitives.ToggleButton button) { button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); Layout(); }
            Use(add);
            using(var finger=host.TouchBegin(At(104,136))){
                host.TouchMove(finger,At(147,160));
                host.TouchMove(finger,At(147,160));
                Require(feedback.SequenceEqual(new[]{1}),"Dragging inside the same grid cell repeated the haptic pulse.");
                Layout();
                Require(Get("pointPreview") is not null && document["points"]!.AsArray().Count==0,"Survey point did not preview until release.");
                host.TouchEnd(finger,At(147,160));
            }
            WaitUntil(()=>view.GetVisualDescendants().OfType<Button>().Any(b=>b.IsDefault && Equals(b.Content,Localization.L("Подтвердить"))));
            var confirmDevice=view.GetVisualDescendants().OfType<Button>().Single(b=>b.IsDefault && Equals(b.Content,Localization.L("Подтвердить")));
            confirmDevice.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            WaitUntil(()=>document["points"] is JsonArray {Count: >0} points && points[0]?["readings"] is JsonArray);
            Require(feedback.SequenceEqual(new[]{1,2,2}),"A successful survey did not pulse twice at the start and finish.");
            var point=document["points"]![0]!;
            Require(add.IsChecked==true,"Add stopped being active after measuring a point.");
            var markerParts=((Canvas)plan).Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Where(e=>ReferenceEquals(e.Tag,point)).ToArray();
            var marker=markerParts.Single(e=>e.Fill is not null);
            Require(marker.Width==2.15 && marker.Stroke is null &&
                markerParts.Where(e=>e.Fill is null).All(e=>e.StrokeThickness==.5 && e.Width<=7.5),
                "Survey markers should stay compact without a stroke on the center.");
            var grid=((Canvas)plan).Children.OfType<Avalonia.Controls.Shapes.Rectangle>().Select(r=>r.Fill).OfType<DrawingBrush>().Single();
            var tile=grid.DestinationRect.Rect;
            double centerX=Canvas.GetLeft(marker)+marker.Width/2, centerY=Canvas.GetTop(marker)+marker.Height/2;
            bool OnGrid(double value,double start,double step)=>Math.Abs((value-start-step/2)/step-Math.Round((value-start-step/2)/step))<1e-9;
            Require(OnGrid(centerX,tile.X,tile.Width) && OnGrid(centerY,tile.Y,tile.Height) && markerParts.Length==3 &&
                markerParts.Count(e=>e.Fill is not null)==1 && markerParts.All(e=>Math.Abs(Canvas.GetLeft(e)+e.Width/2-centerX)<1e-9 && Math.Abs(Canvas.GetTop(e)+e.Height/2-centerY)<1e-9),
                "The survey dot and rings did not share a centered grid crossing.");
            Tap(At(147,160));Require(Get("touchCard") is not null,"Tapping a survey point did not open its measurements.");
            var rings=markerParts.Where(e=>e.Stroke is not null).OrderBy(e=>e.Width).ToArray();
            var focus=((Canvas)plan).Children.OfType<Avalonia.Controls.Shapes.Ellipse>()
                .Single(e=>e.Stroke is not null && e.Width>rings[^1].Width);
            Require(focus.StrokeThickness==rings[0].StrokeThickness &&
                focus.Width-rings[1].Width==rings[1].Width-rings[0].Width,
                "The focus ring did not preserve the spacing between measurement rings.");
            MenuItem OtherBand()=>((MenuFlyout)((Border)Get("touchCard")!).GetVisualDescendants().OfType<Button>()
                .Single(b=>Equals(b.Content,Localization.L("Замерить на другой частоте"))).Flyout!).Items.OfType<MenuItem>()
                .Single(i=>Equals(i.Header,Localization.L("{0} ГГц","5")));
            OtherBand().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitUntil(()=>view.GetVisualDescendants().Any(v=>v.GetType().Name=="OverlayDialog"));
            Require(point["readings"]!.AsArray().Count==1,"A Wi-Fi band mismatch changed the saved reading.");
            view.HandleBack();Layout();
            Tap(At(147,160));
            var delay=view.FindControl<Slider>("MeasurementDelay")!;delay.Value=1;band="5";
            OtherBand().RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            WaitUntil(()=>Get("waitingForMeasurement") is true && Get("animatedPoint") is not null);
            Require(feedback.SequenceEqual(new[]{1,2,2}),"Measurement feedback started during the delay or after a band mismatch.");
            WaitUntil(()=>Get("waitingForMeasurement") is true && ((Control[])Get("animatedMarker")!).All(part=>part.Opacity==0));
            WaitUntil(()=>Get("waitingForMeasurement") is false && ((Control[])Get("animatedMarker")!)[1].Opacity>0);
            var animated=((Control[])Get("animatedMarker")!).Select(part=>part.Width).ToArray();
            Require(animated.Length==3 && animated[2]-animated[1]==focus.Width-rings[1].Width &&
                MainWindow.MeasurementMarkerOpacity(.59,false,0) is >0 and <1 &&
                MainWindow.MeasurementMarkerOpacity(.59,false,1) is >0 and <1,
                "The survey marker rings are uneven or their animation does not crossfade.");
            WaitUntil(()=>point["readings"]!.AsArray().Count==2);
            Require(document["points"]!.AsArray().Count==1 && point["readings"]![0]!["band"]!.ToString()=="2.4" &&
                point["readings"]![1]!["band"]!.ToString()=="5","Another band replaced the saved reading or duplicated the point.");
            delay.Value=0;
            Use(add);Require(add.IsChecked==false,"Pressing the current tool again did not clear it.");
            Use(repeat);
            Use(add);Require(add.IsChecked==true && repeat.IsChecked==false,"Survey tools were not mutually exclusive.");
            Tap(At(48,192));
            WaitUntil(()=>document["points"] is JsonArray {Count: >1} points && points[1]?["readings"] is JsonArray);
            var wallPoint=document["points"]![1]!;
            var wallMarker=((Canvas)plan).Children.OfType<Avalonia.Controls.Shapes.Ellipse>()
                .Where(e=>ReferenceEquals(e.Tag,wallPoint)).MaxBy(e=>e.Width)!;
            double middle=wallMarker.Width/2;
            Require(wallPoint["x"]!.GetValue<double>()==0 && wallMarker.Clip is {} clip &&
                clip.FillContains(new Point(middle+1,middle)) && !clip.FillContains(new Point(middle-1,middle)),
                "Touch did not save a wall-facing survey point with a clipped marker.");
            var iperf=PlatformServices.IperfRunner;
            try {
                view.FindControl<CheckBox>("RunIperf")!.IsChecked=true;
                PlatformServices.IperfRunner=(_,_,_,_,_)=>Task.FromResult("""{"error":"test failure"}""");
                Use(repeat);Tap(At(48,192));
                WaitUntil(()=>wallPoint["measurementState"]?.ToString()=="partial" && Get("animatedPoint") is null);
                Require(feedback.TakeLast(2).SequenceEqual(new[]{2,3}),"A failed survey did not end with a triple pulse.");
                var failedParts=((Canvas)plan).Children.OfType<Avalonia.Controls.Shapes.Ellipse>()
                    .Where(e=>ReferenceEquals(e.Tag,wallPoint)).ToArray();
                Require(failedParts.Length==3 && failedParts.All(e=>Equals(e.Fill??e.Stroke,Brushes.Firebrick)),"An enabled failed test did not turn the marker red.");
                Use(repeat);Tap(At(48,192));
                var repair=((Border)Get("touchCard")!).GetVisualDescendants().OfType<Button>()
                    .Single(b=>Equals(b.Content,Localization.L("Исправить")));
                PlatformServices.IperfRunner=(_,_,_,_,_)=>Task.FromResult("""{"end":{"sum_received":{"bits_per_second":500000000}}}""");
                repair.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                WaitUntil(()=>wallPoint["measurementState"]?.ToString()=="complete" && Get("animatedPoint") is null);
            } finally {PlatformServices.IperfRunner=iperf;}
            Use(select);
            var list=view.FindControl<ListBox>("Objects")!;
            list.SelectedItems!.Clear();
            Tap(At(147,160));Tap(At(48,192));
            Require(list.SelectedItems.Count==2,"Survey tool did not add two points to the selection.");
            list.SelectedItems.Clear();
            host.MouseDown(At(27,137),MouseButton.Left);
            host.MouseMove(At(213,219));
            host.MouseUp(At(213,219),MouseButton.Left);
            Layout();
            Require(list.SelectedItems.Count==2,"A mouse marquee did not select both survey points.");
            Use(delete);Tap(At(147,160));
            var confirmation=view.GetVisualDescendants().Single(v=>v.GetType().Name=="OverlayDialog");
            foreach (var pointToDelete in document["points"]!.AsArray())
                Require(confirmation.GetVisualDescendants().OfType<TextBlock>().Any(t=>t.Text!.StartsWith(pointToDelete!["name"]!.ToString()+" · ")),
                    "Deletion did not identify all selected points.");
            confirmation.GetVisualDescendants().OfType<Button>().Single(b=>b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            Require(document["points"]!.AsArray().Count==2,"Cancel deleted measurements.");
            Tap(At(147,160));
            confirmation=view.GetVisualDescendants().Single(v=>v.GetType().Name=="OverlayDialog");
            confirmation.GetVisualDescendants().OfType<Button>().Single(b=>!b.IsCancel).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Layout();
            Require(document["points"]!.AsArray().Count==0 && delete.IsChecked==true,"The delete tool did not remove the selected group.");
        } finally {PlatformServices.WifiReader=wifi;PlatformServices.HapticFeedback=savedHaptics;}
        Call("CreateNamedPlan");Layout();
        var naming=view.GetVisualDescendants().Single(v=>v.GetType().Name=="OverlayDialog");
        var actions=naming.GetVisualDescendants().OfType<Button>().ToArray();
        Require(actions.All(b=>b.Content?.ToString()!="×"),"New plan dialog unexpectedly has a close icon.");
        var cancel=actions.Single(b=>b.IsCancel);var create=actions.Single(b=>b.IsDefault);
        Require(cancel.TranslatePoint(default,naming)!.Value.X<create.TranslatePoint(default,naming)!.Value.X,"New plan actions are reversed.");
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Layout();
        Require(!view.GetVisualDescendants().Any(v=>v.GetType().Name=="OverlayDialog"),"Cancel did not close the new plan dialog.");
        Call("ShowPreferences",0);Layout();
        var tabs=view.GetVisualDescendants().OfType<TabControl>().Single(t=>t.Classes.Contains("settingsTabs"));
        var dialog=(Control)tabs.GetVisualAncestors().First(c=>c.GetType().Name=="OverlayDialog");
        foreach(int index in new[]{0,1,2,3,4}){tabs.SelectedIndex=index;Layout();Require(dialog.Bounds.Width<=host.Bounds.Width-24 && dialog.Bounds.Height<host.Bounds.Height,"Phone settings exceed the screen.");}
        host.Width=360;Layout();Require(dialog.Width<=312,"Open settings did not adapt to a narrower screen.");
        Require(view.HandleBack(),"Phone Back did not close settings.");Layout();
        host.Width=412;Layout();
        var drawer=view.FindControl<Border>("RightSidebar")!;
        Call("ToggleDrawer",drawer);Layout();
        Require(drawer.RenderTransform is TranslateTransform,"Phone drawer did not animate opening.");
        WaitUntil(()=>drawer.RenderTransform is null);
        var handle=((Grid)drawer.Child!).Children[1];
        var handleCenter=handle.TranslatePoint(new Point(12,handle.Bounds.Height/2),host)!.Value;
        double widthBefore=drawer.Width;
        using(var finger=host.TouchBegin(handleCenter)){
            host.TouchMove(finger,handleCenter+new Vector(34,0));host.TouchEnd(finger,handleCenter+new Vector(34,0));
        }
        Layout();
        Require(drawer.Width<widthBefore,"Phone drawer did not resize by touch.");
        host.Width=300;Layout();
        Require(drawer.Width<=268,"Open drawer did not adapt to rotation or split screen.");
        view.HandleBack();host.Width=1440;host.Height=900;Layout();
        var dock=view.FindControl<DockPanel>("CanvasDock")!;
        Require(dock.Bounds.Height==28,"Desktop dock did not restore its height.");
        foreach(var button in dock.GetVisualDescendants().OfType<Button>())Require(button.MinHeight<40,"Desktop retained phone button sizes.");
        Require(((StackPanel)dock.Children[1]).Children.OfType<Button>().First().Content?.ToString()!="⛶","Desktop retained phone fit icon.");
        host.Close();Console.WriteLine("Phone checks passed: touch drawing, persistent tape, pinch without accidental objects, settings tabs, split-screen drawers and desktop restoration.");
    }
}
