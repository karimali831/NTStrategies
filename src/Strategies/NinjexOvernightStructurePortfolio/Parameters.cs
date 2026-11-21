using System.ComponentModel.DataAnnotations;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightStructurePortfolio
    {
        #region 1. Sessions & data
        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Overnight start (ET HHmmss)", Description = "Range starts at previous ETH evening.", GroupName = "1. Sessions & data", Order = 0)]
        public int OvernightStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Premarket start (ET HHmmss)", Description = "Premarket range starts 03:00 ET.", GroupName = "1. Sessions & data", Order = 1)]
        public int PremarketStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "RTH open (ET HHmmss)", Description = "RTH session open.", GroupName = "1. Sessions & data", Order = 2)]
        public int MarketOpenTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Earliest entry (ET HHmmss)", Description = "Completed signals evaluated from 09:35.", GroupName = "1. Sessions & data", Order = 3)]
        public int EntryStartTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Entry cutoff (ET HHmmss)", Description = "No new orders after cutoff.", GroupName = "1. Sessions & data", Order = 4)]
        public int EntryEndTime { get; set; }

        [NinjaScriptProperty]
        [Range(0, 235959)]
        [Display(Name = "Force-flat time (ET HHmmss)", Description = "Close active strategy positions at 16:00 ET.", GroupName = "1. Sessions & data", Order = 5)]
        public int FlattenTime { get; set; }

        [NinjaScriptProperty]
        [Range(1, 186)]
        [Display(Name = "Minimum overnight 5m bars", Description = "Avoid acting on severely incomplete overnight data.", GroupName = "1. Sessions & data", Order = 6)]
        public int MinimumOvernightFiveMinuteBars { get; set; }

        [NinjaScriptProperty]
        [Range(1, 78)]
        [Display(Name = "Minimum premarket 5m bars", Description = "Reject seriously incomplete premarket ranges.", GroupName = "1. Sessions & data", Order = 7)]
        public int MinimumPremarketFiveMinuteBars { get; set; }

        #endregion

        #region 2. Regime & indicators
        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "5m fast EMA", Description = "Completed context only.", GroupName = "2. Regime & indicators", Order = 0)]
        public int FastEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "5m slow EMA", Description = "Completed context only.", GroupName = "2. Regime & indicators", Order = 1)]
        public int SlowEmaPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "5m ATR period", Description = "Diagnostic context.", GroupName = "2. Regime & indicators", Order = 2)]
        public int AtrPeriod { get; set; }

        [NinjaScriptProperty]
        [Range(5, 100)]
        [Display(Name = "Prior premarket sessions", Description = "Previous completed PM sessions only; contract starts require warm-up.", GroupName = "2. Regime & indicators", Order = 3)]
        public int CompressionLookbackSessions { get; set; }

        [NinjaScriptProperty]
        [Range(1, 99)]
        [Display(Name = "Compression percentile", Description = "Nearest-rank percentile of preceding premarket widths.", GroupName = "2. Regime & indicators", Order = 4)]
        public int CompressionPercentile { get; set; }

        #endregion

        #region 3. ONL breakout short
        [NinjaScriptProperty]
        [Display(Name = "Enable ONL breakdown", Description = "First break below overnight low, conditional on relative PM compression.", GroupName = "3. ONL breakout short", Order = 0)]
        public bool EnableOnlBreakdown { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "ONL maximum minutes after open", Description = "Maximum signal time.", GroupName = "3. ONL breakout short", Order = 1)]
        public int OnlMaximumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require bearish opening direction", Description = "Signal close below RTH open and previous completed minute close.", GroupName = "3. ONL breakout short", Order = 2)]
        public bool RequireOnlOpeningMomentum { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ONL stop ticks", Description = "Fixed stop, fill-relative.", GroupName = "3. ONL breakout short", Order = 3)]
        public int OnlStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ONL target ticks", Description = "Fixed target, fill-relative.", GroupName = "3. ONL breakout short", Order = 4)]
        public int OnlTargetTicks { get; set; }

        #endregion

        #region 4. ONH breakout long
        [NinjaScriptProperty]
        [Display(Name = "Enable ONH breakout", Description = "First strong completed bullish close above overnight high.", GroupName = "4. ONH breakout long", Order = 0)]
        public bool EnableOnhBreakout { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "ONH maximum minutes after open", Description = "Maximum signal time.", GroupName = "4. ONH breakout long", Order = 1)]
        public int OnhMaximumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ONH minimum bullish body (ticks)", Description = "Minimum candle body on first ONH crossing.", GroupName = "4. ONH breakout long", Order = 2)]
        public int OnhMinBodyTicks { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Require bullish opening direction", Description = "Signal close above recorded RTH open.", GroupName = "4. ONH breakout long", Order = 3)]
        public bool RequireOnhOpeningDirection { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "ONH stop ticks", Description = "Fixed stop, fill-relative.", GroupName = "4. ONH breakout long", Order = 4)]
        public int OnhStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "ONH target ticks", Description = "Fixed target, fill-relative.", GroupName = "4. ONH breakout long", Order = 5)]
        public int OnhTargetTicks { get; set; }

        #endregion

        #region 5. PDC reclaim long
        [NinjaScriptProperty]
        [Display(Name = "Enable PDC trend reclaim", Description = "Trend-conditioned bullish cross back above the prior RTH close.", GroupName = "5. PDC reclaim long", Order = 0)]
        public bool EnablePdcReclaim { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "PDC maximum minutes after open", Description = "Maximum signal time.", GroupName = "5. PDC reclaim long", Order = 1)]
        public int PdcMaximumMinutesFromOpen { get; set; }

        [NinjaScriptProperty]
        [Range(0, 300)]
        [Display(Name = "PDC maximum 1m range ticks", Description = "No entries off extremely broad bars.", GroupName = "5. PDC reclaim long", Order = 2)]
        public double PdcMaximumSignalRangeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(-100, 100)]
        [Display(Name = "PDC min slow EMA slope ticks", Description = "Completed 5m EMA(21) slope, at least zero by default.", GroupName = "5. PDC reclaim long", Order = 3)]
        public double PdcMinimumSlowEmaSlopeTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 200)]
        [Display(Name = "PDC stop ticks", Description = "Fixed stop, fill-relative.", GroupName = "5. PDC reclaim long", Order = 4)]
        public int PdcStopTicks { get; set; }

        [NinjaScriptProperty]
        [Range(1, 500)]
        [Display(Name = "PDC target ticks", Description = "Fixed target, fill-relative.", GroupName = "5. PDC reclaim long", Order = 5)]
        public int PdcTargetTicks { get; set; }

        #endregion

        #region 6. Portfolio execution
        [NinjaScriptProperty]
        [Range(1, 100)]
        [Display(Name = "ES order quantity", Description = "1 ES baseline; experimental executions only.", GroupName = "6. Portfolio execution", Order = 0)]
        public int OrderQuantity { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum trades per day", Description = "0 disables cap.", GroupName = "6. Portfolio execution", Order = 1)]
        public int MaximumTradesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum winners per day", Description = "0 disables cap.", GroupName = "6. Portfolio execution", Order = 2)]
        public int MaximumWinnersPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 100)]
        [Display(Name = "Maximum losses per day", Description = "0 disables cap.", GroupName = "6. Portfolio execution", Order = 3)]
        public int MaximumLossesPerDay { get; set; }

        [NinjaScriptProperty]
        [Range(0, 390)]
        [Display(Name = "Maximum hold minutes", Description = "Time exits on execution series; 0 disables.", GroupName = "6. Portfolio execution", Order = 4)]
        public int MaximumHoldMinutes { get; set; }

        #endregion

        #region 7. Research & safety
        [NinjaScriptProperty]
        [Display(Name = "Allow Playback orders only", Description = "Never submits to regular live accounts; Playback account/connection required.", GroupName = "7. Research & safety", Order = 0)]
        public bool EnablePlaybackOrders { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Allow historical simulated orders", Description = "Opt in to historical tick-series Analyzer simulation. Default OFF.", GroupName = "7. Research & safety", Order = 1)]
        public bool EnableHistoricalBacktestOrders { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Export research CSV files", Description = "Capture bars, context, candidates, trades, sessions and equity.", GroupName = "7. Research & safety", Order = 2)]
        public bool EnableResearchExport { get; set; }

        [NinjaScriptProperty]
        [Display(Name = "Enable daily file diagnostics", Description = "Daily file logs include version and trading date.", GroupName = "7. Research & safety", Order = 3)]
        public bool EnableDiagnosticLogging { get; set; }

        #endregion

    }
}
