// Build-only NinjaTrader and .NET Framework shims.
// They are NOT part of the NTStrategies production project.
using System;
using System.Collections.Generic;
using System.Text.Json;
namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        public int MaxJsonLength { get; set; }
        public string Serialize(object value) => JsonSerializer.Serialize(value);
        public T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value);
    }
}
namespace NinjaTrader.Data
{
    public enum BarsPeriodType { Minute }
    public sealed class BarsPeriod { public BarsPeriodType BarsPeriodType; public int Value; }
}
namespace NinjaTrader.NinjaScript
{
    public enum State { SetDefaults, Configure, DataLoaded, Realtime, Terminated }
    public enum Calculate { OnBarClose }
    [AttributeUsage(AttributeTargets.Property)] public sealed class NinjaScriptPropertyAttribute : Attribute { }
}
namespace NinjaTrader.NinjaScript.Strategies
{
    public sealed class FakeInstrument { public string FullName; }
    public class FakeSeries<T>
    {
        public T this[int value] => default;
    }
    public class Strategy
    {
        protected string Name, Description;
        protected NinjaTrader.NinjaScript.Calculate Calculate;
        protected bool IsOverlay, IsExitOnSessionCloseStrategy, IsInstantiatedOnEachOptimizationIteration;
        protected int BarsRequiredToTrade, BarsInProgress;
        protected int[] CurrentBars = new int[3];
        protected NinjaTrader.Data.BarsPeriod BarsPeriod;
        protected FakeInstrument Instrument;
        protected NinjaTrader.NinjaScript.State State;
        protected FakeSeries<DateTime>[] Times = Array.Empty<FakeSeries<DateTime>>();
        protected FakeSeries<double>[] Opens = Array.Empty<FakeSeries<double>>();
        protected FakeSeries<double>[] Highs = Array.Empty<FakeSeries<double>>();
        protected FakeSeries<double>[] Lows = Array.Empty<FakeSeries<double>>();
        protected FakeSeries<double>[] Closes = Array.Empty<FakeSeries<double>>();
        protected FakeSeries<double>[] Volumes = Array.Empty<FakeSeries<double>>();
        protected virtual void OnStateChange() { }
        protected virtual void OnBarUpdate() { }
        protected void Print(string text) { }
        protected void AddDataSeries(NinjaTrader.Data.BarsPeriodType type, int value) { }
    }
}
