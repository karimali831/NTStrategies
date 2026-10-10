using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.Strategies;
internal static class EsEvaluationRefinementTests
{
    private static void Check(bool x,string why){if(!x)throw new Exception(why);}
    private static EvaluationSignalEngine Warm()
    {
        var e=new EvaluationSignalEngine(.25,2,1,4,2,2,120,new EvaluationEngineOptions{ConfirmedEntries=true,ReclaimHoldSeconds=2});
        DateTime t=new DateTime(2025,9,16,8,50,0);
        for(int i=0;i<2700;i++)
        {
            var now=t.AddSeconds(i);
            double p=now.Hour==9 && now.Minute==31?102:now.Hour==9 && now.Minute==32?100:now.Hour==9 && now.Minute==33?101.5:now.Hour==9 && now.Minute==34?101.75:101;
            e.Accept(now,p,1,(a,b)=>{});
        }
        e.Accept(new DateTime(2025,9,16,9,35,0),101,1,(a,b)=>{});return e;
    }
    public static void Main()
    {
        var frame=new EvaluationTrendFrame(15);int closed=0;
        DateTime start=new DateTime(2025,9,15,18,0,0);
        Action<int,DateTime,double,double,double,double,double,bool> export=(a,b,c,d,e,f,g,h)=>{closed++;Check(h,"Synthetic complete frame invalid");};
        for(int i=0;i<14;i++)frame.Accept(new EvaluationMinute{Start=start.AddMinutes(i),End=start.AddMinutes(i+1),Open=100+i,High=101+i,Low=99+i,Close=100.5+i,Volume=1,Complete=true},export);
        Check(closed==0 && frame.AsOf==default(DateTime),"Unclosed 15-minute candle leaked");
        frame.Accept(new EvaluationMinute{Start=start.AddMinutes(14),End=start.AddMinutes(15),Open=114,High=115,Low=113,Close=114.5,Volume=1,Complete=true},export);
        Check(closed==1 && frame.AsOf==start.AddMinutes(15),"15-minute close/as-of clock wrong");
        var f15=new EvaluationTrendFrame(15);var f30=new EvaluationTrendFrame(30);var f60=new EvaluationTrendFrame(60);
        for(int i=0;i<14*60;i++)
        {
            var m=new EvaluationMinute{Start=start.AddMinutes(i),End=start.AddMinutes(i+1),Open=100+i*.05,High=101+i*.05,Low=99+i*.05,Close=100.5+i*.05,Complete=true};
            f15.Accept(m,export);f30.Accept(m,export);f60.Accept(m,export);
            Check(f15.AsOf<=m.End && f30.AsOf<=m.End && f60.AsOf<=m.End,"Trend look-ahead");
        }
        Check(!f15.IsCurrent(start.AddHours(16)),"Stale completed frame still treated as current");
        Check(f15.Ready && f30.Ready && f60.Ready && f15.Direction==1 && f30.Direction==1 && f60.Direction==1,"Aligned completed trends missing");
        // Incomplete higher-timeframe bar must invalidate its directional readiness.
        for(int i=0;i<15;i++)f15.Accept(new EvaluationMinute{Start=start.AddHours(14).AddMinutes(i),End=start.AddHours(14).AddMinutes(i+1),Open=140,High=141,Low=139,Close=140,Complete=i!=3},(a,b,c,d,e,f,g,h)=>Check(!h,"Incomplete bar accepted"));
        Check(!f15.Ready && f15.Direction==0,"Invalid candle retained trend readiness");
        var e1=Warm();DateTime t=new DateTime(2025,9,16,9,35,1);int early=0;
        foreach(double p in new[]{100.0,99.75,99.5,99.25,100,100.25,101}){early+=e1.Accept(t,p,1,(a,b)=>{}).Count;t=t.AddSeconds(1);}
        Check(early==0,"Tick reclaim bypassed completed rejection candle");
        var signals=new List<EvaluationCandidate>();
        for(int i=0;i<3;i++)signals.AddRange(e1.Accept(new DateTime(2025,9,16,9,36,i),101,1,(a,b)=>{}));
        Check(signals.Any(c=>c.Model=="SweepReclaim" && c.LevelName=="OR5L" && c.ConfirmationTime==new DateTime(2025,9,16,9,36,0)),"Confirmed sweep/hold/as-of missing");
        var e2=Warm();t=new DateTime(2025,9,16,9,35,1);early=0;
        foreach(double p in new[]{102.0,102.25,102.5,103}){early+=e2.Accept(t,p,1,(a,b)=>{}).Count;t=t.AddSeconds(1);}
        Check(early==0,"Momentum bypassed completed displacement candle");
        e2.Accept(new DateTime(2025,9,16,9,36,0),103,1,(a,b)=>{});
        e2.Accept(new DateTime(2025,9,16,9,36,1),102.25,1,(a,b)=>{});
        signals=e2.Accept(new DateTime(2025,9,16,9,36,2),103,1,(a,b)=>{});
        Check(signals.Any(c=>c.Model=="MomentumRetest" && c.LevelName=="OR5H"),"Impulse then later retest/bounce missing");
        // Quiet intervals are diagnostics with a separate, less aggressive threshold.
        var quiet=new EvaluationSignalEngine(.25,2,1,4,2,2,120);int bad=0;
        quiet.Accept(new DateTime(2025,9,15,18,0,0),100,1,(a,b)=>bad++);
        quiet.Accept(new DateTime(2025,9,15,18,1,30),100,1,(a,b)=>bad++);
        Check(bad==0,"Normal 90-second overnight quiet interval treated as missing data");
        Console.WriteLine("PASS: closed HTF/as-of/readiness, no-lookahead, completed rejection/hold, impulse-before-retest, quiet gap handling");
    }
}
