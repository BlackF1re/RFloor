using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private JsonArray Tapes => Geometry["tapes"] as JsonArray ?? (JsonArray)(Geometry["tapes"]=new JsonArray())!;
    private Border TapeLabel(Point a,Point b) => new() {CornerRadius=new CornerRadius(4),Background=Brushes.White,Padding=new Thickness(4,2,4,2),Child=new TextBlock {Text=L("{0:0.##} м", ((Vector)(b-a)).Length),Foreground=Brushes.Black,FontSize=12}};
    private Line? pendingTapeLine;
    private TextBlock? pendingTapeLength;
    private Border? pendingTapeLabel;
    private bool DeleteTapeAt(Point p)
    {
        var pixel=new Point(Screen(p.X),Screen(p.Y));
        var hit=Tapes.Select(n=>n!.AsObject()).Reverse().FirstOrDefault(t=>SegmentDistance(p,t).Distance<7/(Scale*PlanZoom.ScaleX) || Plan.Children.OfType<Border>().Any(label=>label.Tag is ObjectEntry entry && ReferenceEquals(entry.Item,t) &&
            new Rect(new Point(Canvas.GetLeft(label),Canvas.GetTop(label)),label.DesiredSize).Contains(pixel)));
        if(hit is null)return false;
        Remember();Tapes.Remove(hit);dirty=true;RenderPlan();return true;
    }
    // Updating the tape must not replace hit targets, selection or mouse-wheel routes.
    private void DrawTape()
    {
        foreach(var child in Plan.Children.OfType<FrameworkElement>().Where(v=>Equals(v.Tag,"tape-overlay")).ToList())Plan.Children.Remove(child);
        if(surveyMode)return;
        void Add(FrameworkElement element){element.Tag="tape-overlay";element.IsHitTestVisible=false;Ui.SetZIndex(element,91);Plan.Children.Add(element);}
        foreach(var tape in Tapes.Select(n=>n!.AsObject())) {
            Point a=End(tape,"a"),b=End(tape,"b");
            Add(new Line {X1=Screen(a.X),Y1=Screen(a.Y),X2=Screen(b.X),Y2=Screen(b.Y),Stroke=Brushes.DarkOrange,StrokeThickness=1,DataContext=tape});
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
