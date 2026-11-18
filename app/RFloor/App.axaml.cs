using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace RFloor;
public class App : Application
{
    public MainWindow? View {get;private set;}
    public override void Initialize(){Localization.Initialize();AvaloniaXamlLoader.Load(this);}
    public override void OnFrameworkInitializationCompleted()
    {
        if(ApplicationLifetime is null){base.OnFrameworkInitializationCompleted();return;}
        var view=new MainWindow(); View=view;
        if(ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) {
            var window=new Window {Title="RFloor",Icon=new WindowIcon(AssetLoader.Open(new Uri("avares://RFloor/Assets/RFloor.ico"))),Width=1440,Height=900,MinWidth=800,MinHeight=560,WindowState=WindowState.Maximized,Content=view};
            bool approved=false;
            window.Closing+=async (_,e)=>{if(approved)return;e.Cancel=true;if(await view.RequestClose()){approved=true;window.Close();}};
            if(desktop.Args is [var plan, ..] && plan.EndsWith(".rfplan",StringComparison.OrdinalIgnoreCase))
                window.Opened+=async (_,_)=>await view.OpenLocalProjectAsync(plan);
            desktop.MainWindow=window;
        }else if(ApplicationLifetime is ISingleViewApplicationLifetime mobile)mobile.MainView=view;
        base.OnFrameworkInitializationCompleted();
    }
}
