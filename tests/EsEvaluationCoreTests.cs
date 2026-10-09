// Standalone core tests: compile with SignalEngine.cs, ResearchWriter.cs, ShadowResearch.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using NinjaTrader.NinjaScript.Strategies;
internal static class EsEvaluationCoreTests
{
    private static void Assert(bool ok,string message) { if(!ok) throw new Exception(message); }
    private static EvaluationSignalEngine Warm()
    {
        var e=new EvaluationSignalEngine(.25,2,1,4,2,2,120);
        DateTime start=new DateTime(2025,9,16,8,50,0);
        for(int i=0;i<45*60;i++)
        {
            DateTime t=start.AddSeconds(i);
            double price=t.Hour==9 && t.Minute==31?102:t.Hour==9 && t.Minute==32?100:t.Hour==9 && t.Minute==33?101.5:t.Hour==9 && t.Minute==34?101.75:101;
            e.Accept(t,price,1,(a,b)=>{});
        }
        e.Accept(new DateTime(2025,9,16,9,35,0),101,1,(a,b)=>{});
        return e;
    }
    private static List<EvaluationCandidate> Feed(EvaluationSignalEngine e,double[] prices)
    {
        var result=new List<EvaluationCandidate>();
        DateTime t=new DateTime(2025,9,16,9,35,1);
        foreach(double p in prices) { result.AddRange(e.Accept(t,p,1,(a,b)=>{})); t=t.AddSeconds(1); }
        return result;
    }
    public static void Main()
    {
        var e=Warm();
        var signals=Feed(e,new[]{100.0,99.75,99.5,99.75,100,100.25});
        Assert(signals.Any(x=>x.Model=="SweepReclaim" && x.LevelName=="OR5L" && x.Direction==1 && x.Stop==99),"Gradual tick sweep/reclaim missing or wrong stop");
        signals=Feed(Warm(),new[]{100.0,99.75,100,100.25});
        Assert(!signals.Any(x=>x.Model=="SweepReclaim" && x.LevelName=="OR5L"),"Shallow touch falsely qualified as sweep");
        signals=Feed(Warm(),new[]{102.0,102.25,102.5,102.75,103,102.75,102.5,102.25,102.5,102.75,103});
        Assert(signals.Any(x=>x.Model=="MomentumRetest" && x.LevelName=="OR5H" && x.Direction==1),"Momentum retest missing");
        var count=0;
        e.Accept(new DateTime(2025,9,16,9,34,0),100,1,(a,b)=>{if(a=="OutOfOrder")count++;});
        Assert(count==1,"Out-of-order tick not flagged");
        Assert(!e.InWindow(new DateTime(2025,9,16,11,30,0)),"Entry cutoff wrong");
        Assert(e.InWindow(new DateTime(2025,9,16,13,0,0)),"Afternoon window wrong");
        string root=Path.Combine(Path.GetTempPath(),"evaluation-test-"+Guid.NewGuid().ToString("N"));
        string path;
        using(var w=new EvaluationResearchWriter(root,"ES 12-25",true))
        {
            path=w.DirectoryPath;
            w.Write("manifest","Quoted", "a,b\"c");
            var shadows=new EvaluationShadowResearch(w,.25);
            shadows.Register(new EvaluationCandidate{Id=1,Direction=1,Model="Fixture"},new DateTime(2025,9,16,10,0,0),100);
            shadows.Accept(new DateTime(2025,9,16,10,0,1),102,true);
            shadows.Finish(new DateTime(2025,9,16,10,0,2));
            w.Raw(new DateTime(2025,9,16,10,0,0),100,1,0,true);
        }
        Assert(File.ReadAllText(Path.Combine(path,"manifest.csv")).Contains("a,b\"\"c"),"CSV escaping");
        using(var z=new GZipStream(File.OpenRead(Path.Combine(path,"ticks.csv.gz")),CompressionMode.Decompress))
        using(var r=new StreamReader(z)) Assert(r.ReadToEnd().Contains("100"),"Gzip export unreadable");
        Assert(File.ReadAllText(Path.Combine(path,"shadows.csv")).Contains("Censored"),"Unfinished shadow not censored");
        Directory.Delete(root,true);
        Console.WriteLine("PASS: gradual sweeps, shallow rejection, retests, ordering, windows, CSV/gzip, shadow censoring");
    }
}
