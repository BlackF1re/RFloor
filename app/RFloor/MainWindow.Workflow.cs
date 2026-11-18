using System.Text.Json.Nodes;
namespace RFloor;
public partial class MainWindow
{
    private Point? marqueeStart,marqueeEnd;
    private bool InsideMarquee(ObjectEntry entry,Rect area)
    {
        var obj=entry.Item;
        if(entry.Kind=="decoration")return area.Contains(DecorationBounds(obj));
        if(entry.Kind is "wall" or "door")return area.Contains(End(obj,"a")) && area.Contains(End(obj,"b"));
        if(entry.Kind=="room")return area.Contains(new Rect(Number(obj["x"]),Number(obj["y"]),Number(obj["w"]),Number(obj["h"])));
        return area.Contains(new Point(Number(obj["x"]),Number(obj["y"])));
    }
    private Rectangle? marquee;
    private void SelectMarquee(Point first, Point last)
    {
        marqueeStart=marqueeEnd=null;
        ClearMarquee();
        var area=Rects.Between(first,last);
        updating=true;
        try {
            var selection=Objects.SelectedItems!;
            if (!PointerContext.Modifiers.HasFlag(KeyModifiers.Control)) selection.Clear();
            foreach (var entry in Objects.Items.Cast<ObjectEntry>().Where(v=>InsideMarquee(v,area)))
                if (!selection.Contains(entry)) selection.Add(entry);
        } finally { updating=false; }
        Objects_SelectionChanged(Objects,null!);
    }
    private void ClearMarquee()
    {
        if(marquee is null)return;
        Plan.Children.Remove(marquee);
        marquee=null;
    }
    private void DrawMarquee()
    {
        if(marqueeStart is not Point a || marqueeEnd is not Point b)return;
        if(marquee is null)
        {
            marquee=new Rectangle {Stroke=Brushes.DarkOrange,StrokeThickness=1,Fill=new SolidColorBrush(Color.FromArgb(35,255,128,0)),IsHitTestVisible=false};
            Ui.SetZIndex(marquee,150);
        }
        marquee.Width=Math.Abs(b.X-a.X)*Scale;
        marquee.Height=Math.Abs(b.Y-a.Y)*Scale;
        Canvas.SetLeft(marquee,Screen(Math.Min(a.X,b.X)));
        Canvas.SetTop(marquee,Screen(Math.Min(a.Y,b.Y)));
        if(marquee.Parent is null)Plan.Children.Add(marquee);
    }
    private void DrawWallMaterial()
    {
        var walls = Walls.Select(node => node!.AsObject()).ToList();
        foreach (var wall in walls) { var shape=new Avalonia.Controls.Shapes.Path {
            Data=WallGeometry(wall,walls), Fill=Brushes.Gray, Tag=wall, Opacity=IsDragging(wall)?.18:1, IsHitTestVisible=false,
            ZIndex=surveyMode?1:0 };
            RenderOptions.SetEdgeMode(shape,EdgeMode.Aliased); Plan.Children.Add(shape);
        }
    }
    private static string ObjectName(JsonObject item,string fallback) => item["name"]?.GetValue<string>() ?? fallback;
    private void SelectAll_Click(object sender,RoutedEventArgs e) => Objects.SelectAll();
    private async void DeleteSelection_Click(object sender,RoutedEventArgs e)
    {
        if(measurementCancellation is not null)return;
        var selected=Objects.SelectedItems.Cast<ObjectEntry>().Select(v=>v.Item).ToList();
        if (surveyMode) { await DeleteSurveyPointsAsync(selected); return; }
        CommitInlineRename();ResetPointerTools();
        if(selected.Count==0)return;
        string before=document.ToJsonString(); int count=undo.Count;
        foreach(var item in selected) { var entry=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(v=>ReferenceEquals(v.Item,item)); if(entry is null)continue; Objects.SelectedItem=entry; Delete_Click(sender,e); }
        while(undo.Count>count)undo.Pop(); undo.Push(before);
    }
    private void Objects_Rename(object sender,PointerPressedEventArgs e)
    {
        if(e.ClickCount!=2)return;
        if(e.Source is Control source && source.FindAncestorOfType<ListBoxItem>() is ListBoxItem item)
        { Objects.SelectedItem=item.Content; Rename_Click(sender,e); e.Handled=true; }
    }
    private void Rename_Click(object sender,RoutedEventArgs e)
    {
        if(Objects.SelectedItem is ObjectEntry entry) BeginInlineRename(entry);
    }
    private static string? MeasurementStateText(JsonObject point) => point["measurementState"]?.ToString() switch {
        "running"=>L("Замер выполняется…"),
        "cancelled"=>L("Замер отменён"),
        "mixed"=>L("Во время замера изменился BSSID, диапазон или состояние подключения. Повторите замер."),
        "unverified"=>L("Не удалось подтвердить диапазон и BSSID. Повторите замер после подключения к Wi-Fi."),
        "failed"=>L("Ошибка замера"),
        _=>null
    };

    private ToolTip PointTooltip(JsonObject point) => new() { Content=PointDetails(point) };

    private ScrollViewer PointDetails(JsonObject point)
    {
        var panel = new StackPanel { Margin=new Thickness(6), MaxWidth=340 };
        panel.Children.Add(new TextBlock { Text=ObjectName(point,L("Точка")), FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,0,0,6) });
        if(MeasurementStateText(point) is {} state)
            panel.Children.Add(new TextBlock {Text=state,TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkOrange});
        if(point["measurementDiagnostic"] is {} diagnostic)
            panel.Children.Add(new TextBlock {Text=diagnostic.ToString(),TextWrapping=TextWrapping.Wrap,Foreground=Brushes.DarkOrange});
        foreach(var reading in PointReadings(point).DefaultIfEmpty(point))
            AddReadingDetails(panel,reading);
        return new ScrollViewer {Content=panel,MaxHeight=400,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    }

    private void AddReadingDetails(StackPanel panel,JsonObject point)
    {
        var measurements=point["measurements"] as JsonObject;
        if(measurements is null) return;
        panel.Children.Add(new TextBlock {Text=L("{0} ГГц",point["band"])+" · "+(AccessPointId(point)??"—"),
            FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,2)});
        var requested=point["requested"] as JsonObject;
        bool Expected(string key) => requested?[key]?.GetValue<bool>() ?? false;
        void Row(string label, JsonNode? number, string kind, string unit, bool expected)
        {
            bool available=Numeric(number,out var value), uncovered=available && NoCoverage(value,kind);
            var row = new StackPanel { Orientation=Orientation.Horizontal, Margin=new Thickness(0,2,0,2) };
            row.Children.Add(new TextBlock {
                Text=uncovered?"○":available?"●":expected?"!":"○",
                Foreground=uncovered?Brushes.Gray:available?new SolidColorBrush(SampleColor(value,kind)):expected?Brushes.DarkOrange:Brushes.Gray,
                Width=20, FontWeight=FontWeights.Bold });
            row.Children.Add(new TextBlock { Text=label+": "+(uncovered?L("нет покрытия"):available?$"{value:0.##} {unit}":expected?L("нет результата"):L("не выбран")), TextWrapping=TextWrapping.Wrap, MaxWidth=300 });
            panel.Children.Add(row);
        }
        bool hasRssi=measurements?["rssi"] is not null;
        Row(L("Сигнал"), measurements?[hasRssi?"rssi":"quality"], hasRssi?"rssi":"quality", hasRssi?"dBm":"%", Expected("signal"));
        if(hasRssi && measurements?["quality"] is not null) Row(L("Качество"),measurements["quality"],"quality","%",Expected("signal"));
        var pings=measurements?["pings"] as JsonArray ?? new JsonArray();
        var targets=(requested?["pingTargets"] as JsonArray)?.Select(t=>t!.GetValue<string>())
            ?? pings.Select(p=>p?["target"]?.GetValue<string>()??"Ping");
        foreach(var target in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var ping=pings.FirstOrDefault(p=>string.Equals(p?["target"]?.GetValue<string>(),target,StringComparison.OrdinalIgnoreCase));
            panel.Children.Add(new TextBlock { Text=target, FontWeight=FontWeights.SemiBold, Margin=new Thickness(0,6,0,2) });
            Row(L("Ping, средний"),ping?["avg"],"ping",L("мс"),Expected("ping"));
            if(ping is not null) {
                Row(L("Минимум"),ping["min"],"ping",L("мс"),Expected("ping"));
                Row(L("Максимум"),ping["max"],"ping",L("мс"),Expected("ping"));
                Row(L("Потери"),ping["loss"],"loss","%",Expected("ping"));
            }
        }
        if(!targets.Any()) Row("Ping",null,"ping",L("мс"),Expected("ping"));
        Row(L("Скачивание"),measurements?["downloadMbps"],"download",L("Мбит/с"),Expected("download"));
        Row(L("Отправка"),measurements?["uploadMbps"],"upload",L("Мбит/с"),Expected("upload"));
        if(measurements?["ssid"] is not null)panel.Children.Add(new TextBlock { Text=L("Сеть: {0}", measurements["ssid"]), Margin=new Thickness(0,6,0,0) });
        if(measurements?["bssid"] is not null)panel.Children.Add(new TextBlock { Text=L("Точка доступа: {0}", measurements["bssid"]) });
        if(measurements?["frequencyMHz"] is not null)panel.Children.Add(new TextBlock { Text=L("Частота: {0} МГц", measurements["frequencyMHz"]) });
        if(measurements?["error"] is not null)panel.Children.Add(new TextBlock { Text=measurements["error"]!.ToString(), TextWrapping=TextWrapping.Wrap, Foreground=Brushes.DarkOrange });
    }
}
