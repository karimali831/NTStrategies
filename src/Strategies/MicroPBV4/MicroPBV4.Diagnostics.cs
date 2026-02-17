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
    public partial class MicroPBV4 : Strategy
    {
        private void PrintDiagnostics(int minFromOpen)
        {
	        if (!DebugMode)
		        return;
	        
	        var snap = EvaluateEntrySnapshot(minFromOpen);
	        var effContracts = GetEffectiveContractsToday();
	        
	        var accountName = Account != null ? Account.Name : "N/A";
	        var realizedToday = GetRealizedToday();
	        var dailyKill = GetDailyKillLimitUsd();
	        var dailyMaxProfit = GetDailyProfitLimitUsd();
	   
	        // Wick filter should match entry gating (use sigClosed)
	        var wickDiag = BuildWickDiag();
	        
	        var pos = snap.TrendUp ? "Long" : snap.TrendDown ? "Short" : "None";

	        var trend =
		        snap.TrendUp  ? "trendUp=True" : snap.TrendDown ? "trendDown=True" : "trend=None";
	        
	         var unrealizedNow = snap.Flat ? 0.0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
	        var totalToday = realizedToday + unrealizedNow;

	        var bufferToKill = dailyKill > 0 ? dailyKill + totalToday : 0.0;
	        var dailyProfitRemaining = dailyMaxProfit > 0 ? dailyMaxProfit - totalToday : 0.0;
	        
	        if (!snap.TimeOk)
	        {
		        Print($"Out of time window: {Time[0]:yyyy-MM-dd HH:mm:ss} (minFromOpen={minFromOpen}, inMainWindow={snap.InMainWindow}, midBreak={snap.InMidBreak})\n");
		        return;
	        }

	        if (snap.PnlOrDdLock)
	        {
		        Print(string.Format("PnL/DD/Trades Locked: (realizedToday={0:C2}, unrealizedNow={1:C2}, totalToday={2:C2}, dayLocked={3}, tradesToday={4}\n", 
			        realizedToday, 
			        unrealizedNow, 
			        totalToday,
			        dayLocked.ToString(),
			        tradesToday));
		        return;
	        }
	        
	        var sb = new StringBuilder(512);

	        sb.AppendLine(
		        $"[DIAG] {Time[0]:yyyy-MM-dd HH:mm:ss} - Acc: {accountName} " +
		        $"(Default Contracts: {Contracts} - Effective Contracts: {effContracts})");

	        sb.AppendLine($"  a) Blocks/filters: {snap.Blocks}");
	        sb.AppendLine($"  b) Wick ticks: {wickDiag}");
	        sb.AppendLine("   c) Regime Metrics:");
	        sb.AppendLine($"     ADX   = {snap.Regime.Adx:0.##}");
	        sb.AppendLine($"     ATRt  = {snap.Regime.AtrTicks:0.##} ");
	        
	        sb.AppendLine(
		        $"     EMA   fastEmaDistTicks={snap.PbDistTicks:F1},  " +
		        $"slopeT={snap.Regime.EmaSlopeTicks:0.##}, sepT={snap.Regime.EmaSepTicks:0.##}");
	        
	        sb.AppendLine(
		        $"  d) MaxDailyLoss={dailyKill:C0}, " +
		        $"LossRemaining={bufferToKill:C0}, " +
		        $"MaxDailyProfit={dailyMaxProfit:C0}, " +
		        $"ProfitRemaining={dailyProfitRemaining:C0}");
	        
	        sb.AppendLine(
		        $"  e) MaxPriorBarRange={MaxPriorBarRangeTicks:0.0} " +
		        $"PriorBarLongRangeTicks={snap.PriorBarRangeTicksLong:0.0} " +
		        $"PriorBarShortRangeTicks={snap.PriorBarRangeTicksShort:0.0} " +
		        $"pass={OkIcon(snap.PassesEntryDistance)}");
	        
	        sb.AppendLine($"  f) TrendStrengthTicks={snap.TrendStrengthTicks} (trendStrengthMinTicks={snap.TrendStrengthMinTicks}, trendStrengthTicksLbBars={snap.TrendStrengthTicksLbBars})");
	        
	        sb.AppendLine(
		        $"  g) {pos} Entry {OkIcon(snap.WouldSubmitNow)} " +
		        $"({trend}, pulledBack=" +
		        $"{(snap.TrendUp ? snap.LongPulledBack : snap.ShortPulledBack)}, " +
		        $"reclaimed=" +
		        $"{(snap.TrendUp ? snap.LongReclaimed : snap.ShortReclaimed)}, " +
		        $"trendFailReason={snap.TrendFailReason}, " +
		        $"confirmFailReason={snap.ConfirmFailReason})");

	        sb.AppendLine(new string('-', 103));

	        Print(sb.ToString());
        }
        
        private static string OkIcon(bool ok) { return ok ? "✔" : "✖"; }
        
        private string BuildWickDiag()
        {
	        if (WickFilterLookback <= 0)
		        return "wick=off";

	        if (WickOnlyPreviousBar)
	        {
		        // OnBarClose => [0] is closed; "previous bar" should be barsAgo=1 (if it exists)
		        if (CurrentBar < 1)
			        return "wick=insufficient-bars";

		        GetWickTickStats(1, out var u, out var l, out var bp);

		        var pass = PassesWickFilter(); // your existing filter logic
		        return $"wick[b1]=U{u:0.0} L{l:0.0} body%={bp:0.00} pass={pass}";
	        }

	        var max = Math.Min(WickFilterLookback, CurrentBar);
	        if (max <= 0)
		        return "wick=insufficient-bars";

	        var sb = new StringBuilder();
	        sb.Append("wick[");

	        for (var i = 0; i < max; i++)
	        {
		        GetWickTickStats(i, out var u, out var l, out var bp);

		        // NOTE: if PassesWickFilter() is multi-bar, it will be same each iteration.
		        // If you want per-bar pass, we can add PassesWickFilterForBar(i) later.
		        var pass = PassesWickFilter();

		        if (i > 0) sb.Append(" | ");
		        sb.Append($"b{i}:U{u:0.0} L{l:0.0} body%={bp:0.00} {(pass ? "OK" : "BLOCK")}");
	        }

	        sb.Append("]");
	        return sb.ToString();
        }


        private void GetWickTickStats(int barsAgo, out double upperTicks, out double lowerTicks, out double bodyPctOfRange)
        {
	        upperTicks = 0;
	        lowerTicks = 0;
	        bodyPctOfRange = 0;

	        var o = Open[barsAgo];
	        var c = Close[barsAgo];
	        var h = High[barsAgo];
	        var l = Low[barsAgo];

	        var range = Math.Max(h - l, TickSize);
	        var body = Math.Abs(c - o);

	        var upperWick = h - Math.Max(o, c);
	        var lowerWick = Math.Min(o, c) - l;

	        upperTicks = upperWick / TickSize;
	        lowerTicks = lowerWick / TickSize;

	        bodyPctOfRange = body / range;
        }
        
        private EntryEvalSnapshot EvaluateEntrySnapshot(int minFromOpen)
		{
		    var s = new EntryEvalSnapshot
		    {
			    // ---- time gate ----
			    InMainWindow = minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen,
			    InMidBreak = MidBreakEndMin > MidBreakStartMin &&
			                 minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin,
			    HasSessionStart = sessionStart != DateTime.MinValue,
			    Flat = Position.MarketPosition == MarketPosition.Flat
		    };

		    s.TimeOk = s.HasSessionStart && s.InMainWindow  && !s.InMidBreak;

		    // ---- locks ----
		    s.DayOk = DayNotLocked();
		    s.TradeCountOk = tradesToday < MaxTradesPerDay;
		    s.PnlOrDdLock = !DayNotLocked() || !s.TradeCountOk;
		    
			// -- Entry Dist Cooldown ---
		    s.EntryDistCooldownEnabled = EntryDistCooldownBars > 0;
		    s.EntryDistCooldownOk = true;
		    s.EntryDistBlockBarsLeft = 0;
		    s.EntryDistCooldownReason = "entry-dist-cooldown=off";

		    if (EntryDistCooldownBars > 0)
		    {
			    if (_entryDistBlockLastBar >= 0 && CurrentBar <= _entryDistBlockLastBar)
			    {
				    s.EntryDistCooldownOk = false;
				    s.EntryDistBlockBarsLeft = _entryDistBlockLastBar - CurrentBar + 1;
				    s.EntryDistCooldownReason = $"entry-dist-cooldown=block(barsLeft={s.EntryDistBlockBarsLeft})";
			    }
			    else
			    {
				    s.EntryDistCooldownReason = "entry-dist-cooldown=ok";
			    }
		    }
		    
		    // ---- spacing ----
		    s.SpacingOk = true;
		    if (MinMinutesBetweenTrades > 0 && lastFlatExecutionTime != DateTime.MinValue)
		    {
		        var minsSinceLast = Time[0].Subtract(lastFlatExecutionTime ).TotalMinutes;
		        s.SpacingOk = minsSinceLast >= MinMinutesBetweenTrades;
		    }

		    // ---- WICK FITLER ----
		    s.WickOk = RecentBarsAreCleanForEntry();
		    
		    //-- CONFIRM BARS
		    var confirmDelay = Math.Max(0, ConfirmBars - 1);

		    s.EntryDistDeferLongBar  = _entryDistDeferLongBar;
		    s.EntryDistDeferShortBar = _entryDistDeferShortBar;

		    s.LongExtraDelay  = _entryDistDeferLongBar  >= 0 && CurrentBar < _entryDistDeferLongBar  ? 1 : 0;
		    s.ShortExtraDelay = _entryDistDeferShortBar >= 0 && CurrentBar < _entryDistDeferShortBar ? 1 : 0;
		    s.SigLongAgo  = confirmDelay + s.LongExtraDelay;
		    s.SigShortAgo = confirmDelay + s.ShortExtraDelay;
		    
			//-- ENTRY DISTANCE (per-side, using the signal barsAgo)
			s.EntryDistLongOk  = PassesEntryDistanceFilter(true, s.SigLongAgo,  out var longPriorRangeTicks);
			s.EntryDistShortOk = PassesEntryDistanceFilter(false, s.SigShortAgo, out var shortPriorRangeTicks);

			s.PriorBarRangeTicksLong  = longPriorRangeTicks;
			s.PriorBarRangeTicksShort = shortPriorRangeTicks;

			
		    // ---- TREND ----
		    TrendConfirm(out var trendFail, out var trendUp, out var trendDown);
		    s.TrendUp = trendUp;
		    s.TrendDown = trendDown;
		    s.TrendFailReason = trendFail;
		    
		    s.ConfirmFailReason = "none";
		    var confirmLong  = ConfirmLongEntry(s.SigLongAgo, out var longFailReason);
		    var confirmShort = ConfirmShortEntry(s.SigShortAgo, out var shortFailReason);

		    if (s.TrendUp)
			    s.ConfirmFailReason = confirmLong ? "none" : longFailReason;
		    else if (s.TrendDown)
			    s.ConfirmFailReason = confirmShort ? "none" : shortFailReason;
		    
		    TrendTicks(30, out _, out _, out _, out s.LongRangeTicks, out s.ShortRangeTicks);

			// ---- setup components (signal bar) ----
			// s.LongPulledBack  = PullbackTouchedFastEma(true,  s.SigLongAgo,  out s.PbEmaLong,  out s.PbDistLong);
			// s.ShortPulledBack = PullbackTouchedFastEma(false, s.SigShortAgo, out s.PbEmaShort, out s.PbDistShort);
			
			s.LongPulledBack  = TouchedEma(true, out var longDistTicks);
			s.ShortPulledBack = TouchedEma(false, out var shortDistTicks);

			s.LongReclaimed  = Reclaimed(true, s.SigLongAgo);
			s.ShortReclaimed = Reclaimed(false, s.SigShortAgo);
			
			// Which side matters "right now"?
			if (s.TrendUp || s.LongPulledBack)
			{
			    s.PassesEntryDistance = s.EntryDistLongOk;
			    s.PbDistTicks = longDistTicks;
			    
			    StrongTrend(true, out s.TrendStrengthTicks, out s.TrendStrengthTicksLbBars, out s.TrendStrengthMinTicks);
			}
			else if (s.TrendDown || s.ShortPulledBack)
			{
			    s.PassesEntryDistance = s.EntryDistShortOk;
			    s.PbDistTicks = shortDistTicks;
			    
			    StrongTrend(false, out s.TrendStrengthTicks, out s.TrendStrengthTicksLbBars, out s.TrendStrengthMinTicks);
			}
			else
			{
			    s.PassesEntryDistance = true;
			}

		    // ---- Base regime snapshot (always populated for DIAG) ----
		    ComputeMarketRegime(out s.Regime);

			// ---- Candidate definition (WITHOUT regime gating) ----
		    var baseConfirm =
			    !s.PnlOrDdLock &&
			    s.TimeOk       &&
			    s.WickOk;

		    s.LongCandidate =
			    baseConfirm &&
			    confirmLong &&
			    EnableLongs &&
			    s.TrendUp &&
			    s.LongPulledBack &&
			    s.LongReclaimed;

		    s.ShortCandidate =
			    baseConfirm &&
			    confirmShort &&
			    EnableShorts &&
			    s.TrendDown &&
			    s.ShortPulledBack &&
			    s.ShortReclaimed;
		    
		    // ---- Regime gate only if candidate ----
			// IMPORTANT: gate must use the SAME sigBarsAgo you used for pulledBack/reclaimed/confirm,
			// otherwise DIAG and actual behavior will diverge.
		    s.Tradeable = true; // default true when not applicable
		    if (s.LongCandidate)
			    s.Tradeable = PassesMarketRegimeGate(true, s.SigLongAgo, out s.Regime);
		    else if (s.ShortCandidate)
			    s.Tradeable = PassesMarketRegimeGate(false, s.SigShortAgo, out s.Regime);

		    // ---- Would submit ----
		    s.WouldSubmitLongNow  = s.Flat && s.LongCandidate  && s.Tradeable;
		    s.WouldSubmitShortNow = s.Flat && s.ShortCandidate && s.Tradeable;
		    s.WouldSubmitNow      = s.WouldSubmitLongNow || s.WouldSubmitShortNow;
		    
			var blocks = new List<string>(16);

		    if (!s.TimeOk) blocks.Add(s.InMidBreak ? "mid-break" : "outside-time-window");
		    if (!s.DayOk) blocks.Add("dayLocked");
		    if (!s.TradeCountOk) blocks.Add("max-trades");
		    if (!s.SpacingOk) blocks.Add("min-minutes-between");
		    if (!s.EntryDistCooldownOk) blocks.Add("entry-dist-cooldown");
		    if (!s.WickOk) blocks.Add("wick-filter");
		    if ((s.TrendUp || s.LongPulledBack) && !s.EntryDistLongOk)
		    {
			    var willDeferNext = s.EntryDistDeferLongBar == CurrentBar + 2;
			    blocks.Add(
				    $"long-prior-bar-too-large: (rangeTicks={s.PriorBarRangeTicksLong:0.0} > max={MaxPriorBarRangeTicks:0.0}, " +
				    $"cooldownBars={EntryDistCooldownBars}, willDeferNext={willDeferNext})");
		    }
		    else if ((s.TrendDown || s.ShortPulledBack) && !s.EntryDistShortOk)
		    {
			    var willDeferNext = s.EntryDistDeferShortBar == CurrentBar + 2;
			    blocks.Add(
				    $"short-prior-bar-too-large: (rangeTicks={s.PriorBarRangeTicksShort:0.0} > max={MaxPriorBarRangeTicks:0.0}, " +
				    $"cooldownBars={EntryDistCooldownBars}, willDeferNext={willDeferNext})");
		    }

		    if ((s.LongCandidate || s.ShortCandidate) && !s.Tradeable)
			    blocks.Add("market-regime-block");
		    
		    s.Blocks = blocks.Count == 0 ? "none" : string.Join(";", blocks);
		    
		    return s;
		}	
        
        
        private struct EntryEvalSnapshot
        {
	        public bool TimeOk, InMainWindow, InMidBreak, HasSessionStart, DayOk, PnlOrDdLock, TradeCountOk, SpacingOk;
	        public bool WickOk;
	        public bool TrendUp, TrendDown, Tradeable;
	        public string TrendFailReason, ConfirmFailReason;
	        public bool LongPulledBack, ShortPulledBack;
	        public bool LongReclaimed, ShortReclaimed;
	        public bool Flat, LongCandidate, ShortCandidate, WouldSubmitLongNow, WouldSubmitShortNow, WouldSubmitNow;

	        public double PriorBarRangeTicksLong, PriorBarRangeTicksShort, PbDistTicks;
	        public bool EntryDistLongOk, EntryDistShortOk, EntryDistCooldownOk, EntryDistCooldownEnabled;
	        public int EntryDistBlockBarsLeft;
	        
	        public string EntryDistCooldownReason;
	        public bool PassesEntryDistance { get; set; }
	        public RegimeSnapshot Regime;
	        public int SigLongAgo, SigShortAgo;
	        public int LongExtraDelay, ShortExtraDelay;
	        public double TrendStrengthTicks, TrendStrengthMinTicks, LongRangeTicks, ShortRangeTicks;
	        public int TrendStrengthTicksLbBars, EntryDistDeferLongBar, EntryDistDeferShortBar;
	        public string Blocks;              // final: "wick-filter;chop-filter;..." etc
        }
    }
}
