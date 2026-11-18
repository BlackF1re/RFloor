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
        bool changed = false;
        string? wifiError = null;
        (string? Bssid,string? Band,bool Offline)? identity = null;
        async Task<bool> Observe()
        {
            try
            {
                var reading = await ReadWifi(token);
                if (reading["error"] is {} error) wifiError = error.ToString();
                var bssid = reading["bssid"]?.ToString()?.ToUpperInvariant();
                var band = reading["band"]?.ToString();
                bool offline = reading["coverage"]?.GetValue<bool>() == false;
                if (!offline && (string.IsNullOrWhiteSpace(bssid) || band is not ("2.4" or "5" or "6"))) {
                    return false;
                }
                var current = (bssid,band,offline);
                if (identity is {} first && current != first) changed = true;
                identity ??= current;
                readings.Add(reading);
                return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                wifiError = ex.Message;
                return false;
            }
        }
        async Task<bool> ObserveBoundary() => await Observe() || await Observe();

        bool startVerified = await ObserveBoundary();
        var network = RunConfiguredAsync(false,targets,count,host,port,duration,token,download,upload);
        int observations = 1;
        bool endVerified = startVerified;
        while (signal && observations < 5 || !network.IsCompleted)
        {
            var interval = Task.Delay(450,token);
            if (signal && observations < 5) await interval;
            else await Task.WhenAny(interval,network);
            token.ThrowIfCancellationRequested();
            endVerified = await Observe();
            observations++;
        }
        var result = await network;
        if (!endVerified || !signal || targets.Length>0 || !string.IsNullOrEmpty(host))
            endVerified = await ObserveBoundary();

        if (changed || !startVerified || !endVerified || identity is null)
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
                result["error"] = string.Join("; ",new[] {result["error"]?.ToString(),wifiError ?? L("Сигнал Wi-Fi недоступен")}.OfType<string>());
        }
        return result;
    }

    internal static async Task<JsonObject> ReadWifi(CancellationToken token) {
        if(PlatformServices.WifiReader is {} native)return await native(token);
        var result=new JsonObject();var errors=new List<string>();
        if(OperatingSystem.IsWindows())
            return ParseWindowsWifi(await RunProcess("netsh.exe",["wlan","show","interfaces"],token,TimeSpan.FromSeconds(4)));
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
    internal static JsonObject ParseWindowsWifi(string text)
    {
        var adapters = new List<Dictionary<string, string>> { new(StringComparer.OrdinalIgnoreCase) };
        foreach (var line in text.Split('\n')) {
            int colon = line.IndexOf(':');
            if (colon <= 0) continue;
            string key = line[..colon].Trim();
            if (key.Equals("Name", StringComparison.OrdinalIgnoreCase) || key.Equals("Имя", StringComparison.OrdinalIgnoreCase))
                adapters.Add(new(StringComparer.OrdinalIgnoreCase));
            adapters[^1][key] = line[(colon+1)..].Trim();
        }
        JsonObject? active = null;
        foreach (var fields in adapters) {
            string? Field(params string[] names) => names.Select(n => fields.GetValueOrDefault(n)).FirstOrDefault(v => v != null);
            string? state = Field("state","состояние")?.ToLowerInvariant();
            if (state is "disconnected" or "отключено" or "отключен" or "отключён") continue;
            var bssid = Regex.Match(Field("ap bssid","bssid","bssid точки доступа","bssid тд") ?? "", @"\b(?:[a-f0-9]{2}:){5}[a-f0-9]{2}\b", RegexOptions.IgnoreCase);
            if (!bssid.Success && state is not ("connected" or "подключено" or "подключен" or "подключён")) continue;
            var reading = new JsonObject { ["coverage"]=true, ["ssid"]=Field("ssid") };
            if (bssid.Success) reading["bssid"] = bssid.Value;
            var quality = Regex.Match(Field("signal","сигнал","уровень сигнала") ?? "", @"\d+");
            if (int.TryParse(quality.Value, out int q)) reading["quality"]=Math.Clamp(q,0,100);
            var rssi = Regex.Match(Field("rssi","уровень rssi") ?? "", @"-?\d+");
            if (int.TryParse(rssi.Value, out int dbm) && dbm is >= -120 and <= 0) reading["rssi"]=dbm;
            bool hasChannel = int.TryParse(Field("channel","канал"),out int channel);
            var frequencyBand = Regex.Match(Field("band","диапазон","диапазон частот") ?? "", @"(2[.,]4|5|6)\s*(?:ghz|ггц)",RegexOptions.IgnoreCase);
            string? band = frequencyBand.Success ? frequencyBand.Groups[1].Value.Replace(',','.') :
                hasChannel ? channel switch { >0 and <=14 => "2.4", >=30 and <=177 => "5", _ => null } : null;
            if (band is not null) {
                reading["band"] = band;
                if (hasChannel) reading["frequencyMHz"] = band switch {
                    "2.4" => channel==14?2484:2407+channel*5, "6" => channel==2?5935:5950+channel*5, _ => 5000+channel*5
                };
            }
            if (bssid.Success && band is not null) return reading;
            active ??= reading;
        }

        if (active is not null) return active;
        if (adapters.Any(fields => (fields.GetValueOrDefault("state") ?? fields.GetValueOrDefault("состояние"))?.ToLowerInvariant()
            is "disconnected" or "отключено" or "отключен" or "отключён"))
            return new JsonObject { ["coverage"]=false, ["quality"]=0 };
        return new JsonObject { ["error"]=L("Сигнал Wi-Fi недоступен") };
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
