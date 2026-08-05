#region Using declarations
using System;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
#endregion

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk
{
    public sealed class TrailStepSettings
    {
        public int ProfitTriggerTicks { get; set; }
        public int StopLossTicks { get; set; }
        public int FrequencyTicks { get; set; }

        public bool IsEnabled => ProfitTriggerTicks > 0;
    }

    public sealed class TradeManagementSettings
    {
        public double TickSize { get; set; }
        public double PointValue { get; set; }
        public int Quantity { get; set; }
        public double RiskRewardRatio { get; set; }
        public int BreakEvenTriggerTicks { get; set; }
        public int BreakEvenPlusTicks { get; set; }

        public TrailStepSettings Step1 { get; set; }
        public TrailStepSettings Step2 { get; set; }
        public TrailStepSettings Step3 { get; set; }
    }

    public sealed class HypotheticalTrade
    {
        private double activeStopPrice;
        private double targetPrice;
        private double highestPrice;
        private double lowestPrice;
        private double nextTrailReferencePrice;
        private bool breakEvenActivated;
        private int highestTrailStep;

        public HypotheticalTrade(
            EntryCandidate candidate,
            DateTime entryTime,
            double entryPrice,
            TradeManagementSettings settings,
            string policyName,
            bool useBreakEven,
            bool useTrail)
        {
            Candidate = candidate;
            EntryTime = entryTime;
            EntryPrice = entryPrice;
            Settings = settings;
            PolicyName = policyName;
            UseBreakEven = useBreakEven;
            UseTrail = useTrail;

            activeStopPrice = candidate.PlannedStopPrice;
            highestPrice = entryPrice;
            lowestPrice = entryPrice;

            var riskPrice =
                candidate.ActualRiskTicks * settings.TickSize;

            targetPrice = candidate.Direction == TradeDirection.Long
                ? entryPrice + riskPrice * settings.RiskRewardRatio
                : entryPrice - riskPrice * settings.RiskRewardRatio;

            nextTrailReferencePrice = entryPrice;

            Outcome = new ManagementOutcome
            {
                PolicyName = policyName
            };
        }

        public EntryCandidate Candidate { get; private set; }
        public DateTime EntryTime { get; private set; }
        public double EntryPrice { get; private set; }
        public string PolicyName { get; private set; }
        public bool UseBreakEven { get; private set; }
        public bool UseTrail { get; private set; }
        public TradeManagementSettings Settings { get; private set; }
        public ManagementOutcome Outcome { get; private set; }

        public bool IsClosed => Outcome.IsClosed;

        public void ProcessTick(DateTime time, double price)
        {
            if (IsClosed)
                return;

            highestPrice = Math.Max(highestPrice, price);
            lowestPrice = Math.Min(lowestPrice, price);

            UpdateExcursions();
            ApplyBreakEven(price);
            ApplyTrail(price);

            if (Candidate.Direction == TradeDirection.Long)
            {
                if (price <= activeStopPrice)
                {
                    Close(time, activeStopPrice, "Stop");
                    return;
                }

                if (price >= targetPrice)
                {
                    Close(time, targetPrice, "Target");
                }
            }
            else
            {
                if (price >= activeStopPrice)
                {
                    Close(time, activeStopPrice, "Stop");
                    return;
                }

                if (price <= targetPrice)
                {
                    Close(time, targetPrice, "Target");
                }
            }
        }

        public void ForceClose(DateTime time, double price, string reason)
        {
            if (!IsClosed)
                Close(time, price, reason);
        }

        private void ApplyBreakEven(double price)
        {
            if (!UseBreakEven
                || breakEvenActivated
                || Settings.BreakEvenTriggerTicks <= 0)
                return;

            var favorableTicks = Candidate.Direction == TradeDirection.Long
                ? (price - EntryPrice) / Settings.TickSize
                : (EntryPrice - price) / Settings.TickSize;

            if (favorableTicks < Settings.BreakEvenTriggerTicks)
                return;

            var breakEvenStop = Candidate.Direction == TradeDirection.Long
                ? EntryPrice + Settings.BreakEvenPlusTicks * Settings.TickSize
                : EntryPrice - Settings.BreakEvenPlusTicks * Settings.TickSize;

            activeStopPrice = Candidate.Direction == TradeDirection.Long
                ? Math.Max(activeStopPrice, breakEvenStop)
                : Math.Min(activeStopPrice, breakEvenStop);

            breakEvenActivated = true;
            Outcome.BreakEvenActivated = true;
        }

        private void ApplyTrail(double price)
        {
            if (!UseTrail)
                return;

            var favorableTicks = Candidate.Direction == TradeDirection.Long
                ? (price - EntryPrice) / Settings.TickSize
                : (EntryPrice - price) / Settings.TickSize;

            var step = ResolveTrailStep(favorableTicks);
            if (step == null)
                return;

            var stepNumber = step == Settings.Step3 ? 3
                : step == Settings.Step2 ? 2
                : 1;

            highestTrailStep = Math.Max(highestTrailStep, stepNumber);
            Outcome.HighestTrailStepActivated = highestTrailStep;

            double frequencyTicks = Math.Max(1, step.FrequencyTicks);
            var movesBeyondTrigger =
                Math.Max(0, favorableTicks - step.ProfitTriggerTicks);

            var completedMoves =
                Math.Floor(movesBeyondTrigger / frequencyTicks);

            var lockedTicks =
                step.StopLossTicks + completedMoves * frequencyTicks;

            var desiredStop = Candidate.Direction == TradeDirection.Long
                ? EntryPrice + lockedTicks * Settings.TickSize
                : EntryPrice - lockedTicks * Settings.TickSize;

            activeStopPrice = Candidate.Direction == TradeDirection.Long
                ? Math.Max(activeStopPrice, desiredStop)
                : Math.Min(activeStopPrice, desiredStop);
        }

        private TrailStepSettings ResolveTrailStep(double favorableTicks)
        {
            if (Settings.Step3 != null
                && Settings.Step3.IsEnabled
                && favorableTicks >= Settings.Step3.ProfitTriggerTicks)
                return Settings.Step3;

            if (Settings.Step2 != null
                && Settings.Step2.IsEnabled
                && favorableTicks >= Settings.Step2.ProfitTriggerTicks)
                return Settings.Step2;

            if (Settings.Step1 != null
                && Settings.Step1.IsEnabled
                && favorableTicks >= Settings.Step1.ProfitTriggerTicks)
                return Settings.Step1;

            return null;
        }

        private void UpdateExcursions()
        {
            if (Candidate.Direction == TradeDirection.Long)
            {
                Outcome.MfeTicks =
                    Math.Max(Outcome.MfeTicks,
                        (highestPrice - EntryPrice) / Settings.TickSize);

                Outcome.MaeTicks =
                    Math.Max(Outcome.MaeTicks,
                        (EntryPrice - lowestPrice) / Settings.TickSize);
            }
            else
            {
                Outcome.MfeTicks =
                    Math.Max(Outcome.MfeTicks,
                        (EntryPrice - lowestPrice) / Settings.TickSize);

                Outcome.MaeTicks =
                    Math.Max(Outcome.MaeTicks,
                        (highestPrice - EntryPrice) / Settings.TickSize);
            }
        }

        private void Close(DateTime time, double exitPrice, string reason)
        {
            Outcome.IsClosed = true;
            Outcome.ExitTime = time;
            Outcome.ExitPrice = exitPrice;
            Outcome.ExitReason = reason;

            var signedTicks = Candidate.Direction == TradeDirection.Long
                ? (exitPrice - EntryPrice) / Settings.TickSize
                : (EntryPrice - exitPrice) / Settings.TickSize;

            Outcome.RealizedTicks = signedTicks;
            Outcome.RealizedUsd =
                signedTicks
                * Settings.TickSize
                * Settings.PointValue
                * Math.Max(1, Settings.Quantity);
        }
    }
}
