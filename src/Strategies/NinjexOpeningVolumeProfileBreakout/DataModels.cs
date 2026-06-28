using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private sealed class SetupCandidate
        {
            public string Direction;
            public string Decision;

            public DateTime SignalDateEt;
            public DateTime SignalTimeChart;
            public DateTime SignalTimeEt;

            public double VAH;
            public double VAL;
            public double POC;

            public double Open;
            public double High;
            public double Low;
            public double Close;
            public double BodyHigh;
            public double BodyLow;

            public double EntryPrice;
            public double BreakoutLevel;
            public double EntryDistanceTicks;
            public double EntryDistancePoints;

            public double StopPrice;
            public double TargetPrice;

            public int Quantity;
        }
        
        private sealed class PendingActualTradePlan
        {
            public string Direction;
            public string SignalName;

            public DateTime SignalTimeChart;
            public DateTime SignalTimeEt;

            public double SignalEntryPrice;

            public int StopTicks;
            public int TargetTicks;

            public double VAH;
            public double VAL;
            public double POC;

            public double SignalEntryDistanceTicks;
            public double SignalEntryDistancePoints;
        }

        private sealed class TrackedResearchSetup
        {
            public SetupCandidate Candidate;

            public int BarsTracked;
            public double MfeUsd;
            public double MaeUsd;
        }

        private sealed class ActualTradeState
        {
            public string Direction;
            public string EntrySignal;

            public DateTime EntryTime;
            public double EntryPrice;
            public int EntryQuantity;

            public double StopPrice;
            public double TargetPrice;

            public double VAH;
            public double VAL;
            public double POC;

            public double EntryDistanceTicks;
            public double EntryDistancePoints;
        }
    }
}