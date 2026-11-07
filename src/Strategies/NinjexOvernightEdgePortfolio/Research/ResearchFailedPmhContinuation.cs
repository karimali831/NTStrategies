using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // Research-only experiment. A failed PMH short does NOT automatically
        // become a long: the market must subsequently reclaim and hold above
        // the premarket high. No NinjaTrader orders are ever submitted here.
        private const string FailedPmhExperimentVersion = "failed-pmh-continuation-v1";

        private sealed class FailedPmhCandidate
        {
            public NinjexOvernightEdgeResearchRow Parent;
            public string Id;
            public double Level;
            public bool BullishContextPassed;
            public int ConsecutiveCloses;
            public DateTime ConfirmationTime = Core.Globals.MinDate;
            public double ConfirmationClose = double.NaN;
        }

        private sealed class FailedPmhTrade
        {
            public FailedPmhCandidate Candidate;
            public string Scenario;
            public int StopTicks;
            public int TargetTicks;
            public bool BackfillAttempted;
            public bool Entered;
            public DateTime EntryTime = Core.Globals.MinDate;
            public double EntryPrice = double.NaN;
            public DateTime ExitDeadline = Core.Globals.MinDate;
            public double MfeDollars;
            public double MaeDollars;
        }

        private readonly object failedPmhResearchSync = new object();
        private readonly List<FailedPmhCandidate> failedPmhAwaiting =
            new List<FailedPmhCandidate>();
        private readonly List<FailedPmhTrade> failedPmhTrades =
            new List<FailedPmhTrade>();
        private readonly HashSet<string> failedPmhCandidateIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<DateTime> failedPmhConfirmedTimes =
            new HashSet<DateTime>();
        private string failedPmhCandidatePath = string.Empty;
        private string failedPmhTradePath = string.Empty;
        private bool failedPmhResearchFaulted;

        private void InitializeFailedPmhContinuationResearch()
        {
            failedPmhResearchFaulted = false;
            failedPmhAwaiting.Clear();
            failedPmhTrades.Clear();
            failedPmhCandidateIds.Clear();
            failedPmhConfirmedTimes.Clear();
            failedPmhCandidatePath = string.Empty;
            failedPmhTradePath = string.Empty;

            if (!EnableFailedPmhContinuationResearch)
                return;

            if (!EnableResearchTelemetry)
            {
                failedPmhResearchFaulted = true;
                Diagnostic(DateTime.Now,
                    "FAILED PMH RESEARCH DISABLED Reason=RequiresResearchTelemetry");
                return;
            }

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir, "NinjexResearch", "OvernightEdgePortfolio");
                Directory.CreateDirectory(directory);
                var instrument = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);
                var suffix = instrument + "_" + researchRunId + ".csv";

                failedPmhCandidatePath = Path.Combine(
                    directory, "overnight_edge_failed_pmh_candidates_" + suffix);
                failedPmhTradePath = Path.Combine(
                    directory, "overnight_edge_failed_pmh_trades_" + suffix);

                File.WriteAllText(failedPmhCandidatePath,
                    "RunId,Instrument,ExperimentVersion,ParentCandidateId,ParentSignalTimeET," +
                    "PMHPrice,ParentSweepTicks,ParentClose,ParentATR5Ticks," +
                    "ParentEMA5Fast,ParentEMA5Slow,ParentEMAFastSlopeTicks," +
                    "ParentCloseMinusEMA5FastTicks,BullishContextPassed,Status," +
                    "ConfirmationTimeET,ConfirmationClose,ConsecutiveCloses,RecordedAtET" +
                    Environment.NewLine);

                File.WriteAllText(failedPmhTradePath,
                    "RunId,Instrument,ExperimentVersion,ParentCandidateId,ParentSignalTimeET," +
                    "ConfirmationTimeET,ConfirmationClose,PMHPrice,Scenario,StopTicks," +
                    "TargetTicks,EntryTimeET,EntryPrice,ExitTimeET,ExitPrice,ExitReason," +
                    "GrossPnl,MfeDollars,MaeDollars,ProcessingState" +
                    Environment.NewLine);

                Diagnostic(DateTime.Now,
                    "FAILED PMH RESEARCH READY RunId={0} ConfirmCloses={1} Reclaim={2}t MaxWait={3}m BullishEMA={4}",
                    researchRunId, FailedPmhConfirmCloses,
                    FailedPmhReclaimTicks, FailedPmhConfirmationMinutes,
                    FailedPmhRequireBullishEma);
            }
            catch (Exception ex)
            {
                DisableFailedPmhResearch("Initialization", ex);
            }
        }

        // Called once for each completed 1-minute bar, after the four-model
        // candidate snapshots have been captured and before portfolio caps.
        private void ProcessFailedPmhContinuationBar(DateTime barEnd, double close)
        {
            if (!EnableFailedPmhContinuationResearch || !EnableResearchTelemetry
                || researchTelemetryFaulted || failedPmhResearchFaulted)
                return;

            lock (failedPmhResearchSync)
            {
                try
                {
                    // First advance *older* candidates. Never allow the parent
                    // rejection candle to confirm its own continuation signal.
                    for (var i = failedPmhAwaiting.Count - 1; i >= 0; i--)
                    {
                        var candidate = failedPmhAwaiting[i];
                        var age = (barEnd - candidate.Parent.EventTime).TotalMinutes;
                        if (barEnd.Date != candidate.Parent.EventTime.Date
                            || age > FailedPmhConfirmationMinutes
                            || ToTimeValue(barEnd) >= EntryEndTime)
                        {
                            WriteFailedPmhCandidate(candidate,
                                "ConfirmationExpired", barEnd);
                            failedPmhAwaiting.RemoveAt(i);
                            continue;
                        }

                        if (age <= 0)
                            continue;

                        candidate.ConsecutiveCloses =
                            close >= candidate.Level + FailedPmhReclaimTicks * TickSize
                                ? candidate.ConsecutiveCloses + 1
                                : 0;

                        if (candidate.ConsecutiveCloses < FailedPmhConfirmCloses)
                            continue;

                        candidate.ConfirmationTime = barEnd;
                        candidate.ConfirmationClose = close;

                        // Two separate PMH rejections may reclaim on the same
                        // candle. Count only one long signal per confirmation bar.
                        if (!failedPmhConfirmedTimes.Add(barEnd))
                        {
                            WriteFailedPmhCandidate(candidate,
                                "DuplicateConfirmationBar", barEnd);
                        }
                        else
                        {
                            WriteFailedPmhCandidate(candidate,
                                "Confirmed", barEnd);
                            AddFailedPmhTrade(candidate, "Run8_20_40", 20, 40);
                            AddFailedPmhTrade(candidate, "Research_15_50", 15, 50);
                        }
                        failedPmhAwaiting.RemoveAt(i);
                    }

                    // Then register the new qualified PMH rejection, whether
                    // or not the live portfolio was eligible to execute it.
                    NinjexOvernightEdgeResearchRow snapshot;
                    if (!researchSignalSnapshots.TryGetValue(
                            ResearchSignalKey(barEnd, PremarketHighEntrySignal),
                            out snapshot)
                        || snapshot == null
                        || snapshot.Qualified != true)
                        return;

                    var id = BuildResearchShadowCandidateId(snapshot);
                    if (!failedPmhCandidateIds.Add(id))
                        return;

                    var candidateNew = new FailedPmhCandidate
                    {
                        Parent = snapshot.Clone(),
                        Id = id,
                        Level = snapshot.PremarketHigh
                    };

                    var bullishContext =
                        IsFinite(snapshot.Ema5Fast)
                        && IsFinite(snapshot.Ema5Slow)
                        && IsFinite(snapshot.Ema5FastDistanceTicks)
                        && snapshot.Ema5Fast > snapshot.Ema5Slow
                        && snapshot.Ema5FastDistanceTicks >= 0;

                    candidateNew.BullishContextPassed = bullishContext;

                    if (FailedPmhRequireBullishEma && !bullishContext)
                    {
                        WriteFailedPmhCandidate(candidateNew,
                            "FilteredBullishContext", barEnd);
                        return;
                    }

                    failedPmhAwaiting.Add(candidateNew);
                }
                catch (Exception ex)
                {
                    DisableFailedPmhResearch("BarProcessing", ex);
                }
            }
        }

        private void AddFailedPmhTrade(
            FailedPmhCandidate candidate, string scenario, int stopTicks, int targetTicks)
        {
            failedPmhTrades.Add(new FailedPmhTrade
            {
                Candidate = candidate,
                Scenario = scenario,
                StopTicks = stopTicks,
                TargetTicks = targetTicks
            });
        }

        // Invoked from the existing single OnMarketData callback for Last ticks.
        // An independent shadow sink failure must not suppress this experiment.
        private void ProcessFailedPmhContinuationTick(DateTime time, double price)
        {
            if (!EnableFailedPmhContinuationResearch || !EnableResearchTelemetry
                || researchTelemetryFaulted || failedPmhResearchFaulted
                || !IsFinite(price))
                return;

            lock (failedPmhResearchSync)
            {
                try
                {
                    for (var i = failedPmhTrades.Count - 1; i >= 0; i--)
                    {
                        var trade = failedPmhTrades[i];
                        var completed = false;

                        // Recover the first causal tick if NinjaTrader calls
                        // OnMarketData just after the one-minute OnBarUpdate.
                        if (!trade.BackfillAttempted)
                        {
                            trade.BackfillAttempted = true;
                            var ticks = CollectResearchTicksSince(
                                trade.Candidate.ConfirmationTime);
                            foreach (var tick in ticks)
                            {
                                if (ApplyFailedPmhTick(trade, tick.Time, tick.Price))
                                {
                                    completed = true;
                                    break;
                                }
                            }
                        }

                        if (!completed)
                            completed = ApplyFailedPmhTick(trade, time, price);

                        if (completed)
                            failedPmhTrades.RemoveAt(i);
                    }
                }
                catch (Exception ex)
                {
                    DisableFailedPmhResearch("TickProcessing", ex);
                }
            }
        }

        private bool ApplyFailedPmhTick(FailedPmhTrade trade, DateTime time, double price)
        {
            if (time < trade.Candidate.ConfirmationTime)
                return false;

            if (!trade.Entered)
            {
                if (time.Date != trade.Candidate.ConfirmationTime.Date
                    || ToTimeValue(time) >= EntryEndTime)
                {
                    WriteFailedPmhTrade(trade, time, double.NaN, "NoCausalEntry");
                    return true;
                }

                trade.Entered = true;
                trade.EntryTime = time;
                trade.EntryPrice = price;
                trade.ExitDeadline = FailedPmhMaxHoldMinutes > 0
                    ? time.AddMinutes(FailedPmhMaxHoldMinutes)
                    : Core.Globals.MinDate;
                return false;
            }

            var pointValue = Instrument == null
                ? 0.0
                : Instrument.MasterInstrument.PointValue;
            var size = Math.Max(1, OrderQuantity);
            trade.MfeDollars = Math.Max(trade.MfeDollars,
                Math.Max(0, price - trade.EntryPrice) * pointValue * size);
            trade.MaeDollars = Math.Max(trade.MaeDollars,
                Math.Max(0, trade.EntryPrice - price) * pointValue * size);

            var stop = trade.EntryPrice - trade.StopTicks * TickSize;
            var target = trade.EntryPrice + trade.TargetTicks * TickSize;
            if (price <= stop)
            {
                WriteFailedPmhTrade(trade, time, stop, "StopLoss");
                return true;
            }

            if (price >= target)
            {
                WriteFailedPmhTrade(trade, time, target, "ProfitTarget");
                return true;
            }

            if (time.Date != trade.EntryTime.Date
                || ToTimeValue(time) >= FlattenTime)
            {
                WriteFailedPmhTrade(trade, time, price, "FlattenTime");
                return true;
            }

            if (trade.ExitDeadline != Core.Globals.MinDate
                && time >= trade.ExitDeadline)
            {
                WriteFailedPmhTrade(trade, time, price, "MaxHold");
                return true;
            }

            return false;
        }

        private void WriteFailedPmhCandidate(
            FailedPmhCandidate candidate, string status, DateTime observedAt)
        {
            if (failedPmhResearchFaulted)
                return;

            var s = candidate.Parent;
            var line = string.Join(",", new[]
            {
                CsvFailedPmh(researchRunId),
                CsvFailedPmh(Instrument == null ? "" : Instrument.FullName),
                CsvFailedPmh(FailedPmhExperimentVersion),
                CsvFailedPmh(candidate.Id),
                DateFailedPmh(s.EventTime),
                NumberFailedPmh(candidate.Level),
                NumberFailedPmh(s.PmhSweepTicks),
                NumberFailedPmh(s.SignalClose),
                NumberFailedPmh(s.Atr5Ticks),
                NumberFailedPmh(s.Ema5Fast),
                NumberFailedPmh(s.Ema5Slow),
                NumberFailedPmh(s.Ema5FastSlopeTicks),
                NumberFailedPmh(s.Ema5FastDistanceTicks),
                candidate.BullishContextPassed ? "1" : "0",
                CsvFailedPmh(status),
                DateFailedPmh(candidate.ConfirmationTime),
                NumberFailedPmh(candidate.ConfirmationClose),
                candidate.ConsecutiveCloses.ToString(CultureInfo.InvariantCulture),
                DateFailedPmh(observedAt)
            });
            File.AppendAllText(failedPmhCandidatePath,
                line + Environment.NewLine);
        }

        private void WriteFailedPmhTrade(
            FailedPmhTrade trade, DateTime exitTime, double exitPrice, string reason)
        {
            if (failedPmhResearchFaulted)
                return;

            var candidate = trade.Candidate;
            var pnl = trade.Entered && IsFinite(exitPrice)
                ? (exitPrice - trade.EntryPrice)
                    * (Instrument == null ? 0.0 : Instrument.MasterInstrument.PointValue)
                    * Math.Max(1, OrderQuantity)
                : double.NaN;

            var line = string.Join(",", new[]
            {
                CsvFailedPmh(researchRunId),
                CsvFailedPmh(Instrument == null ? "" : Instrument.FullName),
                CsvFailedPmh(FailedPmhExperimentVersion),
                CsvFailedPmh(candidate.Id),
                DateFailedPmh(candidate.Parent.EventTime),
                DateFailedPmh(candidate.ConfirmationTime),
                NumberFailedPmh(candidate.ConfirmationClose),
                NumberFailedPmh(candidate.Level),
                CsvFailedPmh(trade.Scenario),
                trade.StopTicks.ToString(CultureInfo.InvariantCulture),
                trade.TargetTicks.ToString(CultureInfo.InvariantCulture),
                DateFailedPmh(trade.EntryTime),
                NumberFailedPmh(trade.EntryPrice),
                DateFailedPmh(exitTime),
                NumberFailedPmh(exitPrice),
                CsvFailedPmh(reason),
                NumberFailedPmh(pnl),
                NumberFailedPmh(trade.MfeDollars),
                NumberFailedPmh(trade.MaeDollars),
                CsvFailedPmh(State.ToString())
            });
            File.AppendAllText(failedPmhTradePath, line + Environment.NewLine);
        }

        private void DisposeFailedPmhContinuationResearch()
        {
            if (!EnableFailedPmhContinuationResearch || failedPmhResearchFaulted)
                return;

            lock (failedPmhResearchSync)
            {
                try
                {
                    foreach (var candidate in failedPmhAwaiting)
                        WriteFailedPmhCandidate(candidate,
                            "IncompleteAtTermination", Core.Globals.MinDate);
                    failedPmhAwaiting.Clear();

                    foreach (var trade in failedPmhTrades)
                        WriteFailedPmhTrade(trade,
                            Core.Globals.MinDate, double.NaN, "IncompleteAtTermination");
                    failedPmhTrades.Clear();
                }
                catch (Exception ex)
                {
                    DisableFailedPmhResearch("Termination", ex);
                }
            }
        }

        private void DisableFailedPmhResearch(string stage, Exception ex)
        {
            failedPmhResearchFaulted = true;
            Diagnostic(DateTime.Now,
                "FAILED PMH RESEARCH DISABLED Stage={0} Error={1}",
                stage, ex.Message);
        }

        private static string DateFailedPmh(DateTime value)
        {
            return value == Core.Globals.MinDate
                ? ""
                : value.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
        }

        private static string NumberFailedPmh(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? ""
                : value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private static string CsvFailedPmh(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\"\"") + "\"";
        }
    }
}
