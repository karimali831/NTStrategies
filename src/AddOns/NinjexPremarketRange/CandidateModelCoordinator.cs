using System;
using System.Collections.Generic;
using System.Linq;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Models;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Contracts;
using NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange.Interfaces;

namespace NinjaTrader.NinjaScript.AddOns.Ninjex.PremarketRange
{
    public sealed class CandidateModelCoordinator
    {
        private readonly IReadOnlyList<IEntryCandidateModel> models;
        private readonly HashSet<string> emittedCandidateIds =
            new HashSet<string>(StringComparer.Ordinal);
        
        public CandidateModelCoordinator(
            IEnumerable<IEntryCandidateModel> models)
        {
            if (models == null)
                throw new ArgumentNullException(nameof(models));

            this.models = models
                .Where(x => x != null)
                .ToList()
                .AsReadOnly();
        }

        public void Reset(RangeSessionSnapshot session)
        {
            emittedCandidateIds.Clear();

            foreach (var model in models)
                model.Reset(session);
        }

        public void OnBreakout(BreakoutSignalSnapshot breakout)
        {
            foreach (var model in models)
            {
                if (model.IsEnabled)
                    model.OnBreakout(breakout);
            }
        }

        public IReadOnlyList<CandidateSignal> Evaluate(
            CandidateModelContext context)
        {
            var output = new List<CandidateSignal>();

            foreach (var model in models)
            {
                if (!model.IsEnabled)
                    continue;

                var generated = model.Evaluate(context);
                if (generated == null)
                    continue;

                foreach (var signal in generated)
                {
                    if (signal == null)
                        continue;

                    if (!emittedCandidateIds.Add(signal.CandidateId))
                        continue;

                    output.Add(signal);
                }
            }

            return output.AsReadOnly();
        }

        public void OnBreakoutResolved(string eventId)
        {
            foreach (var model in models)
                model.OnBreakoutResolved(eventId);
        }
    }
}