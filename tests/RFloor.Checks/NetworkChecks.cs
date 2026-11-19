using System.Net;
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
            PlatformServices.WifiReader=_=>Task.FromResult(observation++==2 ? new JsonObject() :
                new JsonObject {["band"]="5",["bssid"]="test",["rssi"]=-53});
            result=await NetworkMeasurements.RunSurveyAsync(true,[],4,null,5201,1,CancellationToken.None,false,false);
            if(result["measurementState"] is not null || result["rssi"]?.GetValue<double>()!=-53)
                throw new Exception("An unreadable observation discarded stable Wi-Fi readings.");
            observation=0;
            PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject {["band"]=observation++==0?"2.4":"5",["bssid"]="test",["rssi"]=-53});
            result=await NetworkMeasurements.RunSurveyAsync(false,[],4,null,5201,1,CancellationToken.None,false,false);
            if(result["measurementState"]?.ToString()!="mixed" || result["rssi"] is not null)throw new Exception("A band change produced a valid survey reading.");
            observation=0;
            PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject {["band"]="5",["bssid"]="test",
                ["adapterId"]=observation++==0?"internal":"usb",["rssi"]=-53});
            result=await NetworkMeasurements.RunSurveyAsync(false,[],4,null,5201,1,CancellationToken.None,false,false);
            if(result["measurementState"]?.ToString()!="mixed")throw new Exception("A mid-survey adapter change was accepted.");
            PlatformServices.WifiReader=_=>Task.FromResult(new JsonObject { ["band"]="5",["bssid"]="test",
                ["adapterId"]="internal",["nonWifiRoute"]=true });
            PlatformServices.IperfRunner=(_,_,_,_,_)=>throw new Exception("iperf3 was invoked outside the survey adapter.");
            result=await NetworkMeasurements.RunSurveyAsync(false,["router"],1,"router",5201,1,CancellationToken.None,true,true);
            if(result["error"] is null || result["pings"] is not JsonArray {Count:0} || result["downloadMbps"] is not null || result["uploadMbps"] is not null)
                throw new Exception("Transport measurements were attributed to the wrong adapter.");
            Console.WriteLine("Network checks passed: platform Wi-Fi/native iperf bridge, both directions, enabled measurements, failures and cancellation.");
        }finally{PlatformServices.WifiReader=wifi;PlatformServices.IperfRunner=iperf;}
        foreach(var (frequency,band) in new[]{(2412,"2.4"),(5180,"5"),(6115,"6")}){
            var parsed=NetworkMeasurements.ParseLinuxWifi($"Connected to aa:bb:cc:dd:ee:ff (on wlan0)\n\tSSID: Apartment / тест\n\tfreq: {frequency}\n\tsignal: -53.5 dBm\n");
            if(parsed["rssi"]!.GetValue<double>()!=-53.5 || parsed["band"]!.ToString()!=band || parsed["ssid"]!.ToString()!="Apartment / тест" || parsed["coverage"]!.GetValue<bool>()!=true)throw new Exception("Linux Wi-Fi parser lost connection details.");
        }
        if(NetworkMeasurements.ParseLinuxWifi("Not connected.")["coverage"]!.GetValue<bool>())throw new Exception("Linux disconnected adapter reported coverage.");
        if(NetworkMeasurements.ParseLinuxWifi("Connected to aa:bb:cc:dd:ee:ff\n signal: -999 dBm")["rssi"] is not null)throw new Exception("Invalid Linux RSSI was accepted.");
        var windows=NetworkMeasurements.ParseWindowsWifi(
            "Name : Wi-Fi\nState : connected\nSSID : test\nAP BSSID : aa:bb:cc:dd:ee:ff\nBand : 5 GHz\nChannel : 36\nSignal : 84%\nRssi : -44\nName : Spare\nState : disconnected");
        if(windows["bssid"]?.ToString()!="aa:bb:cc:dd:ee:ff" || windows["frequencyMHz"]?.GetValue<int>()!=5180 || windows["rssi"]?.GetValue<int>()!=-44)
            throw new Exception("A disconnected adapter overwrote the active Windows Wi-Fi interface.");
        const string primary="11111111-1111-1111-1111-111111111111", secondary="22222222-2222-2222-2222-222222222222";
        var dual=$"Name : Wi-Fi\nGUID : {primary}\nState : connected\nBSSID : aa:bb:cc:dd:ee:01\nBand : 5 GHz\nName : External\nGUID : {secondary}\nState : connected\nBSSID : aa:bb:cc:dd:ee:02\nBand : 5 GHz";
        if(NetworkMeasurements.ParseWindowsWifi(dual,secondary)["adapterId"]?.ToString()!=secondary ||
            NetworkMeasurements.ParseWindowsWifi(dual)["error"] is null)
            throw new Exception("Connected Wi-Fi adapters were silently conflated.");
        var localized=$"Имя : Беспроводная сеть\nИдентификатор GUID : {primary}\nСостояние : подключено\nAP BSSID : aa:bb:cc:dd:ee:01\nДиапазон : 5 GHz";
        if(NetworkMeasurements.ParseWindowsWifi(localized)["adapterId"]?.ToString()!=primary)
            throw new Exception("The Windows adapter GUID was lost in localized output.");
        if(Environment.GetEnvironmentVariable("RFLOOR_NETWORK_CHECK_HOST") is {} host){
            // Hosted runners test real transport over loopback and have no Wi-Fi adapter.
            bool loopback=IPAddress.TryParse(host,out var address) && IPAddress.IsLoopback(address);
            var result=loopback
                ? await NetworkMeasurements.RunConfiguredAsync(false,[host],2,host,5201,1,CancellationToken.None,true,true)
                : await NetworkMeasurements.RunSurveyAsync(true,[host],2,host,5201,1,CancellationToken.None,true,true);
            if(result["error"] is not null || result["measurementState"] is not null ||
                (!loopback && result["rssi"]?.GetValue<double>() is not <0) ||
                result["downloadMbps"]?.GetValue<double>() is not >0 || result["uploadMbps"]?.GetValue<double>() is not >0 ||
                result["pings"]![0]!["received"]!.GetValue<int>()!=2)throw new Exception("Live network check failed: "+result);
            var radio=loopback?"":FormattableString.Invariant($"{result["frequencyMHz"]} MHz, {result["rssi"]} dBm, ");
            Console.WriteLine(FormattableString.Invariant($"Live network passed: {radio}ICMP, iperf3 {result["downloadMbps"]} Mbps down / {result["uploadMbps"]} Mbps up."));
        }
    }
}
