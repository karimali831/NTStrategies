using System;
using System.Collections.Generic;
using System.Linq;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOpeningVolumeProfileBreakout : Strategy
    {
        private readonly List<TrackedResearchSetup> activeResearchSetups = new List<TrackedResearchSetup>();
        private int researchSetupsToday;

        private void StartResearchSetup(SetupCandidate candidate)
        {
            if (!EnableDataCollection || !EnableResearchMode || !TrackForwardOutcome)
                return;

            if (researchSetupsToday >= MaxResearchSetupsPerDay)
                return;

            activeResearchSetups.Add(new TrackedResearchSetup
            {
                Candidate = candidate,
                BarsTracked = 0,
                MfeUsd = 0,
                MaeUsd = 0
            });

            researchSetupsToday++;
        }

        private void UpdateResearchSetupsFromTick()
        {
            if (!EnableDataCollection || !EnableResearchMode || !TrackForwardOutcome)
                return;

            if (activeResearchSetups.Count == 0)
                return;

            if (!UseTickDataForProfile)
                return;

            if (CurrentBars.Length <= 1 || CurrentBars[1] < 1)
                return;

            var tickTime = Times[1][0];
            var tickPrice = Closes[1][0];

            if (tickPrice <= 0)
                return;

            var pointValue = Instrument.MasterInstrument.PointValue;

            for (int i = activeResearchSetups.Count - 1; i >= 0; i--)
            {
                var setup = activeResearchSetups[i];

                if (tickTime <= setup.Candidate.SignalTimeChart)
                    continue;

                var safeQuantity = Math.Max(1, setup.Candidate.Quantity);

                if (setup.Candidate.Direction == "LONG")
                {
                    var openPnl = (tickPrice - setup.Candidate.EntryPrice) * pointValue * safeQuantity;

                    setup.MfeUsd = Math.Max(setup.MfeUsd, openPnl);
                    setup.MaeUsd = Math.Min(setup.MaeUsd, openPnl);

                    if (tickPrice >= setup.Candidate.TargetPrice)
                    {
                        LogSetupOutcome(
                            "RESEARCH_OUTCOME",
                            setup,
                            "TargetHit",
                            tickTime,
                            setup.Candidate.TargetPrice,
                            "Resolved from 1-tick series.");

                        activeResearchSetups.RemoveAt(i);
                        continue;
                    }

                    if (tickPrice <= setup.Candidate.StopPrice)
                    {
                        LogSetupOutcome(
                            "RESEARCH_OUTCOME",
                            setup,
                            "StopHit",
                            tickTime,
                            setup.Candidate.StopPrice,
                            "Resolved from 1-tick series.");

                        activeResearchSetups.RemoveAt(i);
                    }
                }
                else if (setup.Candidate.Direction == "SHORT")
                {
                    var openPnl = (setup.Candidate.EntryPrice - tickPrice) * pointValue * safeQuantity;

                    setup.MfeUsd = Math.Max(setup.MfeUsd, openPnl);
                    setup.MaeUsd = Math.Min(setup.MaeUsd, openPnl);

                    if (tickPrice <= setup.Candidate.TargetPrice)
                    {
                        LogSetupOutcome(
                            "RESEARCH_OUTCOME",
                            setup,
                            "TargetHit",
                            tickTime,
                            setup.Candidate.TargetPrice,
                            "Resolved from 1-tick series.");

                        activeResearchSetups.RemoveAt(i);
                        continue;
                    }

                    if (tickPrice >= setup.Candidate.StopPrice)
                    {
                        LogSetupOutcome(
                            "RESEARCH_OUTCOME",
                            setup,
                            "StopHit",
                            tickTime,
                            setup.Candidate.StopPrice,
                            "Resolved from 1-tick series.");

                        activeResearchSetups.RemoveAt(i);
                    }
                }
            }
        }

        private void UpdateResearchSetupBarTracking()
        {
            if (!EnableDataCollection || !EnableResearchMode || !TrackForwardOutcome)
                return;

            if (activeResearchSetups.Count == 0)
                return;

            for (int i = activeResearchSetups.Count - 1; i >= 0; i--)
            {
                var setup = activeResearchSetups[i];

                if (Time[0] <= setup.Candidate.SignalTimeChart)
                    continue;

                setup.BarsTracked++;

                if (setup.BarsTracked >= ForwardBarsToTrack)
                {
                    LogSetupOutcome(
                        "RESEARCH_OUTCOME",
                        setup,
                        "Timeout",
                        Time[0],
                        Close[0],
                        "Forward bar tracking limit reached.");

                    activeResearchSetups.RemoveAt(i);
                }
            }
        }

        private void FlushResearchSetupsForNewDay()
        {
            if (activeResearchSetups.Count == 0)
                return;

            foreach (var setup in activeResearchSetups.ToList())
            {
                LogSetupOutcome(
                    "RESEARCH_OUTCOME",
                    setup,
                    "NewDay",
                    Time[0],
                    Close[0],
                    "New trading date reached before stop/target outcome.");
            }

            activeResearchSetups.Clear();
        }
    }
}