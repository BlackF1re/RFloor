using System.Text.Json.Nodes;
namespace RFloor;
public static class PlatformServices {
    public static string? DeviceModel {get;set;}
    public static Action<int,int>? HapticFeedback {get;set;}
    public static string Text(string source)=>Localization.L(source);
    public static Func<CancellationToken,Task<JsonObject>>? WifiReader {get;set;}
    public static Func<string,int,int,bool,CancellationToken,Task<string>>? IperfRunner {get;set;}
}
