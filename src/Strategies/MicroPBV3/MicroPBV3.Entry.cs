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
            if (BarsInProgress == 0 && Bars.IsFirstBarOfSession)
            {
                _sessionStartBarIdx = CurrentBar;
            }
            
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
                // track MFE/MAE first (so it runs even on early exits/locks)
                if (entryPriceHard > 0 && Position.Quantity > 0)
                {
                    // var px = Close[0];
                    var px = Position.MarketPosition == MarketPosition.Long ? GetCurrentBid() : GetCurrentAsk();
                    if (px <= 0) px = Close[0];
                    
                    var dir = Position.MarketPosition == MarketPosition.Long ? 1.0 : -1.0;
                    var pnlTicks = dir * (px - entryPriceHard) / TickSize;

                    _mfeTicks = Math.Max(_mfeTicks, pnlTicks);
                    _maeTicks = Math.Min(_maeTicks, pnlTicks);
                }

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

                if (MinMinutesBetweenTrades > 0 && lastFlatExecutionTime != DateTime.MinValue)
                {
                    var minsSinceLast = now.Subtract(lastFlatExecutionTime).TotalMinutes;
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
                
                // Determine trend
                var trendUp   = IsTrendUp(sigClosed, out _, out _, out _);
                var trendDown = IsTrendDown(sigClosed, out _, out _, out _);
                
                // Avoid chop zones
                var chopOk = ComputeChopOk(out _, out _, out _,
                    out _, out _, out _, out _, out _);
                
                if (!chopOk)
                {
                    ManageBreakEven();
                    return;
                }
                
                if (!trendUp && !trendDown)
                {
                    ManageBreakEven();
                    return;
                }
                
                // Cooldown after an "entry too far from EMA" rejection
                if (EntryDistCooldownBars > 0 && _entryDistBlockLastBar >= 0 && CurrentBar <= _entryDistBlockLastBar)
                {
                    if (DebugMode && IsFirstTickOfBar)
                        Print($"[ENTRY BLOCKED] {Time[0]:yyyy-MM-dd HH:mm:ss} entry-dist-cooldown active (CurrentBar={CurrentBar}, blockThrough={_entryDistBlockLastBar})");
                    
                    ManageBreakEven();
                    return;
                }

                // EMA structure filter
                ComputeEmaStructure(sigClosed, out _, out _, out _, out var emaStructureOk);

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
                    var pulledBack = PullbackTouchedFastEmaPrevBar(true, out _, out _);
                    var reclaimed  = Close[sigSignal] > emaFast[sigSignal];

                    if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, true, sigSignal))
                    {
                        var tag = "MPB_LONG_" + ++entrySeq;
                        PrepareBracket(tag, atrNow);

                        var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sigSignal] + buf, High[sigSignal] + buf));
                        var trigger = NormalizeBuyStopPrice(rawTrigger);

                        if (!IsMarketTradable(out _, out _))
                        {
                            ManageBreakEven();
                            return;
                        }

                        if (!PassesEntryDistanceFilter(out _))
                        {
                            if (EntryDistCooldownBars > 0)
                                _entryDistBlockLastBar = CurrentBar + EntryDistCooldownBars;
                            
                            ManageBreakEven();
                            return;
                        }

                        if (EnableMomentumFilter && !HasMomentum(0, SigClosed(), true, out _, out _, out _, out _, out _, out _))
                        {
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
                    var pulledBack = PullbackTouchedFastEmaPrevBar(false, out _, out _);
                    var reclaimed  = Close[sigSignal] < emaFast[sigSignal];

                    if (pulledBack && reclaimed && TrendConfirm(ConfirmBars, false, sigSignal))
                    {
                        var tag = "MPB_SHORT_" + ++entrySeq;
                        PrepareBracket(tag, atrNow);

                        var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Min(Close[sigSignal] - buf, Low[sigSignal] - buf));
                        var trigger = NormalizeSellStopPrice(rawTrigger);
                        
                        if (!IsMarketTradable(out _, out _))
                        {
                            ManageBreakEven();
                            return;
                        }

                        if (!PassesEntryDistanceFilter(out _))
                        {
                            if (EntryDistCooldownBars > 0)
                                _entryDistBlockLastBar = CurrentBar + EntryDistCooldownBars;

                            ManageBreakEven();
                            return;
                        }
                        
                        if (EnableMomentumFilter && !HasMomentum(0, SigClosed(), false, out _, out _, out _, out _, out _, out _))
                        {
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
                // if (EnableEndOfRunReport && haveMinFromOpen)
                //     TrackEntryDecisionForReport(sigSignal, sigEntry, sigClosed, minFromOpen, submitted);
            }
        }
        
        // Centralized trend logic (use everywhere: entry logic + DIAG)
        private bool IsTrendUp(int barsAgo, out double emaSlopeTicks, out double emaSepTicks, out double rangeTicksDiff)
        {
            var sigClosed = SigClosed();
            var m = GetEmaStruct(barsAgo);

            emaSlopeTicks = m.SlopeStrengthTicks; // or m.SlopeDirTicks if that’s what you want to expose
            emaSepTicks   = m.SepTicks;
            
            ComputeChopOk(out _, out _, out _,
                out _, out _, out var upTicksChop, out _, out _);
            
            TrendTicks(30, out _, out _, out _, out var upTicksLongRange, out var downTicksLongRange);

            rangeTicksDiff = upTicksLongRange - downTicksLongRange;
            if (rangeTicksDiff <= RangeTicksDiff && adx[sigClosed] < ChopBypassAdx) 
                return false;

            // ugly 
            if (adx[sigClosed] >= ChopBypassAdx && rangeTicksDiff <= 50)
                return false;
            
            // return m.HasBars && m.PriceAboveBoth && m.StructureOk && upTicks > downTicks;
            return m.HasBars && m.PriceAboveFast && upTicksChop >= ChopMinRangeTicks && upTicksLongRange > downTicksLongRange;
        }

        private bool IsTrendDown(int barsAgo, out double emaSlopeTicks, out double emaSepTicks, out double rangeTicksDiff)
        {
            var sigClosed = SigClosed();
            var m = GetEmaStruct(barsAgo);

            emaSlopeTicks = m.SlopeStrengthTicks; // or m.SlopeDirTicks
            emaSepTicks   = m.SepTicks;

            ComputeChopOk(out _, out _, out _,
                out _, out _, out var upTicks, out var downTicksChop, out _);
            
            TrendTicks(30, out _, out _, out _, out var upTicksLongRange, out var downTicksLongRange);

            rangeTicksDiff = downTicksLongRange - upTicksLongRange;
            if (rangeTicksDiff <= RangeTicksDiff && adx[sigClosed] < ChopBypassAdx) 
                return false;
            
            // ugly 
            if (adx[sigClosed] >= ChopBypassAdx && rangeTicksDiff <= 50)
                return false;
            
            // return m.HasBars && m.PriceBelowBoth && m.StructureOk && downTicks > upTicks;
            return m.HasBars && m.PriceBelowFast && downTicksChop >= ChopMinRangeTicks && downTicksLongRange > upTicksLongRange;
        }
        
        private bool BodyMidpointOnCorrectSide(int barsAgo, bool longSide, double ema)
        {
            var o = Open[barsAgo];
            var c = Close[barsAgo];
            var mid = (o + c) * 0.5;

            return longSide ? mid > ema : mid < ema;
        }

        private bool PassesEntryDistanceFilter(out double distTicks)
        {
            distTicks = 0;

            if (MaxPriorBarRangeTicks <= 0)
                return true;

            var barsAgo = SigClosed();        // last CLOSED bar (0 on OBC, 1 intrabar)
            if (CurrentBar < barsAgo)
                return true;

            var barHigh = High[barsAgo];
            var barLow  = Low[barsAgo];

            distTicks = (barHigh - barLow) / TickSize;
            return distTicks <= MaxPriorBarRangeTicks;
        }

        private bool IsMarketTradable(out double er, out bool trendOverride)
        {
            const int    ER_LOOKBACK = 10;
            const double ER_MIN = 0.32;

            const double ADX_OVERRIDE_MIN = 30;
            const double MIN_SLOPE_TICKS  = 20;   // tune
            const double MIN_SEP_TICKS    = 12;   // tune

            er = 0;
            trendOverride = false;

            var barsAgo = SigClosed();

            if (CurrentBar < barsAgo + ER_LOOKBACK)
                return true;

            // ---- Efficiency ----
            var netMove = Math.Abs(Close[barsAgo] - Close[barsAgo + ER_LOOKBACK]);

            double grossMove = 0;
            for (var i = barsAgo; i < barsAgo + ER_LOOKBACK; i++)
                grossMove += Math.Abs(Close[i] - Close[i + 1]);

            if (grossMove > TickSize)
                er = netMove / grossMove;

            var erOk = er >= ER_MIN;

            // ---- Trend override ----
            var adxVal = adx[barsAgo];

            var slopeTicks =
                Math.Abs(emaFast[barsAgo] - emaFast[barsAgo + 5]) / TickSize;

            var sepTicks =
                Math.Abs(emaFast[barsAgo] - emaSlow[barsAgo]) / TickSize;

            trendOverride =
                adxVal >= ADX_OVERRIDE_MIN &&
                slopeTicks >= MIN_SLOPE_TICKS &&
                sepTicks >= MIN_SEP_TICKS;

            return erOk || trendOverride;
        }

        private bool PullbackTouchedFastEmaPrevBar(bool longSide, out double emaTouch, out double distTicks)
        {
            emaTouch = 0;
            distTicks = double.NaN;

            var sig = SigClosed();

            if (CurrentBar < sig)
                return false;

            emaTouch = emaFast[sig];

            if (longSide)
            {
                var prox = Math.Max(0, LongTouchTicks) * TickSize;
                distTicks = (Low[sig] - emaTouch) / TickSize;
                return Low[sig] <= (emaTouch + prox);
            }
            else
            {
                var prox = Math.Max(0, ShortTouchTicks) * TickSize;
                distTicks = (High[sig] - emaTouch) / TickSize;
                return High[sig] >= (emaTouch - prox);
            }
        }

        // ✅ NEW overload (add this ABOVE your existing ComputeEmaStructure)
        // ADD: overload for diagnostics (and reporting) that returns ticks too
        private void ComputeEmaStructure(int sigSignal,
            out double slopeTicks,
            out double sepTicks,
            out bool slopeOk,
            out bool sepOk,
            out bool emaCrossover,
            out bool structureOk)
        {
            slopeTicks = 0;
            sepTicks = 0;
            slopeOk = true;
            sepOk = true;
            emaCrossover = false;

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

            var eff = pathTicks <= 1e-9 ? 0.0 : netMoveTicks / pathTicks;
            
            // directional movement × straightness penalty
            // slopeTicks = netMoveTicks * eff;
            // how many ticks the EMA rose over N bars
            slopeTicks = netMoveTicks;
            
            sepTicks = Math.Abs(emaFast[sigSignal] - emaSlow[sigSignal]) / TickSize;
            slopeOk = MinEmaSlopeTicks <= 0 || slopeTicks >= MinEmaSlopeTicks;
            sepOk = MinEmaSeparationTicks <= 0 || sepTicks >= MinEmaSeparationTicks;

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
            out bool emaCrossover,
            out bool structureOk)
        {
            ComputeEmaStructure(sigSignal, out _, out _, out slopeOk, out sepOk, out emaCrossover, out structureOk);
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

        private EmaStruct GetEmaStruct(int barsAgo)
        {
            var r = new EmaStruct();

            var lb = Math.Max(1, EmaSlopeLookbackBars);
            if (CurrentBar < barsAgo + lb)
            {
                r.HasBars = false;
                r.SlopeOk = false;
                r.SepOk = false;
                r.StructureOk = false;
                return r;
            }

            r.HasBars = true;

            // --- price vs EMAs ---
            var c = Close[barsAgo];
            var f0 = emaFast[barsAgo];
            var s0 = emaSlow[barsAgo];
            
            r.PriceAboveFast = c > f0;
            r.PriceBelowFast = c < f0;
            r.PriceAboveBoth = r.PriceAboveFast && c > s0;
            r.PriceBelowBoth = r.PriceBelowFast && c < s0;

            // --- separation (single source) ---
            r.SepTicks = Math.Abs(f0 - s0) / TickSize;

            // --- directional slope (single source) ---
            var fPast = emaFast[barsAgo + lb];
            r.SlopeDirTicks = (f0 - fPast) / TickSize;

            // --- strength score (your “slopeTicks” used in DIAG/structure) ---
            var netMoveTicks = Math.Abs(f0 - fPast) / TickSize;

            var pathTicks = 0.0;
            for (var i = 0; i < lb; i++)
            {
                var a = emaFast[barsAgo + i];
                var b = emaFast[barsAgo + i + 1];
                pathTicks += Math.Abs(a - b) / TickSize;
            }

            var eff = pathTicks <= 1e-9 ? 0.0 : netMoveTicks / pathTicks;
            r.SlopeStrengthTicks = netMoveTicks * eff;

            // NOTE: choose ONE metric to compare to MinEmaSlopeTicks.
            // If MinEmaSlopeTicks was intended for your DIAG/strength score, keep this line:
            var slopeMetricForThreshold = r.SlopeStrengthTicks;

            // If you intended MinEmaSlopeTicks to be “pure directional slope”, switch to:
            // var slopeMetricForThreshold = Math.Abs(r.SlopeDirTicks);

            r.SlopeOk = MinEmaSlopeTicks <= 0 || slopeMetricForThreshold >= MinEmaSlopeTicks;
            r.SepOk   = MinEmaSeparationTicks <= 0 || r.SepTicks >= MinEmaSeparationTicks;

            // --- crossover within lookback (single source) ---
            r.EmaCrossover = false;
            for (var i = 0; i < lb; i++)
            {
                var d0 = emaFast[barsAgo + i] - emaSlow[barsAgo + i];
                var d1 = emaFast[barsAgo + i + 1] - emaSlow[barsAgo + i + 1];

                if (d0 == 0 || d1 == 0 || (d0 > 0 && d1 < 0) || (d0 < 0 && d1 > 0))
                {
                    r.EmaCrossover = true;
                    break;
                }
            }

            r.StructureOk = r.SlopeOk && (r.SepOk || r.EmaCrossover);
            return r;
        }

        private sealed class EmaStruct
        {
            public bool HasBars;
            public bool PriceAboveBoth;
            public bool PriceBelowBoth;
            public bool PriceAboveFast;
            public bool PriceBelowFast;

            public double SlopeDirTicks;      // directional: emaNow - emaPast (ticks)
            public double SlopeStrengthTicks; // your netMove * eff (ticks, non-directional strength score)
            public double SepTicks;

            public bool SlopeOk;
            public bool SepOk;
            public bool EmaCrossover;
            public bool StructureOk;
        }
    }
}
