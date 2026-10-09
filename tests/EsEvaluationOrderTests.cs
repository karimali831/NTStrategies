using System;
using System.IO;
using System.Reflection;
using NinjaTrader.Cbi;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Strategies;
internal sealed class EvaluationHarness:NinjexEsEvaluationScalper
{
    internal void Init(){State=State.SetDefaults;OnStateChange();State=State.DataLoaded;OnStateChange();State=State.Realtime;OnStateChange();}
    internal void Fill(Order order,double price,int quantity)
    { OnExecutionUpdate(new Execution{Order=order},Guid.NewGuid().ToString(),price,quantity,MarketPosition.Flat,"fixture",new DateTime(2025,9,16,10,0,0)); }
    internal void Rejected(Order order)
    { OnOrderUpdate(order,0,0,2,0,0,OrderState.Rejected,new DateTime(2025,9,16,10,0,0),ErrorCode.OrderRejected,"FixtureReject"); }
    internal void EntryWorking(Order order)
    { OnOrderUpdate(order,0,0,2,1,100,OrderState.PartFilled,new DateTime(2025,9,16,10,0,0),ErrorCode.NoError,""); }
    internal void EntryCancelled(Order order)
    { OnOrderUpdate(order,0,0,2,1,100,OrderState.Cancelled,new DateTime(2025,9,16,10,0,0),ErrorCode.NoError,""); }
    internal void Finish(){State=State.Terminated;OnStateChange();}
}
internal static class EsEvaluationOrderTests
{
    private static readonly Type T=typeof(NinjexEsEvaluationScalper);
    private static object Get(object target,string name){return T.GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}
    private static void Submit(EvaluationHarness h)
    {T.GetMethod("Submit",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(h,new object[]{new EvaluationCandidate{Id=1,Model="Fixture",Direction=1},new DateTime(2025,9,16,10,0,0),12,2});}
    private static void Assert(bool test,string message){if(!test)throw new Exception(message);}
    public static void Main()
    {
        string root=Path.Combine(Path.GetTempPath(),"eval-orders-"+Guid.NewGuid().ToString("N"));
        NinjaTrader.Core.Globals.UserDataDir=root;
        var h=new EvaluationHarness();h.Init();Submit(h);
        Assert(h.SubmittedContext==0 && h.SubmittedQuantity==2 && h.StopTicks==12 && h.TargetTicks==18,"Managed entry/context/bracket config");
        var entry=new Order{Name="Eval_1",OrderAction=OrderAction.Buy,OrderState=OrderState.PartFilled};
        h.Fill(entry,100,1);Assert((int)Get(h,"filledQuantity")==1 && (double)Get(h,"realized")==-2.5,"First partial fee");
        entry.OrderState=OrderState.Filled;h.Fill(entry,101,1);
        Assert((double)Get(h,"averageEntry")==100.5 && (double)Get(h,"realized")==-5 && !(bool)Get(h,"pendingEntry"),"Second partial accounting");
        var exit=new Order{Name="Profit target",OrderAction=OrderAction.Sell,OrderState=OrderState.Filled};
        h.Fill(exit,102,1);Assert((int)Get(h,"filledQuantity")==1 && (double)Get(h,"realized")==67.5,"Partial exit accounting");
        h.Fill(exit,99,1);Assert((int)Get(h,"filledQuantity")==0 && (double)Get(h,"realized")==-10,"Final round trip accounting");
        string output=((EvaluationResearchWriter)Get(h,"research")).DirectoryPath;h.Finish();
        Assert(File.ReadAllLines(Path.Combine(output,"trades.csv")).Length==2,"One completed trade row for partial fills");
        var bad=new EvaluationHarness();bad.Init();Submit(bad);bad.Rejected(entry);
        Assert((bool)Get(bad,"faulted") && !(bool)Get(bad,"pendingEntry"),"Rejected order must lock new entries");bad.Finish();
        var partial=new EvaluationHarness();partial.Init();Submit(partial);
        entry.OrderState=OrderState.PartFilled;partial.EntryWorking(entry);partial.Fill(entry,100,1);partial.Fill(exit,99,1);
        Assert((bool)Get(partial,"pendingEntry") && partial.CancelRequests==1,"Unfinished entry must be cancelled and stay latched after early exit");
        partial.EntryCancelled(entry);Assert(!(bool)Get(partial,"pendingEntry"),"Cancellation clears pending remainder");partial.Finish();
        Directory.Delete(root,true);
        Console.WriteLine("PASS: managed context/brackets, partial fills/fees, weighted entry, partial exits, rejection lock");
    }
}
