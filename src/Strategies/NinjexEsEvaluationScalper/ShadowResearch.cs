using System;
using System.Collections.Generic;
namespace NinjaTrader.NinjaScript.Strategies
{
    // Candidate-only diagnostics, independent of actual orders. Last-price barrier outcomes
    // are hypothetical and intentionally do not contribute to the evaluation pass rate.
    internal sealed class EvaluationShadowResearch
    {
        private sealed class Path
        {
            internal EvaluationCandidate Candidate;
            internal DateTime Start;
            internal double Entry, Mfe, Mae;
            internal int StopTicks;
            internal double Reward;
        }
        private readonly List<Path> paths = new List<Path>();
        private readonly EvaluationResearchWriter writer;
        private readonly double tick;
        internal EvaluationShadowResearch(EvaluationResearchWriter output, double tickSize)
        { writer=output; tick=tickSize; }
        internal void Register(EvaluationCandidate candidate, DateTime time, double price)
        {
            if (paths.Count > 2000)
            { writer.Write("quality",time,"Realtime","ShadowCapacity","Candidate omitted from shadows"); return; }
            foreach (int stop in new[] {8,12,16,20})
                foreach (double reward in new[] {1.0,1.5,2.0})
                    paths.Add(new Path { Candidate=candidate,Start=time,Entry=price,StopTicks=stop,Reward=reward });
        }
        internal void Accept(DateTime time,double price,bool inWindow)
        {
            for (int i=paths.Count-1;i>=0;i--)
            {
                var p=paths[i]; double move=p.Candidate.Direction*(price-p.Entry)/tick;
                p.Mfe=Math.Max(p.Mfe,move); p.Mae=Math.Min(p.Mae,move);
                string outcome=move<=-p.StopTicks?"Stop":move>=p.StopTicks*p.Reward?"Target":
                    !inWindow?"WindowEnd":(time-p.Start).TotalSeconds>=300?"Time":"";
                if (outcome.Length==0) continue;
                writer.Write("shadows",p.Start,time,p.Candidate.Id,p.Candidate.Model,p.StopTicks,p.Reward,
                    outcome,move,p.Mfe,p.Mae,"LastPrices_NoCosts_NoOrders"); paths.RemoveAt(i);
            }
        }
        internal void Finish(DateTime time)
        {
            foreach(var p in paths) writer.Write("shadows",p.Start,time,p.Candidate.Id,p.Candidate.Model,
                p.StopTicks,p.Reward,"Censored",double.NaN,p.Mfe,p.Mae,"LastPrices_NoCosts_NoOrders");
            paths.Clear();
        }
    }
}
