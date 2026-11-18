using System.IO;
using System.Text.Json.Nodes;

namespace RFloor;
public partial class MainWindow
{
    private bool showGrid=true,showObjectLabels=true;
    private double zoomFactor=1.25;
    private static readonly (string Id,string Label,string Gesture)[] ShortcutDefaults=[
        ("new","Новый план","Ctrl+N"),("survey","Режим замеров","Ctrl+Shift+N"),("open","Открыть","Ctrl+O"),
        ("save","Сохранить","Ctrl+S"),("saveAs","Сохранить как","Ctrl+Shift+S"),("close","Закрыть вкладку","Ctrl+W"),
        ("next","Следующая вкладка","Ctrl+Tab"),("previous","Предыдущая вкладка","Ctrl+Shift+Tab"),
        ("undo","Отменить","Ctrl+Z"),("redo","Повторить","Ctrl+Y"),("all","Выделить всё","Ctrl+A"),
        ("delete","Удалить","Delete"),("rename","Переименовать","F2"),("fit","Показать весь план","Ctrl+D0"),
        ("zoomIn","Увеличить","Ctrl+Add"),("zoomOut","Уменьшить","Ctrl+Subtract"),("settings","Настройки","Ctrl+OemComma"),
        ("select","Выделение","V"),("room","Комната","R"),("wall","Стена","W"),("door","Проём","D"),("tape","Рулетка","T"),
        ("point","Точка замера","M"),("cancel","Сбросить инструмент / отменить замер","Escape")
    ];
    private Dictionary<string,string> shortcuts=ShortcutDefaults.ToDictionary(x=>x.Id,x=>x.Gesture);
    private static readonly string PreferencesPath=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RFloor","preferences.json");
    private static string Chord(Key key,KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control)?"Ctrl+":"")+(modifiers.HasFlag(KeyModifiers.Alt)?"Alt+":"")+(modifiers.HasFlag(KeyModifiers.Shift)?"Shift+":"")+(modifiers.HasFlag(KeyModifiers.Meta)?"Win+":"")+key;
    private static string NormalizeShortcut(string text)
    {
        var parts=text.Split('+',StringSplitOptions.TrimEntries|StringSplitOptions.RemoveEmptyEntries);
        if(parts.Length==0)throw new FormatException(L("Сочетание клавиш не может быть пустым."));
        KeyModifiers mods=KeyModifiers.None;
        foreach(string part in parts[..^1])mods|=part.ToLowerInvariant() switch {"ctrl" or "control"=>KeyModifiers.Control,"alt"=>KeyModifiers.Alt,"shift"=>KeyModifiers.Shift,"win"=>KeyModifiers.Meta,_=>throw new FormatException(L("Неизвестный модификатор: ")+part)};
        string name=parts[^1] switch {"0"=>"D0",","=>"OemComma","Esc"=>"Escape","Del"=>"Delete",var other=>other};
        if(!Enum.TryParse<Key>(name,true,out var key) || !Enum.IsDefined(key) || key is Key.None or Key.None or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin)
            throw new FormatException(L("Неизвестная клавиша: ")+name);
        if(mods.HasFlag(KeyModifiers.Meta) || (mods.HasFlag(KeyModifiers.Alt) && key is Key.F4 or Key.Tab) || (mods.HasFlag(KeyModifiers.Control) && mods.HasFlag(KeyModifiers.Alt) && key==Key.Delete))throw new FormatException(L("Это сочетание зарезервировано Windows."));
        return Chord(key,mods);
    }
    private void LoadPreferences()
    {
        try {
            if(File.Exists(PreferencesPath) && new FileInfo(PreferencesPath).Length<64000 && JsonNode.Parse(File.ReadAllText(PreferencesPath)) is JsonObject prefs) {
                showGrid=prefs["showGrid"]?.GetValue<bool>()??true;showObjectLabels=prefs["showObjectLabels"]?.GetValue<bool>()??true;
                gridView=showGrid?Math.Clamp(prefs["gridView"]?.GetValue<int>()??0,0,1):2;
                GridViewButton.Content=gridView switch {0=>"▦",1=>"⠿",_=>"□"};
                labelOpacity=Math.Clamp(prefs["labelOpacity"]?.GetValue<double>()??.85,.2,1);zoomFactor=Math.Clamp(prefs["zoomFactor"]?.GetValue<double>()??1.25,1.05,1.5);
                rulerMode=Math.Clamp(prefs["rulerMode"]?.GetValue<int>()??1,0,2);SnapEnabled.IsChecked=prefs["snap"]?.GetValue<bool>()??true;
                if(prefs["shortcuts"] is JsonObject values) {
                    var candidate=new Dictionary<string,string>(shortcuts);
                    foreach(var row in ShortcutDefaults)if(values[row.Id] is JsonValue value)candidate[row.Id]=NormalizeShortcut(value.GetValue<string>());
                    if(candidate.Values.Distinct().Count()==candidate.Count)shortcuts=candidate;
                }
            }
        } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException or FormatException) {Status.Text=L("Не удалось загрузить параметры. Используются стандартные настройки.");}
        UpdateShortcutHints();
    }
    private void SavePreferences()
    {
        var prefs=new JsonObject { ["showGrid"]=showGrid,["showObjectLabels"]=showObjectLabels,["labelOpacity"]=labelOpacity,["zoomFactor"]=zoomFactor,["rulerMode"]=rulerMode,["snap"]=SnapEnabled.IsChecked==true,["shortcuts"]=new JsonObject(shortcuts.Select(x=>new KeyValuePair<string,JsonNode?>(x.Key,JsonValue.Create(x.Value)))) };
        prefs["gridView"]=gridView;
        prefs["language"]=Localization.Language;
        string temp=PreferencesPath+".tmp";
        try {Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PreferencesPath)!);File.WriteAllText(temp,prefs.ToJsonString());File.Move(temp,PreferencesPath,true);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {Status.Text=L("Не удалось сохранить параметры: ")+ex.Message;}
        finally {try{if(File.Exists(temp))File.Delete(temp);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
    private static string ShortcutDisplay(string value)=>value.Replace("OemComma",",").Replace("D0","0").Replace("Subtract","−").Replace("Add","+");
    private void UpdateShortcutHints()
    {
        void Visit(Control obj) {
            if(obj is MenuItem {Tag:string id} item && shortcuts.TryGetValue(id,out var value))item.InputGesture=KeyGesture.Parse(value);
            if(obj is ItemsControl items)foreach(var child in items.Items.OfType<Control>())Visit(child);
            else if(obj is Panel panel)foreach(Control child in panel.Children)Visit(child);
        }
        if(Content is Control root)Visit(root);
        foreach(var button in new[]{SelectTool,RoomTool,WallTool,DoorTool,TapeTool})if(button is not null) {
            string id=(string)button.Tag;var row=ShortcutDefaults.First(x=>x.Id==id);button.Tip=$"{L(row.Label)} ({ShortcutDisplay(shortcuts[id])})";
        }
    }
    private async void HandleShortcut(KeyEventArgs e)
    {
        Key key=e.Key==Key.None?e.Key:e.Key;
        string chord=Chord(key,PointerContext.Modifiers);
        string? action=shortcuts.FirstOrDefault(x=>x.Value==chord).Key;
        if(action is null)return;
        if(activeSession is null && action is not ("new" or "open")) { e.Handled=true; return; }
        bool editing=TopLevel.GetTopLevel(this)?.FocusManager?.GetFocusedElement() is TextBox;
        if(editing && action is not ("save" or "saveAs" or "open" or "new" or "survey" or "close" or "next" or "previous" or "settings"))return;
        if(measurementCancellation is not null){if(action=="cancel")measurementCancellation.Cancel();e.Handled=true;return;}
        e.Handled=true;var args=new RoutedEventArgs();
        switch(action) {
            case "new":New_Click(this,args);break;case "survey":NewSurvey_Click(this,args);break;case "open":Open_Click(this,args);break;
            case "save":await Save();break;case "saveAs":await Save(true);break;case "close":CloseTab_Click(this,args);break;
            case "next":case "previous":if(sessions.Count>1)ActivateSession(sessions[(sessions.IndexOf(activeSession!)+(action=="next"?1:sessions.Count-1))%sessions.Count]);break;
            case "undo":Undo_Click(this,args);break;case "redo":Redo_Click(this,args);break;case "all":SelectAll_Click(this,args);break;
            case "delete":DeleteSelection_Click(this,args);break;case "rename":Rename_Click(this,args);break;case "fit":Fit_Click(this,args);break;
            case "zoomIn":ZoomIn_Click(this,args);break;case "zoomOut":ZoomOut_Click(this,args);break;case "settings":Preferences_Click(this,args);break;
            case "select" when surveyMode:ToggleSurveyTool(SurveyTool.Select);break;
            case "select":case "room":case "wall":case "door":case "tape":SetTool(action);break;case "point":AddPointMode_Click(this,args);break;
            case "cancel":ResetPointerTools();RenderPlan();break;
        }
    }
    private void Preferences_Click(object sender,RoutedEventArgs e)=>ShowPreferences(0);
    private void Shortcuts_Click(object sender,RoutedEventArgs e)=>ShowPreferences(3);
}
