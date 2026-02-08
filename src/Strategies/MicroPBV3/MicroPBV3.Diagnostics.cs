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
        private bool IsWithinTradingWindow()
        {
            if (sessionStart == DateTime.MinValue)
                return false;
        
            var now = Time[0];
            var minFromOpen = (int)Math.Floor(now.Subtract(sessionStart).TotalMinutes);
            if (MidBreakEndMin > MidBreakStartMin && minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin)
                return false;
        
            return minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen;
        }

        private void PrintDiagnostics(int minFromOpen)
        {
	        if (!DebugMode)
		        return;

	        if (State == State.Historical && !IsHistorical)
		        return;

	        var sigSignal = SigSignal(); // last CLOSED bar (stable)
	        var sigEntry = SigEntry(); // current forming bar (0)
	        var sigClosed = SigClosed(); // your "closed" index used by wick filter / other stable filters
	        
	        var diagTime = Time[sigEntry];
	        if ((diagTime - lastDiagTime).TotalSeconds < diagThrottleSeconds)
		        return;

	        lastDiagTime = diagTime;
	        
	        var snap = EvaluateEntrySnapshot(sigSignal, sigEntry, sigClosed, minFromOpen);
	        var effContracts = GetEffectiveContractsToday(sigSignal);

	        var tSignal = Time[sigSignal];
	        var tEntry = Time[sigEntry];
	        
	        var accountName = Account != null ? Account.Name : "N/A";
	        var realizedToday = GetRealizedToday();
	        var dailyKill = GetDailyKillLimitUsd();
	        var dailyMaxProfit = GetDailyProfitLimitUsd();
	        var pnlOrDdLock = !DayNotLocked() || !snap.TradeCountOk;

	        // Wick filter should match entry gating (use sigClosed)
	        // var wickDiag = BuildWickDiag(sigClosed);
	        
	        // Base filters
	        var baseLongFilter =
		        EnableLongs && snap.TrendUp && snap.AdxOk && snap.ChopOk &&
		        !snap.VolBlocked && !pnlOrDdLock && snap.TimeOk && snap.WickOk && snap.EmaStructureOk;

	        var baseShortFilter =
		        EnableShorts && snap.TrendDown && snap.AdxOk && snap.ChopOk &&
		        !snap.VolBlocked && !pnlOrDdLock && snap.TimeOk && snap.WickOk && snap.EmaStructureOk;
	        
	        // Setups
	        var longSetupOk = baseLongFilter && snap.LongPulledBack && snap.LongReclaimed && snap.LongConfirm;
	        var shortSetupOk = baseShortFilter && snap.ShortPulledBack && snap.ShortReclaimed && snap.ShortConfirm;

	        var flat = Position.MarketPosition == MarketPosition.Flat;

	        var evalEntriesNow =
		        Calculate == Calculate.OnBarClose ||
		        State != State.Realtime ||
		        (Calculate == Calculate.OnEachTick && IsFirstTickOfBar);

	        var canSubmitNow = flat && evalEntriesNow;
	        var wouldSubmitLongNow = canSubmitNow && longSetupOk;
	        var wouldSubmitShortNow = canSubmitNow && shortSetupOk;
	        var wouldSubmitNow = wouldSubmitLongNow || wouldSubmitShortNow;

	        var pos = snap.TrendUp ? "Long" : snap.TrendDown ? "Short" : "None";
	        var confirmFail =
		        snap.TrendUp ? snap.LongConfirmReason : snap.TrendDown ? snap.ShortConfirmReason : "n/a";

	        var trend =
		        snap.TrendUp  ? "trendUp=True" : snap.TrendDown ? "trendDown=True" : "trend=None";
	        
	        // var entrySig = $" idx(sigSignal={sigSignal}@{tSignal:HH:mm:ss}, sigEntry={sigEntry}@{tEntry:HH:mm:ss});";
	        var unrealizedNow = flat ? 0.0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
	        var totalToday = realizedToday + unrealizedNow;

	        var bufferToKill = dailyKill > 0 ? dailyKill + totalToday : 0.0;
	        var dailyProfitRemaining = dailyMaxProfit > 0 ? dailyMaxProfit - totalToday : 0.0;

	        if (!snap.TimeOk)
	        {
		        Print($"Out of time window: {tSignal:yyyy-MM-dd HH:mm:ss} (minFromOpen={minFromOpen}, inMainWindow={snap.InMainWindow}, midBreak={snap.InMidBreak})\n");
		        return;
	        }

	        if (pnlOrDdLock)
	        {
		        Print(string.Format("PnL/DD/Trades Locked: (realizedToday={0:C2}, unrealizedNow={1:C2}, totalToday={2:C2}, dayLocked={3}, tradesToday={4}\n", 
			        realizedToday, 
			        unrealizedNow, 
			        totalToday,
			        dayLocked.ToString(),
			        tradesToday));
		        return;
	        }

	        Print(string.Format(
			    "[DIAG] {0:yyyy-MM-dd HH:mm:ss} - Acc: {1} (Default Contracts: {2} - Effective Contracts: {7})\n" +
			    "  a) Blocks/filters: {15}\n" +
			    // "  b) Wick ticks: {31}\n" +
			    "  b) PB(prev) vs EMAFast: ema={33:F2} distTicks={34:F1} pbLong={35} | ema={37:F2} distTicks={38:F1} pbShort={39}\n" +
			    "  c) adxOk={16}, adx={17:F2}, min={18}, max={19}, volBlocked={20}\n" +
			    "  d) EMA structure: slopeTicks={40:F1} (min={41:F1}, lb={42}) ok={43} | sepTicks={44:F1} (min={45:F1}) | emaCrossover={36}, ok={46} structureOk={47}\n" +
			    "  e) Chop: ok={48} eff={49:0.00} (minEff={50:0.00}, minTicks={12}, upTicks={3}, downTicks={32} lb={51}) reason={52}\n"  +
			    "  f) MaxDailyLoss={21:C0}, LossRemaining={22:C0}, MaxDailyProfit={30:C0}, ProfitRemaining={13:C0}\n" +
			    "  g) Trend Ticks: (range={11:0.00}, rangeUp={14:0.00}, rangeDown={56:0.00}, diff={57:0.00}) \n" +
			    "  h) {23} Entry {24} ({25}, pulledBack={26}, reclaimed={27}, confirm={28}, confirmFail={29})\n" +
			    "-------------------------------------------------------------------------------------------------------\n",
			    tSignal,                    // 0
			    accountName,                // 1
			    Contracts,                  // 2
			    snap.ChopUpTicks,           // 3
			    minFromOpen,                // 4
			    snap.InMainWindow,          // 5
			    snap.InMidBreak,            // 6
			    effContracts,               // 7
			    pnlOrDdLock,                // 8
			    realizedToday,              // 9
			    unrealizedNow,              // 10
			    snap.RangeTicks,            // 11
			    ChopMinRangeTicks,          // 12
			    dailyProfitRemaining,       // 13
			    snap.RangeUpTicks,          // 14
			    snap.Blocks,                // 15
			    snap.AdxOk,                 // 16
			    snap.Adx,                   // 17
			    ADXMin,                     // 18
			    ADXMax,                     // 19
			    snap.VolBlocked,            // 20
			    dailyKill,                  // 21
			    bufferToKill,               // 22
			    pos,                        // 23
			    OkIcon(wouldSubmitNow),     // 24
			    trend,                      // 25
			    snap.TrendUp ? snap.LongPulledBack : snap.ShortPulledBack, // 26
			    snap.TrendUp ? snap.LongReclaimed  : snap.ShortReclaimed,  // 27
			    snap.TrendUp ? snap.LongConfirm    : snap.ShortConfirm,    // 28
			    confirmFail,                // 29
			    dailyMaxProfit,             // 30
			    false, //wickDiag,                   // 31
			    snap.ChopDownTicks,         // 32
			    snap.PbEmaLong,             // 33
			    snap.PbDistLong,            // 34
			    snap.LongPulledBack,        // 35
			    snap.EmaCrossover,          // 36
			    snap.PbEmaShort,            // 37
			    snap.PbDistShort,           // 38
			    snap.ShortPulledBack,       // 39
			    snap.EmaSlopeTicks,         // 40
			    MinEmaSlopeTicks,           // 41
			    EmaSlopeLookbackBars,       // 42
			    snap.EmaSlopeOk,            // 43
			    snap.EmaSepTicks,           // 44
			    MinEmaSeparationTicks,      // 45
			    snap.EmaSepOk,              // 46
			    snap.EmaStructureOk,        // 47
			    // --- NEW: Chop ---
			    snap.ChopOk,                      // 48
			    snap.ChopEff,                     // 49
			    MinChopEfficiency,                // 50
			    snap.ChopLbUsed,                  // 51
			    snap.ChopReason,                  // 52
			    snap.EntryDistCooldownOk,         // 53
			    snap.EntryDistBlockBarsLeft,      // 54
			    snap.EntryDistCooldownReason,     // 55
			    snap.RangeDownTicks,			  // 56
			    snap.RangeTicksDiff 			  // 57
			));
        }
        
        private static string OkIcon(bool ok) { return ok ? "✔" : "✖"; }
        
        // private string BuildWickDiag(int sigClosed)
        // {
	       //  if (WickFilterLookback <= 0)
		      //   return "wick=off";
        //
	       //  // Report the same bars your wick filter checks
	       //  if (WickOnlyPreviousBar)
	       //  {
		      //   GetWickTickStats(sigClosed, out var u, out var l, out var bp);
        //
		      //   var pass = PassesWickFilter(sigClosed);
		      //   return $"wick[b{sigClosed}]=U{u:0.0} L{l:0.0} body%={bp:0.00} pass={pass}";
	       //  }
        //
	       //  var max = Math.Min(WickFilterLookback, CurrentBar - sigClosed);
	       //  if (max <= 0)
		      //   return "wick=insufficient-bars";
        //
	       //  var sb = new StringBuilder();
	       //  sb.Append("wick[");
        //
	       //  for (var i = 0; i < max; i++)
	       //  {
		      //   var barsAgo = sigClosed + i;
        //
		      //   GetWickTickStats(barsAgo, out var u, out var l, out var bp);
		      //   
		      //   var pass = PassesWickFilter(barsAgo);
        //
		      //   if (i > 0) sb.Append(" | ");
		      //   sb.Append($"b{barsAgo}:U{u:0.0} L{l:0.0} body%={bp:0.00} {(pass ? "OK" : "BLOCK")}");
	       //  }
        //
	       //  sb.Append("]");
	       //  return sb.ToString();
        // }

        // private void GetWickTickStats(int barsAgo, out double upperTicks, out double lowerTicks, out double bodyPctOfRange)
        // {
        //     upperTicks = 0;
        //     lowerTicks = 0;
        //     bodyPctOfRange = 0;
        //
        //     var o = Open[barsAgo];
        //     var c = Close[barsAgo];
        //     var h = High[barsAgo];
        //     var l = Low[barsAgo];
        //
        //     var range = Math.Max(h - l, TickSize);
        //     var body = Math.Abs(c - o);
        //
        //     var upperWick = h - Math.Max(o, c);
        //     var lowerWick = Math.Min(o, c) - l;
        //
        //     upperTicks = upperWick / TickSize;
        //     lowerTicks = lowerWick / TickSize;
        //
        //     bodyPctOfRange = body / range;
        // }

        private bool HasWorkingEntryOrder()
        {
            if (Account == null || Instrument == null)
                return false;

            foreach (var o in Account.Orders)
            {
	            if (o?.Instrument == null) continue;
	            if (o.Instrument.FullName != Instrument.FullName) continue;
	            if (!IsEntryOrderName(o.Name)) continue;

	            if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)
		            return true;
            }

            return false;
        }
        
        private EntryEvalSnapshot EvaluateEntrySnapshot(int sigSignal, int sigEntry, int sigClosed, int minFromOpen)
		{
		    var s = new EntryEvalSnapshot
		    {
			    // ---- time gate ----
			    InMainWindow = minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen,
			    InMidBreak = MidBreakEndMin > MidBreakStartMin &&
			                 minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin,
			    HasSessionStart = sessionStart != DateTime.MinValue
		    };

		    s.TimeOk = s.HasSessionStart && s.InMainWindow  && !s.InMidBreak;

		    // ---- locks ----
		    s.DayOk = DayNotLocked();
		    s.TradeCountOk = tradesToday < MaxTradesPerDay;
		    
		    // -- Chop filter ---
		    s.ChopOk = true;
		    s.ChopEff = 1.0;
		    s.ChopLbUsed = 0;
		    s.ChopHasBars = false;
		    s.ChopBypassedAdx = false;
		    s.ChopBypassedEmaSlope = false;
		    s.ChopBlockedByEff = false;
		    s.ChopReason = "chop=off";

		    if (EnableChopFilter)
		    {
			    s.ChopOk = ComputeChopOk(out s.ChopEff, out s.ChopLbUsed, out s.ChopHasBars,
				    out s.ChopBypassedAdx, out s.ChopBypassedEmaSlope, out s.ChopUpTicks, out s.ChopDownTicks, out s.ChopReason);

			    s.ChopBlockedByEff = !s.ChopOk && s.ChopHasBars; // (best-effort flag)
		    }
		    
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

		    //-- Entry Dist Cooldown --
		    s.EntryDistCooldownOk = true;
		    s.EntryDistBlockBarsLeft = 0;

		    if (EntryDistCooldownBars > 0 && _entryDistBlockLastBar >= 0 && CurrentBar < _entryDistBlockLastBar)
		    {
			    s.EntryDistCooldownOk = false;
			    s.EntryDistBlockBarsLeft = _entryDistBlockLastBar - CurrentBar;
		    }
		    
		    // ---- spacing ----
		    s.SpacingOk = true;
		    if (MinMinutesBetweenTrades > 0 && lastFlatExecutionTime != DateTime.MinValue)
		    {
		        var minsSinceLast = Time[sigEntry].Subtract(lastFlatExecutionTime ).TotalMinutes;
		        s.SpacingOk = minsSinceLast >= MinMinutesBetweenTrades;
		    }

		    // ---- wick ----
		    // s.WickOk = RecentBarsAreCleanForEntry(sigClosed);
		    s.WickOk = PassesWickQualityFilter(out var avgWickPct);
		    
		    //-- Market Tradeable/Efficient --//
		    s.IsMarketTradable = IsMarketTradable(out var erNow, out var trendOverride);
		    
		    // ---- ATR ----
		    var atrNow = Math.Max(atr[sigClosed], TickSize);
		    s.AtrTicks = atrNow / TickSize;
		    s.AtrOk = MaxAtrTicks <= 0 || (s.AtrTicks <= MaxAtrTicks);
		    s.VolBlocked = MaxAtrTicks > 0 && !s.AtrOk;

		    // ---- ADX ----
		    s.Adx = adx[sigClosed];
		    s.AdxOk = s.Adx >= ADXMin && s.Adx <= ADXMax;
		    
		    //-- ENTRY DISTANCE ----
		    s.PasseEntryDistance = PassesEntryDistanceFilter(out var priorBarRangeTicks);

		    // ---- EMA structure (DETAILS) ----
		    ComputeEmaStructure(sigClosed,
		        out s.EmaSlopeTicks,
		        out s.EmaSepTicks,
		        out s.EmaSlopeOk,
		        out s.EmaSepOk,
		        out s.EmaCrossover,
		        out s.EmaStructureOk);

		    // ---- trend ----
		    s.TrendUp   = IsTrendUp(sigClosed, out _, out _, out var rangeTicksUpDiff);
		    s.TrendDown = IsTrendDown(sigClosed, out _, out _, out var rangeTicksDownDiff);

		    if (s.TrendUp)
		    {
			    s.RangeTicksDiff = rangeTicksUpDiff;
		    }

		    if (s.TrendDown)
		    {
			    s.RangeTicksDiff = rangeTicksDownDiff;
		    }
		    
		    TrendTicks(30, out _, out _, out s.RangeTicks, out s.RangeUpTicks, out s.RangeDownTicks);

			// ---- setup components (signal bar) ----
		    s.LongPulledBack  = PullbackTouchedFastEmaPrevBar(true, out s.PbEmaLong,  out  s.PbDistLong);
		    s.ShortPulledBack = PullbackTouchedFastEmaPrevBar(false, out  s.PbEmaShort, out s.PbDistShort);
		    
		    s.LongReclaimed  = Close[sigSignal] > emaFast[sigSignal];
		    s.ShortReclaimed = Close[sigSignal] < emaFast[sigSignal];

		    s.LongConfirm  = TrendConfirm(ConfirmBars, true,  sigSignal, out s.LongConfirmReason,  out _);
		    s.ShortConfirm = TrendConfirm(ConfirmBars, false, sigSignal, out s.ShortConfirmReason, out _);

			// Candidate definition = fully formed setup
		    s.LongCandidate  = EnableLongs  && s.TrendUp   && s.LongPulledBack  && s.LongReclaimed  && s.LongConfirm;
		    s.ShortCandidate = EnableShorts && s.TrendDown && s.ShortPulledBack && s.ShortReclaimed && s.ShortConfirm;
		    s.AnyCandidate   = s.LongCandidate || s.ShortCandidate;
		    
			// Momentum filter
			s.HasMomentum = HasMomentum(0, SigClosed(), false, out var momFail, out var er,
			    out var ov, out var bodyT, out var wb, out var clv);

			var blocks = new List<string>(16);

		    if (!s.TimeOk) blocks.Add(s.InMidBreak ? "mid-break" : "outside-time-window");
		    if (!s.DayOk) blocks.Add("dayLocked");
		    if (!s.TradeCountOk) blocks.Add("max-trades");
		    if (!s.SpacingOk) blocks.Add("min-minutes-between");
		    if (!s.EntryDistCooldownOk) blocks.Add("entry-dist-cooldown");
		    if (!s.WickOk) blocks.Add("wick-filter " + $"(avgWickPct={avgWickPct:0.00} lb=4)");
		    if (!s.AtrOk) blocks.Add("atr-too-high");
		    if (!s.AdxOk) blocks.Add("adx-out-of-range");
		    if (EnableChopFilter && !s.ChopOk)
		    {
			    blocks.Add("chop-filter");
		    }

		    if (EnableMomentumFilter && !s.HasMomentum)
		    {
			    blocks.Add($"no-momentum: (reason={momFail} er={er:0.00} ov={ov:0.00} bodyT={bodyT:0.0} wb={wb:0.00} clv={clv:0.00})");
		    }
		    if (!s.IsMarketTradable) blocks.Add("regime-volatile: " + $"(er={erNow:0.00} override={(trendOverride ? "YES" : "NO")} " + $"adx={adx[SigClosed()]:0.0})");
		    if (!s.PasseEntryDistance) blocks.Add($"long-prior-bar-too-large: (rangeTicks={priorBarRangeTicks:0.0} > max={MaxPriorBarRangeTicks} cooldownBars={EntryDistCooldownBars})");


		    if (!s.EmaStructureOk)
		    {
			    blocks.Add("ema-structure");
		    }
		    else
		    {
			    if (!s.EmaSlopeOk) blocks.Add("ema-slope");
			    if (!s.EmaSepOk && !s.EmaCrossover) blocks.Add("ema-separation");
		    }

		    s.Blocks = blocks.Count == 0 ? "none" : string.Join(";", blocks);
		    
		    return s;
		}	
        
        private struct EntryEvalSnapshot
        {
	        public bool TimeOk, InMainWindow, InMidBreak, HasSessionStart, DayOk, TradeCountOk, SpacingOk;
	        public bool WickOk, AtrOk, AdxOk, VolBlocked;
	        public bool TrendUp, TrendDown;

	        public bool EmaSlopeOk, EmaSepOk, EmaCrossover, EmaStructureOk;
	        public double EmaSlopeTicks, EmaSepTicks;

	        public bool LongPulledBack, ShortPulledBack;
	        public bool LongReclaimed, ShortReclaimed;
	        public bool LongConfirm, ShortConfirm;
	        public string LongConfirmReason, ShortConfirmReason;

	        public bool LongCandidate, ShortCandidate, AnyCandidate;

	        public double AtrTicks;
	        public double Adx;
	        public double PbEmaLong, PbDistLong, PbEmaShort, PbDistShort;
	        public bool ChopOk;
	        public double ChopEff;
	        public bool EntryDistCooldownOk;
	        public int EntryDistBlockBarsLeft;
	        
	        public int ChopLbUsed;
	        public bool ChopHasBars;
	        public bool ChopBypassedAdx;
	        public bool ChopBypassedEmaSlope;
	        public bool ChopBlockedByEff;      // true if eff < min AND no bypass
	        public double ChopUpTicks;
	        public double ChopDownTicks;
	        public double RangeUpTicks;
	        public double RangeDownTicks;
	        public double RangeTicks;
	        public double RangeTicksDiff;
	        public string ChopReason;          // single readable reason

	        public bool EntryDistCooldownEnabled;
	        public string EntryDistCooldownReason;
			public bool IsMarketTradable { get; set; }
	        public bool PasseEntryDistance { get; set; }
	        public bool HasMomentum { get; set; }
	        public string Blocks;              // final: "wick-filter;chop-filter;..." etc
        }
    }
}
