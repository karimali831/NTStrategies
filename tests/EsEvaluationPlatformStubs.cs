// TEST DOUBLE ONLY. Not a NinjaTrader implementation or a substitute for NT compilation/replay.
using System;
namespace System.ComponentModel.DataAnnotations
{
    public sealed class DisplayAttribute:Attribute { public string Name{get;set;} public string GroupName{get;set;} public int Order{get;set;} }
    public sealed class RangeAttribute:Attribute { public RangeAttribute(double a,double b){} }
}
namespace NinjaTrader.Cbi
{
    public enum MarketPosition{Flat,Long,Short}
    public enum OrderState{Working,PartFilled,Filled,Cancelled,Rejected}
    public enum OrderAction{Buy,SellShort,Sell,BuyToCover}
    public enum ErrorCode{NoError,OrderRejected}
    public enum EntryHandling{AllEntries}
    public enum StopTargetHandling{PerEntryExecution}
    public enum StartBehavior{WaitUntilFlat}
    public enum RealtimeErrorHandling{StopCancelClose}
    public sealed class Order { public string Name,OrderId; public OrderAction OrderAction; public OrderState OrderState; }
    public sealed class Execution { public Order Order; }
    public sealed class Account { public string Name="Playback101"; }
    public sealed class Position { public MarketPosition MarketPosition; }
    public sealed class MasterInstrument { public string Name="ES"; public double PointValue=50; }
    public sealed class Instrument { public MasterInstrument MasterInstrument=new MasterInstrument(); public string FullName="ES 12-25"; }
}
namespace NinjaTrader.Data
{
    public enum BarsPeriodType{Tick,Minute}
    public sealed class BarsPeriod { public BarsPeriodType BarsPeriodType=BarsPeriodType.Minute; public int Value=1; }
    public sealed class TradingHours { public string Name="CME US Index Futures ETH"; }
    public sealed class Bars { public TradingHours TradingHours=new TradingHours(); }
}
namespace NinjaTrader.Core
{
    public static class Globals
    {
        public static string UserDataDir=System.IO.Path.GetTempPath();
        public static Options GeneralOptions=new Options();
    }
    public sealed class Options { public TimeZoneInfo TimeZoneInfo=TimeZoneInfo.CreateCustomTimeZone("Eastern Standard Time",TimeSpan.Zero,"Eastern","Eastern"); }
}
namespace NinjaTrader.NinjaScript
{
    public sealed class NinjaScriptPropertyAttribute:Attribute{}
    public enum State{SetDefaults,Configure,DataLoaded,Historical,Realtime,Terminated}
    public enum Calculate{OnEachTick}
    public enum CalculationMode{Ticks}
}
namespace NinjaTrader.NinjaScript.Strategies
{
    using NinjaTrader.Cbi; using NinjaTrader.Data;
    public class Strategy
    {
        public string Name,Description;
        public State State;
        public Calculate Calculate;
        public int EntriesPerDirection,ExitOnSessionCloseSeconds,BarsRequiredToTrade;
        public bool IsExitOnSessionCloseStrategy,IsInstantiatedOnEachOptimizationIteration;
        public EntryHandling EntryHandling; public StopTargetHandling StopTargetHandling;
        public StartBehavior StartBehavior; public RealtimeErrorHandling RealtimeErrorHandling;
        public double TickSize=.25; public Instrument Instrument=new Instrument();
        public BarsPeriod BarsPeriod=new BarsPeriod(); public Bars Bars=new Bars();
        public Bars[] BarsArray={new Bars(),new Bars()}; public Account Account=new Account(); public Position Position=new Position();
        public int BarsInProgress; public int[] CurrentBars={0,0};
        public DateTime[][] Times={new DateTime[1],new DateTime[1]};
        public double[][] Closes={new double[1],new double[1]},Volumes={new double[1],new double[1]};
        public int SubmittedContext,SubmittedQuantity,StopTicks,TargetTicks,ExitRequests,CancelRequests;
        protected virtual void OnStateChange(){}
        protected virtual void OnBarUpdate(){}
        protected virtual void OnOrderUpdate(Order o,double l,double s,int q,int f,double a,OrderState st,DateTime t,ErrorCode e,string c){}
        protected virtual void OnExecutionUpdate(Execution e,string id,double p,int q,MarketPosition mp,string oid,DateTime t){}
        protected void AddDataSeries(BarsPeriodType t,int v){}
        protected void Print(string s){}
        protected void SetStopLoss(string n,CalculationMode m,int ticks,bool simulated){StopTicks=ticks;}
        protected void SetProfitTarget(string n,CalculationMode m,int ticks){TargetTicks=ticks;}
        protected void CancelOrder(Order order){CancelRequests++;}
        protected void EnterLong(int context,int quantity,string name){SubmittedContext=context;SubmittedQuantity=quantity;}
        protected void EnterShort(int context,int quantity,string name){SubmittedContext=context;SubmittedQuantity=quantity;}
        protected void ExitLong(int c,int q,string reason,string signal){ExitRequests++;}
        protected void ExitShort(int c,int q,string reason,string signal){ExitRequests++;}
    }
}
