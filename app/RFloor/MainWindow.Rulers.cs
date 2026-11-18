
namespace RFloor;

public partial class MainWindow
{
    private Point? rulerPointer;
    private int rulerMode = 1;
    private void CycleRuler_Click(object sender, RoutedEventArgs e) { rulerMode=(rulerMode+1)%3; RefreshRulers(); SavePreferences(); }
    private void UpdateRulerIcon()
    {
        if(RulerModeButton is null)return;
        var icon=new Canvas { Width=18,Height=18,IsHitTestVisible=false };
        void Line(double x1,double y1,double x2,double y2, IBrush? color=null) => icon.Children.Add(new Line {X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=color??Brushes.Gray,StrokeThickness=1.5});
        Line(2,2,16,2);Line(2,2,2,16);
        for(int i=6;i<=14;i+=4){Line(i,2,i,5);Line(2,i,5,i);}
        if(rulerMode==0)Line(3,16,16,3,Brushes.Firebrick);
        else {Line(11,0,11,6,Brushes.DarkOrange);Line(0,11,6,11,Brushes.DarkOrange);if(rulerMode==2){Line(11,6,11,16,Brushes.DarkOrange);Line(6,11,16,11,Brushes.DarkOrange);}}
        RulerModeButton.Content=icon;
        RulerModeButton.Tip=new[]{L("Без линеек → линейки с рисками"),L("Линейки с рисками → проекция курсора"),L("Проекция курсора → без линеек")}[rulerMode];
    }
    private void Viewport_ScrollChanged(object sender, ScrollChangedEventArgs e) => RefreshRulers();
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) => RefreshRulers();
    private void Viewport_PointerMove(object sender, PointerEventArgs e) { if(e.Pointer.Type==PointerType.Touch)return; PanMove(e); if(!surveyMode && tool=="tape" && tapeStart is not null && panAnchor is null){tapeCursor=TapeSnap(World(e.GetPosition(Plan),false));DrawTape();} rulerPointer=e.GetPosition(Viewport); RefreshRulers(); }
    private void Viewport_PointerLeave(object sender, PointerEventArgs e) { rulerPointer=null; RefreshRulers(); if(routerPreview is not null){routerPreview=null;DrawRouterPreview();} if(e.Pointer.Type!=PointerType.Touch && pointPreview is not null){pointPreview=null;DrawPointPreview();} }
    private void RefreshRulers()
    {
        if (RulerModeButton is null || RulerTop is null || Viewport is null || ProjectionOverlay is null || PlanZoom is null) return;
        UpdateRulerIcon(); bool visible=rulerMode>0;
        foreach (var ruler in new[] { RulerTop,RulerBottom,RulerLeft,RulerRight }) { ruler.IsVisible=visible?true:false; ruler.Children.Clear(); }
        ProjectionOverlay.Children.Clear(); if (!visible) return;
        double zoom=PlanZoom.ScaleX, unit=Scale*zoom;
        if (unit<1) return;
        double step=unit<50?2:unit>150?.5:1;
        void Draw(Canvas canvas, bool vertical, double offset, double size, bool far)
        {
            if (size<=0) return;
            double start=Math.Ceiling((offset/zoom-origin)/Scale/step)*step;
            for (double value=start; (origin+value*Scale)*zoom-offset<size; value+=step)
            {
                double position=(origin+value*Scale)*zoom-offset; if (position<0) continue;
                var tick=new Line { Stroke=Brushes.Gray,StrokeThickness=1 };
                if (vertical) { tick.X1=far?0:18; tick.X2=far?6:24; tick.Y1=tick.Y2=position; }
                else { tick.Y1=far?0:18; tick.Y2=far?6:24; tick.X1=tick.X2=position; }
                canvas.Children.Add(tick);
                var text=new TextBlock { Text=(Math.Abs(value)<1e-8 ? 0 : value).ToString("0.#"),FontSize=9,Foreground=Brushes.DimGray,IsHitTestVisible=false };
                if (vertical) { text.RenderTransform=new RotateTransform(-90); Canvas.SetLeft(text,far?7:0); Canvas.SetTop(text,position+2); }
                else { Canvas.SetLeft(text,position+2); Canvas.SetTop(text,far?7:0); }
                canvas.Children.Add(text);
            }
        }
        Draw(RulerTop,false,Viewport.HorizontalOffset-PlanSurface.Padding.Left,Viewport.ViewportWidth,false); Draw(RulerBottom,false,Viewport.HorizontalOffset-PlanSurface.Padding.Left,Viewport.ViewportWidth,true);
        Draw(RulerLeft,true,Viewport.VerticalOffset-PlanSurface.Padding.Top,Viewport.ViewportHeight,false); Draw(RulerRight,true,Viewport.VerticalOffset-PlanSurface.Padding.Top,Viewport.ViewportHeight,true);
        if (rulerPointer is not Point p || p.X<0 || p.Y<0 || p.X>Viewport.ViewportWidth || p.Y>Viewport.ViewportHeight) return;
        foreach (var ruler in new[] { RulerTop,RulerBottom,RulerLeft,RulerRight })
        {
            bool vertical=ruler==RulerLeft || ruler==RulerRight;
            ruler.Children.Add(vertical?new Line { X1=0,X2=24,Y1=p.Y,Y2=p.Y,Stroke=Brushes.DarkOrange,StrokeThickness=2 }:new Line { X1=p.X,X2=p.X,Y1=0,Y2=24,Stroke=Brushes.DarkOrange,StrokeThickness=2 });
        }
        if (rulerMode==2)
        {
            ProjectionOverlay.Children.Add(new Line { X1=p.X,X2=p.X,Y1=0,Y2=Viewport.ViewportHeight,Stroke=Brushes.DarkOrange,Opacity=.65,StrokeThickness=1,StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> {4,4} });
            ProjectionOverlay.Children.Add(new Line { X1=0,X2=Viewport.ViewportWidth,Y1=p.Y,Y2=p.Y,Stroke=Brushes.DarkOrange,Opacity=.65,StrokeThickness=1,StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> {4,4} });
        }
    }
}
