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
            ResetResearchPerturbationDailyState();
        }

        private void CaptureResearchPreviousFiveMinuteContext(double previousEmaFast, double previousEmaSlow)
        {
            researchPreviousEmaFast = previousEmaFast;
            researchPreviousEmaSlow = previousEmaSlow;
        }

        private void CaptureFourModelResearchCandidates(
            DateTime signalTime, double signalOpen, double high, double low, double close, double previousClose,
            int minutesFromOpen, double overnightWidthTicks)
        {
            if (!EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            var range1mTicks = TickSize > 0 ? (high - low) / TickSize : double.NaN;
            var bodyTicks = TickSize > 0 ? Math.Abs(close - signalOpen) / TickSize : double.NaN;
            var premarketWidthTicks = GetPremarketWidthTicks();
            var priorCloseCross = IsFinite(priorDayClose) && priorDayCloseDate < signalTime.Date && previousClose <= priorDayClose && close > priorDayClose;
            var priorRangeOk = IsFinite(range1mTicks) && range1mTicks <= PriorCloseMaximumRangeTicks;
            var priorWidthOk = IsFinite(overnightWidthTicks) && overnightWidthTicks >= PriorCloseMinimumOvernightWidthTicks;
            var pdcEarlyWindow = minutesFromOpen >= 0 && minutesFromOpen <= PdcEarlyMaximumMinutesFromOpen;
            var priorPremarketWidthOk = !EnablePdcEarlyPremarketWidthFilter
                || !pdcEarlyWindow
                || (IsFinite(premarketWidthTicks) && premarketWidthTicks >= PdcEarlyMinimumPremarketWidthTicks);
            var priorQualified = priorCloseCross && priorRangeOk && priorWidthOk && priorPremarketWidthOk;

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
            var useFastEmaFilter = EnableEMAFilter;
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
                    IsResearchSignalPortfolioEligible(PriorCloseEntrySignal), GetResearchPortfolioBlockReasonForSignal(PriorCloseEntrySignal, false), qualifiedCount, priorRangeOk, null, priorWidthOk, priorPremarketWidthOk, null,
                    TickSize > 0 ? (close - priorDayClose) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (pmhSweep)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    PremarketHighModelName, PremarketHighEntrySignal, PendingDirection.Short, pmhQualified, selectedSignal == PremarketHighEntrySignal,
                    IsResearchSignalPortfolioEligible(PremarketHighEntrySignal), GetResearchPortfolioBlockReasonForSignal(PremarketHighEntrySignal, false), qualifiedCount, null, pmhTimeOk, null, null, pmhAtrOk,
                    TickSize > 0 ? (premarketHigh - close) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (rthCross)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    RthOpenModelName, RthOpenEntrySignal, PendingDirection.Short, rthQualified, selectedSignal == RthOpenEntrySignal,
                    IsResearchSignalPortfolioEligible(RthOpenEntrySignal), GetResearchPortfolioBlockReasonForSignal(RthOpenEntrySignal, false), qualifiedCount, null, rthTimeOk, null, rthWidthOk, null,
                    TickSize > 0 ? (rthOpen - close) / TickSize : double.NaN, range1mTicks, bodyTicks);

            if (pmlCross)
                WriteCandidateResearchRow(signalTime, signalOpen, high, low, close, previousClose, minutesFromOpen, overnightWidthTicks, premarketWidthTicks,
                    PremarketLowModelName, PremarketLowEntrySignal, PendingDirection.Short, pmlQualified, selectedSignal == PremarketLowEntrySignal,
                    IsResearchSignalPortfolioEligible(PremarketLowEntrySignal), GetResearchPortfolioBlockReasonForSignal(PremarketLowEntrySignal, false), qualifiedCount, null, null, null, null, pmlAtrOk,
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
            RegisterResearchPerturbationTrade(
                activeResearchSignal,
                time,
                entryPrice,
                activeMaxHoldExitTime);
        }

        private void RecordResearchTradeExit(DateTime time, string exitName, double exitPrice)
        {
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
            CompleteResearchPerturbationActualTrade(
                time,
                exitPrice,
                exitName ?? string.Empty);
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
                PortfolioMode = "Run8FourModel",
                Model = modelName ?? string.Empty,
                Signal = signalName ?? string.Empty,
                Direction = direction.ToString()
            };
        }

        private bool IsResearchSignalPortfolioEligible(string signalName)
        {
            return string.IsNullOrEmpty(
                GetResearchPortfolioBlockReasonForSignal(signalName, false));
        }

        private string GetResearchPortfolioBlockReasonForSignal(
            string signalName,
            bool allowExistingPendingEntry)
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

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(
            Name = "Enable Research Telemetry",
            Description = "Writes candidate-signal and trade research rows to the NinjaTrader user-data NinjexResearch\\OvernightEdgePortfolio folder. Observational only; does not change entry/exit logic.",
            GroupName = "5. Diagnostics",
            Order = 2)]
        public bool EnableResearchTelemetry
        {
            get;
            set;
        }

        [NinjaScriptProperty]
        [System.ComponentModel.DataAnnotations.Display(
            Name = "Enable Research Perturbation Scenarios",
            Description = "Research-only tick-level scenarios for entry fill sensitivity, target distance and break-even policies. Requires Research Telemetry. Never submits or changes real orders.",
            GroupName = "5. Diagnostics",
            Order = 3)]
        public bool EnableResearchPerturbationScenarios
        {
            get;
            set;
        }
    }
}
