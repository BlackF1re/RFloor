using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private static IEnumerable<JsonObject> PointReadings(JsonObject point)
    {
        if(point["readings"] is JsonArray readings) return readings.OfType<JsonObject>();
        return point["measurements"] is JsonObject ? [point] : [];
    }

    private JsonObject? MapReading(JsonObject point) => PointReadings(point).LastOrDefault(r=>
        r["band"]?.ToString()==Band &&
        string.Equals(AccessPointId(r),SelectedAccessPoint,StringComparison.OrdinalIgnoreCase));

    private static void SavePointReading(JsonObject point, JsonObject result, JsonObject requested, string band, string? accessPoint)
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
            ["requested"]=requested, ["timestamp"]=DateTime.UtcNow.ToString("O")
        };
        var existing = readings.OfType<JsonObject>().FirstOrDefault(r=>r["band"]?.ToString()==band &&
            string.Equals(AccessPointId(r),AccessPointId(next),StringComparison.OrdinalIgnoreCase));
        if(existing is not null) readings[readings.IndexOf(existing)]=next;
        else readings.Add(next);

        foreach(var key in new[] {"band","accessPoint","measurements","requested","timestamp"})
            point[key]=next[key]?.DeepClone();
    }
}
