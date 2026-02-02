#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV3 : Strategy
    {
        protected override void OnBarUpdate()
        {
            if (_duplicateBlocked)
            {
                TryDuplicateSafetyCleanup("OnBarUpdate while duplicate-blocked");
                return;
            }

            // API Reporter: only primary series, realtime only, and not Strategy Analyzer
            if (BarsInProgress == 0 && ApiEnabled && State == State.Realtime && !IsInStrategyAnalyzer)
            {
                if (_tradeApi != null)
                    TryReportNewClosedTrades();

                if (_tddApi != null)
                {
                    var inWindow = IsWithinTradingWindow();

                    if (Bars.IsFirstBarOfSession)
                        _tddApi.ResetPeak();

                    _tddApi.OnHeartbeat(this, inWindow);
                }
            }

            // --- Main logic (primary series only) ---
            if (BarsInProgress != 0)
                return;

            if (CurrentBar < 50)
                return;

            // ===== POSITION MANAGEMENT (RUNS INTRABAR) =====
            if (Position.MarketPosition != MarketPosition.Flat)
            {
                if (EnforceDailyKill()) return;
                if (EnforceDailyProfitLock()) return;
                if (EnforceConsistencyRule()) return;

                CancelStaleEntryOrders(Time[0]);
                TryHardStopByTicks();
                TryProtectiveWatchdog();
                ManageBreakEven();
                return;
            }

            // ----- flat: keep working orders tidy -----
            CancelStaleEntryOrders(Time[0]);

            // ===== ENTRY EVALUATION GATE (ONLY WHEN FLAT) =====
            var isRealtime = (State == State.Realtime);

            var evalEntriesNow =
                Calculate == Calculate.OnBarClose ||
                !isRealtime ||
                (Calculate == Calculate.OnEachTick && IsFirstTickOfBar);

            if (!evalEntriesNow)
            {
                ManageBreakEven();
                return;
            }

            var sigSignal = SigSignal();   // last CLOSED bar (1 in realtime OnEachTick, else 0)
            var sigEntry  = SigEntry();    // current bar (0)
            var sigClosed = SigClosed();   // last CLOSED bar depending on Calculate mode

            // End-of-run report tracking: ensure we always record (even on early returns)
            var submitted = false;
            var minFromOpen = 0;
            var haveMinFromOpen = false;

            try
            {
                // one attempt per bar when realtime + OnEachTick
                if (isRealtime && Calculate == Calculate.OnEachTick)
                {
                    if (CurrentBar == lastEntryAttemptBar)
                    {
                        ManageBreakEven();
                        return;
                    }
                }

                // ===== DAY / SESSION SETUP (true session begin via SessionIterator) =====
                var now = Time[sigEntry];
                var sessionChanged = UpdateSessionTimes(now);

                if (sessionStart == DateTime.MinValue || sessionChanged)
                {
                    // optional: prior session summary before reset
                    if (sessionStart != DateTime.MinValue && prevSessionDate != DateTime.MinValue && DebugMode)
                    {
                        var cum = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                        var realizedPrevDay = cum - cumAtSessionOpen;
                        
                        Print($"[{prevSessionDate:yyyy-MM-dd}] realized={realizedPrevDay,8:C2} trades={tradesToday} locked={dayLocked.ToString()}");
                    }

                    ResetDay(_currentSessionBegin);
                }

                minFromOpen = (int)Math.Floor(now.Subtract(sessionStart).TotalMinutes);
                haveMinFromOpen = true;

                // ----- diagnostics (throttled) -----
                PrintDiagnostics(minFromOpen);

                // ===== LOCKS =====
                if (EnforceDailyProfitLock()) return;
                if (EnforceDailyKill()) return;
                if (EnforceConsistencyRule()) return;

                // ===== TIME FILTERS =====
                if (MidBreakEndMin > MidBreakStartMin)
                {
                    if (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin)
                    {
                        ManageBreakEven();
                        return;
                    }
                }

                if (minFromOpen < MinMinutesFromOpen || minFromOpen > MaxMinutesFromOpen)
                {
                    ManageBreakEven();
                    return;
                }

                if (!DayNotLocked())
                {
                    ManageBreakEven();
                    return;
                }

                if (tradesToday >= MaxTradesPerDay)
                {
                    ManageBreakEven();
                    return;
                }

                if (!RecentBarsAreCleanForEntry(sigClosed))
                {
                    ManageBreakEven();
                    return;
                }

                if (MinMinutesBetweenTrades > 0 && lastEntryExecutionTime != DateTime.MinValue)
                {
                    var minsSinceLast = now.Subtract(lastEntryExecutionTime).TotalMinutes;
                    if (minsSinceLast < MinMinutesBetweenTrades)
                    {
                        ManageBreakEven();
                        return;
                    }
                }

                // ===== FILTERS (USE LAST CLOSED BAR) =====
                var atrNow = Math.Max(atr[sigClosed], TickSize);
                if (atrNow <= 0)
                    return;

                var atrTicksNow = atrNow / TickSize;
                if (MaxAtrTicks > 0 && atrTicksNow > MaxAtrTicks)
                {
                    ManageBreakEven();
                    return;
                }

                // var trendUp   = Close[sigClosed] > emaFast[sigClosed] && Close[sigClosed] > emaSlow[sigClosed];
                // var trendDown = Close[sigClosed] < emaFast[sigClosed] && Close[sigClosed] < emaSlow[sigClosed];
                var trendUp   = IsTrendUp(sigClosed, out _, out _);
                var trendDown = IsTrendDown(sigClosed, out _, out _);

                if (!trendUp && !trendDown)
                {
                    ManageBreakEven();
                    return;
                }

                // EMA structure filter
                ComputeEmaStructure(sigClosed, out _, out _, out var emaStructureOk);

                if (!emaStructureOk)
                {
                    ManageBreakEven();
                    return;
                }

                var adxNow = adx[sigClosed];
                var adxOk = adxNow >= ADXMin && adxNow <= ADXMax;
                if (!adxOk)
                {
                    ManageBreakEven();
                    return;
                }

                // (submitted declared above for reporting)
                var buf = TickSize;
                var qty = GetEntryQty(sigEntry);

                // ===== LONG =====
                if (!submitted && EnableLongs && trendUp)
                {
                    var pulledBack = PullbackTouchedFastEmaPrevBar(true, sigSignal, out _, out _);
                    var reclaimed  = Close[sigSignal] > emaFast[sigSignal];

                    if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, true, sigSignal))
                    {
                        var tag = "MPB_LONG_" + (++entrySeq);
                        PrepareBracket(tag, atrNow);

                        var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sigSignal] + buf, High[sigSignal] + buf));
                        var trigger = NormalizeBuyStopPrice(rawTrigger);

                        if (!PassesEntryDistanceFilter(trigger, sigSignal, out var distTicksToEma))
                        {
                            if (DebugMode)
                                Print($"[ENTRY BLOCKED] {Time[sigEntry]:yyyy-MM-dd HH:mm:ss} LONG trigger too far from EMAFast: distTicks={distTicksToEma:0.0} > max={MaxEntryDistFromEmaFastTicks}");
                            ManageBreakEven();
                            return;
                        }

                        lastEntryAttemptBar = CurrentBar;
                        EnterLongStopMarket(qty, trigger, tag);
                        tradesToday++;
                        lastEntryTag = tag;
                        submitted = true;
                    }
                }

                // ===== SHORT =====
                if (!submitted && EnableShorts && trendDown)
                {
                    var pulledBack = PullbackTouchedFastEmaPrevBar(false, sigSignal, out _, out _);
                    var reclaimed  = Close[sigSignal] < emaFast[sigSignal];

                    if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, false, sigSignal))
                    {
                        var tag = "MPB_SHORT_" + (++entrySeq);
                        PrepareBracket(tag, atrNow);

                        var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Min(Close[sigSignal] - buf, Low[sigSignal] - buf));
                        var trigger = NormalizeSellStopPrice(rawTrigger);

                        if (!PassesEntryDistanceFilter(trigger, sigSignal, out var distTicksToEma))
                        {
                            if (DebugMode)
                                Print($"[ENTRY BLOCKED] {Time[sigEntry]:yyyy-MM-dd HH:mm:ss} SHORT trigger too far from EMAFast: distTicks={distTicksToEma:0.0} > max={MaxEntryDistFromEmaFastTicks}");
                            ManageBreakEven();
                            return;
                        }

                        lastEntryAttemptBar = CurrentBar;
                        EnterShortStopMarket(qty, trigger, tag);
                        tradesToday++;
                        lastEntryTag = tag;
                        submitted = true;
                    }
                }

                ManageBreakEven();
            }
            finally
            {
                if (EnableEndOfRunReport && haveMinFromOpen)
                    TrackEntryDecisionForReport(sigSignal, sigEntry, sigClosed, minFromOpen, submitted);
            }
        }
        
        // Centralized trend logic (use everywhere: entry logic + DIAG)
        private bool IsTrendUp(int barsAgo, out double emaSlopeTicks, out double emaSepTicks)
        {
            emaSlopeTicks = 0;
            emaSepTicks   = 0;

            var lb = Math.Max(1, EmaSlopeLookbackBars);

            // Need enough bars for slope measurement: barsAgo + lb must exist
            if (CurrentBar < barsAgo + lb)
                return false;

            var emaNow = emaFast[barsAgo];
            var emaPast = emaFast[barsAgo + lb];

            // Positive slope for uptrend
            emaSlopeTicks = (emaNow - emaPast) / TickSize;

            // Separation (absolute distance between EMAs)
            emaSepTicks = Math.Abs(emaFast[barsAgo] - emaSlow[barsAgo]) / TickSize;

            var priceOk = Close[barsAgo] > emaFast[barsAgo] && Close[barsAgo] > emaSlow[barsAgo];
            var slopeOk = MinEmaSlopeTicks <= 0 || emaSlopeTicks >= MinEmaSlopeTicks;
            var sepOk   = MinEmaSeparationTicks <= 0 || emaSepTicks >= MinEmaSeparationTicks;

            return priceOk && slopeOk && sepOk;
        }

        private bool IsTrendDown(int barsAgo, out double emaSlopeTicks, out double emaSepTicks)
        {
            emaSlopeTicks = 0;
            emaSepTicks   = 0;

            var lb = Math.Max(1, EmaSlopeLookbackBars);

            if (CurrentBar < barsAgo + lb)
                return false;

            var emaNow = emaFast[barsAgo];
            var emaPast = emaFast[barsAgo + lb];

            // Negative slope for downtrend
            emaSlopeTicks = (emaNow - emaPast) / TickSize;

            emaSepTicks = Math.Abs(emaFast[barsAgo] - emaSlow[barsAgo]) / TickSize;

            var priceOk = Close[barsAgo] < emaFast[barsAgo] && Close[barsAgo] < emaSlow[barsAgo];
            var slopeOk = MinEmaSlopeTicks <= 0 || emaSlopeTicks <= -MinEmaSlopeTicks;
            var sepOk   = MinEmaSeparationTicks <= 0 || emaSepTicks >= MinEmaSeparationTicks;

            return priceOk && slopeOk && sepOk;
        }


        private bool PassesEntryDistanceFilter(double entryTriggerPrice, int sigSignal, out double distTicks)
        {
            distTicks = 0;

            if (MaxEntryDistFromEmaFastTicks <= 0)
                return true;

            var ema = emaFast[sigSignal];
            if (ema <= 0 || double.IsNaN(ema) || double.IsInfinity(ema))
                return true;

            distTicks = Math.Abs(entryTriggerPrice - ema) / TickSize;
            return distTicks <= MaxEntryDistFromEmaFastTicks;
        }

        private bool PullbackTouchedFastEmaPrevBar(bool longSide, int sigSignal, out double emaTouch, out double distTicks)
        {
            emaTouch = 0;
            distTicks = double.NaN;

            if (CurrentBar < sigSignal)
                return false;

            emaTouch = emaFast[sigSignal];

            if (longSide)
            {
                var prox = Math.Max(0, LongTouchTicks) * TickSize;
                distTicks = (Low[sigSignal] - emaTouch) / TickSize;
                return Low[sigSignal] <= (emaTouch + prox);
            }
            else
            {
                var prox = Math.Max(0, ShortTouchTicks) * TickSize;
                distTicks = (High[sigSignal] - emaTouch) / TickSize;
                return High[sigSignal] >= (emaTouch - prox);
            }
        }

        // ✅ NEW overload (add this ABOVE your existing ComputeEmaStructure)
        // ADD: overload for diagnostics (and reporting) that returns ticks too
        private void ComputeEmaStructure(int sigSignal,
            out double slopeTicks,
            out double sepTicks,
            out bool slopeOk,
            out bool sepOk,
            out bool structureOk)
        {
            slopeTicks = 0;
            sepTicks = 0;
            slopeOk = true;
            sepOk = true;

            var lb = Math.Max(1, EmaSlopeLookbackBars);

            if (CurrentBar < sigSignal + lb)
            {
                slopeOk = false;
                sepOk = false;
                structureOk = false;
                return;
            }

            // Trend-strength score: net move * efficiency
            var emaNow = emaFast[sigSignal];
            var emaPast = emaFast[sigSignal + lb];
            var netMoveTicks = Math.Abs(emaNow - emaPast) / TickSize;

            var pathTicks = 0.0;
            for (var i = 0; i < lb; i++)
            {
                var a = emaFast[sigSignal + i];
                var b = emaFast[sigSignal + i + 1];
                pathTicks += Math.Abs(a - b) / TickSize;
            }

            var eff = (pathTicks <= 1e-9) ? 0.0 : (netMoveTicks / pathTicks);
            slopeTicks = netMoveTicks * eff;

            sepTicks = Math.Abs(emaFast[sigSignal] - emaSlow[sigSignal]) / TickSize;

            slopeOk = (MinEmaSlopeTicks <= 0) || (slopeTicks >= MinEmaSlopeTicks);
            sepOk = (MinEmaSeparationTicks <= 0) || (sepTicks >= MinEmaSeparationTicks);

            var emaCrossover = false;
            for (var i = 0; i < lb; i++)
            {
                var d0 = emaFast[sigSignal + i] - emaSlow[sigSignal + i];
                var d1 = emaFast[sigSignal + i + 1] - emaSlow[sigSignal + i + 1];

                if (d0 == 0 || d1 == 0 || (d0 > 0 && d1 < 0) || (d0 < 0 && d1 > 0))
                {
                    emaCrossover = true;
                    break;
                }
            }

            structureOk = slopeOk && (sepOk || emaCrossover);
        }

        
        private void ComputeEmaStructure(int sigSignal,
            out bool slopeOk,
            out bool sepOk,
            out bool structureOk)
        {
            ComputeEmaStructure(sigSignal, out _, out _, out slopeOk, out sepOk, out structureOk);
        }

        private bool PassesWickFilter(int barsAgo)
        {
            var o = Open[barsAgo];
            var c = Close[barsAgo];
            var h = High[barsAgo];
            var l = Low[barsAgo];

            var range = Math.Max(h - l, TickSize);
            var body = Math.Abs(c - o);

            var upperWick = h - Math.Max(o, c);
            var lowerWick = Math.Min(o, c) - l;

            var upperTicks = upperWick / TickSize;
            var lowerTicks = lowerWick / TickSize;

            if (MaxBothWicksTicks > 0 && upperTicks >= MaxBothWicksTicks && lowerTicks >= MaxBothWicksTicks)
                return false;

            if (WickBlockSingleWick && MaxSingleWickTicks > 0 && (upperTicks >= MaxSingleWickTicks || lowerTicks >= MaxSingleWickTicks))
                return false;

            if (WickBlockSmallBody && MinBodyPctOfRange > 0 && (body / range) < MinBodyPctOfRange)
                return false;

            return true;
        }

        private bool RecentBarsAreCleanForEntry(int sig)
        {
            if (WickFilterLookback <= 0)
                return true;

            if (WickOnlyPreviousBar)
                return PassesWickFilter(sig);

            var max = Math.Min(WickFilterLookback, CurrentBar - sig);
            for (var i = 0; i < max; i++)
            {
                var barsAgo = sig + i;
                if (!PassesWickFilter(barsAgo))
                    return false;
            }

            return true;
        }
    }
}
