// Overnight Structure Portfolio — research-first, independent of Overnight Edge.
// 5-minute primary chart, 1-minute completed-bar signals, 1-tick Playback entry.
// Chart and data must be configured to ET. No time-zone conversion is done.
using System;
using System.Collections.Generic;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightStructurePortfolio : Strategy
    {
        private const string Version = "1.0.0-research1";
        private const int ContextBip = 0;
        private const int SignalBip = 1;
        private const int ExecutionBip = 2;
        private const string OnlSignal = "ONL-COMP-BREAK-S";
        private const string OnhSignal = "ONH-DISP-BREAK-L";
        private const string PdcSignal = "PDC-TREND-RECLAIM-L";
        private const string TimeExit = "STRUCT-TIME";
        private const string FlatExit = "STRUCT-FLAT";

        private sealed class Candidate
        {
            public string Signal;
            public string Family;
            public bool Long;
            public DateTime Time;
            public double Level;
            public double Open;
            public double High;
            public double Low;
            public double Close;
            public int StopTicks;
            public int TargetTicks;
            public string FilterReason;
            public bool Qualified;
            public double CompressionThreshold;
            public int HistoryCount;
        }

        private NinjexPremarketRangeEngine onEngine;
        private NinjexPremarketRangeEngine pmEngine;
        private EMA slowEma;
        private EMA fastEma;
        private ATR atr5m;

        private DateTime onDate = Core.Globals.MinDate;
        private DateTime pmDate = Core.Globals.MinDate;
        private double onHigh = double.NaN, onLow = double.NaN;
        private double pmHigh = double.NaN, pmLow = double.NaN;
        private int onBarCount, pmBarCount;
        private bool onReady, pmReady;
        private DateTime lastContextEnd = Core.Globals.MinDate;
        private double emaSlow = double.NaN, emaFast = double.NaN;
        private double emaSlowSlopeTicks = double.NaN, atrTicks = double.NaN;

        // The history is only populated by earlier, *completed* premarket
        // sessions. The current session never contributes to its own percentile.
        private readonly Queue<double> priorPmWidths = new Queue<double>();
        private DateTime compressionDate = Core.Globals.MinDate;
        private double compressionThreshold = double.NaN;
        private bool compressedToday;
        private int compressionHistoryCount;

        private DateTime rthDate = Core.Globals.MinDate;
        private double rthOpen = double.NaN;
        private double rthLastClose = double.NaN;
        private int rthMinuteBars;
        private double priorRthClose = double.NaN;
        private DateTime priorRthDate = Core.Globals.MinDate;
        private bool onlAttempted, onhAttempted;
        private DateTime lastSignalDate = Core.Globals.MinDate;
        private double previousRthMinuteClose = double.NaN;
        private readonly List<Candidate> evaluatedThisBar = new List<Candidate>();

        private Candidate pending;
        private DateTime pendingExpires = Core.Globals.MinDate;
        private bool entrySubmitted, manualExitPending;
        private Order workingEntry;
        private string activeSignal = string.Empty;
        private bool activeLong, activeTrade;
        private double entryPriceTotal, exitGross, entryAverage;
        private int entryFillQty, exitFillQty;
        private DateTime entryTime = Core.Globals.MinDate;
        private DateTime deadline = Core.Globals.MinDate;
        private double mfeTicks, maeTicks;
        private int activeStopTicks, activeTargetTicks;
        private DateTime accountingDate = Core.Globals.MinDate;
        private int tradesToday, winnersToday, lossesToday;
        private double dailyGross, cumulativeGross;
        private double equityPeak, maxDrawdown;
        private DateTime maxDrawdownAt = Core.Globals.MinDate;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex Overnight Structure Portfolio";
                Description = "Research first: relative-compression ONL short, ONH displacement long and trend-confirmed PDC long. Playback-only real orders.";
                Calculate = Calculate.OnEachTick;
                EntryHandling = EntryHandling.AllEntries;
                EntriesPerDirection = 1;
                IsExitOnSessionCloseStrategy = false;
                StartBehavior = StartBehavior.WaitUntilFlat;
                RealtimeErrorHandling = RealtimeErrorHandling.StopCancelClose;
                StopTargetHandling = StopTargetHandling.PerEntryExecution;
                BarsRequiredToTrade = 30;
                IsInstantiatedOnEachOptimizationIteration = true;

                OvernightStartTime = 180000;
                PremarketStartTime = 30000;
                MarketOpenTime = 93000;
                EntryStartTime = 93500;
                EntryEndTime = 133000;
                FlattenTime = 160000;
                MinimumOvernightFiveMinuteBars = 180;
                MinimumPremarketFiveMinuteBars = 75;

                EnableOnlBreakdown = true;
                EnableOnhBreakout = true;
                EnablePdcReclaim = true;
                CompressionLookbackSessions = 20;
                CompressionPercentile = 35;
                OnlMaximumMinutesFromOpen = 150;
                OnlStopTicks = 30;
                OnlTargetTicks = 75;
                RequireOnlOpeningMomentum = true;
                OnhMaximumMinutesFromOpen = 150;
                OnhMinBodyTicks = 10;
                OnhStopTicks = 15;
                OnhTargetTicks = 40;
                RequireOnhOpeningDirection = true;
                PdcMaximumMinutesFromOpen = 240;
                PdcMaximumSignalRangeTicks = 30;
                PdcMinimumSlowEmaSlopeTicks = 0;
                PdcStopTicks = 25;
                PdcTargetTicks = 40;
                SlowEmaPeriod = 21;
                FastEmaPeriod = 9;
                AtrPeriod = 14;

                OrderQuantity = 1;
                MaximumTradesPerDay = 3;
                MaximumWinnersPerDay = 2;
                MaximumLossesPerDay = 2;
                MaximumHoldMinutes = 60;
                EnablePlaybackOrders = true;
                EnableHistoricalBacktestOrders = false;
                EnableResearchExport = true;
                EnableDiagnosticLogging = true;
            }
            else if (State == State.Configure)
            {
                AddDataSeries(BarsPeriodType.Minute, 1);
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                onEngine = new NinjexPremarketRangeEngine();
                pmEngine = new NinjexPremarketRangeEngine();
                slowEma = EMA(Closes[ContextBip], SlowEmaPeriod);
                fastEma = EMA(Closes[ContextBip], FastEmaPeriod);
                atr5m = ATR(Closes[ContextBip], AtrPeriod);
                InitializeStructureResearch();
                LogEvent(DateTime.Now, "READY Version=" + Version + " Signals=ONL,ONH,PDC ResearchOnly=true");
            }
            else if (State == State.Terminated)
            {
                FinalizeStructureSession();
                DisposeStructureResearch();
            }
        }

        protected override void OnBarUpdate()
        {
            if (BarsInProgress == ContextBip)
            {
                ProcessCompletedContext();
                return;
            }

            if (BarsInProgress == SignalBip)
            {
                ProcessCompletedMinute();
                return;
            }

            if (BarsInProgress == ExecutionBip)
                ProcessExecutionTick();
        }

        private void ProcessCompletedContext()
        {
            if (CurrentBars[ContextBip] < 1 || !IsFirstTickOfBar)
                return;

            var end = Times[ContextBip][1];
            var high = Highs[ContextBip][1];
            var low = Lows[ContextBip][1];

            var onFinished = onEngine.ProcessCompletedBar(
                end, high, low, KeyLevelsMode.Overnight,
                PremarketStartTime, OvernightStartTime, MarketOpenTime);
            var pmFinished = pmEngine.ProcessCompletedBar(
                end, high, low, KeyLevelsMode.Premarket,
                PremarketStartTime, OvernightStartTime, MarketOpenTime);

            if (onFinished && onEngine.IsRangeComplete)
            {
                onDate = onEngine.LatestRangeDate;
                onHigh = onEngine.LatestHigh;
                onLow = onEngine.LatestLow;
                onBarCount = onEngine.RangeBarCount;
                onReady = Valid(onHigh) && Valid(onLow) && onHigh > onLow
                    && onBarCount >= MinimumOvernightFiveMinuteBars;
                LogEvent(end, "ON_FINAL Date=" + onDate.ToString("yyyy-MM-dd")
                    + " High=" + Num(onHigh) + " Low=" + Num(onLow)
                    + " Count=" + onBarCount + " Ready=" + onReady);
            }

            if (pmFinished && pmEngine.IsRangeComplete)
            {
                pmDate = pmEngine.LatestRangeDate;
                pmHigh = pmEngine.LatestHigh;
                pmLow = pmEngine.LatestLow;
                pmBarCount = pmEngine.RangeBarCount;
                pmReady = Valid(pmHigh) && Valid(pmLow) && pmHigh > pmLow
                    && pmBarCount >= MinimumPremarketFiveMinuteBars;

                // Calculate before enqueuing today's PM width — no lookahead.
                var width = Valid(pmHigh) && Valid(pmLow) && TickSize > 0
                    ? (pmHigh - pmLow) / TickSize : double.NaN;
                compressionHistoryCount = priorPmWidths.Count;
                compressionThreshold = compressionHistoryCount >= CompressionLookbackSessions
                    ? Percentile(priorPmWidths, CompressionPercentile) : double.NaN;
                compressedToday = pmReady && Valid(compressionThreshold)
                    && width <= compressionThreshold;
                compressionDate = pmDate;

                WriteStructureSessionSnapshot(end, width);
                LogEvent(end, "PM_FINAL Date=" + pmDate.ToString("yyyy-MM-dd")
                    + " WidthTicks=" + Num(width) + " Threshold=" + Num(compressionThreshold)
                    + " Count=" + compressionHistoryCount
                    + " Compressed=" + compressedToday + " Bars=" + pmBarCount);

                if (pmReady && Valid(width))
                {
                    priorPmWidths.Enqueue(width);
                    while (priorPmWidths.Count > CompressionLookbackSessions)
                        priorPmWidths.Dequeue();
                }
            }

            if (CurrentBars[ContextBip] >= Math.Max(30,
                Math.Max(AtrPeriod, Math.Max(SlowEmaPeriod, FastEmaPeriod))) + 2)
            {
                lastContextEnd = end;
                emaSlow = slowEma[1];
                emaFast = fastEma[1];
                atrTicks = TickSize > 0 ? atr5m[1] / TickSize : double.NaN;
                emaSlowSlopeTicks = TickSize > 0
                    ? (slowEma[1] - slowEma[2]) / TickSize : double.NaN;
                WriteContextBar(end, Opens[ContextBip][1], high, low,
                    Closes[ContextBip][1], Volumes[ContextBip][1]);
            }
        }

        private void ProcessCompletedMinute()
        {
            if (CurrentBars[SignalBip] < 2 || !IsFirstTickOfBar)
                return;

            var end = Times[SignalBip][1];
            var open = Opens[SignalBip][1];
            var high = Highs[SignalBip][1];
            var low = Lows[SignalBip][1];
            var close = Closes[SignalBip][1];
            var previous = Closes[SignalBip][2];
            var clock = Clock(end);

            if (rthDate != Core.Globals.MinDate && end.Date > rthDate)
                FinalizeRthReference(end);

            if (clock < MarketOpenTime || clock > FlattenTime)
            {
                WriteMinuteBar(end, open, high, low, close, Volumes[SignalBip][1]);
                return;
            }

            if (rthDate != end.Date)
            {
                FlushStructureDailySummary();
                rthDate = end.Date;
                rthOpen = open;
                rthLastClose = double.NaN;
                rthMinuteBars = 0;
                onlAttempted = false;
                onhAttempted = false;
                previousRthMinuteClose = double.NaN;
                ResetDailyTradingState(end.Date, end);
            }

            rthMinuteBars++;
            rthLastClose = close;
            WriteMinuteBar(end, open, high, low, close, Volumes[SignalBip][1]);

            if (clock < EntryStartTime || clock >= EntryEndTime)
            {
                previousRthMinuteClose = close;
                return;
            }

            // All level context must originate from the current completed
            // session; otherwise no signals. Never use an old contract's levels.
            if (!onReady || onDate != end.Date
                || !pmReady || pmDate != end.Date
                || compressionDate != end.Date
                || lastContextEnd == Core.Globals.MinDate
                || lastContextEnd > end
                || TickSize <= 0)
            {
                previousRthMinuteClose = close;
                return;
            }

            if (pending != null && end > pendingExpires)
            {
                LogEvent(end, "PENDING_EXPIRED " + pending.Signal);
                pending = null;
            }

            evaluatedThisBar.Clear();
            var minutesFromOpen = MinutesFromOpen(clock);
            var referencePrevious = Valid(previousRthMinuteClose)
                ? previousRthMinuteClose : previous;

            // Only the first genuine cross of each overnight boundary is
            // eligible. A rejected/filtered first attempt still consumes it.
            if (!onlAttempted && referencePrevious >= onLow && close < onLow)
            {
                onlAttempted = true;
                var c = NewCandidate(OnlSignal, "ONL Breakdown", false,
                    end, onLow, open, high, low, close, OnlStopTicks, OnlTargetTicks);
                c.FilterReason = !EnableOnlBreakdown ? "ModelDisabled"
                    : minutesFromOpen > OnlMaximumMinutesFromOpen ? "OutsideModelWindow"
                    : !Valid(compressionThreshold) ? "InsufficientPremarketHistory"
                    : !compressedToday ? "PremarketNotCompressed"
                    : RequireOnlOpeningMomentum
                        && !(Valid(rthOpen) && close < rthOpen
                             && close < referencePrevious) ? "NoBearishOpeningMomentum"
                    : string.Empty;
                c.Qualified = string.IsNullOrEmpty(c.FilterReason);
                evaluatedThisBar.Add(c);
            }

            if (!onhAttempted && referencePrevious <= onHigh && close > onHigh)
            {
                onhAttempted = true;
                var c = NewCandidate(OnhSignal, "ONH Breakout", true,
                    end, onHigh, open, high, low, close, OnhStopTicks, OnhTargetTicks);
                c.FilterReason = !EnableOnhBreakout ? "ModelDisabled"
                    : minutesFromOpen > OnhMaximumMinutesFromOpen ? "OutsideModelWindow"
                    : close <= open || (close - open) / TickSize < OnhMinBodyTicks
                        ? "InsufficientBullishDisplacement"
                    : RequireOnhOpeningDirection
                        && !(Valid(rthOpen) && close > rthOpen)
                        ? "NoBullishOpeningDirection"
                    : string.Empty;
                c.Qualified = string.IsNullOrEmpty(c.FilterReason);
                evaluatedThisBar.Add(c);
            }

            if (Valid(priorRthClose) && priorRthDate < end.Date
                && (end.Date - priorRthDate).TotalDays <= 7
                && referencePrevious <= priorRthClose && close > priorRthClose)
            {
                var c = NewCandidate(PdcSignal, "PDC Trend Reclaim", true,
                    end, priorRthClose, open, high, low, close, PdcStopTicks, PdcTargetTicks);
                c.FilterReason = !EnablePdcReclaim ? "ModelDisabled"
                    : minutesFromOpen > PdcMaximumMinutesFromOpen ? "OutsideModelWindow"
                    : (high - low) / TickSize > PdcMaximumSignalRangeTicks
                        ? "SignalBarTooWide"
                    : !Valid(emaSlowSlopeTicks)
                        || emaSlowSlopeTicks < PdcMinimumSlowEmaSlopeTicks
                        ? "SlowEmaSlopeNotBullish"
                    : !Valid(emaFast) || !Valid(emaSlow)
                        ? "MissingCompleted5mContext"
                    : string.Empty;
                c.Qualified = string.IsNullOrEmpty(c.FilterReason);
                evaluatedThisBar.Add(c);
            }

            // Every raw candidate is persisted, including those filtered by
            // regime and those blocked by the portfolio scheduler.
            Candidate selected = null;
            foreach (var c in evaluatedThisBar)
            {
                if (c.Qualified && selected == null)
                    selected = c;
            }
            var block = PortfolioBlockReason(end, false);
            foreach (var c in evaluatedThisBar)
            {
                var outcome = !c.Qualified ? c.FilterReason
                    : block != "" ? block
                    : !Object.ReferenceEquals(c, selected) ? "OtherSignalPriority"
                    : "Selected";
                WriteCandidate(c, outcome);
            }

            if (selected != null && block == "")
            {
                pending = selected;
                pendingExpires = end.AddMinutes(2);
                LogEvent(end, "SIGNAL_ARM " + selected.Signal
                    + " Level=" + Num(selected.Level) + " Stop=" + selected.StopTicks
                    + " Target=" + selected.TargetTicks);
            }

            previousRthMinuteClose = close;
        }

        private void FinalizeRthReference(DateTime now)
        {
            if (rthDate == Core.Globals.MinDate)
                return;

            // A reduced RTH session is accepted when >= 200 completed minutes;
            // a severely incomplete session must not define tomorrow's PDC.
            if (rthMinuteBars >= 200 && Valid(rthLastClose))
            {
                priorRthClose = rthLastClose;
                priorRthDate = rthDate;
            }
            FlushStructureDailySummary();
            rthDate = Core.Globals.MinDate;
        }

        private void ResetDailyTradingState(DateTime date, DateTime now)
        {
            if (accountingDate == date)
                return;
            if (activeTrade)
                LogEvent(now, "WARNING OPEN_TRADE_CROSSES_DATE " + activeSignal);
            accountingDate = date;
            tradesToday = 0;
            winnersToday = 0;
            lossesToday = 0;
            dailyGross = 0;
            pending = null;
            pendingExpires = Core.Globals.MinDate;
            LogEvent(now, "NEW_RTH_DATE " + date.ToString("yyyy-MM-dd"));
        }

        private string PortfolioBlockReason(DateTime time, bool ignoreCurrentPending)
        {
            if (!CanSubmitResearchOrders())
                return "ResearchOnlyNoOrders";
            if ((!ignoreCurrentPending && pending != null)
                || entrySubmitted || manualExitPending
                || activeTrade || Position.MarketPosition != MarketPosition.Flat)
                return "PositionOrOrderActive";
            if (MaximumTradesPerDay > 0 && tradesToday >= MaximumTradesPerDay)
                return "MaximumTradesPerDay";
            if (MaximumWinnersPerDay > 0 && winnersToday >= MaximumWinnersPerDay)
                return "MaximumWinnersPerDay";
            if (MaximumLossesPerDay > 0 && lossesToday >= MaximumLossesPerDay)
                return "MaximumLossesPerDay";
            return string.Empty;
        }

        private bool CanSubmitResearchOrders()
        {
            if (State == State.Historical)
                return EnableHistoricalBacktestOrders;
            if (State != State.Realtime || !EnablePlaybackOrders)
                return false;
            try
            {
                var name = Account == null ? "" : Account.Name ?? "";
                if (name.StartsWith("Playback", StringComparison.OrdinalIgnoreCase))
                    return true;
                var connection = Account != null && Account.Connection != null
                    && Account.Connection.Options != null
                        ? Account.Connection.Options.Name : "";
                return !string.IsNullOrEmpty(connection)
                    && connection.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch
            {
                return false;
            }
        }

        private void ProcessExecutionTick()
        {
            if (CurrentBars[ExecutionBip] < 0)
                return;
            var time = Times[ExecutionBip][0];
            var price = Closes[ExecutionBip][0];
            UpdateStructureEquity(time, price);

            if (Clock(time) >= FlattenTime)
            {
                pending = null;
                if (!manualExitPending && Position.MarketPosition != MarketPosition.Flat)
                {
                    manualExitPending = true;
                    if (Position.MarketPosition == MarketPosition.Long)
                        ExitLong(ExecutionBip, Position.Quantity, FlatExit, activeSignal);
                    else
                        ExitShort(ExecutionBip, Position.Quantity, FlatExit, activeSignal);
                }
                return;
            }

            if (activeTrade && !manualExitPending && MaximumHoldMinutes > 0
                && deadline != Core.Globals.MinDate && time >= deadline
                && Position.MarketPosition != MarketPosition.Flat)
            {
                manualExitPending = true;
                if (activeLong)
                    ExitLong(ExecutionBip, Position.Quantity, TimeExit, activeSignal);
                else
                    ExitShort(ExecutionBip, Position.Quantity, TimeExit, activeSignal);
                return;
            }

            if (pending != null && time > pendingExpires)
            {
                LogEvent(time, "PENDING_EXPIRED " + pending.Signal);
                pending = null;
            }
            if (pending == null || time < pending.Time)
                return;
            if (time.Date != pending.Time.Date || Clock(time) >= EntryEndTime)
            {
                pending = null;
                return;
            }
            var block = PortfolioBlockReason(time, true);
            if (block != "")
            {
                LogEvent(time, "SIGNAL_DROP " + pending.Signal + " Reason=" + block);
                pending = null;
                return;
            }

            var c = pending;
            pending = null;
            entrySubmitted = true;
            SetStopLoss(c.Signal, CalculationMode.Ticks, c.StopTicks, false);
            SetProfitTarget(c.Signal, CalculationMode.Ticks, c.TargetTicks);
            LogEvent(time, "ENTRY_SUBMIT " + c.Signal + " Qty=" + OrderQuantity
                + " Price=" + Num(price) + " Stop=" + c.StopTicks
                + " Target=" + c.TargetTicks);
            if (c.Long)
                EnterLong(ExecutionBip, OrderQuantity, c.Signal);
            else
                EnterShort(ExecutionBip, OrderQuantity, c.Signal);
        }

        protected override void OnOrderUpdate(Order order, double limitPrice,
            double stopPrice, int quantity, int filled, double averageFillPrice,
            OrderState orderState, DateTime time, ErrorCode error, string comment)
        {
            if (order == null)
                return;
            if (order.Name == FlatExit || order.Name == TimeExit)
            {
                if (orderState == OrderState.Rejected
                    || orderState == OrderState.Cancelled)
                {
                    manualExitPending = false;
                    LogEvent(time, "EXIT_ORDER_ERROR " + order.Name
                        + " State=" + orderState + " Code=" + error);
                }
                return;
            }
            if (!IsOurEntry(order.Name))
                return;

            if (orderState == OrderState.Submitted || orderState == OrderState.Accepted
                || orderState == OrderState.Working || orderState == OrderState.PartFilled)
                workingEntry = order;
            else if (orderState == OrderState.Rejected
                || orderState == OrderState.Cancelled)
            {
                if (!activeTrade)
                    entrySubmitted = false;
                LogEvent(time, "ORDER_ERROR " + order.Name + " State="
                    + orderState + " Code=" + error + " Comment=" + comment);
            }
        }

        protected override void OnExecutionUpdate(Execution execution, string executionId,
            double price, int quantity, MarketPosition marketPosition,
            string orderId, DateTime time)
        {
            if (execution == null || execution.Order == null || quantity <= 0)
                return;
            var order = execution.Order;
            if (IsOurEntry(order.Name))
            {
                if (!activeTrade)
                {
                    activeTrade = true;
                    activeSignal = order.Name;
                    activeLong = activeSignal != OnlSignal;
                    entryTime = time;
                    deadline = MaximumHoldMinutes > 0
                        ? time.AddMinutes(MaximumHoldMinutes) : Core.Globals.MinDate;
                    entryFillQty = 0;
                    entryPriceTotal = 0;
                    exitFillQty = 0;
                    exitGross = 0;
                    mfeTicks = 0;
                    maeTicks = 0;
                    activeStopTicks = activeSignal == OnlSignal ? OnlStopTicks
                        : activeSignal == OnhSignal ? OnhStopTicks : PdcStopTicks;
                    activeTargetTicks = activeSignal == OnlSignal ? OnlTargetTicks
                        : activeSignal == OnhSignal ? OnhTargetTicks : PdcTargetTicks;
                    tradesToday++;
                }
                entryFillQty += quantity;
                entryPriceTotal += price * quantity;
                entryAverage = entryPriceTotal / Math.Max(1, entryFillQty);
                entrySubmitted = false;
                LogEvent(time, "ENTRY_FILL " + activeSignal + " Fill=" + Num(price)
                    + " Qty=" + quantity + " Avg=" + Num(entryAverage));
                return;
            }

            if (!activeTrade || order.FromEntrySignal != activeSignal)
                return;

            var pnl = (activeLong ? price - entryAverage : entryAverage - price)
                * Instrument.MasterInstrument.PointValue * quantity;
            exitGross += pnl;
            exitFillQty += quantity;
            LogEvent(time, "EXIT_FILL " + activeSignal + " Order=" + order.Name
                + " Fill=" + Num(price) + " Qty=" + quantity
                + " GrossSoFar=" + Num(exitGross));

            if (exitFillQty < entryFillQty)
                return;

            cumulativeGross += exitGross;
            dailyGross += exitGross;
            if (exitGross > 0) winnersToday++;
            else if (exitGross < 0) lossesToday++;
            WriteTrade(time, order.Name, price);
            WriteEquity(time, price, 0, cumulativeGross,
                Math.Max(0.0, equityPeak - cumulativeGross));
            LogEvent(time, "TRADE_COMPLETE " + activeSignal + " Gross=" + Num(exitGross)
                + " DayTrades=" + tradesToday + " Wins=" + winnersToday
                + " Losses=" + lossesToday);
            activeTrade = false;
            activeSignal = "";
            activeLong = false;
            entryFillQty = exitFillQty = 0;
            entryPriceTotal = 0;
            entryTime = Core.Globals.MinDate;
            deadline = Core.Globals.MinDate;
            manualExitPending = false;
            workingEntry = null;
        }

        private void UpdateStructureEquity(DateTime time, double price)
        {
            if (!Valid(price))
                return;
            var unrealized = 0.0;
            if (activeTrade && entryFillQty > exitFillQty && entryTime <= time)
            {
                var points = activeLong ? price - entryAverage : entryAverage - price;
                var ticks = points / TickSize;
                mfeTicks = Math.Max(mfeTicks, ticks);
                maeTicks = Math.Max(maeTicks, -ticks);
                unrealized = points * Instrument.MasterInstrument.PointValue
                    * (entryFillQty - exitFillQty);
            }
            var eq = cumulativeGross + unrealized;
            bool peakUpdated = eq > equityPeak;
            if (peakUpdated) equityPeak = eq;
            var dd = Math.Max(0.0, equityPeak - eq);
            bool ddUpdated = dd > maxDrawdown;
            if (ddUpdated)
            {
                maxDrawdown = dd;
                maxDrawdownAt = time;
            }
            if (peakUpdated || ddUpdated)
                WriteEquity(time, price, unrealized, eq, dd);
        }

        private Candidate NewCandidate(string signal, string family, bool isLong,
            DateTime time, double level, double open, double high, double low,
            double close, int stop, int target)
        {
            return new Candidate {
                Signal = signal, Family = family, Long = isLong,
                Time = time, Level = level, Open = open,
                High = high, Low = low, Close = close, StopTicks = stop,
                TargetTicks = target, CompressionThreshold = compressionThreshold,
                HistoryCount = compressionHistoryCount
            };
        }

        private static double Percentile(IEnumerable<double> history, int percentile)
        {
            var a = new List<double>(history);
            if (a.Count == 0) return double.NaN;
            a.Sort();
            var index = Math.Max(0, Math.Min(a.Count - 1,
                (int)Math.Ceiling(a.Count * (percentile / 100.0)) - 1));
            return a[index];
        }

        private bool IsOurEntry(string name)
        {
            return name == OnlSignal || name == OnhSignal || name == PdcSignal;
        }

        private static int Clock(DateTime time)
        {
            return time.Hour * 10000 + time.Minute * 100 + time.Second;
        }

        private int MinutesFromOpen(int clock)
        {
            return (clock / 10000 * 60 + clock / 100 % 100)
                - (MarketOpenTime / 10000 * 60 + MarketOpenTime / 100 % 100);
        }

        private static bool Valid(double v)
        {
            return !double.IsNaN(v) && !double.IsInfinity(v);
        }
    }
}
