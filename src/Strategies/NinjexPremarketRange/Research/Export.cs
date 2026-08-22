#region Using declarations
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NinjaTrader.Core;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Analysis;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Risk;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexPremarketRangeResearch
    {
        private StreamWriter manifestWriter;
        private StreamWriter sessionWriter;
        private StreamWriter breakoutAuditWriter;
        private StreamWriter breakoutFinalWriter;
        private StreamWriter candidateWriter;
        private StreamWriter tradeWriter;
        private StreamWriter dailyWriter;
        private StreamWriter riskScenarioWriter;
        private StreamWriter executionEquityWriter;
        private string exportBaseName;

        private void InitializeExport()
        {
            if (!EnableDataAnalysis)
                return;

            try
            {
                var folder = Path.Combine(Globals.UserDataDir,
                    string.IsNullOrWhiteSpace(OutputFolderName) ? "NinjexData" : OutputFolderName.Trim());
                Directory.CreateDirectory(folder);

                var prefix = string.IsNullOrWhiteSpace(OutputFilePrefix)
                    ? "premarket_range_research"
                    : OutputFilePrefix.Trim();
                var contract = SanitizeFileName(Instrument.FullName);
                exportBaseName = $"{prefix}_{runId}_{contract}_{strategyInstanceId}";

                manifestWriter = CreateWriter(folder, exportBaseName + "_manifest.csv",
                    "RunId,StrategyInstanceId,Version,Instrument,Contract,DataSource,ContextTimeframe,EntryTimeframe,PrecisionTickAnalysis,TradingHours,RangeStartTime,MarketOpenTime,EntryStartTime,EntryEndTime,FlattenTime,MinimumBreakoutDistanceTicks,EntryMinDistanceTicks,EntryMaxDistanceTicks,MaximumRetestBars,RetestOutsideTicks,RetestInsideTicks,MinimumStrongBodyPercent,MinimumCloseLocationPercent,RelativeBodyLookback,MinimumRelativeBodyMultiple,MinimumRetestConfirmationBodyPercent,Atr1MinutePeriod,Atr5MinutePeriod,Adx5MinutePeriod,EnableAcceptanceModel,MinimumAcceptanceClosesOutside,MaximumAcceptanceBars,MinimumAcceptanceExcursionTicks,MinimumAcceptanceCloseDistanceTicks,AllowAcceptanceLaterAttempts,MinimumAcceptancePriorFailedAttempts,MaximumInitialStopTicks,RiskRewardRatio,Quantity,BETriggerTicks,BEPlusTicks,CreatedAt");

                sessionWriter = CreateWriter(folder, exportBaseName + "_sessions.csv",
                    "RecordType,RunId,StrategyInstanceId,Version,Instrument,Contract,TradingDate,PremarketHigh,PremarketLow,RangeTicks,HighFormationTime,LowFormationTime,TickSize,PointValue,FiveMinuteRangeBarCount,OneMinuteEntryWindowBarCount,TickCount,HasFiveMinuteData,HasOneMinuteData,HasTickData,FirstFiveMinuteBarTime,LastFiveMinuteBarTime,FirstOneMinuteBarTime,LastOneMinuteBarTime,FirstTickTime,LastTickTime,DataQualityStatus");

                breakoutAuditWriter = CreateWriter(folder, exportBaseName + "_breakouts_audit.csv", BreakoutHeader("RecordType"));
                breakoutFinalWriter = CreateWriter(folder, exportBaseName + "_breakouts_final.csv", BreakoutHeader(null));

                candidateWriter = CreateWriter(folder, exportBaseName + "_candidates.csv",
                    "RecordType,RunId,StrategyInstanceId,Version,Instrument,Contract,CandidateId,BreakoutEventId,Model,ModelVersion,QualificationCode,Direction,SignalTime,SignalBarIndex,RangeLevel,Qualified,DirectionPassed,BodyPassed,CloseLocationPassed,RelativeBodyPassed,FinalStatus,IsFinalized,FinalizedAt,Reason,BarsAfterBreakout,RetestInsideDepthTicks,RetestOutsideDistanceTicks,ConfirmationOpen,ConfirmationHigh,ConfirmationLow,ConfirmationClose,RangeTicks,BodyTicks,BodyPercent,UpperWickTicks,LowerWickTicks,CloseLocationPercent,AverageBodyTicks,RelativeBodyMultiple,AverageVolume,RelativeVolumeMultiple,FeatureCapturedAt,Atr1MinuteTicks,Atr5MinuteTicks,Adx5Minute,PremarketRangeToAtr5Minute,BreakoutDistanceToAtr1Minute,StructuralRiskToAtr1Minute,ConsecutiveClosesOutside,BarsContinuouslyOutside,MinimumCloseDistanceOutsideTicks,MaximumExcursionSinceBreakoutTicks,AttemptNumber,PriorSameDirectionAttempts,PriorOppositeDirectionAttempts,PriorReturnsInside15Minutes,PriorReturnsInside30Minutes,PriorReturnsInside60Minutes,MinutesSincePreviousAttempt,PreviousAttemptMfeTicks,PreviousAttemptBarsUntilReturnInside,BothRangeSidesBroken,StructuralStopPrice,EntryTime,EntryPrice,EntryDistanceTicks,ActualStopPrice,StructuralRiskTicks,ActualRiskTicks,StopWasCapped");

                tradeWriter = CreateWriter(folder, exportBaseName + "_trades.csv",
                    "RunId,StrategyInstanceId,Version,Instrument,Contract,CandidateId,BreakoutEventId,Model,Direction,Policy,EntryTime,EntryPrice,StopPrice,InitialRiskTicks,ExitTime,ExitPrice,ExitReason,RealizedTicks,RealizedUsd,MfeTicks,MaeTicks,BreakEvenActivated,HighestTrailStepActivated");

                riskScenarioWriter =
                    CreateWriter(
                        folder,
                        exportBaseName
                        + "_risk_scenarios.csv",
                        "RunId,StrategyInstanceId,Version,Instrument,Contract," +
                        "CandidateId,BreakoutEventId,Model,Direction," +
                        "ScenarioId,MaximumStopTicks,RiskRewardRatio," +
                        "EntryTime,EntryPrice,StructuralRiskTicks," +
                        "InitialRiskTicks,StopWasCapped,StopPrice,TargetPrice," +
                        "ExitTime,ExitPrice,ExitReason," +
                        "RealizedTicks,RealizedR,RealizedUsd," +
                        "MfeTicks,MaeTicks");
                
                dailyWriter =
                    CreateWriter(
                        folder,
                        exportBaseName + "_daily.csv",
                        "RunId,StrategyInstanceId,Version,Instrument,Contract,TradingDate," +
                        "BreakoutCount,LongBreakouts,ShortBreakouts," +
                        "Fakeout20Count,QualifiedCandidates,RejectedCandidates," +
                        "CompletedTrades,CandidatesCreated,CandidatesFinalized," +
                        "CandidatesUnresolved");
                
                executionEquityWriter =
                    CreateWriter(
                        folder,
                        exportBaseName
                        + "_execution_equity.csv",
                        "RunId,StrategyInstanceId,Version,Instrument,Contract," +
                        "ObservedAt,TradingDate,CandidateId,Direction," +
                        "MarketPrice,PositionQuantity,PositionOpen," +
                        "RealizedPnl,UnrealizedPnl,EquityDelta," +
                        "PeakEquityDelta,DrawdownFromPeak");
            }
            catch (Exception ex)
            {
                Print(Name + " | Export initialization failed: " + ex);
                CloseWriters();
            }
        }

        private static string BreakoutHeader(
            string firstColumn)
        {
            var prefix =
                string.IsNullOrEmpty(firstColumn)
                    ? ""
                    : firstColumn + ",";

            return prefix +
                   "RunId,StrategyInstanceId,Version,Instrument,Contract," +
                   "EventId,TradingDate,Direction,Attempt," +
                   "BreakoutTime,RangeLevel,BreakoutClose," +
                   "DistanceOutsideTicks,Open,High,Low,Close," +
                   "Volume,RangeTicks,BodyTicks,BodyPercent," +
                   "UpperWickTicks,LowerWickTicks," +
                   "CloseLocationPercent,AverageBodyTicks," +
                   "RelativeBodyMultiple,AverageVolume," +
                   "RelativeVolumeMultiple,MfeTicks,MaeTicks," +
                   "ReturnedInside,ReturnedInsideTime," +
                   "BarsUntilReturnInside,MfeBeforeReturnTicks," +
                   "IsFakeout20Ticks,Reached10,TimeTo10," +
                   "Reached20,TimeTo20,Reached30,TimeTo30," +
                   "Reached40,TimeTo40,Reached60,TimeTo60," +
                   "Reached100,TimeTo100,Mfe1m,Mae1m," +
                   "Mfe2m,Mae2m,Mfe3m,Mae3m,Mfe5m,Mae5m," +
                   "Mfe10m,Mae10m,Mfe15m,Mae15m," +
                   "Mfe30m,Mae30m,Mfe60m,Mae60m," +
                   "RawRetestArmed," +
                   "RawRetestArmedBarIndex," +
                   "FurthestExcursionBeforeRawRetestTicks," +
                   "FirstRawRetestBarIndex," +
                   "FirstRawRetestReferencePrice," +
                   "RawRetestObserved,FirstRawRetestTime," +
                   "FirstRawRetestBarsAfterBreakout," +
                   "FirstRawRetestMinutesAfterBreakout," +
                   "FirstRawRetestInsideDepthTicks," +
                   "FirstRawRetestOutsideDistanceTicks," +
                   "FirstRawRetestWithinDepthTolerance," +
                   "RawRetestMaximumInsideDepthTicks," +
                   "RawRetestWithinDepthTolerance," +
                   "RawRetestMinimumOutsideDistanceTicks," +
                   "RawRetestTouchedExactLevel," +
                   "RawRetestWasWithinModelBarWindow," +
                   "RawRetestConfirmed," +
                   "RawRetestConfirmationTime," +
                   "MfeBeforeRawRetestTicks," +
                   "MfeAfterRawRetestTicks," +
                   "RawRetestStatus," +
                   "FeatureCapturedAt,Atr1MinuteTicks,Atr5MinuteTicks,Adx5Minute," +
                   "PremarketRangeToAtr5Minute,BreakoutDistanceToAtr1Minute," +
                   "ConsecutiveClosesOutside,BarsContinuouslyOutside," +
                   "MinimumCloseDistanceOutsideTicks,MaximumExcursionSinceBreakoutTicks," +
                   "PriorSameDirectionAttempts,PriorOppositeDirectionAttempts," +
                   "PriorReturnsInside15Minutes,PriorReturnsInside30Minutes,PriorReturnsInside60Minutes," +
                   "MinutesSincePreviousAttempt,PreviousAttemptMfeTicks," +
                   "PreviousAttemptBarsUntilReturnInside,BothRangeSidesBroken," +
                   "ResolutionTime,ResolutionReason";
        }

        private StreamWriter CreateWriter(string folder, string fileName, string header)
        {
            var writer = new StreamWriter(Path.Combine(folder, fileName), false, new UTF8Encoding(false));
            writer.AutoFlush = false;
            writer.WriteLine(header);
            return writer;
        }

        private void ExportManifest()
        {
            if (manifestWriter == null)
                return;

            manifestWriter.WriteLine(string.Join(",",
                Csv(runId), Csv(strategyInstanceId), Csv(Version), Csv(Instrument.MasterInstrument.Name), Csv(Instrument.FullName), Csv(DataSourceLabel),
                Csv("5 Minute"), Csv("1 Minute"), Bool(EnablePrecisionTickAnalysis), Csv(Bars?.TradingHours?.Name ?? ""),
                RangeStartTime, MarketOpenTime, EntryStartTime, EntryEndTime, FlattenTime,
                MinimumBreakoutDistanceTicks, EntryMinimumDistanceTicksFromRange, EntryMaximumDistanceTicksFromRange,
                MaximumRetestBars, RetestOutsideDistanceTicks, RetestInsideDistanceTicks,
                Num(MinimumStrongBodyPercent), Num(MinimumCloseLocationPercent), RelativeBodyLookback,
                Num(MinimumRelativeBodyMultiple), Num(MinimumRetestConfirmationBodyPercent),
                Atr1MinutePeriod,
                Atr5MinutePeriod,
                Adx5MinutePeriod,
                Bool(EnableAcceptanceModel), MinimumAcceptanceClosesOutside, MaximumAcceptanceBars,
                Num(MinimumAcceptanceExcursionTicks), Num(MinimumAcceptanceCloseDistanceTicks),
                Bool(AllowAcceptanceLaterAttempts), MinimumAcceptancePriorFailedAttempts,
                MaximumInitialStopTicks, Num(RiskRewardRatio), Quantity, BEProfitTriggerTicks, BEPlusTicks, Csv(DateTime.Now.ToString("O"))));
            manifestWriter.Flush();
        }

        private void ExportSession(string recordType, RangeSessionContext s, SessionDataQuality q)
        {
            if (sessionWriter == null || s == null)
                return;
            q = q ?? new SessionDataQuality();
            sessionWriter.WriteLine(string.Join(",",
                Csv(recordType), Csv(runId), Csv(strategyInstanceId), Csv(Version), Csv(s.Instrument), Csv(s.Contract), Csv(s.TradingDate.ToString("yyyy-MM-dd")),
                Num(s.PremarketHigh), Num(s.PremarketLow), Num(s.RangeTicks), Dt(s.HighFormationTime), Dt(s.LowFormationTime), Num(s.TickSize), Num(s.PointValue),
                q.FiveMinuteRangeBarCount, q.OneMinuteEntryWindowBarCount, q.TickCount, Bool(q.HasFiveMinuteData), Bool(q.HasOneMinuteData), Bool(q.HasTickData),
                Dt(q.FirstFiveMinuteBarTime), Dt(q.LastFiveMinuteBarTime), Dt(q.FirstOneMinuteBarTime), Dt(q.LastOneMinuteBarTime), Dt(q.FirstTickTime), Dt(q.LastTickTime), Csv(q.Status)));
        }

        private void ExportBreakoutAudit(string recordType, BreakoutEvent x)
        {
            breakoutAuditWriter?.WriteLine(BreakoutRow(x, recordType));
        }

        private void ExportBreakoutFinal(BreakoutEvent x)
        {
            if (breakoutFinalWriter != null)
            {
                breakoutFinalWriter.WriteLine(BreakoutRow(x, null));
                breakoutFinalWriter.Flush();
            }
        }

        private string BreakoutRow(BreakoutEvent x, string recordType)
        {
            var c = x.Candle ?? new CandleSnapshot();
            var m = x.Metrics ?? new CandleMetrics();
            var prefix = recordType == null ? "" : Csv(recordType) + ",";
            return prefix + string.Join(",",
                Csv(runId), Csv(strategyInstanceId), Csv(Version), Csv(Instrument.MasterInstrument.Name), Csv(x.Contract),
                Csv(x.EventId), Csv(x.TradingDate.ToString("yyyy-MM-dd")), Csv(x.Direction.ToString()), x.AttemptNumber,
                Dt(x.BreakoutTime), Num(x.RangeLevel), Num(x.BreakoutClose), Num(x.DistanceOutsideTicks), Num(c.Open),
                Num(c.High), Num(c.Low), Num(c.Close), Num(c.Volume),
                Num(m.RangeTicks), Num(m.BodyTicks), Num(m.BodyPercent), Num(m.UpperWickTicks), Num(m.LowerWickTicks),
                Num(m.CloseLocationPercent), Num(m.AverageBodyTicks), Num(m.RelativeBodyMultiple), Num(m.AverageVolume),
                Num(m.RelativeVolumeMultiple),
                Num(x.MfeTicks), Num(x.MaeTicks), Bool(x.ReturnedInside), Dt(x.ReturnedInsideTime),
                x.BarsUntilReturnInside, Num(x.MfeBeforeReturnTicks), Bool(x.IsFakeout20Ticks),
                Bool(x.Reached10Ticks), Dt(x.TimeTo10Ticks), Bool(x.Reached20Ticks), Dt(x.TimeTo20Ticks),
                Bool(x.Reached30Ticks), Dt(x.TimeTo30Ticks), Bool(x.Reached40Ticks), Dt(x.TimeTo40Ticks),
                Bool(x.Reached60Ticks), Dt(x.TimeTo60Ticks), Bool(x.Reached100Ticks), Dt(x.TimeTo100Ticks),
                Num(x.Mfe1Minute), Num(x.Mae1Minute), Num(x.Mfe2Minutes), Num(x.Mae2Minutes), Num(x.Mfe3Minutes),
                Num(x.Mae3Minutes), Num(x.Mfe5Minutes), Num(x.Mae5Minutes), Num(x.Mfe10Minutes), Num(x.Mae10Minutes),
                Num(x.Mfe15Minutes), Num(x.Mae15Minutes), Num(x.Mfe30Minutes), Num(x.Mae30Minutes), Num(x.Mfe60Minutes),
                Num(x.Mae60Minutes),
                Bool(x.RawRetestArmed),
                x.RawRetestArmedBarIndex,
                Num(x.FurthestExcursionBeforeRawRetestTicks),
                x.FirstRawRetestBarIndex,
                Num(x.FirstRawRetestReferencePrice),
                Bool(x.RawRetestObserved),
                Dt(x.FirstRawRetestTime),
                x.FirstRawRetestBarsAfterBreakout,
                Num(x.FirstRawRetestMinutesAfterBreakout),
                Num(x.FirstRawRetestInsideDepthTicks),
                Num(x.FirstRawRetestOutsideDistanceTicks),
                Bool(x.FirstRawRetestWithinDepthTolerance),
                Num(x.RawRetestMaximumInsideDepthTicks),
                Bool(x.RawRetestWithinDepthTolerance),
                Num(
                    x.RawRetestMinimumOutsideDistanceTicks
                    == double.MaxValue
                        ? 0
                        : x.RawRetestMinimumOutsideDistanceTicks),
                Bool(x.RawRetestTouchedExactLevel),
                Bool(x.RawRetestWasWithinModelBarWindow),
                Bool(x.RawRetestConfirmed),
                Dt(x.RawRetestConfirmationTime),
                Num(x.MfeBeforeRawRetestTicks),
                Num(x.MfeAfterRawRetestTicks),
                Csv(x.RawRetestStatus),
                Dt((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).CapturedAt),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).Atr1MinuteTicks),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).Atr5MinuteTicks),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).Adx5Minute),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PremarketRangeToAtr5Minute),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).BreakoutDistanceToAtr1Minute),
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).ConsecutiveClosesOutside,
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).BarsContinuouslyOutside,
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).MinimumCloseDistanceOutsideTicks),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).MaximumExcursionSinceBreakoutTicks),
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PriorSameDirectionAttempts,
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PriorOppositeDirectionAttempts,
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PriorReturnsInside15Minutes,
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PriorReturnsInside30Minutes,
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PriorReturnsInside60Minutes,
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).MinutesSincePreviousAttempt),
                Num((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PreviousAttemptMfeTicks),
                (x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).PreviousAttemptBarsUntilReturnInside,
                Bool((x.FeatureSnapshot ?? CandidateFeatureSnapshot.Empty).BothRangeSidesBroken),
                Dt(x.ResolutionTime),
                Csv(x.ResolutionReason));
        }

        private void ExportCandidate(string recordType, EntryCandidate x)
        {
            if (candidateWriter == null || x == null)
                return;

            var c = x.ConfirmationCandle ?? new CandleSnapshot();
            var m = x.Metrics ?? new CandleMetrics();
            var f = x.Features ?? CandidateFeatureSnapshot.Empty;

            candidateWriter.WriteLine(string.Join(",",
                Csv(recordType), Csv(runId), Csv(strategyInstanceId), Csv(Version), Csv(Instrument.MasterInstrument.Name), Csv(Instrument.FullName),
                Csv(x.CandidateId), Csv(x.BreakoutEventId), Csv(x.ModelName), Csv(x.ModelVersion), Csv(x.QualificationCode),
                Csv(x.Direction.ToString()), Dt(x.SignalTime), x.SignalBarIndex, Num(x.RangeLevel), Bool(x.StrongCandleQualified),
                Bool(x.DirectionPassed), Bool(x.BodyPassed), Bool(x.CloseLocationPassed), Bool(x.RelativeBodyPassed),
                Csv(x.FinalStatus),
                Bool(x.IsFinalized),
                Dt(x.FinalizedAt),
                Csv(x.QualificationReason),
                x.BarsAfterBreakout, Num(x.RetestInsideDepthTicks), Num(x.RetestOutsideDistanceTicks),
                Num(c.Open), Num(c.High), Num(c.Low), Num(c.Close), Num(m.RangeTicks), Num(m.BodyTicks), Num(m.BodyPercent),
                Num(m.UpperWickTicks), Num(m.LowerWickTicks), Num(m.CloseLocationPercent), Num(m.AverageBodyTicks),
                Num(m.RelativeBodyMultiple), Num(m.AverageVolume), Num(m.RelativeVolumeMultiple),
                Dt(f.CapturedAt), Num(f.Atr1MinuteTicks), Num(f.Atr5MinuteTicks), Num(f.Adx5Minute),
                Num(f.PremarketRangeToAtr5Minute), Num(f.BreakoutDistanceToAtr1Minute), Num(f.StructuralRiskToAtr1Minute),
                f.ConsecutiveClosesOutside, f.BarsContinuouslyOutside, Num(f.MinimumCloseDistanceOutsideTicks),
                Num(f.MaximumExcursionSinceBreakoutTicks), f.AttemptNumber, f.PriorSameDirectionAttempts,
                f.PriorOppositeDirectionAttempts, f.PriorReturnsInside15Minutes, f.PriorReturnsInside30Minutes,
                f.PriorReturnsInside60Minutes, Num(f.MinutesSincePreviousAttempt), Num(f.PreviousAttemptMfeTicks),
                f.PreviousAttemptBarsUntilReturnInside, Bool(f.BothRangeSidesBroken),
                Num(x.StructuralStopPrice), Dt(x.PlannedEntryTime), Num(x.PlannedEntryPrice), Num(x.EntryDistanceTicks),
                Num(x.PlannedStopPrice), Num(x.StructuralRiskTicks), Num(x.ActualRiskTicks), Bool(x.StopWasCapped)));
        }

        private void ExportTrade(HypotheticalTrade t)
        {
            if (tradeWriter == null || t == null)
                return;
            
            var c = t.Candidate;
            var o = t.Outcome;
            
            tradeWriter.WriteLine(string.Join(",",
                Csv(runId), Csv(strategyInstanceId), Csv(Version), Csv(Instrument.MasterInstrument.Name), Csv(Instrument.FullName), Csv(c.CandidateId), Csv(c.BreakoutEventId), Csv(c.ModelName), Csv(c.Direction.ToString()), Csv(t.PolicyName), Dt(t.EntryTime), Num(t.EntryPrice), Num(c.PlannedStopPrice), Num(c.ActualRiskTicks), Dt(o.ExitTime), Num(o.ExitPrice), Csv(o.ExitReason), Num(o.RealizedTicks), Num(o.RealizedUsd), Num(o.MfeTicks), Num(o.MaeTicks), Bool(o.BreakEvenActivated), o.HighestTrailStepActivated));
            
            completedTradeCount++;
        }

        private void ExportDailySummary(
            DateTime date)
        {
            date = date.Date;

            if (dailyWriter == null
                || date == Globals.MinDate.Date
                || !exportedDailySummaryDates.Add(date))
            {
                return;
            }

            var candidatesCreated =
                entryCandidates.Count;

            var candidatesFinalized =
                entryCandidates.Count(
                    x => x != null
                         && x.IsFinalized);

            var candidatesUnresolved =
                candidatesCreated
                - candidatesFinalized;

            dailyWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(runId),
                    Csv(strategyInstanceId),
                    Csv(Version),
                    Csv(
                        Instrument.MasterInstrument.Name),
                    Csv(Instrument.FullName),
                    Csv(
                        date.ToString(
                            "yyyy-MM-dd")),
                    breakoutEvents.Count,
                    breakoutEvents.Count(
                        x => x.Direction
                             == TradeDirection.Long),
                    breakoutEvents.Count(
                        x => x.Direction
                             == TradeDirection.Short),
                    breakoutEvents.Count(
                        x => x.IsFakeout20Ticks),
                    entryCandidates.Count(
                        x => x.StrongCandleQualified),
                    entryCandidates.Count(
                        x => x.IsFinalized
                             && (
                                 string.Equals(
                                     x.FinalStatus,
                                     "SignalRejected",
                                     StringComparison.Ordinal)
                                 || x.FinalStatus.StartsWith(
                                     "Rejected",
                                     StringComparison.Ordinal)
                                 || x.FinalStatus.StartsWith(
                                     "Skipped",
                                     StringComparison.Ordinal))),
                    completedTradeCount,
                    candidatesCreated,
                    candidatesFinalized,
                    candidatesUnresolved));
        }
        
        private void ExportRiskScenarioTrade(
            RiskScenarioTradeResult trade)
        {
            if (riskScenarioWriter == null
                || trade == null
                || trade.RiskScenario == null)
            {
                return;
            }

            var candidate =
                trade.Candidate;

            var scenario =
                trade.RiskScenario;

            var outcome =
                trade.Outcome;

            if (candidate == null
                || outcome == null)
            {
                return;
            }

            var realizedR =
                trade.InitialRiskTicks > 0
                    ? outcome.RealizedTicks
                      / trade.InitialRiskTicks
                    : 0;

            riskScenarioWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(runId),
                    Csv(strategyInstanceId),
                    Csv(Version),
                    Csv(
                        Instrument
                            .MasterInstrument
                            .Name),
                    Csv(Instrument.FullName),

                    Csv(candidate.CandidateId),
                    Csv(candidate.BreakoutEventId),
                    Csv(candidate.ModelName),
                    Csv(candidate.Direction.ToString()),

                    Csv(scenario.ScenarioId),
                    scenario.MaximumInitialStopTicks,
                    Num(scenario.RiskRewardRatio),

                    Dt(trade.EntryTime),
                    Num(trade.EntryPrice),

                    Num(
                        candidate
                            .StructuralRiskTicks),

                    Num(
                        trade.InitialRiskTicks),

                    Bool(
                        trade.StopWasCapped),

                    Num(
                        trade.InitialStopPrice),

                    Num(
                        trade.TargetPrice),

                    Dt(
                        outcome.ExitTime),

                    Num(
                        outcome.ExitPrice),

                    Csv(
                        outcome.ExitReason),

                    Num(
                        outcome.RealizedTicks),

                    Num(
                        realizedR),

                    Num(
                        outcome.RealizedUsd),

                    Num(
                        outcome.MfeTicks),

                    Num(
                        outcome.MaeTicks)));
        }
        
        private void ExportExecutionEquitySnapshot(
            ExecutionEquitySnapshot snapshot)
        {
            if (executionEquityWriter == null
                || snapshot == null)
            {
                return;
            }

            executionEquityWriter.WriteLine(
                string.Join(
                    ",",
                    Csv(runId),
                    Csv(strategyInstanceId),
                    Csv(Version),
                    Csv(
                        Instrument
                            .MasterInstrument
                            .Name),
                    Csv(
                        Instrument.FullName),

                    Dt(
                        snapshot.ObservedAt),

                    Csv(
                        snapshot.TradingDate
                            .ToString(
                                "yyyy-MM-dd")),

                    Csv(
                        snapshot.CandidateId),

                    Csv(
                        snapshot.Direction?
                            .ToString()
                        ?? string.Empty),

                    Num(
                        snapshot.MarketPrice),

                    snapshot.PositionQuantity,

                    Bool(
                        snapshot.PositionOpen),

                    Num(
                        snapshot.RealizedPnl),

                    Num(
                        snapshot.UnrealizedPnl),

                    Num(
                        snapshot.EquityDelta),

                    Num(
                        snapshot.PeakEquityDelta),

                    Num(
                        snapshot.DrawdownFromPeak)));
        }

        private void FlushAndDisposeExport()
        {
            if (activeTradingDate != Globals.MinDate)
                ExportDailySummary(activeTradingDate);
            CloseWriters();
        }

        private void CloseWriters()
        {
            CloseWriter(ref manifestWriter); CloseWriter(ref sessionWriter); CloseWriter(ref breakoutAuditWriter); CloseWriter(ref breakoutFinalWriter);
            CloseWriter(ref candidateWriter); CloseWriter(ref tradeWriter); CloseWriter(ref dailyWriter); CloseWriter(ref riskScenarioWriter);
            CloseWriter(ref executionEquityWriter);
        }

        private static void CloseWriter(ref StreamWriter writer)
        {
            if (writer == null) return;
            try
            {
                writer.Flush(); 
                writer.Dispose();
            }
            catch
            {
                // ignored
            }
            finally { writer = null; }
        }

        private static string SanitizeFileName(string value)
        {
            var result = value ?? "unknown";
            
            result = Path.GetInvalidFileNameChars()
                .Aggregate(result, (current, c) => current.Replace(c, '_'));
            
            return result.Replace(' ', '_');
        }
        
        private void FlushExportWriters()
        {
            sessionWriter?.Flush();
            breakoutAuditWriter?.Flush();
            breakoutFinalWriter?.Flush();
            candidateWriter?.Flush();
            tradeWriter?.Flush();
            dailyWriter?.Flush();
            riskScenarioWriter?.Flush();
            manifestWriter?.Flush();
            executionEquityWriter?.Flush();
        }

        private static string Csv(string value) => "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        private static string Num(double value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        private static string Num(decimal value) => value.ToString("0.########", CultureInfo.InvariantCulture);
        private static string Bool(bool value) => value ? "1" : "0";
        private static string Dt(DateTime value) => Csv(value == DateTime.MinValue || value == Core.Globals.MinDate ? "" : value.ToString("O"));
    }
}
