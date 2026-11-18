namespace RFloor;

public partial class MainWindow
{
    private enum TouchGesture { Tap, Pan, Transform, ObjectDrag, LabelDrag, Selection, Drawing, PointPlacement, Consumed }
    private const double TouchSlop = 8;
    private readonly Dictionary<int, Point> touches = [];
    private TouchGesture touchGesture;
    private Point touchOrigin, touchLast, pinchCenter;
    private double pinchDistance;
    private IDisposable? touchHold;
    private Point? touchDrawingAnchor;
    private Ellipse? touchAnchorMarker;
    private ObjectEntry? touchLabelTarget;
    private Border? touchDraggedLabel;
    private Point touchLabelStart,touchLabelPosition;
    private bool touchLabels;
    private Border? touchCard;

    private void InitializeInput()
    {
        Viewport.AddHandler(PointerWheelChangedEvent, Viewport_MouseWheel, RoutingStrategies.Tunnel);
        AddHandler(PointerPressedEvent, (_, e) => {
            PointerContext.Pointer=e.Pointer;
            PointerContext.Modifiers=e.KeyModifiers;
            PointerContext.LastPosition=e.GetPosition(Viewport);
            if (touchCard is not null && e.Source is Visual source &&
                source != touchCard && !source.GetVisualAncestors().Contains(touchCard))
                HideTouchCard();
        }, RoutingStrategies.Tunnel, true);
        AddHandler(PointerMovedEvent, (_, e) => {
            PointerContext.Pointer=e.Pointer;
            PointerContext.Modifiers=e.KeyModifiers;
            PointerContext.LastPosition=e.GetPosition(Viewport);
        }, RoutingStrategies.Tunnel, true);
        Viewport.AddHandler(PointerPressedEvent, TouchPressed, RoutingStrategies.Tunnel);
        Viewport.AddHandler(PointerMovedEvent, TouchMoved, RoutingStrategies.Tunnel);
        Viewport.AddHandler(PointerReleasedEvent, TouchReleased, RoutingStrategies.Tunnel);
        Objects.AddHandler(PointerReleasedEvent, Objects_OpenCard, RoutingStrategies.Bubble, handledEventsToo:true);
        Objects.KeyUp+=(_,e)=>{
            if(e.Key is Key.Up or Key.Down or Key.Home or Key.End or Key.Enter or Key.Space)
                ShowSelectedObjectCard(new Point(Viewport.Bounds.Width,Viewport.Bounds.Height/2));
        };
        Viewport.PointerCaptureLost += (_, e) => {
            if (!touches.Remove(e.Pointer.Id)) return;
            CancelTouchGesture(clearTapePreview:true);
            touchGesture = TouchGesture.Consumed;
        };
        KeyDown += (_, e) => PointerContext.Modifiers=e.KeyModifiers;
    }

    private void TouchPressed(object? sender, PointerPressedEventArgs e)
    {
        if(e.Source is Visual source && source.GetSelfAndVisualAncestors().Any(v=>v is Avalonia.Controls.TextBox or ScrollBar)) return;
        if(e.Pointer.Type != PointerType.Touch) {
            if(touchLabels) {
                touchLabels=false;
                if(surveyMode)Dispatcher.UIThread.Post(()=>{if(!touchLabels)RenderPlan();});
            }
            PanStart(sender!,e);
            return;
        }
        rulerPointer=null;
        SetHoveredSurveyPoint(null);
        UpdateRulerPointer();
        UpdateCoverageHover();
        e.Handled=true;
        var position=e.GetPosition(Viewport);
        touches[e.Pointer.Id]=position;
        e.Pointer.Capture(Viewport);
        if(touches.Count > 1) {
            CancelTouchGesture(clearTapePreview:true);
            touchGesture=TouchGesture.Transform;
            (pinchDistance,pinchCenter)=PinchGeometry();
            return;
        }
        if(inlineNameEditor is not null) CommitInlineRename();
        touchGesture=TouchGesture.Tap;
        touchOrigin=touchLast=position;
        var local=e.GetPosition(Plan);
        touchLabelTarget=(e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Border>().Select(b=>b.Tag).OfType<ObjectEntry>().FirstOrDefault();
        var hit=touchLabelTarget ??
            HitObject(World(local,false), 14/(Scale*PlanZoom.ScaleX));
        if(!touchLabels) { touchLabels=true; if(surveyMode)RenderPlan(); }
        PreviewTouch(local);
        if(measurementCancellation is not null) return;
        if(surveyMode && surveyTool==SurveyTool.Add) {
            touchGesture=TouchGesture.PointPlacement;
            return;
        }
        if(!surveyMode && tool=="select" && touchLabelTarget is {} callout)
            touchHold=DispatcherTimer.RunOnce(()=>StartTouchLabelDrag(callout,local),TimeSpan.FromMilliseconds(475));
        else if (surveyMode && surveyTool == SurveyTool.Select && hit is null ||
            !surveyMode && tool=="select" && (hit is null || hit.Kind=="room" && touchLabelTarget is null))
            touchHold=DispatcherTimer.RunOnce(()=>{
                marqueeStart=marqueeEnd=World(local,false);
                touchGesture=TouchGesture.Selection;
                PlatformServices.LongPressFeedback?.Invoke();
            },TimeSpan.FromMilliseconds(475));
        else if((surveyMode && surveyTool == SurveyTool.None || !surveyMode && tool=="select") && hit is not null)
            touchHold=DispatcherTimer.RunOnce(()=>StartTouchDrag(hit,local),TimeSpan.FromMilliseconds(475));
        else if(!surveyMode && tool=="tape")
            touchHold=DispatcherTimer.RunOnce(()=>{
                if(DeleteTapeAt(World(local,false))) {
                    touchGesture=TouchGesture.Consumed;
                    PlatformServices.LongPressFeedback?.Invoke();
                } else {
                    if(tapeStart is null)TapeClick(TapeSnap(World(local,false)));
                    touchGesture=TouchGesture.Drawing;
                    PlatformServices.LongPressFeedback?.Invoke();
                }
            },TimeSpan.FromMilliseconds(475));
        else if(!surveyMode && tool!="select")
            touchHold=DispatcherTimer.RunOnce(()=>{
                touchDrawingAnchor=tool is "text" or "router"?null:World(local,tool!="wall");
                touchGesture=TouchGesture.Drawing;
                PreviewTouch(local);
                PlatformServices.LongPressFeedback?.Invoke();
            },TimeSpan.FromMilliseconds(475));
    }

    private (double Distance,Point Center) PinchGeometry()
    {
        var pair=touches.Values.Take(2).ToArray();
        return (((Vector)(pair[1]-pair[0])).Length,pair[0]+(pair[1]-pair[0])/2);
    }

    private void TouchMoved(object? sender, PointerEventArgs e)
    {
        if(!touches.ContainsKey(e.Pointer.Id)) return;
        e.Handled=true;
        var next=e.GetPosition(Viewport);
        touches[e.Pointer.Id]=next;
        if(touchGesture==TouchGesture.Transform) {
            if(touches.Count>=2) {
                var (distance,center)=PinchGeometry();
                if(distance>1 && pinchDistance>1) ZoomAt(distance/pinchDistance,pinchCenter,center);
                else PanBy(pinchCenter-center);
                pinchDistance=distance;
                pinchCenter=center;
            } else PanBy(touchLast-next);
        } else if(touchGesture==TouchGesture.ObjectDrag && dragItem is {} item && dragStart is Point start) {
            dragDelta=AdjustDrag(item,World(e.GetPosition(Plan),false)-start);
            DrawDragPreview();
        } else if(touchGesture==TouchGesture.LabelDrag && touchDraggedLabel is {} label) {
            MoveTouchLabel(label,touchLabelPosition+(e.GetPosition(Plan)-touchLabelStart));
        } else if(touchGesture==TouchGesture.Selection) {
            marqueeEnd=World(e.GetPosition(Plan),false);
            DrawMarquee();
        } else if(touchGesture is TouchGesture.PointPlacement or TouchGesture.Drawing) {
            PreviewTouch(e.GetPosition(Plan));
        } else if(touchGesture==TouchGesture.Pan || touchGesture==TouchGesture.Tap && ((Vector)(next-touchOrigin)).Length>TouchSlop) {
            bool starting=touchGesture==TouchGesture.Tap;
            if(starting) CancelTouchGesture(clearTapePreview:true);
            touchGesture=TouchGesture.Pan;
            PanBy((starting?touchOrigin:touchLast)-next);
        } else if(touchGesture==TouchGesture.Tap) PreviewTouch(e.GetPosition(Plan));
        touchLast=next;
    }

    private void TouchReleased(object? sender, PointerReleasedEventArgs e)
    {
        if(e.Pointer.Type!=PointerType.Touch) { PanEnd(sender!,e); return; }
        if(!touches.Remove(e.Pointer.Id)) return;
        e.Handled=true;
        e.Pointer.Capture(null);
        touchHold?.Dispose();
        touchHold=null;
        if(touches.Count>0) {
            if(touches.Count>=2) (pinchDistance,pinchCenter)=PinchGeometry();
            else touchLast=touches.Values.First();
            return;
        }
        var local=e.GetPosition(Plan);
        if(touchGesture==TouchGesture.ObjectDrag && dragItem is {} item && dragStart is Point from) {
            var delta=AdjustDrag(item,World(local,false)-from);
            CancelTouchGesture();
            if(CanMove(item,delta)) MoveSelected(item,delta);
        } else if(touchGesture==TouchGesture.LabelDrag && touchDraggedLabel is {Tag:ObjectEntry callout} label) {
            Vector delta=local-touchLabelStart;
            MoveTouchLabel(label,touchLabelPosition+delta);
            SaveLabelOffset(callout.Item,delta);
            touchDraggedLabel=null;
            RefreshTabHeaders();
        } else if(touchGesture==TouchGesture.Selection && marqueeStart is Point first) {
            SelectMarquee(first,World(local,false),e.GetPosition(Viewport));
        } else if(touchGesture==TouchGesture.PointPlacement) {
            var position=e.GetPosition(Viewport);
            _=FinishSurveyPlacement(local,position,14,((Vector)(position-touchOrigin)).Length>TouchSlop);
        } else if(touchGesture==TouchGesture.Drawing ||
            touchGesture==TouchGesture.Tap && ((Vector)(e.GetPosition(Viewport)-touchOrigin)).Length<=TouchSlop) {
            CommitTouchTap(local,e.GetPosition(Viewport));
        }
        CancelTouchGesture();
    }

    private void StartTouchLabelDrag(ObjectEntry callout,Point local)
    {
        var label=Plan.Children.OfType<Border>().FirstOrDefault(b=>b.Tag is ObjectEntry entry && ReferenceEquals(entry.Item,callout.Item));
        var selected=Objects.Items.OfType<ObjectEntry>().FirstOrDefault(entry=>ReferenceEquals(entry.Item,callout.Item));
        if(label is null || selected is null)return;
        Objects.SelectedItem=selected;
        selectedCallout=selected.Item;
        touchDraggedLabel=label;
        touchLabelStart=local;
        touchLabelPosition=new Point(Canvas.GetLeft(label),Canvas.GetTop(label));
        touchGesture=TouchGesture.LabelDrag;
        PlatformServices.LongPressFeedback?.Invoke();
    }

    private void MoveTouchLabel(Border label,Point position)
    {
        Canvas.SetLeft(label,position.X);
        Canvas.SetTop(label,position.Y);
        if(label.Tag is ObjectEntry entry && Plan.Children.OfType<Polyline>()
            .FirstOrDefault(p=>p.Tag is LabelLeader leader && ReferenceEquals(leader.Item,entry.Item)) is {} line)
            PositionLabelLeader(line,entry,new Rect(position,label.DesiredSize));
    }

    private void StartTouchDrag(ObjectEntry hit, Point local)
    {
        var selected=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(entry=>ReferenceEquals(entry.Item,hit.Item));
        if(selected is null) return;
        if(selected.Kind=="wall" && selected.Item["roomId"] is {} id)
            selected=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(entry=>entry.Kind=="room" && entry.Item["id"]?.ToString()==id.ToString()) ?? selected;
        selectedCallout=null;
        Objects.SelectedItem=selected;
        dragItem=selected;
        dragStart=World(local,false);
        touchGesture=TouchGesture.ObjectDrag;
        PlatformServices.LongPressFeedback?.Invoke();
        RenderPlan();
    }

    private void PreviewTouch(Point local)
    {
        if(surveyMode && surveyTool == SurveyTool.Add && measurementCancellation is null) {
            var position=SurveyPosition(local);
            if(pointPreview!=position) { pointPreview=position; DrawPointPreview(); }
        }
        else if(!surveyMode && tool=="router") { routerPreview=World(local); DrawRouterPreview(); }
        else if(!surveyMode && tool=="text") {
            drawingStart=drawingEnd=World(local);drawingFilled=true;DrawConstructionPreview();
        }
        else if(!surveyMode && tool=="tape" && tapeStart is not null) {
            tapeCursor=TapeSnap(World(local,false)); DrawPendingTape();
        } else if(touchDrawingAnchor is Point start) {
            drawingStart=start; drawingEnd=World(local,tool!="wall"); drawingFilled=!phoneOutline; DrawConstructionPreview();
        }
    }

    private async void CommitTouchTap(Point local,Point viewportPoint)
    {
        var point=World(local,false);
        if(surveyMode && surveyTool != SurveyTool.None) { await SurveyClick(local,14/(Scale*PlanZoom.ScaleX)); return; }
        if(surveyMode || tool=="select") {
            var target=touchLabelTarget;
            var hit=target is null?HitObject(point,14/(Scale*PlanZoom.ScaleX)):
                Objects.Items.OfType<ObjectEntry>().FirstOrDefault(entry=>ReferenceEquals(entry.Item,target.Item));
            Objects.SelectedItem=hit;
            selectedCallout=target is not null?hit?.Item:null;
            if(surveyMode && hit is not null) ShowTouchCard(hit,viewportPoint);
            else ShowSelectedObjectCard(viewportPoint);
            return;
        }
        if(tool=="tape") { TapeClick(TapeSnap(point)); return; }
        point=World(local,tool!="wall");
        if(tool=="router") { AddRouter(point); return; }
        if(tool=="text") { AddDecoration(point,point); return; }
        if(touchDrawingAnchor is Point start) { touchDrawingAnchor=null; DrawTouchAnchor(); FinishDrawing(start,point); }
        else {
            touchDrawingAnchor=point;
            DrawTouchAnchor();
            Status.Text=L("Коснитесь второй точки, чтобы завершить построение.");
        }
    }

    private void CancelTouchGesture(bool clearTapePreview=false)
    {
        touchHold?.Dispose();
        touchHold=null;
        if(touchDraggedLabel is {} label)
        {
            MoveTouchLabel(label,touchLabelPosition);
            touchDraggedLabel=null;
        }
        touchLabelTarget=null;
        if (touchGesture == TouchGesture.Selection) {
            marqueeStart=marqueeEnd=null;
            ClearMarquee();
        }
        if(touches.Count>0) touchGesture=TouchGesture.Consumed;
        if(constructionPreview.Count>0) {
            drawingStart=drawingEnd=null;
            ClearConstructionPreview();
        }
        if(clearTapePreview && tapeStart is not null) { tapeCursor=tapeStart;DrawPendingTape(); }
        if(touchGesture==TouchGesture.Drawing || clearTapePreview && touches.Count>1) {
            touchDrawingAnchor=null;
            DrawTouchAnchor();
        }
        if(dragStart is not null && dragItem is not null) {
            dragStart=null; dragItem=null; dragDelta=default; RenderPlan();
        }
        if(pointPreview is not null) { pointPreview=null; DrawPointPreview(); }
        if(routerPreview is not null) { routerPreview=null; DrawRouterPreview(); }
    }

    private void DrawTouchAnchor()
    {
        if(touchAnchorMarker is not null)Plan.Children.Remove(touchAnchorMarker);
        touchAnchorMarker=null;
        if(touchDrawingAnchor is not Point point)return;
        const double size=8;
        touchAnchorMarker=new Ellipse {Width=size,Height=size,Fill=Brushes.DarkOrange,IsHitTestVisible=false};
        Canvas.SetLeft(touchAnchorMarker,Screen(point.X)-size/2);
        Canvas.SetTop(touchAnchorMarker,Screen(point.Y)-size/2);
        Ui.SetZIndex(touchAnchorMarker,150);
        Plan.Children.Add(touchAnchorMarker);
    }
    private void ShowSelectedObjectCard(Point position)
    {
        if(surveyMode)return;
        if(Objects.SelectedItems?.Count==1 && Objects.SelectedItem is ObjectEntry entry)
            ShowTouchCard(entry,position);
        else HideTouchCard();
    }

    private void ShowTouchCard(ObjectEntry hit,Point position)
    {
        HideTouchCard();
        bool overDrawer=compactLayout && RightSidebar.IsEnabled && hit.Kind!="point";
        var grid=overDrawer?(Grid)Content!:(Grid)Viewport.Parent!;
        Control details=hit.Kind=="point"?PointDetails(hit.Item):IsLabelVisible(hit)?LabelText(hit):
            new TextBlock {Text=ObjectName(hit.Item,hit.Label),FontWeight=FontWeights.SemiBold};
        if(details is TextBlock label) label.TextWrapping=TextWrapping.Wrap;
        var content=new DockPanel();
        var actions=hit.Kind=="point"?SurveyCardActions(hit.Item):EditorCardActions(hit);
        DockPanel.SetDock(actions,Dock.Bottom);
        content.Children.Add(actions);
        content.Children.Add(details);
        touchCard=new Border {
            Child=content,
            Background=Brushes.White, BorderBrush=Brushes.LightGray, BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(8), Padding=new Thickness(8),
            Margin=overDrawer?new Thickness(8,8,8,84):new Thickness(8),
            MaxWidth=Math.Max(80,Math.Min(350,grid.Bounds.Width-16)),
            MaxHeight=Math.Max(72,grid.Bounds.Height*.46), ZIndex=overDrawer?303:10,
            HorizontalAlignment=position.X<grid.Bounds.Width/2?HorizontalAlignment.Right:HorizontalAlignment.Left,
            VerticalAlignment=overDrawer || position.Y<grid.Bounds.Height/2?VerticalAlignment.Bottom:VerticalAlignment.Top
        };
        grid.Children.Add(touchCard);
    }

    private Control EditorCardActions(ObjectEntry hit)
    {
        var delete=new Button {Content=L("Удалить")};
        var properties=new Button {Content=L("Свойства")};
        delete.Click+=(_,_)=>{
            HideTouchCard();
            if(!Objects.Items.Contains(hit))return;
            Objects.SelectedItem=hit;
            DeletePlanEntries([hit]);
        };
        properties.Click+=(_,_)=>{
            HideTouchCard();
            if(!Objects.Items.Contains(hit))return;
            Objects.SelectedItem=hit;
            if(compactLayout?!RightSidebar.IsEnabled:!rightPanelOpen)TogglePanel(RightSidebar);
        };
        return OverlayDialog.Actions(delete,properties);
    }

    private void HideTouchCard()
    {
        if(touchCard?.Parent is Grid grid) grid.Children.Remove(touchCard);
        touchCard=null;
    }
}
