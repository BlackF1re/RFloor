global using static RFloor.Localization;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RFloor;

internal static class Localization
{
    private static readonly Dictionary<string,string> English=Read("English.json");
    private static readonly Dictionary<string,string> Xaml=Read("Xaml.json");
    internal static string Language {get;private set;}="ru";
    private static Dictionary<string,string> Read(string name)
    {
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream("RFloor.Localization."+name)
            ?? throw new InvalidOperationException("Missing language resource: "+name);
        return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
    }
    internal static string L(string source,params object?[] arguments)
    {
        string format=Language=="en"&&English.TryGetValue(source,out var english)?english:source;
        return arguments.Length==0?format:string.Format(CultureInfo.CurrentCulture,format,arguments);
    }
    internal static void SetLanguage(string language)
    {
        Language=language=="en"?"en":"ru";
        var culture=CultureInfo.GetCultureInfo(Language=="en"?"en-US":"ru-RU");
        CultureInfo.CurrentCulture=culture;CultureInfo.CurrentUICulture=culture;
        CultureInfo.DefaultThreadCurrentCulture=culture;CultureInfo.DefaultThreadCurrentUICulture=culture;
        if(Application.Current is {} app)foreach(var (key,value) in Xaml)app.Resources[key]=L(value);
    }
    internal static void Initialize(string? preferencesPath=null)
    {
        string language="ru";
        try {
            string path=preferencesPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RFloor","preferences.json");
            if(File.Exists(path)&&new FileInfo(path).Length<64000) {
                using var json=JsonDocument.Parse(File.ReadAllText(path));
                if(json.RootElement.TryGetProperty("language",out var saved))language=saved.GetString()??"ru";
            }
        }catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException) {}
        SetLanguage(language);
    }
}
