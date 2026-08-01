#region Using declarations
using System;
using System.Collections.Generic;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis
{
    public enum TradeDirection
    {
        Long = 1,
        Short = -1
    }

    public sealed class RangeSessionContext
    {
        public DateTime TradingDate { get; set; }
        public double PremarketHigh { get; set; }
        public double PremarketLow { get; set; }
        public double TickSize { get; set; }
        public double PointValue { get; set; }

        public double RangeTicks
        {
            get
            {
                if (TickSize <= 0)
                    return 0;

                return (PremarketHigh - PremarketLow) / TickSize;
            }
        }
    }

    public sealed class CandleSnapshot
    {
        public DateTime Time { get; set; }
        public int BarIndex { get; set; }
        public double Open { get; set; }
        public double High { get; set; }
        public double Low { get; set; }
        public double Close { get; set; }
        public double Volume { get; set; }

        public double Range
        {
            get { return Math.Max(0, High - Low); }
        }

        public double Body
        {
            get { return Math.Abs(Close - Open); }
        }

        public double UpperWick
        {
            get { return Math.Max(0, High - Math.Max(Open, Close)); }
        }

        public double LowerWick
        {
            get { return Math.Max(0, Math.Min(Open, Close) - Low); }
        }

        public bool IsBullish
        {
            get { return Close > Open; }
        }

        public bool IsBearish
        {
            get { return Close < Open; }
        }
    }

    public sealed class CandleMetrics
    {
        public double RangeTicks { get; set; }
        public double BodyTicks { get; set; }
        public double BodyPercent { get; set; }
        public double UpperWickTicks { get; set; }
        public double LowerWickTicks { get; set; }
        public double CloseLocationPercent { get; set; }
        public double AverageBodyTicks { get; set; }
        public double RelativeBodyMultiple { get; set; }
        public double AverageVolume { get; set; }
        public double RelativeVolumeMultiple { get; set; }
    }

    public sealed class ModelBarContext
    {
        public RangeSessionContext Session { get; set; }
        public CandleSnapshot Bar { get; set; }
        public CandleSnapshot PreviousBar { get; set; }
        public CandleMetrics Metrics { get; set; }
        public IReadOnlyList<CandleSnapshot> History { get; set; }
    }

    public sealed class BreakoutEvent
    {
        public string EventId { get; set; }
        public DateTime TradingDate { get; set; }
        public TradeDirection Direction { get; set; }
        public int AttemptNumber { get; set; }
        public DateTime BreakoutTime { get; set; }
        public int BreakoutBarIndex { get; set; }
        public double RangeLevel { get; set; }
        public double BreakoutClose { get; set; }
        public double DistanceOutsideTicks { get; set; }
        public CandleSnapshot Candle { get; set; }
        public CandleMetrics Metrics { get; set; }

        public double MfeTicks { get; set; }
        public double MaeTicks { get; set; }
        public bool ReturnedInside { get; set; }
        public DateTime ReturnedInsideTime { get; set; }
        public int BarsUntilReturnInside { get; set; }
        public bool Reached10Ticks { get; set; }
        public bool Reached20Ticks { get; set; }
        public bool Reached30Ticks { get; set; }
        public bool Reached40Ticks { get; set; }
        public bool Reached60Ticks { get; set; }
        public bool Reached100Ticks { get; set; }
        public bool IsResolved { get; set; }
    }

    public sealed class EntryCandidate
    {
        public string CandidateId { get; set; }
        public string BreakoutEventId { get; set; }
        public string ModelName { get; set; }
        public TradeDirection Direction { get; set; }
        public DateTime SignalTime { get; set; }
        public int SignalBarIndex { get; set; }
        public double RangeLevel { get; set; }
        public CandleSnapshot ConfirmationCandle { get; set; }
        public CandleMetrics Metrics { get; set; }

        public int BarsAfterBreakout { get; set; }
        public double RetestInsideDepthTicks { get; set; }
        public double RetestOutsideDistanceTicks { get; set; }

        public bool StrongCandleQualified { get; set; }
        public string QualificationReason { get; set; }

        public double StructuralStopPrice { get; set; }
        public double PlannedEntryPrice { get; set; }
        public double PlannedStopPrice { get; set; }
        public double StructuralRiskTicks { get; set; }
        public double ActualRiskTicks { get; set; }
        public bool StopWasCapped { get; set; }
    }

    public sealed class ManagementOutcome
    {
        public string PolicyName { get; set; }
        public bool IsClosed { get; set; }
        public DateTime ExitTime { get; set; }
        public double ExitPrice { get; set; }
        public string ExitReason { get; set; }
        public double RealizedTicks { get; set; }
        public double RealizedUsd { get; set; }
        public double MfeTicks { get; set; }
        public double MaeTicks { get; set; }
        public bool BreakEvenActivated { get; set; }
        public int HighestTrailStepActivated { get; set; }
    }
}
