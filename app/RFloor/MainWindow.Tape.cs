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
        void Draw(Point a,Point b,bool pending) {
            Add(new Line {X1=Screen(a.X),Y1=Screen(a.Y),X2=Screen(b.X),Y2=Screen(b.Y),Stroke=Brushes.DarkOrange,StrokeThickness=1,Opacity=pending?SelectionOpacity:1});
            var label=TapeLabel(a,b);Rect bounds=TapeLabelBounds(a,b);
            Canvas.SetLeft(label,bounds.X);Canvas.SetTop(label,bounds.Y);Add(label);
        }
        foreach(var tape in Tapes.Select(n=>n!.AsObject()))Draw(End(tape,"a"),End(tape,"b"),false);
        if(tapeStart is Point start)Draw(start,tapeCursor??start,true);
    }
}
