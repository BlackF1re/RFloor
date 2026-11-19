using System.Runtime.InteropServices;
using System.Text.Json.Nodes;

namespace RFloor;

public partial class MainWindow
{
    private enum LayerKind { Off, Signal, Download, Upload, Ping, Quality, PingMinimum, PingMaximum, Loss }
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
    private readonly record struct SourceSample(SurveySource Source,CoverageInterpolator.Sample Sample);
    private sealed record CoverageTile(Rect Bounds,CoverageInterpolator[] Models,WriteableBitmap Bitmap);
    private sealed record CoverageTiles(
        SourceSample[] Samples,Rect[] Rooms,WallSegment[] Walls,double Radius,SurveySummary Summary,
        (double Low,double Mid,double High) Thresholds,string Kind,List<CoverageTile> Tiles);

    private SurveyLayer? requestedLayer;
    private SurveyLayer activeLayer;
    private List<SurveyLayer> availableLayers=[];
    private CoverageTiles? coverageTiles;

    private string LayerTitle(SurveyLayer layer) => layer.Kind switch {
        LayerKind.Signal=>L("Сигнал"),
        LayerKind.Quality=>L("Качество сигнала"),
        LayerKind.Download=>L("iperf3 · скачивание"),
        LayerKind.Upload=>L("iperf3 · отправка"),
        LayerKind.Ping=>L("Ping · {0}",layer.Target),
        LayerKind.PingMinimum=>L("Ping · минимум · {0}",layer.Target),
        LayerKind.PingMaximum=>L("Ping · максимум · {0}",layer.Target),
        LayerKind.Loss=>L("Потери пакетов · {0}",layer.Target),
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
                break;
            case LayerKind.Quality:
                if(Numeric(measurements["quality"],out var quality)) return (quality,"quality");
                break;
            case LayerKind.Download:
                if(Numeric(measurements["downloadMbps"],out var download)) return (download,"download");
                break;
            case LayerKind.Upload:
                if(Numeric(measurements["uploadMbps"],out var upload)) return (upload,"upload");
                break;
            case LayerKind.Ping:
            case LayerKind.PingMinimum:
            case LayerKind.PingMaximum:
            case LayerKind.Loss:
                string field=layer.Kind switch {LayerKind.PingMinimum=>"min",LayerKind.PingMaximum=>"max",LayerKind.Loss=>"loss",_=>"avg"};
                foreach(var ping in Pings(measurements))
                    if(string.Equals(ping["target"]?.ToString().Trim(),layer.Target,StringComparison.OrdinalIgnoreCase) &&
                       Numeric(ping[field],out var latency)) return (latency,field=="loss"?"loss":"ping");
                break;
        }
        return null;
    }

    private (double Value,string Kind)? Sample(JsonObject point)
    {
        var samples=PointSourceSamples(ViewReadings(point),activeLayer.Kind==LayerKind.Off?new(LayerKind.Signal):activeLayer).ToArray();
        var total=new CoverageSummary();
        foreach(var sample in samples)
            if(!NoCoverage(sample.Sample.Value,sample.Sample.Kind)) total.Add(sample.Sample.Value,1,summary);
        if(total.Count>0) return (total.Value(summary),samples[0].Sample.Kind);
        return samples.Length>0?samples[0].Sample:null;
    }

    private static IEnumerable<(SurveySource Source,(double Value,string Kind) Sample)> PointSourceSamples(
        IEnumerable<JsonObject> readings,SurveyLayer layer) =>
        readings.Select(r=>(Reading:r,Sample:LayerSample(r["measurements"] as JsonObject,layer)))
            .Where(r=>r.Sample.HasValue).GroupBy(r=>ReadingSource(r.Reading))
            .Select(group=>{
                var latest=group.OrderBy(r=>r.Reading["timestamp"]?.ToString(),StringComparer.Ordinal).Last().Sample!.Value;
                return (group.Key,latest);
            });

    private void UpdateLayers(IReadOnlyList<JsonObject> readings)
    {
        var layers=new List<SurveyLayer>();
        foreach(var kind in new[]{LayerKind.Signal,LayerKind.Quality,LayerKind.Download,LayerKind.Upload}) {
            var layer=new SurveyLayer(kind);
            if(readings.Any(m=>LayerSample(m,layer) is not null)) layers.Add(layer);
        }
        var targets=readings.SelectMany(Pings)
            .Select(p=>p["target"]?.ToString().Trim()).OfType<string>().Where(t=>t.Length>0)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(t=>t,StringComparer.OrdinalIgnoreCase);
        foreach(var kind in new[]{LayerKind.Ping,LayerKind.PingMinimum,LayerKind.PingMaximum,LayerKind.Loss})
            layers.AddRange(targets.Select(t=>new SurveyLayer(kind,t)).Where(layer=>readings.Any(m=>LayerSample(m,layer) is not null)));
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
        "rssi"=>"dBm", "quality" or "loss"=>"%", "ping"=>L("мс"), _=>L("Мбит/с")
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
            .Select(p=>(Position:new Point(Number(p["x"]),Number(p["y"])), Readings:ViewReadings(p).ToArray())).ToArray();
        UpdateLayers(readings.SelectMany(p=>p.Readings).Select(r=>r["measurements"]).OfType<JsonObject>().ToArray());
        if(activeLayer.Kind==LayerKind.Off) {
            HeatLegend.Text=availableLayers.Count>1 ? L("Тепловая карта скрыта") : "";
            return;
        }

        var measured=readings.SelectMany(p=>PointSourceSamples(p.Readings,activeLayer)
            .Select(s=>(p.Position,s.Sample.Kind,s.Sample.Value,s.Source))).ToArray();
        string kind=measured[0].Kind;
        var samples=measured.Select(p=>new SourceSample(p.Source,
            new CoverageInterpolator.Sample(p.Position,NoCoverage(p.Value,p.Kind)?null:p.Value))).ToArray();
        var thresholds=LayerThresholds(kind);
        HeatLegend.Text=L("Красный: {0:0.#} · жёлтый: {1:0.#} · зелёный: {2:0.#} {3}\nТочек: {4}. Локальная оценка между замерами (минимум 3, не на одной прямой), радиус опоры {5:0.##} м. Прозрачный — нет покрытия или недостаточно данных.",
            thresholds.Low,thresholds.Mid,thresholds.High,CoverageUnit(kind),samples.Select(s=>s.Sample.Position).Distinct().Count(),CoverageRadius);
        if(SummaryView) HeatLegend.Text+=Environment.NewLine+L("Сводка · {0} · источников замеров: {1}",
            SummaryLabel(summary),samples.Select(s=>s.Source).Distinct().Count());

        var rooms=Rooms.OfType<JsonObject>().Select(r=>new Rect(Number(r["x"]),Number(r["y"]),Number(r["w"]),Number(r["h"]))).ToArray();
        var walls=Walls.OfType<JsonObject>().Select(w=>new WallSegment(End(w,"a"),End(w,"b"))).ToArray();
        double radius=CoverageRadius;
        if(coverageTiles is not {} cache || cache.Radius!=radius || cache.Thresholds!=thresholds || cache.Kind!=kind || cache.Summary!=summary ||
           !cache.Samples.SequenceEqual(samples) || !cache.Rooms.SequenceEqual(rooms) || !cache.Walls.SequenceEqual(walls)) {
            if(coverageTiles is {} old)
                foreach(var tile in old.Tiles) tile.Bitmap.Dispose();
            cache=new(samples,rooms,walls,radius,summary,thresholds,kind,[]);
            if(samples.Count(p=>p.Sample.Value.HasValue)>=3) {
                foreach(var room in rooms) {
                    var tile=BuildCoverageTile(room,samples,walls,radius,thresholds,kind is "ping" or "loss",summary);
                    if(tile is {} result) cache.Tiles.Add(result);
                }
            }
            coverageTiles=cache;
        }
        foreach(var (bounds,_,bitmap) in cache.Tiles) {
            var image=new Image {Source=bitmap,Width=bounds.Width*Scale,Height=bounds.Height*Scale,
                Stretch=Stretch.Fill,IsHitTestVisible=false,UseLayoutRounding=false,ZIndex=SurveyMarkerLayer-2};
            RenderOptions.SetBitmapInterpolationMode(image,BitmapInterpolationMode.MediumQuality);
            Canvas.SetLeft(image,Screen(bounds.X)); Canvas.SetTop(image,Screen(bounds.Y)); Plan.Children.Add(image);
        }
    }

    private static CoverageTile? BuildCoverageTile(Rect room,SourceSample[] samples,
        WallSegment[] walls,double radius,(double Low,double Mid,double High) thresholds,bool reversed,SurveySummary summary)
    {
        var groups=samples.Where(s=>s.Sample.Position.X>=room.Left-WallFaceTolerance && s.Sample.Position.X<=room.Right+WallFaceTolerance &&
            s.Sample.Position.Y>=room.Top-WallFaceTolerance && s.Sample.Position.Y<=room.Bottom+WallFaceTolerance)
            .GroupBy(s=>s.Source,s=>s.Sample).Where(g=>g.Count(p=>p.Value.HasValue)>=3).ToArray();
        if(groups.Length==0 || room.Width<=0 || room.Height<=0) return null;
        var local=groups.SelectMany(g=>g).ToArray();
        var support=new Rect(new Point(local.Min(p=>p.Position.X),local.Min(p=>p.Position.Y)),
            new Point(local.Max(p=>p.Position.X),local.Max(p=>p.Position.Y)));
        var bounds=room.Intersect(support.Inflate(.02));
        if(bounds.Width<=0 || bounds.Height<=0) return null;
        double density=Math.Min(112,Math.Min(2048/Math.Max(bounds.Width,bounds.Height),Math.Sqrt(250000/(bounds.Width*bounds.Height))));
        int width=(int)Math.Ceiling(bounds.Width*density), height=(int)Math.Ceiling(bounds.Height*density);
        var models=groups.Select(group=>new CoverageInterpolator(group,Blocked,radius)).ToArray();
        var pixels=CoverageInterpolator.Rasterize(models,summary,bounds,width,height,(value,alpha)=>CoverageColor(value,alpha,thresholds,reversed));
        if(pixels is null) return null;
        var bitmap=new WriteableBitmap(new PixelSize(width,height),new Vector(96,96),PixelFormat.Bgra8888,AlphaFormat.Premul);
        using(var frame=bitmap.Lock()) {
            for(int y=0;y<height;y++)
                Marshal.Copy(pixels,y*width*4,IntPtr.Add(frame.Address,y*frame.RowBytes),width*4);
        }
        return new(bounds,models,bitmap);

        bool Blocked(Point a,Point b)
        {
            foreach(var wall in walls)
                if(wall.Crosses(a,b)) return true;
            return false;
        }
    }

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
        var total=new CoverageSummary();
        for(int i=cache.Tiles.Count-1;i>=0 && total.Count==0;i--) {
            var tile=cache.Tiles[i];
            if(!tile.Bounds.Contains(position)) continue;
            foreach(var model in tile.Models)
                if(model.Estimate(position) is {} estimate) total.Add(estimate.Value,estimate.Confidence,cache.Summary);
        }
        if(total.Count==0 || WallFaces().Any(wall=>InsideWall(position,wall))) {
            CoverageHoverBadge.IsVisible=false;
            return;
        }

        double value=total.Value(cache.Summary);
        CoverageHoverText.Text=$"{value:0.#} {CoverageUnit(cache.Kind)}"+(SummaryView?L(" · Источников: {0}",total.Count):"");
        coverageHoverColor.Color=CoverageColor(value,255,cache.Thresholds,cache.Kind is "ping" or "loss");
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
