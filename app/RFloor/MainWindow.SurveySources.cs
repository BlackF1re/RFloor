using System.IO;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private readonly record struct SurveySource(string? Device, string? Adapter);

    private static readonly Lazy<string> LocalSurveyDevice=new(LoadSurveyDevice);
    private static JsonObject? localSurveyIdentity;
    private string? viewDevice, viewAdapter;
    private SurveySummary summary;
    private bool SummaryView => viewDevice is null;
    private JsonObject? SurveyDevices => document["devices"] as JsonObject;

    private static string LoadSurveyDevice()
    {
        string path=Path.Combine(Path.GetDirectoryName(PreferencesPath)!,"survey-device");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        if(!File.Exists(path)) {
            try {
                using var output=new StreamWriter(new FileStream(path,FileMode.CreateNew));
                output.Write(Guid.NewGuid().ToString("N"));
            } catch(IOException) when(File.Exists(path)) { }
        }
        return Guid.ParseExact(File.ReadAllText(path).Trim(),"N").ToString("N");
    }

    private static SurveySource ReadingSource(JsonObject reading) =>
        new(reading["deviceId"]?.ToString(),reading["adapterId"]?.ToString());

    private static string DeviceKey(SurveySource source) => source.Device ?? "";
    private static string AdapterKey(SurveySource source) => source.Adapter ?? "";

    private bool InSourceView(JsonObject reading)
    {
        var source=ReadingSource(reading);
        return SummaryView || DeviceKey(source)==viewDevice && AdapterKey(source)==viewAdapter;
    }

    private IEnumerable<JsonObject> ViewReadings(JsonObject point) => PointReadings(point).Where(r=>
        r["band"]?.ToString()==Band && InSourceView(r) && (SummaryView ||
        string.Equals(AccessPointId(r),SelectedAccessPoint,StringComparison.OrdinalIgnoreCase)));

    private string DeviceLabel(string? id)
    {
        if(string.IsNullOrEmpty(id)) return L("Устройство не указано");
        string name=SurveyDevices?[id]?["name"]?.ToString() ?? id;
        return SurveyDevices?.Any(device=>device.Key!=id && device.Value?["name"]?.ToString()==name)==true
            ? $"{name} · {id[..Math.Min(id.Length,6)]}" : name;
    }

    private string AdapterLabel(SurveySource source)
    {
        if(source.Device is null) return L("Адаптер не указан");
        var adapter=SurveyDevices?[source.Device]?["adapters"]?[AdapterKey(source)];
        string name=adapter?["name"]?.ToString() ?? L("Адаптер не указан");
        return adapter?["verified"]?.GetValue<bool>()==true ? name : L("{0} (не подтверждён)",name);
    }

    private void SurveySources_Changed(object? sender,SelectionChangedEventArgs e)
    {
        if(!IsInitialized || updating || loadingSettings || !surveyMode) return;
        if(ReferenceEquals(sender,DevicePicker)) {
            viewDevice=(DevicePicker.SelectedItem as ComboBoxItem)?.Tag as string;
            viewAdapter=null;
        } else if(ReferenceEquals(sender,AdapterPicker)) {
            viewAdapter=(AdapterPicker.SelectedItem as ComboBoxItem)?.Tag as string;
        } else if((SummaryPicker.SelectedItem as ComboBoxItem)?.Tag is SurveySummary mode) {
            summary=mode;
        }
        HideTouchCard();
        RenderPlan();
    }

    private static string? UpdatePicker(ComboBox picker,IEnumerable<(string? Key,string Label)> choices,string? selection)
    {
        var items=choices.ToArray();
        if(!picker.Items.OfType<ComboBoxItem>().Select(i=>(i.Tag as string,i.Content as string ?? "")).SequenceEqual(items)) {
            picker.Items.Clear();
            foreach(var (key,label) in items) picker.Items.Add(new ComboBoxItem {Tag=key,Content=label});
        }
        var chosen=picker.Items.OfType<ComboBoxItem>().FirstOrDefault(i=>
            string.Equals(i.Tag as string,selection,StringComparison.OrdinalIgnoreCase)) ?? picker.Items.OfType<ComboBoxItem>().FirstOrDefault();
        picker.SelectedItem=chosen;
        return chosen?.Tag as string;
    }

    private void UpdateSurveySources()
    {
        var sources=Points.OfType<JsonObject>().SelectMany(PointReadings).Select(ReadingSource)
            .Concat((SurveyDevices?.AsEnumerable() ?? []).SelectMany(device=>
                (device.Value?["adapters"] as JsonObject)?.Select(adapter=>new SurveySource(device.Key,adapter.Key)) ?? []))
            .Distinct().ToArray();
        var devices=sources.Select(DeviceKey).Distinct().OrderBy(DeviceLabel,StringComparer.CurrentCulture);
        viewDevice=UpdatePicker(DevicePicker,new (string?,string)[]{(null,L("Все устройства"))}
            .Concat(devices.Select(id=>((string?)id,DeviceLabel(id)))),viewDevice);
        AdapterOptions.IsVisible=!SummaryView;
        SummaryOptions.IsVisible=SummaryView;
        AccessPointOptions.IsVisible=!SummaryView;
        var adapters=sources.Where(s=>DeviceKey(s)==viewDevice).OrderBy(AdapterLabel,StringComparer.CurrentCulture);
        viewAdapter=UpdatePicker(AdapterPicker,adapters.Select(s=>((string?)AdapterKey(s),AdapterLabel(s))),viewAdapter);
        foreach(var mode in Enum.GetValues<SurveySummary>()) {
            if(SummaryPicker.Items.Count<=(int)mode) SummaryPicker.Items.Add(new ComboBoxItem {Tag=mode});
            if(SummaryPicker.Items[(int)mode] is ComboBoxItem item) item.Content=SummaryLabel(mode);
        }
        SummaryPicker.SelectedIndex=(int)summary;
    }

    private static string SummaryLabel(SurveySummary mode) => mode switch {
        SurveySummary.Minimum=>L("Минимум"), SurveySummary.Maximum=>L("Максимум"), _=>L("Среднее")
    };

    private async Task<JsonObject?> IdentifySurveyDevice()
    {
        string id=LocalSurveyDevice.Value;
        if(SurveyDevices?[id] is JsonObject saved) return saved;
        if(localSurveyIdentity is not null) return localSurveyIdentity;
        string? model=ReadSurveyDeviceModel();
        var field=new TextBox {Text=OperatingSystem.IsAndroid()?model??Environment.MachineName:Environment.MachineName,MaxLength=100};
        var cancel=new Button {Content=L("Отмена"),IsCancel=true};
        var confirm=new Button {Content=L("Подтвердить"),IsDefault=true,IsEnabled=!string.IsNullOrWhiteSpace(field.Text)};
        field.TextChanged+=(_,_)=>confirm.IsEnabled=!string.IsNullOrWhiteSpace(field.Text);
        var dialog=new OverlayDialog(this) {
            Title=L("Устройство для замеров"),Width=420,
            Content=new StackPanel {Margin=new Thickness(16),Spacing=8,Children={
                new TextBlock {Text=L("Имя устройства"),TextWrapping=TextWrapping.Wrap},field,
                OverlayDialog.Actions(cancel,confirm)}}
        };
        confirm.Click+=(_,_)=>dialog.Complete(true);
        if(!await dialog.ShowAsync()) return null;
        localSurveyIdentity=new JsonObject {["name"]=field.Text!.Trim(),["model"]=model};
        return localSurveyIdentity;
    }

    private static string? ReadSurveyDeviceModel()
    {
        if(PlatformServices.DeviceModel is {} model) return model;
        try {
            if(OperatingSystem.IsWindows())
                return Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\BIOS",
                    "SystemProductName",null) as string;
            const string linuxModel="/sys/devices/virtual/dmi/id/product_name";
            if(OperatingSystem.IsLinux() && File.Exists(linuxModel)) return File.ReadAllText(linuxModel).Trim();
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        return null;
    }

    private SurveySource RegisterSurveySource(JsonObject identity,JsonObject wifi)
    {
        string deviceId=LocalSurveyDevice.Value;
        string adapterId=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(deviceId+":"+(wifi["adapterId"]?.ToString()??"unknown"))))[..24];
        if(document["devices"] is not JsonObject devices) document["devices"]=devices=new JsonObject();
        if(devices[deviceId] is not JsonObject device) devices[deviceId]=device=identity.DeepClone().AsObject();
        if(device["adapters"] is not JsonObject adapters) device["adapters"]=adapters=new JsonObject();
        if(adapters[adapterId] is null) adapters[adapterId]=new JsonObject {
            ["name"]=wifi["adapterName"]?.ToString() ?? L("Wi-Fi"),
            ["verified"]=wifi["adapterVerified"]?.GetValue<bool>() ?? false
        };
        return new(deviceId,adapterId);
    }
}
