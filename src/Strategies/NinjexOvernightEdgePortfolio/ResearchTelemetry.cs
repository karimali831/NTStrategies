using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    internal interface INinjexOvernightEdgeResearchSink : IDisposable
    {
        string OutputPath { get; }
        void Write(NinjexOvernightEdgeResearchRow row);
    }

    internal sealed class NinjexOvernightEdgeNullResearchSink : INinjexOvernightEdgeResearchSink
    {
        public string OutputPath { get { return string.Empty; } }
        public void Write(NinjexOvernightEdgeResearchRow row) { }
        public void Dispose() { }
    }

    internal sealed class NinjexOvernightEdgeCsvResearchSink : INinjexOvernightEdgeResearchSink
    {
        private readonly object syncRoot = new object();
        private readonly StreamWriter writer;

        public NinjexOvernightEdgeCsvResearchSink(string outputPath)
        {
            OutputPath = outputPath ?? string.Empty;
            writer = new StreamWriter(OutputPath, false);
            writer.AutoFlush = true;
            writer.WriteLine(NinjexOvernightEdgeResearchRow.CsvHeader);
        }

        public string OutputPath { get; private set; }

        public void Write(NinjexOvernightEdgeResearchRow row)
        {
            if (row == null)
                return;

            lock (syncRoot)
            {
                writer.WriteLine(row.ToCsv());
            }
        }

        public void Dispose()
        {
            lock (syncRoot)
            {
                writer.Dispose();
            }
        }
    }

    internal sealed class NinjexOvernightEdgeResearchRow
    {
        public const string CsvHeader =
            "EventType,EventTime,TradingDate,Instrument,StrategyVersion,PortfolioMode," +
            "Model,Signal,Direction,RawTrigger,Qualified,SelectedByPriority,PortfolioEligible,PortfolioBlockReason," +
            "CandidateOccurrenceToday,ModelTradeOccurrenceToday,QualifiedCountSameTimestamp," +
            "SignalOpen,SignalHigh,SignalLow,SignalClose,PreviousClose,SignalRangeTicks,SignalBodyTicks,BreakDepthTicks," +
            "MinutesFromOpen,ATR5Ticks,EMA5Fast,EMA5Slow,EMA5FastDistanceTicks,EMA5SlowDistanceTicks,EMA5FastSlopeTicks,EMA5SlowSlopeTicks," +
            "OvernightHigh,OvernightLow,OvernightWidthTicks,PremarketHigh,PremarketLow,PremarketWidthTicks,PriorDayClose,RthOpen," +
            "TimeFilterOk,ATRFilterOk,RangeFilterOk,OvernightWidthFilterOk,PremarketWidthFilterOk,EMAFilterOk," +
            "TradesToday,WinnersToday,LossesToday,DayGross,EntryPrice,ExitPrice,TradeGross,ExitName";

        public string EventType;
        public DateTime EventTime;
        public DateTime TradingDate;
        public string Instrument;
        public string StrategyVersion;
        public string PortfolioMode;
        public string Model;
        public string Signal;
        public string Direction;
        public bool? RawTrigger;
        public bool? Qualified;
        public bool? SelectedByPriority;
        public bool? PortfolioEligible;
        public string PortfolioBlockReason;
        public int CandidateOccurrenceToday = -1;
        public int ModelTradeOccurrenceToday = -1;
        public int QualifiedCountSameTimestamp = -1;
        public double SignalOpen = double.NaN;
        public double SignalHigh = double.NaN;
        public double SignalLow = double.NaN;
        public double SignalClose = double.NaN;
        public double PreviousClose = double.NaN;
        public double SignalRangeTicks = double.NaN;
        public double SignalBodyTicks = double.NaN;
        public double BreakDepthTicks = double.NaN;
        public int MinutesFromOpen = int.MinValue;
        public double Atr5Ticks = double.NaN;
        public double Ema5Fast = double.NaN;
        public double Ema5Slow = double.NaN;
        public double Ema5FastDistanceTicks = double.NaN;
        public double Ema5SlowDistanceTicks = double.NaN;
        public double Ema5FastSlopeTicks = double.NaN;
        public double Ema5SlowSlopeTicks = double.NaN;
        public double OvernightHigh = double.NaN;
        public double OvernightLow = double.NaN;
        public double OvernightWidthTicks = double.NaN;
        public double PremarketHigh = double.NaN;
        public double PremarketLow = double.NaN;
        public double PremarketWidthTicks = double.NaN;
        public double PriorDayClose = double.NaN;
        public double RthOpen = double.NaN;
        public bool? TimeFilterOk;
        public bool? AtrFilterOk;
        public bool? RangeFilterOk;
        public bool? OvernightWidthFilterOk;
        public bool? PremarketWidthFilterOk;
        public bool? EmaFilterOk;
        public int TradesToday = -1;
        public int WinnersToday = -1;
        public int LossesToday = -1;
        public double DayGross = double.NaN;
        public double EntryPrice = double.NaN;
        public double ExitPrice = double.NaN;
        public double TradeGross = double.NaN;
        public string ExitName;

        public NinjexOvernightEdgeResearchRow Clone()
        {
            return (NinjexOvernightEdgeResearchRow)MemberwiseClone();
        }

        public string ToCsv()
        {
            var values = new string[]
            {
                Csv(EventType),
                EventTime == Core.Globals.MinDate ? string.Empty : EventTime.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
                TradingDate == Core.Globals.MinDate ? string.Empty : TradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Csv(Instrument), Csv(StrategyVersion), Csv(PortfolioMode), Csv(Model), Csv(Signal), Csv(Direction),
                Bool(RawTrigger), Bool(Qualified), Bool(SelectedByPriority), Bool(PortfolioEligible), Csv(PortfolioBlockReason),
                Int(CandidateOccurrenceToday), Int(ModelTradeOccurrenceToday), Int(QualifiedCountSameTimestamp),
                Number(SignalOpen), Number(SignalHigh), Number(SignalLow), Number(SignalClose), Number(PreviousClose),
                Number(SignalRangeTicks), Number(SignalBodyTicks), Number(BreakDepthTicks),
                MinutesFromOpen == int.MinValue ? string.Empty : MinutesFromOpen.ToString(CultureInfo.InvariantCulture),
                Number(Atr5Ticks), Number(Ema5Fast), Number(Ema5Slow), Number(Ema5FastDistanceTicks), Number(Ema5SlowDistanceTicks),
                Number(Ema5FastSlopeTicks), Number(Ema5SlowSlopeTicks), Number(OvernightHigh), Number(OvernightLow), Number(OvernightWidthTicks),
                Number(PremarketHigh), Number(PremarketLow), Number(PremarketWidthTicks), Number(PriorDayClose), Number(RthOpen),
                Bool(TimeFilterOk), Bool(AtrFilterOk), Bool(RangeFilterOk), Bool(OvernightWidthFilterOk), Bool(PremarketWidthFilterOk), Bool(EmaFilterOk),
                Int(TradesToday), Int(WinnersToday), Int(LossesToday), Number(DayGross), Number(EntryPrice), Number(ExitPrice), Number(TradeGross), Csv(ExitName)
            };

            return string.Join(",", values);
        }

        private static string Number(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string Int(int value)
        {
            return value < 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Bool(bool? value)
        {
            return value.HasValue ? (value.Value ? "1" : "0") : string.Empty;
        }

        private static string Csv(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            var escaped = value.Replace("\"", "\"\"");
            return "\"" + escaped + "\"";
        }
    }

    public partial class NinjexOvernightEdgePortfolio
    {
        private INinjexOvernightEdgeResearchSink researchSink = new NinjexOvernightEdgeNullResearchSink();
        private readonly Dictionary<string, int> researchCandidateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, int> researchTradeCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, NinjexOvernightEdgeResearchRow> researchSignalSnapshots = new Dictionary<string, NinjexOvernightEdgeResearchRow>(StringComparer.Ordinal);
        private NinjexOvernightEdgeResearchRow pendingResearchSignal;
        private NinjexOvernightEdgeResearchRow activeResearchSignal;
        private double researchPreviousEmaFast = double.NaN;
        private double researchPreviousEmaSlow = double.NaN;
        private string researchTelemetryPath = string.Empty;
        private bool researchTelemetryFaulted;

        // Live daily risk-state recovery. These fields are independent of the
        // optional research CSV; the existing research hooks are simply safe
        // integration points already called by V1's execution path.
        private const int LiveDailyRecoveryGraceMilliseconds = 5000;
        private const string LiveDailySnapshotVersion = "1";
        private bool liveDailyRecoveryApplicable;
        private bool liveDailyRecoveryStarted;
        private bool liveDailyRecoveryComplete = true;
        private bool liveDailyRecoveryBlockLogged;
        private DateTime liveDailyRecoveryDate = Core.Globals.MinDate;
        private DateTime liveDailyRecoveryNotBeforeUtc = Core.Globals.MinDate;
        private string liveDailyStatePath = string.Empty;

        private sealed class LiveDailyStateSnapshot
        {
            public DateTime TradingDate = Core.Globals.MinDate;
            public int Trades;
            public int Winners;
            public int Losses;
            public double GrossPnl;
        }

        private sealed class LiveRecoveryOpenTrade
        {
            public string EntrySignal = string.Empty;
            public PendingDirection Direction = PendingDirection.None;
            public int OpenQuantity;
            public int EntryQuantity;
            public double EntryPriceQuantity;
            public double GrossPnl;
        }

        private void InitializeResearchTelemetry()
        {
            DisposeResearchTelemetry();
            researchTelemetryFaulted = false;

            if (!EnableResearchTelemetry)
            {
                researchSink = new NinjexOvernightEdgeNullResearchSink();
                researchTelemetryPath = string.Empty;
                return;
            }

            try
            {
                var directory = Path.Combine(Core.Globals.UserDataDir, "NinjexResearch", "OvernightEdgePortfolio");
                Directory.CreateDirectory(directory);

                var instrumentName = Instrument == null ? "UnknownInstrument" : SanitizeFileName(Instrument.FullName);
                var fileName = string.Format(
                    CultureInfo.InvariantCulture,
                    "overnight_edge_research_{0}_{1}_{2}.csv",
                    instrumentName,
                    DateTime.Now.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture),
                    Guid.NewGuid().ToString("N").Substring(0, 8));

                researchTelemetryPath = Path.Combine(directory, fileName);
                researchSink = new NinjexOvernightEdgeCsvResearchSink(researchTelemetryPath);
                Diagnostic(DateTime.Now, "RESEARCH TELEMETRY READY Path={0}", researchTelemetryPath);
            }
            catch (Exception ex)
            {
                researchTelemetryFaulted = true;
                researchTelemetryPath = string.Empty;
                researchSink = new NinjexOvernightEdgeNullResearchSink();
                Diagnostic(DateTime.Now, "RESEARCH TELEMETRY DISABLED InitError={0}", ex.Message);
            }
        }

        private void DisposeResearchTelemetry()
        {
            try
            {
                if (researchSink != null)
                    researchSink.Dispose();
            }
            catch
            {
                // Telemetry cleanup must never interfere with strategy lifecycle.
            }
            finally
            {
                researchSink = new NinjexOvernightEdgeNullResearchSink();
            }
        }

        private void ResetResearchTelemetryDailyState()
        {
            researchCandidateCounts.Clear();
            researchTradeCounts.Clear();
            researchSignalSnapshots.Clear();
            pendingResearchSignal = null;
            activeResearchSignal = null;
        }

        private void CaptureResearchPreviousFiveMinuteContext(double previousEmaFast, double previousEmaSlow)
        {
            // Runs even when research telemetry is disabled. In real time this
            // normally restores the daily counters before the 09:35 entry window.
            EnsureLiveDailyStateRecoveryForSignal(GetCurrentStrategyTimeForRecovery());

            researchPreviousEmaFast = previousEmaFast;
            researchPreviousEmaSlow = previousEmaSlow;
        }

        private void CaptureFourModelResearchCandidates(
            DateTime signalTime, double signalOpen, double high, double low, double close, double previousClose,
            int minutesFromOpen, double overnightWidthTicks)
        {
            // Called immediately before Core.CanTakeNewTrade() in production
            // FourModel modes. This is the fail-closed restart safety gate even
            // when EnableResearchTelemetry is false.
            EnsureLiveDailyStateRecoveryForSignal(signalTime);

            if (!EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            var range1mTicks = TickSize > 0 ? (high - low) / TickSize : double.NaN;
            var bodyTicks = TickSize > 0 ? Math.Abs(close - signalOpen) / TickSize : double.NaN;
            var premarketWidthTicks = GetPremarketWidthTicks();
            var blockReason = GetResearchPortfolioBlockReason(false);
            var portfolioEligible = string.IsNullOrEmpty(blockReason);

            var priorCloseCross = IsFinite(priorDayClose) && priorDayCloseDate < signalTime.Date && previousClose <= priorDayClose && close > priorDayClose;
            var priorRangeOk = IsFinite(range1mTicks) && range1mTicks <= PriorCloseMaximumRangeTicks;
            var priorWidthOk = IsFinite(overnightWidthTicks) && overnightWidthTicks >= PriorCloseMinimumOvernightWidthTicks;
            var priorQualified = priorCloseCross && priorRangeOk && priorWidthOk;

            var pmhSweep = IsFinite(premarketHigh) && high > premarketHigh && close < premarketHigh;
            var pmhTimeOk = minutesFromOpen >= 0 && minutesFromOpen <= PremarketHighMaximumMinutesFromOpen;
            var pmhAtrOk = IsFinite(last5mAtrTicks) && last5mAtrTicks >= PremarketHighMinimumAtr5mTicks;
            var pmhQualified = pmhSweep && pmhTimeOk && pmhAtrOk;

            var rthCross = IsFinite(rthOpen) && rthOpenDate == signalTime.Date && previousClose >= rthOpen && close < rthOpen;
            var rthTimeOk = minutesFromOpen >= RthOpenMinimumMinutesFromOpen;
            var rthWidthOk = IsFinite(premarketWidthTicks) && premarketWidthTicks >= RthOpenMinimumPremarketWidthTicks;
            var rthQualified = rthCross && rthTimeOk && rthWidthOk;

            var pmlCross = IsFinite(premarketLow) && previousClose >= premarketLow && close < premarketLow;
            var pmlAtrOk = IsFinite(last5mAtrTicks) && last5mAtrTicks >= PremarketLowMinimumAtr5mTicks;
            var useFastEmaFilter = EnableEMAFilter && PortfolioMode == NinjexOvernightEdgePortfolioMode.FourModelResearchFiltered;
            var pmlEmaOk = !useFastEmaFilter || (IsFinite(last5mEmaFast) && close > last5mEmaFast);
            var pmlQualified = pmlCross && pmlAtrOk && pmlEmaOk;

            var qualifiedCount = (priorQualified ? 1 : 0) + (pmhQualified ? 1 : 0) + (rthQualified ? 1 : 0) + (pmlQualified ? 1 : 0);
            var selectedSignal = priorQualified ? PriorCloseEntrySignal
                : pmhQualified ? PremarketHighEntrySignal
                : rthQualified ? RthOpenEntrySignal
                : pmlQualified ? PremarketLowEntrySignal
                : string.Empty;

            if (priorCloseCross)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    PriorCloseModelName, PriorCloseEntrySignal, PendingDirection.Long, priorQualified, selectedSignal == PriorCloseEntrySignal,
                    portfolioEligible, blockReason, qualifiedCount, priorRangeOk, null, priorWidthOk, null, null,
                    TickSize > 0 ? (close - priorDayClose) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (pmhSweep)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    PremarketHighModelName, PremarketHighEntrySignal, PendingDirection.Short, pmhQualified, selectedSignal == PremarketHighEntrySignal,
                    portfolioEligible, blockReason, qualifiedCount, null, pmhTimeOk, null, null, pmhAtrOk,
                    TickSize > 0 ? (premarketHigh - close) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (rthCross)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    RthOpenModelName, RthOpenEntrySignal, PendingDirection.Short, rthQualified, selectedSignal == RthOpenEntrySignal,
                    portfolioEligible, blockReason, qualifiedCount, null, rthTimeOk, null, rthWidthOk, null,
                    TickSize > 0 ? (rthOpen - close) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (pmlCross)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    PremarketLowModelName, PremarketLowEntrySignal, PendingDirection.Short, pmlQualified, selectedSignal == PremarketLowEntrySignal,
                    portfolioEligible, blockReason, qualifiedCount, null, null, null, null, pmlAtrOk,
                    TickSize > 0 ? (premarketLow - close) / TickSize : double.NaN, range1mTicks, bodyTicks, pmlEmaOk);
        }

        private void WriteCandidateResearchRow(
            DateTime signalTime, double signalOpen, double high, double low, double close, double previousClose,
            int minutesFromOpen, double overnightWidthTicks, double premarketWidthTicks,
            string modelName, string signalName, PendingDirection direction, bool qualified, bool selected,
            bool portfolioEligible, string blockReason, int qualifiedCount,
            bool? rangeFilterOk, bool? timeFilterOk, bool? overnightWidthFilterOk, bool? premarketWidthFilterOk, bool? atrFilterOk,
            double breakDepthTicks, double range1mTicks, double bodyTicks, bool? emaFilterOk = null)
        {
            var row = CreateBaseResearchRow(signalTime, modelName, signalName, direction);
            row.EventType = "Candidate";
            row.RawTrigger = true;
            row.Qualified = qualified;
            row.SelectedByPriority = selected;
            row.PortfolioEligible = portfolioEligible;
            row.PortfolioBlockReason = blockReason;
            row.CandidateOccurrenceToday = IncrementResearchCounter(researchCandidateCounts, signalName);
            row.QualifiedCountSameTimestamp = qualifiedCount;
            row.SignalOpen = signalOpen;
            row.SignalHigh = high;
            row.SignalLow = low;
            row.SignalClose = close;
            row.PreviousClose = previousClose;
            row.SignalRangeTicks = range1mTicks;
            row.SignalBodyTicks = bodyTicks;
            row.BreakDepthTicks = breakDepthTicks;
            row.MinutesFromOpen = minutesFromOpen;
            row.Atr5Ticks = last5mAtrTicks;
            row.Ema5Fast = last5mEmaFast;
            row.Ema5Slow = last5mEmaSlow;
            row.Ema5FastDistanceTicks = TickSize > 0 && IsFinite(last5mEmaFast) ? (close - last5mEmaFast) / TickSize : double.NaN;
            row.Ema5SlowDistanceTicks = TickSize > 0 && IsFinite(last5mEmaSlow) ? (close - last5mEmaSlow) / TickSize : double.NaN;
            row.Ema5FastSlopeTicks = TickSize > 0 && IsFinite(last5mEmaFast) && IsFinite(researchPreviousEmaFast) ? (last5mEmaFast - researchPreviousEmaFast) / TickSize : double.NaN;
            row.Ema5SlowSlopeTicks = TickSize > 0 && IsFinite(last5mEmaSlow) && IsFinite(researchPreviousEmaSlow) ? (last5mEmaSlow - researchPreviousEmaSlow) / TickSize : double.NaN;
            row.OvernightHigh = overnightHigh;
            row.OvernightLow = overnightLow;
            row.OvernightWidthTicks = overnightWidthTicks;
            row.PremarketHigh = premarketHigh;
            row.PremarketLow = premarketLow;
            row.PremarketWidthTicks = premarketWidthTicks;
            row.PriorDayClose = priorDayClose;
            row.RthOpen = rthOpen;
            row.RangeFilterOk = rangeFilterOk;
            row.TimeFilterOk = timeFilterOk;
            row.OvernightWidthFilterOk = overnightWidthFilterOk;
            row.PremarketWidthFilterOk = premarketWidthFilterOk;
            row.AtrFilterOk = atrFilterOk;
            row.EmaFilterOk = emaFilterOk;
            row.TradesToday = tradesToday;
            row.WinnersToday = winnersToday;
            row.LossesToday = lossesToday;
            row.DayGross = grossPnlToday;

            WriteResearchRow(row);
            researchSignalSnapshots[ResearchSignalKey(signalTime, signalName)] = row.Clone();
        }

        private void AttachPendingResearchSignal(DateTime signalTime, string signalName)
        {
            pendingResearchSignal = null;
            if (!EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            NinjexOvernightEdgeResearchRow snapshot;
            if (researchSignalSnapshots.TryGetValue(ResearchSignalKey(signalTime, signalName), out snapshot))
                pendingResearchSignal = snapshot.Clone();
        }

        private void ClearPendingResearchSignal()
        {
            pendingResearchSignal = null;
        }

        private void RecordResearchEntryFill(DateTime time, double entryPrice)
        {
            // Core increments tradesToday immediately before this callback.
            PersistLiveDailyStateSnapshot(time, "EntryFill");

            if (!EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            activeResearchSignal = pendingResearchSignal == null
                ? CreateBaseResearchRow(time, activeModelName, activeEntrySignal, activeTradeDirection)
                : pendingResearchSignal.Clone();

            activeResearchSignal.EventType = "TradeEntry";
            activeResearchSignal.EventTime = time;
            activeResearchSignal.TradingDate = time.Date;
            activeResearchSignal.Model = activeModelName;
            activeResearchSignal.Signal = activeEntrySignal;
            activeResearchSignal.Direction = activeTradeDirection.ToString();
            activeResearchSignal.ModelTradeOccurrenceToday = IncrementResearchCounter(researchTradeCounts, activeEntrySignal);
            activeResearchSignal.TradesToday = tradesToday;
            activeResearchSignal.WinnersToday = winnersToday;
            activeResearchSignal.LossesToday = lossesToday;
            activeResearchSignal.DayGross = grossPnlToday;
            activeResearchSignal.EntryPrice = entryPrice;
            WriteResearchRow(activeResearchSignal);
        }

        private void RecordResearchTradeExit(DateTime time, string exitName, double exitPrice)
        {
            // Core has already updated winnersToday/lossesToday/grossPnlToday.
            PersistLiveDailyStateSnapshot(time, "TradeComplete");

            if (!EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            var row = activeResearchSignal == null
                ? CreateBaseResearchRow(time, activeModelName, activeEntrySignal, activeTradeDirection)
                : activeResearchSignal.Clone();

            row.EventType = "TradeExit";
            row.EventTime = time;
            row.TradingDate = time.Date;
            row.ExitPrice = exitPrice;
            row.TradeGross = activeTradeGrossPnl;
            row.ExitName = exitName ?? string.Empty;
            row.TradesToday = tradesToday;
            row.WinnersToday = winnersToday;
            row.LossesToday = lossesToday;
            row.DayGross = grossPnlToday;
            WriteResearchRow(row);
            activeResearchSignal = null;
            pendingResearchSignal = null;
        }

        private void WriteResearchRow(NinjexOvernightEdgeResearchRow row)
        {
            if (row == null || !EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            try
            {
                researchSink.Write(row);
            }
            catch (Exception ex)
            {
                researchTelemetryFaulted = true;
                DisposeResearchTelemetry();
                Diagnostic(
                    row.EventTime == Core.Globals.MinDate ? DateTime.Now : row.EventTime,
                    "RESEARCH TELEMETRY DISABLED WriteError={0}",
                    ex.Message);
            }
        }

        private NinjexOvernightEdgeResearchRow CreateBaseResearchRow(DateTime time, string modelName, string signalName, PendingDirection direction)
        {
            return new NinjexOvernightEdgeResearchRow
            {
                EventTime = time,
                TradingDate = time.Date,
                Instrument = Instrument == null ? string.Empty : Instrument.FullName,
                StrategyVersion = StrategyVersion,
                PortfolioMode = PortfolioMode.ToString(),
                Model = modelName ?? string.Empty,
                Signal = signalName ?? string.Empty,
                Direction = direction.ToString()
            };
        }

        private string GetResearchPortfolioBlockReason(bool allowExistingPendingEntry)
        {
            if (Position.MarketPosition != MarketPosition.Flat || entryOrderPending || manualExitPending
                || (!allowExistingPendingEntry && pendingDirection != PendingDirection.None))
                return "OrderOrPositionActive";
            if (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay)
                return "MaxTrades";
            if (MaxWinnersPerDay > 0 && winnersToday >= MaxWinnersPerDay)
                return "MaxWinners";
            if (MaxLossesPerDay > 0 && lossesToday >= MaxLossesPerDay)
                return "MaxLosses";
            return string.Empty;
        }

        private static int IncrementResearchCounter(Dictionary<string, int> counters, string key)
        {
            var normalizedKey = key ?? string.Empty;
            int count;
            counters.TryGetValue(normalizedKey, out count);
            count++;
            counters[normalizedKey] = count;
            return count;
        }

        private static string ResearchSignalKey(DateTime signalTime, string signalName)
        {
            return signalTime.Ticks.ToString(CultureInfo.InvariantCulture) + "|" + (signalName ?? string.Empty);
        }

        private static string SanitizeFileName(string value)
        {
            var result = value ?? string.Empty;
            foreach (var invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');
            return result.Replace(' ', '_');
        }

        // ---------------------------------------------------------------------
        // Restart-safe live daily portfolio state
        // ---------------------------------------------------------------------

        private void EnsureLiveDailyStateRecoveryForSignal(DateTime eventTime)
        {
            if (State != State.Realtime)
                return;

            if (!liveDailyRecoveryStarted
                || liveDailyRecoveryDate != eventTime.Date)
            {
                BeginLiveDailyStateRecovery(eventTime);
            }

            if (liveDailyRecoveryComplete)
                return;

            if (DateTime.UtcNow < liveDailyRecoveryNotBeforeUtc)
            {
                FailClosedLiveDailyState(eventTime, "WaitingForAccountExecutions");
                return;
            }

            LiveDailyStateSnapshot recovered;
            string error;

            if (!TryRebuildLiveDailyStateFromAccountExecutions(eventTime.Date, out recovered, out error))
            {
                FailClosedLiveDailyState(
                    eventTime,
                    string.IsNullOrEmpty(error) ? "AccountExecutionRecoveryFailed" : error);
                return;
            }

            ApplyRecoveredLiveDailyState(recovered, eventTime, "AccountExecutions");
            PersistLiveDailyStateSnapshot(eventTime, "RecoveryAccountExecutions");
        }

        private void BeginLiveDailyStateRecovery(DateTime eventTime)
        {
            liveDailyRecoveryStarted = true;
            liveDailyRecoveryApplicable = ShouldUseLiveDailyStateRecovery();
            liveDailyRecoveryComplete = !liveDailyRecoveryApplicable;
            liveDailyRecoveryBlockLogged = false;
            liveDailyRecoveryDate = eventTime.Date;
            liveDailyStatePath = BuildLiveDailyStatePath();

            if (!liveDailyRecoveryApplicable)
                return;

            LiveDailyStateSnapshot persisted;
            if (TryReadLiveDailyStateSnapshot(eventTime.Date, out persisted))
            {
                LiveDailyStateSnapshot accountState;
                string accountError;

                if (TryRebuildLiveDailyStateFromAccountExecutions(eventTime.Date, out accountState, out accountError))
                {
                    if (accountState.Trades > persisted.Trades)
                    {
                        ApplyRecoveredLiveDailyState(accountState, eventTime, "AccountExecutionsNewer");
                        PersistLiveDailyStateSnapshot(eventTime, "RecoveryAccountExecutionsNewer");
                        return;
                    }

                    if (accountState.Trades == persisted.Trades
                        && !LiveDailyStatesEquivalent(persisted, accountState))
                    {
                        Diagnostic(
                            eventTime,
                            "LIVE DAILY STATE RECOVERY FAILED Reason=PersistedAccountMismatch " +
                            "Persisted=T{0}/W{1}/L{2}/Pnl{3:0.00} " +
                            "Account=T{4}/W{5}/L{6}/Pnl{7:0.00}",
                            persisted.Trades, persisted.Winners, persisted.Losses, persisted.GrossPnl,
                            accountState.Trades, accountState.Winners, accountState.Losses, accountState.GrossPnl);
                        return;
                    }
                }

                // Never downgrade a durable snapshot merely because broker
                // executions are still repopulating after reconnect.
                ApplyRecoveredLiveDailyState(persisted, eventTime, "PersistedSnapshot");
                return;
            }

            // First run after this feature is installed, or a fresh trading day:
            // allow the broker execution collection time to populate, and block
            // every new live entry until recovery completes.
            liveDailyRecoveryNotBeforeUtc = DateTime.UtcNow.AddMilliseconds(LiveDailyRecoveryGraceMilliseconds);

            Diagnostic(
                eventTime,
                "LIVE DAILY STATE RECOVERY PENDING Date={0:yyyy-MM-dd} Reason=NoPersistedSnapshot",
                eventTime.Date);

            FailClosedLiveDailyState(eventTime, "WaitingForAccountExecutions");
        }

        private void FailClosedLiveDailyState(DateTime eventTime, string reason)
        {
            // Core.CanTakeNewTrade() runs immediately after the four-model
            // candidate hook. Make every enabled daily cap appear reached until
            // authoritative state replaces these sentinel values.
            if (MaxTradesPerDay > 0)
                tradesToday = Math.Max(tradesToday, MaxTradesPerDay);
            if (MaxWinnersPerDay > 0)
                winnersToday = Math.Max(winnersToday, MaxWinnersPerDay);
            if (MaxLossesPerDay > 0)
                lossesToday = Math.Max(lossesToday, MaxLossesPerDay);

            if (liveDailyRecoveryBlockLogged)
                return;

            liveDailyRecoveryBlockLogged = true;
            Diagnostic(
                eventTime,
                "TRADE BLOCK Reason=LiveDailyStateRecovery Detail={0} Date={1:yyyy-MM-dd}",
                reason ?? string.Empty,
                eventTime.Date);
        }

        private void ApplyRecoveredLiveDailyState(
            LiveDailyStateSnapshot snapshot,
            DateTime eventTime,
            string source)
        {
            if (snapshot == null)
                return;

            activeTradingDate = snapshot.TradingDate.Date;
            tradesToday = snapshot.Trades;
            winnersToday = snapshot.Winners;
            lossesToday = snapshot.Losses;
            grossPnlToday = snapshot.GrossPnl;
            liveDailyRecoveryDate = snapshot.TradingDate.Date;
            liveDailyRecoveryComplete = true;
            liveDailyRecoveryBlockLogged = false;

            Diagnostic(
                eventTime,
                "LIVE DAILY STATE RECOVERED Source={0} Date={1:yyyy-MM-dd} " +
                "Trades={2} Winners={3} Losses={4} GrossPnl={5:0.00}",
                source ?? string.Empty,
                snapshot.TradingDate,
                snapshot.Trades,
                snapshot.Winners,
                snapshot.Losses,
                snapshot.GrossPnl);
        }

        private void PersistLiveDailyStateSnapshot(DateTime eventTime, string reason)
        {
            if (!liveDailyRecoveryApplicable
                || State != State.Realtime
                || activeTradingDate == Core.Globals.MinDate)
                return;

            try
            {
                if (string.IsNullOrEmpty(liveDailyStatePath))
                    liveDailyStatePath = BuildLiveDailyStatePath();
                if (string.IsNullOrEmpty(liveDailyStatePath))
                    return;

                var directory = Path.GetDirectoryName(liveDailyStatePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var lines = new[]
                {
                    "Version=" + LiveDailySnapshotVersion,
                    "TradingDate=" + activeTradingDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    "Account=" + GetLiveRecoveryAccountName(),
                    "Instrument=" + GetLiveRecoveryInstrumentName(),
                    "Trades=" + tradesToday.ToString(CultureInfo.InvariantCulture),
                    "Winners=" + winnersToday.ToString(CultureInfo.InvariantCulture),
                    "Losses=" + lossesToday.ToString(CultureInfo.InvariantCulture),
                    "GrossPnl=" + grossPnlToday.ToString("0.########", CultureInfo.InvariantCulture),
                    "UpdatedUtc=" + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                    "Reason=" + (reason ?? string.Empty)
                };

                var tempPath = liveDailyStatePath + ".tmp";
                File.WriteAllLines(tempPath, lines);

                if (File.Exists(liveDailyStatePath))
                {
                    try
                    {
                        File.Replace(tempPath, liveDailyStatePath, null);
                    }
                    catch
                    {
                        File.Copy(tempPath, liveDailyStatePath, true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(tempPath, liveDailyStatePath);
                }
            }
            catch (Exception ex)
            {
                Diagnostic(
                    eventTime,
                    "LIVE DAILY STATE PERSIST ERROR Reason={0} Message={1}",
                    reason ?? string.Empty,
                    ex.Message);
            }
        }

        private bool TryReadLiveDailyStateSnapshot(DateTime tradingDate, out LiveDailyStateSnapshot snapshot)
        {
            snapshot = null;

            try
            {
                if (string.IsNullOrEmpty(liveDailyStatePath) || !File.Exists(liveDailyStatePath))
                    return false;

                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadAllLines(liveDailyStatePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;
                    var separator = line.IndexOf('=');
                    if (separator <= 0)
                        continue;
                    values[line.Substring(0, separator)] = line.Substring(separator + 1);
                }

                string version, dateText, account, instrument, tradesText, winnersText, lossesText, grossText;
                if (!values.TryGetValue("Version", out version)
                    || version != LiveDailySnapshotVersion
                    || !values.TryGetValue("TradingDate", out dateText)
                    || !values.TryGetValue("Account", out account)
                    || !values.TryGetValue("Instrument", out instrument)
                    || !values.TryGetValue("Trades", out tradesText)
                    || !values.TryGetValue("Winners", out winnersText)
                    || !values.TryGetValue("Losses", out lossesText)
                    || !values.TryGetValue("GrossPnl", out grossText))
                    return false;

                DateTime date;
                int trades, winners, losses;
                double gross;

                if (!DateTime.TryParseExact(dateText, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                    || !int.TryParse(tradesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out trades)
                    || !int.TryParse(winnersText, NumberStyles.Integer, CultureInfo.InvariantCulture, out winners)
                    || !int.TryParse(lossesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out losses)
                    || !double.TryParse(grossText, NumberStyles.Float, CultureInfo.InvariantCulture, out gross))
                    return false;

                if (date.Date != tradingDate.Date
                    || !string.Equals(account, GetLiveRecoveryAccountName(), StringComparison.Ordinal)
                    || !string.Equals(instrument, GetLiveRecoveryInstrumentName(), StringComparison.OrdinalIgnoreCase)
                    || trades < 0 || winners < 0 || losses < 0 || winners + losses > trades)
                    return false;

                snapshot = new LiveDailyStateSnapshot
                {
                    TradingDate = date.Date,
                    Trades = trades,
                    Winners = winners,
                    Losses = losses,
                    GrossPnl = gross
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryRebuildLiveDailyStateFromAccountExecutions(
            DateTime tradingDate,
            out LiveDailyStateSnapshot snapshot,
            out string error)
        {
            snapshot = new LiveDailyStateSnapshot { TradingDate = tradingDate.Date };
            error = string.Empty;

            if (Account == null)
            {
                error = "AccountUnavailable";
                return false;
            }

            if (Account.Connection == null || Account.Connection.Status != ConnectionStatus.Connected)
            {
                error = "OrderConnectionNotConnected";
                return false;
            }

            if (Instrument == null)
            {
                error = "InstrumentUnavailable";
                return false;
            }

            var relevant = new List<Execution>();
            try
            {
                lock (Account.Executions)
                {
                    foreach (var execution in Account.Executions)
                    {
                        if (execution == null || execution.Order == null || execution.Instrument == null
                            || execution.Quantity <= 0 || execution.Time.Date != tradingDate.Date
                            || !string.Equals(execution.Instrument.FullName, Instrument.FullName, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var fromEntry = execution.Order.FromEntrySignal ?? string.Empty;
                        if (IsEntrySignalName(execution.Order.Name)
                            || IsEntrySignalName(fromEntry)
                            || IsKnownLiveRecoveryExitName(execution.Order.Name))
                            relevant.Add(execution);
                    }
                }
            }
            catch (Exception ex)
            {
                error = "AccountExecutionsReadError:" + ex.Message;
                return false;
            }

            relevant.Sort(delegate(Execution left, Execution right)
            {
                var compare = left.Time.CompareTo(right.Time);
                if (compare != 0)
                    return compare;
                return string.Compare(left.ExecutionId ?? string.Empty, right.ExecutionId ?? string.Empty, StringComparison.Ordinal);
            });

            var openTrade = new LiveRecoveryOpenTrade();
            var pointValue = Instrument.MasterInstrument.PointValue;

            foreach (var execution in relevant)
            {
                var order = execution.Order;

                if (IsEntrySignalName(order.Name))
                {
                    var direction = IsLongEntrySignalName(order.Name) ? PendingDirection.Long : PendingDirection.Short;

                    if (openTrade.OpenQuantity <= 0)
                    {
                        openTrade = new LiveRecoveryOpenTrade
                        {
                            EntrySignal = order.Name,
                            Direction = direction
                        };
                        snapshot.Trades++;
                    }
                    else if (!string.Equals(openTrade.EntrySignal, order.Name, StringComparison.Ordinal)
                             || openTrade.Direction != direction)
                    {
                        error = "OverlappingStrategyEntries";
                        return false;
                    }

                    openTrade.OpenQuantity += execution.Quantity;
                    openTrade.EntryQuantity += execution.Quantity;
                    openTrade.EntryPriceQuantity += execution.Price * execution.Quantity;
                    continue;
                }

                var fromEntrySignal = order.FromEntrySignal ?? string.Empty;
                var looksLikeExit = IsEntrySignalName(fromEntrySignal) || IsKnownLiveRecoveryExitName(order.Name);
                if (!looksLikeExit)
                    continue;

                if (openTrade.OpenQuantity <= 0 || openTrade.EntryQuantity <= 0)
                {
                    error = "ExitWithoutRecoverableEntry";
                    return false;
                }

                if (!string.IsNullOrEmpty(fromEntrySignal)
                    && IsEntrySignalName(fromEntrySignal)
                    && !string.Equals(fromEntrySignal, openTrade.EntrySignal, StringComparison.Ordinal))
                {
                    error = "ExitEntrySignalMismatch";
                    return false;
                }

                if (execution.Quantity > openTrade.OpenQuantity)
                {
                    error = "ExitQuantityExceedsRecoveredPosition";
                    return false;
                }

                var averageEntry = openTrade.EntryPriceQuantity / openTrade.EntryQuantity;
                var points = openTrade.Direction == PendingDirection.Long
                    ? execution.Price - averageEntry
                    : averageEntry - execution.Price;

                openTrade.GrossPnl += points * pointValue * execution.Quantity;
                openTrade.OpenQuantity -= execution.Quantity;

                if (openTrade.OpenQuantity == 0)
                {
                    snapshot.GrossPnl += openTrade.GrossPnl;
                    if (openTrade.GrossPnl > 0)
                        snapshot.Winners++;
                    else if (openTrade.GrossPnl < 0)
                        snapshot.Losses++;
                    openTrade = new LiveRecoveryOpenTrade();
                }
            }

            if (openTrade.OpenQuantity > 0)
            {
                error = "RecoveredStrategyTradeStillOpen";
                return false;
            }

            if (snapshot.Winners + snapshot.Losses > snapshot.Trades)
            {
                error = "RecoveredCounterInvariantFailed";
                return false;
            }

            return true;
        }

        private static bool LiveDailyStatesEquivalent(LiveDailyStateSnapshot left, LiveDailyStateSnapshot right)
        {
            return left != null && right != null
                   && left.TradingDate.Date == right.TradingDate.Date
                   && left.Trades == right.Trades
                   && left.Winners == right.Winners
                   && left.Losses == right.Losses
                   && Math.Abs(left.GrossPnl - right.GrossPnl) < 0.01;
        }

        private bool ShouldUseLiveDailyStateRecovery()
        {
            if (State != State.Realtime || Account == null)
                return false;

            try
            {
                var accountName = Account.Name ?? string.Empty;
                if (accountName.StartsWith("Playback", StringComparison.OrdinalIgnoreCase))
                    return false;

                var connectionName = Account.Connection != null && Account.Connection.Options != null
                    ? Account.Connection.Options.Name
                    : string.Empty;

                if (!string.IsNullOrEmpty(connectionName)
                    && connectionName.IndexOf("Playback", StringComparison.OrdinalIgnoreCase) >= 0)
                    return false;
            }
            catch
            {
                // If metadata is temporarily unavailable on a real-time strategy,
                // prefer the safer recovery path.
            }

            return true;
        }

        private static bool IsKnownLiveRecoveryExitName(string orderName)
        {
            return string.Equals(orderName, "Stop loss", StringComparison.Ordinal)
                   || string.Equals(orderName, "Profit target", StringComparison.Ordinal)
                   || string.Equals(orderName, LongEodExitSignal, StringComparison.Ordinal)
                   || string.Equals(orderName, ShortEodExitSignal, StringComparison.Ordinal)
                   || string.Equals(orderName, LongTimeExitSignal, StringComparison.Ordinal)
                   || string.Equals(orderName, ShortTimeExitSignal, StringComparison.Ordinal);
        }

        private DateTime GetCurrentStrategyTimeForRecovery()
        {
            try
            {
                if (CurrentBars != null && CurrentBars.Length > TickSeriesIndex && CurrentBars[TickSeriesIndex] >= 0)
                    return Times[TickSeriesIndex][0];
                if (CurrentBars != null && CurrentBars.Length > SignalSeriesIndex && CurrentBars[SignalSeriesIndex] >= 0)
                    return Times[SignalSeriesIndex][0];
                if (CurrentBars != null && CurrentBars.Length > ContextSeriesIndex && CurrentBars[ContextSeriesIndex] >= 0)
                    return Times[ContextSeriesIndex][0];
            }
            catch
            {
                // Fall back only when strategy data timestamps are unavailable.
            }
            return DateTime.Now;
        }

        private string BuildLiveDailyStatePath()
        {
            try
            {
                var directory = Path.Combine(Core.Globals.UserDataDir, "NinjexState", "OvernightEdgePortfolio");
                var fileName = "daily_state_"
                    + SanitizeFileName(GetLiveRecoveryAccountName()) + "_"
                    + SanitizeFileName(GetLiveRecoveryInstrumentName()) + ".state";
                return Path.Combine(directory, fileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetLiveRecoveryAccountName()
        {
            return Account == null ? string.Empty : Account.Name ?? string.Empty;
        }

        private string GetLiveRecoveryInstrumentName()
        {
            return Instrument == null ? string.Empty : Instrument.FullName ?? string.Empty;
        }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(
            Name = "Enable Research Telemetry",
            Description = "Writes candidate-signal and trade research rows to the NinjaTrader user-data NinjexResearch\\OvernightEdgePortfolio folder. Observational only; does not change entry/exit logic.",
            GroupName = "8. Diagnostics",
            Order = 2)]
        public bool EnableResearchTelemetry
        {
            get;
            set;
        }
    }
}
