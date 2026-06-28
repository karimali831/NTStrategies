using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private void TrySubmitBarCloseEntry()
        {
            if (tradesToday >= MaxTradesPerDay)
                return;

            var longTrigger = GetLongTrigger();
            var shortTrigger = GetShortTrigger();

            var bodyHigh = Math.Max(Open[0], Close[0]);
            var bodyLow = Math.Min(Open[0], Close[0]);

            var previousCloseInsideRange = CurrentBar > 0 && IsInsideProfileRange(Close[1]);
            var currentOpenedInsideRange = IsInsideProfileRange(Open[0]);

            var originatedInsideRange = previousCloseInsideRange || currentOpenedInsideRange;

            var rawLongBodyBreak =
                originatedInsideRange &&
                bodyLow <= longTrigger &&
                bodyHigh >= longTrigger &&
                Close[0] > longTrigger;

            var rawShortBodyBreak =
                originatedInsideRange &&
                bodyHigh >= shortTrigger &&
                bodyLow <= shortTrigger &&
                Close[0] < shortTrigger;

            var longBodyBreak = EnableLongs && rawLongBodyBreak;
            var shortBodyBreak = EnableShorts && rawShortBodyBreak;

            DebugPrint(
                "Bar close check"
                + " Time=" + Time[0]
                + " Open=" + Open[0]
                + " Close=" + Close[0]
                + " VAH=" + activeVAH
                + " VAL=" + activeVAL
                + " POC=" + activePOC
                + " LongEnabled=" + EnableLongs
                + " ShortEnabled=" + EnableShorts
                + " LongArmed=" + longBreakoutArmed
                + " ShortArmed=" + shortBreakoutArmed
                + " RawLongBreak=" + rawLongBodyBreak
                + " RawShortBreak=" + rawShortBodyBreak
                + " OriginatedInside=" + originatedInsideRange);

            if (rawLongBodyBreak && !EnableLongs)
            {
                LogRejectedSetup("LONG", "LongsDisabled", bodyHigh, bodyLow);
                return;
            }

            if (rawShortBodyBreak && !EnableShorts)
            {
                LogRejectedSetup("SHORT", "ShortsDisabled", bodyHigh, bodyLow);
                return;
            }

            if (longBodyBreak)
            {
                if (!longBreakoutArmed)
                {
                    DebugPrint("Long blocked: minimum retracement inside range not satisfied.");
                    LogRejectedSetup("LONG", "NotArmed", bodyHigh, bodyLow);
                    return;
                }

                if (!IsEntryDistanceAllowed("LONG", Close[0]))
                {
                    double distanceTicks = GetEntryDistanceTicks("LONG", Close[0]);

                    DebugPrint("Long blocked: entry too far from VAH. DistanceTicks=" + distanceTicks);
                    LogRejectedSetup("LONG", "TooFarFromBreakoutLevel", bodyHigh, bodyLow);
                    return;
                }

                SubmitManagedLong();
                return;
            }

            if (shortBodyBreak)
            {
                if (!shortBreakoutArmed)
                {
                    DebugPrint("Short blocked: minimum retracement inside range not satisfied.");
                    LogRejectedSetup("SHORT", "NotArmed", bodyHigh, bodyLow);
                    return;
                }

                if (!IsEntryDistanceAllowed("SHORT", Close[0]))
                {
                    var distanceTicks = GetEntryDistanceTicks("SHORT", Close[0]);

                    DebugPrint("Short blocked: entry too far from VAL. DistanceTicks=" + distanceTicks);
                    LogRejectedSetup("SHORT", "TooFarFromBreakoutLevel", bodyHigh, bodyLow);
                    return;
                }

                SubmitManagedShort();
            }
        }

        private void SubmitManagedLong()
        {
            var expectedEntry = Close[0];

            var stopDistance = CurrencyToPriceDistance(StopLossUsd, Quantity);
            var targetDistance = CurrencyToPriceDistance(ProfitTargetUsd, Quantity);

            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(expectedEntry - stopDistance);
            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(expectedEntry + targetDistance);

            if (stopPrice >= expectedEntry || targetPrice <= expectedEntry)
            {
                DebugPrint("Long skipped: invalid bracket. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
                return;
            }

            SetStopLoss(LongSignal, CalculationMode.Price, stopPrice, false);
            SetProfitTarget(LongSignal, CalculationMode.Price, targetPrice);

            StartPendingSetup("LONG", "Submitted", expectedEntry, stopPrice, targetPrice);

            longBreakoutArmed = false;
            shortBreakoutArmed = false;
            breakEvenMoved = false;

            EnterLong(Quantity, LongSignal);
            tradesToday++;

            DebugPrint("Long submitted. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
        }

        private void SubmitManagedShort()
        {
            var expectedEntry = Close[0];

            var stopDistance = CurrencyToPriceDistance(StopLossUsd, Quantity);
            var targetDistance = CurrencyToPriceDistance(ProfitTargetUsd, Quantity);

            var stopPrice = Instrument.MasterInstrument.RoundToTickSize(expectedEntry + stopDistance);
            var targetPrice = Instrument.MasterInstrument.RoundToTickSize(expectedEntry - targetDistance);

            if (stopPrice <= expectedEntry || targetPrice >= expectedEntry)
            {
                DebugPrint("Short skipped: invalid bracket. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
                return;
            }

            SetStopLoss(ShortSignal, CalculationMode.Price, stopPrice, false);
            SetProfitTarget(ShortSignal, CalculationMode.Price, targetPrice);

            StartPendingSetup("SHORT", "Submitted", expectedEntry, stopPrice, targetPrice);

            longBreakoutArmed = false;
            shortBreakoutArmed = false;
            breakEvenMoved = false;

            EnterShort(Quantity, ShortSignal);
            tradesToday++;

            DebugPrint("Short submitted. Entry=" + expectedEntry + " Stop=" + stopPrice + " Target=" + targetPrice);
        }
        
        private double GetEntryDistanceTicks(string direction, double entryPrice)
        {
            if (direction == "LONG")
                return Math.Abs(entryPrice - GetLongTrigger()) / TickSize;

            if (direction == "SHORT")
                return Math.Abs(entryPrice - GetShortTrigger()) / TickSize;

            return double.NaN;
        }

        private bool IsEntryDistanceAllowed(string direction, double entryPrice)
        {
            if (MaxDistanceTicksFromBreakoutLevel <= 0)
                return true;

            var distanceTicks = GetEntryDistanceTicks(direction, entryPrice);

            return !double.IsNaN(distanceTicks)
                   && distanceTicks <= MaxDistanceTicksFromBreakoutLevel;
        }
    }
}