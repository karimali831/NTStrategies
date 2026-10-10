using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        #region 1-minute signal detection

        private void ProcessSignalSeries()
        {
            if (CurrentBars[SignalSeriesIndex] < 2)
                return;

            if (!IsFirstTickOfBar)
                return;


            var signalTime =
                Times[SignalSeriesIndex][1];

            var currentBarOpen =
                Opens[SignalSeriesIndex][0];

            var signalOpen =
                Opens[SignalSeriesIndex][1];

            var high =
                Highs[SignalSeriesIndex][1];

            var low =
                Lows[SignalSeriesIndex][1];

            var close =
                Closes[SignalSeriesIndex][1];

            var previousClose =
                Closes[SignalSeriesIndex][2];

            //
            // NinjaTrader time-based bars are end-stamped. At the first
            // tick of the next 1-minute bar, Times[1][0] is already one
            // minute later than the tick that made [1] complete. Arm from
            // signalTime so BIP 2 can use that first causal tick rather
            // than waiting through the whole next minute.
            //
            EnsureTradingDate(
                signalTime.Date,
                signalTime);


            var timeValue =
                ToTimeValue(
                    signalTime);


            UpdateRthReferenceLevels(
                signalTime,
                timeValue,
                currentBarOpen,
                close);


            if (timeValue < EntryStartTime
                || timeValue >= EntryEndTime)
            {
                return;
            }


            if (!CanEvaluateSignal(
                    signalTime))
            {
                return;
            }


            var minutesFromOpen =
                MinutesBetween(
                    MarketOpenTime,
                    timeValue);

            var overnightWidthTicks =
                GetOvernightWidthTicks();


            // Capture four-model candidates before portfolio-state gating so
            // research can see signals displaced by an active trade or daily cap.
            CaptureFourModelResearchCandidates(
                signalTime,
                signalOpen,
                high,
                low,
                close,
                previousClose,
                minutesFromOpen,
                overnightWidthTicks);


            if (!CanTakeNewTrade(
                    signalTime,
                    false))
            {
                return;
            }


            ProcessFourModelSignals(
                signalTime,
                high,
                low,
                close,
                previousClose,
                minutesFromOpen,
                overnightWidthTicks);
        }


        private void ProcessFourModelSignals(
            DateTime signalTime,
            double high,
            double low,
            double close,
            double previousClose,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            var range1mTicks =
                TickSize > 0
                    ? (high - low) / TickSize
                    : double.NaN;

            var premarketWidthTicks =
                GetPremarketWidthTicks();


            var priorCloseCross =
                IsFinite(priorDayClose)
                && priorDayCloseDate < signalTime.Date
                && previousClose <= priorDayClose
                && close > priorDayClose;

            var pdcEarlyWindow =
                minutesFromOpen >= 0
                && minutesFromOpen <= PdcEarlyMaximumMinutesFromOpen;

            var pdcEarlyPremarketWidthOk =
                !pdcEarlyWindow
                || (IsFinite(premarketWidthTicks)
                    && premarketWidthTicks >= PdcEarlyMinimumPremarketWidthTicks);

            var priorCloseQualified =
                priorCloseCross
                && IsFinite(range1mTicks)
                && range1mTicks
                    <= PriorCloseMaximumRangeTicks
                && IsFinite(overnightWidthTicks)
                && overnightWidthTicks
                    >= PriorCloseMinimumOvernightWidthTicks
                && pdcEarlyPremarketWidthOk;


            if (priorCloseCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PDC-Reclaim " +
                    "Qualified={0} PDC={1} " +
                    "PrevClose={2} Close={3} " +
                    "Range1m={4:0.0}t MaxRange={5:0.0}t " +
                    "ONWidth={6:0.0}t MinONWidth={7:0.0}t " +
                    "MinutesFromOpen={8} " +
                    "PMWidth={9:0.0}t MinEarlyPMWidth={10:0.0}t EarlyPmOk={11}",
                    priorCloseQualified,
                    priorDayClose,
                    previousClose,
                    close,
                    range1mTicks,
                    PriorCloseMaximumRangeTicks,
                    overnightWidthTicks,
                    PriorCloseMinimumOvernightWidthTicks,
                    minutesFromOpen,
                    premarketWidthTicks,
                    PdcEarlyMinimumPremarketWidthTicks,
                    pdcEarlyPremarketWidthOk);
            }


            var premarketHighSweep =
                IsFinite(premarketHigh)
                && high > premarketHigh
                && close < premarketHigh;

            var premarketHighQualified =
                premarketHighSweep
                && minutesFromOpen >= 0
                && minutesFromOpen <= PremarketHighMaximumMinutesFromOpen
                && IsFinite(last5mAtrTicks)
                && last5mAtrTicks >= PremarketHighMinimumAtr5mTicks;


            if (premarketHighSweep)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PMH-Rejection " +
                    "Qualified={0} PMH={1} High={2} Close={3} " +
                    "MinutesFromOpen={4} MaxMinutes={5} " +
                    "ATR5={6:0.0}t MinATR={7:0.0}t",
                    premarketHighQualified,
                    premarketHigh,
                    high,
                    close,
                    minutesFromOpen,
                    PremarketHighMaximumMinutesFromOpen,
                    last5mAtrTicks,
                    PremarketHighMinimumAtr5mTicks);
            }


            var rthOpenCross =
                IsFinite(rthOpen)
                && rthOpenDate == signalTime.Date
                && previousClose >= rthOpen
                && close < rthOpen;

            var rthOpenQualified =
                rthOpenCross
                && minutesFromOpen >= RthOpenMinimumMinutesFromOpen
                && IsFinite(premarketWidthTicks)
                && premarketWidthTicks >= RthOpenMinimumPremarketWidthTicks;


            if (rthOpenCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=RTH-Open-Breakdown " +
                    "Qualified={0} RTHOpen={1} " +
                    "PrevClose={2} Close={3} " +
                    "MinutesFromOpen={4} MinMinutes={5} " +
                    "PMWidth={6:0.0}t MinPMWidth={7:0.0}t",
                    rthOpenQualified,
                    rthOpen,
                    previousClose,
                    close,
                    minutesFromOpen,
                    RthOpenMinimumMinutesFromOpen,
                    premarketWidthTicks,
                    RthOpenMinimumPremarketWidthTicks);
            }


            var premarketLowCross =
                IsFinite(premarketLow)
                && previousClose >= premarketLow
                && close < premarketLow;

            // Validated Run 8 PML rule: raw close must remain above EMA(9).
            var fastEmaOk =
                IsFinite(last5mEmaFast)
                && close > last5mEmaFast;

            var premarketLowQualified =
                premarketLowCross
                && IsFinite(last5mAtrTicks)
                && last5mAtrTicks >= PremarketLowMinimumAtr5mTicks
                && fastEmaOk;


            if (premarketLowCross)
            {
                Diagnostic(
                    signalTime,
                    "FOUR CHECK Model=PML-Breakdown " +
                    "Qualified={0} PML={1} " +
                    "PrevClose={2} Close={3} " +
                    "ATR5={4:0.0}t MinATR={5:0.0}t " +
                    "EMA5Fast={6} CloseAboveEMA={7}",
                    premarketLowQualified,
                    premarketLow,
                    previousClose,
                    close,
                    last5mAtrTicks,
                    PremarketLowMinimumAtr5mTicks,
                    last5mEmaFast,
                    fastEmaOk);
            }


            var qualifiedCount =
                (priorCloseQualified ? 1 : 0)
                + (premarketHighQualified ? 1 : 0)
                + (rthOpenQualified ? 1 : 0)
                + (premarketLowQualified ? 1 : 0);

            if (qualifiedCount > 1)
            {
                Diagnostic(
                    signalTime,
                    "FOUR PRIORITY Multiple={0} " +
                    "Order=PDC,PMH,RTHOpen,PML",
                    qualifiedCount);
            }


            //
            // This ordering is the ordering used by the research portfolio
            // when more than one model qualifies on the same timestamp.
            //
            if (priorCloseQualified)
            {
                ArmPendingEntry(
                    PriorCloseModelName,
                    PriorCloseEntrySignal,
                    PendingDirection.Long,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (premarketHighQualified)
            {
                ArmPendingEntry(
                    PremarketHighModelName,
                    PremarketHighEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (rthOpenQualified)
            {
                ArmPendingEntry(
                    RthOpenModelName,
                    RthOpenEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);

                return;
            }


            if (premarketLowQualified)
            {
                ArmPendingEntry(
                    PremarketLowModelName,
                    PremarketLowEntrySignal,
                    PendingDirection.Short,
                    signalTime,
                    signalTime,
                    high,
                    low,
                    close,
                    minutesFromOpen,
                    overnightWidthTicks);
            }
        }


        private void UpdateRthReferenceLevels(
            DateTime signalTime,
            int timeValue,
            double currentBarOpen,
            double completedClose)
        {
            // Use only completed RTH minutes strictly before FlattenTime.
            // With FlattenTime=16:00, the prior-day reference is the 15:59 close.
            if (timeValue < MarketOpenTime
                || timeValue >= FlattenTime)
            {
                return;
            }


            if (currentRthReferenceDate
                != signalTime.Date)
            {
                if (currentRthReferenceDate
                        != Core.Globals.MinDate
                    && IsFinite(currentRthLastClose))
                {
                    priorDayCloseDate =
                        currentRthReferenceDate;

                    priorDayClose =
                        currentRthLastClose;

                    Diagnostic(
                        signalTime,
                        "PRIOR DAY CLOSE READY " +
                        "SourceDate={0:yyyy-MM-dd} Close={1}",
                        priorDayCloseDate,
                        priorDayClose);
                }


                currentRthReferenceDate =
                    signalTime.Date;

                currentRthLastClose =
                    double.NaN;
            }


            if (timeValue == MarketOpenTime
                && IsFinite(currentBarOpen))
            {
                rthOpenDate =
                    signalTime.Date;

                var capturedBarOpen =
                    currentBarOpen;

                var appliedRthOpen =
                    capturedBarOpen;

                double verifiedOverridePrice;
                string verifiedOverrideSource;
                string verifiedOverrideReason;

                var verifiedOverrideApplied =
                    TryGetVerifiedRthOpenOverride(
                        signalTime.Date,
                        out verifiedOverridePrice,
                        out verifiedOverrideSource,
                        out verifiedOverrideReason);

                if (verifiedOverrideApplied)
                {
                    appliedRthOpen =
                        verifiedOverridePrice;
                }

                rthOpen =
                    appliedRthOpen;

                Diagnostic(
                    signalTime,
                    "RTH OPEN READY Date={0:yyyy-MM-dd} Open={1} Source={2}",
                    rthOpenDate,
                    rthOpen,
                    verifiedOverrideApplied
                        ? "VerifiedOverride"
                        : "Captured1mOpen");
            }


            if (IsFinite(completedClose))
            {
                currentRthLastClose =
                    completedClose;

            }
        }


        private bool CanEvaluateSignal(
            DateTime signalTime)
        {
            if (!overnightRangeReady)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=OvernightRangeNotReady");

                return false;
            }


            if (overnightRangeDate
                != signalTime.Date)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=OvernightRangeDateMismatch " +
                    "RangeDate={0:yyyy-MM-dd} SignalDate={1:yyyy-MM-dd}",
                    overnightRangeDate,
                    signalTime.Date);

                return false;
            }


            if (RequireCompleteOvernightRange
                && overnightBars
                    < ExpectedOvernightBars)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=IncompleteOvernightRange " +
                    "Bars={0} Expected={1}",
                    overnightBars,
                    ExpectedOvernightBars);

                return false;
            }


            if (!premarketRangeReady)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=PremarketRangeNotReady");

                return false;
            }


            if (premarketRangeDate
                != signalTime.Date)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=PremarketRangeDateMismatch " +
                    "RangeDate={0:yyyy-MM-dd} SignalDate={1:yyyy-MM-dd}",
                    premarketRangeDate,
                    signalTime.Date);

                return false;
            }


            if (RequireCompletePremarketRange
                && premarketBars
                    < ExpectedPremarketBars)
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=IncompletePremarketRange " +
                    "Bars={0} Expected={1}",
                    premarketBars,
                    ExpectedPremarketBars);

                return false;
            }


            if (!IsFinite(last5mAtrTicks)
                || !IsFinite(last5mEmaFast)
                || !IsFinite(last5mEmaSlow))
            {
                Diagnostic(
                    signalTime,
                    "SIGNAL BLOCK " +
                    "Reason=FiveMinuteContextNotReady");

                return false;
            }


            return true;
        }


        private void ArmPendingEntry(
            string modelName,
            string entrySignal,
            PendingDirection direction,
            DateTime signalTime,
            DateTime earliestExecutionTime,
            double high,
            double low,
            double close,
            int minutesFromOpen,
            double overnightWidthTicks)
        {
            pendingDirection =
                direction;

            pendingModelName =
                modelName ?? string.Empty;

            pendingEntrySignal =
                entrySignal ?? string.Empty;

            pendingSignalTime =
                signalTime;

            pendingEarliestExecutionTime =
                earliestExecutionTime;

            pendingSignalHigh =
                high;

            pendingSignalLow =
                low;

            pendingSignalClose =
                close;

            pendingAtr5mTicks =
                last5mAtrTicks;

            pendingEmaSlow5m =
                last5mEmaSlow;

            pendingEmaFast5m =
                last5mEmaFast;

            pendingOvernightWidthTicks =
                overnightWidthTicks;

            pendingPremarketWidthTicks =
                GetPremarketWidthTicks();

            pendingMinutesFromOpen =
                minutesFromOpen;

            AttachPendingResearchSignal(
                signalTime,
                entrySignal);


            Diagnostic(
                signalTime,
                "SIGNAL ARMED " +
                "Model={0} Signal={1} Direction={2} " +
                "EarliestTick={3:HH:mm:ss.fff} " +
                "Close={4} ATR5={5:0.0}t " +
                "EMA5Fast={6} EMA5Slow={7} " +
                "ONWidth={8:0.0}t PMWidth={9:0.0}t " +
                "Trades={10}/{11} Winners={12}/{13}",
                pendingModelName,
                pendingEntrySignal,
                direction,
                pendingEarliestExecutionTime,
                pendingSignalClose,
                pendingAtr5mTicks,
                pendingEmaFast5m,
                pendingEmaSlow5m,
                pendingOvernightWidthTicks,
                pendingPremarketWidthTicks,
                tradesToday,
                MaxTradesPerDay,
                winnersToday,
                MaxWinnersPerDay);
        }

        #endregion

    }
}
