using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private PendingActualTradePlan pendingActualTradePlan;
        
        private void TrySubmitBarCloseEntry()
        {
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
                    var distanceTicks = GetEntryDistanceTicks("LONG", Close[0]);

                    DebugPrint("Long blocked: entry too far from VAH. DistanceTicks=" + distanceTicks);
                    LogRejectedSetup("LONG", "TooFarFromBreakoutLevel", bodyHigh, bodyLow);
                    return;
                }

                var candidate = BuildSetupCandidate("LONG", "Eligible", bodyHigh, bodyLow);
                StartResearchSetup(candidate);

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    LogRejectedSetup("LONG", "ActualTradeSkipped_PositionNotFlat", bodyHigh, bodyLow);
                    return;
                }

                if (tradesToday >= MaxTradesPerDay)
                {
                    LogRejectedSetup("LONG", "ActualTradeSkipped_MaxTradesReached", bodyHigh, bodyLow);
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

                var candidate = BuildSetupCandidate("SHORT", "Eligible", bodyHigh, bodyLow);
                StartResearchSetup(candidate);

                if (Position.MarketPosition != MarketPosition.Flat)
                {
                    LogRejectedSetup("SHORT", "ActualTradeSkipped_PositionNotFlat", bodyHigh, bodyLow);
                    return;
                }

                if (tradesToday >= MaxTradesPerDay)
                {
                    LogRejectedSetup("SHORT", "ActualTradeSkipped_MaxTradesReached", bodyHigh, bodyLow);
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
            
            longBreakoutArmed = false;
            shortBreakoutArmed = false;
            breakEvenMoved = false;
            
            PreparePendingActualTradePlan(
                direction: "LONG",
                signalName: LongSignal,
                expectedEntry: expectedEntry,
                stopPrice: stopPrice,
                targetPrice: targetPrice);

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
            
            longBreakoutArmed = false;
            shortBreakoutArmed = false;
            breakEvenMoved = false;
            
            PreparePendingActualTradePlan(
                direction: "SHORT",
                signalName: ShortSignal,
                expectedEntry: expectedEntry,
                stopPrice: stopPrice,
                targetPrice: targetPrice);

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
        
        private SetupCandidate BuildSetupCandidate(string direction, string decision, double bodyHigh, double bodyLow)
        {
            var easternNow = ConvertChartTimeToEastern
                ? ConvertTime(Time[0], sourceTimeZone, easternTimeZone)
                : Time[0];

            var entryPrice = Close[0];
            var stopDistance = CurrencyToPriceDistance(StopLossUsd, Quantity);
            var targetDistance = CurrencyToPriceDistance(ProfitTargetUsd, Quantity);

            var stopPrice = direction == "LONG"
                ? Instrument.MasterInstrument.RoundToTickSize(entryPrice - stopDistance)
                : Instrument.MasterInstrument.RoundToTickSize(entryPrice + stopDistance);

            var targetPrice = direction == "LONG"
                ? Instrument.MasterInstrument.RoundToTickSize(entryPrice + targetDistance)
                : Instrument.MasterInstrument.RoundToTickSize(entryPrice - targetDistance);

            var breakoutLevel = direction == "LONG" ? GetLongTrigger() : GetShortTrigger();
            var entryDistanceTicks = GetEntryDistanceTicks(direction, entryPrice);

            return new SetupCandidate
            {
                Direction = direction,
                Decision = decision,

                SignalDateEt = easternNow.Date,
                SignalTimeChart = Time[0],
                SignalTimeEt = easternNow,

                VAH = activeVAH,
                VAL = activeVAL,
                POC = activePOC,

                Open = Open[0],
                High = High[0],
                Low = Low[0],
                Close = Close[0],
                BodyHigh = bodyHigh,
                BodyLow = bodyLow,

                EntryPrice = entryPrice,
                BreakoutLevel = breakoutLevel,
                EntryDistanceTicks = entryDistanceTicks,
                EntryDistancePoints = entryDistanceTicks * TickSize,

                StopPrice = stopPrice,
                TargetPrice = targetPrice,

                Quantity = Quantity
            };
        }
    }
}