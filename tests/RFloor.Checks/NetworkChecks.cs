using System.Text.Json.Nodes;
using RFloor;
internal static class NetworkChecks {
    public static async Task Run(){
        var wifi=PlatformServices.WifiReader;var iperf=PlatformServices.IperfRunner;
        try {
            int signals=0;var directions=new List<bool>();
            PlatformServices.WifiReader=_=>{signals++;return Task.FromResult(new JsonObject {["rssi"]=-55,["frequencyMHz"]=5180,["band"]="5",["bssid"]="aa:bb:cc:dd:ee:ff"});};
            PlatformServices.IperfRunner=(host,port,seconds,reverse,token)=>{if(host!="router"||port!=5201||seconds!=5)throw new Exception("Incorrect native iperf arguments.");directions.Add(reverse);return Task.FromResult(reverse?"""{"end":{"sum_received":{"bits_per_second":900000000}}}""":"""{"end":{"sum_sent":{"bits_per_second":800000000}}}""");};
            var result=await NetworkMeasurements.RunConfiguredAsync(true,[],4,"router",5201,5,CancellationToken.None,true,true);
            if(signals!=1||directions.Count!=2||result["downloadMbps"]!.GetValue<double>()!=900||result["uploadMbps"]!.GetValue<double>()!=800||result["rssi"]!.GetValue<int>()!=-55)throw new Exception("Native readings did not reach the measurement point.");
            signals=0;directions.Clear();result=await NetworkMeasurements.RunConfiguredAsync(false,[],4,"router",5201,5,CancellationToken.None,false,true);
            if(signals!=0||directions.Count!=1||directions[0]||result["downloadMbps"] is not null)throw new Exception("Disabled measurements were executed.");
            PlatformServices.WifiReader=_=>throw new InvalidOperationException("Permission denied");PlatformServices.IperfRunner=(_,_,_,_,_)=>Task.FromResult("""{"error":"Connection refused"}""");
            result=await NetworkMeasurements.RunConfiguredAsync(true,[],4,"router",5201,5,CancellationToken.None,true,false);
            if(result["rssi"] is not null||result["downloadMbps"] is not null||!result["error"]!.ToString().Contains("Connection refused"))throw new Exception("Unavailable readings were fabricated.");
            using var cancellation=new CancellationTokenSource();cancellation.Cancel();
            PlatformServices.WifiReader=ct=>Task.FromCanceled<JsonObject>(ct);
            try{await NetworkMeasurements.RunConfiguredAsync(true,[],4,null,5201,5,cancellation.Token,false,false);throw new Exception("Measurement cancellation was ignored.");}catch(OperationCanceledException){}
            int observation=0;
            int[] rssis=[-78,-50,-53,-55,-51];
            PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject {["rssi"]=rssis[observation++],["band"]="5",["bssid"]="test"});
            result=await NetworkMeasurements.RunSurveyAsync(true,[],4,null,5201,1,CancellationToken.None,false,false);
            if(observation!=5 || result["rssi"]!.GetValue<double>()!=-53)throw new Exception("The survey did not save the RSSI median.");
            observation=0;
            PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject {["band"]=observation++==0?"2.4":"5",["bssid"]="test",["rssi"]=-53});
            result=await NetworkMeasurements.RunSurveyAsync(false,[],4,null,5201,1,CancellationToken.None,false,false);
            if(result["measurementState"]?.ToString()!="mixed" || result["rssi"] is not null)throw new Exception("A band change produced a valid survey reading.");
            Console.WriteLine("Network checks passed: platform Wi-Fi/native iperf bridge, both directions, enabled measurements, failures and cancellation.");
        }finally{PlatformServices.WifiReader=wifi;PlatformServices.IperfRunner=iperf;}
        foreach(var (frequency,band) in new[]{(2412,"2.4"),(5180,"5"),(6115,"6")}){
            var parsed=NetworkMeasurements.ParseLinuxWifi($"Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: Apartment / тест\n\tfreq: {frequency}\n\tsignal: -53.5 dBm\n");
            if(parsed["rssi"]!.GetValue<double>()!=-53.5 || parsed["band"]!.ToString()!=band || parsed["ssid"]!.ToString()!="Apartment / тест" || parsed["coverage"]!.GetValue<bool>()!=true)throw new Exception("Linux Wi-Fi parser lost connection details.");
        }
        if(NetworkMeasurements.ParseLinuxWifi("Not connected.")["coverage"]!.GetValue<bool>())throw new Exception("Linux disconnected adapter reported coverage.");
        if(NetworkMeasurements.ParseLinuxWifi("Connected to aa:bb:cc:dd:ee:ff\n signal: -999 dBm")["rssi"] is not null)throw new Exception("Invalid Linux RSSI was accepted.");
        if(Environment.GetEnvironmentVariable("RFLOOR_NETWORK_CHECK_HOST") is {} host){
            var result=await NetworkMeasurements.RunConfiguredAsync(false,[host],2,host,5201,1,CancellationToken.None,true,true);
            if(result["error"] is not null || result["downloadMbps"]?.GetValue<double>() is not >0 || result["uploadMbps"]?.GetValue<double>() is not >0 || result["pings"]![0]!["received"]!.GetValue<int>()!=2)throw new Exception("Live network check failed: "+result);
            Console.WriteLine("Live network checks passed: ICMP, bundled iperf3 upload and download.");
        }
    }
}
