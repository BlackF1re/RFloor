using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool networkBusy;
    private string? SelectedAccessPoint => (AccessPointPicker.SelectedItem as ComboBoxItem)?.Tag as string;
    private static string? AccessPointId(JsonObject point) =>
        (point["measurements"] as JsonObject)?["bssid"]?.ToString() ?? point["accessPoint"]?.ToString();
    private void UpdateAccessPoints()
    {
        string? previous = SelectedAccessPoint;
        var ids = Points.OfType<JsonObject>().SelectMany(PointReadings).Where(p => p["band"]?.ToString() == Band)
            .Select(AccessPointId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existing=AccessPointPicker.Items.OfType<ComboBoxItem>().Select(i=>i.Tag as string);
        if(existing.SequenceEqual(ids.Count==0?[null]:ids,StringComparer.OrdinalIgnoreCase)) {
            if(ids.Count==0 && AccessPointPicker.Items[0] is ComboBoxItem placeholder) placeholder.Content=L("Нет данных");
            return;
        }
        AccessPointPicker.Items.Clear();
        if (ids.Count == 0) AccessPointPicker.Items.Add(new ComboBoxItem { Content = L("Нет данных"), Tag = null });
        else foreach (var id in ids) AccessPointPicker.Items.Add(new ComboBoxItem { Content = id, Tag = id });
        AccessPointPicker.SelectedItem = AccessPointPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals(i.Tag as string, previous, StringComparison.OrdinalIgnoreCase)) ?? AccessPointPicker.Items[0];
    }
    private void AccessPoint_Changed(object sender, SelectionChangedEventArgs e) { if (IsInitialized && !updating) RenderPlan(); }
    private void StartNetworkStatus()
    {
        networkTimer.Tick += async (_, _) => await RefreshNetworkStatus(); networkTimer.Start();
        Loaded += async (_, _) => await RefreshNetworkStatus();
    }
    private async Task RefreshNetworkStatus()
    {
        if (networkBusy) return; networkBusy = true;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            var info = await NetworkMeasurements.RunAsync(true, [], 1, null, 5201, 1, cts.Token);
            string? ssid = info["ssid"]?.GetValue<string>(), quality = info["quality"]?.ToString(), band = info["band"]?.GetValue<string>(), frequency = info["frequencyMHz"]?.ToString();
            NetworkStatus.Text = ssid is null && quality is null ? L("Wi-Fi: данные недоступны") : $"Wi-Fi: {ssid ?? "—"} · {quality ?? "—"}%" + (band is null ? "" : L(" · {0} ГГц", band)) + (frequency is null ? "" : L(" · {0} МГц", frequency));
        }
        catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException) { NetworkStatus.Text = L("Wi-Fi: данные недоступны"); }
        finally { networkBusy = false; }
    }

    private static readonly (string Key, string Label, double[] Values)[] ThresholdDefaults =
    [
        ("rssi", "Сигнал, dBm", [-90,-70,-45]), ("quality", "Качество, %", [20,50,85]),
        ("ping", "Ping, мс", [150,65,20]), ("loss", "Потери, %", [100,10,0]), ("download", "Скачивание, Мбит/с", [5,40,150]), ("upload", "Отправка, Мбит/с", [2,20,80])
    ];
    private void Thresholds_Click(object sender,RoutedEventArgs e)=>ShowPreferences(2);
    private void DrawMeasurementRays(Point p)
    {
        var walls=WallFaces();
        foreach (var d in new[] { new Vector(1,0), new Vector(0,1), new Vector(-1,0), new Vector(0,-1) })
        {
            double nearest = double.PositiveInfinity;
            foreach (var wall in walls) nearest=Math.Min(nearest,WallEntryDistance(wall,p,d));
            if (!double.IsFinite(nearest)) continue;
            var end=p+d*nearest;
            if (nearest>WallFaceTolerance)
            {
                Plan.Children.Add(new Line { X1=Screen(p.X),Y1=Screen(p.Y),X2=Screen(end.X),Y2=Screen(end.Y),
                    Stroke=Brushes.DarkOrange,StrokeThickness=1.5,StrokeLineCap=PenLineCap.Flat,
                    StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> { 3,2 },IsHitTestVisible=false,Tag="point-preview" });
            }
            var label=new TextBlock { Text=L("{0:0.##} м", nearest),Background=Brushes.White,Foreground=Brushes.DarkOrange,
                IsHitTestVisible=false,Tag="point-preview",ZIndex=2 };
            label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            var middle=p+d*(nearest/2);
            double x=Screen(middle.X),y=Screen(middle.Y);
            if (d.X!=0) { x+=d.X*Math.Max(0,10-nearest*Scale/2)-label.DesiredSize.Width/2; y-=label.DesiredSize.Height+3; }
            else { x+=4; y+=d.Y*Math.Max(0,10-nearest*Scale/2)-label.DesiredSize.Height/2; }
            Canvas.SetLeft(label,x); Canvas.SetTop(label,y); Plan.Children.Add(label);
        }
    }
}
