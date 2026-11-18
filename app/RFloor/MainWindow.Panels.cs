namespace RFloor;

public partial class MainWindow
{
    private bool leftPanelOpen=true, rightPanelOpen=true;
    private double leftPanelWidth=240, rightPanelWidth=250, leftDrawerWidth=304, rightDrawerWidth=304;
    private readonly Dictionary<Border,IDisposable> panelAnimations=[];
    private static readonly TimeSpan PanelDuration=TimeSpan.FromMilliseconds(145);

    private void ToggleLeftPanel_Click(object sender,RoutedEventArgs e)=>TogglePanel(LeftSidebar);
    private void ToggleRightPanel_Click(object sender,RoutedEventArgs e)=>TogglePanel(RightSidebar);

    private double DrawerWidth(bool left)=>Math.Min(left?leftDrawerWidth:rightDrawerWidth,Math.Max(0,Bounds.Width-32));
    private double DesktopPanelWidth(bool left) => Math.Clamp(left?leftPanelWidth:rightPanelWidth,190,Math.Max(190,(Bounds.Width-360)/2));

    private void ApplyPanelWidths()
    {
        foreach(var panel in new[]{LeftSidebar,RightSidebar}) {
            bool left=panel==LeftSidebar;
            if(compactLayout) panel.Width=DrawerWidth(left);
            else {
                bool open=left?leftPanelOpen:rightPanelOpen;
                if(panelAnimations.ContainsKey(panel) && !open) continue;
                panel.Width=open?DesktopPanelWidth(left):0;
                panel.IsVisible=panel.IsEnabled=open;
            }
        }
    }

    private void TogglePanel(Border panel)
    {
        CommitInlineRename();
        if(compactLayout) { ToggleDrawer(panel);return; }
        bool left=panel==LeftSidebar;
        bool open=left ? (leftPanelOpen=!leftPanelOpen) : (rightPanelOpen=!rightPanelOpen);
        AnimatePanel(panel,open,DesktopPanelWidth(left));
        RefreshPanelButtons();
    }

    private void ToggleDrawer(Border panel)
    {
        bool open=!panel.IsEnabled;
        CloseDrawers();
        if(open) AnimatePanel(panel,true,DrawerWidth(panel==LeftSidebar));
        drawerShade.IsVisible=open;
        RefreshPanelButtons();
    }

    private void CloseDrawers()
    {
        if(!compactLayout)return;
        foreach(var panel in new[]{LeftSidebar,RightSidebar})
            if(panel.IsEnabled)AnimatePanel(panel,false,panel.Width);
        drawerShade.IsVisible=false;
        RefreshPanelButtons();
    }

    private void AnimatePanel(Border panel,bool open,double width)
    {
        if(panelAnimations.Remove(panel,out var previous))previous.Dispose();
        double away=(panel==LeftSidebar?-1:1)*(width+8);
        var transform=panel.RenderTransform as TranslateTransform ?? new TranslateTransform(!panel.IsVisible?away:0,0);
        panel.RenderTransform=transform;
        transform.Transitions??=new Transitions {new DoubleTransition {Property=TranslateTransform.XProperty,Duration=PanelDuration}};
        panel.Width=width;
        panel.IsVisible=true;
        panel.IsEnabled=open;
        transform.X=open?0:away;
        panelAnimations[panel]=DispatcherTimer.RunOnce(()=>{
            panelAnimations.Remove(panel);
            if(!open){ panel.IsVisible=false;if(!compactLayout)panel.Width=0; }
            panel.RenderTransform=null;
        },PanelDuration);
    }

    private void RefreshPanelButtons()
    {
        foreach(var (button,panel,left) in new[]{(LeftPanelButton,LeftSidebar,true),(RightPanelButton,RightSidebar,false)}) {
            bool open=compactLayout?panel.IsEnabled:left?leftPanelOpen:rightPanelOpen;
            button.Content=left?"◧":"◨";
            button.Background=open?Brushes.PeachPuff:Brushes.WhiteSmoke;
            button.Tip=left?(open?L("Скрыть левую панель"):L("Показать левую панель")):
                (open?L("Скрыть правую панель"):L("Показать правую панель"));
        }
    }

    private void InitializePanelResize(Border panel,bool left)
    {
        var content=panel.Child!;
        panel.Child=null;
        var grid=new Grid();
        grid.Children.Add(content);
        var handle=new Border {
            Width=20, Margin=left?new Thickness(0,0,-12,0):new Thickness(-12,0,0,0),
            HorizontalAlignment=left?HorizontalAlignment.Right:HorizontalAlignment.Left,
            Background=Brushes.Transparent, Cursor=new Cursor(StandardCursorType.SizeWestEast),
            Child=new Border { Width=3, Height=36, CornerRadius=new CornerRadius(2), Background=Brushes.LightGray },
            Tip=L("Изменить ширину панели")
        };
        IPointer? pointer=null;
        double startX=0, width=0;
        handle.PointerPressed+=(_,e)=>{
            if(e.Pointer.Type!=PointerType.Touch && !e.GetCurrentPoint(handle).Properties.IsLeftButtonPressed)return;
            pointer=e.Pointer;startX=e.GetPosition(this).X;width=panel.Width;
            e.Pointer.Capture(handle);e.Handled=true;
        };
        handle.PointerMoved+=(_,e)=>{
            if(e.Pointer!=pointer)return;
            double next=width+(e.GetPosition(this).X-startX)*(left?1:-1);
            if(compactLayout) {
                next=Math.Clamp(next,Math.Min(190,Bounds.Width-32),Bounds.Width-32);
                if(left)leftDrawerWidth=next;else rightDrawerWidth=next;
            } else {
                next=Math.Clamp(next,190,Math.Max(190,(Bounds.Width-360)/2));
                if(left)leftPanelWidth=next;else rightPanelWidth=next;
            }
            panel.Width=next;e.Handled=true;
        };
        handle.PointerReleased+=(_,e)=>{if(e.Pointer==pointer){pointer=null;e.Pointer.Capture(null);e.Handled=true;}};
        handle.PointerCaptureLost+=(_,_)=>pointer=null;
        grid.Children.Add(handle);
        panel.Child=grid;
    }
}
