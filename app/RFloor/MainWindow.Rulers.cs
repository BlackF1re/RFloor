
using System.Globalization;

namespace RFloor;

public partial class MainWindow
{
    private const double ProjectionStroke = 1;
    private Point? rulerPointer;
    private int rulerMode = 1;
    private (int Mode, double Zoom, double Origin, Size Size, Vector Offset, CultureInfo Culture)? rulerLayout;
    private (int Mode, string Language)? rulerIconState;
    private RulerAxis[]? rulerAxes;
    private (Canvas Canvas, Line Line, TranslateTransform Position, bool Vertical, bool Projection)[]? rulerMarkers;
    private void CycleRuler_Click(object sender, RoutedEventArgs e) { rulerMode=(rulerMode+1)%3; RefreshRulers(); SavePreferences(); }
    private void UpdateRulerIcon()
    {
        if(RulerModeButton is null)return;
        var state=(rulerMode,Language);
        if(rulerIconState==state)return;
        rulerIconState=state;
        var icon=new Canvas { Width=18,Height=18,IsHitTestVisible=false };
        void Line(double x1,double y1,double x2,double y2, IBrush? color=null) => icon.Children.Add(new Line {X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=color??Brushes.Gray,StrokeThickness=1.5});
        Line(2,2,16,2);Line(2,2,2,16);
        for(int i=6;i<=14;i+=4){Line(i,2,i,5);Line(2,i,5,i);}
        if(rulerMode==0)Line(3,16,16,3,Brushes.Firebrick);
        else {Line(11,0,11,6,Brushes.DarkOrange);Line(0,11,6,11,Brushes.DarkOrange);if(rulerMode==2){Line(11,6,11,16,Brushes.DarkOrange);Line(6,11,16,11,Brushes.DarkOrange);}}
        RulerModeButton.Content=icon;
        RulerModeButton.Tip=new[]{L("Без линеек → линейки с рисками"),L("Линейки с рисками → проекция курсора"),L("Проекция курсора → без линеек")}[rulerMode];
    }
    private void Viewport_ScrollChanged(object sender, ScrollChangedEventArgs e) { RefreshRulers(); UpdateCoverageHover(); }
    private void Viewport_SizeChanged(object sender, SizeChangedEventArgs e) { RefreshRulers(); UpdateCoverageHover(); }
    private void Viewport_PointerMove(object sender, PointerEventArgs e)
    {
        if(e.Pointer.Type==PointerType.Touch)return;
        PanMove(e);
        if(!surveyMode && tool=="tape" && tapeStart is not null && panAnchor is null &&
           e.Source is Visual source && source.GetSelfAndVisualAncestors().Contains(Plan))
        {
            var cursor=TapeSnap(World(e.GetPosition(Plan),false));
            if(tapeCursor!=cursor){tapeCursor=cursor;DrawPendingTape();}
        }
        rulerPointer=e.GetPosition(Viewport);
        SetHoveredSurveyPoint(e.Source is Border {Tag:"survey-point-hit"} hit ? hit : null);
        UpdateRulerPointer();
        UpdateCoverageHover();
    }
    private void Viewport_PointerLeave(object sender, PointerEventArgs e)
    {
        rulerPointer=null; SetHoveredSurveyPoint(null);
        UpdateRulerPointer();
        UpdateCoverageHover();
        if(routerPreview is not null){routerPreview=null;DrawRouterPreview();}
        if(e.Pointer.Type!=PointerType.Touch && pointPreview is not null){pointPreview=null;DrawPointPreview();}
    }
    private void RefreshRulers()
    {
        if (RulerModeButton is null || RulerRight is null || Viewport is null || ProjectionOverlay is null || PlanZoom is null) return;
        UpdateRulerIcon();
        double zoom=PlanZoom.ScaleX;
        var size=new Size(Viewport.ViewportWidth,Viewport.ViewportHeight);
        var offset=new Vector(Viewport.HorizontalOffset-PlanSurface.Padding.Left,Viewport.VerticalOffset-PlanSurface.Padding.Top);
        var layout=(rulerMode,zoom,origin,size,offset,CultureInfo.CurrentCulture);
        if(layout==rulerLayout)return;
        rulerLayout=layout;
        double unit=Scale*zoom;
        bool visible=rulerMode>0 && unit>=1;
        RulerTop.IsVisible=RulerBottom.IsVisible=RulerLeft.IsVisible=RulerRight.IsVisible=visible;
        ProjectionOverlay.IsVisible=visible && rulerMode==2;
        if(!visible)return;

        if(rulerAxes is null)
        {
            rulerAxes=[
                new(RulerTop,false,false),new(RulerBottom,false,true),
                new(RulerLeft,true,false),new(RulerRight,true,true)
            ];
            rulerMarkers=[
                CreateMarker(RulerTop,true),CreateMarker(RulerBottom,true),
                CreateMarker(RulerLeft,false),CreateMarker(RulerRight,false),
                CreateMarker(ProjectionOverlay,true,true),CreateMarker(ProjectionOverlay,false,true)
            ];
            foreach(var (canvas,line,_,vertical,projection) in rulerMarkers)
            {
                if(!projection) { if(vertical)line.Y2=24;else line.X2=24; }
                canvas.Children.Add(line);
            }
        }
        double step=unit<50?2:unit>150?.5:1;
        rulerAxes[0].Update(unit,step,origin*zoom-offset.X,size.Width);
        rulerAxes[1].Update(unit,step,origin*zoom-offset.X,size.Width);
        rulerAxes[2].Update(unit,step,origin*zoom-offset.Y,size.Height);
        rulerAxes[3].Update(unit,step,origin*zoom-offset.Y,size.Height);
        rulerMarkers![4].Line.Y2=size.Height;
        rulerMarkers[5].Line.X2=size.Width;
        UpdateRulerPointer();
    }

    private sealed class RulerAxis
    {
        private readonly Canvas marks;
        private readonly TranslateTransform position=new();
        private readonly bool vertical,far;
        private (double Unit, double Step, double First, int Count, CultureInfo Culture)? layout;

        internal RulerAxis(Canvas canvas,bool vertical,bool far)
        {
            this.vertical=vertical;
            this.far=far;
            marks=new Canvas {IsHitTestVisible=false,RenderTransform=position};
            canvas.Children.Add(marks);
        }

        internal void Update(double unit,double step,double zero,double length)
        {
            marks.Width=vertical?24:length;
            marks.Height=vertical?length:24;
            double spacing=unit*step;
            double first=Math.Ceiling(-zero/spacing);
            int count=Math.Max(0,(int)(Math.Ceiling((length-zero)/spacing)-first));
            var next=(unit,step,first,count,CultureInfo.CurrentCulture);
            if(vertical)position.Y=zero+first*spacing;
            else position.X=zero+first*spacing;
            if(layout==next)return;
            layout=next;
            marks.Children.Clear();
            for(int i=0;i<count;i++)
            {
                double at=i*spacing;
                var tick=new Line {Stroke=Brushes.Gray,StrokeThickness=1};
                double value=(first+i)*step;
                var text=new TextBlock {Text=(value==0?0:value).ToString("0.#"),FontSize=9,Foreground=Brushes.DimGray};
                if(vertical)
                {
                    tick.X1=far?0:18;tick.X2=far?6:24;tick.Y1=tick.Y2=at;
                    text.RenderTransform=new RotateTransform(-90);
                    Canvas.SetLeft(text,far?7:0);Canvas.SetTop(text,at+2);
                }
                else
                {
                    tick.Y1=far?0:18;tick.Y2=far?6:24;tick.X1=tick.X2=at;
                    Canvas.SetLeft(text,at+2);Canvas.SetTop(text,far?7:0);
                }
                marks.Children.Add(tick);
                marks.Children.Add(text);
            }
        }
    }

    private static (Canvas,Line,TranslateTransform,bool,bool) CreateMarker(Canvas canvas,bool vertical,bool projection=false)
    {
        var position=new TranslateTransform();
        var line=new Line {
            Stroke=Brushes.DarkOrange,StrokeThickness=projection?ProjectionStroke:2,
            Opacity=projection?.65:1,RenderTransform=position,IsHitTestVisible=false,IsVisible=false
        };
        if(projection)line.StrokeDashArray=new Avalonia.Collections.AvaloniaList<double> {4,4};
        return (canvas,line,position,vertical,projection);
    }

    private void UpdateRulerPointer()
    {
        if(rulerMode==0 || rulerMarkers is null)return;
        var point=rulerPointer.GetValueOrDefault();
        bool inside=rulerPointer.HasValue && point.X>=0 && point.Y>=0 &&
            point.X<=Viewport.ViewportWidth && point.Y<=Viewport.ViewportHeight;
        foreach(var (_,line,position,vertical,projection) in rulerMarkers)
        {
            line.IsVisible=inside && (!projection || rulerMode==2);
            if(!line.IsVisible)continue;
            if(vertical)position.X=point.X;
            else position.Y=point.Y;
        }
    }
}
