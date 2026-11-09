using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // V2 is an independent observational experiment, never an order model.
        // V1 and Run 8 are deliberately left unchanged.
        private const string FailedPmhV2Version = "failed-pmh-v2-retest-1.0";

        private enum FailedPmhV2Stage
        {
            AwaitReclaim,
            AwaitRetest,
            AwaitFollowThrough
        }

        private sealed class FailedPmhV2Candidate
        {
            public string Id;
            public NinjexOvernightEdgeResearchRow Parent;
            public FailedPmhV2Stage Stage;
            public double Pmh;
            public int ReclaimCloses;
            public DateTime ReclaimTime = Core.Globals.MinDate;
            public DateTime RetestTime = Core.Globals.MinDate;
            public DateTime ConfirmationTime = Core.Globals.MinDate;
            public double RetestHigh = double.NaN;
            public double RetestLow = double.NaN;
            public double ConfirmationClose = double.NaN;
            public double ConfirmationAtr = double.NaN;
            public double ConfirmationFastEma = double.NaN;
            public double ConfirmationSlowEma = double.NaN;
        }

        private sealed class FailedPmhV2Trade
        {
            public FailedPmhV2Candidate Candidate;
            public string Scenario;
            public bool Backfilled;
            public bool Entered;
            public int StopTicks;
            public int TargetTicks;
            public DateTime EntryTime = Core.Globals.MinDate;
            public DateTime HoldDeadline = Core.Globals.MinDate;
            public double EntryPrice = double.NaN;
            public double Mfe;
            public double Mae;
            public bool PortfolioPositionOpen;
            public bool PortfolioCapsReached;
            public bool PortfolioOrderPending;
            public int PortfolioTradesAtEntry;
            public int PortfolioWinnersAtEntry;
            public int PortfolioLossesAtEntry;
        }

        private readonly object failedPmhV2Sync = new object();
        private readonly List<FailedPmhV2Candidate> failedPmhV2Pending =
            new List<FailedPmhV2Candidate>();
        private readonly List<FailedPmhV2Trade> failedPmhV2Active =
            new List<FailedPmhV2Trade>();
        private readonly HashSet<string> failedPmhV2Parents =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<DateTime> failedPmhV2Confirmations =
            new HashSet<DateTime>();

        private string failedPmhV2CandidateCsv = string.Empty;
        private string failedPmhV2TradeCsv = string.Empty;
        private bool failedPmhV2Faulted;

        private void InitializeFailedPmhV2Research()
        {
            failedPmhV2Faulted = false;
            failedPmhV2CandidateCsv = string.Empty;
            failedPmhV2TradeCsv = string.Empty;
            failedPmhV2Pending.Clear();
            failedPmhV2Active.Clear();
            failedPmhV2Parents.Clear();
            failedPmhV2Confirmations.Clear();

            if (!EnableFailedPmhV2Research)
                return;

            if (!EnableResearchTelemetry)
            {
                failedPmhV2Faulted = true;
                Diagnostic(DateTime.Now,
                    "FAILED PMH V2 DISABLED Reason=RequiresResearchTelemetry");
                return;
            }

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir, "NinjexResearch",
                    "OvernightEdgePortfolio");
                Directory.CreateDirectory(directory);

                var instrumentName = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);

                var suffix = instrumentName + "_" + researchRunId + ".csv";
                failedPmhV2CandidateCsv = Path.Combine(
                    directory, "overnight_edge_failed_pmh_v2_candidates_" + suffix);
                failedPmhV2TradeCsv = Path.Combine(
                    directory, "overnight_edge_failed_pmh_v2_trades_" + suffix);

                File.WriteAllText(failedPmhV2CandidateCsv,
                    "RunId,Instrument,ModelVersion,ParentCandidateId,ParentSignalTimeET," +
                    "ParentPMH,ParentSweepTicks,ParentATR5Ticks,ParentFastEma,ParentSlowEma," +
                    "ParentFastSlopeTicks,ParentEMADistanceTicks,Status,Stage," +
                    "ReclaimTimeET,RetestTimeET,RetestLow,RetestHigh," +
                    "ConfirmationTimeET,ConfirmationClose,ConfirmationATR5Ticks," +
                    "ConfirmationFastEma,ConfirmationSlowEma,RecordedAtET" +
                    Environment.NewLine);

                File.WriteAllText(failedPmhV2TradeCsv,
                    "RunId,Instrument,ModelVersion,ParentCandidateId,ParentSignalTimeET," +
                    "PMH,ReclaimTimeET,RetestTimeET,RetestLow,RetestHigh," +
                    "ConfirmationTimeET,ConfirmationClose,ConfirmationATR5Ticks," +
                    "Scenario,StopTicks,TargetTicks,EntryTimeET,EntryPrice," +
                    "ExitTimeET,ExitPrice,ExitReason,GrossPnl,MfeDollars,MaeDollars," +
                    "PortfolioPositionOpen,PortfolioCapsReached,PortfolioOrderPending," +
                    "PortfolioTradesAtEntry,PortfolioWinnersAtEntry," +
                    "PortfolioLossesAtEntry,ProcessingState" +
                    Environment.NewLine);

                Diagnostic(DateTime.Now,
                    "FAILED PMH V2 READY RunId={0} Reclaim={1}m Retest={2}m FollowThrough={3}m",
                    researchRunId, FailedPmhV2ReclaimMinutes,
                    FailedPmhV2RetestMinutes, FailedPmhV2FollowThroughMinutes);
            }
            catch (Exception ex)
            {
                DisableFailedPmhV2("Initialize", ex);
            }
        }

        // Called for every *completed* one-minute bar, independent of portfolio
        // permission and before the new candle's candidate is registered.
        private void AdvanceFailedPmhV2CompletedBar(
            DateTime barTime, double open, double high, double low, double close)
        {
            if (!EnableFailedPmhV2Research || failedPmhV2Faulted ||
                !EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            lock (failedPmhV2Sync)
            {
                try
                {
                    for (var i = failedPmhV2Pending.Count - 1; i >= 0; i--)
                    {
                        var c = failedPmhV2Pending[i];

                        if (barTime.Date != c.Parent.EventTime.Date
                            || ToTimeValue(barTime) >= EntryEndTime)
                        {
                            WriteFailedPmhV2Candidate(c, "WindowClosed", barTime);
                            failedPmhV2Pending.RemoveAt(i);
                            continue;
                        }

                        // The parent rejection candle cannot act as the next
                        // candle or supply a future retest/confirmation.
                        if (barTime <= c.Parent.EventTime)
                            continue;

                        if (c.Stage == FailedPmhV2Stage.AwaitReclaim)
                        {
                            if ((barTime - c.Parent.EventTime).TotalMinutes
                                > FailedPmhV2ReclaimMinutes)
                            {
                                WriteFailedPmhV2Candidate(c, "ReclaimExpired", barTime);
                                failedPmhV2Pending.RemoveAt(i);
                                continue;
                            }

                            c.ReclaimCloses =
                                close >= c.Pmh + FailedPmhV2ReclaimTicks * TickSize
                                    ? c.ReclaimCloses + 1
                                    : 0;

                            if (c.ReclaimCloses >= FailedPmhV2ReclaimCloses)
                            {
                                c.ReclaimTime = barTime;
                                c.Stage = FailedPmhV2Stage.AwaitRetest;
                            }

                            continue;
                        }

                        if (c.Stage == FailedPmhV2Stage.AwaitRetest)
                        {
                            if ((barTime - c.ReclaimTime).TotalMinutes
                                > FailedPmhV2RetestMinutes)
                            {
                                WriteFailedPmhV2Candidate(c, "RetestExpired", barTime);
                                failedPmhV2Pending.RemoveAt(i);
                                continue;
                            }

                            if (low < c.Pmh - FailedPmhV2MaxBreachTicks * TickSize)
                            {
                                WriteFailedPmhV2Candidate(c, "RetestBreach", barTime);
                                failedPmhV2Pending.RemoveAt(i);
                                continue;
                            }

                            // Distinct completed bar, touching PMH from above,
                            // with the close holding at or above PMH.
                            if (low <= c.Pmh + FailedPmhV2RetestTouchTicks * TickSize
                                && close >= c.Pmh)
                            {
                                c.RetestTime = barTime;
                                c.RetestLow = low;
                                c.RetestHigh = high;
                                c.Stage = FailedPmhV2Stage.AwaitFollowThrough;
                            }

                            continue;
                        }

                        if (c.Stage != FailedPmhV2Stage.AwaitFollowThrough)
                            continue;

                        if ((barTime - c.RetestTime).TotalMinutes
                            > FailedPmhV2FollowThroughMinutes)
                        {
                            WriteFailedPmhV2Candidate(
                                c, "FollowThroughExpired", barTime);
                            failedPmhV2Pending.RemoveAt(i);
                            continue;
                        }

                        if (low < c.Pmh - FailedPmhV2MaxBreachTicks * TickSize)
                        {
                            WriteFailedPmhV2Candidate(
                                c, "FollowThroughBreach", barTime);
                            failedPmhV2Pending.RemoveAt(i);
                            continue;
                        }

                        // Confirmation is a third, separate completed bar.
                        // Use the completed 5m cache available at this instant,
                        // never a future candle or final-session EMA.
                        var contextStillBullish =
                            IsFinite(last5mEmaFast) && IsFinite(last5mEmaSlow)
                            && last5mEmaFast > last5mEmaSlow
                            && close >= last5mEmaFast;

                        if (!(close > open
                            && close >= c.RetestHigh
                                + FailedPmhV2FollowThroughTicks * TickSize
                            && close >= c.Pmh
                                + FailedPmhV2ReclaimTicks * TickSize
                            && contextStillBullish))
                            continue;

                        c.ConfirmationTime = barTime;
                        c.ConfirmationClose = close;
                        c.ConfirmationAtr = last5mAtrTicks;
                        c.ConfirmationFastEma = last5mEmaFast;
                        c.ConfirmationSlowEma = last5mEmaSlow;

                        if (!failedPmhV2Confirmations.Add(barTime))
                        {
                            WriteFailedPmhV2Candidate(
                                c, "DuplicateConfirmation", barTime);
                        }
                        else
                        {
                            WriteFailedPmhV2Candidate(c, "Confirmed", barTime);
                            failedPmhV2Active.Add(new FailedPmhV2Trade
                            {
                                Candidate = c, Scenario = "Fixed20_40"
                            });
                            failedPmhV2Active.Add(new FailedPmhV2Trade
                            {
                                Candidate = c, Scenario = "ATR_Stop_2R"
                            });
                            failedPmhV2Active.Add(new FailedPmhV2Trade
                            {
                                Candidate = c, Scenario = "Retest_Stop_2R"
                            });
                        }

                        failedPmhV2Pending.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    DisableFailedPmhV2("BarAdvance", ex);
                }
            }
        }

        // Called after the existing PMH candidate telemetry is captured.
        // No part of V2 can change original signal priority or daily limits.
        private void RegisterFailedPmhV2Parent(DateTime signalTime)
        {
            if (!EnableFailedPmhV2Research || failedPmhV2Faulted ||
                !EnableResearchTelemetry || researchTelemetryFaulted)
                return;

            lock (failedPmhV2Sync)
            {
                try
                {
                    NinjexOvernightEdgeResearchRow s;
                    if (!researchSignalSnapshots.TryGetValue(
                            ResearchSignalKey(signalTime, PremarketHighEntrySignal),
                            out s)
                        || s == null || s.Qualified != true)
                        return;

                    var id = BuildResearchShadowCandidateId(s);
                    if (!failedPmhV2Parents.Add(id))
                        return;

                    var c = new FailedPmhV2Candidate
                    {
                        Id = id, Parent = s.Clone(), Pmh = s.PremarketHigh,
                        Stage = FailedPmhV2Stage.AwaitReclaim
                    };

                    var contextPass = IsFinite(s.PremarketHigh)
                        && IsFinite(s.Ema5Fast)
                        && IsFinite(s.Ema5Slow)
                        && IsFinite(s.Ema5FastSlopeTicks)
                        && IsFinite(s.Ema5FastDistanceTicks)
                        && s.Ema5Fast > s.Ema5Slow
                        && (s.Ema5Fast - s.Ema5Slow) / TickSize
                            >= FailedPmhV2MinEmaSpreadTicks
                        && s.Ema5FastSlopeTicks
                            >= FailedPmhV2MinFastSlopeTicks
                        && s.Ema5FastDistanceTicks >= 0;

                    if (!contextPass)
                    {
                        WriteFailedPmhV2Candidate(
                            c, "FilteredParentTrend", signalTime);
                        return;
                    }

                    failedPmhV2Pending.Add(c);
                }
                catch (Exception ex)
                {
                    DisableFailedPmhV2("ParentRegistration", ex);
                }
            }
        }

        // Shared OnMarketData(Last) tick stream with the existing shadow engine.
        private void ProcessFailedPmhV2Tick(DateTime time, double price)
        {
            if (!EnableFailedPmhV2Research || failedPmhV2Faulted ||
                !EnableResearchTelemetry || researchTelemetryFaulted ||
                !IsFinite(price))
                return;

            lock (failedPmhV2Sync)
            {
                try
                {
                    for (var i = failedPmhV2Active.Count - 1; i >= 0; i--)
                    {
                        var t = failedPmhV2Active[i];
                        var completed = false;
                        if (!t.Backfilled)
                        {
                            t.Backfilled = true;
                            var ticks = CollectResearchTicksSince(
                                t.Candidate.ConfirmationTime);
                            foreach (var historicalTick in ticks)
                            {
                                if (ApplyFailedPmhV2Tick(
                                    t, historicalTick.Time, historicalTick.Price))
                                {
                                    completed = true;
                                    break;
                                }
                            }
                        }

                        if (!completed)
                            completed = ApplyFailedPmhV2Tick(t, time, price);

                        if (completed)
                            failedPmhV2Active.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    DisableFailedPmhV2("TickProcessing", ex);
                }
            }
        }

        private bool ApplyFailedPmhV2Tick(
            FailedPmhV2Trade t, DateTime time, double price)
        {
            if (time < t.Candidate.ConfirmationTime)
                return false;

            if (!t.Entered)
            {
                if (time.Date != t.Candidate.ConfirmationTime.Date
                    || ToTimeValue(time) >= EntryEndTime)
                {
                    WriteFailedPmhV2Trade(t, time, double.NaN, "NoCausalEntry");
                    return true;
                }

                t.Entered = true;
                t.EntryTime = time;
                t.EntryPrice = price;
                t.HoldDeadline = FailedPmhV2MaxHoldMinutes > 0
                    ? time.AddMinutes(FailedPmhV2MaxHoldMinutes)
                    : Core.Globals.MinDate;

                // An entry eligibility snapshot, NOT a portfolio simulation.
                t.PortfolioPositionOpen = Position != null &&
                    Position.MarketPosition != MarketPosition.Flat;
                t.PortfolioOrderPending =
                    entryOrderPending || manualExitPending;
                t.PortfolioCapsReached =
                    (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay)
                    || (MaxWinnersPerDay > 0
                        && winnersToday >= MaxWinnersPerDay)
                    || (MaxLossesPerDay > 0
                        && lossesToday >= MaxLossesPerDay);
                t.PortfolioTradesAtEntry = tradesToday;
                t.PortfolioWinnersAtEntry = winnersToday;
                t.PortfolioLossesAtEntry = lossesToday;

                if (t.Scenario == "Fixed20_40")
                {
                    t.StopTicks = 20;
                    t.TargetTicks = 40;
                }
                else if (t.Scenario == "ATR_Stop_2R")
                {
                    var atr = t.Candidate.ConfirmationAtr;
                    if (!IsFinite(atr) || atr <= 0)
                    {
                        WriteFailedPmhV2Trade(
                            t, time, double.NaN, "MissingConfirmationATR");
                        return true;
                    }

                    t.StopTicks = (int)Math.Max(12, Math.Min(
                        FailedPmhV2MaxStopTicks,
                        Math.Ceiling(atr * FailedPmhV2AtrStopFactor)));
                    t.TargetTicks = 2 * t.StopTicks;
                }
                else
                {
                    // Retest support - buffer; reject structurally distant
                    // stops rather than silently clamping them to invalid levels.
                    var distance =
                        (price - t.Candidate.RetestLow) / TickSize
                            + FailedPmhV2StructureBufferTicks;
                    if (!IsFinite(distance) || distance <= 0
                        || distance > FailedPmhV2MaxStopTicks)
                    {
                        WriteFailedPmhV2Trade(
                            t, time, double.NaN, "StructureStopTooWide");
                        return true;
                    }

                    t.StopTicks = Math.Max(8, (int)Math.Ceiling(distance));
                    t.TargetTicks = 2 * t.StopTicks;
                }

                // The first causal tick establishes entry with zero excursion.
                return false;
            }

            // No synthetic exit can be borrowed from a later session.
            if (time.Date != t.EntryTime.Date)
            {
                WriteFailedPmhV2Trade(
                    t, Core.Globals.MinDate,
                    double.NaN, "MissingSameDayExitTick");
                return true;
            }

            var pointValue = Instrument == null
                ? 0.0 : Instrument.MasterInstrument.PointValue;
            var quantity = Math.Max(1, OrderQuantity);
            t.Mfe = Math.Max(t.Mfe,
                Math.Max(0.0, price - t.EntryPrice) * pointValue * quantity);
            t.Mae = Math.Max(t.Mae,
                Math.Max(0.0, t.EntryPrice - price) * pointValue * quantity);

            var stopPrice = t.EntryPrice - t.StopTicks * TickSize;
            var targetPrice = t.EntryPrice + t.TargetTicks * TickSize;

            if (price <= stopPrice)
            {
                WriteFailedPmhV2Trade(t, time, stopPrice, "StopLoss");
                return true;
            }

            if (price >= targetPrice)
            {
                WriteFailedPmhV2Trade(t, time, targetPrice, "ProfitTarget");
                return true;
            }

            if (ToTimeValue(time) >= FlattenTime)
            {
                WriteFailedPmhV2Trade(t, time, price, "FlattenTime");
                return true;
            }

            if (t.HoldDeadline != Core.Globals.MinDate
                && time >= t.HoldDeadline)
            {
                WriteFailedPmhV2Trade(t, time, price, "MaxHold");
                return true;
            }

            return false;
        }

        private void WriteFailedPmhV2Candidate(
            FailedPmhV2Candidate c, string status, DateTime observedAt)
        {
            if (failedPmhV2Faulted || string.IsNullOrEmpty(failedPmhV2CandidateCsv))
                return;

            var p = c.Parent;
            var fields = new[]
            {
                CsvFailedPmh(researchRunId),
                CsvFailedPmh(Instrument == null ? "" : Instrument.FullName),
                CsvFailedPmh(FailedPmhV2Version),
                CsvFailedPmh(c.Id),
                DateFailedPmh(p.EventTime),
                NumberFailedPmh(c.Pmh),
                NumberFailedPmh(p.PmhSweepTicks),
                NumberFailedPmh(p.Atr5Ticks),
                NumberFailedPmh(p.Ema5Fast),
                NumberFailedPmh(p.Ema5Slow),
                NumberFailedPmh(p.Ema5FastSlopeTicks),
                NumberFailedPmh(p.Ema5FastDistanceTicks),
                CsvFailedPmh(status),
                CsvFailedPmh(c.Stage.ToString()),
                DateFailedPmh(c.ReclaimTime),
                DateFailedPmh(c.RetestTime),
                NumberFailedPmh(c.RetestLow),
                NumberFailedPmh(c.RetestHigh),
                DateFailedPmh(c.ConfirmationTime),
                NumberFailedPmh(c.ConfirmationClose),
                NumberFailedPmh(c.ConfirmationAtr),
                NumberFailedPmh(c.ConfirmationFastEma),
                NumberFailedPmh(c.ConfirmationSlowEma),
                DateFailedPmh(observedAt)
            };
            File.AppendAllText(failedPmhV2CandidateCsv,
                string.Join(",", fields) + Environment.NewLine);
        }

        private void WriteFailedPmhV2Trade(
            FailedPmhV2Trade t, DateTime exitTime, double exitPrice, string reason)
        {
            if (failedPmhV2Faulted || string.IsNullOrEmpty(failedPmhV2TradeCsv))
                return;

            var c = t.Candidate;
            var grossPnl = t.Entered && IsFinite(exitPrice)
                ? (exitPrice - t.EntryPrice)
                    * (Instrument == null ? 0 : Instrument.MasterInstrument.PointValue)
                    * Math.Max(1, OrderQuantity)
                : double.NaN;

            var fields = new[]
            {
                CsvFailedPmh(researchRunId),
                CsvFailedPmh(Instrument == null ? "" : Instrument.FullName),
                CsvFailedPmh(FailedPmhV2Version),
                CsvFailedPmh(c.Id),
                DateFailedPmh(c.Parent.EventTime),
                NumberFailedPmh(c.Pmh),
                DateFailedPmh(c.ReclaimTime),
                DateFailedPmh(c.RetestTime),
                NumberFailedPmh(c.RetestLow),
                NumberFailedPmh(c.RetestHigh),
                DateFailedPmh(c.ConfirmationTime),
                NumberFailedPmh(c.ConfirmationClose),
                NumberFailedPmh(c.ConfirmationAtr),
                CsvFailedPmh(t.Scenario),
                t.StopTicks.ToString(CultureInfo.InvariantCulture),
                t.TargetTicks.ToString(CultureInfo.InvariantCulture),
                DateFailedPmh(t.EntryTime),
                NumberFailedPmh(t.EntryPrice),
                DateFailedPmh(exitTime),
                NumberFailedPmh(exitPrice),
                CsvFailedPmh(reason),
                NumberFailedPmh(grossPnl),
                NumberFailedPmh(t.Mfe),
                NumberFailedPmh(t.Mae),
                t.PortfolioPositionOpen ? "1" : "0",
                t.PortfolioCapsReached ? "1" : "0",
                t.PortfolioOrderPending ? "1" : "0",
                t.PortfolioTradesAtEntry.ToString(CultureInfo.InvariantCulture),
                t.PortfolioWinnersAtEntry.ToString(CultureInfo.InvariantCulture),
                t.PortfolioLossesAtEntry.ToString(CultureInfo.InvariantCulture),
                CsvFailedPmh(State.ToString())
            };

            File.AppendAllText(failedPmhV2TradeCsv,
                string.Join(",", fields) + Environment.NewLine);
        }

        private void DisposeFailedPmhV2Research()
        {
            if (!EnableFailedPmhV2Research || failedPmhV2Faulted)
                return;

            lock (failedPmhV2Sync)
            {
                try
                {
                    foreach (var c in failedPmhV2Pending)
                        WriteFailedPmhV2Candidate(
                            c, "IncompleteAtTermination", Core.Globals.MinDate);
                    failedPmhV2Pending.Clear();

                    foreach (var t in failedPmhV2Active)
                        WriteFailedPmhV2Trade(
                            t, Core.Globals.MinDate,
                            double.NaN, "IncompleteAtTermination");
                    failedPmhV2Active.Clear();
                }
                catch (Exception ex)
                {
                    DisableFailedPmhV2("Dispose", ex);
                }
            }
        }

        private void DisableFailedPmhV2(string stage, Exception error)
        {
            failedPmhV2Faulted = true;
            Diagnostic(DateTime.Now,
                "FAILED PMH V2 DISABLED Stage={0} Error={1}",
                stage, error == null ? "Unknown" : error.Message);
        }
    }
}
