using Avalonia.LogicalTree;
namespace RFloor;
public partial class MainWindow {
    private bool compactLayout,phoneOutline;
    private Button? outlineButton;
    private Grid? sidebarParent;
    private readonly Dictionary<Avalonia.Controls.Button,(double MinHeight,double MinWidth,Thickness Margin,Thickness Padding)> dockDefaults=new();
    private readonly Avalonia.Controls.Border drawerShade=new() {Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),ZIndex=299,IsVisible=false};
    private void UpdateModeVisual(){
        if(outlineButton is not null)outlineButton.IsVisible=!surveyMode;
        ModeThumb.Width=(ModeSwitch.Width-8)/2;
        ((TranslateTransform)ModeThumb.RenderTransform!).Transitions??=new Transitions {new DoubleTransition {Property=TranslateTransform.XProperty,Duration=TimeSpan.FromMilliseconds(150)}};
        ((TranslateTransform)ModeThumb.RenderTransform!).X=surveyMode?0:ModeThumb.Width;
        ModePlanText.Foreground=surveyMode?Brushes.DimGray:Brushes.White;ModeSurveyText.Foreground=surveyMode?Brushes.White:Brushes.DimGray;
        ModeSwitch.Background=new SolidColorBrush(Color.FromRgb(242,242,242));
    }
    public bool HandleBack() {
        if(overlays.Count>0){overlays.Keys.Last().Cancel();return true;}
        if(compactLayout && drawerShade.IsVisible){CloseDrawers();return true;}
        if(touchCard is not null){HideTouchCard();return true;}
        if(inlineNameEditor is not null){CommitInlineRename();return true;}
        if(measurementCancellation is not null){measurementCancellation.Cancel();return true;}
        if(surveyTool != SurveyTool.None||drawingStart is not null||tapeStart is not null||touchDrawingAnchor is not null){ResetPointerTools();RenderPlan();return true;}
        return false;
    }
    private void InitializeResponsiveLayout(){
        InitializePanelResize(LeftSidebar,true);InitializePanelResize(RightSidebar,false);
        RefreshPanelButtons();
        drawerShade.PointerPressed+=(_,e)=>{CloseDrawers();e.Handled=true;};
        SizeChanged+=(_,_)=>{UpdateResponsiveLayout();RefreshOverlayBounds();};
        AttachedToVisualTree+=(_,_)=>UpdateResponsiveLayout();
    }
    private void UpdateResponsiveLayout(){
        // Android can arrange the initial single-view surface at one pixel before its first real frame.
        if(Bounds.Width<64)return;
        bool compact=Bounds.Width<840 || (OperatingSystem.IsAndroid() && Bounds.Height<600);
        Welcome.Padding=new Thickness(compact?12:32);
        if(Welcome.Child is Border card){card.Padding=new Thickness(compact?24:64,32);if(card.Child is StackPanel body)body.Width=compact?Math.Max(220,Bounds.Width-96):360;}
        if(compact==compactLayout)ApplyPanelWidths();
        if(compact==compactLayout)return;
        foreach(var animation in panelAnimations.Values)animation.Dispose();
        panelAnimations.Clear();
        compactLayout=compact;var root=(Grid)Content!;
        if(compact){
            sidebarParent=(Grid)LeftSidebar.Parent!;sidebarParent.Children.Remove(LeftSidebar);sidebarParent.Children.Remove(RightSidebar);
            drawerShade.Margin=new Thickness(0,0,0,74);
            root.Children.Add(drawerShade);
            foreach(var panel in new[]{LeftSidebar,RightSidebar}){panel.ZIndex=300;panel.Margin=new Thickness(8,60,8,78);panel.CornerRadius=new CornerRadius(10);panel.VerticalAlignment=VerticalAlignment.Stretch;panel.HorizontalAlignment=panel==LeftSidebar?HorizontalAlignment.Left:HorizontalAlignment.Right;panel.Width=DrawerWidth(panel==LeftSidebar);panel.IsVisible=false;panel.IsEnabled=false;panel.RenderTransform=null;root.Children.Add(panel);}
            foreach(var button in new[]{LeftPanelButton,RightPanelButton}) {
                CanvasPanelTabs.Children.Remove(button);root.Children.Add(button);
                button.ZIndex=302;
            }
            Objects.SelectionMode=surveyMode?SelectionMode.Multiple|SelectionMode.Toggle:SelectionMode.Multiple;ModeSwitch.Width=116;StatusBar.IsVisible=true;CanvasDock.Height=44;ZoomLabel.IsVisible=false;GridPicker.MinHeight=40;GridPicker.Height=40;
            if(StatusBar.Child is DockPanel statusDock)statusDock.Children[0].IsVisible=false;
            Status.FontSize=11;Status.TextTrimming=TextTrimming.CharacterEllipsis;
            foreach(var button in CanvasDock.GetLogicalDescendants().OfType<Avalonia.Controls.Button>()){
                dockDefaults.TryAdd(button,(button.MinHeight,button.MinWidth,button.Margin,button.Padding));
                button.MinHeight=40;button.MinWidth=40;button.Margin=new Thickness(0);button.Padding=new Thickness(5,0);
            }
            if(CanvasDock.Children[0] is StackPanel gridControls && gridControls.Children[0] is Control label)label.IsVisible=false;
            CanvasDock.Children[1].IsVisible=false;
            var outline=new Button {Content="▧",MinWidth=40,MinHeight=40,Margin=new Thickness(0),Padding=new Thickness(5,0),Tip=L("Только контур")};
            outline.Click+=(_,_)=>{phoneOutline=!phoneOutline;outline.Background=phoneOutline?Brushes.PeachPuff:Brushes.White;};
            outlineButton=outline;((StackPanel)CanvasDock.Children[0]).Children.Add(outline);
        }else{
            drawerShade.IsVisible=false;root.Children.Remove(drawerShade);
            foreach(var panel in new[]{LeftSidebar,RightSidebar}){root.Children.Remove(panel);panel.Margin=new Thickness(0);panel.CornerRadius=new CornerRadius(0);panel.RenderTransform=null;panel.HorizontalAlignment=HorizontalAlignment.Stretch;sidebarParent!.Children.Add(panel);}
            foreach(var button in new[]{LeftPanelButton,RightPanelButton}) {
                root.Children.Remove(button);CanvasPanelTabs.Children.Add(button);
                button.ZIndex=0;
            }
            Objects.SelectionMode=SelectionMode.Multiple;ApplyPanelWidths();ModeSwitch.Width=144;StatusBar.IsVisible=true;CanvasDock.Height=28;ZoomLabel.IsVisible=true;GridPicker.MinHeight=0;GridPicker.Height=28;
            if(StatusBar.Child is DockPanel statusDock)statusDock.Children[0].IsVisible=true;
            Status.ClearValue(TextBlock.FontSizeProperty);Status.ClearValue(TextBlock.TextTrimmingProperty);
            if(outlineButton is not null)((StackPanel)CanvasDock.Children[0]).Children.Remove(outlineButton);
            outlineButton=null;
            foreach(var (control,defaults) in dockDefaults){control.MinHeight=defaults.MinHeight;control.MinWidth=defaults.MinWidth;control.Margin=defaults.Margin;control.Padding=defaults.Padding;}
            dockDefaults.Clear();
            CanvasDock.Children[1].IsVisible=true;
            if(CanvasDock.Children[0] is StackPanel gridControls && gridControls.Children[0] is Control label)label.IsVisible=true;
            phoneOutline=false;
        }
        RefreshPanelButtons();
        UpdateModeVisual();
    }
}
