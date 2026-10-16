using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;

namespace NinjaTrader.NinjaScript.Indicators
{
    // 09:30-09:35 ET range derived exclusively from completed minute bars.
    // Apply to a 1-minute or 5-minute chart; platform/chart timestamps MUST already be ET.
    public class NinjexFirstFiveMinuteRange : Indicator
    {
        private DateTime sessionDate = DateTime.MinValue;
        private int pieces;
        private double high;
        private double low;
        private bool ready;
        private int completedAtBar;

        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "NinjexFirstFiveMinuteRange";
                Description = "First RTH five-minute candle (09:30-09:35 ET), with optional daily high/low lines.";
                Calculate = Calculate.OnBarClose;
                IsOverlay = true;
                DrawOnPricePanel = true;
                DisplayInDataBox = true;
                IsSuspendedWhileInactive = true;
                ShowLevels = true;
                AddPlot(Brushes.Transparent, "OpeningHigh");
                AddPlot(Brushes.Transparent, "OpeningLow");
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute
                    || (BarsPeriod.Value != 1 && BarsPeriod.Value != 5))
                    throw new InvalidOperationException("NinjexFirstFiveMinuteRange requires a 1-minute or 5-minute chart.");
            }
        }

        protected override void OnBarUpdate()
        {
            DateTime end = Time[0];
            if (sessionDate != end.Date)
            {
                sessionDate = end.Date;
                pieces = 0;
                ready = false;
                high = double.MinValue;
                low = double.MaxValue;
                completedAtBar = -1;
            }

            OpeningHigh[0] = double.NaN;
            OpeningLow[0] = double.NaN;

            DateTime first = sessionDate.AddHours(9).AddMinutes(30);
            DateTime last = first.AddMinutes(5);
            bool isPart = BarsPeriod.Value == 1
                ? end > first && end <= last
                : end == last;

            if (!ready && isPart)
            {
                high = Math.Max(high, High[0]);
                low = Math.Min(low, Low[0]);
                pieces++;
            }

            int required = BarsPeriod.Value == 1 ? 5 : 1;
            if (!ready && end >= last && pieces == required
                && high > low)
            {
                ready = true;
                completedAtBar = CurrentBar;
            }

            // A missing opening bar intentionally leaves the level unavailable.
            // Never infer a range from incomplete / late market data.
            if (!ready)
                return;

            OpeningHigh[0] = high;
            OpeningLow[0] = low;

            if (ShowLevels)
            {
                string key = sessionDate.ToString("yyyyMMdd");
                int barsAgo = CurrentBar - completedAtBar;
                Draw.Line(this, "NX5_ORH_" + key, false,
                    barsAgo, high, 0, high,
                    Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
                Draw.Line(this, "NX5_ORL_" + key, false,
                    barsAgo, low, 0, low,
                    Brushes.OrangeRed, DashStyleHelper.Solid, 2);
            }
        }

        [NinjaScriptProperty]
        [Display(Name = "Show Lines", GroupName = "Visuals", Order = 1)]
        public bool ShowLevels { get; set; }

        [Browsable(false)]
        [System.Xml.Serialization.XmlIgnore]
        public Series<double> OpeningHigh { get { return Values[0]; } }

        [Browsable(false)]
        [System.Xml.Serialization.XmlIgnore]
        public Series<double> OpeningLow { get { return Values[1]; } }
    }
}