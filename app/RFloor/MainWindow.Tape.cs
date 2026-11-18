using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private JsonArray Tapes => Geometry["tapes"] as JsonArray ?? (JsonArray)(Geometry["tapes"]=new JsonArray())!;
    private Border TapeLabel(Point a,Point b) => new() {CornerRadius=new CornerRadius(4),Background=Brushes.White,Padding=new Thickness(4,2,4,2),Child=new TextBlock {Text=L("{0:0.##} м", ((Vector)(b-a)).Length),Foreground=Brushes.Black,FontSize=12}};
    private Rect TapeLabelBounds(Point a,Point b)
    {
        var label=TapeLabel(a,b);label.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        return new Rect(new Point(Screen((a.X+b.X)/2)+6,Screen((a.Y+b.Y)/2)+6),label.DesiredSize);
    }
    private Line? pendingTapeLine;
    private TextBlock? pendingTapeLength;
    private Border? pendingTapeLabel;
    private bool DeleteTapeAt(Point p)
    {
        var pixel=new Point(Screen(p.X),Screen(p.Y));
        var hit=Tapes.Select(n=>n!.AsObject()).Reverse().FirstOrDefault(t=>SegmentDistance(p,t).Distance<7/(Scale*PlanZoom.ScaleX) || TapeLabelBounds(End(t,"a"),End(t,"b")).Contains(pixel));
        if(hit is null)return false;
        Remember();Tapes.Remove(hit);dirty=true;DrawTape();RefreshTabHeaders();return true;
    }
    // Updating the tape must not replace hit targets, selection or mouse-wheel routes.
    private void DrawTape()
    {
        foreach(var child in Plan.Children.OfType<FrameworkElement>().Where(v=>Equals(v.Tag,"tape-overlay")).ToList())Plan.Children.Remove(child);
        if(surveyMode)return;
        void Add(FrameworkElement element){element.Tag="tape-overlay";element.IsHitTestVisible=false;Ui.SetZIndex(element,150);Plan.Children.Add(element);}
        foreach(var tape in Tapes.Select(n=>n!.AsObject())) {
            Point a=End(tape,"a"),b=End(tape,"b");
            Add(new Line {X1=Screen(a.X),Y1=Screen(a.Y),X2=Screen(b.X),Y2=Screen(b.Y),Stroke=Brushes.DarkOrange,StrokeThickness=1});
            var label=TapeLabel(a,b);
            PositionTapeLabel(label,a,b);Add(label);
        }
        DrawPendingTape();
    }

    private void PositionTapeLabel(Border label,Point a,Point b)
    {
        Canvas.SetLeft(label,Screen((a.X+b.X)/2)+6);
        Canvas.SetTop(label,Screen((a.Y+b.Y)/2)+6);
    }

    private void DrawPendingTape()
    {
        if(tapeStart is not Point a || surveyMode)return;
        Point b=tapeCursor??a;
        if(pendingTapeLine is null)
        {
            pendingTapeLine=new Line {Stroke=Brushes.DarkOrange,StrokeThickness=1,Opacity=SelectionOpacity,Tag="tape-overlay",IsHitTestVisible=false};
            pendingTapeLabel=TapeLabel(a,b);
            pendingTapeLabel.Tag="tape-overlay";
            pendingTapeLabel.IsHitTestVisible=false;
            pendingTapeLength=(TextBlock)pendingTapeLabel.Child!;
            Ui.SetZIndex(pendingTapeLine,150);
            Ui.SetZIndex(pendingTapeLabel,150);
        }
        pendingTapeLine.X1=Screen(a.X);pendingTapeLine.Y1=Screen(a.Y);
        pendingTapeLine.X2=Screen(b.X);pendingTapeLine.Y2=Screen(b.Y);
        pendingTapeLength!.Text=L("{0:0.##} м",((Vector)(b-a)).Length);
        PositionTapeLabel(pendingTapeLabel!,a,b);
        if(pendingTapeLine.Parent is null)
        {
            Plan.Children.Add(pendingTapeLine);
            Plan.Children.Add(pendingTapeLabel!);
        }
    }
}
