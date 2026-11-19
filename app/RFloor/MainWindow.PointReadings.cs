using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private static IEnumerable<JsonObject> PointReadings(JsonObject point)
    {
        if(point["readings"] is JsonArray readings) return readings.OfType<JsonObject>();
        return point["measurements"] is JsonObject ? [point] : [];
    }

    private JsonObject? MapReading(JsonObject point) => ViewReadings(point).LastOrDefault();

    private static bool ReadingHasFailures(JsonObject? reading)
    {
        if(reading?["requested"] is not JsonObject requested) return false;
        var measurements=reading["measurements"] as JsonObject;
        bool Missing(string key, params string[] fields) =>
            requested[key]?.GetValue<bool>()==true && !fields.Any(field=>Numeric(measurements?[field],out _));
        return Missing("signal","rssi","quality") || Missing("download","downloadMbps") || Missing("upload","uploadMbps") ||
            requested["ping"]?.GetValue<bool>()==true && requested["pingTargets"] is JsonArray targets &&
            targets.Any(target=>measurements is null || LayerSample(measurements,new(LayerKind.Ping,target?.ToString())) is null);
    }

    private bool PointHasFailure(JsonObject point)
    {
        if(ViewReadings(point).Any(ReadingHasFailures)) return true;
        if(point["measurementState"]?.ToString() is not ("failed" or "mixed" or "unverified" or "cancelled")) return false;
        if(point["attemptBand"] is not {} attempt) return !PointReadings(point).Any();
        return attempt.ToString()==Band && (SummaryView ||
            DeviceKey(new(point["attemptDeviceId"]?.ToString(),null))==viewDevice &&
            (point["attemptAdapterId"]?.ToString()??"")==viewAdapter) && (point["attemptAccessPoint"] is null || SummaryView ||
            string.Equals(point["attemptAccessPoint"]?.ToString(),SelectedAccessPoint,StringComparison.OrdinalIgnoreCase));
    }

    private static JsonObject SavePointReading(JsonObject point, JsonObject result, JsonObject requested, string band, string? accessPoint,
        SurveySource source)
    {
        if(point["readings"] is not JsonArray readings)
        {
            readings = [];
            if(point["measurements"] is JsonObject)
            {
                var legacy = new JsonObject();
                foreach(var key in new[] {"band","accessPoint","measurements","requested","timestamp"})
                    legacy[key]=point[key]?.DeepClone();
                readings.Add(legacy);
            }
            point["readings"]=readings;
        }
        var next = new JsonObject {
            ["band"]=band, ["accessPoint"]=accessPoint, ["measurements"]=result,
            ["deviceId"]=source.Device, ["adapterId"]=source.Adapter,
            ["requested"]=requested, ["timestamp"]=DateTime.UtcNow.ToString("O")
        };
        var existing = readings.OfType<JsonObject>().FirstOrDefault(r=>ReadingSource(r)==source && r["band"]?.ToString()==band &&
            string.Equals(AccessPointId(r),AccessPointId(next),StringComparison.OrdinalIgnoreCase));
        if(existing is not null) readings[readings.IndexOf(existing)]=next;
        else readings.Add(next);

        foreach(var key in new[] {"band","accessPoint","measurements","requested","timestamp"})
            point[key]=next[key]?.DeepClone();
        return next;
    }
}
