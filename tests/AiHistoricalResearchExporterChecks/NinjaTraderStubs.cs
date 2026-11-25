using System;
using System.Text.Json;
namespace System.Web.Script.Serialization {
public sealed class JavaScriptSerializer { public string Serialize(object x)=>JsonSerializer.Serialize(x); }
}
namespace NinjaTrader.Data {
public enum BarsPeriodType { Minute }
public sealed class BarsPeriod { public BarsPeriodType BarsPeriodType; public int Value; }
}
namespace NinjaTrader.NinjaScript {
public enum State { SetDefaults, Configure, DataLoaded, Historical, Realtime, Terminated }
public enum Calculate { OnBarClose }
[AttributeUsage(AttributeTargets.Property)] public sealed class NinjaScriptPropertyAttribute:Attribute{}
}
namespace NinjaTrader.NinjaScript.Strategies {
public sealed class FakeInstrument { public string FullName; }
public class FakeSeries<T> { public T this[int i]=>default; }
public class Strategy {
protected string Name,Description;
protected NinjaTrader.NinjaScript.Calculate Calculate;
protected bool IsOverlay,IsExitOnSessionCloseStrategy,IsInstantiatedOnEachOptimizationIteration;
protected int BarsRequiredToTrade,BarsInProgress;
protected int[] CurrentBars=new int[4];
protected NinjaTrader.Data.BarsPeriod BarsPeriod;
protected FakeInstrument Instrument;
protected NinjaTrader.NinjaScript.State State;
protected FakeSeries<DateTime>[] Times=new FakeSeries<DateTime>[4];
protected FakeSeries<double>[] Opens=new FakeSeries<double>[4];
protected FakeSeries<double>[] Highs=new FakeSeries<double>[4];
protected FakeSeries<double>[] Lows=new FakeSeries<double>[4];
protected FakeSeries<double>[] Closes=new FakeSeries<double>[4];
protected FakeSeries<double>[] Volumes=new FakeSeries<double>[4];
protected virtual void OnStateChange(){}
protected virtual void OnBarUpdate(){}
protected void Print(string s){}
protected void AddDataSeries(NinjaTrader.Data.BarsPeriodType type,int mins){}
}
}