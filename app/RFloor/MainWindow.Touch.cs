namespace RFloor;

public partial class MainWindow
{
    private enum TouchGesture { Tap, Pan, Transform, ObjectDrag, Consumed }
    private const double TouchSlop = 8;
    private readonly Dictionary<int, Point> touches = [];
    private TouchGesture touchGesture;
    private Point touchOrigin, touchLast, pinchCenter;
    private double pinchDistance;
    private IDisposable? touchHold;
    private Point? touchDrawingAnchor;
    private Ellipse? touchAnchorMarker;
    private ObjectEntry? touchLabelTarget;
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
        Viewport.PointerCaptureLost += (_, e) => {
            if (!touches.Remove(e.Pointer.Id)) return;
            CancelTouchGesture(clearTapePreview:true);
            touchGesture = TouchGesture.Consumed;
        };
        KeyDown += (_, e) => PointerContext.Modifiers=e.KeyModifiers;
    }

    private void TouchPressed(object? sender, PointerPressedEventArgs e)
    {
        if(e.Pointer.Type != PointerType.Touch) {
            if(touchLabels) {
                touchLabels=false;
                Dispatcher.UIThread.Post(()=>{if(!touchLabels)RenderPlan();});
            }
            PanStart(sender!,e);
            return;
        }
        if(e.Source is Visual source && (source is Avalonia.Controls.TextBox or ScrollBar ||
            source.GetVisualAncestors().Any(v=>v is Avalonia.Controls.TextBox or ScrollBar))) return;

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
        if(!touchLabels) { touchLabels=true; RenderPlan(); }
        PreviewTouch(local);
        if(measurementCancellation is not null) return;
        if((surveyMode && !addingPoint || !surveyMode && tool=="select") && hit is not null)
            touchHold=DispatcherTimer.RunOnce(()=>StartTouchDrag(hit,local),TimeSpan.FromMilliseconds(475));
        else if(!surveyMode && tool=="tape")
            touchHold=DispatcherTimer.RunOnce(()=>{
                if(DeleteTapeAt(World(local,false))) {
                    touchGesture=TouchGesture.Consumed;
                    PlatformServices.LongPressFeedback?.Invoke();
                }
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
                else PanTouch(pinchCenter-center);
                pinchDistance=distance;
                pinchCenter=center;
            } else PanTouch(touchLast-next);
        } else if(touchGesture==TouchGesture.ObjectDrag && dragItem is {} item && dragStart is Point start) {
            dragDelta=AdjustDrag(item,World(e.GetPosition(Plan),false)-start);
            DrawDragPreview();
        } else if(touchGesture==TouchGesture.Pan || touchGesture==TouchGesture.Tap && ((Vector)(next-touchOrigin)).Length>TouchSlop) {
            bool starting=touchGesture==TouchGesture.Tap;
            if(starting) CancelTouchGesture(clearTapePreview:true);
            touchGesture=TouchGesture.Pan;
            PanTouch((starting?touchOrigin:touchLast)-next);
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
        } else if(touchGesture==TouchGesture.Tap && ((Vector)(e.GetPosition(Viewport)-touchOrigin)).Length<=TouchSlop) {
            CommitTouchTap(local,e.GetPosition(Viewport));
        }
        CancelTouchGesture();
    }

    private void StartTouchDrag(ObjectEntry hit, Point local)
    {
        var selected=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(entry=>ReferenceEquals(entry.Item,hit.Item));
        if(selected is null) return;
        if(selected.Kind=="wall" && selected.Item["roomId"] is {} id)
            selected=Objects.Items.Cast<ObjectEntry>().FirstOrDefault(entry=>entry.Kind=="room" && entry.Item["id"]?.ToString()==id.ToString()) ?? selected;
        Objects.SelectedItem=selected;
        dragItem=selected;
        dragStart=World(local,false);
        touchGesture=TouchGesture.ObjectDrag;
        PlatformServices.LongPressFeedback?.Invoke();
        RenderPlan();
    }

    private void PreviewTouch(Point local)
    {
        if(surveyMode && addingPoint) { pointPreview=World(local,false); DrawPointPreview(); }
        else if(!surveyMode && tool=="router") { routerPreview=World(local); DrawRouterPreview(); }
        else if(!surveyMode && tool=="text") {
            drawingStart=drawingEnd=World(local);drawingFilled=true;DrawTouchConstruction();
        }
        else if(!surveyMode && tool=="tape" && tapeStart is not null) {
            tapeCursor=TapeSnap(World(local,false)); DrawTape();
        } else if(touchDrawingAnchor is Point start) {
            drawingStart=start; drawingEnd=World(local); drawingFilled=!phoneOutline; DrawTouchConstruction();
        }
    }

    private void CommitTouchTap(Point local,Point viewportPoint)
    {
        var point=World(local,false);
        if(surveyMode && addingPoint) { SurveyClick(point); return; }
        if(surveyMode || tool=="select") {
            var hit=touchLabelTarget ?? HitObject(point,14/(Scale*PlanZoom.ScaleX));
            Objects.SelectedItem=hit;
            if(hit is not null && (hit.Kind=="point" || hit.Item["hideLabel"]?.GetValue<bool>()!=true))
                ShowTouchCard(hit,viewportPoint);
            return;
        }
        if(tool=="tape") { TapeClick(TapeSnap(point)); return; }
        point=World(local);
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
        if(touches.Count>0) touchGesture=TouchGesture.Consumed;
        if(touchConstruction.Count>0) {
            drawingStart=drawingEnd=null;
            ClearTouchConstruction();
        }
        if(clearTapePreview && tapeStart is not null) { tapeCursor=tapeStart;DrawTape(); }
        if(dragStart is not null && dragItem is not null) {
            dragStart=null; dragItem=null; dragDelta=default; RenderPlan();
        }
        if(pointPreview is not null) { pointPreview=null; DrawPointPreview(); }
        if(routerPreview is not null) { routerPreview=null; DrawRouterPreview(); }
    }

    private readonly List<Control> touchConstruction=[];
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
    private void ClearTouchConstruction()
    {
        foreach(var control in touchConstruction) Plan.Children.Remove(control);
        touchConstruction.Clear();
    }

    private void DrawTouchConstruction()
    {
        ClearTouchConstruction();
        int first=Plan.Children.Count;
        DrawConstruction(drawingStart!.Value,drawingEnd!.Value);
        touchConstruction.AddRange(Plan.Children.Skip(first));
    }

    private static string ObjectLabel(ObjectEntry entry)
    {
        var obj=entry.Item;
        string name=ObjectName(obj,entry.Label);
        return entry.Kind switch {
            "wall" or "door"=>name+L("\n{0:0.##} м",Length(obj)),
            "room"=>name+L("\n{0:0.##} м × {1:0.##} м = {2:0.##} м²", Number(obj["w"]),Number(obj["h"]),Number(obj["w"])*Number(obj["h"])),
            _=>name
        };
    }

    private void ShowTouchCard(ObjectEntry hit,Point position)
    {
        HideTouchCard();
        var grid=(Grid)Viewport.Parent!;
        Control content=new TextBlock {Text=ObjectLabel(hit),TextWrapping=TextWrapping.Wrap};
        if(hit.Kind=="point") {
            var details=new DockPanel();
            var repeat=new Button {Content=L("Повторить замер"),Margin=new Thickness(4),IsEnabled=measurementCancellation is null};
            repeat.Click+=(_,e)=>{HideTouchCard();Measure_Click(repeat,e);};
            DockPanel.SetDock(repeat,Dock.Bottom);
            details.Children.Add(repeat);
            details.Children.Add((Control)PointTooltip(hit.Item).Content!);
            content=details;
        }
        touchCard=new Border {
            Child=content,
            Background=Brushes.White, BorderBrush=Brushes.LightGray, BorderThickness=new Thickness(1),
            CornerRadius=new CornerRadius(8), Padding=new Thickness(8),
            Margin=new Thickness(8), MaxWidth=Math.Max(80,Math.Min(350,grid.Bounds.Width-16)),
            MaxHeight=Math.Max(60,grid.Bounds.Height*.55),
            HorizontalAlignment=position.X<grid.Bounds.Width/2?HorizontalAlignment.Right:HorizontalAlignment.Left,
            VerticalAlignment=position.Y<grid.Bounds.Height/2?VerticalAlignment.Bottom:VerticalAlignment.Top
        };
        grid.Children.Add(touchCard);
    }

    private void HideTouchCard()
    {
        if(touchCard?.Parent is Grid grid) grid.Children.Remove(touchCard);
        touchCard=null;
    }
}
