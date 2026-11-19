using System.Reflection;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;

namespace RFloor.Android;

// Compiled only into the CI verification APK, never into release packages.
internal static class PlatformChecks {
    internal static async Task Run(MainActivity activity){
        try{
            await Task.Delay(1500);
            var ready=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            activity.RunOnUiThread(()=>{try{CheckEditor();ready.SetResult();}catch(Exception ex){ready.SetException(ex);}});
            await ready.Task;
            await CheckNetwork();
            global::Android.Util.Log.Info("RFloorChecks","RFLOOR_PLATFORM_CHECKS_PASSED");
        }catch(Exception ex){global::Android.Util.Log.Error("RFloorChecks","RFLOOR_PLATFORM_CHECKS_FAILED: "+ex);}
    }
    private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void CheckEditor(){
        var view=((RFloor.App)Avalonia.Application.Current!).View!;
        const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
        object? Call(string name,params object?[] args)=>typeof(MainWindow).GetMethod(name,flags)!.Invoke(view,args);
        var doc=(JsonObject)typeof(MainWindow).GetField("document",flags)!.GetValue(view)!;
        Call("AddDocument",doc,null);Call("SwitchMode",false);
        Call("AddRoom_Click",view,new Avalonia.Interactivity.RoutedEventArgs());
        Require(doc["geometry"]!["walls"]!.AsArray().Count==4,"Android room creation failed.");
        Call("SetTool","tape");Call("TapeClick",new Point(1,1));Call("TapeClick",new Point(2,1));
        Require(doc["geometry"]!["tapes"]!.AsArray().Count==1,"Android tape failed.");
        Call("SetTool","rect");Call("AddDecoration",new Point(1,1),new Point(2,2));
        Require(doc["geometry"]!["decorations"]!.AsArray().Count==1,"Android primitives failed.");
        typeof(MainWindow).GetMethod("Validate",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[JsonNode.Parse(doc.ToJsonString())!.AsObject()]);
        Call("SwitchMode",true);Require(view.FindControl<Control>("SurveyTools")!.IsVisible,"Android survey mode failed.");
    }
    private static async Task CheckNetwork(){
        var type=typeof(MainWindow).Assembly.GetType("RFloor.NetworkMeasurements")!;
        var run=type.GetMethod("RunConfiguredAsync")!;
        PlatformServices.IperfRunner=NativeIperf.Run;
        var result=await (Task<JsonObject>)run.Invoke(null,[true,new[]{"10.0.2.2"},2,"10.0.2.2",5201,1,CancellationToken.None,true,true])!;
        Require(result["error"] is null && result["uploadMbps"]?.GetValue<double>() is >0 && result["downloadMbps"]?.GetValue<double>() is >0,"Android native iperf3 failed: "+result);
        Require(result["pings"]![0]!["received"]!.GetValue<int>()==2,"Android ICMP failed: "+result);
        Require(result["frequencyMHz"]?.GetValue<int>() is >0 && result["rssi"]?.GetValue<int>() is >=-120 and <=0 && !string.IsNullOrWhiteSpace(result["ssid"]?.ToString()),"Android Wi-Fi details were unavailable: "+result);
        Require(!string.IsNullOrWhiteSpace(result["adapterId"]?.ToString()) && result["adapterVerified"]?.GetValue<bool>()==false,
            "Android did not report its Wi-Fi interface as a logical adapter.");
        var failure=JsonNode.Parse(await NativeIperf.Run("127.0.0.1",59999,1,false,CancellationToken.None));
        Require(failure?["error"] is not null,"Connection refusal was not reported.");
        using var cancellation=new CancellationTokenSource(250);
        try{await NativeIperf.Run("10.0.2.2",5201,3,false,cancellation.Token);throw new Exception("Native cancellation was ignored.");}catch(System.OperationCanceledException){}
        // The server also needs to finish releasing the interrupted stream.
        await Task.Delay(500);
        var recovered=JsonNode.Parse(await NativeIperf.Run("10.0.2.2",5201,1,true,CancellationToken.None));
        Require(recovered?["end"]?["sum_received"]?["bits_per_second"]?.GetValue<double>() is >0,"Native iperf3 did not recover after cancellation: "+recovered);
    }
}
