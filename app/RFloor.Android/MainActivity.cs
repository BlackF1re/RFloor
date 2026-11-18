using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using Avalonia.Android;
using System.Text.Json.Nodes;
namespace RFloor.Android;
[Activity(Label="RFloor",Theme="@style/RFloorTheme",Icon="@drawable/icon",MainLauncher=true,Exported=true,WindowSoftInputMode=global::Android.Views.SoftInput.AdjustResize,ConfigurationChanges=ConfigChanges.Orientation|ConfigChanges.ScreenSize|ConfigChanges.UiMode|ConfigChanges.KeyboardHidden)]
public class MainActivity : AvaloniaMainActivity {
    private bool requested;
    #pragma warning disable CS0672, CS0618
    public override async void OnBackPressed(){if(Avalonia.Application.Current is RFloor.App app && app.View is {} view){if(view.HandleBack())return;if(!await view.RequestClose())return;}base.OnBackPressed();}
    #pragma warning restore CS0672, CS0618
    protected override void OnCreate(Bundle? state){PlatformServices.LongPressFeedback=()=>Window?.DecorView?.PerformHapticFeedback(global::Android.Views.FeedbackConstants.LongPress);PlatformServices.WifiReader=ReadWifi;PlatformServices.IperfRunner=NativeIperf.Run; base.OnCreate(state);
#if RFLOOR_PLATFORM_CHECKS
        if(Intent?.GetBooleanExtra("rfloor_platform_checks",false)==true)_=PlatformChecks.Run(this);
#endif
    }
    protected override void OnResume(){base.OnResume();if(!requested){requested=true;var permissions=new List<string>{Manifest.Permission.AccessCoarseLocation,Manifest.Permission.AccessFineLocation};if(OperatingSystem.IsAndroidVersionAtLeast(33))permissions.Add(Manifest.Permission.NearbyWifiDevices);var missing=permissions.Where(p=>CheckSelfPermission(p)!=Permission.Granted).ToArray();if(missing.Length>0)RequestPermissions(missing,1);}}
    private Task<JsonObject> ReadWifi(CancellationToken token){
        token.ThrowIfCancellationRequested();
        if(CheckSelfPermission(Manifest.Permission.AccessFineLocation)!=Permission.Granted)throw new InvalidOperationException(PlatformServices.Text("Разрешите точное местоположение в настройках приложения Android."));
        var wifi=(WifiManager?)ApplicationContext!.GetSystemService(WifiService);
        #pragma warning disable CS0618, CA1422
        var info=wifi?.ConnectionInfo;
        #pragma warning restore CS0618, CA1422
        if(info is null||info.NetworkId<0)return Task.FromResult(new JsonObject {["coverage"]=false,["quality"]=0});
        string? ssid=info.SSID?.Trim('"'),bssid=info.BSSID;
        if(string.IsNullOrEmpty(bssid)||bssid=="02:00:00:00:00:00"||ssid=="<unknown ssid>")throw new InvalidOperationException(PlatformServices.Text("Включите местоположение в настройках Android для определения SSID и BSSID."));
        int frequency=info.Frequency,rssi=info.Rssi;
        if(frequency<=0 || rssi is <-120 or >0)throw new InvalidOperationException(PlatformServices.Text("Сигнал Wi-Fi недоступен"));
        return Task.FromResult(new JsonObject {["ssid"]=ssid,["bssid"]=bssid,["frequencyMHz"]=frequency,["band"]=frequency<3000?"2.4":frequency<5925?"5":"6",["rssi"]=rssi,["quality"]=Math.Clamp((rssi+100)*2,0,100),["coverage"]=true});
    }
}
