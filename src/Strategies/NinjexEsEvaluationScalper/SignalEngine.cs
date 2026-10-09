using System;
using System.Collections.Generic;
using System.Linq;
namespace NinjaTrader.NinjaScript.Strategies
{
    internal sealed class EvaluationCandidate
    {
        internal long Id;
        internal string Model, LevelName;
        internal int Direction;
        internal double Level, Stop;
    }
    // Pure ordered Last-tick processor. No NinjaTrader, orders or IO dependencies.
    internal sealed class EvaluationSignalEngine
    {
        private sealed class Level
        {
            internal string Name;
            internal double Price;
            internal int SweepSide, BreakSide;
            internal bool Retested, Swept;
            internal double Extreme;
            internal DateTime SweepTime, BreakTime, Used;
        }
        private sealed class Minute
        {
            internal DateTime Time;
            internal double High, Low, Close;
        }
        private readonly double tick;
        private readonly int sweep, reclaim, breakout, tolerance, confirm, expiry;
        private readonly List<Level> levels = new List<Level>();
        private readonly Queue<Minute> recent = new Queue<Minute>();
        private Minute minute;
        private DateTime day, lastTime, overnightStart, rthFirst, rthLast;
        private double lastPrice, onHigh = double.MinValue, onLow = double.MaxValue,
            pmHigh = double.MinValue, pmLow = double.MaxValue,
            rthHigh = double.MinValue, rthLow = double.MaxValue, rthClose,
            orHigh = double.MinValue, orLow = double.MaxValue, weightedPrice, totalVolume,
            ema9, ema21, previousClose, atr;
        private bool overnightGood, premarketGood;
        private int minutes;
        private long id;
        internal string LevelSummary { get { return string.Join(";",levels.Select(x=>x.Name+"="+x.Price.ToString(System.Globalization.CultureInfo.InvariantCulture))); } }
        internal double Vwap { get { return totalVolume > 0 ? weightedPrice / totalVolume : double.NaN; } }
        internal double AtrTicks { get { return minutes >= 14 ? atr / tick : double.NaN; } }
        internal EvaluationSignalEngine(double tickSize, int sweepTicks, int reclaimTicks, int breakoutTicks,
            int retestTicks, int confirmTicks, int expirySeconds)
        {
            tick = tickSize; sweep = sweepTicks; reclaim = reclaimTicks; breakout = breakoutTicks;
            tolerance = retestTicks; confirm = confirmTicks; expiry = expirySeconds;
        }
        internal bool InWindow(DateTime time)
        {
            var t = time.TimeOfDay;
            return (t >= new TimeSpan(9,35,0) && t < new TimeSpan(11,30,0)) ||
                   (t >= new TimeSpan(13,0,0) && t < new TimeSpan(15,30,0));
        }
        private void Put(string name, double price)
        {
            if (double.IsNaN(price) || price == double.MinValue || price == double.MaxValue) return;
            var old = levels.FirstOrDefault(x => x.Name == name);
            if (old != null && old.Price == price) return;
            if (old != null) levels.Remove(old);
            levels.Add(new Level { Name = name, Price = price });
        }
        internal List<EvaluationCandidate> Accept(DateTime time, double price, double volume, Action<string,string> quality)
        {
            var output = new List<EvaluationCandidate>();
            if (lastTime != default(DateTime) && time < lastTime)
            { quality("OutOfOrder", lastTime.ToString("o")); return output; }
            if (lastTime != default(DateTime) && time - lastTime > TimeSpan.FromSeconds(60) &&
                (time.TimeOfDay < new TimeSpan(16,0,0)))
            {
                quality("TickGap", (time-lastTime).TotalSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (time.TimeOfDay < new TimeSpan(9,30,0)) { overnightGood = false; premarketGood = false; }
                foreach (var l in levels) { l.SweepSide = l.BreakSide = 0; }
            }
            if (time.Date != day)
            {
                levels.Clear();
                // Only use a prior RTH with observed open/close, and never across a long contract gap.
                if (rthFirst != default(DateTime) && rthFirst.TimeOfDay <= new TimeSpan(9,31,0) &&
                    rthLast.TimeOfDay >= new TimeSpan(15,59,0) && (time.Date-rthLast.Date).TotalDays <= 4)
                { Put("PDH", rthHigh); Put("PDL", rthLow); Put("PDC", rthClose); }
                day = time.Date; weightedPrice = totalVolume = 0;
                rthHigh = orHigh = double.MinValue; rthLow = orLow = double.MaxValue;
                rthFirst = rthLast = default(DateTime);
            }
            var tod = time.TimeOfDay;
            if (tod >= new TimeSpan(18,0,0) && overnightStart.Date != time.Date)
            {
                overnightStart = time; overnightGood = tod <= new TimeSpan(18,1,0);
                premarketGood = false; onHigh = pmHigh = double.MinValue; onLow = pmLow = double.MaxValue;
            }
            if (tod >= new TimeSpan(18,0,0) || tod < new TimeSpan(9,30,0))
            {
                onHigh = Math.Max(onHigh,price); onLow = Math.Min(onLow,price);
                if (tod >= new TimeSpan(8,0,0) && tod < new TimeSpan(9,30,0))
                {
                    if (pmHigh == double.MinValue) premarketGood = tod <= new TimeSpan(8,1,0);
                    pmHigh = Math.Max(pmHigh,price); pmLow = Math.Min(pmLow,price);
                }
            }
            if (tod >= new TimeSpan(9,30,0) && tod < new TimeSpan(16,0,0))
            {
                if (rthFirst == default(DateTime))
                {
                    rthFirst = time;
                    if (overnightGood && overnightStart.Date == time.Date.AddDays(-1)) { Put("ONH", onHigh); Put("ONL", onLow); }
                    else quality("OvernightUnavailable", "Require prior 18:00 ET warm-up without observed >60s gaps");
                    if (premarketGood) { Put("PMH", pmHigh); Put("PML", pmLow); }
                }
                rthLast = time; rthHigh = Math.Max(rthHigh,price); rthLow = Math.Min(rthLow,price); rthClose = price;
                weightedPrice += price * volume; totalVolume += volume;
                if (tod < new TimeSpan(9,35,0)) { orHigh = Math.Max(orHigh,price); orLow = Math.Min(orLow,price); }
                else if (rthFirst.TimeOfDay <= new TimeSpan(9,31,0)) { Put("OR5H",orHigh); Put("OR5L",orLow); }
            }
            DateTime bucket = new DateTime(time.Year,time.Month,time.Day,time.Hour,time.Minute,0);
            if (minute == null || minute.Time != bucket)
            {
                if (minute != null)
                {
                    if ((bucket-minute.Time).TotalMinutes == 1)
                    {
                        double tr = Math.Max(minute.High-minute.Low, Math.Max(Math.Abs(minute.High-previousClose),Math.Abs(minute.Low-previousClose)));
                        if (minutes == 0) { ema9 = ema21 = minute.Close; atr = minute.High-minute.Low; }
                        else { ema9 += .2*(minute.Close-ema9); ema21 += (2.0/22)*(minute.Close-ema21); atr += (tr-atr)/14; }
                        previousClose = minute.Close; minutes++;
                        if (minute.Time.TimeOfDay >= new TimeSpan(9,30,0) && minute.Time.TimeOfDay < new TimeSpan(16,0,0)) recent.Enqueue(minute);
                    }
                    else { recent.Clear(); minutes = 0; }
                }
                while (recent.Count > 0 && (bucket-recent.Peek().Time).TotalMinutes > 5) recent.Dequeue();
                if (recent.Count != 5) levels.RemoveAll(x => x.Name == "RollingH" || x.Name == "RollingL");
                if (recent.Count == 5)
                { Put("RollingH",recent.Max(x=>x.High)); Put("RollingL",recent.Min(x=>x.Low)); }
                minute = new Minute { Time=bucket, High=price, Low=price, Close=price };
            }
            minute.High = Math.Max(minute.High,price); minute.Low = Math.Min(minute.Low,price); minute.Close = price;
            if (InWindow(time) && minutes >= 14 && rthFirst != default(DateTime) && rthFirst.TimeOfDay <= new TimeSpan(9,31,0))
            {
                foreach (var l in levels)
                {
                    if ((time-l.Used).TotalSeconds < expiry) continue;
                    if (l.SweepSide != 0 && (time-l.SweepTime).TotalSeconds > expiry) l.SweepSide = 0;
                    if (l.BreakSide != 0 && (time-l.BreakTime).TotalSeconds > expiry) { l.BreakSide = 0; l.Retested = false; }
                    if (l.SweepSide == 0)
                    {
                        if (lastPrice >= l.Price && price < l.Price)
                        { l.SweepSide=1; l.Extreme=price; l.SweepTime=time; l.Swept=false; }
                        else if (lastPrice <= l.Price && price > l.Price)
                        { l.SweepSide=-1; l.Extreme=price; l.SweepTime=time; l.Swept=false; }
                    }
                    if (l.SweepSide != 0)
                    {
                        l.Extreme = l.SweepSide>0 ? Math.Min(l.Extreme,price) : Math.Max(l.Extreme,price);
                        if (l.SweepSide*(l.Extreme-l.Price) <= -sweep*tick) l.Swept=true;
                        if (l.Swept && l.SweepSide*(price-l.Price) >= reclaim*tick)
                        {
                            output.Add(New(l,"SweepReclaim",l.SweepSide,l.Extreme-l.SweepSide*2*tick));
                            l.SweepSide=l.BreakSide=0; l.Used=time; continue;
                        }
                    }
                    if (l.BreakSide == 0)
                    {
                        if (lastPrice < l.Price+breakout*tick && price >= l.Price+breakout*tick)
                        { l.BreakSide=1; l.BreakTime=time; l.Extreme=price; l.Retested=false; }
                        else if (lastPrice > l.Price-breakout*tick && price <= l.Price-breakout*tick)
                        { l.BreakSide=-1; l.BreakTime=time; l.Extreme=price; l.Retested=false; }
                    }
                    else
                    {
                        int d=l.BreakSide;
                        if (d*(price-l.Price) < -2*tick) { l.BreakSide=0; l.Retested=false; }
                        else
                        {
                            if (!l.Retested && Math.Abs(price-l.Price)<=tolerance*tick) { l.Retested=true; l.Extreme=price; }
                            if (l.Retested)
                            {
                                l.Extreme=d>0?Math.Min(l.Extreme,price):Math.Max(l.Extreme,price);
                                if (d*(price-l.Price)>=(tolerance+confirm)*tick && d*(ema9-ema21)>0 && d*(price-Vwap)>0)
                                {
                                    output.Add(New(l,"MomentumRetest",d,l.Extreme-d*2*tick));
                                    l.BreakSide=l.SweepSide=0; l.Retested=false; l.Used=time;
                                }
                            }
                        }
                    }
                }
            }
            lastTime=time; lastPrice=price;
            return output;
        }
        private EvaluationCandidate New(Level l,string model,int direction,double stop)
        { return new EvaluationCandidate { Id=++id, Model=model, Direction=direction, LevelName=l.Name, Level=l.Price, Stop=stop }; }
    }
}
