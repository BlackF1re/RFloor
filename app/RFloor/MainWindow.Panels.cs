
namespace RFloor;
public partial class MainWindow
{
    private bool leftPanelOpen=true,rightPanelOpen=true;
    private void ToggleLeftPanel_Click(object sender,RoutedEventArgs e)=>TogglePanel(LeftSidebar,LeftPanelButton,ref leftPanelOpen,240,true);
    private void ToggleRightPanel_Click(object sender,RoutedEventArgs e)=>TogglePanel(RightSidebar,RightPanelButton,ref rightPanelOpen,250,false);
    private void TogglePanel(Border panel,Button button,ref bool open,double width,bool left)
    {
        if(compactLayout){ToggleDrawer(panel);return;}
        CommitInlineRename();open=!open;panel.IsEnabled=open;
        button.Content=left?(open?"◧ ‹":"› ◧"):(open?"› ◨":"◨ ‹");
        button.Tip=(open?L("Скрыть "):L("Показать "))+(left?L("левую панель"):L("правую панель"));
        panel.Transitions??=new Transitions {new DoubleTransition {Property=WidthProperty,Duration=TimeSpan.FromMilliseconds(140)}};panel.Width=open?width:0;
    }
}
