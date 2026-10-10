using System;
using System.Collections.Generic;
namespace NinjaTrader.NinjaScript.Strategies
{
    internal sealed class EvaluationMinute
    {
        internal DateTime Start, End;
        internal double Open, High, Low, Close, Volume;
        internal bool Complete;
    }
    internal sealed class EvaluationTrendFrame
    {
        private readonly int period;
        private DateTime bucket, lastMinute;
        private int count, bars;
        private double open, high, low, close, volume, fast, slow, atr, previousClose;
        private bool complete;
        internal DateTime AsOf { get; private set; }
        internal bool Ready { get { return bars >= 13; } }
        internal int Direction { get; private set; }
        internal double Strength { get; private set; }
        internal bool IsCurrent(DateTime now) { return Ready && AsOf<=now && now-AsOf<=TimeSpan.FromMinutes(period+1); }
        internal EvaluationTrendFrame(int minutes) { period=minutes; }
        internal void Accept(EvaluationMinute m,Action<int,DateTime,double,double,double,double,double,bool> export)
        {
            int minuteOfDay=m.Start.Hour*60+m.Start.Minute;
            DateTime b=m.Start.Date.AddMinutes((minuteOfDay/period)*period);
            if(count==0 || b!=bucket)
            {
                bucket=b;count=0;open=m.Open;high=m.High;low=m.Low;volume=0;
                complete=m.Start==b;
            }
            if(count>0 && m.Start!=lastMinute.AddMinutes(1)) complete=false;
            complete &= m.Complete;lastMinute=m.Start;count++;
            high=Math.Max(high,m.High);low=Math.Min(low,m.Low);close=m.Close;volume+=m.Volume;
            if(m.End!=b.AddMinutes(period))return;
            bool valid=complete && count==period;
            export(period,m.End,open,high,low,close,volume,valid);
            if(valid)
            {
                double tr=bars==0?high-low:Math.Max(high-low,Math.Max(Math.Abs(high-previousClose),Math.Abs(low-previousClose)));
                double oldFast=fast;
                if(bars==0){fast=slow=close;atr=tr;}
                else{fast+=(2.0/6)*(close-fast);slow+=(2.0/14)*(close-slow);atr+=(tr-atr)/14;}
                previousClose=close;bars++;AsOf=m.End;
                Strength=atr>0?(fast-slow)/atr:0;
                Direction=Ready && Math.Abs(Strength)>=.10 && Math.Sign(fast-slow)==Math.Sign(fast-oldFast) && Math.Sign(close-fast)==Math.Sign(fast-slow)?Math.Sign(fast-slow):0;
            }
            else{bars=0;Direction=0;Strength=0;AsOf=default(DateTime);}
            count=0;
        }
    }
    internal sealed class EvaluationEngineOptions
    {
        internal bool ConfirmedEntries;
        internal int ReclaimHoldSeconds=2, QuietGapSeconds=300;
        internal double ImpulseAtrFraction=.5;
    }
}
