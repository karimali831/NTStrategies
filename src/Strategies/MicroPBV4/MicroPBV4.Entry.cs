#region Using declarations

using System;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class MicroPBV4 : Strategy
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

                CancelStaleEntryOrders(Time[0]);
                TryHardStopByTicks();
                ManageBreakEven();
                return;
            }


            // ----- flat: keep working orders tidy -----
            CancelStaleEntryOrders(Time[0]);
            var submitted = false;
            
            // ---- confirm bars
            var confirmDelay = Math.Max(0, ConfirmBars - 1);

            // expire missed deferrals (only valid for the exact bar)
            if (_entryDistDeferLongBar >= 0 && CurrentBar > _entryDistDeferLongBar)  _entryDistDeferLongBar = -1;
            if (_entryDistDeferShortBar >= 0 && CurrentBar > _entryDistDeferShortBar) _entryDistDeferShortBar = -1;
            
            // ===== DAY / SESSION SETUP (true session begin via SessionIterator) =====
            var now = Time[0];
            var sessionChanged = UpdateSessionTimes(now);

            if (sessionStart == DateTime.MinValue || sessionChanged)
            {
                // optional: prior session summary before reset
                if (sessionStart != DateTime.MinValue && prevSessionDate != DateTime.MinValue && DebugMode)
                {
                    var cum = SystemPerformance.AllTrades.TradesPerformance.Currency.CumProfit;
                    var realizedPrevDay = cum - cumAtSessionOpen;

                    Print(
                        $"[{prevSessionDate:yyyy-MM-dd}] realized={realizedPrevDay,8:C2} trades={tradesToday} locked={dayLocked.ToString()}");
                }

                ResetDay(_currentSessionBegin);
            }

            var minFromOpen = (int)Math.Floor(now.Subtract(sessionStart).TotalMinutes);

            // ----- diagnostics (throttled) -----
            PrintDiagnostics(minFromOpen);

            // ===== LOCKS =====
            if (EnforceDailyProfitLock()) return;
            if (EnforceDailyKill()) return;

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

            if (!RecentBarsAreCleanForEntry())
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
            var atrNow = Math.Max(atr[0], TickSize);
            if (atrNow <= 0)
                return;

            var atrTicksNow = atrNow / TickSize;
            if (MaxAtrTicks > 0 && atrTicksNow > MaxAtrTicks)
            {
                ManageBreakEven();
                return;
            }
            
            // Cooldown after an "entry too far from EMA" rejection
            if (EntryDistCooldownBars > 0 && _entryDistBlockLastBar >= 0 && CurrentBar <= _entryDistBlockLastBar)
            {
                if (DebugMode)
                    Print(
                        $"[ENTRY BLOCKED] {Time[0]:yyyy-MM-dd HH:mm:ss} entry-dist-cooldown active (CurrentBar={CurrentBar}, blockThrough={_entryDistBlockLastBar})");

                ManageBreakEven();
                return;
            }
            
            // (submitted declared above for reporting)
            var buf = TickSize;
            var qty = GetEntryQty();
            
            // Trend
            TrendConfirm(out _, out var trendUp, out var trendDown);

            // ===== LONG =====
            if (!submitted && EnableLongs && trendUp)
            {
                var extraDelay = CurrentBar == _entryDistDeferLongBar ? 1 : 0;
                var sig = confirmDelay + extraDelay;

                if (CurrentBar < sig)
                {
                    ManageBreakEven();
                    return;
                }

                var pulledBack = PullbackTouchedFastEmaPrevBar(true, sig, out _, out _);
                var reclaimed  = Close[sig] > emaFast[sig];

                if (pulledBack && reclaimed && ConfirmLongEntry(sig, out _))  // ConfirmLongEntry uses [0] today; see NOTE below
                {
                    var tag = $"MPB_LONG__{Time[0]:yyMMddHHmmss}_{++entrySeq:000}";
                    PrepareBracket(tag, atrNow);

                    var rawTrigger =
                        Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sig] + buf, High[sig] + buf));
                    var trigger = NormalizeBuyStopPrice(rawTrigger);

                    if (extraDelay == 0 && !PassesEntryDistanceFilter(sig, out var distTicks))
                    {
                        _entryDistDeferLongBar = CurrentBar + 1;
                        ManageBreakEven();
                        return;
                    }
                    
                    // clear defer if we are consuming it
                    if (extraDelay == 1)
                        _entryDistDeferLongBar = -1;

                    if (!PassesMarketRegimeGate(true, out var r))
                    {
                        if (DebugMode)
                            Print($"[ENTRY BLOCKED] {Time[0]:yyyy-MM-dd HH:mm:ss} market-regime " +
                                  $"fail={r.Fail} score={r.Score:0.#} minScore={r.MinScoreUsed:0.#} " +
                                  $"ER={r.Er:0.###} minER={RegimeErMin:0.###} " +
                                  $"crossPenalty={r.CrossPenaltyActive} barsSinceCross={r.BarsSinceCross} " +
                                  $"label={r.Label}");

                        ManageBreakEven();
                        return;
                    }

                    RememberRegimeForTag(tag, r.Json);
                    EnterLongStopMarket(qty, trigger, tag);
                    tradesToday++;
                    lastEntryTag = tag;
                    submitted = true;
                }
            }

            // ===== SHORT =====
            if (!submitted && EnableShorts && trendDown)
            {
                var extraDelay = CurrentBar == _entryDistDeferShortBar ? 1 : 0;
                var sig = confirmDelay + extraDelay;

                if (CurrentBar < sig)
                {
                    ManageBreakEven();
                    return;
                }

                var pulledBack = PullbackTouchedFastEmaPrevBar(false, sig, out _, out _);
                var reclaimed  = Close[sig] < emaFast[sig];

                if (pulledBack && reclaimed && ConfirmShortEntry(sig, out _)) // ConfirmShortEntry uses [0] today; see NOTE below
                {
                    var tag = $"MPB_SHORT__{Time[0]:yyMMddHHmmss}_{++entrySeq:000}";
                    PrepareBracket(tag, atrNow);

                    var rawTrigger =
                        Instrument.MasterInstrument.RoundToTickSize(Math.Min(Close[sig] - buf, Low[sig] - buf));
                    var trigger = NormalizeSellStopPrice(rawTrigger);

                    if (extraDelay == 0 && !PassesEntryDistanceFilter(sig, out var distTicks))
                    {
                        _entryDistDeferShortBar = CurrentBar + 1;
                        ManageBreakEven();
                        return;
                    }

                    if (extraDelay == 1)
                        _entryDistDeferShortBar = -1;

                    if (!PassesMarketRegimeGate(false, out var r))
                    {
                        if (DebugMode)
                            Print($"[ENTRY BLOCKED] {Time[0]:yyyy-MM-dd HH:mm:ss} market-regime " +
                                  $"fail={r.Fail} score={r.Score:0.#} minScore={r.MinScoreUsed:0.#} " +
                                  $"ER={r.Er:0.###} minER={RegimeErMin:0.###} " +
                                  $"crossPenalty={r.CrossPenaltyActive} barsSinceCross={r.BarsSinceCross} " +
                                  $"label={r.Label}");

                        ManageBreakEven();
                        return;
                    }

                    RememberRegimeForTag(tag, r.Json);
                    EnterShortStopMarket(qty, trigger, tag);
                    tradesToday++;
                    lastEntryTag = tag;
                    submitted = true;
                }
            }

            ManageBreakEven();
        }
        
        private bool RecentBarsAreCleanForEntry()
        {
            if (WickFilterLookback <= 0)
                return true;

            if (WickOnlyPreviousBar)
                return PassesWickFilter();

            var max = Math.Min(WickFilterLookback, CurrentBar);
            for (var i = 0; i < max; i++)
            {
                if (!PassesWickFilter())
                    return false;
            }

            return true;
        }
        
        private bool PullbackTouchedFastEmaPrevBar(bool longSide, int barsAgo, out double emaTouch, out double distTicks)
        {
            emaTouch = 0;
            distTicks = double.NaN;

            if (CurrentBar < barsAgo)
                return false;

            emaTouch = emaFast[barsAgo];

            if (longSide)
            {
                var prox = Math.Max(0, LongTouchTicks) * TickSize;
                distTicks = (Low[barsAgo] - emaTouch) / TickSize;
                return Low[barsAgo] <= (emaTouch + prox);
            }
            else
            {
                var prox = Math.Max(0, ShortTouchTicks) * TickSize;
                distTicks = (High[barsAgo] - emaTouch) / TickSize;
                return High[barsAgo] >= (emaTouch - prox);
            }
        }
    }
}
