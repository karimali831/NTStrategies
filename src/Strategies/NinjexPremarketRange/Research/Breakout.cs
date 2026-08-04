using System;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch : Strategy
    {
        private void RegisterBreakout(BreakoutEvent breakout)
        {
            breakoutEvents.Add(breakout);
            ExportBreakoutAudit("Created", breakout);
            FlushExportWriters();
            Diagnostic(breakout.BreakoutTime,
                "BREAKOUT {0} {1} Level={2} Close={3} Distance={4:0.0} ticks",
                breakout.EventId,
                breakout.Direction,
                breakout.RangeLevel,
                breakout.BreakoutClose,
                breakout.DistanceOutsideTicks);

            foreach (var model in entryModels)
                model.OnBreakout(breakout);
        }
        
        private void UpdateBreakoutExcursions(DateTime time, double price)
        {
            foreach (var breakout in breakoutEvents)
            {
                if (breakout.IsResolved)
                    continue;

                var favorable = breakout.Direction == TradeDirection.Long
                    ? (price - breakout.RangeLevel) / TickSize
                    : (breakout.RangeLevel - price) / TickSize;
                var adverse = breakout.Direction == TradeDirection.Long
                    ? (breakout.RangeLevel - price) / TickSize
                    : (price - breakout.RangeLevel) / TickSize;

                UpdateBreakoutPath(breakout, time, favorable, adverse);
            }
        }

        private void UpdateBreakoutExcursionsFromBar(CandleSnapshot bar)
        {
            if (EnablePrecisionTickAnalysis)
                return;

            foreach (var breakout in breakoutEvents)
            {
                if (breakout.IsResolved)
                    continue;

                var favorable = breakout.Direction == TradeDirection.Long
                    ? (bar.High - breakout.RangeLevel) / TickSize
                    : (breakout.RangeLevel - bar.Low) / TickSize;
                var adverse = breakout.Direction == TradeDirection.Long
                    ? (breakout.RangeLevel - bar.Low) / TickSize
                    : (bar.High - breakout.RangeLevel) / TickSize;

                UpdateBreakoutPath(breakout, bar.Time, favorable, adverse);
            }
        }

        private void UpdateBreakoutPath(
            BreakoutEvent breakout,
            DateTime time,
            double favorable,
            double adverse)
        {
            favorable = Math.Max(0, favorable);
            adverse = Math.Max(0, adverse);

            breakout.MfeTicks =
                Math.Max(
                    breakout.MfeTicks,
                    favorable);

            breakout.MaeTicks =
                Math.Max(
                    breakout.MaeTicks,
                    adverse);

            UpdateMilestones(
                breakout,
                time,
                favorable);

            var minutes =
                (time - breakout.BreakoutTime).TotalMinutes;

            UpdateHorizon(
                minutes,
                1,
                favorable,
                adverse,
                breakout.Mfe1Minute,
                breakout.Mae1Minute,
                out var mfe1,
                out var mae1);

            breakout.Mfe1Minute = mfe1;
            breakout.Mae1Minute = mae1;

            UpdateHorizon(
                minutes,
                2,
                favorable,
                adverse,
                breakout.Mfe2Minutes,
                breakout.Mae2Minutes,
                out var mfe2,
                out var mae2);

            breakout.Mfe2Minutes = mfe2;
            breakout.Mae2Minutes = mae2;

            UpdateHorizon(
                minutes,
                3,
                favorable,
                adverse,
                breakout.Mfe3Minutes,
                breakout.Mae3Minutes,
                out double mfe3,
                out double mae3);

            breakout.Mfe3Minutes = mfe3;
            breakout.Mae3Minutes = mae3;

            UpdateHorizon(
                minutes,
                5,
                favorable,
                adverse,
                breakout.Mfe5Minutes,
                breakout.Mae5Minutes,
                out var mfe5,
                out var mae5);

            breakout.Mfe5Minutes = mfe5;
            breakout.Mae5Minutes = mae5;

            UpdateHorizon(
                minutes,
                10,
                favorable,
                adverse,
                breakout.Mfe10Minutes,
                breakout.Mae10Minutes,
                out var mfe10,
                out var mae10);

            breakout.Mfe10Minutes = mfe10;
            breakout.Mae10Minutes = mae10;

            UpdateHorizon(
                minutes,
                15,
                favorable,
                adverse,
                breakout.Mfe15Minutes,
                breakout.Mae15Minutes,
                out var mfe15,
                out var mae15);

            breakout.Mfe15Minutes = mfe15;
            breakout.Mae15Minutes = mae15;

            UpdateHorizon(
                minutes,
                30,
                favorable,
                adverse,
                breakout.Mfe30Minutes,
                breakout.Mae30Minutes,
                out var mfe30,
                out var mae30);

            breakout.Mfe30Minutes = mfe30;
            breakout.Mae30Minutes = mae30;

            UpdateHorizon(
                minutes,
                60,
                favorable,
                adverse,
                breakout.Mfe60Minutes,
                breakout.Mae60Minutes,
                out var mfe60,
                out var mae60);

            breakout.Mfe60Minutes = mfe60;
            breakout.Mae60Minutes = mae60;
        }

        private static void UpdateHorizon(
            double elapsedMinutes,
            int horizon,
            double favorable,
            double adverse,
            double currentMfe,
            double currentMae,
            out double updatedMfe,
            out double updatedMae)
        {
            updatedMfe = currentMfe;
            updatedMae = currentMae;

            if (elapsedMinutes < 0
                || elapsedMinutes > horizon)
            {
                return;
            }

            updatedMfe = Math.Max(currentMfe, favorable);
            updatedMae = Math.Max(currentMae, adverse);
        }
        
        private static void UpdateMilestones(
            BreakoutEvent breakout,
            DateTime time,
            double favorableTicks)
        {
            if (!breakout.Reached10Ticks
                && favorableTicks >= 10)
            {
                breakout.Reached10Ticks = true;
                breakout.TimeTo10Ticks = time;
            }

            if (!breakout.Reached20Ticks
                && favorableTicks >= 20)
            {
                breakout.Reached20Ticks = true;
                breakout.TimeTo20Ticks = time;
            }

            if (!breakout.Reached30Ticks
                && favorableTicks >= 30)
            {
                breakout.Reached30Ticks = true;
                breakout.TimeTo30Ticks = time;
            }

            if (!breakout.Reached40Ticks
                && favorableTicks >= 40)
            {
                breakout.Reached40Ticks = true;
                breakout.TimeTo40Ticks = time;
            }

            if (!breakout.Reached60Ticks
                && favorableTicks >= 60)
            {
                breakout.Reached60Ticks = true;
                breakout.TimeTo60Ticks = time;
            }

            if (!breakout.Reached100Ticks
                && favorableTicks >= 100)
            {
                breakout.Reached100Ticks = true;
                breakout.TimeTo100Ticks = time;
            }
        }
        
        private void UpdateBreakoutReturnInside(CandleSnapshot bar)
        {
            foreach (var breakout in breakoutEvents.ToList())
            {
                if (breakout.IsResolved)
                    continue;

                var returned = breakout.Direction == TradeDirection.Long
                    ? bar.Close <= breakout.RangeLevel
                    : bar.Close >= breakout.RangeLevel;

                if (!returned)
                    continue;

                breakout.ReturnedInside = true;
                breakout.ReturnedInsideTime = bar.Time;
                breakout.BarsUntilReturnInside = bar.BarIndex - breakout.BreakoutBarIndex;
                breakout.MfeBeforeReturnTicks = breakout.MfeTicks;
                breakout.IsFakeout20Ticks = breakout.MfeBeforeReturnTicks < 20;
                ResolveBreakout(breakout, bar.Time, "ReturnedInside");
            }
        }

        private void FinalizeOpenBreakoutEvents(DateTime time, string reason)
        {
            foreach (var breakout in breakoutEvents.ToList())
            {
                if (!breakout.IsResolved)
                    ResolveBreakout(breakout, time, reason);
            }
        }

        private void ResolveBreakout(BreakoutEvent breakout, DateTime time, string reason)
        {
            breakout.IsResolved = true;
            breakout.ResolutionTime = time;
            breakout.ResolutionReason = reason;
            
            ExportBreakoutAudit("Resolved", breakout);
            ExportBreakoutFinal(breakout);
            FlushExportWriters();
            
            Diagnostic(time,
                "BREAKOUT FINAL {0} Reason={1} MFE={2:0.0} MAE={3:0.0} ReturnedInside={4} Fakeout20={5}",
                breakout.EventId,
                reason,
                breakout.MfeTicks,
                breakout.MaeTicks,
                breakout.ReturnedInside,
                breakout.IsFakeout20Ticks);
        }
    }
}