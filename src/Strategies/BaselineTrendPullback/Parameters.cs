#region Using declarations
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Text;
using NinjaTrader.Cbi;
using NinjaTrader.Data;
using NinjaTrader.Gui.Tools;
using NinjaTrader.NinjaScript;
using NinjaTrader.NinjaScript.Indicators;
using NinjaTrader.NinjaScript.Strategies;
#endregion

namespace NinjaTrader.NinjaScript.Strategies
{

    public partial class BaselineTrendPullback : Strategy
    {
        // ----- Session state -----
        [NinjaScriptProperty]
        [Display(Name = "MaxTradesPerSession", GroupName = "Session", Order = 10)]
        public int MaxTradesPerSession { get; set; }

        [Browsable(false)] public int TradesThisSession { get; private set; }

        [NinjaScriptProperty]
        [Display(Name = "UseTimeWindow", GroupName = "Session", Order = 20)]
        public bool UseTimeWindow { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "StartTimeHHmm", GroupName = "Session", Order = 21)]
        public int StartTimeHHmm { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EndTimeHHmm", GroupName = "Session", Order = 22)]
        public int EndTimeHHmm { get; set; }

        // ----- Indicators -----
        [NinjaScriptProperty]
        [Display(Name = "EmaFastPeriod", GroupName = "Indicators", Order = 10)]
        public int EmaFastPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "EmaSlowPeriod", GroupName = "Indicators", Order = 11)]
        public int EmaSlowPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AdxPeriod", GroupName = "Indicators", Order = 12)]
        public int AdxPeriod { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AtrPeriod", GroupName = "Indicators", Order = 13)]
        public int AtrPeriod { get; set; }

        // ----- Trend / structure -----
        [NinjaScriptProperty]
        [Display(Name = "TrendSlopeLookbackBars", GroupName = "Trend", Order = 10)]
        public int TrendSlopeLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MinTrendSlopeTicks", GroupName = "Trend", Order = 11)]
        public int MinTrendSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MinEmaSepTicks", GroupName = "Trend", Order = 12)]
        public int MinEmaSepTicks { get; set; }

        // ----- Regime (minimal) -----
        [NinjaScriptProperty]
        [Display(Name = "MinAdx", GroupName = "Regime", Order = 10)]
        public int MinAdx { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MaxAdx", GroupName = "Regime", Order = 11)]
        public int MaxAdx { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "AtrMedianLookbackBars", GroupName = "Regime", Order = 12)]
        public int AtrMedianLookbackBars { get; set; }

        // ----- Pullback / trigger -----
        [NinjaScriptProperty]
        [Display(Name = "TouchLookbackBars", GroupName = "Pullback", Order = 10)]
        public int TouchLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "TouchTicks", GroupName = "Pullback", Order = 11)]
        public int TouchTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RequireCloseBackAcrossFastEma", GroupName = "Pullback", Order = 12)]
        public bool RequireCloseBackAcrossFastEma { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "RequireSignalCandleInTrendDir", GroupName = "Pullback", Order = 13)]
        public bool RequireSignalCandleInTrendDir { get; set; }
        
        [NinjaScriptProperty]
        [Display(Name = "UseBreakoutTrigger", GroupName = "Pullback", Order = 14)]
        public bool UseBreakoutTrigger { get; set; }

        // ----- Candle quality -----
        [NinjaScriptProperty]
        [Display(Name = "MaxWickPct", GroupName = "Candle", Order = 10)]
        public double MaxWickPct { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MinRangeTicks", GroupName = "Candle", Order = 11)]
        public int MinRangeTicks { get; set; }

        // ----- Risk -----
        [NinjaScriptProperty]
        [Display(Name = "RiskMode", GroupName = "Risk", Order = 10)]
        public BaselineRiskMode RiskMode { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "SwingLookbackBars", GroupName = "Risk", Order = 11)]
        public int SwingLookbackBars { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "StopBufferTicks", GroupName = "Risk", Order = 12)]
        public int StopBufferTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "MaxStopTicks", GroupName = "Risk", Order = 13)]
        public int MaxStopTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "ProfitTargetR", GroupName = "Risk", Order = 14)]
        public double ProfitTargetR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "UseBreakEven", GroupName = "Risk", Order = 20)]
        public bool UseBreakEven { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BreakEvenAtR", GroupName = "Risk", Order = 21)]
        public double BreakEvenAtR { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "BreakEvenPlusTicks", GroupName = "Risk", Order = 22)]
        public int BreakEvenPlusTicks { get; set; }

        // ----- Logging -----
        [NinjaScriptProperty]
        [Display(Name = "DiagEnabled", GroupName = "Logging", Order = 10)]
        public bool DiagEnabled { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "LogOnlyOnSignalOrBlock", GroupName = "Logging", Order = 11)]
        public bool LogOnlyOnSignalOrBlock { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "LogToFile", GroupName = "Logging", Order = 12)]
        public bool LogToFile { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "LogFileNamePrefix", GroupName = "Logging", Order = 13)]
        public string LogFileNamePrefix { get; set; }

        private void ResetSessionIfNeeded()
        {
            // New session detection
            if (Bars.IsFirstBarOfSession && Time[0].Date != _lastSessionDate.Date)
            {
                _lastSessionDate = Time[0].Date;
                TradesThisSession = 0;

                if (DiagEnabled)
                {
                    _log.Info("SESSION", new Dictionary<string, object>
                    {
                        ["t"] = Time[0].ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                        ["event"] = "session-start"
                    });
                }
            }
        }
    }
}
