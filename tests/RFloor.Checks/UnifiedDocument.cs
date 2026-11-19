using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using RFloor;

internal static class UnifiedDocument {
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run(){
        var view=new MainWindow();var host=new Window {Width=1440,Height=900,Content=view};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(view,args);
        object? Get(string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(view);
        void Set(string name,object? value)=>typeof(MainWindow).GetField(name,Flags)!.SetValue(view,value);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        var source=JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"sample.rfplan")))!.AsObject();
        Call("AddDocument",source,null);Call("SwitchMode",false);
        var points=source["points"]!.AsArray();points.Clear();
        points.Add(JsonNode.Parse("""{"id":"sample","x":1,"y":1,"band":"5","measurements":{"rssi":-55,"downloadMbps":850,"uploadMbps":720,"frequencyMHz":5180,"ssid":"Test","bssid":"aa:bb:cc:dd:ee:ff","pings":[{"target":"192.168.2.1","avg":2,"loss":0},{"target":"8.8.8.8","avg":45},{"target":"timeout.test","avg":null,"loss":100}]}}"""));
        var sourceType=typeof(MainWindow).GetNestedType("SurveySource",BindingFlags.NonPublic)!;
        object SurveySource(string? device=null,string? adapter=null)=>Activator.CreateInstance(sourceType,[device,adapter])!;
        var saveReading=typeof(MainWindow).GetMethod("SavePointReading",BindingFlags.Static|BindingFlags.NonPublic)!;
        saveReading.Invoke(null,
            [points[0],new JsonObject {["rssi"]=-62,["frequencyMHz"]=2437,["bssid"]="aa:bb:cc:dd:ee:01"},
                new JsonObject {["signal"]=true},"2.4","aa:bb:cc:dd:ee:01",SurveySource()]);
        Call("SwitchMode",true);Require(points.Count==1,"Switching modes discarded measurements.");
        var picker=view.FindControl<SplitButton>("LayerPicker")!;
        var pickerItems=(MenuFlyout)picker.Flyout!;
        MenuItem[] Layers()=>pickerItems.Items.OfType<MenuItem>().ToArray();
        string LayerName()=>view.FindControl<TextBlock>("LayerLabel")!.Text!;
        Require(Layers().Select(i=>(string)i.Header!).SequenceEqual(new[]{
            Localization.L("Сигнал"),Localization.L("iperf3 · скачивание"),Localization.L("iperf3 · отправка"),
            Localization.L("Ping · {0}","192.168.2.1"),Localization.L("Ping · {0}","8.8.8.8"),
            Localization.L("Потери пакетов · {0}","192.168.2.1"),Localization.L("Потери пакетов · {0}","timeout.test"),Localization.L("Слои выключены")}),
            "Layer choices were not derived from the available results.");
        host.Show();Dispatcher.UIThread.RunJobs();host.UpdateLayout();
        void ClickLayerPart(string name) {
            var button=picker.GetVisualDescendants().OfType<Button>().Single(b=>b.Name==name);
            var at=button.TranslatePoint(new Point(button.Bounds.Width/2,button.Bounds.Height/2),host)!.Value;
            host.MouseDown(at,MouseButton.Left);host.MouseUp(at,MouseButton.Left);Dispatcher.UIThread.RunJobs();
        }
        ClickLayerPart("PART_PrimaryButton");
        Require(LayerName()==Localization.L("iperf3 · скачивание") && Layers()[1].IsChecked,"The primary button did not cycle the active layer.");
        ClickLayerPart("PART_SecondaryButton");
        Require(pickerItems.IsOpen && LayerName()==Localization.L("iperf3 · скачивание"),"The arrow did not open the layer list independently.");
        Layers()[4].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        pickerItems.Hide();host.Hide();
        Require(LayerName()==Localization.L("Ping · {0}","8.8.8.8") && Layers().Count(i=>i.IsChecked)==1 && Layers()[4].IsChecked,
            "The selected host was not marked as the active layer.");
        var entry=view.FindControl<ListBox>("Objects")!.Items[0]!;
        var entryLabel=(string)entry.GetType().GetProperty("Label")!.GetValue(entry)!;
        Require(entryLabel.EndsWith($" · 45 {Localization.L("мс")}"),"The ping layer displayed a different target's latency.");
        picker.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Require(LayerName()==Localization.L("Потери пакетов · {0}","192.168.2.1"),"The loss layer was missing.");
        picker.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Require(LayerName()==Localization.L("Потери пакетов · {0}","timeout.test"),"An unreachable target lost its packet loss layer.");
        picker.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Require(LayerName()==Localization.L("Слои выключены"),"The primary button did not include the off state.");
        picker.RaiseEvent(new RoutedEventArgs(SplitButton.ClickEvent));
        Require(LayerName()==Localization.L("Сигнал"),"The primary button did not wrap to the first layer.");
        var band=view.FindControl<ComboBox>("BandPicker")!;
        var plan=view.FindControl<Canvas>("Plan")!;
        Avalonia.Controls.Shapes.Ellipse[] PointParts()=>plan.Children.OfType<Avalonia.Controls.Shapes.Ellipse>()
            .Where(e=>ReferenceEquals(e.Tag,points[0])).ToArray();
        band.SelectedIndex=0;
        Require(Layers().Length==2 && PointParts().Length==3,"A band with only RSSI offered unrelated layers or did not render the saved point.");
        band.SelectedIndex=2;
        Require(!picker.IsEnabled && Layers().Length==1,"A band without results exposed a measurement layer.");
        Require(PointParts() is [{Fill:not null}],"A point measured on another band did not become a filled placeholder.");
        band.SelectedIndex=1;
        source["devices"]=JsonNode.Parse("""{"laptop":{"name":"Laptop","adapters":{"internal":{"name":"Internal","verified":true},"usb":{"name":"USB","verified":true}}},"phone":{"name":"Phone"}}""");
        void SaveSignal(string device,string adapter,int rssi)=>saveReading.Invoke(null,
            [points[0],new JsonObject {["rssi"]=rssi,["bssid"]="aa:bb:cc:dd:ee:ff"},
                new JsonObject {["signal"]=true},"5","aa:bb:cc:dd:ee:ff",SurveySource(device,adapter)]);
        SaveSignal("laptop","internal",-62);
        SaveSignal("laptop","usb",-65);
        SaveSignal("laptop","usb",-63);
        Call("RenderPlan");
        var devices=view.FindControl<ComboBox>("DevicePicker")!;
        var adapters=view.FindControl<ComboBox>("AdapterPicker")!;
        var summary=view.FindControl<ComboBox>("SummaryPicker")!;
        string PointLabel()=>(string)view.FindControl<ListBox>("Objects")!.Items[0]!.GetType().GetProperty("Label")!
            .GetValue(view.FindControl<ListBox>("Objects")!.Items[0])!;
        Require(PointLabel().EndsWith("-60 dBm"),"The combined map did not count each adapter once.");
        summary.SelectedIndex=1;
        Require(PointLabel().EndsWith("-63 dBm"),"The minimum did not use the lowest RSSI.");
        summary.SelectedIndex=2;
        Require(PointLabel().EndsWith("-55 dBm"),"The maximum did not use the highest RSSI.");
        devices.SelectedItem=devices.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"laptop"));
        adapters.SelectedItem=adapters.Items.OfType<ComboBoxItem>().Single(i=>Equals(i.Tag,"usb"));
        Require(PointLabel().EndsWith("-63 dBm"),"Switching adapters showed results from another source.");
        band.SelectedIndex=0;
        Require(PointParts() is [{Fill:not null}],"Another source's position did not remain available as a placeholder.");
        band.SelectedIndex=1;
        var file=DispatchProxy.Create<IStorageFile,FileProxy>();var target=(FileProxy)(object)file;((Dictionary<string,IStorageFile>)Get("storageFiles")!).Add(file.Path.ToString(),file);Set("path",file.Path.ToString());Set("dirty",true);
        var save=(Task<bool>)Call("Save",false)!;
        var deadline=DateTime.UtcNow.AddSeconds(10);while(!save.IsCompleted && DateTime.UtcNow<deadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
        Require(save.IsCompletedSuccessfully&&save.Result,"Mobile storage save failed.");
        var restored=JsonNode.Parse(target.Bytes.ToArray())!.AsObject();
        typeof(MainWindow).GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[restored]);
        Require(restored["format"]!.ToString()=="rfloor.plan" && restored["geometry"]!["walls"]!.AsArray().Count>0,"Plan geometry was lost.");
        var readings=restored["points"]![0]!["readings"]!.AsArray();
        Require(readings.Count==4 && readings[0]!["measurements"]!["downloadMbps"]!.GetValue<int>()==850 &&
            readings[1]!["measurements"]!["rssi"]!.GetValue<int>()==-62 &&
            readings[3]!["adapterId"]!.ToString()=="usb" && readings[3]!["measurements"]!["rssi"]!.GetValue<int>()==-63,
            "Multi-band roundtrip lost source identities, new or legacy results.");
        Require(restored["modifiedAt"] is not null && restored.IndexOf("devices")<restored.IndexOf("geometry") &&
            restored.IndexOf("settings")<restored.IndexOf("geometry"),"The file header lost metadata or survey settings.");
        Require(Get("dirty") is false,"Save did not clear dirty state.");
        var documentTabs=view.FindControl<TabControl>("DocumentTabs")!;
        Require(((TabItem)documentTabs.SelectedItem!).Header is StackPanel {Children:[TextBlock {Text:"sample.rfplan"},_]},
            "Android tab title contained a storage prefix or repeated extension.");
        Set("autoSaveEnabled",true);
        Set("dirty",true);
        view.FindControl<RFloor.Controls.TextBox>("ProjectName")!.Text="Autosaved survey";
        Call("AddDocument",(JsonObject)restored.DeepClone(),null);
        Set("dirty",true);
        var autoSave=(Task)Call("AutoSaveProjectsAsync")!;
        deadline=DateTime.UtcNow.AddSeconds(10);
        while(!autoSave.IsCompleted && DateTime.UtcNow<deadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
        Require(autoSave.IsCompletedSuccessfully &&
            JsonNode.Parse(target.Bytes.ToArray())!["name"]?.ToString()=="Autosaved survey" && Get("dirty") is true,
            "Autosave did not persist the inactive project or changed the unsaved project.");
        var firstTab=(TabItem)documentTabs.Items[0]!;
        Require(firstTab.Header is StackPanel {Children:[TextBlock {Text:"sample.rfplan"},_]},
            "Autosave did not clear the saved tab's dirty flag.");
        Call("ActivateSession",firstTab.Tag);
        var saveDirectory=Directory.CreateTempSubdirectory("RFloor-check-");
        try {
            var nameless=System.IO.Path.Combine(saveDirectory.FullName,".rfplan");
            Set("path",new Uri(nameless).AbsoluteUri);Set("dirty",true);
            view.FindControl<RFloor.Controls.TextBox>("ProjectName")!.Text="Portable room";
            for(int attempt=0;attempt<2;attempt++)
            {
                var localSave=(Task<bool>)Call("Save",false)!;
                deadline=DateTime.UtcNow.AddSeconds(10);
                while(!localSave.IsCompleted && DateTime.UtcNow<deadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
                Require(localSave.IsCompletedSuccessfully && localSave.Result,"Local plan save failed");
            }
            Require(File.Exists(System.IO.Path.Combine(saveDirectory.FullName,"Portable room.rfplan")) && !File.Exists(nameless),
                "Local save produced a file without the project name");
            Require(((TabItem)documentTabs.SelectedItem!).Header is StackPanel {Children:[TextBlock {Text:"Portable room.rfplan"},_]},
                "The saved plan's tab did not show the filename.");
        } finally {saveDirectory.Delete(true);}
        Call("AddDocument",restored,"imported.rfplan");
        Require(Get("surveyMode") is true,"An imported survey did not open in measurement mode.");
        var blank=(JsonObject)restored.DeepClone();
        blank["points"]!.AsArray().Clear();
        Call("AddDocument",blank,"blank.rfplan");
        Require(Get("surveyMode") is false,"An imported plan without readings did not open in plan mode.");
        var room=blank["geometry"]!["rooms"]![0]!;
        double x=room["x"]!.GetValue<double>(),y=room["y"]!.GetValue<double>();
        double w=room["w"]!.GetValue<double>(),h=room["h"]!.GetValue<double>();
        for(int i=0;i<3;i++)
            blank["points"]!.AsArray().Add(new JsonObject {["id"]=$"tile-{i}",["x"]=x+w*(i==1?.7:.3),["y"]=y+h*(i==2?.7:.3),
                ["band"]="5",["measurements"]=new JsonObject {["rssi"]=-50-i,
                    ["pings"]=new JsonArray(new JsonObject {["target"]="192.168.2.1",["avg"]=20+40*i})}});
        var layerObjects=blank["geometry"]!["decorations"]!.AsArray();
        layerObjects.Add(new JsonObject {["id"]="coverage-primitive",["kind"]="rectangle",["x"]=x,["y"]=y,["w"]=1,["h"]=1});
        layerObjects.Add(new JsonObject {["id"]="coverage-router",["kind"]="router",["x"]=x+1,["y"]=y+1,["w"]=.2,["h"]=.2});
        Call("SwitchMode",true);
        var coverage=plan.Children.OfType<Image>().Single();
        var firstTile=coverage.Source;
        Require(firstTile is Avalonia.Media.Imaging.WriteableBitmap && !coverage.IsHitTestVisible,
            "Coverage was not rendered as a non-interactive continuous image.");
        int LayerOf(JsonNode? item)=>plan.Children.OfType<Control>().First(control=>ReferenceEquals(control.Tag,item)).ZIndex;
        Require(LayerOf(layerObjects[^2])<coverage.ZIndex && coverage.ZIndex<LayerOf(layerObjects[^1]) &&
            coverage.ZIndex<LayerOf(blank["points"]![0]),"Coverage obscured a router or measurement point, or stayed behind a primitive.");
        Call("RenderPlan");
        Require(ReferenceEquals(firstTile,plan.Children.OfType<Image>().Single().Source),"An unchanged plan rerasterized the layer.");
        host.Show();Dispatcher.UIThread.RunJobs();host.UpdateLayout();
        var lastTab=(TabItem)documentTabs.Items[^1]!;
        var addTab=documentTabs.GetVisualDescendants().OfType<Button>().Single(b=>Equals(b.Content,"＋"));
        var tabEnd=lastTab.TranslatePoint(new Point(lastTab.Bounds.Width,0),documentTabs)!.Value.X;
        var addPosition=addTab.TranslatePoint(default,documentTabs)!.Value.X;
        Require(Math.Abs(addPosition-tabEnd-4)<1,"The create button did not follow the last tab.");
        var badge=view.FindControl<Border>("CoverageHoverBadge")!;
        var hoverText=view.FindControl<TextBlock>("CoverageHoverText")!;
        var hoverMarker=view.FindControl<Avalonia.Controls.Shapes.Ellipse>("CoverageHoverMarker")!;
        Point OnMap(double u,double v)=>plan.TranslatePoint(new Point((double)Call("Screen",x+w*u)!, (double)Call("Screen",y+h*v)!),host)!.Value;
        void MoveTo(Point at) {host.MouseMove(at);Dispatcher.UIThread.RunJobs();}
        var probe=OnMap(.43,.37);
        MoveTo(probe);
        string expectedSignal=$"{-50.675:0.#} dBm"+Localization.L(" · Источников: {0}",1);
        Require(badge.IsVisible && hoverText.Text==expectedSignal && hoverMarker.Fill is SolidColorBrush,
            "Hover did not show the local, unsnapped signal estimate and color.");
        var signalColor=((SolidColorBrush)hoverMarker.Fill!).Color;
        host.MouseWheel(probe,new Vector(0,1),RawInputModifiers.Control);Dispatcher.UIThread.RunJobs();
        Require(badge.IsVisible && hoverText.Text==expectedSignal,"Cursor-anchored zoom changed the hover estimate.");
        Layers()[1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var latency=hoverText.Text!.Split(" · ",2)[0].Split(' ',2);
        Require(badge.IsVisible && latency.Length==2 && latency[1]==Localization.L("мс") &&
            double.TryParse(latency[0],out double milliseconds) && Math.Abs(milliseconds-47)<1 &&
            ((SolidColorBrush)hoverMarker.Fill!).Color!=signalColor,"Changing layers did not refresh the estimate and color.");
        MoveTo(OnMap(.8,.8));
        Require(!badge.IsVisible,"Hover displayed an estimate outside the surveyed area.");
        MoveTo(OnMap(.3,.3));
        Require(!badge.IsVisible,"The interpolated badge obscured a measurement point's own tooltip.");
        int RingCount(string tag)=>plan.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Count(v=>Equals(v.Tag,tag));
        Require(RingCount("point-hover")==1,"The marker did not gain a ring on hover.");
        MoveTo(OnMap(.43,.37));
        Require(badge.IsVisible && RingCount("point-hover")==0,"Leaving the marker should dismiss its ring and restore the estimate.");
        void Click(Point at){host.MouseDown(at,MouseButton.Left);host.MouseUp(at,MouseButton.Left);Dispatcher.UIThread.RunJobs();}
        Click(OnMap(.3,.3));MoveTo(OnMap(.43,.37));
        Require(RingCount("selection-highlight")==1 && RingCount("point-hover")==0,"The selected marker lost its ring after the cursor left.");
        Click(OnMap(.7,.3));MoveTo(OnMap(.43,.37));
        Require(plan.Children.OfType<Avalonia.Controls.Shapes.Ellipse>().Single(v=>Equals(v.Tag,"selection-highlight")).Bounds.X >
            (double)Call("Screen",x+w*.5)!,"The previous marker stayed selected after another marker was clicked.");
        MoveTo(new Point(2,2));
        Require(!badge.IsVisible,"Leaving the viewport left the hover badge visible.");
        MoveTo(OnMap(.43,.37));
        Layers()[^1].RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        Require(!badge.IsVisible,"Turning layers off left the hover badge visible.");
        Require(!plan.Children.OfType<Image>().Any(),"Turning layers off left a coverage image on the plan.");
        host.Hide();
        host.Width=1;view.Measure(new Size(1,800));view.Arrange(new Rect(0,0,1,800));Call("UpdateResponsiveLayout");
        foreach(double width in new[]{360d,412d,1440d}){
            host.Width=width;host.Height=800;view.Measure(new Size(width,800));view.Arrange(new Rect(0,0,width,800));Call("UpdateResponsiveLayout");
            Require((bool)Get("compactLayout")== (width<840),"Incorrect responsive breakpoint.");
            if(width<840){Require(!view.FindControl<Control>("LeftSidebar")!.IsEnabled,"Closed phone drawer can still receive input.");Call("ToggleDrawer",view.FindControl<Control>("LeftSidebar"));Require(view.HandleBack(),"Back did not dismiss drawer.");}
            Require(view.FindControl<Control>("ModeSwitch")!.Bounds.Width<=144,"Mode switch is too wide.");
        }
        host.Close();Console.WriteLine("Document checks passed: content-URI save, roundtrip, filtered layer menu and bitmap reuse, phone drawers and desktop restoration.");
    }
    public class FileProxy:DispatchProxy {
        internal readonly MemoryStream Bytes=new();
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {
            "get_Name"=>"primary%3ADocuments%2Fsample.rfplan.rfplan",
            "get_Path"=>new Uri("content://rfloor-test/document/primary%3ADocuments%2Fsample.rfplan.rfplan"),
            "get_CanBookmark"=>false,
            "OpenReadAsync"=>Task.FromResult<Stream>(new MemoryStream(Bytes.ToArray())),
            "OpenWriteAsync"=>Task.FromResult<Stream>(new RetainedStream(Bytes)),
            "Dispose"=>null,
            _=>throw new NotSupportedException(method.Name)
        };
    }
    private sealed class RetainedStream(MemoryStream target):Stream {
        public override bool CanRead=>false;public override bool CanSeek=>true;public override bool CanWrite=>true;
        public override long Length=>target.Length;public override long Position{get=>target.Position;set=>target.Position=value;}
        public override void Flush()=>target.Flush();public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>target.Seek(offset,origin);
        public override void SetLength(long value){target.SetLength(value);target.Position=0;}
        public override void Write(byte[] buffer,int offset,int count)=>target.Write(buffer,offset,count);
    }
}
