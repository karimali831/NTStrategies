#region Using declarations
using System;
using System.Collections.Generic;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;

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
        public string Instrument { get; set; }
        public string Contract { get; set; }
        public double PremarketHigh { get; set; }
        public double PremarketLow { get; set; }
        public DateTime HighFormationTime { get; set; }
        public DateTime LowFormationTime { get; set; }
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

    public sealed class SessionDataQuality
    {
        public DateTime TradingDate { get; set; }
        public int FiveMinuteRangeBarCount { get; set; }
        public int OneMinuteEntryWindowBarCount { get; set; }
        public long TickCount { get; set; }
        public DateTime FirstFiveMinuteBarTime { get; set; }
        public DateTime LastFiveMinuteBarTime { get; set; }
        public DateTime FirstOneMinuteBarTime { get; set; }
        public DateTime LastOneMinuteBarTime { get; set; }
        public DateTime FirstTickTime { get; set; }
        public DateTime LastTickTime { get; set; }
        public bool HasFiveMinuteData { get; set; }
        public bool HasOneMinuteData { get; set; }
        public bool HasTickData { get; set; }
        public bool IsFinalized { get; set; }
        public string Status { get; set; }
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

        public double Range => Math.Max(0, High - Low);
        public double Body => Math.Abs(Close - Open);
        public double UpperWick => Math.Max(0, High - Math.Max(Open, Close));
        public double LowerWick => Math.Max(0, Math.Min(Open, Close) - Low);
        public bool IsBullish => Close > Open;
        public bool IsBearish => Close < Open;
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
        public string Contract { get; set; }
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
        public double MfeBeforeReturnTicks { get; set; }
        public bool IsFakeout20Ticks { get; set; }

        public bool Reached10Ticks { get; set; }
        public bool Reached20Ticks { get; set; }
        public bool Reached30Ticks { get; set; }
        public bool Reached40Ticks { get; set; }
        public bool Reached60Ticks { get; set; }
        public bool Reached100Ticks { get; set; }
        public DateTime TimeTo10Ticks { get; set; }
        public DateTime TimeTo20Ticks { get; set; }
        public DateTime TimeTo30Ticks { get; set; }
        public DateTime TimeTo40Ticks { get; set; }
        public DateTime TimeTo60Ticks { get; set; }
        public DateTime TimeTo100Ticks { get; set; }

        public double Mfe1Minute { get; set; }
        public double Mae1Minute { get; set; }
        public double Mfe2Minutes { get; set; }
        public double Mae2Minutes { get; set; }
        public double Mfe3Minutes { get; set; }
        public double Mae3Minutes { get; set; }
        public double Mfe5Minutes { get; set; }
        public double Mae5Minutes { get; set; }
        public double Mfe10Minutes { get; set; }
        public double Mae10Minutes { get; set; }
        public double Mfe15Minutes { get; set; }
        public double Mae15Minutes { get; set; }
        public double Mfe30Minutes { get; set; }
        public double Mae30Minutes { get; set; }
        public double Mfe60Minutes { get; set; }
        public double Mae60Minutes { get; set; }

        public bool IsResolved { get; set; }
        public DateTime ResolutionTime { get; set; }
        public string ResolutionReason { get; set; }
        public bool RawRetestObserved { get; set; }

        public DateTime FirstRawRetestTime { get; set; }

        public int FirstRawRetestBarsAfterBreakout { get; set; }

        public double FirstRawRetestMinutesAfterBreakout { get; set; }

        public double RawRetestMaximumInsideDepthTicks { get; set; }
        public bool RawRetestWithinDepthTolerance { get; set; }

        public double RawRetestMinimumOutsideDistanceTicks { get; set; }

        public bool RawRetestTouchedExactLevel { get; set; }

        public bool RawRetestWasWithinModelBarWindow { get; set; }

        public bool RawRetestConfirmed { get; set; }

        public DateTime RawRetestConfirmationTime { get; set; }

        public double MfeBeforeRawRetestTicks { get; set; }

        public double MfeAfterRawRetestTicks { get; set; }

        public string RawRetestStatus { get; set; }
        public bool RawRetestArmed { get; set; }
        public int RawRetestArmedBarIndex { get; set; }
        public double FurthestExcursionBeforeRawRetestTicks { get; set; }
        public int FirstRawRetestBarIndex { get; set; }
        public double FirstRawRetestReferencePrice { get; set; }
        public double FirstRawRetestInsideDepthTicks { get; set; }

        public double FirstRawRetestOutsideDistanceTicks { get; set; }

        public bool FirstRawRetestWithinDepthTolerance { get; set; }
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
        public string ModelVersion { get; set; }

        public string QualificationCode { get; set; }

        public CandidateFeatureSnapshot Features { get; set; }
        public int BarsAfterBreakout { get; set; }
        public double RetestInsideDepthTicks { get; set; }
        public double RetestOutsideDistanceTicks { get; set; }

        public bool StrongCandleQualified { get; set; }
        public bool DirectionPassed { get; set; }
        public bool BodyPassed { get; set; }
        public bool CloseLocationPassed { get; set; }
        public bool RelativeBodyPassed { get; set; }
        public string QualificationReason { get; set; }
        public string FinalStatus { get; set; }

        public double StructuralStopPrice { get; set; }
        public double PlannedEntryPrice { get; set; }
        public DateTime PlannedEntryTime { get; set; }
        public double PlannedStopPrice { get; set; }
        public double StructuralRiskTicks { get; set; }
        public double ActualRiskTicks { get; set; }
        public bool StopWasCapped { get; set; }
        public double EntryDistanceTicks { get; set; }
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
