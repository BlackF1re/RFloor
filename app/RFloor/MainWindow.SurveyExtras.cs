using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private readonly DispatcherTimer networkTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool networkBusy;
    private string? SelectedAccessPoint => (AccessPointPicker.SelectedItem as ComboBoxItem)?.Tag as string;
    private static string? AccessPointId(JsonObject point) => point["measurements"]?["bssid"]?.GetValue<string>() ?? point["accessPoint"]?.GetValue<string>();
    private void UpdateAccessPoints()
    {
        string? previous = SelectedAccessPoint;
        var ids = Points.Select(n => n!.AsObject()).Where(p => p["band"]?.GetValue<string>() == Band)
            .Select(AccessPointId).Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
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
    private void DrawMeasurementRays(JsonObject point)
    {
        var p = new Point(Number(point["x"]), Number(point["y"]));
        foreach (var d in new[] { new Vector(1,0), new Vector(0,1), new Vector(-1,0), new Vector(0,-1) })
        {
            double nearest = double.PositiveInfinity;
            foreach (var node in Walls)
            {
                var wall = node!.AsObject(); var a = new Point(Number(wall["a"]!["x"]),Number(wall["a"]!["y"])); var b = new Point(Number(wall["b"]!["x"]),Number(wall["b"]!["y"])); Vector s=b-a, q=a-p;
                double den=d.X*s.Y-d.Y*s.X; if (Math.Abs(den)<1e-9) continue;
                double t=(q.X*s.Y-q.Y*s.X)/den, u=(q.X*d.Y-q.Y*d.X)/den;
                if(t<1e-6 || u<0 || u>1) continue;
                double thickness = 0;
                double angle = Math.Abs(den)/Math.Max(1e-9,s.Length);
                nearest=Math.Min(nearest,Math.Max(0,t-thickness/(2*Math.Max(.1,angle))));
            }
            if (!double.IsFinite(nearest)) continue;
            var end=p+d*nearest; var line=new Line { X1=Screen(p.X),Y1=Screen(p.Y),X2=Screen(end.X),Y2=Screen(end.Y),Stroke=Brushes.DarkOrange,StrokeThickness=1.5,StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> { 3,2 },IsHitTestVisible=false,Tag=HighlightTag }; Plan.Children.Add(line);
            var label=new TextBlock { Text=L("{0:0.##} м", nearest),Background=Brushes.White,Foreground=Brushes.DarkOrange,IsHitTestVisible=false,Tag=HighlightTag }; Canvas.SetLeft(label,Screen((p.X+end.X)/2)); Canvas.SetTop(label,Screen((p.Y+end.Y)/2)); Plan.Children.Add(label);
        }
    }
}
