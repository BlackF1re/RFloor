using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private enum SurveyTool { None, Select, Add, Delete, Repeat }
    private bool surveyMode;
    private SurveyTool surveyTool;
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
        PingTargets.Text=settings["pingTargets"] is JsonArray targets?string.Join(", ",targets.Select(t=>t?.GetValue<string>())):"1.1.1.1, 8.8.8.8";
        PingCount.Text=settings["pingCount"]?.ToString() ?? "4"; IperfHost.Text=settings["iperfHost"]?.GetValue<string>() ?? "192.168.2.1"; IperfPort.Text=settings["iperfPort"]?.ToString() ?? "5201"; IperfDuration.Text=settings["iperfDuration"]?.ToString() ?? "5";
    }
    private void EditMode_Click(object sender, RoutedEventArgs e) => SwitchMode(false);
    private void SurveyMode_Click(object sender, RoutedEventArgs e) { SwitchMode(true); }
    private void SwitchMode(bool survey)
    {
        if (measurementCancellation is not null || activeSession is null) return;
        CommitInlineRename(); ResetPointerTools(); surveyMode=survey; EditorPanel.IsVisible=!survey; SurveyPanel.IsVisible=survey;
        ObjectHeading.Text=survey?L("Точки замера"):L("Объекты"); ObjectSubtitle.Text=survey?L("Выберите точку, чтобы просмотреть её замеры"):L("Стены, комнаты и проёмы");
        DeleteButton.IsVisible=!survey;

        ModeSwitch.IsChecked=survey;
        UpdateModeVisual();
        RefreshPanelButtons();
        DrawingToolGroup.IsVisible=survey?false:true;
        foreach(var button in new[]{RoomTool,WallTool,DoorTool,TapeTool,RectangleTool,RoundedRectangleTool,RouterTool,EllipseTool,LineTool,TextTool}) button.IsVisible=survey?false:true;
        SurveyTools.IsVisible=survey?true:false;
        if(survey) tool="select"; SetToolVisuals();
        ToolHint.Text=survey?L("Добавьте точки замеров и просматривайте итоговую тепловую карту."):L("Выберите инструмент построения или объект на холсте.");
        Status.Text=survey?L("Добавьте точку на план или выберите существующую."):L("Редактор плана"); RenderPlan();
    }
    private void AddPointMode_Click(object sender, RoutedEventArgs e) => ToggleSurveyTool(SurveyTool.Add);
    private void SurveyTool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton {Tag: string value} && Enum.TryParse<SurveyTool>(value, out var next))
            ToggleSurveyTool(next);
    }
    private void ToggleSurveyTool(SurveyTool next)
    {
        if (measurementCancellation is not null || activeSession is null) return;
        if (!surveyMode) SwitchMode(true);
        var previous = surveyTool;
        ResetPointerTools();
        SetSurveyTool(previous == next ? SurveyTool.None : next);
        if (surveyTool == SurveyTool.Add && Rooms.Count == 0)
            Status.Text = L("Сначала добавьте комнату в редакторе плана.");
        else
            Status.Text = surveyTool switch {
                SurveyTool.Add => L("Нажмите на план, чтобы разместить точку и начать замер."),
                SurveyTool.Select => L("Нажимайте на точки или обведите их рамкой, чтобы выделить несколько."),
                SurveyTool.Delete => L("Нажмите на точку, чтобы подтвердить удаление."),
                SurveyTool.Repeat => L("Нажмите на точку, чтобы повторить замер."),
                _ => L("Добавьте точку на план или выберите существующую.")
            };
    }
    private void SetSurveyTool(SurveyTool next)
    {
        surveyTool = next;
        SelectPointsTool.IsChecked = next == SurveyTool.Select;
        AddPointTool.IsChecked = next == SurveyTool.Add;
        DeletePointsTool.IsChecked = next == SurveyTool.Delete;
        RepeatPointTool.IsChecked = next == SurveyTool.Repeat;
    }
    private void SurveyView_Changed(object sender, RoutedEventArgs e) { if (IsInitialized && !loadingSettings && !updating && Plan != null && Objects != null) RenderPlan(); }
    private bool InsideSurveyRoom(Point point) => Rooms.OfType<JsonObject>().Any(r=>
        point.X>=Number(r["x"])-WallFaceTolerance && point.X<=Number(r["x"])+Number(r["w"])+WallFaceTolerance &&
        point.Y>=Number(r["y"])-WallFaceTolerance && point.Y<=Number(r["y"])+Number(r["h"])+WallFaceTolerance);

    private void DrawPointPreview()
    {
        foreach(var control in Plan.Children.OfType<Control>().Where(c=>Equals(c.Tag,"point-preview")).ToArray())
            Plan.Children.Remove(control);
        if(!surveyMode || surveyTool != SurveyTool.Add || measurementCancellation is not null || pointPreview is not Point p || !InsideSurveyRoom(p)) return;
        DrawMeasurementRays(p);
        Plan.Children.Add(CreatePointMarker(p, Brushes.DarkOrange, "point-preview", 6, .7));
    }

    private Point SurveyPosition(Point local)
    {
        return SnapSurveyPosition(World(local, false));
    }
    private Point SnapSurveyPosition(Point point)
    {
        if (SnapEnabled.IsChecked != true || PointerContext.Modifiers.HasFlag(KeyModifiers.Shift)) return point;
        double step = GridSize;
        var gridPoint=new Point(Math.Round(point.X/step)*step, Math.Round(point.Y/step)*step);
        double range=Math.Min(step/2,6/(Scale*PlanZoom.ScaleX));
        double closest=range*range;
        var result=gridPoint;
        foreach (var wall in WallFaces())
        foreach (var face in wall)
        {
            var contact=OnEdge(point,face.A,face.B);
            double distance=((Vector)(contact-point)).SquaredLength;
            if (distance>closest || !InsideSurveyRoom(contact)) continue;
            // Keep grid alignment along the wall while holding the perpendicular coordinate at its face.
            var aligned=OnEdge(gridPoint,face.A,face.B);
            result=InsideSurveyRoom(aligned) ? aligned : contact;
            closest=distance;
        }
        return result;
    }

    private async Task SurveyClick(Point local, double hitRadius)
    {
        if (measurementCancellation is not null) return;
        var hit = HitObject(World(local, false), hitRadius);
        if (surveyTool == SurveyTool.Select) {
            if (hit is null) Objects.SelectedItems.Clear();
            else if (Objects.SelectedItems.Contains(hit)) Objects.SelectedItems.Remove(hit);
            else Objects.SelectedItems.Add(hit);
            return;
        }
        if (surveyTool == SurveyTool.Delete) {
            if (hit is not null)
                await DeleteSurveyPointsAsync(Objects.SelectedItems.Contains(hit)
                    ? Objects.SelectedItems.Cast<ObjectEntry>().Select(entry => entry.Item)
                    : [hit.Item]);
            return;
        }
        if (surveyTool == SurveyTool.Repeat) {
            if (hit is null || !TryMeasurementOptions(out var repeat)) return;
            if (!Objects.SelectedItems.Contains(hit)) Objects.SelectedItem = hit;
            await MeasurePointAsync(hit.Item, repeat);
            return;
        }
        if (surveyTool != SurveyTool.Add) return;
        var point = SurveyPosition(local);
        if (!InsideSurveyRoom(point)) { Status.Text=L("Разместите точку внутри комнаты."); return; }
        if (!TryMeasurementOptions(out var options)) return;
        Remember();
        EnsureSurveyDocument();
        var item=Points.OfType<JsonObject>().MinBy(p=>((Vector)(point-new Point(Number(p["x"]),Number(p["y"])))).SquaredLength);
        if(item is null || ((Vector)(point-new Point(Number(item["x"]),Number(item["y"])))).SquaredLength>1e-12)
        {
            item=new JsonObject { ["id"]=Id(), ["name"]=L("Точка {0}", Points.Count+1), ["x"]=point.X, ["y"]=point.Y, ["band"]=Band, ["accessPoint"]=SelectedAccessPoint };
            Points.Add(item);
        }
        pointPreview=null;
        await MeasurePointAsync(item, options, remember:false, select:true);
    }

    private async Task DeleteSurveyPointsAsync(IEnumerable<JsonObject> items)
    {
        var points = items.Distinct().Where(Points.Contains).ToArray();
        if (points.Length == 0 || overlays.Count>0 || measurementCancellation is not null) return;
        var source=document;
        var dialog=new OverlayDialog(this) {
            Title=points.Length==1?L("Удалить точку замера?"):L("Удалить точки замеров?"),
            Width=420, ShowCloseButton=false
        };
        var names=new StackPanel {Spacing=6};
        foreach (var point in points)
            names.Children.Add(new TextBlock {Text=L("{0} · X {1:0.##} м, Y {2:0.##} м",
                ObjectName(point,L("Точка")),Number(point["x"]),Number(point["y"])),TextWrapping=TextWrapping.Wrap});
        var list=new ScrollViewer {Content=names,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var cancel=new Button {Content=L("Отмена"),IsCancel=true,MinHeight=44,Margin=new Thickness(0)};
        var delete=new Button {Content=L("Удалить"),Background=Brushes.DarkRed,Foreground=Brushes.White,MinHeight=44,Margin=new Thickness(0)};
        cancel.Click+=(_,_)=>dialog.Cancel();
        delete.Click+=(_,_)=>dialog.Complete(true);
        var actions=new Grid {ColumnDefinitions=new ColumnDefinitions("*,*"),ColumnSpacing=8,Margin=new Thickness(0,16,0,0)};
        Grid.SetColumn(delete,1);
        actions.Children.Add(cancel);
        actions.Children.Add(delete);
        DockPanel.SetDock(actions,Dock.Bottom);
        dialog.Content=new DockPanel {Margin=new Thickness(20,4,20,20),Children={actions,list}};
        var confirmation=dialog.ShowAsync();
        cancel.Focus();
        if (!await confirmation || !ReferenceEquals(source,document) || !points.All(Points.Contains)) return;
        Remember();
        foreach (var point in points) Points.Remove(point);
        dirty = true;
        RenderPlan();
    }
    private static bool Numeric(JsonNode? node, out double value) => double.TryParse(node?.ToJsonString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    private static string DescribePoint(JsonObject point)
    {
        var m=point["measurements"] as JsonObject;
        string Value(string key,string unit) => Numeric(m?[key], out double x) ? $"{x:0.##} {unit}" : "—";
        var ping=(m?["pings"] as JsonArray)?.FirstOrDefault(p=>p?["avg"] is JsonValue);
        string latency=Numeric(ping?["avg"], out var ms)?L("{0:0.##} мс", ms):"—";
        string pings = string.Join("\n", (m?["pings"] as JsonArray ?? new JsonArray()).Select(p => L("{0}: {1} · потери {2}", p?["target"]?.GetValue<string>() ?? "Ping", (Numeric(p?["avg"],out var avg) ? L("{0:0.##} мс", avg) : "—"), (Numeric(p?["loss"],out var loss) ? $"{loss:0.#}%" : "—"))));
        return L("{0}\nСигнал: {1}\nPing: {2}\nСкачивание: {3}\nОтправка: {4}\n{5} {6} {7} МГц\n{8}\n{9}\n{10}", point["name"]?.GetValue<string>() ?? L("Точка"), (m?["rssi"] is null ? Value("quality", "%") : Value("rssi", "dBm")), latency, Value("downloadMbps", L("Мбит/с")), Value("uploadMbps", L("Мбит/с")), m?["ssid"]?.GetValue<string>() ?? "", m?["band"]?.GetValue<string>() ?? "", m?["frequencyMHz"]?.ToString() ?? "", m?["bssid"]?.GetValue<string>() ?? "", pings, m?["error"]?.GetValue<string>() ?? "").Trim();
    }
    private sealed record MeasurementOptions(bool Signal, bool Ping, bool Download, bool Upload, string[] Targets, int Count, string Host, int Port, int Duration);
    private MeasurementOptions ReadMeasurementOptions()
    {
        bool signal=RunSignal.IsChecked==true, ping=RunPing.IsChecked==true, download=RunIperf.IsChecked==true, upload=RunUpload.IsChecked==true;
        var targets=(PingTargets.Text ?? "").Split(',',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(ping && (targets.Length is 0 or >6 || targets.Any(x=>x.Length>253 || Uri.CheckHostName(x)==UriHostNameType.Unknown))) throw new FormatException(L("Укажите от 1 до 6 корректных адресов для ping."));
        if(!int.TryParse(PingCount.Text,out int count) || count is <1 or >20) { if(ping)throw new FormatException(L("Количество запросов ping: от 1 до 20.")); count=4; }
        if(!int.TryParse(IperfPort.Text,out int port) || port is <1 or >65535) { if(download || upload)throw new FormatException(L("Порт сервера iperf3: от 1 до 65535.")); port=5201; }
        if(!int.TryParse(IperfDuration.Text,out int duration) || duration is <1 or >30) { if(download || upload)throw new FormatException(L("Длительность теста скорости: от 1 до 30 секунд.")); duration=5; }
        string host=IperfHost.Text?.Trim() ?? "";
        if((download || upload) && Uri.CheckHostName(host)==UriHostNameType.Unknown) throw new FormatException(L("Укажите корректный адрес сервера iperf3."));
        return new(signal,ping,download,upload,targets,count,host,port,duration);
    }
    private bool TryMeasurementOptions(out MeasurementOptions options)
    {
        options = null!;
        try { options = ReadMeasurementOptions(); }
        catch (FormatException ex) { Status.Text = ex.Message; return false; }
        if (options.Signal || options.Ping || options.Download || options.Upload) return true;
        Status.Text = L("Все замеры отключены в настройках.");
        return false;
    }

    private void Measure_Click(object sender, RoutedEventArgs e) => measurementCancellation?.Cancel();

    private async Task MeasurePointAsync(JsonObject item, MeasurementOptions options, bool remember = true, bool select = false)
    {
        var currentDocument=document;
        string requestedBand=Band; string? requestedAccessPoint=SelectedAccessPoint;
        SurveyPanel.IsEnabled=false; SettingsMenu.IsEnabled=false; DocumentTabs.IsEnabled=false; SurveyTools.IsEnabled=false; ModeSwitch.IsEnabled=false;
        measurementCancellation=new CancellationTokenSource(); HistoryTools.IsVisible=false; MeasureButton.IsVisible=true; Status.Text=L("Выполняется замер сети…");
        try
        {
            var requested=new JsonObject { ["signal"]=options.Signal,["ping"]=options.Ping,["download"]=options.Download,["upload"]=options.Upload,
                ["pingTargets"]=new JsonArray((options.Ping?options.Targets:[]).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()) };
            if (remember) Remember();
            item["measurementState"]="running"; item.Remove("measurementDiagnostic"); dirty=true;
            RenderPlan();
            if (select) Objects.SelectedItem=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(o=>ReferenceEquals(o.Item,item));
            var result=await NetworkMeasurements.RunSurveyAsync(options.Signal,options.Ping?options.Targets:[],options.Count,(options.Download || options.Upload)?options.Host:null,options.Port,options.Duration,measurementCancellation.Token,options.Download,options.Upload);
            if (!ReferenceEquals(document,currentDocument) || !Points.Contains(item)) { Status.Text=L("Проект изменён, результат замера не применён."); return; }
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
                updating=true;
                try {
                    BandPicker.SelectedIndex=detected=="2.4"?0:detected=="5"?1:2;
                    UpdateAccessPoints();
                    if (bssid is not null)
                        AccessPointPicker.SelectedItem=AccessPointPicker.Items.Cast<ComboBoxItem>()
                            .FirstOrDefault(i=>string.Equals(i.Tag as string,bssid,StringComparison.OrdinalIgnoreCase));
                } finally { updating=false; }
            }
            var settings=document["settings"]!.AsObject(); settings["runSignal"]=options.Signal; settings["runPing"]=options.Ping; settings["runIperf"]=options.Download; settings["runUpload"]=options.Upload;
            settings["pingTargets"]=new JsonArray(options.Targets.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()); settings["pingCount"]=options.Count; settings["iperfHost"]=options.Host; settings["iperfPort"]=options.Port; settings["iperfDuration"]=options.Duration;
            dirty=true;
            Status.Text=result["error"] is null ? L("Замер завершён.") : L("Замер завершён с ошибками — подробности в свойствах точки.");
        }
        catch(OperationCanceledException) { item["measurementState"]="cancelled"; Status.Text=L("Замер отменён."); }
        catch(Exception ex) { item["measurementState"]="failed"; item["measurementDiagnostic"]=ex.Message; Status.Text=L("Ошибка замера: ") + ex.Message; }
        finally
        {
            SurveyPanel.IsEnabled=true; SettingsMenu.IsEnabled=true; DocumentTabs.IsEnabled=true; SurveyTools.IsEnabled=true; ModeSwitch.IsEnabled=true;
            measurementCancellation.Dispose(); measurementCancellation=null; MeasureButton.IsVisible=false; HistoryTools.IsVisible=true;
            if(ReferenceEquals(document,currentDocument) && Points.Contains(item)) RenderPlan();
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
            const double hitSize = 14;
            var position = new Point(Number(point["x"]), Number(point["y"]));
            var marker = CreatePointMarker(position, Brushes.DarkOrange, point, 6);
            var hit = new Border { Width=hitSize, Height=hitSize, Background=Brushes.Transparent, Tip=PointTooltip(point), UseLayoutRounding=false };
            if (IsDragging(point)) marker.Opacity = 0;
            Canvas.SetLeft(hit, Screen(position.X)-hitSize/2);
            Canvas.SetTop(hit, Screen(position.Y)-hitSize/2);
            Plan.Children.Add(marker);
            Plan.Children.Add(hit);
            string unit=sample?.Kind switch { "rssi" => "dBm", "quality" => "%", "ping" => L("мс"), _ => L("Мбит/с") };
            string value=sample is { } v ? $"{v.Value:0.#} {unit}" : L("нет замера");

            Objects.Items.Add(new ObjectEntry($"{index}. {point["name"]?.GetValue<string>() ?? L("Точка")} · {value}",point,"point"));
        }
    }
    private Ellipse CreatePointMarker(Point position, IBrush stroke, object? tag, double size, double opacity = 1)
    {
        var marker = new Ellipse {
            Width=size, Height=size, Stroke=stroke, StrokeThickness=1.2,
            IsHitTestVisible=false, UseLayoutRounding=false, Tag=tag, Opacity=opacity, ZIndex=2
        };
        Canvas.SetLeft(marker, Screen(position.X)-size/2);
        Canvas.SetTop(marker, Screen(position.Y)-size/2);
        return marker;
    }
    private void RenderHeatmap()
    {
        if(ShowHeat.IsChecked!=true) { HeatLegend.Text=L("Тепловая карта скрыта"); return; }
        var samples=Points.Select(n=>n!.AsObject()).Where(p=>Sample(p)!=null).ToList();
        if(samples.Count==0 || Rooms.Count==0) { HeatLegend.Text=L("Нет данных для выбранного показателя и диапазона"); return; }
        var covered=samples.FirstOrDefault(p=>Sample(p) is {Kind:"rssi"} s && !NoCoverage(s.Value,s.Kind))
            ?? samples.FirstOrDefault(p=>{var sample=Sample(p)!.Value; return !NoCoverage(sample.Value,sample.Kind);});
        string kind=Sample(covered??samples[0])!.Value.Kind;
        samples=samples.Where(p=>{var sample=Sample(p)!.Value; return sample.Kind==kind || NoCoverage(sample.Value,sample.Kind);}).ToList();
        var defaults=ThresholdDefaults.First(t=>t.Key==kind).Values;
        var saved=document["settings"]?["thresholds"]?[kind] as JsonArray;
        double threshold(int i) => saved is not null && saved.Count==3 && Numeric(saved[i],out var v) ? v : defaults[i];
        string units=kind switch { "rssi"=>"dBm", "quality"=>"%", "ping"=>L("мс"), _=>L("Мбит/с") };
        HeatLegend.Text=L("Красный: {0:0.#} · жёлтый: {1:0.#} · зелёный: {2:0.#} {3}\nТочек: {4}. Локальная оценка между замерами (минимум 3, не на одной прямой), радиус опоры {5:0.##} м. Прозрачный — нет покрытия или недостаточно данных.", threshold(0), threshold(1), threshold(2), units, samples.Count,CoverageRadius);
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
