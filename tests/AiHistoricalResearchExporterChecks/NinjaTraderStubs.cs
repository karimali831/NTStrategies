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
public sealed class FakeBar {
public DateTime Time; public double Open,High,Low,Close; public long Volume;
public FakeBar(DateTime time,double open,double high,double low,double close,long volume)
{Time=time;Open=open;High=high;Low=low;Close=close;Volume=volume;}
}
public sealed class FakeBars {
public FakeBar[] Data = Array.Empty<FakeBar>();
public int Count => Data.Length;
public DateTime GetTime(int i) => Data[i].Time;
public double GetOpen(int i) => Data[i].Open;
public double GetHigh(int i) => Data[i].High;
public double GetLow(int i) => Data[i].Low;
public double GetClose(int i) => Data[i].Close;
public long GetVolume(int i) => Data[i].Volume;
}
public class FakeSeries<T> { public T this[int i]=>default; }
public class Strategy {
protected string Name,Description;
protected NinjaTrader.NinjaScript.Calculate Calculate;
protected bool IsOverlay,IsExitOnSessionCloseStrategy,IsInstantiatedOnEachOptimizationIteration;
protected int BarsRequiredToTrade,BarsInProgress;
protected int[] CurrentBars=new int[4];
protected NinjaTrader.Data.BarsPeriod BarsPeriod;
protected FakeInstrument Instrument;
protected FakeBars[] BarsArray=new FakeBars[4];
public void SetBarsForTests(FakeBars[] bars) { BarsArray=bars; }
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