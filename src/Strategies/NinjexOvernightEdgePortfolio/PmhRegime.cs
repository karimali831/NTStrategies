using System;
using System.Globalization;
using System.IO;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private const string PmhContinuationEntrySignal =
            "PMH-CONT-L";

        private const string PmhContinuationModelName =
            "Failed-PMH continuation long";

        private enum PmhContinuationStage
        {
            AwaitReclaim,
            AwaitRetest,
            AwaitFollowThrough
        }

        private sealed class PmhContinuationCandidate
        {
            public DateTime ParentTime;
            public PmhContinuationStage Stage;
            public double Pmh;
            public int ReclaimCloses;
            public DateTime ReclaimTime =
                Core.Globals.MinDate;
            public DateTime RetestTime =
                Core.Globals.MinDate;
            public double RetestLow =
                double.NaN;
            public double RetestHigh =
                double.NaN;
        }

        private readonly System.Collections.Generic.List<PmhContinuationCandidate>
            pmhContinuationCandidates =
                new System.Collections.Generic.List<PmhContinuationCandidate>();

        private bool pmhContinuationReady;
        private DateTime pmhContinuationSignalTime =
            Core.Globals.MinDate;
        private double pmhContinuationRetestLow =
            double.NaN;
        private double pmhContinuationRetestHigh =
            double.NaN;

        private bool pendingPmhContinuationStructureBracket;
        private double pendingPmhContinuationRetestLow =
            double.NaN;

        private string pmhRegimeEventsPath =
            string.Empty;
        private bool pmhRegimeEventsFaulted;

        private void InitializePmhRegime()
        {
            pmhContinuationCandidates.Clear();
            ResetPmhContinuationReady();
            ResetPendingPmhContinuationBracket();

            pmhRegimeEventsPath =
                string.Empty;
            pmhRegimeEventsFaulted = false;

            if (!EnableResearchTelemetry)
                return;

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(
                    directory);

                var instrumentName =
                    Instrument == null
                        ? "UnknownInstrument"
                        : SanitizeFileName(
                            Instrument.FullName);

                pmhRegimeEventsPath =
                    Path.Combine(
                        directory,
                        "overnight_edge_pmh_regime_events_"
                        + instrumentName
                        + "_"
                        + researchRunId
                        + ".csv");

                File.WriteAllText(
                    pmhRegimeEventsPath,
                    "RunId,Instrument,EventTimeET,EventType,Signal,Direction,"
                    + "PMH,Price,RetestLow,ATR5Ticks,EMA5Fast,EMA5Slow,"
                    + "EMA5FastDistanceTicks,TradesToday,WinnersToday,LossesToday,"
                    + "Reason,ProcessingState"
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                pmhRegimeEventsFaulted = true;

                Diagnostic(
                    DateTime.Now,
                    "PMH REGIME EVENTS DISABLED InitError={0}",
                    ex.Message);
            }
        }

        private void PreparePmhRegimeCompletedBar(
            DateTime barTime,
            double open,
            double high,
            double low,
            double close)
        {
            ResetPmhContinuationReady();

            for (var i =
                    pmhContinuationCandidates.Count - 1;
                i >= 0;
                i--)
            {
                var candidate =
                    pmhContinuationCandidates[i];

                if (barTime.Date
                        != candidate.ParentTime.Date
                    || ToTimeValue(barTime)
                        >= EntryEndTime)
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

                    pmhContinuationCandidates.RemoveAt(i);
                    continue;
                }

                if (barTime
                    <= candidate.ParentTime)
                {
                    continue;
                }

                if (candidate.Stage
                    == PmhContinuationStage.AwaitReclaim)
                {
                    if ((barTime
                         - candidate.ParentTime)
                            .TotalMinutes
                        > PmhContinuationReclaimMinutes)
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

                        pmhContinuationCandidates.RemoveAt(i);
                        continue;
                    }

                    candidate.ReclaimCloses =
                        close >= candidate.Pmh
                            + PmhContinuationReclaimTicks
                            * TickSize
                            ? candidate.ReclaimCloses + 1
                            : 0;

                    if (candidate.ReclaimCloses
                        >= PmhContinuationReclaimCloses)
                    {
                        candidate.ReclaimTime =
                            barTime;

                        candidate.Stage =
                            PmhContinuationStage.AwaitRetest;

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
                    == PmhContinuationStage.AwaitRetest)
                {
                    if ((barTime
                         - candidate.ReclaimTime)
                            .TotalMinutes
                        > PmhContinuationRetestMinutes)
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

                        pmhContinuationCandidates.RemoveAt(i);
                        continue;
                    }

                    if (low < candidate.Pmh
                        - PmhContinuationMaxBreachTicks
                        * TickSize)
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

                        pmhContinuationCandidates.RemoveAt(i);
                        continue;
                    }

                    if (low <= candidate.Pmh
                            + PmhContinuationRetestTouchTicks
                            * TickSize
                        && close >= candidate.Pmh)
                    {
                        candidate.RetestTime =
                            barTime;

                        candidate.RetestLow =
                            low;

                        candidate.RetestHigh =
                            high;

                        candidate.Stage =
                            PmhContinuationStage.AwaitFollowThrough;

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

                if ((barTime
                     - candidate.RetestTime)
                        .TotalMinutes
                    > PmhContinuationFollowThroughMinutes)
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

                    pmhContinuationCandidates.RemoveAt(i);
                    continue;
                }

                if (low < candidate.Pmh
                    - PmhContinuationMaxBreachTicks
                    * TickSize)
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

                    pmhContinuationCandidates.RemoveAt(i);
                    continue;
                }

                var contextStillBullish =
                    IsFinite(last5mEmaFast)
                    && IsFinite(last5mEmaSlow)
                    && last5mEmaFast
                        > last5mEmaSlow
                    && close >= last5mEmaFast;

                var confirmed =
                    close > open
                    && close
                        >= candidate.RetestHigh
                        + PmhContinuationFollowThroughTicks
                        * TickSize
                    && close
                        >= candidate.Pmh
                        + PmhContinuationReclaimTicks
                        * TickSize
                    && contextStillBullish;

                if (!confirmed)
                    continue;

                if (!pmhContinuationReady)
                {
                    pmhContinuationReady = true;
                    pmhContinuationSignalTime =
                        barTime;
                    pmhContinuationRetestLow =
                        candidate.RetestLow;
                    pmhContinuationRetestHigh =
                        candidate.RetestHigh;

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

                pmhContinuationCandidates.RemoveAt(i);
            }
        }

        private void RegisterPmhRegimeParentIfQualified(
            DateTime signalTime,
            double high,
            double close,
            int minutesFromOpen)
        {
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
                TickSize > 0
                && IsFinite(last5mEmaFast)
                    ? (close - last5mEmaFast)
                        / TickSize
                    : double.NaN;

            var strictShort =
                IsFinite(fastDistanceTicks)
                && fastDistanceTicks
                    <= PmhShortMaximumFastEmaDistanceTicks
                && last5mAtrTicks
                    < PmhShortMaximumAtrTicks;

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

            var bullishParent =
                IsFinite(last5mEmaFast)
                && IsFinite(last5mEmaSlow)
                && last5mEmaFast
                    > last5mEmaSlow
                && IsFinite(researchPreviousEmaFast)
                && TickSize > 0
                && (last5mEmaFast
                    - researchPreviousEmaFast)
                    / TickSize
                    >= PmhContinuationMinFastSlopeTicks
                && (last5mEmaFast
                    - last5mEmaSlow)
                    / TickSize
                    >= PmhContinuationMinEmaSpreadTicks
                && IsFinite(fastDistanceTicks)
                && fastDistanceTicks >= 0;

            if (!bullishParent)
                return;

            pmhContinuationCandidates.Add(
                new PmhContinuationCandidate
                {
                    ParentTime =
                        signalTime,
                    Stage =
                        PmhContinuationStage.AwaitReclaim,
                    Pmh =
                        premarketHigh
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

        private bool IsPmhStrictShortQualified(
            bool baseQualified,
            double close)
        {
            if (!baseQualified
                || TickSize <= 0
                || !IsFinite(last5mEmaFast)
                || !IsFinite(last5mAtrTicks))
            {
                return false;
            }

            var fastDistanceTicks =
                (close - last5mEmaFast)
                / TickSize;

            return fastDistanceTicks
                       <= PmhShortMaximumFastEmaDistanceTicks
                   && last5mAtrTicks
                       < PmhShortMaximumAtrTicks;
        }

        private bool TryArmPmhContinuationLong(
            DateTime signalTime,
            double high,
            double low,
            double close,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            if (!pmhContinuationReady
                || pmhContinuationSignalTime
                    != signalTime)
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

            pendingPmhContinuationStructureBracket = true;
            pendingPmhContinuationRetestLow =
                pmhContinuationRetestLow;

            return true;
        }

        private void RecordPmhContinuationReadyBlocked(
            DateTime time,
            string reason)
        {
            if (!pmhContinuationReady
                || pmhContinuationSignalTime
                    != time)
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
                pmhContinuationRetestLow,
                reason ?? string.Empty);
        }

        private bool TryResolvePmhContinuationBracket(
            DateTime time,
            double observedPrice,
            string signalName,
            out int stopTicks,
            out int targetTicks)
        {
            stopTicks =
                StopLossTicks;

            targetTicks =
                ProfitTargetTicks;

            if (signalName
                != PmhContinuationEntrySignal)
            {
                return true;
            }

            if (!pendingPmhContinuationStructureBracket
                || !IsFinite(
                    pendingPmhContinuationRetestLow)
                || TickSize <= 0)
            {
                Diagnostic(
                    time,
                    "PMH CONTINUATION ENTRY BLOCK "
                    + "Reason=MissingStructureBracket");

                return false;
            }

            var distanceTicks =
                (observedPrice
                 - pendingPmhContinuationRetestLow)
                / TickSize
                + PmhContinuationStructureBufferTicks;

            if (!IsFinite(distanceTicks)
                || distanceTicks <= 0)
            {
                Diagnostic(
                    time,
                    "PMH CONTINUATION ENTRY BLOCK "
                    + "Reason=InvalidStructureDistance "
                    + "Observed={0} RetestLow={1}",
                    observedPrice,
                    pendingPmhContinuationRetestLow);

                return false;
            }

            stopTicks =
                Math.Max(
                    8,
                    (int)Math.Ceiling(
                        distanceTicks));

            if (stopTicks
                > PmhContinuationMaxStopTicks)
            {
                Diagnostic(
                    time,
                    "PMH CONTINUATION ENTRY BLOCK "
                    + "Reason=StructureStopTooWide "
                    + "Stop={0}t Max={1}t",
                    stopTicks,
                    PmhContinuationMaxStopTicks);

                WritePmhRegimeEvent(
                    time,
                    "ContinuationPortfolioBlocked",
                    signalName,
                    PendingDirection.Long,
                    premarketHigh,
                    observedPrice,
                    pendingPmhContinuationRetestLow,
                    "StructureStopTooWide");

                return false;
            }

            targetTicks =
                stopTicks * 2;

            return true;
        }

        private void ResetPmhContinuationReady()
        {
            pmhContinuationReady = false;
            pmhContinuationSignalTime =
                Core.Globals.MinDate;
            pmhContinuationRetestLow =
                double.NaN;
            pmhContinuationRetestHigh =
                double.NaN;
        }

        private void ResetPendingPmhContinuationBracket()
        {
            pendingPmhContinuationStructureBracket =
                false;

            pendingPmhContinuationRetestLow =
                double.NaN;
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
            if (!EnableResearchTelemetry
                || pmhRegimeEventsFaulted
                || string.IsNullOrEmpty(
                    pmhRegimeEventsPath))
            {
                return;
            }

            try
            {
                var emaDistance =
                    TickSize > 0
                    && IsFinite(last5mEmaFast)
                    && IsFinite(price)
                        ? (price - last5mEmaFast)
                            / TickSize
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
                        CsvPmhRegime(
                            time.ToString(
                                "yyyy-MM-dd HH:mm:ss.fff",
                                CultureInfo.InvariantCulture)),
                        CsvPmhRegime(eventType),
                        CsvPmhRegime(signal),
                        CsvPmhRegime(
                            direction.ToString()),
                        NumberPmhRegime(pmh),
                        NumberPmhRegime(price),
                        NumberPmhRegime(retestLow),
                        NumberPmhRegime(
                            last5mAtrTicks),
                        NumberPmhRegime(
                            last5mEmaFast),
                        NumberPmhRegime(
                            last5mEmaSlow),
                        NumberPmhRegime(
                            emaDistance),
                        tradesToday.ToString(
                            CultureInfo.InvariantCulture),
                        winnersToday.ToString(
                            CultureInfo.InvariantCulture),
                        lossesToday.ToString(
                            CultureInfo.InvariantCulture),
                        CsvPmhRegime(reason),
                        CsvPmhRegime(
                            State.ToString()))
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                pmhRegimeEventsFaulted = true;

                Diagnostic(
                    time,
                    "PMH REGIME EVENTS DISABLED "
                    + "WriteError={0}",
                    ex.Message);
            }
        }

        private static string NumberPmhRegime(
            double value)
        {
            return double.IsNaN(value)
                   || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(
                    "0.########",
                    CultureInfo.InvariantCulture);
        }

        private static string CsvPmhRegime(
            string value)
        {
            return """
                + (value ?? string.Empty)
                    .Replace(""", """")
                + """;
        }
    }
}
