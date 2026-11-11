using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private const string PmhContinuationEntrySignal = "PMH-CONT-L";
        private const string PmhContinuationModelName =
            "Failed-PMH continuation long";

        private enum PmhRegimeCandidateStage
        {
            AwaitReclaim,
            AwaitRetest,
            AwaitFollowThrough
        }

        private sealed class PmhRegimeCandidate
        {
            public DateTime ParentTime;
            public PmhRegimeCandidateStage Stage;
            public double Pmh;
            public int ReclaimCloses;
            public DateTime ReclaimTime = Core.Globals.MinDate;
            public DateTime RetestTime = Core.Globals.MinDate;
            public double RetestLow = double.NaN;
            public double RetestHigh = double.NaN;
        }

        private readonly List<PmhRegimeCandidate> pmhRegimeCandidates =
            new List<PmhRegimeCandidate>();

        private bool pmhRegimeLongReady;
        private DateTime pmhRegimeLongSignalTime = Core.Globals.MinDate;
        private double pmhRegimeLongRetestLow = double.NaN;
        private double pmhRegimeLongRetestHigh = double.NaN;

        // Pending custom bracket state. Only PMH-CONT-L sets this.
        private bool pendingPmhRegimeStructureBracket;
        private double pendingPmhRegimeRetestLow = double.NaN;

        private bool pmhRegimeEvaluationBlocked;
        private string pmhRegimeEvaluationBlockReason = string.Empty;

        // Exact strategy-equity path, updated on every delivered 1-tick BIP.
        private double pmhRegimeCumulativeRealized;
        private DateTime pmhRegimeActiveEntryFillTime = Core.Globals.MinDate;
        private double pmhRegimePeakEquity;
        private double pmhRegimeMaxIntratradeDrawdown;
        private DateTime pmhRegimeMaxDrawdownTime = Core.Globals.MinDate;
        private string pmhRegimeEventsPath = string.Empty;
        private string pmhRegimeEquityPath = string.Empty;
        private bool pmhRegimeOutputFaulted;

        private void InitializePmhRegimeEvaluation()
        {
            pmhRegimeCandidates.Clear();
            ResetPmhRegimeLongReady();
            ResetPendingPmhRegimeBracket();

            pmhRegimeEvaluationBlocked = false;
            pmhRegimeEvaluationBlockReason = string.Empty;
            pmhRegimeCumulativeRealized = 0.0;
            pmhRegimeActiveEntryFillTime = Core.Globals.MinDate;
            pmhRegimePeakEquity = 0.0;
            pmhRegimeMaxIntratradeDrawdown = 0.0;
            pmhRegimeMaxDrawdownTime = Core.Globals.MinDate;
            pmhRegimeEventsPath = string.Empty;
            pmhRegimeEquityPath = string.Empty;
            pmhRegimeOutputFaulted = false;

            if (!EnablePmhRegimeSplitEvaluation)
                return;

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);

                var instrumentName = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);

                var suffix =
                    instrumentName + "_" + researchRunId + ".csv";

                pmhRegimeEventsPath = Path.Combine(
                    directory,
                    "overnight_edge_pmh_regime_integrated_events_" + suffix);

                pmhRegimeEquityPath = Path.Combine(
                    directory,
                    "overnight_edge_pmh_regime_integrated_equity_" + suffix);

                File.WriteAllText(
                    pmhRegimeEventsPath,
                    "RunId,Instrument,EventTimeET,EventType,Signal,Direction," +
                    "PMH,Price,RetestLow,ATR5Ticks,EMA5Fast,EMA5Slow," +
                    "EMA5FastDistanceTicks,TradesToday,WinnersToday,LossesToday," +
                    "Reason,ProcessingState" + Environment.NewLine);

                File.WriteAllText(
                    pmhRegimeEquityPath,
                    "RunId,Instrument,EventTimeET,EventType,LastPrice," +
                    "CumulativeRealized,OpenUnrealized,StrategyEquity,PeakEquity," +
                    "IntratradeDrawdown,MaxIntratradeDrawdown,ActiveSignal," +
                    "ActiveDirection,ProcessingState" + Environment.NewLine);

                Diagnostic(
                    DateTime.Now,
                    "PMH REGIME EVALUATION READY RunId={0} " +
                    "ShortMaxATR={1:0.0}t ShortMaxEmaDistance={2:0.0}t " +
                    "Continuation=ReclaimRetestFollowThrough StructureStop2R",
                    researchRunId,
                    PmhRegimeShortMaximumAtrTicks,
                    PmhRegimeShortMaximumFastEmaDistanceTicks);
            }
            catch (Exception ex)
            {
                pmhRegimeOutputFaulted = true;
                Diagnostic(
                    DateTime.Now,
                    "PMH REGIME EVALUATION OUTPUT DISABLED Error={0}",
                    ex.Message);
            }
        }

        private void ValidatePmhRegimeEvaluationEnvironment(DateTime time)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return;

            bool isPlayback;
            var detected = TryDetectPlaybackRecoveryContext(out isPlayback);

            pmhRegimeEvaluationBlocked =
                !detected
                || !isPlayback
                || MirrorVerifiedLiveExecutions;

            if (!detected)
                pmhRegimeEvaluationBlockReason = "PlaybackDetectionFailed";
            else if (!isPlayback)
                pmhRegimeEvaluationBlockReason = "PlaybackAccountRequired";
            else if (MirrorVerifiedLiveExecutions)
                pmhRegimeEvaluationBlockReason =
                    "MirrorVerifiedLiveExecutionsMustBeFalse";
            else
                pmhRegimeEvaluationBlockReason = string.Empty;

            Diagnostic(
                time,
                "PMH REGIME EVALUATION SAFETY Allowed={0} Reason={1}",
                !pmhRegimeEvaluationBlocked,
                pmhRegimeEvaluationBlockReason);
        }

        private bool CanSubmitPmhRegimeEvaluationOrders(DateTime time)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return true;

            if (State != State.Realtime)
                return false;

            if (pmhRegimeEvaluationBlocked)
                return false;

            bool isPlayback;
            if (!TryDetectPlaybackRecoveryContext(out isPlayback)
                || !isPlayback)
            {
                pmhRegimeEvaluationBlocked = true;
                pmhRegimeEvaluationBlockReason =
                    "PlaybackAccountRequired";
                Diagnostic(
                    time,
                    "PMH REGIME EVALUATION BLOCKED Reason={0}",
                    pmhRegimeEvaluationBlockReason);
                return false;
            }

            return true;
        }

        private void PreparePmhRegimeEvaluationCompletedBar(
            DateTime barTime,
            double open,
            double high,
            double low,
            double close)
        {
            ResetPmhRegimeLongReady();

            if (!EnablePmhRegimeSplitEvaluation)
                return;

            for (var i = pmhRegimeCandidates.Count - 1; i >= 0; i--)
            {
                var candidate = pmhRegimeCandidates[i];

                if (barTime.Date != candidate.ParentTime.Date
                    || ToTimeValue(barTime) >= EntryEndTime)
                {
                    WritePmhRegimeEvent(
                        barTime,
                        "ContinuationExpired",
                        PmhContinuationEntrySignal,
                        PendingDirection.Long,
                        candidate.Pmh,
                        close,
                        candidate.RetestLow,
                        "WindowClosed");

                    pmhRegimeCandidates.RemoveAt(i);
                    continue;
                }

                if (barTime <= candidate.ParentTime)
                    continue;

                if (candidate.Stage
                    == PmhRegimeCandidateStage.AwaitReclaim)
                {
                    if ((barTime - candidate.ParentTime).TotalMinutes
                        > FailedPmhV2ReclaimMinutes)
                    {
                        WritePmhRegimeEvent(
                            barTime,
                            "ContinuationExpired",
                            PmhContinuationEntrySignal,
                            PendingDirection.Long,
                            candidate.Pmh,
                            close,
                            candidate.RetestLow,
                            "ReclaimExpired");

                        pmhRegimeCandidates.RemoveAt(i);
                        continue;
                    }

                    candidate.ReclaimCloses =
                        close >= candidate.Pmh
                            + FailedPmhV2ReclaimTicks * TickSize
                            ? candidate.ReclaimCloses + 1
                            : 0;

                    if (candidate.ReclaimCloses
                        >= FailedPmhV2ReclaimCloses)
                    {
                        candidate.ReclaimTime = barTime;
                        candidate.Stage =
                            PmhRegimeCandidateStage.AwaitRetest;

                        WritePmhRegimeEvent(
                            barTime,
                            "ContinuationReclaim",
                            PmhContinuationEntrySignal,
                            PendingDirection.Long,
                            candidate.Pmh,
                            close,
                            candidate.RetestLow,
                            string.Empty);
                    }

                    continue;
                }

                if (candidate.Stage
                    == PmhRegimeCandidateStage.AwaitRetest)
                {
                    if ((barTime - candidate.ReclaimTime).TotalMinutes
                        > FailedPmhV2RetestMinutes)
                    {
                        WritePmhRegimeEvent(
                            barTime,
                            "ContinuationExpired",
                            PmhContinuationEntrySignal,
                            PendingDirection.Long,
                            candidate.Pmh,
                            close,
                            candidate.RetestLow,
                            "RetestExpired");

                        pmhRegimeCandidates.RemoveAt(i);
                        continue;
                    }

                    if (low < candidate.Pmh
                        - FailedPmhV2MaxBreachTicks * TickSize)
                    {
                        WritePmhRegimeEvent(
                            barTime,
                            "ContinuationInvalid",
                            PmhContinuationEntrySignal,
                            PendingDirection.Long,
                            candidate.Pmh,
                            close,
                            candidate.RetestLow,
                            "RetestBreach");

                        pmhRegimeCandidates.RemoveAt(i);
                        continue;
                    }

                    if (low <= candidate.Pmh
                            + FailedPmhV2RetestTouchTicks * TickSize
                        && close >= candidate.Pmh)
                    {
                        candidate.RetestTime = barTime;
                        candidate.RetestLow = low;
                        candidate.RetestHigh = high;
                        candidate.Stage =
                            PmhRegimeCandidateStage.AwaitFollowThrough;

                        WritePmhRegimeEvent(
                            barTime,
                            "ContinuationRetestHeld",
                            PmhContinuationEntrySignal,
                            PendingDirection.Long,
                            candidate.Pmh,
                            close,
                            candidate.RetestLow,
                            string.Empty);
                    }

                    continue;
                }

                if ((barTime - candidate.RetestTime).TotalMinutes
                    > FailedPmhV2FollowThroughMinutes)
                {
                    WritePmhRegimeEvent(
                        barTime,
                        "ContinuationExpired",
                        PmhContinuationEntrySignal,
                        PendingDirection.Long,
                        candidate.Pmh,
                        close,
                        candidate.RetestLow,
                        "FollowThroughExpired");

                    pmhRegimeCandidates.RemoveAt(i);
                    continue;
                }

                if (low < candidate.Pmh
                    - FailedPmhV2MaxBreachTicks * TickSize)
                {
                    WritePmhRegimeEvent(
                        barTime,
                        "ContinuationInvalid",
                        PmhContinuationEntrySignal,
                        PendingDirection.Long,
                        candidate.Pmh,
                        close,
                        candidate.RetestLow,
                        "FollowThroughBreach");

                    pmhRegimeCandidates.RemoveAt(i);
                    continue;
                }

                var contextStillBullish =
                    IsFinite(last5mEmaFast)
                    && IsFinite(last5mEmaSlow)
                    && last5mEmaFast > last5mEmaSlow
                    && close >= last5mEmaFast;

                var confirmed =
                    close > open
                    && close >= candidate.RetestHigh
                        + FailedPmhV2FollowThroughTicks * TickSize
                    && close >= candidate.Pmh
                        + FailedPmhV2ReclaimTicks * TickSize
                    && contextStillBullish;

                if (!confirmed)
                    continue;

                // Multiple parent PMH events can converge on the same market
                // confirmation. The integrated portfolio can act only once.
                if (!pmhRegimeLongReady)
                {
                    pmhRegimeLongReady = true;
                    pmhRegimeLongSignalTime = barTime;
                    pmhRegimeLongRetestLow = candidate.RetestLow;
                    pmhRegimeLongRetestHigh = candidate.RetestHigh;

                    WritePmhRegimeEvent(
                        barTime,
                        "ContinuationConfirmed",
                        PmhContinuationEntrySignal,
                        PendingDirection.Long,
                        candidate.Pmh,
                        close,
                        candidate.RetestLow,
                        string.Empty);
                }
                else
                {
                    WritePmhRegimeEvent(
                        barTime,
                        "ContinuationDuplicate",
                        PmhContinuationEntrySignal,
                        PendingDirection.Long,
                        candidate.Pmh,
                        close,
                        candidate.RetestLow,
                        "SameConfirmationBar");
                }

                pmhRegimeCandidates.RemoveAt(i);
            }
        }

        private void RegisterPmhRegimeParentIfQualified(
            DateTime signalTime,
            double high,
            double close,
            int minutesFromOpen)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return;

            var basePmhQualified =
                IsFinite(premarketHigh)
                && high > premarketHigh
                && close < premarketHigh
                && minutesFromOpen >= 0
                && minutesFromOpen
                    <= PremarketHighMaximumMinutesFromOpen
                && IsFinite(last5mAtrTicks)
                && last5mAtrTicks
                    >= PremarketHighMinimumAtr5mTicks;

            if (!basePmhQualified)
                return;

            var fastDistanceTicks =
                TickSize > 0 && IsFinite(last5mEmaFast)
                    ? (close - last5mEmaFast) / TickSize
                    : double.NaN;

            var strictShort =
                IsFinite(fastDistanceTicks)
                && fastDistanceTicks
                    <= PmhRegimeShortMaximumFastEmaDistanceTicks
                && last5mAtrTicks
                    < PmhRegimeShortMaximumAtrTicks;

            WritePmhRegimeEvent(
                signalTime,
                strictShort
                    ? "StrictShortQualified"
                    : "OriginalPmhSuppressed",
                PremarketHighEntrySignal,
                PendingDirection.Short,
                premarketHigh,
                close,
                double.NaN,
                strictShort
                    ? string.Empty
                    : "RegimeFilter");

            // The continuation family starts only from bullish parent context.
            var bullishParent =
                IsFinite(last5mEmaFast)
                && IsFinite(last5mEmaSlow)
                && last5mEmaFast > last5mEmaSlow
                && IsFinite(researchPreviousEmaFast)
                && TickSize > 0
                && (last5mEmaFast - researchPreviousEmaFast) / TickSize
                    >= FailedPmhV2MinFastSlopeTicks
                && (last5mEmaFast - last5mEmaSlow) / TickSize
                    >= FailedPmhV2MinEmaSpreadTicks
                && IsFinite(fastDistanceTicks)
                && fastDistanceTicks >= 0;

            if (!bullishParent)
                return;

            pmhRegimeCandidates.Add(
                new PmhRegimeCandidate
                {
                    ParentTime = signalTime,
                    Stage = PmhRegimeCandidateStage.AwaitReclaim,
                    Pmh = premarketHigh
                });

            WritePmhRegimeEvent(
                signalTime,
                "ContinuationParentRegistered",
                PmhContinuationEntrySignal,
                PendingDirection.Long,
                premarketHigh,
                close,
                double.NaN,
                string.Empty);
        }

        private bool IsPmhRegimeStrictShortQualified(
            bool originalPmhQualified,
            double close)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return originalPmhQualified;

            if (!originalPmhQualified
                || TickSize <= 0
                || !IsFinite(last5mEmaFast)
                || !IsFinite(last5mAtrTicks))
            {
                return false;
            }

            var fastDistanceTicks =
                (close - last5mEmaFast) / TickSize;

            return fastDistanceTicks
                       <= PmhRegimeShortMaximumFastEmaDistanceTicks
                   && last5mAtrTicks
                       < PmhRegimeShortMaximumAtrTicks;
        }

        private bool TryArmPmhRegimeContinuationLong(
            DateTime signalTime,
            double high,
            double low,
            double close,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            if (!EnablePmhRegimeSplitEvaluation
                || !pmhRegimeLongReady
                || pmhRegimeLongSignalTime != signalTime)
            {
                return false;
            }

            ArmPendingEntry(
                PmhContinuationModelName,
                PmhContinuationEntrySignal,
                PendingDirection.Long,
                signalTime,
                signalTime,
                high,
                low,
                close,
                minutesFromOpen,
                overnightWidthTicks);

            pendingPmhRegimeStructureBracket = true;
            pendingPmhRegimeRetestLow = pmhRegimeLongRetestLow;

            return true;
        }

        private void RecordPmhRegimeReadyBlocked(
            DateTime time,
            string reason)
        {
            if (!EnablePmhRegimeSplitEvaluation
                || !pmhRegimeLongReady
                || pmhRegimeLongSignalTime != time)
            {
                return;
            }

            WritePmhRegimeEvent(
                time,
                "ContinuationPortfolioBlocked",
                PmhContinuationEntrySignal,
                PendingDirection.Long,
                premarketHigh,
                double.NaN,
                pmhRegimeLongRetestLow,
                reason ?? string.Empty);
        }

        private bool TryResolvePmhRegimeEntryBracket(
            DateTime time,
            double observedPrice,
            string signalName,
            out int stopTicks,
            out int targetTicks)
        {
            stopTicks = StopLossTicks;
            targetTicks = ProfitTargetTicks;

            if (signalName != PmhContinuationEntrySignal)
                return true;

            if (!EnablePmhRegimeSplitEvaluation
                || !pendingPmhRegimeStructureBracket
                || !IsFinite(pendingPmhRegimeRetestLow)
                || TickSize <= 0)
            {
                Diagnostic(
                    time,
                    "PMH REGIME ENTRY BLOCK " +
                    "Reason=MissingStructureBracket");
                return false;
            }

            var distanceTicks =
                (observedPrice - pendingPmhRegimeRetestLow) / TickSize
                + FailedPmhV2StructureBufferTicks;

            if (!IsFinite(distanceTicks)
                || distanceTicks <= 0)
            {
                Diagnostic(
                    time,
                    "PMH REGIME ENTRY BLOCK " +
                    "Reason=InvalidStructureDistance " +
                    "Observed={0} RetestLow={1}",
                    observedPrice,
                    pendingPmhRegimeRetestLow);
                return false;
            }

            stopTicks =
                Math.Max(
                    8,
                    (int)Math.Ceiling(distanceTicks));

            if (stopTicks > FailedPmhV2MaxStopTicks)
            {
                Diagnostic(
                    time,
                    "PMH REGIME ENTRY BLOCK " +
                    "Reason=StructureStopTooWide Stop={0}t Max={1}t",
                    stopTicks,
                    FailedPmhV2MaxStopTicks);

                WritePmhRegimeEvent(
                    time,
                    "ContinuationPortfolioBlocked",
                    signalName,
                    PendingDirection.Long,
                    premarketHigh,
                    observedPrice,
                    pendingPmhRegimeRetestLow,
                    "StructureStopTooWide");

                return false;
            }

            targetTicks = stopTicks * 2;
            return true;
        }

        private void ResetPmhRegimeLongReady()
        {
            pmhRegimeLongReady = false;
            pmhRegimeLongSignalTime = Core.Globals.MinDate;
            pmhRegimeLongRetestLow = double.NaN;
            pmhRegimeLongRetestHigh = double.NaN;
        }

        private void ResetPendingPmhRegimeBracket()
        {
            pendingPmhRegimeStructureBracket = false;
            pendingPmhRegimeRetestLow = double.NaN;
        }

        private string GetPmhRegimePortfolioBlockReason()
        {
            if (!CanSubmitPmhRegimeEvaluationOrders(DateTime.Now))
                return string.IsNullOrEmpty(pmhRegimeEvaluationBlockReason)
                    ? "EvaluationSafetyGate"
                    : pmhRegimeEvaluationBlockReason;

            if (Position.MarketPosition != MarketPosition.Flat
                || entryOrderPending
                || manualExitPending
                || pendingDirection != PendingDirection.None)
            {
                return "OrderOrPositionActive";
            }

            if (MaxTradesPerDay > 0
                && tradesToday >= MaxTradesPerDay)
                return "MaxTrades";

            if (MaxWinnersPerDay > 0
                && winnersToday >= MaxWinnersPerDay)
                return "MaxWinners";

            if (MaxLossesPerDay > 0
                && lossesToday >= MaxLossesPerDay)
                return "MaxLosses";

            return "RiskOrRecoveryGate";
        }

        private void RecordPmhRegimeEquityTick(
            DateTime time,
            double lastPrice)
        {
            if (!EnablePmhRegimeSplitEvaluation
                || pmhRegimeOutputFaulted
                || !IsFinite(lastPrice))
            {
                return;
            }

            var openUnrealized = 0.0;

            if (activeTradeCounted
                && Position.MarketPosition != MarketPosition.Flat)
            {
                // Playback can deliver a cached BIP-2 tick callback after the
                // execution callback even when that tick's own timestamp is
                // slightly earlier than the entry fill. Never let a pre-fill
                // tick contribute to open equity or drawdown.
                if (pmhRegimeActiveEntryFillTime != Core.Globals.MinDate
                    && time < pmhRegimeActiveEntryFillTime)
                {
                    return;
                }

                var averageEntry = GetActiveAverageEntryPrice();

                if (IsFinite(averageEntry))
                {
                    var points =
                        activeTradeDirection == PendingDirection.Long
                            ? lastPrice - averageEntry
                            : averageEntry - lastPrice;

                    openUnrealized =
                        points
                        * Instrument.MasterInstrument.PointValue
                        * Math.Max(1, Position.Quantity);
                }
            }

            UpdatePmhRegimeEquity(
                time,
                lastPrice,
                openUnrealized,
                "Tick");
        }

        private void RecordPmhRegimeTradeCompleted(
            DateTime time,
            double exitPrice,
            double tradeGross)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return;

            pmhRegimeCumulativeRealized += tradeGross;

            UpdatePmhRegimeEquity(
                time,
                exitPrice,
                0.0,
                "TradeExit");

            WritePmhRegimeEvent(
                time,
                "IntegratedTradeExit",
                activeEntrySignal,
                activeTradeDirection,
                premarketHigh,
                exitPrice,
                double.NaN,
                tradeGross.ToString(
                    "0.00",
                    CultureInfo.InvariantCulture));

            pmhRegimeActiveEntryFillTime = Core.Globals.MinDate;
        }

        private void RecordPmhRegimeEntryFill(
            DateTime time,
            double entryPrice)
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return;

            pmhRegimeActiveEntryFillTime = time;

            WritePmhRegimeEvent(
                time,
                "IntegratedEntryFill",
                activeEntrySignal,
                activeTradeDirection,
                premarketHigh,
                entryPrice,
                pendingPmhRegimeRetestLow,
                string.Empty);

            UpdatePmhRegimeEquity(
                time,
                entryPrice,
                0.0,
                "TradeEntry");
        }

        private void UpdatePmhRegimeEquity(
            DateTime time,
            double lastPrice,
            double openUnrealized,
            string eventType)
        {
            var equity =
                pmhRegimeCumulativeRealized
                + openUnrealized;

            var newPeak = false;
            var newMaximumDrawdown = false;

            if (equity > pmhRegimePeakEquity)
            {
                pmhRegimePeakEquity = equity;
                newPeak = true;
            }

            var drawdown =
                pmhRegimePeakEquity - equity;

            if (drawdown > pmhRegimeMaxIntratradeDrawdown)
            {
                pmhRegimeMaxIntratradeDrawdown = drawdown;
                pmhRegimeMaxDrawdownTime = time;
                newMaximumDrawdown = true;
            }

            if (eventType == "TradeEntry"
                || eventType == "TradeExit"
                || newPeak
                || newMaximumDrawdown)
            {
                WritePmhRegimeEquityRow(
                    time,
                    eventType,
                    lastPrice,
                    openUnrealized,
                    equity,
                    drawdown);
            }
        }

        private void WritePmhRegimeEvent(
            DateTime time,
            string eventType,
            string signal,
            PendingDirection direction,
            double pmh,
            double price,
            double retestLow,
            string reason)
        {
            if (!EnablePmhRegimeSplitEvaluation
                || pmhRegimeOutputFaulted
                || string.IsNullOrEmpty(pmhRegimeEventsPath))
            {
                return;
            }

            try
            {
                var emaDistance =
                    TickSize > 0
                    && IsFinite(last5mEmaFast)
                    && IsFinite(price)
                        ? (price - last5mEmaFast) / TickSize
                        : double.NaN;

                File.AppendAllText(
                    pmhRegimeEventsPath,
                    string.Join(
                        ",",
                        CsvPmhRegime(researchRunId),
                        CsvPmhRegime(
                            Instrument == null
                                ? string.Empty
                                : Instrument.FullName),
                        DatePmhRegime(time),
                        CsvPmhRegime(eventType),
                        CsvPmhRegime(signal),
                        CsvPmhRegime(direction.ToString()),
                        NumberPmhRegime(pmh),
                        NumberPmhRegime(price),
                        NumberPmhRegime(retestLow),
                        NumberPmhRegime(last5mAtrTicks),
                        NumberPmhRegime(last5mEmaFast),
                        NumberPmhRegime(last5mEmaSlow),
                        NumberPmhRegime(emaDistance),
                        tradesToday.ToString(
                            CultureInfo.InvariantCulture),
                        winnersToday.ToString(
                            CultureInfo.InvariantCulture),
                        lossesToday.ToString(
                            CultureInfo.InvariantCulture),
                        CsvPmhRegime(reason),
                        CsvPmhRegime(State.ToString()))
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                pmhRegimeOutputFaulted = true;

                Diagnostic(
                    time,
                    "PMH REGIME OUTPUT DISABLED Error={0}",
                    ex.Message);
            }
        }

        private void WritePmhRegimeEquityRow(
            DateTime time,
            string eventType,
            double lastPrice,
            double openUnrealized,
            double equity,
            double drawdown)
        {
            if (pmhRegimeOutputFaulted
                || string.IsNullOrEmpty(pmhRegimeEquityPath))
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    pmhRegimeEquityPath,
                    string.Join(
                        ",",
                        CsvPmhRegime(researchRunId),
                        CsvPmhRegime(
                            Instrument == null
                                ? string.Empty
                                : Instrument.FullName),
                        DatePmhRegime(time),
                        CsvPmhRegime(eventType),
                        NumberPmhRegime(lastPrice),
                        NumberPmhRegime(
                            pmhRegimeCumulativeRealized),
                        NumberPmhRegime(openUnrealized),
                        NumberPmhRegime(equity),
                        NumberPmhRegime(pmhRegimePeakEquity),
                        NumberPmhRegime(drawdown),
                        NumberPmhRegime(
                            pmhRegimeMaxIntratradeDrawdown),
                        CsvPmhRegime(activeEntrySignal),
                        CsvPmhRegime(
                            activeTradeDirection.ToString()),
                        CsvPmhRegime(State.ToString()))
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                pmhRegimeOutputFaulted = true;

                Diagnostic(
                    time,
                    "PMH REGIME EQUITY OUTPUT DISABLED Error={0}",
                    ex.Message);
            }
        }

        private void DisposePmhRegimeEvaluation()
        {
            if (!EnablePmhRegimeSplitEvaluation)
                return;

            Diagnostic(
                DateTime.Now,
                "PMH REGIME EVALUATION SUMMARY " +
                "RealizedGross={0:0.00} PeakEquity={1:0.00} " +
                "MaxIntratradeDD={2:0.00} MaxDDTime={3}",
                pmhRegimeCumulativeRealized,
                pmhRegimePeakEquity,
                pmhRegimeMaxIntratradeDrawdown,
                pmhRegimeMaxDrawdownTime
                    == Core.Globals.MinDate
                        ? string.Empty
                        : pmhRegimeMaxDrawdownTime.ToString(
                            "yyyy-MM-dd HH:mm:ss.fff",
                            CultureInfo.InvariantCulture));

            if (!pmhRegimeOutputFaulted
                && !string.IsNullOrEmpty(pmhRegimeEquityPath))
            {
                WritePmhRegimeEquityRow(
                    DateTime.Now,
                    "FinalSummary",
                    double.NaN,
                    0.0,
                    pmhRegimeCumulativeRealized,
                    Math.Max(
                        0.0,
                        pmhRegimePeakEquity
                        - pmhRegimeCumulativeRealized));
            }
        }

        private static string DatePmhRegime(DateTime value)
        {
            return value == Core.Globals.MinDate
                ? string.Empty
                : value.ToString(
                    "yyyy-MM-dd HH:mm:ss.fff",
                    CultureInfo.InvariantCulture);
        }

        private static string NumberPmhRegime(double value)
        {
            return double.IsNaN(value)
                   || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(
                    "0.########",
                    CultureInfo.InvariantCulture);
        }

        private static string CsvPmhRegime(string value)
        {
            return "\""
                   + (value ?? string.Empty)
                       .Replace("\"", "\"\"")
                   + "\"";
        }
    }
}
