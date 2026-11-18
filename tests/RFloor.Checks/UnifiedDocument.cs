using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using RFloor;

internal static class UnifiedDocument {
    private const BindingFlags Flags=BindingFlags.Instance|BindingFlags.NonPublic;
    public static void Run(){
        var view=new MainWindow();var host=new Window {Content=view};
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,Flags)!.Invoke(view,args);
        object? Get(string name)=>typeof(MainWindow).GetField(name,Flags)!.GetValue(view);
        void Set(string name,object? value)=>typeof(MainWindow).GetField(name,Flags)!.SetValue(view,value);
        void Require(bool condition,string message){if(!condition)throw new Exception(message);}
        var source=JsonNode.Parse(File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory,"sample.rfplan")))!.AsObject();
        Call("AddDocument",source,null);Call("SwitchMode",false);
        var points=source["points"]!.AsArray();points.Clear();
        points.Add(JsonNode.Parse("""{"id":"sample","x":1,"y":1,"band":"5","measurements":{"rssi":-55,"downloadMbps":850,"uploadMbps":720,"frequencyMHz":5180,"ssid":"Test","bssid":"aa:bb:cc:dd:ee:ff","pings":[{"target":"192.168.2.1","avg":2,"loss":0}]}}"""));
        typeof(MainWindow).GetMethod("SavePointReading",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,
            [points[0],new JsonObject {["rssi"]=-62,["frequencyMHz"]=2437,["bssid"]="aa:bb:cc:dd:ee:01"},
                new JsonObject {["signal"]=true},"2.4","aa:bb:cc:dd:ee:01"]);
        Call("SwitchMode",true);Require(points.Count==1,"Switching modes discarded measurements.");
        var file=DispatchProxy.Create<IStorageFile,FileProxy>();var target=(FileProxy)(object)file;((Dictionary<string,IStorageFile>)Get("storageFiles")!).Add(file.Path.ToString(),file);Set("path",file.Path.ToString());Set("dirty",true);
        var save=(Task<bool>)Call("Save",false)!;
        var deadline=DateTime.UtcNow.AddSeconds(10);while(!save.IsCompleted && DateTime.UtcNow<deadline){Dispatcher.UIThread.RunJobs();Thread.Sleep(1);}
        Require(save.IsCompletedSuccessfully&&save.Result,"Mobile storage save failed.");
        var restored=JsonNode.Parse(target.Bytes.ToArray())!.AsObject();
        typeof(MainWindow).GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[restored]);
        Require(restored["format"]!.ToString()=="rfloor.plan" && restored["geometry"]!["walls"]!.AsArray().Count>0,"Plan geometry was lost.");
        var readings=restored["points"]![0]!["readings"]!.AsArray();
        Require(readings.Count==2 && readings[0]!["measurements"]!["downloadMbps"]!.GetValue<int>()==850 &&
            readings[1]!["measurements"]!["rssi"]!.GetValue<int>()==-62,"Multi-band roundtrip lost new or legacy results.");
        Require(Get("dirty") is false,"Save did not clear dirty state.");
        Call("AddDocument",restored,"imported.rfplan");
        Require(Get("surveyMode") is true,"An imported survey did not open in measurement mode.");
        var blank=(JsonObject)restored.DeepClone();
        blank["points"]!.AsArray().Clear();
        Call("AddDocument",blank,"blank.rfplan");
        Require(Get("surveyMode") is false,"An imported plan without readings did not open in plan mode.");
        host.Width=1;view.Measure(new Size(1,800));view.Arrange(new Rect(0,0,1,800));Call("UpdateResponsiveLayout");
        foreach(double width in new[]{360d,412d,1440d}){
            host.Width=width;host.Height=800;view.Measure(new Size(width,800));view.Arrange(new Rect(0,0,width,800));Call("UpdateResponsiveLayout");
            Require((bool)Get("compactLayout")== (width<840),"Incorrect responsive breakpoint.");
            if(width<840){Require(!view.FindControl<Control>("LeftSidebar")!.IsEnabled,"Closed phone drawer can still receive input.");Call("ToggleDrawer",view.FindControl<Control>("LeftSidebar"));Require(view.HandleBack(),"Back did not dismiss drawer.");}
            Require(view.FindControl<Control>("ModeSwitch")!.Bounds.Width<=144,"Mode switch is too wide.");
        }
        host.Content=null;Console.WriteLine("Document checks passed: content-URI save, measurements and geometry roundtrip, clean state, phone drawers and desktop restoration.");
    }
    public class FileProxy:DispatchProxy {
        internal readonly MemoryStream Bytes=new();
        protected override object? Invoke(MethodInfo? method,object?[]? args)=>method!.Name switch {
            "get_Name"=>"sample.rfplan",
            "get_Path"=>new Uri("content://rfloor-test/sample.rfplan"),
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
