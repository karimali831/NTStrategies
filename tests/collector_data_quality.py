# Run with Python 3 and .NET 8; compiles actual collector methods against small platform-free fixtures.
from pathlib import Path
import tempfile, subprocess
source = Path("src/Strategies/NinjexEsMarketResearchCollector/Core.cs").read_text(encoding="utf-8-sig")
def method(name):
    start = source.index("        private void " + name + "(")
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]
methods = "\n".join(method(n) for n in ["PopulateTickStatistics", "FinalizeTickMinute", "UpdateForwardObservations", "CaptureForwardHorizon", "FinalizeRemainingForwardObservations"])
fixture = r'''
using System;
using System.Linq;
using System.Collections.Generic;
class Test {
 double TickSize=0.25; int MaximumForwardMinutes=60;
 Dictionary<DateTime,TickMinuteStats> completedTickStats=new();
 List<ForwardObservation> activeForwardObservations=new();
 List<MarketObservation> written=new(); TickMinuteStats activeTickStats;
 static DateTime TruncateMinute(DateTime t)=>new(t.Year,t.Month,t.Day,t.Hour,t.Minute,0);
 void WriteObservation(MarketObservation r)=>written.Add(r);
 class TickMinuteStats {
  public DateTime Minute; public int TickCount=3,UpTicks=1,DownTicks=1,UnchangedTicks=1;
  public double High=101,Low=100,First=100,Last=101;
 }
 class MarketObservation {
  public DateTime Time,TickStatsMinute; public bool TickStatsAvailable,ForwardComplete;
  public int TickCount,UpTicks,DownTicks,UnchangedTicks,ForwardMinutesObserved;
  public double UpTickPercent,TickRangeTicks,TickNetChangeTicks;
  public string ForwardFinalizeReason;
  public bool H5Complete,H10Complete,H15Complete,H30Complete,H60Complete;
  public object H5=new(),H10=new(),H15=new(),H30=new(),H60=new();
 }
 class ForwardObservation {
  public MarketObservation Row; public int MinutesObserved;
  public ForwardObservation(MarketObservation r){Row=r;}
  public void Update(double h,double l,double c){MinutesObserved++;}
  public void Capture(object target,int minutes){}
 }
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static DateTime T=new(2025,10,1,9,36,0);
 MarketObservation Start(){var r=new MarketObservation{Time=T};activeForwardObservations.Add(new(r));return r;}
 void Bar(int minutes)=>UpdateForwardObservations(T.AddMinutes(minutes),101,100,100.5);
 static void Main(){
  var a=new Test();var r=a.Start();
  a.completedTickStats[T]=new(){Minute=T};
  a.PopulateTickStatistics(r,T);Check(!r.TickStatsAvailable,"must not use next bucket");
  a.activeTickStats=new(){Minute=T.AddMinutes(-1)};
  a.FinalizeTickMinute();Check(r.TickStatsAvailable&&r.TickStatsMinute==T.AddMinutes(-1),"minute callback first");
  var b=new Test();b.activeTickStats=new(){Minute=T.AddMinutes(-1)};b.FinalizeTickMinute();
  var rb=b.Start();b.PopulateTickStatistics(rb,T);Check(rb.TickStatsAvailable,"tick callback first");
  for(int i=1;i<=60;i++)b.Bar(i);
  Check(rb.ForwardComplete&&rb.H5Complete&&rb.H60Complete&&rb.ForwardMinutesObserved==60,"contiguous 60 minutes");
  var c=new Test();var rc=c.Start();for(int i=1;i<=5;i++)c.Bar(i);c.Bar(7);
  Check(!rc.ForwardComplete&&rc.H5Complete&&!rc.H10Complete&&rc.ForwardMinutesObserved==5&&rc.ForwardFinalizeReason=="ForwardMinuteGap"&&c.written.Count==1,"gap preserves earlier horizon");
  var d=new Test();var rd=d.Start();d.Bar(6);
  Check(!rd.H5Complete&&rd.ForwardMinutesObserved==0&&rd.ForwardFinalizeReason=="ForwardMinuteGap","late bar cannot fill 5-minute horizon");
  var e=new Test();var re=e.Start();e.Bar(1);e.Bar(1);
  Check(re.ForwardFinalizeReason=="ForwardMinuteOutOfOrder"&&re.ForwardMinutesObserved==1,"duplicate not counted");
  var f=new Test();var rf=f.Start();for(int i=1;i<=5;i++)f.Bar(i);f.FinalizeRemainingForwardObservations("RthEnd");
  Check(rf.H5Complete&&!rf.ForwardComplete&&rf.ForwardFinalizeReason=="RthEnd","session end incomplete");
  Console.WriteLine("PASS: exact tick mapping, both callback orders, contiguous/gapped/duplicate minutes, retained horizons, session end");
 }
'''
with tempfile.TemporaryDirectory() as tmp:
    p=Path(tmp)
    (p/"Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>')
    (p/"Program.cs").write_text(fixture+"\n"+methods+"\n}")
    subprocess.run(["dotnet","run","--project",str(p/"Test.csproj")],check=True)
