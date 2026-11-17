using Avalonia.LogicalTree;
namespace RFloor;
public partial class MainWindow {
    private bool compactLayout,touchPan,phoneOutline;
    private Grid? sidebarParent;
    private readonly Dictionary<Avalonia.Controls.Button,(double MinHeight,double MinWidth,Thickness Margin,Thickness Padding)> dockDefaults=new();
    private readonly Avalonia.Controls.Border drawerShade=new() {Background=new SolidColorBrush(Color.FromArgb(80,0,0,0)),ZIndex=299,IsVisible=false};
    private void UpdateModeVisual(){
        ModeThumb.Width=(ModeSwitch.Width-8)/2;
        ((TranslateTransform)ModeThumb.RenderTransform!).Transitions??=new Transitions {new DoubleTransition {Property=TranslateTransform.XProperty,Duration=TimeSpan.FromMilliseconds(150)}};
        ((TranslateTransform)ModeThumb.RenderTransform!).X=surveyMode?ModeThumb.Width:0;
        ModePlanText.Foreground=surveyMode?Brushes.DimGray:Brushes.White;ModeSurveyText.Foreground=surveyMode?Brushes.White:Brushes.DimGray;
        ModeSwitch.Background=new SolidColorBrush(Color.FromRgb(242,242,242));
    }
    private bool heldTape;
    public bool HandleBack() {
        if(overlays.Count>0){overlays.Keys.Last().Cancel();return true;}
        if(compactLayout && (LeftSidebar.IsVisible||RightSidebar.IsVisible)){CloseDrawers();return true;}
        if(inlineNameEditor is not null){CommitInlineRename();return true;}
        if(measurementCancellation is not null){measurementCancellation.Cancel();return true;}
        if(addingPoint||drawingStart is not null||tapeStart is not null){ResetPointerTools();RenderPlan();return true;}
        return false;
    }
    private void InitializeResponsiveLayout(){
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
        if(compactLayout)foreach(var panel in new[]{LeftSidebar,RightSidebar})panel.Width=Math.Min(320,Bounds.Width-32);
        if(compact==compactLayout)return;compactLayout=compact;var root=(Grid)Content!;
        if(compact){
            sidebarParent=(Grid)LeftSidebar.Parent!;sidebarParent.Children.Remove(LeftSidebar);sidebarParent.Children.Remove(RightSidebar);
            root.Children.Add(drawerShade);
            foreach(var panel in new[]{LeftSidebar,RightSidebar}){panel.ZIndex=300;panel.Margin=new Thickness(8,100,8,44);panel.VerticalAlignment=VerticalAlignment.Stretch;panel.HorizontalAlignment=panel==LeftSidebar?HorizontalAlignment.Left:HorizontalAlignment.Right;panel.Width=Math.Min(320,Bounds.Width-32);panel.IsVisible=false;root.Children.Add(panel);}
            Objects.SelectionMode=SelectionMode.Multiple|SelectionMode.Toggle;ModeSwitch.Width=116;StatusBar.IsVisible=true;CanvasDock.Height=44;ZoomLabel.IsVisible=false;GridPicker.MinHeight=40;GridPicker.Height=40;
            if(StatusBar.Child is DockPanel statusDock)statusDock.Children[0].IsVisible=false;
            Status.FontSize=11;Status.TextTrimming=TextTrimming.CharacterEllipsis;
            foreach(var button in CanvasDock.GetLogicalDescendants().OfType<Avalonia.Controls.Button>()){
                dockDefaults.TryAdd(button,(button.MinHeight,button.MinWidth,button.Margin,button.Padding));
                button.MinHeight=40;button.MinWidth=32;button.Margin=new Thickness(0);button.Padding=new Thickness(5,0);
            }
            if(CanvasDock.Children[0] is StackPanel gridControls && gridControls.Children[0] is Control label)label.IsVisible=false;
            if(CanvasDock.Children[1] is StackPanel zoomControls && zoomControls.Children[0] is Avalonia.Controls.Button fit){fit.ClearValue(ContentControl.ContentProperty);fit.Content="⛶";}
            var pan=new Button {Content="✥",MinWidth=36,MinHeight=44,Tip=L("Перемещение холста")};pan.Click+=(_,_)=>{touchPan=!touchPan;pan.Background=touchPan?Brushes.PeachPuff:Brushes.White;};CanvasDock.Children.Add(pan);
            var outline=new Button {Content="▧",MinWidth=36,MinHeight=44,Tip=L("Только контур")};outline.Click+=(_,_)=>{phoneOutline=!phoneOutline;outline.Background=phoneOutline?Brushes.PeachPuff:Brushes.White;};CanvasDock.Children.Add(outline);
        }else{
            drawerShade.IsVisible=false;root.Children.Remove(drawerShade);
            foreach(var panel in new[]{LeftSidebar,RightSidebar}){root.Children.Remove(panel);panel.Margin=new Thickness(0);panel.HorizontalAlignment=HorizontalAlignment.Stretch;panel.IsVisible=true;sidebarParent!.Children.Add(panel);}
            Objects.SelectionMode=SelectionMode.Multiple;LeftSidebar.Width=leftPanelOpen?240:0;RightSidebar.Width=rightPanelOpen?250:0;ModeSwitch.Width=144;StatusBar.IsVisible=true;CanvasDock.Height=28;ZoomLabel.IsVisible=true;GridPicker.MinHeight=0;GridPicker.Height=28;
            if(StatusBar.Child is DockPanel statusDock)statusDock.Children[0].IsVisible=true;
            Status.ClearValue(TextBlock.FontSizeProperty);Status.ClearValue(TextBlock.TextTrimmingProperty);
            while(CanvasDock.Children.Count>2)CanvasDock.Children.RemoveAt(2);
            foreach(var (control,defaults) in dockDefaults){control.MinHeight=defaults.MinHeight;control.MinWidth=defaults.MinWidth;control.Margin=defaults.Margin;control.Padding=defaults.Padding;}
            dockDefaults.Clear();
            if(CanvasDock.Children[1] is StackPanel zoomControls && zoomControls.Children[0] is Avalonia.Controls.Button fit)fit.Bind(ContentControl.ContentProperty,Application.Current!.GetResourceObservable("UI_95"));
            if(CanvasDock.Children[0] is StackPanel gridControls && gridControls.Children[0] is Control label)label.IsVisible=true;
            touchPan=phoneOutline=false;
        }
        UpdateModeVisual();
    }
    private void CloseDrawers(){if(!compactLayout)return;LeftSidebar.IsVisible=RightSidebar.IsVisible=drawerShade.IsVisible=false;}
    private void ToggleDrawer(Border panel){bool open=!panel.IsVisible;CloseDrawers();panel.Width=Math.Min(320,Bounds.Width-32);panel.IsEnabled=true;panel.IsVisible=drawerShade.IsVisible=open;}
    private void PanTouch(Vector delta){
        var pad=PlanSurface.Padding;double x=Viewport.Offset.X+delta.X,y=Viewport.Offset.Y+delta.Y;
        double left=Math.Max(0,-x),top=Math.Max(0,-y),right=Math.Max(0,x+Viewport.Viewport.Width-Viewport.Extent.Width),bottom=Math.Max(0,y+Viewport.Viewport.Height-Viewport.Extent.Height);
        PlanSurface.Padding=new Thickness(pad.Left+left,pad.Top+top,pad.Right+right,pad.Bottom+bottom);Viewport.UpdateLayout();Viewport.Offset=new Vector(x+left,y+top);
    }
    private readonly Dictionary<int,Point> touches=new();
    private readonly Dictionary<int,IPointer> touchPointers=new();
    private bool multiTouch;
    private Point touchAnchor;
    private double pinchDistance;
    private Point pinchCenter;
    private void InitializeInput(){
        Viewport.AddHandler(PointerWheelChangedEvent,Viewport_MouseWheel,RoutingStrategies.Tunnel);
        Plan.SetValue(InputElement.IsHoldingEnabledProperty,true);
        Plan.Holding+=(_,e)=>{if(e.PointerType==PointerType.Touch && e.HoldingState==HoldingState.Started && !surveyMode && tool=="tape"){heldTape=DeleteTapeAt(World(e.Position,false));if(heldTape){tapeStart=tapeCursor=null;DrawTape();e.Handled=true;}}};
        AddHandler(PointerPressedEvent,(_,e)=>{PointerContext.Pointer=e.Pointer;PointerContext.Modifiers=e.KeyModifiers;PointerContext.LastPosition=e.GetPosition(Viewport);},RoutingStrategies.Tunnel,true);
        AddHandler(PointerMovedEvent,(_,e)=>{PointerContext.Pointer=e.Pointer;PointerContext.Modifiers=e.KeyModifiers;PointerContext.LastPosition=e.GetPosition(Viewport);},RoutingStrategies.Tunnel,true);
        Viewport.AddHandler(PointerPressedEvent,(s,e)=>{
            if(e.Pointer.Type==PointerType.Touch){
                touchPointers[e.Pointer.Id]=e.Pointer;touches[e.Pointer.Id]=e.GetPosition(Viewport);touchAnchor=e.GetPosition(Viewport);
                if(touches.Count>=2){multiTouch=true;var savedTape=tapeStart;var savedCursor=tapeCursor;ResetPointerTools();tapeStart=savedTape;tapeCursor=savedCursor;foreach(var pointer in touchPointers.Values)pointer.Capture(Viewport);var pair=touches.Values.Take(2).ToArray();pinchDistance=((Vector)(pair[1]-pair[0])).Length;pinchCenter=new((pair[0].X+pair[1].X)/2,(pair[0].Y+pair[1].Y)/2);e.Handled=true;}
                else if(touchPan){e.Pointer.Capture(Viewport);e.Handled=true;}
                return;
            }
            PanStart(s!,e);
        },RoutingStrategies.Tunnel,true);
        Viewport.AddHandler(PointerMovedEvent,(_,e)=>{
            if(e.Pointer.Type!=PointerType.Touch||!touches.ContainsKey(e.Pointer.Id))return;
            Point next=e.GetPosition(Viewport);touches[e.Pointer.Id]=next;
            if(multiTouch){
                if(touches.Count>=2){var pair=touches.Values.Take(2).ToArray();double distance=((Vector)(pair[1]-pair[0])).Length;Point center=new((pair[0].X+pair[1].X)/2,(pair[0].Y+pair[1].Y)/2);if(pinchDistance>1)ZoomAt(distance/pinchDistance,pinchCenter);Viewport.Offset+=(Vector)(pinchCenter-center);pinchDistance=distance;pinchCenter=center;}e.Handled=true;
            }else if(touchPan){PanTouch((Vector)(touchAnchor-next));touchAnchor=next;e.Handled=true;}
        },RoutingStrategies.Tunnel,true);
        Viewport.AddHandler(PointerReleasedEvent,(s,e)=>{
            if(e.Pointer.Type==PointerType.Touch){bool suppress=multiTouch||touchPan;touches.Remove(e.Pointer.Id);touchPointers.Remove(e.Pointer.Id);if(touches.Count==0)multiTouch=false;
                if(!suppress && !surveyMode && tool=="tape"){if(!heldTape)TapeClick(TapeSnap(World(e.GetPosition(Plan),false)));heldTape=false;e.Handled=true;return;}
                if(suppress){e.Pointer.Capture(null);e.Handled=true;}return;}PanEnd(s!,e);
        },RoutingStrategies.Tunnel,true);
        KeyDown+=(_,e)=>PointerContext.Modifiers=e.KeyModifiers;
    }
}

