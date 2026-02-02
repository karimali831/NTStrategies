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

            // --- Build a "candidate" snapshot ---
            // Time gate
            var inMainWindow = minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen;
            var inMidBreak = (MidBreakEndMin > MidBreakStartMin) && (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin);
            var timeOk = (sessionStart != DateTime.MinValue) && inMainWindow && !inMidBreak;

            // locks
            var dayOk = DayNotLocked();
            var tradeCountOk = tradesToday < MaxTradesPerDay;

            // min time between trades
            var spacingOk = true;
            if (MinMinutesBetweenTrades > 0 && lastEntryExecutionTime != DateTime.MinValue)
            {
                var minsSinceLast = Time[sigEntry].Subtract(lastEntryExecutionTime).TotalMinutes;
                spacingOk = minsSinceLast >= MinMinutesBetweenTrades;
            }

            // basic filters
            var wickOk = RecentBarsAreCleanForEntry(sigClosed);
            var atrNow = Math.Max(atr[sigClosed], TickSize);
            var atrTicks = atrNow / TickSize;
            var atrOk = MaxAtrTicks <= 0 || atrTicks <= MaxAtrTicks;

            // EMA structure
            ComputeEmaStructure(sigClosed, out var slopeOk, out var sepOk, out var structureOk);

            // ADX
            var adxNow = adx[sigClosed];
            var adxOk = adxNow >= ADXMin && adxNow <= ADXMax;

            // Trend
            // var trendUp = Close[sigSignal] > emaFast[sigSignal] && Close[sigSignal] > emaSlow[sigSignal];
            // var trendDown = Close[sigSignal] < emaFast[sigSignal] && Close[sigSignal] < emaSlow[sigSignal];
            var trendUp   = IsTrendUp(sigClosed, out _, out _);
            var trendDown = IsTrendDown(sigClosed, out _, out _);

            // Setup components
            double pbEma, pbDist;
            var longPulledBack = PullbackTouchedFastEmaPrevBar(true, sigSignal, out pbEma, out pbDist);
            var shortPulledBack = PullbackTouchedFastEmaPrevBar(false, sigSignal, out pbEma, out pbDist);
            var longReclaimed = Close[sigSignal] > emaFast[sigSignal];
            var shortReclaimed = Close[sigSignal] < emaFast[sigSignal];

            var longConfirm = TrendConfirm(ConfirmBars, true, sigSignal, out _, out _);
            var shortConfirm = TrendConfirm(ConfirmBars, false, sigSignal, out _, out _);

            var longCandidate = EnableLongs && trendUp && longPulledBack && longReclaimed && longConfirm;
            var shortCandidate = EnableShorts && trendDown && shortPulledBack && shortReclaimed && shortConfirm;
            var anyCandidate = longCandidate || shortCandidate;

            if (!anyCandidate)
                return; // we only count DENIALS for real candidates

            // Entry distance (approx: use same trigger logic)
            var distOk = true;
            if (MaxEntryDistFromEmaFastTicks > 0)
            {
                var buf = TickSize;
                if (longCandidate)
                {
                    var rawTrigger = Instrument.MasterInstrument.RoundToTickSize(Math.Max(Close[sigSignal] + buf, High[sigSignal] + buf));
                    var trigger = NormalizeBuyStopPrice(rawTrigger);
                    distOk = PassesEntryDistanceFilter(trigger, sigSignal, out _);
                }
                else if (shortCandidate)
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
            if (!timeOk) reasons.Add(inMidBreak ? "mid-break" : "outside-time-window");
            if (!dayOk) reasons.Add("dayLocked");
            if (!tradeCountOk) reasons.Add("max-trades" );
            if (!spacingOk) reasons.Add("min-minutes-between");
            if (!wickOk) reasons.Add("wick-filter");
            if (!atrOk) reasons.Add("atr-too-high");
            if (!structureOk) reasons.Add("ema-structure");
            if (structureOk && !slopeOk) reasons.Add("ema-slope");
            if (structureOk && !sepOk) reasons.Add("ema-separation");
            if (!adxOk) reasons.Add("adx-out-of-range");
            if (!distOk) reasons.Add("ema-distance");
            if (!dailyKillOk) reasons.Add("max-daily-loss");
            if (!dailyProfitOk) reasons.Add("daily-profit");
            if (!consistencyOk) reasons.Add("consistency");
            if (!noWorkingEntryOrder) reasons.Add("entry-order-working");

            if (reasons.Count == 0)
            {
                // This means candidate existed but got blocked by something we didn't classify.
                reasons.Add("other");
            }

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

	        var effContracts = GetEffectiveContractsToday(sigSignal);

	        var diagTime = Time[sigEntry];
	        if ((diagTime - lastDiagTime).TotalSeconds < diagThrottleSeconds)
		        return;

	        lastDiagTime = diagTime;

	        var accountName = Account != null ? Account.Name : "N/A";

	        var realizedToday = GetRealizedToday();
	        var dailyKill = GetDailyKillLimitUsd();
	        var dailyMaxProfit = GetDailyProfitLimitUsd();

	        var hasSessionStart = sessionStart != DateTime.MinValue;
	        var inMainWindow = minFromOpen >= MinMinutesFromOpen && minFromOpen <= MaxMinutesFromOpen;
	        var inMidBreak = (MidBreakEndMin > MidBreakStartMin) &&
	                         (minFromOpen >= MidBreakStartMin && minFromOpen < MidBreakEndMin);
	        var timeWindowOk = hasSessionStart && inMainWindow && !inMidBreak;

	        var tradeCountLock = tradesToday >= MaxTradesPerDay;
	        var pnlOrDdLock = !DayNotLocked() || tradeCountLock;

	        var atrNow = Math.Max(atr[sigClosed], TickSize);
	        var atrTicksNow = atrNow / TickSize;
	        var volBlocked = MaxAtrTicks > 0 && atrTicksNow > MaxAtrTicks;

	        // Wick filter should match entry gating (use sigClosed)
	        var wickOk = RecentBarsAreCleanForEntry(sigClosed);
	        var wickDiag = BuildWickDiag(sigClosed);

	        var adxNow = adx[sigClosed];
	        var adxOk = adxNow >= ADXMin && adxNow <= ADXMax;

	        var tSignal = Time[sigSignal];
	        var tEntry = Time[sigEntry];

	        // Pullback (previous closed bar context)

	        var longPulledBack = PullbackTouchedFastEmaPrevBar(true, sigSignal, out var pbEmaLong, out var pbDistLong);
	        var shortPulledBack = PullbackTouchedFastEmaPrevBar(false, sigSignal, out var pbEmaShort, out var pbDistShort);

	        // Trend on signal bar
	        var trendUp   = IsTrendUp(sigClosed, out _, out _);
	        var trendDown = IsTrendDown(sigClosed, out _, out _);

	        var longReclaimed = Close[sigSignal] > emaFast[sigSignal];
	        var shortReclaimed = Close[sigSignal] < emaFast[sigSignal];

	        // Confirm on signal bar
	        var longConfirm = TrendConfirm(ConfirmBars, true, sigSignal, out var longConfirmReason, out _);
	        var shortConfirm =
		        TrendConfirm(ConfirmBars, false, sigSignal, out var shortConfirmReason, out _);

	        // Base filters
	        var baseLongFilter = EnableLongs && trendUp && adxOk && !volBlocked && !pnlOrDdLock && timeWindowOk &&
	                             wickOk;
	        var baseShortFilter = EnableShorts && trendDown && adxOk && !volBlocked && !pnlOrDdLock && timeWindowOk &&
	                              wickOk;

	        // Setups
	        var longSetupOk = baseLongFilter && longPulledBack && longReclaimed && longConfirm;
	        var shortSetupOk = baseShortFilter && shortPulledBack && shortReclaimed && shortConfirm;

	        var flat = Position.MarketPosition == MarketPosition.Flat;

	        var evalEntriesNow =
		        Calculate == Calculate.OnBarClose ||
		        State != State.Realtime ||
		        (Calculate == Calculate.OnEachTick && IsFirstTickOfBar);

	        var canSubmitNow = flat && evalEntriesNow;

	        var wouldSubmitLongNow = canSubmitNow && longSetupOk;
	        var wouldSubmitShortNow = canSubmitNow && shortSetupOk;

	        var wouldSubmitNow = wouldSubmitLongNow || wouldSubmitShortNow;

	        var pos = trendUp ? "Long" : (trendDown ? "Short" : "None");

	        var confirmFail =
		        trendUp ? longConfirmReason : (trendDown ? shortConfirmReason : "n/a");

	        var trend =
		        trendUp ? "trendUp=True" : (trendDown ? "trendDown=True" : "trend=None");

	        var notes = "";
	        if (!wickOk) notes += "wick-filter;";
	        if (!hasSessionStart) notes += "no-session-start;";
	        if (!inMainWindow) notes += "outside-main-window;";
	        if (inMidBreak) notes += "mid-break;";
	        if (volBlocked) notes += "atr-too-high;";
	        if (!adxOk) notes += "adx-out-of-range;";
	        if (!DayNotLocked()) notes += "dayLocked;";
	        if (tradeCountLock) notes += "max-trades-reached;";
	        if (!flat) notes += "position-open;";
	        if (!evalEntriesNow) notes += "entry-gated-not-eval-now;";
	        if (HasWorkingEntryOrder()) notes += "entry-order-working;";
	        if (string.IsNullOrEmpty(notes)) notes = "none";

	        notes += $" idx(sigSignal={sigSignal}@{tSignal:HH:mm:ss}, sigEntry={sigEntry}@{tEntry:HH:mm:ss});";

	        var unrealizedNow = flat ? 0.0 : Position.GetUnrealizedProfitLoss(PerformanceUnit.Currency);
	        var totalToday = realizedToday + unrealizedNow;

	        var bufferToKill = (dailyKill > 0) ? (dailyKill + totalToday) : 0.0;
	        var dailyProfitRemaining = (dailyMaxProfit > 0) ? (dailyMaxProfit - totalToday) : 0.0;

	        if (!timeWindowOk)
	        {
		        Print(string.Format(
			        "Out of time window: {0:yyyy-MM-dd HH:mm:ss} (minFromOpen={1}, inMainWindow={2}, midBreak={3})\n",
			        Time[sigEntry], minFromOpen, inMainWindow, inMidBreak));
		        return;
	        }

	        Print(string.Format(
		        "[DIAG] {0:yyyy-MM-dd HH:mm:ss} - Acc: {1} (Default Contracts: {2} - Effective Contracts: {7})\n" +
		        "  a) PnL/DD/Trades Lock: {8}  (realizedToday={9:C2}, unrealizedNow={10:C2}, totalToday={11:C2}, dayLocked={12}, tradesToday={14})\n" +
		        "  b) Other blocks/filters: {15}\n" +
		        "  b2) Wick ticks: {31}\n" +
		        "  b3) PB(prev) vs EMAFast: " +
		        "touchBar={32:HH:mm:ss} ema={33:F2} distTicks={34:F1} pbLong={35} | " +
		        "touchBar={36:HH:mm:ss} ema={37:F2} distTicks={38:F1} pbShort={39}\n" +
		        "  c) adxOk={16}, adx={17:F2}, min={18}, max={19}, volBlocked={20}\n" +
		        "  d) MaxDailyLoss={21:C0}, LossRemaining={22:C0}, MaxDailyProfit={30:C0}, ProfitRemaining={13:C0}\n" +
		        "  e) {23} Entry {24} ({25}, pulledBack={26}, reclaimed={27}, confirm={28}, confirmFail={29})\n" +
		        "-------------------------------------------------------------------------------------------------------\n",
		        Time[sigEntry], // 0
		        accountName, // 1
		        Contracts, // 2
		        timeWindowOk, // 3 (kept for stability)
		        minFromOpen, // 4
		        inMainWindow, // 5
		        inMidBreak, // 6
		        effContracts, // 7
		        pnlOrDdLock, // 8
		        realizedToday, // 9
		        unrealizedNow, // 10
		        totalToday, // 11
		        dayLocked.ToString(), // 12
		        dailyProfitRemaining, // 13
		        tradesToday, // 14
		        notes, // 15
		        adxOk, // 16
		        adxNow, // 17
		        ADXMin, // 18
		        ADXMax, // 19
		        volBlocked, // 20
		        dailyKill, // 21
		        bufferToKill, // 22
		        pos, // 23
		        OkIcon(wouldSubmitNow), // 24
		        trend, // 25
		        trendUp ? longPulledBack : shortPulledBack, // 26
		        trendUp ? longReclaimed : shortReclaimed, // 27
		        trendUp ? longConfirm : shortConfirm, // 28
		        confirmFail, // 29
		        dailyMaxProfit, // 30
		        wickDiag, // 31

		        // ---- PREVIOUS-BAR PULLBACK DIAG ----
		        Time[sigSignal], // 32 long touch bar time
		        pbEmaLong, // 33
		        pbDistLong, // 34
		        longPulledBack, // 35

		        Time[sigSignal], // 36 short touch bar time
		        pbEmaShort, // 37
		        pbDistShort, // 38
		        shortPulledBack // 39
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
		        double u, l, bp;
		        GetWickTickStats(sigClosed, out u, out l, out bp);

		        bool pass = PassesWickFilter(sigClosed);

		        return $"wick[b{sigClosed}]=U{u:0.0} L{l:0.0} body%={bp:0.00} pass={pass}";
	        }

	        var max = Math.Min(WickFilterLookback, CurrentBar - sigClosed);
	        if (max <= 0)
		        return "wick=insufficient-bars";

	        var sb = new StringBuilder();
	        sb.Append("wick[");

	        for (int i = 0; i < max; i++)
	        {
		        int barsAgo = sigClosed + i;

		        double u, l, bp;
		        GetWickTickStats(barsAgo, out u, out l, out bp);

		        bool pass = PassesWickFilter(barsAgo);

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

            try
            {
                foreach (Order o in Account.Orders)
                {
                    if (o == null) continue;
                    if (o.Instrument == null) continue;
                    if (o.Instrument.FullName != Instrument.FullName) continue;
                    if (!IsEntryOrderName(o.Name)) continue;

                    if (o.OrderState == OrderState.Working || o.OrderState == OrderState.Accepted || o.OrderState == OrderState.PartFilled)
                        return true;
                }
            }
            catch { }

            return false;
        }
    }
}
