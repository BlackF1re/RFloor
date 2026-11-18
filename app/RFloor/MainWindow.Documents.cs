using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private bool loadingSettings;

    private static JsonObject DefaultSurveySettings() => new()
    {
        ["grid"] = .1, ["defaultThickness"] = .14, ["doorWidth"] = .9,
        ["pingTargets"] = new JsonArray("1.1.1.1", "8.8.8.8"), ["pingCount"] = 4,
        ["iperfHost"] = "192.168.2.1", ["iperfPort"] = 5201, ["iperfDuration"] = 5,
        ["runPing"] = true, ["runIperf"] = true, ["runUpload"] = true, ["runSignal"] = true,
        ["networkRefreshSeconds"] = 5, ["coverageRadius"] = CoverageInterpolator.SupportRadius
    };

    private void LoadDocumentSettings()
    {
        loadingSettings = true;
        try
        {
            UpdateGridNumbers();
            ProjectName.Text = document["name"]?.GetValue<string>() ?? L("Новый план");
            var settings = document["settings"] as JsonObject ?? document["planSettings"] as JsonObject;
            Thickness.Text = Display(settings?["defaultThickness"] is null ? .14 : Number(settings["defaultThickness"]));
            DoorWidth.Text = Display(settings?["doorWidth"] is null ? .9 : Number(settings["doorWidth"]));
            double grid = settings?["grid"] is null ? .1 : Number(settings["grid"]);
            GridPicker.SelectedItem = GridPicker.Items.Cast<ComboBoxItem>().FirstOrDefault(item => double.TryParse(item.Tag as string, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) && Math.Abs(value-grid)<1e-8) ?? GridPicker.Items[1];
            if (document["settings"] is null)
            {
                NetworkRefresh.Text = "5"; RunSignal.IsChecked = true; RunPing.IsChecked = true; RunIperf.IsChecked = true; RunUpload.IsChecked = true;
                PingTargets.Text = "1.1.1.1, 8.8.8.8"; PingCount.Text = "4"; IperfHost.Text = "192.168.2.1"; IperfPort.Text = "5201"; IperfDuration.Text = "5";
            }
            LoadSurveySettings();
            if (int.TryParse(NetworkRefresh.Text, out var interval) && interval is >= 2 and <= 120) networkTimer.Interval = TimeSpan.FromSeconds(interval);
        }
        finally { loadingSettings = false; }
    }

    private void UpdateGridNumbers()
    {
        foreach(var item in GridPicker.Items.Cast<ComboBoxItem>())
            item.Content=double.Parse((string)item.Tag,System.Globalization.CultureInfo.InvariantCulture).ToString("0.##");
    }

    private void ProjectName_LostFocus(object sender, RoutedEventArgs e)
    {
        if (loadingSettings) return;
        string name = string.IsNullOrWhiteSpace(ProjectName.Text) ? L("Новый план") : ProjectName.Text.Trim();
        if (document["name"]?.GetValue<string>() == name) return;
        Remember(); document["name"] = name; ProjectName.Text = name; dirty = true; RefreshTabHeaders();
    }

    private void SettingsChanged(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || loadingSettings) return;
        if (sender == NetworkRefresh && int.TryParse(NetworkRefresh.Text, out var interval) && interval is >= 2 and <= 120) networkTimer.Interval = TimeSpan.FromSeconds(interval);
        dirty = true; RefreshTabHeaders();
    }

    private void EnsureSurveyDocument()
    {
        NormalizeProject(document);
    }

    private bool CommitDocumentSettings()
    {
        try
        {
            double thickness = Parse(Thickness, L("Толщина стены"), true), width = Parse(DoorWidth, L("Ширина проёма"), true);
            if (thickness < .05 || thickness > 1 || width < .4 || width > 2.5) throw new FormatException(L("Толщина стены должна быть от 0,05 до 1 м, ширина проёма — от 0,4 до 2,5 м."));
            var settings = document["settings"] as JsonObject ?? document["planSettings"] as JsonObject ?? new JsonObject();
            if (document["settings"] is JsonObject)
            {
                var options=ReadMeasurementOptions();
                if(!int.TryParse(NetworkRefresh.Text,out int interval) || interval is <2 or >120) throw new FormatException(L("Обновление состояния Wi-Fi: от 2 до 120 секунд."));
                settings["runSignal"]=options.Signal; settings["runPing"]=options.Ping; settings["runIperf"]=options.Download; settings["runUpload"]=options.Upload;
                settings["pingTargets"]=new JsonArray(options.Targets.Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray());
                settings["networkRefreshSeconds"]=interval; settings["pingCount"]=options.Count; settings["iperfHost"]=options.Host; settings["iperfPort"]=options.Port; settings["iperfDuration"]=options.Duration;
            }
            settings["defaultThickness"] = thickness; settings["doorWidth"] = width; settings["grid"] = GridSize;
            if (settings.Parent is null) document["settings"] = settings;
            document["name"] = string.IsNullOrWhiteSpace(ProjectName.Text) ? L("Новый план") : ProjectName.Text.Trim();
            return true;
        }
        catch (FormatException ex) { MessageBox.Show(this, ex.Message, L("Проверьте параметры"), MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
    }

    private JsonObject ExportDocument(bool survey)
    {
        NormalizeProject(document);
        return document.DeepClone().AsObject();
    }

    private void ExportMenu_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender; button.ContextMenu?.Open(button);
    }
    private void ExportPlan_Click(object sender, RoutedEventArgs e) => Export(false);
    private void ExportSurvey_Click(object sender, RoutedEventArgs e) => Export(true);
    private async void Export(bool survey) => await Save(true);
}
