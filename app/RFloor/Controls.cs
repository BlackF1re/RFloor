namespace RFloor.Controls;

public class Button : Avalonia.Controls.Button {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.Button);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class ToggleButton : Avalonia.Controls.Primitives.ToggleButton {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.Primitives.ToggleButton);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class TextBox : Avalonia.Controls.TextBox {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.TextBox);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class TextBlock : Avalonia.Controls.TextBlock {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.TextBlock);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class Border : Avalonia.Controls.Border {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.Border);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class Canvas : Avalonia.Controls.Canvas {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.Canvas);}
public class TabItem : Avalonia.Controls.TabItem {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.TabItem);public object? Tip {get=>ToolTip.GetTip(this);set=>ToolTip.SetTip(this,value);}}
public class MenuItem : Avalonia.Controls.MenuItem {protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.MenuItem);public string InputGestureText {set {try{InputGesture=KeyGesture.Parse(value.Replace("Del","Delete"));}catch(ArgumentException){}}}}
public class ScrollViewer : Avalonia.Controls.ScrollViewer
{protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.ScrollViewer);
    public double HorizontalOffset=>Offset.X;
    public double VerticalOffset=>Offset.Y;
    public double ViewportWidth=>Viewport.Width;
    public double ViewportHeight=>Viewport.Height;
    public double ExtentWidth=>Extent.Width;
    public double ExtentHeight=>Extent.Height;
    public void ScrollToHorizontalOffset(double x)=>Offset=new Vector(x,Offset.Y);
    public void ScrollToVerticalOffset(double y)=>Offset=new Vector(Offset.X,y);
}
public class Line : Avalonia.Controls.Shapes.Line
{protected override Type StyleKeyOverride=>typeof(Avalonia.Controls.Shapes.Line);
    public double X1 {get=>StartPoint.X;set=>StartPoint=new Point(value,StartPoint.Y);}
    public double Y1 {get=>StartPoint.Y;set=>StartPoint=new Point(StartPoint.X,value);}
    public double X2 {get=>EndPoint.X;set=>EndPoint=new Point(value,EndPoint.Y);}
    public double Y2 {get=>EndPoint.Y;set=>EndPoint=new Point(EndPoint.X,value);}
}

