using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private enum SurveyTool { None, Select, Add, Delete, Repeat }
    private bool surveyMode;
    private SurveyTool surveyTool;
    private Point? pointPreview;
    private bool placingSurveyPoint;
    private Point surveyPlacementOrigin;
    private CancellationTokenSource? measurementCancellation;
    private JsonObject? animatedPoint;
    private Control[] animatedMarker=[];
    private bool waitingForMeasurement;
    private readonly System.Diagnostics.Stopwatch measurementAnimation=new();
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
    private JsonArray Points => document["points"] as JsonArray ?? new JsonArray();
    private void LoadSurveySettings()
    {
        if((document["settings"] ?? document["planSettings"]?["surveyDefaults"]) is not JsonObject settings) return;
        CoverageRadiusInput.Value=Numeric(settings["coverageRadius"],out var radius)?(decimal)Math.Clamp(radius,.5,30):(decimal)CoverageInterpolator.SupportRadius;
        NetworkRefresh.Text=settings["networkRefreshSeconds"]?.ToString() ?? "5";
        MeasurementDelay.Value=Numeric(settings["measurementDelaySeconds"],out var delay)?Math.Clamp(delay,0,10):0;
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
        Objects.SelectionMode=compactLayout && survey?SelectionMode.Multiple|SelectionMode.Toggle:SelectionMode.Multiple;
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
    private static bool InSurveyRoom(Point point, JsonObject room) =>
        point.X>=Number(room["x"])-WallFaceTolerance && point.X<=Number(room["x"])+Number(room["w"])+WallFaceTolerance &&
        point.Y>=Number(room["y"])-WallFaceTolerance && point.Y<=Number(room["y"])+Number(room["h"])+WallFaceTolerance;

    private bool CanPlaceSurveyPoint(Point point) =>
        Rooms.OfType<JsonObject>().Any(room=>InSurveyRoom(point,room)) && !WallFaces().Any(wall=>InsideWall(point,wall));

    private void DrawPointPreview()
    {
        foreach(var control in Plan.Children.OfType<Control>().Where(c=>Equals(c.Tag,"point-preview")).ToArray())
            Plan.Children.Remove(control);
        if(!surveyMode || surveyTool != SurveyTool.Add || measurementCancellation is not null || pointPreview is not Point p || !CanPlaceSurveyPoint(p)) return;
        DrawMeasurementRays(p);
        foreach(var marker in PointMarkerParts(p, Brushes.DarkOrange, "point-preview")) Plan.Children.Add(marker);
    }

    private Point SurveyPosition(Point local)
    {
        return SnapSurveyPosition(World(local, false));
    }
    private Point SnapSurveyPosition(Point point)
    {
        bool snap = SnapActive;
        var gridPoint=snap ? GridPosition(point) : point;
        bool rawAllowed=CanPlaceSurveyPoint(point);
        if (!snap && rawAllowed) return point;
        var result=CanPlaceSurveyPoint(gridPoint) || !rawAllowed ? gridPoint : point;
        double range=snap ? Math.Min(GridSize/2,6/(Scale*PlanZoom.ScaleX)) : 0;
        double closest=double.PositiveInfinity;
        foreach (var wall in WallFaces())
        {
            bool onWall=wall.All(face=>face.Depth(point)>=-WallFaceTolerance);
            foreach (var face in wall)
            {
                var contact=OnEdge(point,face.A,face.B);
                double distance=((Vector)(contact-point)).SquaredLength;
                if (distance>=closest || (!onWall && distance>range*range) || !CanPlaceSurveyPoint(contact)) continue;
                // Round along the wall while holding the perpendicular coordinate at the face.
                var aligned=OnEdge(gridPoint,face.A,face.B);
                result=CanPlaceSurveyPoint(aligned) ? aligned : contact;
                closest=distance;
            }
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
        if (!CanPlaceSurveyPoint(point)) { Status.Text=L("Разместите точку внутри комнаты или вплотную к стене."); return; }
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

    private async Task FinishSurveyPlacement(Point local, Point viewportPoint, double hitSize, bool dragged)
    {
        double hitRadius=hitSize/(Scale*PlanZoom.ScaleX);
        if(!dragged && HitObject(World(local,false),hitRadius) is {} hit) {
            Objects.SelectedItem=hit;
            ShowTouchCard(hit,viewportPoint);
            return;
        }
        await SurveyClick(local,hitRadius);
    }

    private async Task DeleteSurveyPointsAsync(IEnumerable<JsonObject> items)
    {
        var points = items.Distinct().Where(Points.Contains).ToArray();
        if (points.Length == 0 || overlays.Count>0 || measurementCancellation is not null) return;
        var source=document;
        var dialog=new OverlayDialog(this) {
            Title=points.Length==1?L("Удалить точку замера?"):L("Удалить точки замеров?"),
            Width=420
        };
        var names=new StackPanel {Spacing=6};
        foreach (var point in points)
            names.Children.Add(new TextBlock {Text=L("{0} · X {1:0.##} м, Y {2:0.##} м",
                ObjectName(point,L("Точка")),Number(point["x"]),Number(point["y"])),TextWrapping=TextWrapping.Wrap});
        var list=new ScrollViewer {Content=names,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
        var cancel=new Button {Content=L("Отмена"),IsCancel=true};
        var delete=new Button {Content=L("Удалить")};
        cancel.Click+=(_,_)=>dialog.Cancel();
        delete.Click+=(_,_)=>dialog.Complete(true);
        var actions=OverlayDialog.Actions(cancel,delete);
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

    private async Task<string?> MeasurementConnectionError(string band,string? accessPoint,CancellationToken token)
    {
        using var probe=CancellationTokenSource.CreateLinkedTokenSource(token);
        probe.CancelAfter(TimeSpan.FromSeconds(5));
        try
        {
            var wifi=await NetworkMeasurements.ReadWifi(probe.Token);
            if(wifi["band"]?.ToString()==band && wifi["coverage"]?.GetValue<bool>()!=false &&
                (accessPoint is null || string.Equals(wifi["bssid"]?.ToString(),accessPoint,StringComparison.OrdinalIgnoreCase)))
                return null;
            return L("Подключитесь к нужной точке доступа в диапазоне {0} ГГц.",band);
        }
        catch(Exception ex) when(!token.IsCancellationRequested &&
            ex is OperationCanceledException or InvalidOperationException or IOException or System.ComponentModel.Win32Exception)
        {
            return L("Не удалось проверить текущий диапазон Wi-Fi.");
        }
    }

    private async Task MeasurePointAsync(JsonObject item, MeasurementOptions options, bool remember = true,
        bool select = false, string? targetBand = null, string? targetAccessPoint = null)
    {
        if(measurementCancellation is not null) return;
        using var cancellation=new CancellationTokenSource();
        measurementCancellation=cancellation;
        var currentDocument=document;
        bool measurementStarted=false;
        string requestedBand=targetBand??Band;
        string? requestedAccessPoint=targetBand is null?SelectedAccessPoint:targetAccessPoint;
        int delay=(int)MeasurementDelay.Value;
        SurveyPanel.IsEnabled=false; SettingsMenu.IsEnabled=false; DocumentTabs.IsEnabled=false; SurveyTools.IsEnabled=false; ModeSwitch.IsEnabled=false;
        HistoryTools.IsVisible=false; MeasureButton.IsVisible=true;
        var animation=new DispatcherTimer {Interval=TimeSpan.FromMilliseconds(33)};
        animation.Tick+=(_,_)=>UpdateMeasurementMarker();
        try
        {
            if(targetBand is not null) {
                Status.Text=L("Диапазон Wi-Fi");
                if(await MeasurementConnectionError(targetBand,targetAccessPoint,cancellation.Token) is {} error) {
                    Status.Text=error;
                    MessageBox.Show(this,error,L("Диапазон Wi-Fi"));
                    return;
                }
            }
            animatedPoint=item;
            waitingForMeasurement=delay>0;
            measurementAnimation.Restart();
            animation.Start();
            var requested=new JsonObject { ["signal"]=options.Signal,["ping"]=options.Ping,["download"]=options.Download,["upload"]=options.Upload,
                ["pingTargets"]=new JsonArray((options.Ping?options.Targets:[]).Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray()) };
            if (remember) Remember();
            item["measurementState"]="running"; item.Remove("measurementDiagnostic"); dirty=true;
            item["attemptBand"]=requestedBand;
            item["attemptAccessPoint"]=requestedAccessPoint;
            RenderPlan();
            if (select) Objects.SelectedItem=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(o=>ReferenceEquals(o.Item,item));
            if(delay>0) {
                Status.Text=L("Замер начнётся через {0} с.",delay);
                await Task.Delay(TimeSpan.FromSeconds(delay),cancellation.Token);
                if(targetBand is not null && await MeasurementConnectionError(targetBand,targetAccessPoint,cancellation.Token) is {} error) {
                    item["measurementState"]="mixed";
                    item["measurementDiagnostic"]=error;
                    Status.Text=error;
                    return;
                }
                waitingForMeasurement=false;
                measurementAnimation.Restart();
                UpdateMeasurementMarker();
            }
            Status.Text=L("Выполняется замер сети…");
            measurementStarted=true;
            if(measurementStartHaptics) PlayHaptics(2);
            var result=await NetworkMeasurements.RunSurveyAsync(options.Signal,options.Ping?options.Targets:[],options.Count,(options.Download || options.Upload)?options.Host:null,options.Port,options.Duration,cancellation.Token,options.Download,options.Upload);
            if (!ReferenceEquals(document,currentDocument) || !Points.Contains(item)) { Status.Text=L("Проект изменён, результат замера не применён."); return; }
            string? detected=result["band"]?.ToString();
            string? bssid=result["bssid"]?.ToString();
            bool offline=result["coverage"]?.GetValue<bool>()==false;
            string? state=result["measurementState"]?.ToString();
            if(targetBand is not null && (detected!=targetBand || targetAccessPoint is not null &&
                !string.Equals(bssid,targetAccessPoint,StringComparison.OrdinalIgnoreCase))) state="mixed";
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
            if(ReadingHasFailures(MapReadingFor(item,detected??requestedBand,bssid??requestedAccessPoint)))
                item["measurementState"]="partial";
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
            Status.Text=item["measurementState"]?.ToString()=="complete" ? L("Замер завершён.") : L("Замер завершён с ошибками — подробности в свойствах точки.");
        }
        catch(OperationCanceledException) {
            if(ReferenceEquals(animatedPoint,item))item["measurementState"]="cancelled";
            Status.Text=L("Замер отменён.");
        }
        catch(Exception ex) {
            if(ReferenceEquals(animatedPoint,item)) {
                item["measurementState"]="failed";
                item["measurementDiagnostic"]=ex.Message;
            }
            Status.Text=L("Ошибка замера: ") + ex.Message;
        }
        finally
        {
            SurveyPanel.IsEnabled=true; SettingsMenu.IsEnabled=true; DocumentTabs.IsEnabled=true; SurveyTools.IsEnabled=true; ModeSwitch.IsEnabled=true;
            animation.Stop();
            measurementAnimation.Reset();
            animatedPoint=null; animatedMarker=[];
            measurementCancellation=null; MeasureButton.IsVisible=false; HistoryTools.IsVisible=true;
            if(ReferenceEquals(document,currentDocument) && Points.Contains(item)) {
                if(measurementStarted && !cancellation.IsCancellationRequested && measurementEndHaptics)
                    PlayHaptics(item["measurementState"]?.ToString()=="complete"?2:3);
                RenderPlan();
            }
        }
    }
    private const double SurveyDotDiameter=2.15, SurveyRingStroke=.5, SurveyRingSpacing=1.5;
    private const double SurveyInnerRingSize=4+SurveyRingStroke;
    private const double SurveyMarkerSize=SurveyInnerRingSize+2*SurveyRingSpacing;
    private const double SurveyFocusSize=SurveyMarkerSize+2*SurveyRingSpacing;
    private const int SurveyMarkerLayer=152;
    private Border? hoveredSurveyPoint;
    private Ellipse? surveyHoverRing;

    private void SetHoveredSurveyPoint(Border? hit)
    {
        if(ReferenceEquals(hoveredSurveyPoint,hit))return;
        hoveredSurveyPoint=hit;
        RefreshSurveyHoverRing();
    }

    private void RefreshSurveyHoverRing()
    {
        if(surveyHoverRing is not null)Plan.Children.Remove(surveyHoverRing);
        surveyHoverRing=null;
        if(!surveyMode || hoveredSurveyPoint is not { DataContext:JsonObject point } hit || ReferenceEquals(animatedPoint,point) ||
           !ReferenceEquals(hit.Parent,Plan) ||
           Objects.SelectedItems?.OfType<ObjectEntry>().Any(entry=>ReferenceEquals(entry.Item,point))==true) return;
        var position=new Point(Number(point["x"]),Number(point["y"]));
        surveyHoverRing=CreatePointMarker(position,PointHasFailure(point,MapReading(point))?Brushes.Firebrick:Brushes.DarkOrange,"point-hover",SurveyFocusSize);
        Plan.Children.Add(surveyHoverRing);
    }

    private void RenderPoints()
    {
        var previousHover=hoveredSurveyPoint?.DataContext;
        hoveredSurveyPoint=null;
        int index=0;
        foreach(var point in Points.OfType<JsonObject>()) {
            index++;
            var sample=Sample(point);
            const double hitSize = 14;
            var position = new Point(Number(point["x"]), Number(point["y"]));
            var reading=MapReading(point);
            bool failed=PointHasFailure(point,reading);
            bool pending=ReferenceEquals(animatedPoint,point);
            bool placeholder=!pending && !(failed && reading is null) && (activeLayer.Kind==LayerKind.Off?reading is null:sample is null);
            var color=pending?Brushes.Gray:failed?Brushes.Firebrick:Brushes.DarkOrange;
            var marker=placeholder
                ? [CreatePointMarker(position,color,point,SurveyMarkerSize,fill:color)]
                : PointMarkerParts(position,color,point);
            var hit = new Border { Width=hitSize, Height=hitSize, Background=Brushes.Transparent,
                UseLayoutRounding=false, Tag="survey-point-hit",DataContext=point,ZIndex=SurveyMarkerLayer+1 };
            if(!compactLayout) hit.Tip=PointTooltip(point);
            if(ReferenceEquals(previousHover,point))hoveredSurveyPoint=hit;
            Canvas.SetLeft(hit, Screen(position.X)-hitSize/2);
            Canvas.SetTop(hit, Screen(position.Y)-hitSize/2);
            foreach(var part in marker) Plan.Children.Add(part);
            if(pending) { animatedMarker=marker; UpdateMeasurementMarker(); }
            Plan.Children.Add(hit);
            string value=sample is { } v ? $"{v.Value:0.#} {CoverageUnit(v.Kind)}" : L("нет замера");

            Objects.Items.Add(new ObjectEntry($"{index}. {point["name"]?.GetValue<string>() ?? L("Точка")} · {value}",point,"point"));
        }
    }
    private Control[] PointMarkerParts(Point position, IBrush color, object tag) =>
        [CreatePointMarker(position,color,tag,SurveyDotDiameter,fill:color),CreatePointMarker(position,color,tag,SurveyInnerRingSize),
         CreatePointMarker(position,color,tag,SurveyMarkerSize)];

    private void UpdateMeasurementMarker()
    {
        double seconds=measurementAnimation.Elapsed.TotalSeconds;
        for(int i=0;i<animatedMarker.Length;i++)
            animatedMarker[i].Opacity=MeasurementMarkerOpacity(seconds,waitingForMeasurement,i);
    }

    internal static double MeasurementMarkerOpacity(double seconds,bool waiting,int index)
    {
        const double interval=.5,fade=.18;
        int frame=(int)(seconds/interval);
        double progress=Math.Clamp((seconds-frame*interval)/fade,0,1);
        progress=progress*progress*(3-2*progress);
        if(waiting)
        {
            if(index!=0)return 0;
            if(frame==0)return 1;
            return frame%2==0?progress:1-progress;
        }
        if(frame==0)return index==0?1:0;
        if(index==frame%3)return progress;
        return index==(frame-1)%3?1-progress:0;
    }

    private Ellipse CreatePointMarker(Point position, IBrush stroke, object? tag, double size, double opacity = 1, IBrush? fill = null)
    {
        var marker = new Ellipse {
            Width=size, Height=size, Stroke=fill is null?stroke:null, StrokeThickness=SurveyRingStroke,Fill=fill,
            IsHitTestVisible=false, UseLayoutRounding=false, Tag=tag, Opacity=opacity, ZIndex=SurveyMarkerLayer,
            Clip=SurveyMarkerClip(position,size)
        };
        Canvas.SetLeft(marker, Screen(position.X)-size/2);
        Canvas.SetTop(marker, Screen(position.Y)-size/2);
        return marker;
    }
}
