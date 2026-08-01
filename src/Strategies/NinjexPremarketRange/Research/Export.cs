#region Using declarations
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private StreamWriter sessionWriter;
        private StreamWriter breakoutWriter;
        private StreamWriter candidateWriter;
        private StreamWriter tradeWriter;
        private StreamWriter dailyWriter;

        private void InitializeExport()
        {
            if (!EnableDataAnalysis)
                return;

            try
            {
                string folder = Path.Combine(
                    Globals.UserDataDir,
                    string.IsNullOrWhiteSpace(OutputFolderName)
                        ? "NinjexData"
                        : OutputFolderName.Trim());

                Directory.CreateDirectory(folder);

                string runId = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string prefix = string.IsNullOrWhiteSpace(OutputFilePrefix)
                    ? "premarket_range_research"
                    : OutputFilePrefix.Trim();

                sessionWriter = CreateWriter(
                    folder,
                    prefix + "_" + runId + "_sessions.csv",
                    "TradingDate,Instrument,PremarketHigh,PremarketLow,RangeTicks,TickSize,PointValue");

                breakoutWriter = CreateWriter(
                    folder,
                    prefix + "_" + runId + "_breakouts.csv",
                    "RecordType,EventId,TradingDate,Direction,Attempt,BreakoutTime,RangeLevel,BreakoutClose,DistanceOutsideTicks,Open,High,Low,Close,Volume,RangeTicks,BodyTicks,BodyPercent,CloseLocationPercent,RelativeBodyMultiple,RelativeVolumeMultiple,MfeTicks,MaeTicks,ReturnedInside,ReturnedInsideTime,BarsUntilReturnInside,Reached10,Reached20,Reached30,Reached40,Reached60,Reached100");

                candidateWriter = CreateWriter(
                    folder,
                    prefix + "_" + runId + "_candidates.csv",
                    "RecordType,CandidateId,BreakoutEventId,Model,Direction,SignalTime,SignalBarIndex,RangeLevel,Qualified,Reason,BarsAfterBreakout,RetestInsideDepthTicks,RetestOutsideDistanceTicks,ConfirmationOpen,ConfirmationHigh,ConfirmationLow,ConfirmationClose,RangeTicks,BodyTicks,BodyPercent,CloseLocationPercent,RelativeBodyMultiple,RelativeVolumeMultiple,StructuralStopPrice,EntryPrice,ActualStopPrice,StructuralRiskTicks,ActualRiskTicks,StopWasCapped");

                tradeWriter = CreateWriter(
                    folder,
                    prefix + "_" + runId + "_trades.csv",
                    "CandidateId,BreakoutEventId,Model,Direction,Policy,EntryTime,EntryPrice,StopPrice,InitialRiskTicks,ExitTime,ExitPrice,ExitReason,RealizedTicks,RealizedUsd,MfeTicks,MaeTicks,BreakEvenActivated,HighestTrailStepActivated");

                dailyWriter = CreateWriter(
                    folder,
                    prefix + "_" + runId + "_daily.csv",
                    "TradingDate,Instrument,BreakoutCount,LongBreakouts,ShortBreakouts,ReturnedInsideCount,QualifiedCandidates,RejectedCandidates,OpenHypotheticalVariants");
            }
            catch (Exception ex)
            {
                Print(Name + " | Export initialization failed: " + ex);
                CloseWriters();
            }
        }

        private StreamWriter CreateWriter(
            string folder,
            string fileName,
            string header)
        {
            var writer = new StreamWriter(
                Path.Combine(folder, fileName),
                false,
                new UTF8Encoding(false));

            writer.AutoFlush = true;
            writer.WriteLine(header);
            return writer;
        }

        private void ExportSession(RangeSessionContext session)
        {
            if (sessionWriter == null || session == null)
                return;

            sessionWriter.WriteLine(string.Join(",",
                Csv(session.TradingDate.ToString("yyyy-MM-dd")),
                Csv(Instrument.FullName),
                Num(session.PremarketHigh),
                Num(session.PremarketLow),
                Num(session.RangeTicks),
                Num(session.TickSize),
                Num(session.PointValue)));
        }

        private void ExportBreakout(BreakoutEvent breakout)
        {
            WriteBreakout("Created", breakout);
        }

        private void ExportBreakoutUpdate(BreakoutEvent breakout)
        {
            WriteBreakout("Updated", breakout);
        }

        private void WriteBreakout(string recordType, BreakoutEvent x)
        {
            if (breakoutWriter == null || x == null)
                return;

            CandleSnapshot c = x.Candle ?? new CandleSnapshot();
            CandleMetrics m = x.Metrics ?? new CandleMetrics();

            breakoutWriter.WriteLine(string.Join(",",
                Csv(recordType),
                Csv(x.EventId),
                Csv(x.TradingDate.ToString("yyyy-MM-dd")),
                Csv(x.Direction.ToString()),
                x.AttemptNumber.ToString(CultureInfo.InvariantCulture),
                Csv(x.BreakoutTime.ToString("O")),
                Num(x.RangeLevel),
                Num(x.BreakoutClose),
                Num(x.DistanceOutsideTicks),
                Num(c.Open),
                Num(c.High),
                Num(c.Low),
                Num(c.Close),
                Num(c.Volume),
                Num(m.RangeTicks),
                Num(m.BodyTicks),
                Num(m.BodyPercent),
                Num(m.CloseLocationPercent),
                Num(m.RelativeBodyMultiple),
                Num(m.RelativeVolumeMultiple),
                Num(x.MfeTicks),
                Num(x.MaeTicks),
                Bool(x.ReturnedInside),
                Csv(x.ReturnedInsideTime == DateTime.MinValue ? "" : x.ReturnedInsideTime.ToString("O")),
                x.BarsUntilReturnInside.ToString(CultureInfo.InvariantCulture),
                Bool(x.Reached10Ticks),
                Bool(x.Reached20Ticks),
                Bool(x.Reached30Ticks),
                Bool(x.Reached40Ticks),
                Bool(x.Reached60Ticks),
                Bool(x.Reached100Ticks)));
        }

        private void ExportCandidate(EntryCandidate candidate)
        {
            WriteCandidate("Created", candidate);
        }

        private void ExportCandidateUpdate(EntryCandidate candidate)
        {
            WriteCandidate("Filled", candidate);
        }

        private void WriteCandidate(string recordType, EntryCandidate x)
        {
            if (candidateWriter == null || x == null)
                return;

            CandleSnapshot c = x.ConfirmationCandle ?? new CandleSnapshot();
            CandleMetrics m = x.Metrics ?? new CandleMetrics();

            candidateWriter.WriteLine(string.Join(",",
                Csv(recordType),
                Csv(x.CandidateId),
                Csv(x.BreakoutEventId),
                Csv(x.ModelName),
                Csv(x.Direction.ToString()),
                Csv(x.SignalTime.ToString("O")),
                x.SignalBarIndex.ToString(CultureInfo.InvariantCulture),
                Num(x.RangeLevel),
                Bool(x.StrongCandleQualified),
                Csv(x.QualificationReason),
                x.BarsAfterBreakout.ToString(CultureInfo.InvariantCulture),
                Num(x.RetestInsideDepthTicks),
                Num(x.RetestOutsideDistanceTicks),
                Num(c.Open),
                Num(c.High),
                Num(c.Low),
                Num(c.Close),
                Num(m.RangeTicks),
                Num(m.BodyTicks),
                Num(m.BodyPercent),
                Num(m.CloseLocationPercent),
                Num(m.RelativeBodyMultiple),
                Num(m.RelativeVolumeMultiple),
                Num(x.StructuralStopPrice),
                Num(x.PlannedEntryPrice),
                Num(x.PlannedStopPrice),
                Num(x.StructuralRiskTicks),
                Num(x.ActualRiskTicks),
                Bool(x.StopWasCapped)));
        }

        private void ExportTrade(HypotheticalTrade trade)
        {
            if (tradeWriter == null || trade == null)
                return;

            EntryCandidate c = trade.Candidate;
            ManagementOutcome o = trade.Outcome;

            tradeWriter.WriteLine(string.Join(",",
                Csv(c.CandidateId),
                Csv(c.BreakoutEventId),
                Csv(c.ModelName),
                Csv(c.Direction.ToString()),
                Csv(trade.PolicyName),
                Csv(trade.EntryTime.ToString("O")),
                Num(trade.EntryPrice),
                Num(c.PlannedStopPrice),
                Num(c.ActualRiskTicks),
                Csv(o.ExitTime.ToString("O")),
                Num(o.ExitPrice),
                Csv(o.ExitReason),
                Num(o.RealizedTicks),
                Num(o.RealizedUsd),
                Num(o.MfeTicks),
                Num(o.MaeTicks),
                Bool(o.BreakEvenActivated),
                o.HighestTrailStepActivated.ToString(CultureInfo.InvariantCulture)));
        }

        private void ExportDailySummary(DateTime date)
        {
            if (dailyWriter == null)
                return;

            dailyWriter.WriteLine(string.Join(",",
                Csv(date.ToString("yyyy-MM-dd")),
                Csv(Instrument.FullName),
                breakoutEvents.Count.ToString(CultureInfo.InvariantCulture),
                breakoutEvents.Count(x => x.Direction == TradeDirection.Long).ToString(CultureInfo.InvariantCulture),
                breakoutEvents.Count(x => x.Direction == TradeDirection.Short).ToString(CultureInfo.InvariantCulture),
                breakoutEvents.Count(x => x.ReturnedInside).ToString(CultureInfo.InvariantCulture),
                entryCandidates.Count(x => x.StrongCandleQualified).ToString(CultureInfo.InvariantCulture),
                entryCandidates.Count(x => !x.StrongCandleQualified).ToString(CultureInfo.InvariantCulture),
                activeTrades.Count.ToString(CultureInfo.InvariantCulture)));
        }

        private void FlushAndDisposeExport()
        {
            if (activeTradingDate != Core.Globals.MinDate)
                ExportDailySummary(activeTradingDate);

            CloseWriters();
        }

        private void CloseWriters()
        {
            CloseWriter(ref sessionWriter);
            CloseWriter(ref breakoutWriter);
            CloseWriter(ref candidateWriter);
            CloseWriter(ref tradeWriter);
            CloseWriter(ref dailyWriter);
        }

        private static void CloseWriter(ref StreamWriter writer)
        {
            if (writer == null)
                return;

            try
            {
                writer.Flush();
                writer.Dispose();
            }
            catch
            {
            }
            finally
            {
                writer = null;
            }
        }

        private static string Csv(string value)
        {
            string text = value ?? "";
            return "\"" + text.Replace("\"", "\"\"") + "\"";
        }

        private static string Num(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string Bool(bool value)
        {
            return value ? "1" : "0";
        }
    }
}
