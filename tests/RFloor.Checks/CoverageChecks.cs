using Avalonia;
using RFloor;
using Sample=RFloor.CoverageInterpolator.Sample;

internal static class CoverageChecks
{
    private static void Check(bool condition,string message) {if(!condition)throw new Exception(message);}
    private static double Field(double x,double y)=>-65+12*Math.Sin(x*1.1)*Math.Cos(y*.9);
    internal static void Run()
    {
        var triangle=new[]{new Sample(new(0,0),-40),new Sample(new(3,0),-70),new Sample(new(0,3),-100)};
        var model=new CoverageInterpolator(triangle);
        foreach(var sample in triangle)Check(Math.Abs(model.Estimate(sample.Position)!.Value.Value-sample.Value!.Value)<1e-8,"Measurement anchor changed.");
        Check(Math.Abs(model.Estimate(new(1,1))!.Value.Value+70)<1e-8,"Barycentric interpolation failed.");
        Check(new CoverageInterpolator(triangle,radius:2).Estimate(new(1,1)) is null,"Support radius did not limit interpolation.");
        Check(model.Estimate(new(2,2)) is null,"Extrapolated outside the survey hull.");
        Check(model.Estimate(new(double.NaN,0)) is null,"Nonfinite query accepted.");
        Check(new CoverageInterpolator(triangle.Take(1)).Estimate(new(0,0)) is null,"One sample painted a heatmap.");
        Check(new CoverageInterpolator(Enumerable.Range(0,5).Select(i=>new Sample(new(i,0),-60))).Estimate(new(1,0)) is null,"Collinear samples painted an area.");
        var gap=new CoverageInterpolator([new(new(0,0),-40),new(new(20,0),-70),new(new(0,20),-100)]);
        Check(gap.Estimate(new(.1,.1)) is null,"A nearby sample justified a large unsurveyed triangle.");
        var blocked=new CoverageInterpolator(triangle,(a,b)=>a.X<.5&&b.X>.5);
        Check(blocked.Estimate(new(.25,.25)) is null,"Prediction crossed a wall.");
        var uncovered=new CoverageInterpolator([triangle[0],triangle[1],new(new(0,3),null)]);
        Check(uncovered.Estimate(new(.5,.5)) is null,"No coverage was converted to a fabricated RSSI.");
        Check(uncovered.Estimate(new(1,0)) is not null,"An uncovered vertex influenced a separate measured edge.");
        var duplicates=new CoverageInterpolator(triangle.Concat(Enumerable.Repeat(triangle[0],200)).Append(new(new(0,0),-50)));
        Check(Math.Abs(duplicates.Estimate(new(0,0))!.Value.Value+50)<1e-8,"Duplicate coordinates biased the latest reading.");

        // The narrow triangle from a survey that used to render as detached squares.
        var narrow=new CoverageInterpolator([new(new(6,.5),-49),new(new(5.2,1.7),-45),new(new(5.5,3),-45)]);
        var bounds=new Rect(4,0,3,4);
        Avalonia.Media.Color Green(double value,byte alpha)=>Avalonia.Media.Color.FromArgb(alpha,0,255,0);
        var pixels=narrow.Rasterize(bounds,300,400,Green)!;
        byte AlphaAt(int x,int y)=>pixels[(y*300+x)*4+3];
        for(int y=52;y<298;y++) {
            double worldY=(y+.5)/100;
            double left=worldY<1.7?6+(5.2-6)*(worldY-.5)/1.2:5.2+(5.5-5.2)*(worldY-1.7)/1.3;
            double right=6+(5.5-6)*(worldY-.5)/2.5;
            int x=(int)(((left+right)/2-4)*100);
            Check(AlphaAt(x,y)>0,"The narrow triangle has a detached row.");
        }
        Check(AlphaAt(15,160)==0,"The raster paints beyond the survey footprint.");
        Check(uncovered.Rasterize(new Rect(0,0,3,3),60,60,Green) is null,"Rasterization turned missing coverage into color.");
        var masked=blocked.Rasterize(new Rect(0,0,3,3),60,60,Green)!;
        Check(masked[(5*60+5)*4+3]==0 && masked[(5*60+25)*4+3]>0,"Rasterization ignored a wall mask.");
        var first=new CoverageInterpolator(triangle.Select(s=>s with {Value=40}));
        var second=new CoverageInterpolator(triangle.Select(s=>new Sample(s.Position+new Vector(1,0),80)));
        var unsupported=new CoverageInterpolator(triangle.Take(1).Select(s=>s with {Value=110}));
        Avalonia.Media.Color Encode(double value,byte alpha)=>Avalonia.Media.Color.FromArgb(value>0?(byte)255:(byte)0,0,0,(byte)value);
        foreach(var (mode,wanted) in new[]{(SurveySummary.Average,60),(SurveySummary.Minimum,40),(SurveySummary.Maximum,80)}) {
            var combined=CoverageInterpolator.Rasterize([first,second,unsupported],mode,new Rect(0,0,4,3),80,60,Encode)!;
            int Pixel(int x,int y)=>combined[(y*80+x)*4];
            Check(Pixel(30,10)==wanted && Pixel(10,10)==40 && Pixel(64,4)==80 && Pixel(75,55)==0,
                "Summary raster pooled raw points, counted unsupported data, or extrapolated.");
        }

        // A grid exercises cocircular points and shared edges. Any affine field
        // must be reproduced exactly, independently of sample density.
        double Affine(double x,double y)=>-90+3*x+2*y;
        var grid=new CoverageInterpolator(from x in Enumerable.Range(0,9) from y in Enumerable.Range(0,9)
            select new Sample(new(x*.5,y*.5),Affine(x*.5,y*.5)));
        for(double x=.13;x<4;x+=.21)for(double y=.17;y<4;y+=.23) {
            var estimate=grid.Estimate(new(x,y));
            Check(estimate is not null&&Math.Abs(estimate.Value.Value-Affine(x,y))<1e-7,"Grid triangulation has a hole or interpolation error.");
        }
        // More spatially distributed samples should improve a smooth nonlinear
        // reference field at held-out locations; this is not a real-world guarantee.
        double Error(int count) {
            var field=new CoverageInterpolator(from x in Enumerable.Range(0,count) from y in Enumerable.Range(0,count)
                let px=x*4.0/(count-1) let py=y*4.0/(count-1) select new Sample(new(px,py),Field(px,py)));
            double sum=0;int n=0;
            for(double x=.1;x<4;x+=.19)for(double y=.13;y<4;y+=.17) {
                var predicted=field.Estimate(new(x,y));Check(predicted is not null,"Reference field has an unexplained gap.");
                sum+=Math.Pow(predicted!.Value.Value-Field(x,y),2);n++;
            }
            return Math.Sqrt(sum/n);
        }
        double sparse=Error(3),dense=Error(9);
        Check(dense<sparse*.3,"Distributed additional samples did not improve held-out predictions.");
        Console.WriteLine($"Coverage passed: anchors, hull, gaps, barriers, absent coverage, duplicate/grid geometry; synthetic RMSE {sparse:F3} -> {dense:F3} dB.");
    }
}
