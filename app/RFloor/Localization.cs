global using static RFloor.Localization;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace RFloor;

internal static class Localization
{
    internal sealed record LanguagePack(string Code,string Culture,string Name,Dictionary<string,string> Strings)
    {
        public override string ToString()=>Name;
    }

    private const string ResourcePrefix="RFloor.Localization.";
    internal static IReadOnlyList<LanguagePack> AvailableLanguages {get;}=typeof(Localization).Assembly
        .GetManifestResourceNames().Where(resource=>resource.StartsWith(ResourcePrefix+"Languages.",StringComparison.Ordinal) &&
            resource.EndsWith(".json",StringComparison.Ordinal))
        .Select(Read<LanguagePack>).OrderBy(pack=>pack.Name,StringComparer.Ordinal).ToArray();
    private static readonly LanguagePack DefaultLanguage=AvailableLanguages.Single(pack=>pack.Code=="ru");
    private static readonly Dictionary<string,string> Xaml=Read<Dictionary<string,string>>(ResourcePrefix+"Xaml.json");
    private static LanguagePack active=DefaultLanguage;
    internal static string Language=>active.Code;

    private static T Read<T>(string resource) where T:class
    {
        using var stream=typeof(Localization).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Missing language resource: "+resource);
        return JsonSerializer.Deserialize<T>(stream) ?? throw new InvalidOperationException("Empty language resource: "+resource);
    }

    internal static string L(string source,params object?[] arguments)
    {
        string format=active.Strings.GetValueOrDefault(source)??DefaultLanguage.Strings.GetValueOrDefault(source)??source;
        return arguments.Length==0?format:string.Format(CultureInfo.CurrentCulture,format,arguments);
    }
    internal static void SetLanguage(string language)
    {
        active=AvailableLanguages.FirstOrDefault(pack=>string.Equals(pack.Code,language,StringComparison.OrdinalIgnoreCase))??DefaultLanguage;
        var culture=CultureInfo.GetCultureInfo(active.Culture);
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
