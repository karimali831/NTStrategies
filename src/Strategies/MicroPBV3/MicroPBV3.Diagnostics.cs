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
        // =========================
        // End-of-run DIAG reporting
        // =========================
        private int _diagEvalCount = 0;
        private int _diagAcceptedCount = 0;
        private int _diagDeniedCount = 0;

        // reason -> count
        private readonly Dictionary<string, int> _diagDeniedByReason = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private void DiagRecordAccepted()
        {
            _diagAcceptedCount++;
        }

        private void DiagRecordDenied(IEnumerable<string> reasons)
        {
            _diagDeniedCount++;
            if (reasons == null)
                return;

            foreach (var r in reasons)
            {
                if (string.IsNullOrWhiteSpace(r))
                    continue;

                if (_diagDeniedByReason.TryGetValue(r, out var c))
                    _diagDeniedByReason[r] = c + 1;
                else
                    _diagDeniedByReason[r] = 1;
            }
        }

        /// <summary>
        /// Track accepted/denied trade *candidates* (not every bar).
        /// A "candidate" is a fully-formed setup (pullback+reclaim+confirm) that would be taken if all gating filters passed.
        /// </summary>
        private void TrackEntryDecisionForReport(int sigSignal, int sigEntry, int sigClosed, int minFromOpen, bool submitted)
        {
            if (!EnableEndOfRunReport)
                return;

            // Don't spam during optimizations/analyzer runs
            if (IsInStrategyAnalyzer)
                return;

            // Only evaluate once per primary-series evaluation pass while flat
            _diagEvalCount++;

            if (submitted)
            {
                DiagRecordAccepted();
                return;
            }

            var snap = EvaluateEntrySnapshot(sigSignal, sigEntry, sigClosed, minFromOpen);

            if (!snap.AnyCandidate)
	            return;

            // Entry distance (approx: use same trigger logic)
            var distOk = true;
            if (MaxEntryDistFromEmaFastTicks > 0)
            {
	            var buf = TickSize;
	            if (snap.LongCandidate)
	            {
		            var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sigSignal] + buf, High[sigSignal] + buf));
		            var trigger = NormalizeBuyStopPrice(rawTrigger);
		            distOk = PassesEntryDistanceFilter(trigger, sigSignal, out _);
	            }
	            else if (snap.ShortCandidate)
	            {
		            var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Min(Close[sigSignal] - buf, Low[sigSignal] - buf));
		            var trigger = NormalizeSellStopPrice(rawTrigger);
		            distOk = PassesEntryDistanceFilter(trigger, sigSignal, out _);
	            }
            }
            

            // PnL locks (evaluate without side effects)
            var dailyKillOk = true;
            var dailyProfitOk = true;
            var consistencyOk = true;
            var dailyKill = GetDailyKillLimitUsd();
            var dailyProfit = GetDailyProfitLimitUsd();
            var realizedToday = GetRealizedToday();
            var totalToday = GetTotalTodayPnlIncludingOpen();
            if (dailyKill > 0 && totalToday <= -dailyKill)
                dailyKillOk = false;
            if (dailyProfit > 0 && realizedToday >= dailyProfit)
                dailyProfitOk = false;
            // consistency: reuse your existing logic without locking
            var pct = GetConsistencyPct();
            if (pct > 0)
            {
                var profitBeforeToday = GetProfitBeforeToday();
                if (profitBeforeToday > 0 && realizedToday > 0)
                {
                    var maxToday = (pct / (1.0 - pct)) * profitBeforeToday;
                    if (realizedToday > maxToday)
                        consistencyOk = false;
                }
            }

            // Working entry order blocks (you already treat this as a block in DIAG)
            var noWorkingEntryOrder = !HasWorkingEntryOrder();

            // Build denial reasons (multi)
            var reasons = new List<string>(8);
            if (!snap.TimeOk) reasons.Add((MidBreakEndMin > MidBreakStartMin && minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin) ? "mid-break" : "outside-time-window");
            if (!snap.DayOk) reasons.Add("dayLocked");
            if (!snap.TradeCountOk) reasons.Add("max-trades");
            if (!snap.SpacingOk) reasons.Add("min-minutes-between");
            if (!snap.WickOk) reasons.Add("wick-filter");
            if (!snap.AtrOk) reasons.Add("atr-too-high");

			// EMA structure reasons
			if (!snap.EmaStructureOk) reasons.Add("ema-structure");
			if (!snap.EmaSlopeOk)     reasons.Add("ema-slope");
			if (!snap.EmaSepOk)       reasons.Add("ema-separation");

            if (!snap.AdxOk) reasons.Add("adx-out-of-range");
            if (!distOk) reasons.Add("ema-distance");
			// dailyKillOk/dailyProfitOk/consistencyOk
            if (!dailyKillOk) reasons.Add("max-daily-loss");
            if (!dailyProfitOk) reasons.Add("daily-profit");
            if (!consistencyOk) reasons.Add("consistency");
            if (!noWorkingEntryOrder) reasons.Add("entry-order-working");
            
            // Chop zone
            if (!snap.ChopOk) reasons.Add("chop-filter");

            if (reasons.Count == 0) reasons.Add("other");

            DiagRecordDenied(reasons);
        }

        private void PrintEndOfRunReport()
        {
            if (!EnableEndOfRunReport)
                return;

            if (IsInStrategyAnalyzer)
                return;

            try
            {
                Print("================================================================================");
                Print($"[DIAG REPORT] {Name}  evals={_diagEvalCount} accepted={_diagAcceptedCount} denied={_diagDeniedCount}");

                if (_diagDeniedCount <= 0)
                {
                    Print("[DIAG REPORT] No denied candidates recorded.");
                    Print("================================================================================");
                    return;
                }

                // sort reasons by count desc
                foreach (var kv in _diagDeniedByReason.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                {
                    Print($"[DIAG REPORT]   denied: {kv.Value,5}  reason={kv.Key}");
                }

                Print("================================================================================");
            }
            catch (Exception ex)
            {
                if (DebugMode)
                    Print("[DIAG REPORT] error: " + ex.Message);
            }
        }

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
	        
	        var tradeCountLock = tradesToday >= MaxTradesPerDay;
	        var pnlOrDdLock = !DayNotLocked() || tradeCountLock;

	        // Wick filter should match entry gating (use sigClosed)
	        var wickDiag = BuildWickDiag(sigClosed);
	        
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

	        var notes = "";
	        if (!snap.WickOk)                notes += "wick-filter;";
	        if (!snap.HasSessionStart)       notes += "no-session-start;";
	        if (!snap.InMainWindow)          notes += "outside-main-window;";
	        if (snap.InMidBreak)             notes += "mid-break;";
	        if (snap.VolBlocked)             notes += "atr-too-high;";
	        if (!snap.AdxOk)                 notes += "adx-out-of-range;";
	        if (!DayNotLocked())             notes += "dayLocked;";
	        if (tradeCountLock)              notes += "max-trades-reached;";
	        if (!flat)                       notes += "position-open;";
	        if (!evalEntriesNow)             notes += "entry-gated-not-eval-now;";
	        if (HasWorkingEntryOrder())      notes += "entry-order-working;";
	        if (!snap.EmaStructureOk)        notes += "ema-structure;";
	        if (!snap.EmaSlopeOk)            notes += "ema-slope;";
	        if (!snap.EmaSepOk)              notes += "ema-separation;";
	        if (!snap.ChopOk)                notes += "chop-filter;";
	        notes += $" idx(sigSignal={sigSignal}@{tSignal:HH:mm:ss}, sigEntry={sigEntry}@{tEntry:HH:mm:ss});";

	        var unrealizedNow = flat ? 0.0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
	        var totalToday = realizedToday + unrealizedNow;

	        var bufferToKill = dailyKill > 0 ? dailyKill + totalToday : 0.0;
	        var dailyProfitRemaining = dailyMaxProfit > 0 ? (dailyMaxProfit - totalToday) : 0.0;

	        if (!snap.TimeOk)
	        {
		        Print($"Out of time window: {tSignal:yyyy-MM-dd HH:mm:ss} (minFromOpen={minFromOpen}, inMainWindow={snap.InMainWindow}, midBreak={snap.InMidBreak})\n");
		        return;
	        }

	        Print(string.Format(
			    "[DIAG] {0:yyyy-MM-dd HH:mm:ss} - Acc: {1} (Default Contracts: {2} - Effective Contracts: {7})\n" +
			    "  a) PnL/DD/Trades Lock: {8}  (realizedToday={9:C2}, unrealizedNow={10:C2}, totalToday={11:C2}, dayLocked={12}, tradesToday={14})\n" +
			    "  b) Other blocks/filters: {15}\n" +
			    "  c) Wick ticks: {31}\n" +
			    "  d) PB(prev) vs EMAFast: ema={33:F2} distTicks={34:F1} pbLong={35} | ema={37:F2} distTicks={38:F1} pbShort={39}\n" +
			    "  e) adxOk={16}, adx={17:F2}, min={18}, max={19}, volBlocked={20}\n" +
			    "  f) EMA structure: slopeTicks={40:F1} (min={41:F1}, lb={42}) ok={43} | sepTicks={44:F1} (min={45:F1}) | emaCrossover={36}, ok={46} structureOk={47}\n" +
			    "  g) Chop: ok={48} eff={49:0.00} (min={50:0.00}, lb={51})\n" +
			    "  h) MaxDailyLoss={21:C0}, LossRemaining={22:C0}, MaxDailyProfit={30:C0}, ProfitRemaining={13:C0}\n" +
			    "  i) {23} Entry {24} ({25}, pulledBack={26}, reclaimed={27}, confirm={28}, confirmFail={29})\n" +
			    "-------------------------------------------------------------------------------------------------------\n",
			    tSignal,                    // 0
			    accountName,                // 1
			    Contracts,                  // 2
			    snap.TimeOk,                // 3 (unused but kept)
			    minFromOpen,                // 4
			    snap.InMainWindow,          // 5
			    snap.InMidBreak,            // 6
			    effContracts,               // 7
			    pnlOrDdLock,                // 8
			    realizedToday,              // 9
			    unrealizedNow,              // 10
			    totalToday,                 // 11
			    dayLocked.ToString(),       // 12
			    dailyProfitRemaining,       // 13
			    tradesToday,                // 14
			    notes,                      // 15
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
			    wickDiag,                   // 31
			    false,                      // 32 (still unused in your output)
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
			    snap.ChopOk,                 // 48
			    snap.ChopEff,                // 49
			    MinChopEfficiency,           // 50
			    ChopLookbackBars            // 51
			));
        }
        
        private static string OkIcon(bool ok) { return ok ? "✔" : "✖"; }
        
        private string BuildWickDiag(int sigClosed)
        {
	        if (WickFilterLookback <= 0)
		        return "wick=off";

	        // Report the same bars your wick filter checks
	        if (WickOnlyPreviousBar)
	        {
		        GetWickTickStats(sigClosed, out var u, out var l, out var bp);

		        var pass = PassesWickFilter(sigClosed);
		        return $"wick[b{sigClosed}]=U{u:0.0} L{l:0.0} body%={bp:0.00} pass={pass}";
	        }

	        var max = Math.Min(WickFilterLookback, CurrentBar - sigClosed);
	        if (max <= 0)
		        return "wick=insufficient-bars";

	        var sb = new StringBuilder();
	        sb.Append("wick[");

	        for (var i = 0; i < max; i++)
	        {
		        var barsAgo = sigClosed + i;

		        GetWickTickStats(barsAgo, out var u, out var l, out var bp);
		        
		        var pass = PassesWickFilter(barsAgo);

		        if (i > 0) sb.Append(" | ");
		        sb.Append($"b{barsAgo}:U{u:0.0} L{l:0.0} body%={bp:0.00} {(pass ? "OK" : "BLOCK")}");
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
		    
		    //-- Chop filter ---
		    s.ChopOk = true;
		    s.ChopEff = 1.0;
		    if (EnableChopFilter)
		    {
			    s.ChopOk = ComputeChopOk(sigClosed, out s.ChopEff);
		    }

		    // ---- spacing ----
		    s.SpacingOk = true;
		    if (MinMinutesBetweenTrades > 0 && lastFlatExecutionTime != DateTime.MinValue)
		    {
		        var minsSinceLast = Time[sigEntry].Subtract(lastFlatExecutionTime ).TotalMinutes;
		        s.SpacingOk = minsSinceLast >= MinMinutesBetweenTrades;
		    }

		    // ---- wick ----
		    s.WickOk = RecentBarsAreCleanForEntry(sigClosed);

		    // ---- ATR ----
		    var atrNow = Math.Max(atr[sigClosed], TickSize);
		    s.AtrTicks = atrNow / TickSize;
		    s.AtrOk = MaxAtrTicks <= 0 || (s.AtrTicks <= MaxAtrTicks);
		    s.VolBlocked = MaxAtrTicks > 0 && !s.AtrOk;

		    // ---- ADX ----
		    s.Adx = adx[sigClosed];
		    s.AdxOk = s.Adx >= ADXMin && s.Adx <= ADXMax;

		    // ---- EMA structure (DETAILS) ----
		    ComputeEmaStructure(sigClosed,
		        out s.EmaSlopeTicks,
		        out s.EmaSepTicks,
		        out s.EmaSlopeOk,
		        out s.EmaSepOk,
		        out s.EmaCrossover,
		        out s.EmaStructureOk);

		    // ---- trend ----
		    s.TrendUp   = IsTrendUp(sigClosed, out _, out _);
		    s.TrendDown = IsTrendDown(sigClosed, out _, out _);

			// ---- setup components (signal bar) ----
		    s.LongPulledBack  = PullbackTouchedFastEmaPrevBar(true,  sigSignal, out var pbEmaLong,  out var pbDistLong);
		    s.ShortPulledBack = PullbackTouchedFastEmaPrevBar(false, sigSignal, out var pbEmaShort, out var pbDistShort);

		    s.PbEmaLong   = pbEmaLong;
		    s.PbDistLong  = pbDistLong;
		    s.PbEmaShort  = pbEmaShort;
		    s.PbDistShort = pbDistShort;

		    s.LongReclaimed  = Close[sigSignal] > emaFast[sigSignal];
		    s.ShortReclaimed = Close[sigSignal] < emaFast[sigSignal];

		    s.LongConfirm  = TrendConfirm(ConfirmBars, true,  sigSignal, out s.LongConfirmReason,  out _);
		    s.ShortConfirm = TrendConfirm(ConfirmBars, false, sigSignal, out s.ShortConfirmReason, out _);

			// Candidate definition = fully formed setup
		    s.LongCandidate  = EnableLongs  && s.TrendUp   && s.LongPulledBack  && s.LongReclaimed  && s.LongConfirm;
		    s.ShortCandidate = EnableShorts && s.TrendDown && s.ShortPulledBack && s.ShortReclaimed && s.ShortConfirm;
		    s.AnyCandidate   = s.LongCandidate || s.ShortCandidate;

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
        }
    }
}
