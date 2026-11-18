using System.Text.Json.Nodes;
namespace RFloor;
public partial class MainWindow {
    private Rect DocumentBounds() {var points=Walls.Concat(Doors).SelectMany(n=>new[]{End(n!.AsObject(),"a"),End(n!.AsObject(),"b")}).Concat(Decorations.SelectMany(n=>new[]{DecorationBounds(n!.AsObject()).TopLeft,DecorationBounds(n!.AsObject()).BottomRight})).ToList();return points.Count==0?new Rect(0,0,1,1):new Rect(points.Min(p=>p.X),points.Min(p=>p.Y),Math.Max(1,points.Max(p=>p.X)-points.Min(p=>p.X)),Math.Max(1,points.Max(p=>p.Y)-points.Min(p=>p.Y)));}
    private async void ExportImage_Click(object? sender,RoutedEventArgs e) {
        if(Walls.Count==0 && Decorations.Count==0){Status.Text=L("Сначала создайте план.");return;}
        var file=await TopLevel.GetTopLevel(this)!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {Title=L("Экспорт карты"),SuggestedFileName="RFloor.png",DefaultExtension="png",FileTypeChoices=[new FilePickerFileType("PNG") {Patterns=["*.png"],MimeTypes=["image/png"]}]});
        if(file is null)return;
        var saved=Objects.SelectedItems!.Cast<object>().ToArray();
        try {
            CommitInlineRename();Objects.SelectedItems.Clear();RenderPlan();
            var bounds=DocumentBounds();const double margin=32,header=64;
            var source=new Rect(Screen(bounds.X)-margin,Screen(bounds.Y)-margin,bounds.Width*Scale+margin*2,bounds.Height*Scale+margin*2);
            double width=Math.Max(360,source.Width),height=source.Height+header+(surveyMode?70:0),factor=Math.Min(1,6000/Math.Max(width,height));
            var canvas=new Avalonia.Controls.Canvas {Width=width,Height=height,Background=Brushes.White};
            var title=new TextBlock {Text=document["name"]?.ToString()??"RFloor",FontSize=20};Canvas.SetLeft(title,12);Canvas.SetTop(title,8);canvas.Children.Add(title);
            var subtitle=new TextBlock {Text=surveyMode?$"{LayerTitle(activeLayer)} · {(BandPicker.SelectedItem as ComboBoxItem)?.Content}":L("План помещения"),Foreground=Brushes.Gray};Canvas.SetLeft(subtitle,12);Canvas.SetTop(subtitle,36);canvas.Children.Add(subtitle);
            var map=new Avalonia.Controls.Border {Width=source.Width,Height=source.Height,Background=new VisualBrush(Plan) {SourceRect=new RelativeRect(source,RelativeUnit.Absolute),Stretch=Stretch.Fill}};Canvas.SetTop(map,header);canvas.Children.Add(map);
            if(surveyMode){var legend=new TextBlock {Text=HeatLegend.Text,MaxWidth=width-24,TextWrapping=TextWrapping.Wrap};Canvas.SetLeft(legend,12);Canvas.SetTop(legend,header+source.Height+8);canvas.Children.Add(legend);}
            canvas.Measure(new Size(width,height));canvas.Arrange(new Rect(0,0,width,height));
            using var bitmap=new RenderTargetBitmap(new PixelSize(Math.Max(1,(int)(width*factor)),Math.Max(1,(int)(height*factor))),new Vector(96*factor,96*factor));bitmap.Render(canvas);
            await using var stream=await file.OpenWriteAsync();if(stream.CanSeek)stream.SetLength(0);bitmap.Save(stream);
            Status.Text=L("Карта сохранена как изображение.");
        }catch(Exception ex) when(ex is System.IO.IOException or UnauthorizedAccessException or ArgumentException or OutOfMemoryException){MessageBox.Show(this,ex.Message,L("Экспорт карты"));}
        finally {foreach(var item in saved)if(Objects.Items.Contains(item))Objects.SelectedItems!.Add(item);RenderPlan();}
    }
}
