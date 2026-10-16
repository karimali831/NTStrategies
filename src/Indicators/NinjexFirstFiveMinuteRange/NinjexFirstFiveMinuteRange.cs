using System;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using NinjaTrader.Data;
using NinjaTrader.Gui;
using NinjaTrader.NinjaScript.DrawingTools;
using NinjaTrader.NinjaScript.Ninjex;

namespace NinjaTrader.NinjaScript.Indicators
{
    // 09:30-09:35 ET range derived exclusively from completed minute bars.
    // Apply to a 1-minute or 5-minute chart; platform/chart timestamps MUST already be ET.
    public class NinjexFirstFiveMinuteRange : Indicator
    {
        private NinjexFirstFiveRangeEngine rangeEngine;
        private int completedAtBar = -1;

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
                rangeEngine = new NinjexFirstFiveRangeEngine();
            }
        }

        protected override void OnBarUpdate()
        {
            bool wasReady = rangeEngine.Ready;
            DateTime previousDate = rangeEngine.ActiveDate;
            rangeEngine.Accept(Time[0], High[0], Low[0], BarsPeriod.Value);
            OpeningHigh[0] = rangeEngine.Ready ? rangeEngine.High : double.NaN;
            OpeningLow[0] = rangeEngine.Ready ? rangeEngine.Low : double.NaN;

            if (!rangeEngine.Ready)
                return;
            if (!wasReady || previousDate != rangeEngine.ActiveDate)
                completedAtBar = CurrentBar;

            if (ShowLevels)
            {
                string key = rangeEngine.ActiveDate.ToString("yyyyMMdd");
                int barsAgo = CurrentBar - completedAtBar;
                Draw.Line(this, "NX5_ORH_" + key, false,
                    barsAgo, rangeEngine.High, 0, rangeEngine.High,
                    Brushes.DodgerBlue, DashStyleHelper.Solid, 2);
                Draw.Line(this, "NX5_ORL_" + key, false,
                    barsAgo, rangeEngine.Low, 0, rangeEngine.Low,
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