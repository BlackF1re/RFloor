using System.IO;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private TrayIcon? trayIcon;

    private static void NormalizeProject(JsonObject project)
    {
        var settings=project["settings"] as JsonObject;
        if(settings is null) {
            settings=DefaultSurveySettings();
            if(project["planSettings"] is JsonObject old) {
                foreach(var pair in old.Where(p=>p.Key!="surveyDefaults")) settings[pair.Key]=pair.Value?.DeepClone();
                if(old["surveyDefaults"] is JsonObject defaults)
                    foreach(var pair in defaults)settings[pair.Key]=pair.Value?.DeepClone();
            }
            project["settings"]=settings;
        }
        project["points"] ??= new JsonArray();
        project["format"]="rfloor.plan";
        project.Remove("planSettings");
    }

    private void InitializeStartScreen()
    {
        ShowWelcome();
        if(OperatingSystem.IsAndroid())return;
        trayIcon=new TrayIcon {Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://RFloor/Assets/RFloor.ico"))),ToolTipText="RFloor",IsVisible=true};
        trayIcon.Clicked+=(_,_)=>{if(TopLevel.GetTopLevel(this) is Avalonia.Controls.Window host){host.Show();host.Activate();}};

    }

    private void ShowWelcome()
    {
        Welcome.IsVisible=true;
        Workspace.IsEnabled=false; Workspace.Opacity=0;
        dirty=false; path=null; document=NewDocument(); undo.Clear(); redo.Clear();
        Title="RFloor";
    }

    private string SafePlanName()
    {
        string name=document["name"]?.ToString()??L("Новый план");
        return string.Concat(name.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c))+".rfplan";
    }

    private async void CreateNamedPlan()
    {
        var field=new TextBox {Text=L("Новый план"),Watermark=L("Название плана"),MinWidth=200};
        var error=new TextBlock {Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};
        var create=new Button {Content=L("Создать"),IsDefault=true};
        var cancel=new Button {Content=L("Отмена"),IsCancel=true};
        var buttons=new StackPanel {Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,16,0,0)};
        buttons.Spacing=8;buttons.Children.Add(cancel);buttons.Children.Add(create);
        var content=new StackPanel {Margin=new Thickness(16,4,16,12),Spacing=4,MaxWidth=400};
        content.Children.Add(field);
        content.Children.Add(new TextBlock {Text=L("Расширение .rfplan добавится автоматически"),Foreground=Brushes.Gray,TextWrapping=TextWrapping.Wrap});
        content.Children.Add(error);content.Children.Add(buttons);
        var dialog=new OverlayDialog(this) {Title=L("Создать план"),Content=new ScrollViewer {Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},ShowCloseButton=false,SizeToContent=SizeToContent.WidthAndHeight,};
        cancel.Click+=(_,_)=>dialog.Cancel();
        field.KeyDown+=(_,e)=>{if(e.Key==Key.Enter){create.RaiseEvent(new RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));e.Handled=true;}};
        string? name=null;
        create.Click+=(_,_)=>{
            string value=field.Text.Trim();
            if(value.EndsWith(".rfplan",StringComparison.OrdinalIgnoreCase))value=value[..^7].Trim();
            if(string.IsNullOrWhiteSpace(value)||value.IndexOfAny(Path.GetInvalidFileNameChars())>=0||value.EndsWith('.')||value.Length>120) {error.Text=L("Введите название до 120 символов без запрещённых символов файла.");return;}
            name=value;dialog.Complete(true);
        };
        dialog.Loaded+=(_,_)=>{field.Focus();field.SelectAll();};
        if(await dialog.ShowAsync()!=true)return;
        var project=NewDocument();project["name"]=name;AddDocument(project);dirty=true;RefreshTabHeaders();
    }
}
