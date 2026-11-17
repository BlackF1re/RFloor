
namespace RFloor;

// Local piecewise-linear interpolation. Geometry is built once per room/render.
// Support is a distance heuristic, not a statistical confidence interval.
internal sealed class CoverageInterpolator
{
    internal readonly record struct Sample(Point Position, double? Value);
    private readonly record struct Triangle(int A, int B, int C);
    private readonly Sample[] samples;
    private readonly Point[] positions;
    private readonly List<Triangle> triangles=[];
    private readonly Func<Point,Point,bool> blocked;
    internal const double SupportRadius=4.5;

    internal CoverageInterpolator(IEnumerable<Sample> source, Func<Point,Point,bool>? blocked=null)
    {
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
            var a=samples[t.A];var b=samples[t.B];var c=samples[t.C];
            double area=Cross(b.Position-a.Position,c.Position-a.Position);
            double wb=Cross(position-a.Position,c.Position-a.Position)/area;
            double wc=Cross(b.Position-a.Position,position-a.Position)/area;
            double wa=1-wb-wc;
            if(wa < -1e-8||wb < -1e-8||wc < -1e-8)continue;
            var support=new[]{(a,Math.Max(0,wa)),(b,Math.Max(0,wb)),(c,Math.Max(0,wc))};
            // Unknown coverage is categorical: never invent an RSSI value for it.
            // Triangles touching an uncovered reading stay blank until sampled further.
            if(support.Any(s=>s.Item2>1e-8 && s.Item1.Value is null))return null;
            double nearest=double.PositiveInfinity,farthest=0,value=0;
            foreach(var (sample,weight) in support.Where(s=>s.Item2>1e-8)) {
                double distance=((Vector)(sample.Position-position)).Length;
                if(blocked(position,sample.Position))return null;
                nearest=Math.Min(nearest,distance);farthest=Math.Max(farthest,distance);
                value+=sample.Value!.Value*weight;
            }
            // One nearby sample cannot justify a large unsurveyed triangle.
            if(farthest>SupportRadius)return null;
            double opacity=Math.Clamp(1-nearest/SupportRadius,.15,1);
            return (value,opacity);
        }
        return null;
    }
}
