using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private bool surveyMode, addingPoint;
    private Point? pointPreview;
    private CancellationTokenSource? measurementCancellation;
    private double CoverageRadius => (double)(CoverageRadiusInput.Value ?? (decimal)CoverageInterpolator.SupportRadius);
    private void CoverageRadius_Changed(object? sender,NumericUpDownValueChangedEventArgs e)
    {
        if(!IsInitialized || loadingSettings || e.NewValue is not decimal value) return;
        EnsureSurveyDocument();
        if(Numeric(document["settings"]?["coverageRadius"],out var previous) && previous==(double)value) return;
        Remember();
        document["settings"]!["coverageRadius"]=(double)value;
        dirty=true;
        RenderPlan();
    }
    private string Band => (BandPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "5";
    private string Metric => (MetricPicker.SelectedItem as ComboBoxItem)?.Tag as string ?? "signal";
    private JsonArray Points => document["points"] as JsonArray ?? new JsonArray();
    private void LoadSurveySettings()
    {
        if((document["settings"] ?? document["planSettings"]?["surveyDefaults"]) is not JsonObject settings) return;
        CoverageRadiusInput.Value=Numeric(settings["coverageRadius"],out var radius)?(decimal)Math.Clamp(radius,.5,30):(decimal)CoverageInterpolator.SupportRadius;
        NetworkRefresh.Text=settings["networkRefreshSeconds"]?.ToString() ?? "5";
        RunSignal.IsChecked=settings["runSignal"]?.GetValue<bool>() ?? true; RunPing.IsChecked=settings["runPing"]?.GetValue<bool>() ?? true; RunIperf.IsChecked=settings["runIperf"]?.GetValue<bool>() ?? true; RunUpload.IsChecked=settings["runUpload"]?.GetValue<bool>() ?? settings["runIperf"]?.GetValue<bool>() ?? false;
        if(settings["pingTargets"] is JsonArray targets) PingTargets.Text=string.Join(", ",targets.Select(t=>t?.GetValue<string>()));
        PingCount.Text=settings["pingCount"]?.ToString() ?? "4"; IperfHost.Text=settings["iperfHost"]?.GetValue<string>() ?? "192.168.2.1"; IperfPort.Text=settings["iperfPort"]?.ToString() ?? "5201"; IperfDuration.Text=settings["iperfDuration"]?.ToString() ?? "5";
    }
    private void EditMode_Click(object sender, RoutedEventArgs e) => SwitchMode(false);
    private void SurveyMode_Click(object sender, RoutedEventArgs e) { SwitchMode(true); }
    private void SwitchMode(bool survey)
    {
        if (measurementCancellation is not null || activeSession is null) return;
        CommitInlineRename(); ResetPointerTools(); surveyMode=survey; addingPoint=false; EditorPanel.IsVisible=survey?false:true; SurveyPanel.IsVisible=survey?true:false;
        ObjectHeading.Text=survey?L("РўРѕС‡РєРё Р·Р°РјРµСЂР°"):L("РћР±СЉРµРєС‚С‹"); ObjectSubtitle.Text=survey?L("Р’С‹Р±РµСЂРёС‚Рµ С‚РѕС‡РєСѓ, С‡С‚РѕР±С‹ РїСЂРѕСЃРјРѕС‚СЂРµС‚СЊ РµС‘ Р·Р°РјРµСЂС‹"):L("РЎС‚РµРЅС‹, РєРѕРјРЅР°С‚С‹ Рё РїСЂРѕС‘РјС‹"); MeasureButton.IsVisible=survey?true:false;

        ModeSwitch.IsChecked=survey;
        UpdateModeVisual();
        DrawingToolGroup.IsVisible=survey?false:true;
        foreach(var button in new[]{RoomTool,WallTool,DoorTool,TapeTool,RectangleTool,RoundedRectangleTool,RouterTool,EllipseTool,LineTool,TextTool}) button.IsVisible=survey?false:true;
        SurveyTools.IsVisible=survey?true:false;
        if(survey) tool="select"; SetToolVisuals();
        ToolHint.Text=survey?L("Р”РѕР±Р°РІСЊС‚Рµ С‚РѕС‡РєРё Р·Р°РјРµСЂРѕРІ Рё РїСЂРѕСЃРјР°С‚СЂРёРІР°Р№С‚Рµ РёС‚РѕРіРѕРІСѓСЋ С‚РµРїР»РѕРІСѓСЋ РєР°СЂС‚Сѓ."):L("Р’С‹Р±РµСЂРёС‚Рµ РёРЅСЃС‚СЂСѓРјРµРЅС‚ РїРѕСЃС‚СЂРѕРµРЅРёСЏ РёР»Рё РѕР±СЉРµРєС‚ РЅР° С…РѕР»СЃС‚Рµ.");
        Status.Text=survey?L("Р”РѕР±Р°РІСЊС‚Рµ С‚РѕС‡РєСѓ РЅР° РїР»Р°РЅ РёР»Рё РІС‹Р±РµСЂРёС‚Рµ СЃСѓС‰РµСЃС‚РІСѓСЋС‰СѓСЋ."):L("Р РµРґР°РєС‚РѕСЂ РїР»Р°РЅР°"); RenderPlan();
    }
    private void AddPointMode_Click(object sender, RoutedEventArgs e) { if (Rooms.Count == 0) { Status.Text=L("РЎРЅР°С‡Р°Р»Р° РґРѕР±Р°РІСЊС‚Рµ РєРѕРјРЅР°С‚Сѓ РІ СЂРµРґР°РєС‚РѕСЂРµ РїР»Р°РЅР°."); return; } if (measurementCancellation is not null) { Status.Text=L("Р”РѕР¶РґРёС‚РµСЃСЊ Р·Р°РІРµСЂС€РµРЅРёСЏ С‚РµРєСѓС‰РµРіРѕ Р·Р°РјРµСЂР° РёР»Рё РѕС‚РјРµРЅРёС‚Рµ РµРіРѕ."); return; } SwitchMode(true); addingPoint=true; Status.Text=L("РќР°Р¶РјРёС‚Рµ РЅР° РїР»Р°РЅ, С‡С‚РѕР±С‹ СЂР°Р·РјРµСЃС‚РёС‚СЊ С‚РѕС‡РєСѓ Рё РЅР°С‡Р°С‚СЊ Р·Р°РјРµСЂ."); }
    private void SurveyView_Changed(object sender, RoutedEventArgs e) { if (IsInitialized && !loadingSettings && !updating && Plan != null && Objects != null) RenderPlan(); }
    private bool InsideSurveyRoom(Point point) => Rooms.OfType<JsonObject>().Any(r=>
        point.X>=Number(r["x"]) && point.X<=Number(r["x"])+Number(r["w"]) &&
        point.Y>=Number(r["y"]) && point.Y<=Number(r["y"])+Number(r["h"]));

    private void DrawPointPreview()
    {
        foreach(var control in Plan.Children.OfType<Control>().Where(c=>Equals(c.Tag,"point-preview")).ToArray())
            Plan.Children.Remove(control);
        if(!surveyMode || !addingPoint || pointPreview is not Point p || !InsideSurveyRoom(p)) return;
        DrawMeasurementRays(p);
        var marker=new Ellipse { Width=5, Height=5, Fill=Brushes.DarkOrange, Opacity=.7, IsHitTestVisible=false, Tag="point-preview" };
        Canvas.SetLeft(marker,Screen(p.X)-2.5);
        Canvas.SetTop(marker,Screen(p.Y)-2.5);
        Plan.Children.Add(marker);
    }

    private void SurveyClick(Point point)
    {
        if (!addingPoint) { SelectAt(point); return; }
        if (!InsideSurveyRoom(point)) { Status.Text=L("Р Р°Р·РјРµСЃС‚РёС‚Рµ С‚РѕС‡РєСѓ РІРЅСѓС‚СЂРё РєРѕРјРЅР°С‚С‹."); return; }
        try { ReadMeasurementOptions(); } catch(FormatException ex) { Status.Text=ex.Message; return; }
        EnsureSurveyDocument();
        var item=Points.OfType<JsonObject>().MinBy(p=>((Vector)(point-new Point(Number(p["x"]),Number(p["y"])))).SquaredLength);
        if(item is null || ((Vector)(point-new Point(Number(item["x"]),Number(item["y"])))).Length>=.1)
        {
            Remember();
            item=new JsonObject { ["id"]=Id(), ["name"]=L("РўРѕС‡РєР° {0}", Points.Count+1), ["x"]=point.X, ["y"]=point.Y, ["band"]=Band, ["accessPoint"]=SelectedAccessPoint };
            Points.Add(item);
            dirty=true;
        }
        addingPoint=false; pointPreview=null; RenderPlan();
        Objects.SelectedItem=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(o=>ReferenceEquals(o.Item,item));
        Status.Text=L("РўРѕС‡РєР° РґРѕР±Р°РІР»РµРЅР°. РќР°С‡РёРЅР°РµС‚СЃСЏ Р·Р°РјРµСЂвЂ¦");
        Measure_Click(this, new RoutedEventArgs());
    }
    private static bool Numeric(JsonNode? node, out double value) => double.TryParse(node?.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static string DescribePoint(JsonObject point)
    {
        var m=point["measurements"] as JsonObject;
        string Value(string key,string unit) => Numeric(m?[key], out double x) ? $"{x:0.##} {unit}" : "вЂ”";
        var ping=(m?["pings"] as JsonArray)?.FirstOrDefault(p=>p?["avg"] is JsonValue);
        string latency=Numeric(ping?["avg"], out var ms)?L("{0:0.##} РјСЃ", ms):"вЂ”";
        string pings = string.Join("\n", (m?["pings"] as JsonArray ?? new JsonArray()).Select(p => L("{0}: {1} В· РїРѕС‚РµСЂРё {2}", p?["target"]?.GetValue<string>() ?? "Ping", (Numeric(p?["avg"],out var avg) ? L("{0:0.##} РјСЃ", avg) : "вЂ”"), (Numeric(p?["loss"],out var loss) ? $"{loss:0.#}%" : "вЂ”"))));
        return L("{0}\nРЎРёРіРЅР°Р»: {1}\nPing: {2}\nРЎРєР°С‡РёРІР°РЅРёРµ: {3}\nРћС‚РїСЂР°РІРєР°: {4}\n{5} {6} {7} РњР“С†\n{8}\n{9}\n{10}", point["name"]?.GetValue<string>() ?? L("РўРѕС‡РєР°"), (m?["rssi"] is null ? Value("quality", "%") : Value("rssi", "dBm")), latency, Value("downloadMbps", L("РњР±РёС‚/СЃ")), Value("uploadMbps", L("РњР±РёС‚/СЃ")), m?["ssid"]?.GetValue<string>() ?? "", m?["band"]?.GetValue<string>() ?? "", m?["frequencyMHz"]?.ToString() ?? "", m?["bssid"]?.GetValue<string>() ?? "", pings, m?["error"]?.GetValue<string>() ?? "").Trim();
    }
    private sealed record MeasurementOptions(bool Signal, bool Ping, bool Download, bool Upload, string[] Targets, int Count, string Host, int Port, int Duration);
    private MeasurementOptions ReadMeasurementOptions()
    {
        bool signal=RunSignal.IsChecked==true, ping=RunPing.IsChecked==true, download=RunIperf.IsChecked==true, upload=RunUpload.IsChecked==true;
        var targets=PingTargets.Text.Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(ping && (targets.Length is 0 or >6 || targets.Any(x=>x.Length>253 || Uri.CheckHostName(x)==UriHostNameType.Unknown))) throw new FormatException(L("РЈРєР°Р¶РёС‚Рµ РѕС‚ 1 РґРѕ 6 РєРѕСЂСЂРµРєС‚РЅС‹С… Р°РґСЂРµСЃРѕРІ РґР»СЏ ping."));
        if(!int.TryParse(PingCount.Text,out int count) || count is <1 or >20) { if(ping)throw new FormatException(L("РљРѕР»РёС‡РµСЃС‚РІРѕ Р·Р°РїСЂРѕСЃРѕРІ ping: РѕС‚ 1 РґРѕ 20.")); count=4; }
        if(!int.TryParse(IperfPort.Text,out int port) || port is <1 or >65535) { if(download || upload)throw new FormatException(L("РџРѕСЂС‚ СЃРµСЂРІРµСЂР° iperf3: РѕС‚ 1 РґРѕ 65535.")); port=5201; }
        if(!int.TryParse(IperfDuration.Text,out int duration) || duration is <1 or >30) { if(download || upload)throw new FormatException(L("Р”Р»РёС‚РµР»СЊРЅРѕСЃС‚СЊ С‚РµСЃС‚Р° СЃРєРѕСЂРѕСЃС‚Рё: РѕС‚ 1 РґРѕ 30 СЃРµРєСѓРЅРґ.")); duration=5; }
        string host=IperfHost.Text.Trim();
        if((download || upload) && Uri.CheckHostName(host)==UriHostNameType.Unknown) throw new FormatException(L("РЈРєР°Р¶РёС‚Рµ РєРѕСЂСЂРµРєС‚РЅС‹Р№ Р°РґСЂРµСЃ СЃРµСЂРІРµСЂР° iperf3."));
        return new(signal,ping,download,upload,targets,count,host,port,duration);
    }
    private async void Measure_Click(object sender, RoutedEventArgs e)
    {
        if (measurementCancellation != null) { measurementCancellation.Cancel(); return; }
        if (Objects.SelectedItem is not ObjectEntry selected || selected.Kind!="point") { Status.Text=L("Р’С‹Р±РµСЂРёС‚Рµ С‚РѕС‡РєСѓ Р·Р°РјРµСЂР°."); return; }
        MeasurementOptions options;
        try { options=ReadMeasurementOptions(); }
        catch(FormatException ex) { Status.Text=ex.Message; return; }
        var item=selected.Item; var currentDocument=document;
        string requestedBand=Band; string? requestedAccessPoint=SelectedAccessPoint;
        SurveyPanel.IsEnabled=false; SettingsMenu.IsEnabled=false; DocumentTabs.IsEnabled=false; EditorRibbon.IsEnabled=false;
        measurementCancellation=new CancellationTokenSource(); MeasureButton.IsEnabled=true; MeasureButton.Content=L("РћС‚РјРµРЅРёС‚СЊ Р·Р°РјРµСЂ"); Status.Text=L("Р’С‹РїРѕР»РЅСЏРµС‚СЃСЏ Р·Р°РјРµСЂ СЃРµС‚РёвЂ¦");
        try
        {
            var requested=new JsonObject { ["signal"]=options.Signal,["ping"]=options.Ping,["download"]=options.Download,["upload"]=options.Upload,
                ["pingTargets"]=new JsonArray((options.Ping?options.Targets:[]).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()) };
            if(!options.Signal && !options.Ping && !options.Download && !options.Upload) {
                Status.Text=L("Р’СЃРµ Р·Р°РјРµСЂС‹ РѕС‚РєР»СЋС‡РµРЅС‹ РІ РЅР°СЃС‚СЂРѕР№РєР°С….");
                return;
            }
            Remember(); item["measurementState"]="running"; item.Remove("measurementDiagnostic"); dirty=true;
            RenderPlan();
            var result=await NetworkMeasurements.RunSurveyAsync(options.Signal,options.Ping?options.Targets:[],options.Count,(options.Download || options.Upload)?options.Host:null,options.Port,options.Duration,measurementCancellation.Token,options.Download,options.Upload);
            if (!ReferenceEquals(document,currentDocument) || !Points.Contains(item)) { Status.Text=L("РџСЂРѕРµРєС‚ РёР·РјРµРЅС‘РЅ, СЂРµР·СѓР»СЊС‚Р°С‚ Р·Р°РјРµСЂР° РЅРµ РїСЂРёРјРµРЅС‘РЅ."); return; }
            string? detected=result["band"]?.ToString();
            string? bssid=result["bssid"]?.ToString();
            bool offline=result["coverage"]?.GetValue<bool>()==false;
            string? state=result["measurementState"]?.ToString();
            if(state is null && offline && requestedAccessPoint is null) state="unverified";
            if(offline) {
                // A disconnected observation describes signal coverage only, for the explicitly selected AP.
                result["pings"]=new JsonArray();
                result.Remove("downloadMbps");
                result.Remove("uploadMbps");
            }
            bool hasMeasurement=result["pings"] is JsonArray {Count: >0} ||
                new[]{"rssi","quality","uploadMbps","downloadMbps"}.Any(key=>Numeric(result[key],out _));
            if(state is null && !hasMeasurement) state="failed";
            item["measurementState"]=state ?? "complete";
            item["measurementDiagnostic"]=(result["wifiError"] ?? (state=="failed"?result["error"]:null))?.DeepClone();
            if(state is not null) {
                Status.Text=MeasurementStateText(item)!;
                return;
            }
            SavePointReading(item,result,requested,detected ?? requestedBand,bssid ?? requestedAccessPoint);
            if(detected is "2.4" or "5" or "6") {
                BandPicker.SelectedIndex=detected=="2.4"?0:detected=="5"?1:2;
                RenderPlan();
                AccessPointPicker.SelectedItem=AccessPointPicker.Items.Cast<ComboBoxItem>()
                    .FirstOrDefault(i=>string.Equals(i.Tag as string,bssid,StringComparison.OrdinalIgnoreCase));
            }
            var settings=document["settings"]!.AsObject(); settings["runSignal"]=options.Signal; settings["runPing"]=options.Ping; settings["runIperf"]=options.Download; settings["runUpload"]=options.Upload;
            settings["pingTargets"]=new JsonArray(options.Targets.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()); settings["pingCount"]=options.Count; settings["iperfHost"]=options.Host; settings["iperfPort"]=options.Port; settings["iperfDuration"]=options.Duration;
            dirty=true;
            Status.Text=result["error"] is null ? L("Р—Р°РјРµСЂ Р·Р°РІРµСЂС€С‘РЅ.") : L("Р—Р°РјРµСЂ Р·Р°РІРµСЂС€С‘РЅ СЃ РѕС€РёР±РєР°РјРё вЂ” РїРѕРґСЂРѕР±РЅРѕСЃС‚Рё РІ СЃРІРѕР№СЃС‚РІР°С… С‚РѕС‡РєРё.");
        }
        catch(OperationCanceledException) { item["measurementState"]="cancelled"; Status.Text=L("Р—Р°РјРµСЂ РѕС‚РјРµРЅС‘РЅ."); }
        catch(Exception ex) { item["measurementState"]="failed"; item["measurementDiagnostic"]=ex.Message; Status.Text=L("РћС€РёР±РєР° Р·Р°РјРµСЂР°: ") + ex.Message; }
        finally
        {
            SurveyPanel.IsEnabled=true; SettingsMenu.IsEnabled=true; DocumentTabs.IsEnabled=true; EditorRibbon.IsEnabled=true; measurementCancellation.Dispose(); measurementCancellation=null; MeasureButton.Content=L("РџРѕРІС‚РѕСЂРёС‚СЊ Р·Р°РјРµСЂ");
            if(ReferenceEquals(document,currentDocument) && Points.Contains(item)) { RenderPlan(); Objects.SelectedItem=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(o=>ReferenceEquals(o.Item,item)); }
            MeasureButton.IsEnabled=Objects.SelectedItem is ObjectEntry { Kind: "point" };
        }
    }
    private (double Value,string Kind)? Sample(JsonObject p)
    {
        var m=MapReading(p)?["measurements"] as JsonObject; if(m is null) return null;
        if(Metric=="signal") { if(Numeric(m["rssi"], out var rv)) return (rv,"rssi"); if(Numeric(m["quality"], out var qv)) return (qv,"quality"); }
        if(Metric=="ping") { var ping=(m["pings"] as JsonArray)?.FirstOrDefault(x=>x?["avg"] is JsonValue); if(Numeric(ping?["avg"], out var pv)) return (pv,"ping"); }
        if(Metric is "download" or "upload" && Numeric(m[Metric+"Mbps"], out var sv)) return (sv,Metric);
        return null;
    }
    private static bool NoCoverage(double value,string kind) => (kind=="rssi" && value<=-120) || (kind=="quality" && value<=0);
    private Color SampleColor(double value,string kind,byte alpha=255)
    {
        (double a,double b,double c)=kind switch { "rssi"=>(-90,-70,-45),"quality"=>(20,50,85),"ping"=>(150,65,20),"loss"=>(100,10,0),"download"=>(5,40,150),_=>(2,20,80) };
        if (document["settings"]?["thresholds"]?[kind] is JsonArray thresholds && thresholds.Count == 3 && Numeric(thresholds[0], out double ta) && Numeric(thresholds[1], out double tb) && Numeric(thresholds[2], out double tc) && (kind is "ping" or "loss" ? ta > tb && tb > tc : ta < tb && tb < tc)) (a,b,c)=(ta,tb,tc);
        if(kind is "ping" or "loss") { value=-value;a=-a;b=-b;c=-c; }
        double score=value<=a?0:value>=c?1:value<=b?.5*(value-a)/(b-a):.5+.5*(value-b)/(c-b);
        var low=score<.5?Color.FromRgb(255,40,30):Color.FromRgb(255,220,0); var high=score<.5?Color.FromRgb(255,220,0):Color.FromRgb(40,240,45); double t=score<.5?score*2:(score-.5)*2;
        return Color.FromArgb(alpha,(byte)(low.R+(high.R-low.R)*t),(byte)(low.G+(high.G-low.G)*t),(byte)(low.B+(high.B-low.B)*t));
    }
    private void RenderPoints()
    {
        int index=0;
        foreach(var point in Points.OfType<JsonObject>()) {
            index++;
            var sample=Sample(point);
            var dot=new Border { Width=14,Height=14,Tag=point,Background=Brushes.Transparent,Tip=PointTooltip(point),Child=new Ellipse {Width=2.5,Height=2.5,Fill=Brushes.DarkOrange,Stroke=Brushes.DarkOrange,StrokeThickness=.5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center} }; Canvas.SetLeft(dot,Screen(Number(point["x"]))-7); Canvas.SetTop(dot,Screen(Number(point["y"]))-7); Plan.Children.Add(dot);
            string unit=sample?.Kind switch { "rssi" => "dBm", "quality" => "%", "ping" => L("РјСЃ"), _ => L("РњР±РёС‚/СЃ") };
            string value=sample is { } v ? $"{v.Value:0.#} {unit}" : L("РЅРµС‚ Р·Р°РјРµСЂР°");

            Objects.Items.Add(new ObjectEntry($"{index}. {point["name"]?.GetValue<string>() ?? L("РўРѕС‡РєР°")} В· {value}",point,"point"));
        }
    }
    private void RenderHeatmap()
    {
        if(ShowHeat.IsChecked!=true) { HeatLegend.Text=L("РўРµРїР»РѕРІР°СЏ РєР°СЂС‚Р° СЃРєСЂС‹С‚Р°"); return; }
        var samples=Points.Select(n=>n!.AsObject()).Where(p=>Sample(p)!=null).ToList();
        if(samples.Count==0 || Rooms.Count==0) { HeatLegend.Text=L("РќРµС‚ РґР°РЅРЅС‹С… РґР»СЏ РІС‹Р±СЂР°РЅРЅРѕРіРѕ РїРѕРєР°Р·Р°С‚РµР»СЏ Рё РґРёР°РїР°Р·РѕРЅР°"); return; }
        var covered=samples.FirstOrDefault(p=>Sample(p) is {Kind:"rssi"} s && !NoCoverage(s.Value,s.Kind))
            ?? samples.FirstOrDefault(p=>{var sample=Sample(p)!.Value; return !NoCoverage(sample.Value,sample.Kind);});
        string kind=Sample(covered??samples[0])!.Value.Kind;
        samples=samples.Where(p=>{var sample=Sample(p)!.Value; return sample.Kind==kind || NoCoverage(sample.Value,sample.Kind);}).ToList();
        var defaults=ThresholdDefaults.First(t=>t.Key==kind).Values;
        var saved=document["settings"]?["thresholds"]?[kind] as JsonArray;
        double threshold(int i) => saved is not null && saved.Count==3 && Numeric(saved[i],out var v) ? v : defaults[i];
        string units=kind switch { "rssi"=>"dBm", "quality"=>"%", "ping"=>L("РјСЃ"), _=>L("РњР±РёС‚/СЃ") };
        HeatLegend.Text=L("РљСЂР°СЃРЅС‹Р№: {0:0.#} В· Р¶С‘Р»С‚С‹Р№: {1:0.#} В· Р·РµР»С‘РЅС‹Р№: {2:0.#} {3}\nРўРѕС‡РµРє: {4}. Р›РѕРєР°Р»СЊРЅР°СЏ РѕС†РµРЅРєР° РјРµР¶РґСѓ Р·Р°РјРµСЂР°РјРё (РјРёРЅРёРјСѓРј 3, РЅРµ РЅР° РѕРґРЅРѕР№ РїСЂСЏРјРѕР№), СЂР°РґРёСѓСЃ РѕРїРѕСЂС‹ {5:0.##} Рј. РџСЂРѕР·СЂР°С‡РЅС‹Р№ вЂ” РЅРµС‚ РїРѕРєСЂС‹С‚РёСЏ РёР»Рё РЅРµРґРѕСЃС‚Р°С‚РѕС‡РЅРѕ РґР°РЅРЅС‹С….", threshold(0), threshold(1), threshold(2), units, samples.Count,CoverageRadius);
        foreach(var node in Rooms)
        {
            var room=node!.AsObject(); double x=Number(room["x"]),y=Number(room["y"]),w=Number(room["w"]),h=Number(room["h"]); double step=Math.Max(.2,Math.Max(w,h)/100);
            var localSamples=samples.Where(p=>Number(p["x"])>=x && Number(p["x"])<=x+w && Number(p["y"])>=y && Number(p["y"])<=y+h).ToList();
            var model=BuildCoverage(localSamples,kind);
            var drawing = new DrawingGroup(); using var context = drawing.Open();
            context.DrawRectangle(Brushes.Transparent, null, new Rect(0,0,w*Scale,h*Scale));
            for(double dy=0;dy<h;dy+=step) for(double dx=0;dx<w;dx+=step)
            {
                double cellWidth=Math.Min(step,w-dx),cellHeight=Math.Min(step,h-dy);
                var estimate=model.Estimate(new Point(x+dx+cellWidth/2,y+dy+cellHeight/2));
                if(estimate is not {} value)continue;
                context.DrawRectangle(new SolidColorBrush(SampleColor(value.Value,kind,(byte)(170*value.Confidence))), null, new Rect(dx*Scale,dy*Scale,Math.Min(step,w-dx)*Scale,Math.Min(step,h-dy)*Scale));
            }
            context.Dispose();
            var image = new Image { Source = new DrawingImage(drawing), Width = w*Scale, Height = h*Scale, Stretch = Stretch.Fill, IsHitTestVisible = false };
            Canvas.SetLeft(image, Screen(x)); Canvas.SetTop(image, Screen(y)); Plan.Children.Add(image);
        }
    }
    private CoverageInterpolator BuildCoverage(IReadOnlyList<JsonObject> samples,string kind)
    {
        var values=new List<CoverageInterpolator.Sample>();
        foreach(var point in samples) {
            if(Sample(point) is not {} sample)continue;
            bool uncovered=NoCoverage(sample.Value,sample.Kind);
            if(!uncovered && sample.Kind!=kind)continue;
            values.Add(new(new Point(Number(point["x"]),Number(point["y"])),uncovered?null:sample.Value));
        }
        return new(values,(a,b)=>Walls.Any(w=>Crosses(a.X,a.Y,b.X,b.Y,w!.AsObject())),CoverageRadius);
    }
    private (double Value,double Confidence)? EstimateHeat(Point position,IReadOnlyList<JsonObject> samples,string kind)
        => BuildCoverage(samples,kind).Estimate(position);

    private static bool Crosses(double ax,double ay,double bx,double by,JsonObject wall)
    {
        double cx=Number(wall["a"]!["x"]),cy=Number(wall["a"]!["y"]),dx=Number(wall["b"]!["x"]),dy=Number(wall["b"]!["y"]);
        double rx=bx-ax,ry=by-ay,sx=dx-cx,sy=dy-cy,den=rx*sy-ry*sx;
        if(Math.Abs(den)<1e-9)return false;
        double qx=cx-ax,qy=cy-ay,t=(qx*sy-qy*sx)/den,u=(qx*ry-qy*rx)/den;
        return t>1e-5 && t<1-1e-5 && u>=-1e-5 && u<=1+1e-5;
    }
}
