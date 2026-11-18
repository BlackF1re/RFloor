
namespace RFloor;

// Local piecewise-linear interpolation. Geometry is built once per room/render.
// Support is a distance heuristic, not a statistical confidence interval.
internal sealed class CoverageInterpolator
{
    internal readonly record struct Sample(Point Position, double? Value);
    private readonly record struct Triangle(int A, int B, int C);
    private readonly record struct Patch(Sample A, Sample B, Sample C)
    {
        private readonly Vector ab=B.Position-A.Position, ac=C.Position-A.Position;
        private readonly double area=Cross(B.Position-A.Position,C.Position-A.Position);
        internal double Left => Math.Min(A.Position.X,Math.Min(B.Position.X,C.Position.X));
        internal double Right => Math.Max(A.Position.X,Math.Max(B.Position.X,C.Position.X));
        internal double Top => Math.Min(A.Position.Y,Math.Min(B.Position.Y,C.Position.Y));
        internal double Bottom => Math.Max(A.Position.Y,Math.Max(B.Position.Y,C.Position.Y));

        internal bool Contains(Point p,out double wa,out double wb,out double wc)
        {
            var offset=p-A.Position;
            wb=Cross(offset,ac)/area; wc=Cross(ab,offset)/area; wa=1-wb-wc;
            return wa>=-1e-8 && wb>=-1e-8 && wc>=-1e-8;
        }
    }
    private readonly Sample[] samples;
    private readonly Point[] positions;
    private readonly List<Triangle> triangles=[];
    private readonly Func<Point,Point,bool> blocked;
    internal const double SupportRadius=4.5;
    private readonly double radius;

    internal CoverageInterpolator(IEnumerable<Sample> source, Func<Point,Point,bool>? blocked=null,double radius=SupportRadius)
    {
        this.radius=radius;
        this.blocked=blocked??((_,_)=>false);
        // Repeated coordinates must not multiply influence. The latest reading wins.
        samples=source.Where(s=>double.IsFinite(s.Position.X)&&double.IsFinite(s.Position.Y)
            && (s.Value is null || double.IsFinite(s.Value.Value)))
            .GroupBy(s=>s.Position).Select(g=>g.Last()).OrderBy(s=>s.Position.X).ThenBy(s=>s.Position.Y).ToArray();
        if(samples.Length<3){positions=[];return;}
        double minX=samples.Min(s=>s.Position.X),minY=samples.Min(s=>s.Position.Y);
        double scale=Math.Max(samples.Max(s=>s.Position.X)-minX,samples.Max(s=>s.Position.Y)-minY);
        if(scale<=1e-9){positions=[];return;}
        positions=samples.Select(s=>new Point((s.Position.X-minX)/scale,(s.Position.Y-minY)/scale))
            .Concat([new Point(-32,-16),new Point(33,-16),new Point(.5,33)]).ToArray();
        int n=samples.Length;
        triangles.Add(new(n,n+1,n+2));
        for(int i=0;i<n;i++) {
            var bad=triangles.Where(t=>InCircle(t,positions[i])).ToList();
            var edges=new Dictionary<(int,int),int>();
            foreach(var t in bad)foreach(var (a,b) in new[]{(t.A,t.B),(t.B,t.C),(t.C,t.A)}) {
                var edge=a<b?(a,b):(b,a);edges[edge]=edges.GetValueOrDefault(edge)+1;
            }
            foreach(var t in bad)triangles.Remove(t);
            foreach(var edge in edges.Where(e=>e.Value==1).Select(e=>e.Key)) {
                double area=Cross(positions[edge.Item2]-positions[edge.Item1],positions[i]-positions[edge.Item1]);
                if(Math.Abs(area)>1e-12)triangles.Add(area>0?new(edge.Item1,edge.Item2,i):new(edge.Item2,edge.Item1,i));
            }
        }
        triangles.RemoveAll(t=>t.A>=n||t.B>=n||t.C>=n);
    }

    private static double Cross(Vector a,Vector b)=>a.X*b.Y-a.Y*b.X;
    private bool InCircle(Triangle t,Point p)
    {
        Vector a=positions[t.A]-p,b=positions[t.B]-p,c=positions[t.C]-p;
        double determinant=a.SquaredLength*Cross(b,c)-b.SquaredLength*Cross(a,c)+c.SquaredLength*Cross(a,b);
        return determinant>1e-12;
    }

    internal (double Value,double Confidence)? Estimate(Point position)
    {
        if(!double.IsFinite(position.X)||!double.IsFinite(position.Y))return null;
        foreach(var t in triangles) {
            var patch=new Patch(samples[t.A],samples[t.B],samples[t.C]);
            if(patch.Contains(position,out var wa,out var wb,out var wc))
                return Evaluate(patch,position,wa,wb,wc);
        }
        return null;
    }

    private (double Value,double Confidence)? Evaluate(Patch patch,Point position,double wa,double wb,double wc)
    {
        double nearest=double.PositiveInfinity, value=0;
        if(!AddSupport(patch.A,position,wa,ref nearest,ref value) ||
           !AddSupport(patch.B,position,wb,ref nearest,ref value) ||
           !AddSupport(patch.C,position,wc,ref nearest,ref value)) return null;
        return (value,Math.Clamp(1-Math.Sqrt(nearest)/radius,.15,1));
    }

    private bool AddSupport(Sample sample,Point position,double weight,ref double nearest,ref double value)
    {
        if(weight<=1e-8) return true;
        if(sample.Value is not double measured) return false;
        double squaredDistance=((Vector)(sample.Position-position)).SquaredLength;
        if(squaredDistance>radius*radius || blocked(position,sample.Position)) return false;
        nearest=Math.Min(nearest,squaredDistance);
        value+=measured*weight;
        return true;
    }

    // Rasterize the triangle footprint with four subpixel samples per pixel.
    // Each subpixel belongs to the first matching triangle, including shared edges.
    internal byte[]? Rasterize(Rect bounds,int width,int height,Func<double,byte,Color> palette)
    {
        if(triangles.Count==0) return null;
        int subWidth=width*2,subHeight=height*2;
        double dx=bounds.Width/subWidth,dy=bounds.Height/subHeight;
        var surface=new uint[subWidth*subHeight];
        bool painted=false;
        foreach(var t in triangles) {
            var patch=new Patch(samples[t.A],samples[t.B],samples[t.C]);
            int x0=Start(patch.Left,bounds.X,dx,subWidth), x1=End(patch.Right,bounds.X,dx,subWidth);
            int y0=Start(patch.Top,bounds.Y,dy,subHeight), y1=End(patch.Bottom,bounds.Y,dy,subHeight);
            for(int y=y0;y<y1;y++) for(int x=x0;x<x1;x++) {
                int index=y*subWidth+x;
                if(surface[index]!=0) continue;
                var p=new Point(bounds.X+(x+.5)*dx,bounds.Y+(y+.5)*dy);
                if(!patch.Contains(p,out var wa,out var wb,out var wc)) continue;
                if(Evaluate(patch,p,wa,wb,wc) is not {} estimate) {
                    surface[index]=1; // Transparent, claimed by this triangle.
                    continue;
                }
                var color=palette(estimate.Value,(byte)(170*estimate.Confidence));
                uint a=color.A;
                surface[index]=a<<24 | color.R*a/255<<16 | color.G*a/255<<8 | color.B*a/255;
                painted=true;
            }
        }
        if(!painted) return null;
        var pixels=new byte[width*height*4];
        for(int y=0;y<height;y++) for(int x=0;x<width;x++) {
            int from=2*y*subWidth+2*x, to=(y*width+x)*4;
            uint a=surface[from], b=surface[from+1], c=surface[from+subWidth], d=surface[from+subWidth+1];
            if(a==1) a=0;
            if(b==1) b=0;
            if(c==1) c=0;
            if(d==1) d=0;
            for(int channel=0;channel<4;channel++) {
                int shift=channel*8;
                pixels[to+channel]=(byte)((((a>>shift)&255)+((b>>shift)&255)+((c>>shift)&255)+((d>>shift)&255)+2)/4);
            }
        }
        return pixels;

        static int Start(double edge,double origin,double step,int limit) =>
            (int)Math.Clamp(Math.Floor((edge-origin)/step),0,limit);
        static int End(double edge,double origin,double step,int limit) =>
            (int)Math.Clamp(Math.Ceiling((edge-origin)/step),0,limit);
    }
}
