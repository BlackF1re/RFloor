using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using RFloor;
using Localization=RFloor.Localization;

internal static class LocalizationChecks
{
    internal static void Run()
    {
        string previous=Localization.Language;
        string temporary=Path.Combine(Path.GetTempPath(),"RFloor-language-"+Guid.NewGuid()+".json");
        try {
            var assembly=typeof(MainWindow).Assembly;
            Dictionary<string,string> Read(string name) {
                using var stream=assembly.GetManifestResourceStream("RFloor.Localization."+name)!;
                return JsonSerializer.Deserialize<Dictionary<string,string>>(stream)!;
            }
            var packs=Localization.AvailableLanguages;
            var english=packs.Single(pack=>pack.Code=="en").Strings;
            var russian=packs.Single(pack=>pack.Code=="ru").Strings;
            if(!english.Keys.ToHashSet().SetEquals(russian.Keys))throw new Exception("Language packs have different translation keys.");
            var xaml=Read("Xaml.json");
            foreach(var (source,translation) in english) {
                string Slots(string text)=>string.Join(",",Regex.Matches(text,@"(?<!\{)\{(\d+)(?:[^{}]*)\}").Select(m=>m.Groups[1].Value).Order());
                if(Slots(source)!=Slots(translation))throw new Exception("Translation changed format arguments: "+source);
                if(source!="Русский"&&Regex.IsMatch(translation,"[А-Яа-яЁё]"))throw new Exception("Untranslated English resource: "+source);
            }
            foreach(string source in xaml.Values)if(!english.ContainsKey(source))throw new Exception("Missing XAML translation: "+source);
            Localization.SetLanguage("en");
            if(Localization.L("Сохранить")!="Save"||Localization.L("{0:0.##} м",1.25)!="1.25 m")throw new Exception("English formatting failed.");
            foreach(var (key,source) in xaml)if(!Equals(Application.Current.Resources[key],english[source]))throw new Exception("English XAML resource failed: "+key);
            var window=new MainWindow(); var host=new Window {Content=window}; Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var project = typeof(MainWindow).GetField("document",flags)!.GetValue(window);
            typeof(MainWindow).GetMethod("AddDocument",flags)!.Invoke(window,[project,null]);
            var switchMode = typeof(MainWindow).GetMethod("SwitchMode",flags)!;
            switchMode.Invoke(window,[true]);
            var heading = window.FindControl<TextBlock>("ObjectHeading")!;
            var accessPoint = window.FindControl<ComboBox>("AccessPointPicker")!;
            if(heading.Text!="Measurement points" || (accessPoint.Items[0] as ComboBoxItem)?.Content?.ToString()!="No data")
                throw new Exception("English survey labels did not load.");

            var band=(Avalonia.Controls.ComboBox)window.FindControl<Control>("BandPicker");
            if(!Equals(((Avalonia.Controls.ComboBoxItem)band.Items[1]).Content,"5 GHz"))throw new Exception("English controls did not receive their resources.");
            Localization.SetLanguage("ru"); Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            switchMode.Invoke(window,[true]);
            if(heading.Text!="Точки замера" || (accessPoint.Items[0] as ComboBoxItem)?.Content?.ToString()!="Нет данных")
                throw new Exception("Survey labels did not switch back to Russian.");
            if(Localization.L("Сохранить")!="Сохранить"||Localization.L("{0:0.##} м",1.25)!="1,25 м")throw new Exception("Russian formatting failed.");
            foreach(var (key,source) in xaml)if(!Equals(Application.Current.Resources[key],russian[source]))throw new Exception("Russian XAML resource failed: "+key);
            if(!Equals(((Avalonia.Controls.ComboBoxItem)band.Items[1]).Content,"5 ГГц"))throw new Exception("Open controls did not switch back to Russian.");

            File.WriteAllText(temporary,"{\"language\":\"en\"}");Localization.Initialize(temporary);
            if(Localization.Language!="en")throw new Exception("Saved language did not load.");
            File.WriteAllText(temporary,"invalid");Localization.Initialize(temporary);
            if(Localization.Language!="ru")throw new Exception("Invalid language preferences did not fall back safely.");
            Console.WriteLine($"Localization passed: {english.Count} translations, format arguments, live XAML resources, cultures, saved preferences and maximized startup.");
        }finally{Localization.SetLanguage(previous);if(File.Exists(temporary))File.Delete(temporary);}
    }
}

