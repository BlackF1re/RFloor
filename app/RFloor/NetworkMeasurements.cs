using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace RFloor;

internal static class NetworkMeasurements
{
    public static async Task<JsonObject> RunAsync(bool wifi, string[] targets, int count, string? iperfHost, int port, int duration, CancellationToken token)
        => await RunConfiguredAsync(wifi, targets, count, iperfHost, port, duration, token, true, true);
    public static async Task<JsonObject> RunConfiguredAsync(bool wifi, string[] targets, int count, string? iperfHost, int port, int duration, CancellationToken token, bool download, bool upload)
    {
        var result = new JsonObject { ["pings"] = new JsonArray(), ["timestamp"] = DateTime.UtcNow.ToString("O") };
        var errors = new List<string>();
        if (wifi)
        {
            try
            {
                var reading=await ReadWifi(token);foreach(var pair in reading)result[pair.Key]=pair.Value?.DeepClone();
            }
            catch (Exception e) when (e is not OperationCanceledException) { errors.Add("Wi-Fi: " + e.Message); }
        }
        foreach (var target in targets)
        {
            token.ThrowIfCancellationRequested(); var times = new List<long>();
            for(int i=0;i<count;i++)
            {
                token.ThrowIfCancellationRequested();
                try { using var ping = new Ping(); var reply=await ping.SendPingAsync(target, TimeSpan.FromMilliseconds(1600), cancellationToken:token); if(reply.Status==IPStatus.Success) times.Add(reply.RoundtripTime); }
                catch (PingException) { }
            }
            result["pings"]!.AsArray().Add(new JsonObject { ["target"]=target, ["sent"]=count, ["received"]=times.Count, ["loss"]=Math.Round((count-times.Count)*100.0/count,1), ["avg"]=times.Count>0 ? Math.Round(times.Average(),2) : null, ["min"]=times.Count>0 ? times.Min() : null, ["max"]=times.Count>0 ? times.Max() : null });
        }
        if (!string.IsNullOrEmpty(iperfHost)) foreach (var reverse in new[] { false, true })
        {
            if (reverse ? !download : !upload) continue;
            try
            {
                var args = new List<string> { "-c", iperfHost, "-p", port.ToString(), "-t", duration.ToString(), "--json" }; if(reverse) args.Add("-R");
                var bundled = Path.Combine(AppContext.BaseDirectory, "iperf3", OperatingSystem.IsWindows()?"iperf3.exe":"iperf3");
                // The bundled Windows client creates its stream buffer in the working directory.
                // Desktop shortcuts may start in a protected directory such as System32.
                var workspace = Path.Combine(Path.GetTempPath(), "RFloor", "iperf");
                Directory.CreateDirectory(workspace);
                var json = JsonNode.Parse(PlatformServices.IperfRunner is {} native?await native(iperfHost,port,duration,reverse,token):await RunProcess(File.Exists(bundled) ? bundled : OperatingSystem.IsWindows()?"iperf3.exe":"iperf3", args, token, TimeSpan.FromSeconds(duration + 10), workspace));
                var value=json?["end"]?[reverse?"sum_received":"sum_sent"]?["bits_per_second"]?.GetValue<double>();
                if(value is double bps && double.IsFinite(bps) && bps>=0) result[reverse?"downloadMbps":"uploadMbps"]=Math.Round(bps/1e6,2);
                else errors.Add("iperf3: " + (json?["error"]?.GetValue<string>() ?? L("скорость не получена")));
            }
            catch(Exception e) when(e is not OperationCanceledException) { errors.Add("iperf3: " + e.Message); }
        }
        if(errors.Count>0) result["error"]=string.Join("; ",errors);
        return result;
    }
    internal static async Task<JsonObject> RunSurveyAsync(bool signal, string[] targets, int count,
        string? host, int port, int duration, CancellationToken token, bool download, bool upload)
    {
        var readings = new List<JsonObject>();
        bool changed = false, unknown = false;
        string? wifiError = null;
        (string? Bssid,string? Band,bool Offline)? identity = null;
        async Task Observe()
        {
            try
            {
                var reading = await ReadWifi(token);
                if (reading["error"] is {} error) wifiError = error.ToString();
                var bssid = reading["bssid"]?.ToString()?.ToUpperInvariant();
                var band = reading["band"]?.ToString();
                bool offline = reading["coverage"]?.GetValue<bool>() == false;
                if (!offline && (string.IsNullOrWhiteSpace(bssid) || band is not ("2.4" or "5" or "6"))) {
                    unknown = true;
                    return;
                }
                var current = (bssid,band,offline);
                if (identity is {} first && current != first) changed = true;
                identity ??= current;
                readings.Add(reading);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unknown = true;
                wifiError = ex.Message;
            }
        }
        await Observe();
        var network = RunConfiguredAsync(false,targets,count,host,port,duration,token,download,upload);
        int observations = 1;
        while (signal && observations < 5 || !network.IsCompleted)
        {
            var interval = Task.Delay(450,token);
            if (signal && observations < 5) await interval;
            else await Task.WhenAny(interval,network);
            token.ThrowIfCancellationRequested();
            await Observe();
            observations++;
        }
        var result = await network;
        if (!signal || targets.Length>0 || !string.IsNullOrEmpty(host)) await Observe();

        if (changed || unknown || identity is null)
        {
            result["measurementState"] = changed ? "mixed" : "unverified";
            if (wifiError is not null) result["wifiError"] = wifiError;
            return result;
        }

        var last = readings[^1];
        foreach (var key in new[] {"ssid","bssid","band","frequencyMHz","coverage"})
            if(last[key] is {} value) result[key]=value.DeepClone();
        if (signal)
        {
            foreach (var key in new[] {"rssi","quality"})
            {
                var samples = readings.Select(r=>r[key]).OfType<JsonValue>()
                    .Select(v=>double.TryParse(v.ToJsonString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var n)?n:double.NaN)
                    .Where(double.IsFinite).Order().ToArray();
                if (samples.Length>=3)
                    result[key]=(samples[(samples.Length-1)/2]+samples[samples.Length/2])/2;
            }
            if (result["rssi"] is null && result["quality"] is null)
                result["error"] = string.Join("; ",new[] {result["error"]?.ToString(),wifiError ?? L("������ Wi-Fi ����������")}.OfType<string>());
        }
        return result;
    }

    internal static async Task<JsonObject> ReadWifi(CancellationToken token) {
        if(PlatformServices.WifiReader is {} native)return await native(token);
        var result=new JsonObject();var errors=new List<string>();
        if(OperatingSystem.IsWindows()){ParseWifi(await RunProcess("netsh.exe",["wlan","show","interfaces"],token),result,errors);}
        else if(OperatingSystem.IsLinux()){
            var devices=await RunProcess("iw",["dev"],token);
            var names=Regex.Matches(devices,@"Interface\s+(\S+)").Select(m=>m.Groups[1].Value).ToArray();
            if(names.Length==0)throw new InvalidOperationException(L("Сигнал Wi-Fi недоступен"));
            foreach(string name in names){
                string link=await RunProcess("iw",["dev",name,"link"],token);
                var reading=ParseLinuxWifi(link);if(reading["bssid"] is not null)return reading;
            }
            result["coverage"]=false;result["quality"]=0;
        }
        else errors.Add(L("Сигнал Wi-Fi недоступен"));
        if(errors.Count>0)result["error"]=string.Join("; ",errors);return result;
    }
    internal static JsonObject ParseLinuxWifi(string link){
        var mac=Regex.Match(link,@"Connected to ([0-9a-f:]{17})",RegexOptions.IgnoreCase);
        if(!mac.Success)return new JsonObject {["coverage"]=false,["quality"]=0};
        var result=new JsonObject {["bssid"]=mac.Groups[1].Value,["ssid"]=Regex.Match(link,@"SSID: (.*)").Groups[1].Value.Trim()};
        if(int.TryParse(Regex.Match(link,@"freq:\s*(\d+)").Groups[1].Value,out int frequency)) {result["frequencyMHz"]=frequency;result["band"]=frequency<3000?"2.4":frequency<5925?"5":"6";}
        if(double.TryParse(Regex.Match(link,@"signal:\s*(-?[\d.]+)").Groups[1].Value,NumberStyles.Float,CultureInfo.InvariantCulture,out double rssi) && rssi is >=-120 and <=0){result["rssi"]=rssi;result["quality"]=Math.Clamp((rssi+100)*2,0,100);result["coverage"]=true;}
        else result["error"]=L("Сигнал Wi-Fi недоступен");
        return result;
    }
    private static void ParseWifi(string text, JsonObject result, List<string> errors)
    {
                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in text.Split('\n')) { var i = line.IndexOf(':'); if (i > 0) fields[line[..i].Trim()] = line[(i+1)..].Trim(); }
                string? Field(params string[] names) => names.Select(n => fields.GetValueOrDefault(n)).FirstOrDefault(v => v != null);
                string? state=Field("state","состояние");
                if(state is not null && (state.Equals("disconnected",StringComparison.OrdinalIgnoreCase) || state.Equals("отключено",StringComparison.OrdinalIgnoreCase)))
                { result["coverage"]=false; result["quality"]=0; return; }
                var q = Regex.Match(Field("signal", "сигнал", "уровень сигнала") ?? "", @"\d+");
                if (q.Success) result["quality"] = Math.Clamp(int.Parse(q.Value, CultureInfo.InvariantCulture), 0, 100);
                else errors.Add(L("Сигнал Wi-Fi недоступен"));
                var rssi = Regex.Match(Field("rssi", "уровень rssi") ?? "", @"-?\d+");
                if (rssi.Success && int.TryParse(rssi.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var dbm) && dbm is >= -120 and <= 0) result["rssi"] = dbm;
                var ssid=Field("ssid"); var bssid=Field("bssid", "ap bssid", "bssid точки доступа");
                if (ssid != null) result["ssid"]=ssid; if (bssid != null) result["bssid"]=bssid;
                var band=Regex.Match(Field("band","диапазон","диапазон частот") ?? "", @"(2[.,]4|5|6)\s*(?:ghz|ггц)",RegexOptions.IgnoreCase);
                if (band.Success) result["band"]=band.Groups[1].Value.Replace(',','.');
                else if (int.TryParse(Field("channel","канал"),out int channel)) { if(channel is >0 and <=14) result["band"]="2.4"; else if(channel is >=30 and <=177) result["band"]="5"; }
                if(int.TryParse(Field("channel","канал"),out int ch)) { string? currentBand=result["band"]?.GetValue<string>(); if(currentBand is not null) result["frequencyMHz"]=currentBand=="2.4"?(ch==14?2484:2407+ch*5):currentBand=="6"?(ch==2?5935:5950+ch*5):5000+ch*5; }

    }
    private static async Task<string> RunProcess(string filename, IEnumerable<string> args, CancellationToken token, TimeSpan? maxWait = null, string? workingDirectory = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var start = new ProcessStartInfo(filename) { UseShellExecute=false, CreateNoWindow=true, RedirectStandardOutput=true, RedirectStandardError=true, StandardOutputEncoding=Encoding.GetEncoding(866), StandardErrorEncoding=Encoding.GetEncoding(866) };
        if (workingDirectory is not null) start.WorkingDirectory = workingDirectory;
        foreach(var arg in args) start.ArgumentList.Add(arg);
        var callerToken = token;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(maxWait ?? TimeSpan.FromSeconds(60)); token = timeout.Token;
        using var process=Process.Start(start) ?? throw new InvalidOperationException(L("Не удалось запустить утилиту."));
        using var registration=token.Register(() => { try { if(!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        static async Task<string> ReadOutput(Stream stream, CancellationToken ct)
        {
            using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct); var bytes=buffer.ToArray();
            try { return new UTF8Encoding(false, true).GetString(bytes); }
            catch (DecoderFallbackException) { return Encoding.GetEncoding(866).GetString(bytes); }
        }
        var output=ReadOutput(process.StandardOutput.BaseStream,token); var error=ReadOutput(process.StandardError.BaseStream,token);
        try { await process.WaitForExitAsync(token); }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested) { throw new TimeoutException(L("Время ожидания утилиты истекло.")); }
        var text=await output; var stderr=await error;
        if(process.ExitCode!=0 && string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr)?L("Код ошибки {0}",process.ExitCode):stderr.Trim());
        return text;
    }
}
