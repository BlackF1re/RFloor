using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private enum LayerKind { Off, Signal, Download, Upload, Ping }
    private readonly record struct SurveyLayer(LayerKind Kind,string? Target=null)
    {
        public bool Matches(SurveyLayer other) =>
            Kind==other.Kind && string.Equals(Target,other.Target,StringComparison.OrdinalIgnoreCase);
    }
    private readonly record struct WallSegment(Point A,Point B)
    {
        public bool Crosses(Point a,Point b)
        {
            Vector r=b-a,s=B-A,q=A-a;
            double padX=Math.Abs(s.X)*1e-5,padY=Math.Abs(s.Y)*1e-5;
            if(Math.Max(a.X,b.X)<Math.Min(A.X,B.X)-padX || Math.Min(a.X,b.X)>Math.Max(A.X,B.X)+padX ||
               Math.Max(a.Y,b.Y)<Math.Min(A.Y,B.Y)-padY || Math.Min(a.Y,b.Y)>Math.Max(A.Y,B.Y)+padY) return false;
            double den=r.X*s.Y-r.Y*s.X;
            if(Math.Abs(den)<1e-9) return false;
            double t=(q.X*s.Y-q.Y*s.X)/den,u=(q.X*r.Y-q.Y*r.X)/den;
            return t>1e-5 && t<1-1e-5 && u>=-1e-5 && u<=1+1e-5;
        }
    }
    private sealed record CoverageTile(Rect Bounds,CoverageInterpolator Model,WriteableBitmap Bitmap);
    private sealed record CoverageTiles(
        CoverageInterpolator.Sample[] Samples,Rect[] Rooms,WallSegment[] Walls,double Radius,
        (double Low,double Mid,double High) Thresholds,string Kind,List<CoverageTile> Tiles);

    private SurveyLayer? requestedLayer;
    private SurveyLayer activeLayer;
    private List<SurveyLayer> availableLayers=[];
    private CoverageTiles? coverageTiles;

    private string LayerTitle(SurveyLayer layer) => layer.Kind switch {
        LayerKind.Signal=>L("Сигнал"),
        LayerKind.Download=>L("iperf3 · скачивание"),
        LayerKind.Upload=>L("iperf3 · отправка"),
        LayerKind.Ping=>L("Ping · {0}",layer.Target),
        _=>L("Слои выключены")
    };

    private void NextLayer_Click(object? sender,RoutedEventArgs e)
    {
        if(availableLayers.Count<2) return;
        int current=availableLayers.IndexOf(activeLayer);
        SelectLayer(availableLayers[(current+1)%availableLayers.Count]);
    }

    private void SelectLayer(SurveyLayer layer)
    {
        requestedLayer=layer;
        RenderPlan();
    }

    private static IEnumerable<JsonObject> Pings(JsonObject measurements) =>
        (measurements["pings"] as JsonArray)?.OfType<JsonObject>() ?? [];

    private static (double Value,string Kind)? LayerSample(JsonObject? measurements,SurveyLayer layer)
    {
        if(measurements is null) return null;
        switch(layer.Kind) {
            case LayerKind.Signal:
                if(Numeric(measurements["rssi"],out var rssi)) return (rssi,"rssi");
                if(Numeric(measurements["quality"],out var quality)) return (quality,"quality");
                break;
            case LayerKind.Download:
                if(Numeric(measurements["downloadMbps"],out var download)) return (download,"download");
                break;
            case LayerKind.Upload:
                if(Numeric(measurements["uploadMbps"],out var upload)) return (upload,"upload");
                break;
            case LayerKind.Ping:
                foreach(var ping in Pings(measurements))
                    if(string.Equals(ping["target"]?.ToString().Trim(),layer.Target,StringComparison.OrdinalIgnoreCase) &&
                       Numeric(ping["avg"],out var latency)) return (latency,"ping");
                break;
        }
        return null;
    }

    private (double Value,string Kind)? Sample(JsonObject point) =>
        LayerSample(MapReading(point)?["measurements"] as JsonObject,
            activeLayer.Kind==LayerKind.Off ? new(LayerKind.Signal) : activeLayer);

    private void UpdateLayers(IReadOnlyList<JsonObject> readings)
    {
        var layers=new List<SurveyLayer>();
        foreach(var kind in new[]{LayerKind.Signal,LayerKind.Download,LayerKind.Upload}) {
            var layer=new SurveyLayer(kind);
            if(readings.Any(m=>LayerSample(m,layer) is not null)) layers.Add(layer);
        }
        var targets=readings.SelectMany(Pings).Where(p=>Numeric(p["avg"],out _))
            .Select(p=>p["target"]?.ToString().Trim()).OfType<string>().Where(t=>t.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t=>t,StringComparer.OrdinalIgnoreCase);
        layers.AddRange(targets.Select(t=>new SurveyLayer(LayerKind.Ping,t)));
        layers.Add(new(LayerKind.Off));

        var chosen=requestedLayer is {} requested
            ? layers.FindIndex(layer=>layer.Matches(requested)) : -1;
        activeLayer=layers[Math.Max(0,chosen)];
        availableLayers=layers;
        LayerPicker.IsEnabled=layers.Count>1;
        LayerLabel.Text=layers.Count>1 ? LayerTitle(activeLayer) : L("Нет данных для слоёв");
        ToolTip.SetTip(LayerPicker,L("Следующий слой карты"));
        var flyout=(MenuFlyout)LayerPicker.Flyout!;
        var items=flyout.Items.OfType<MenuItem>();
        if(!items.Select(i=>((SurveyLayer)i.Tag!,i.Header as string)).SequenceEqual(
            layers.Select(layer=>(layer,(string?)LayerTitle(layer))))) {
            flyout.Items.Clear();
            foreach(var layer in layers) {
                var item=new MenuItem {Header=LayerTitle(layer),Tag=layer,ToggleType=MenuItemToggleType.Radio,GroupName="CoverageLayers"};
                item.Click+=(_,_)=>SelectLayer(layer);
                flyout.Items.Add(item);
            }
        }
        foreach(var item in items) item.IsChecked=(SurveyLayer)item.Tag! == activeLayer;
    }

    private static bool NoCoverage(double value,string kind) => (kind=="rssi" && value<=-120) || (kind=="quality" && value<=0);

    private (double Low,double Mid,double High) LayerThresholds(string kind)
    {
        var defaults=ThresholdDefaults.First(t=>t.Key==kind).Values;
        if(document["settings"]?["thresholds"]?[kind] is JsonArray {Count:3} saved &&
           Numeric(saved[0],out var a) && Numeric(saved[1],out var b) && Numeric(saved[2],out var c) &&
           (kind is "ping" or "loss" ? a>b && b>c : a<b && b<c)) return (a,b,c);
        return (defaults[0],defaults[1],defaults[2]);
    }

    private Color SampleColor(double value,string kind,byte alpha=255) =>
        CoverageColor(value,alpha,LayerThresholds(kind),kind is "ping" or "loss");

    private static string CoverageUnit(string kind) => kind switch {
        "rssi"=>"dBm", "quality"=>"%", "ping"=>L("мс"), _=>L("Мбит/с")
    };

    private static Color CoverageColor(double value,byte alpha,(double Low,double Mid,double High) thresholds,bool reversed)
    {
        var (a,b,c)=thresholds;
        if(reversed) { value=-value; a=-a; b=-b; c=-c; }
        double score=value<=a?0:value>=c?1:value<=b?.5*(value-a)/(b-a):.5+.5*(value-b)/(c-b);
        var yellow=Color.FromRgb(255,220,0);
        var low=score<.5?Color.FromRgb(255,40,30):yellow;
        var high=score<.5?yellow:Color.FromRgb(40,240,45);
        double t=score<.5?score*2:(score-.5)*2;
        return Color.FromArgb(alpha,(byte)(low.R+(high.R-low.R)*t),(byte)(low.G+(high.G-low.G)*t),(byte)(low.B+(high.B-low.B)*t));
    }

    private void RenderHeatmap()
    {
        var readings=Points.OfType<JsonObject>()
            .Select(p=>(Position:new Point(Number(p["x"]),Number(p["y"])), Measurements:MapReading(p)?["measurements"]))
            .Where(p=>p.Measurements is JsonObject).ToArray();
        UpdateLayers(readings.Select(p=>(JsonObject)p.Measurements!).ToArray());
        if(activeLayer.Kind==LayerKind.Off) {
            HeatLegend.Text=availableLayers.Count>1 ? L("Тепловая карта скрыта") : "";
            return;
        }

        var measured=readings.Select(p=>(p.Position,Sample:LayerSample((JsonObject)p.Measurements!,activeLayer)))
            .Where(p=>p.Sample.HasValue).Select(p=>(p.Position,p.Sample!.Value.Value,p.Sample.Value.Kind)).ToArray();
        var covered=measured.Where(p=>!NoCoverage(p.Value,p.Kind)).ToArray();
        string kind=covered.FirstOrDefault(p=>p.Kind=="rssi").Kind ?? covered.FirstOrDefault().Kind ?? measured[0].Kind;
        var samples=measured.Where(p=>p.Kind==kind || NoCoverage(p.Value,p.Kind)).Select(p=>
            new CoverageInterpolator.Sample(p.Position,NoCoverage(p.Value,p.Kind)?null:p.Value)).ToArray();
        var thresholds=LayerThresholds(kind);
        HeatLegend.Text=L("Красный: {0:0.#} · жёлтый: {1:0.#} · зелёный: {2:0.#} {3}\nТочек: {4}. Локальная оценка между замерами (минимум 3, не на одной прямой), радиус опоры {5:0.##} м. Прозрачный — нет покрытия или недостаточно данных.",
            thresholds.Low,thresholds.Mid,thresholds.High,CoverageUnit(kind),samples.Length,CoverageRadius);

        var rooms=Rooms.OfType<JsonObject>().Select(r=>new Rect(Number(r["x"]),Number(r["y"]),Number(r["w"]),Number(r["h"]))).ToArray();
        var walls=Walls.OfType<JsonObject>().Select(w=>new WallSegment(End(w,"a"),End(w,"b"))).ToArray();
        double radius=CoverageRadius;
        if(coverageTiles is not {} cache || cache.Radius!=radius || cache.Thresholds!=thresholds || cache.Kind!=kind ||
           !cache.Samples.SequenceEqual(samples) || !cache.Rooms.SequenceEqual(rooms) || !cache.Walls.SequenceEqual(walls)) {
            if(coverageTiles is {} old)
                foreach(var tile in old.Tiles) tile.Bitmap.Dispose();
            cache=new(samples,rooms,walls,radius,thresholds,kind,[]);
            if(samples.Count(p=>p.Value.HasValue)>=3) {
                foreach(var room in rooms) {
                    var tile=BuildCoverageTile(room,samples,walls,radius,thresholds,kind=="ping");
                    if(tile is {} result) cache.Tiles.Add(result);
                }
            }
            coverageTiles=cache;
        }
        foreach(var (bounds,_,bitmap) in cache.Tiles) {
            var image=new Image {Source=bitmap,Width=bounds.Width*Scale,Height=bounds.Height*Scale,
                Stretch=Stretch.Fill,IsHitTestVisible=false,UseLayoutRounding=false};
            RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.MediumQuality);
            Canvas.SetLeft(image,Screen(bounds.X)); Canvas.SetTop(image,Screen(bounds.Y)); Plan.Children.Add(image);
        }
    }

    private static CoverageTile? BuildCoverageTile(Rect room,CoverageInterpolator.Sample[] samples,
        WallSegment[] walls,double radius,(double Low,double Mid,double High) thresholds,bool reversed)
    {
        var local=samples.Where(p=>p.Position.X>=room.Left-WallFaceTolerance && p.Position.X<=room.Right+WallFaceTolerance &&
            p.Position.Y>=room.Top-WallFaceTolerance && p.Position.Y<=room.Bottom+WallFaceTolerance).ToArray();
        if(local.Count(p=>p.Value.HasValue)<3 || room.Width<=0 || room.Height<=0) return null;
        var support=new Rect(new Point(local.Min(p=>p.Position.X),local.Min(p=>p.Position.Y)),
            new Point(local.Max(p=>p.Position.X),local.Max(p=>p.Position.Y)));
        var bounds=room.Intersect(support.Inflate(.02));
        if(bounds.Width<=0 || bounds.Height<=0) return null;
        double density=Math.Min(112,Math.Min(2048/Math.Max(bounds.Width,bounds.Height),Math.Sqrt(640000/(bounds.Width*bounds.Height))));
        int width=(int)Math.Ceiling(bounds.Width*density), height=(int)Math.Ceiling(bounds.Height*density);
        var model=new CoverageInterpolator(local,Blocked,radius);
        var pixels=model.Rasterize(bounds,width,height,(value,alpha)=>CoverageColor(value,alpha,thresholds,reversed));
        if(pixels is null) return null;
        var bitmap=new WriteableBitmap(new PixelSize(width,height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
        using(var frame=bitmap.Lock()) {
            for(int y=0;y<height;y++)
                Marshal.Copy(pixels,y*width*4,IntPtr.Add(frame.Address,y*frame.RowBytes),width*4);
        }
        return new(bounds,model,bitmap);

        bool Blocked(Point a,Point b)
        {
            foreach(var wall in walls)
                if(wall.Crosses(a,b)) return true;
            return false;
        }
    }

    private Border? hoveredSurveyPoint;
    private readonly SolidColorBrush coverageHoverColor=new();

    private void UpdateCoverageHover()
    {
        if(CoverageHoverBadge is null) return;
        double width=Viewport.ViewportWidth,height=Viewport.ViewportHeight;
        if(!surveyMode || activeSession is null || activeLayer.Kind==LayerKind.Off || panAnchor is not null ||
           coverageTiles is not {Tiles.Count:>0} cache || rulerPointer is not {} pointer ||
           pointer.X<0 || pointer.Y<0 || pointer.X>=width || pointer.Y>=height ||
           Viewport.TranslatePoint(pointer,Plan) is not {} local ||
           hoveredSurveyPoint is {} hit && ReferenceEquals(hit.Parent,Plan) && hit.Bounds.Contains(local)) {
            CoverageHoverBadge.IsVisible=false;
            return;
        }

        var position=World(local,false);
        // The map paints later rooms over earlier rooms where their areas overlap.
        (double Value,double Confidence)? estimate=null;
        for(int i=cache.Tiles.Count-1;i>=0 && estimate is null;i--) {
            var tile=cache.Tiles[i];
            if(tile.Bounds.Contains(position)) estimate=tile.Model.Estimate(position);
        }
        if(estimate is not {} sample || WallFaces().Any(wall=>InsideWall(position,wall))) {
            CoverageHoverBadge.IsVisible=false;
            return;
        }

        CoverageHoverText.Text=$"{sample.Value:0.#} {CoverageUnit(cache.Kind)}";
        coverageHoverColor.Color=CoverageColor(sample.Value,255,cache.Thresholds,cache.Kind=="ping");
        CoverageHoverMarker.Fill=coverageHoverColor;
        CoverageHoverBadge.IsVisible=true;
        CoverageHoverBadge.Measure(Size.Infinity);
        var size=CoverageHoverBadge.DesiredSize;
        const double gap=13, inset=2;
        double x=pointer.X+gap+size.Width<=width-inset ? pointer.X+gap : pointer.X-gap-size.Width;
        double y=pointer.Y-gap-size.Height>=inset ? pointer.Y-gap-size.Height : pointer.Y+gap;
        Canvas.SetLeft(CoverageHoverBadge,Math.Clamp(x,inset,Math.Max(inset,width-size.Width-inset)));
        Canvas.SetTop(CoverageHoverBadge,Math.Clamp(y,inset,Math.Max(inset,height-size.Height-inset)));
    }
}
