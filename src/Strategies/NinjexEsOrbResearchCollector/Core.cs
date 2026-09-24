// Ninjex ES ORB Research Collector 1.0.0
// Baseline: NinjexEsMarketResearchCollector 1.2.0 / context-asof-1.
// Independent, observation-only collector. Never submits orders.
// See ORB_RESEARCH_GUIDE.md for data contracts and installation.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
#if !ORB_CORE_TEST
using System.ComponentModel.DataAnnotations;
using NinjaTrader.Data;
#endif

namespace NinjaTrader.NinjaScript.Strategies
{
#if !ORB_CORE_TEST
    public class NinjexEsOrbResearchCollector : Strategy
    {
        private NinjexEsOrbResearchEngine engine;
        private int lastTickBar = -1;
        protected override void OnStateChange()
        {
            if (State == State.SetDefaults)
            {
                Name = "Ninjex ES ORB Research Collector";
                Description = "Observation-only ORB research, 09:30 through 11:30 Eastern. No orders.";
                Calculate = Calculate.OnEachTick;
                IsOverlay = false;
                IsExitOnSessionCloseStrategy = false;
                BarsRequiredToTrade = 0;
                IsInstantiatedOnEachOptimizationIteration = true;
                OutputFolderName = "NinjexData";
                FirstResearchDate = new DateTime(2000, 1, 1);
                LastResearchDate = new DateTime(2099, 12, 31);
                ExportRawTicks = true;
                MaxEventsPerFamilyDirection = 3;
                RetestToleranceTicks = 1;
                MaxRetestMinutes = 15;
                SuspiciousTickGapSeconds = 30;
                MaxEntryDelaySeconds = 5;
                RelativeVolumeDays = 20;
                MinimumRelativeVolumeDays = 5;
                EnableDiagnostics = true;
            }
            else if (State == State.Configure)
            {
                // All research clocks and bars are derived from this ONE ordered stream.
                // On historical data, each 1-tick bar still supplies one Last observation.
                AddDataSeries(BarsPeriodType.Tick, 1);
            }
            else if (State == State.DataLoaded)
            {
                if (BarsPeriod.BarsPeriodType != BarsPeriodType.Minute || BarsPeriod.Value != 5)
                    throw new InvalidOperationException("Use an ES 5-minute primary chart, CME US Index Futures ETH, platform clock Eastern.");
                if (Instrument.MasterInstrument.Name != "ES")
                    throw new InvalidOperationException("This collector is intended for individual ES contracts.");
                var zone = Core.Globals.GeneralOptions.TimeZoneInfo.Id;
                if (zone != "Eastern Standard Time" && zone != "America/New_York" && zone != "US/Eastern")
                    throw new InvalidOperationException("Set NinjaTrader Tools > Options > General > Time zone to Eastern. Current: " + zone);
                lastTickBar = -1;
                var config = new NinjexEsOrbResearchEngine.Config {
                    Root = Path.Combine(Core.Globals.UserDataDir, OutputFolderName),
                    Instrument = Instrument.FullName, TickSize = TickSize,
                    PlatformZone = zone, TradingHours = Bars.TradingHours.Name,
                    TickTradingHours = BarsArray[1].TradingHours.Name, TickReplay = Bars.IsTickReplay,
                    PointValue = Instrument.MasterInstrument.PointValue,
                    FirstDate = FirstResearchDate.Date, LastDate = LastResearchDate.Date,
                    RawTicks = ExportRawTicks, MaxEvents = MaxEventsPerFamilyDirection,
                    RetestTolerance = RetestToleranceTicks, RetestMinutes = MaxRetestMinutes,
                    GapSeconds = SuspiciousTickGapSeconds, EntryDelaySeconds = MaxEntryDelaySeconds,
                    VolumeDays = RelativeVolumeDays, MinimumVolumeDays = MinimumRelativeVolumeDays
                };
                engine = new NinjexEsOrbResearchEngine(config, s => { if (EnableDiagnostics) Print(Name + " | " + s); });
                Print(Name + " | Output=" + engine.DirectoryPath + " | Platform timestamps MUST be Eastern (EST/EDT). No conversion.");
            }
            else if (State == State.Terminated && engine != null)
            {
                try { engine.Finish("StrategyTerminated"); }
                finally { engine.Dispose(); engine = null; }
            }
        }
        protected override void OnBarUpdate()
        {
            if (engine == null || BarsInProgress != 1 || CurrentBars[1] < 0) return;
            // Preserve distinct prints at the same timestamp. Deduplicate callbacks only.
            if (CurrentBars[1] == lastTickBar) return;
            lastTickBar = CurrentBars[1];
            engine.Accept(Times[1][0], Closes[1][0], Volumes[1][0], State.ToString());
        }
        [NinjaScriptProperty, Display(Name="Output folder under NinjaTrader user directory", GroupName="1. Export", Order=0)]
        public string OutputFolderName { get; set; }
        [NinjaScriptProperty, Display(Name="First research date (Eastern)", GroupName="1. Export", Order=1)]
        public DateTime FirstResearchDate { get; set; }
        [NinjaScriptProperty, Display(Name="Last research date (Eastern)", GroupName="1. Export", Order=2)]
        public DateTime LastResearchDate { get; set; }
        [NinjaScriptProperty, Display(Name="Export compressed raw Last ticks", GroupName="1. Export", Order=3)]
        public bool ExportRawTicks { get; set; }
        [NinjaScriptProperty, Range(1,20), Display(Name="Max candidates per OR / model / side / buffer", GroupName="2. Research", Order=0)]
        public int MaxEventsPerFamilyDirection { get; set; }
        [NinjaScriptProperty, Range(0,8), Display(Name="Retest tolerance (ticks)", GroupName="2. Research", Order=1)]
        public int RetestToleranceTicks { get; set; }
        [NinjaScriptProperty, Range(1,30), Display(Name="Retest expiry (minutes)", GroupName="2. Research", Order=2)]
        public int MaxRetestMinutes { get; set; }
        [NinjaScriptProperty, Range(1,600), Display(Name="Suspicious no-tick interval (seconds)", GroupName="3. Quality", Order=0)]
        public int SuspiciousTickGapSeconds { get; set; }
        [NinjaScriptProperty, Range(1,60), Display(Name="Maximum signal-to-entry delay (seconds)", GroupName="3. Quality", Order=1)]
        public int MaxEntryDelaySeconds { get; set; }
        [NinjaScriptProperty, Range(5,60), Display(Name="Prior complete sessions for RVOL", GroupName="3. Quality", Order=2)]
        public int RelativeVolumeDays { get; set; }
        [NinjaScriptProperty, Range(1,60), Display(Name="Minimum prior RVOL sessions", GroupName="3. Quality", Order=3)]
        public int MinimumRelativeVolumeDays { get; set; }
        [NinjaScriptProperty, Display(Name="Enable diagnostics", GroupName="3. Quality", Order=4)]
        public bool EnableDiagnostics { get; set; }
    }
#endif

    // Pure stream processor: no platform-specific APIs or order interfaces.
    internal sealed class NinjexEsOrbResearchEngine : IDisposable
    {
        internal sealed class Config
        {
            internal string Root, Instrument;
            internal string PlatformZone="SyntheticFixture", TradingHours="SyntheticFixture", TickTradingHours="SyntheticFixture";
            internal bool TickReplay=false;
            internal double TickSize = .25, PointValue = 50;
            internal DateTime FirstDate = new DateTime(2000,1,1), LastDate = new DateTime(2099,12,31);
            internal bool RawTicks = true;
            internal int MaxEvents = 3, RetestTolerance = 1, RetestMinutes = 15;
            internal int GapSeconds = 30, EntryDelaySeconds = 5, VolumeDays = 20, MinimumVolumeDays = 5;
        }
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static readonly int[] OrMinutes = { 1,3,5,10,15,30 };
        private static readonly int[] Buffers = { 0,1,2,4 };
        private static readonly int[] FixedStops = { 8,12,16,20,24,32,40 };
        private static readonly double[] Rewards = { 1,1.5,2,3 };
        private static readonly int[] Horizons = { 5,10,15,30,60 };
        private readonly Config cfg;
        private readonly Action<string> log;
        private readonly List<Table> tables = new List<Table>();
        private readonly Table minutes, ranges, candidates, paths, barriers, sessions, quality, manifest;
        private Table raw;
        private readonly Tech one = new Tech(), five = new Tech();
        private readonly Queue<double>[] volumeHistory = Enumerable.Range(0,120).Select(i => new Queue<double>()).ToArray();
        private readonly Queue<double>[] cumulativeHistory = Enumerable.Range(0,120).Select(i => new Queue<double>()).ToArray();
        private Bar minute, fiveBar;
        private Day background;
        private Stats priorRth;
        private DateTime priorRthDate;
        private Session session;
        private DateTime lastTime = DateTime.MinValue;
        private double lastPrice = double.NaN;
        private long seq, candidateId;
        private bool finished;
        internal string DirectoryPath { get; private set; }
        private const string Features = "SignalLastCompletedMinuteEnd,SignalMinuteOpen,SignalMinuteHigh,SignalMinuteLow,SignalMinuteClose,SignalMinuteVolume,SignalMinuteBodyTicks,SignalMinuteUpperWickTicks,SignalMinuteLowerWickTicks,SignalMinuteComplete,SignalMinuteRvol,SignalRvolPriorSessions,SignalVwap,SignalVwapSd,SignalCumulativeVolume,SignalDistanceVwapTicks,SignalAtr1mTicks,SignalEma9_1m,SignalEma21_1m,SignalEma14_1m,SignalEma50_1m,Signal1mIndicatorReady,Signal5mAsOf,Signal5mAgeMinutes,SignalAtr5mTicks,SignalAdx5m,SignalEma9_5m,SignalEma21_5m,SignalEma14_5m,SignalEma50_5m,SignalEma9Slope5mTicks,Signal5mIndicatorReady,SignalRthOpen,SignalOpenDelayMilliseconds,SignalOvernightHigh,SignalOvernightLow,SignalOvernightComplete,SignalPremarketHigh,SignalPremarketLow,SignalPremarketComplete,SignalPriorRthDate,SignalPriorRthHigh,SignalPriorRthLow,SignalPriorRthClose,SignalPriorRthComplete,SignalGapFromPriorCloseTicks,SignalDataSuspect";
        internal NinjexEsOrbResearchEngine(Config config, Action<string> logger)
        {
            cfg = config; log = logger ?? (s => {});
            if (cfg.TickSize <= 0 || cfg.PointValue <= 0 || cfg.FirstDate > cfg.LastDate || cfg.MinimumVolumeDays > cfg.VolumeDays)
                throw new ArgumentException("Invalid tick size, date range, or relative-volume history settings.");
            string run = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", Inv) + "_" + Guid.NewGuid().ToString("N").Substring(0,8);
            DirectoryPath = Path.Combine(cfg.Root, "es_orb_research_" + Safe(cfg.Instrument) + "_" + run);
            Directory.CreateDirectory(DirectoryPath);
            manifest = NewTable("manifest.csv", "Key,Value");
            minutes = NewTable("minutes.csv", "TradingDate,Start,End,FirstTick,LastTick,FirstSeq,LastSeq,Complete,TickCount,Open,High,Low,Close,Volume,TickVwap,TickPriceSd,UpTickVolume,DownTickVolume,UnchangedTickVolume,TickRuleSignedVolume,PricePathTicks,MaxInterTickMilliseconds,BodyTicks,UpperWickTicks,LowerWickTicks,CloseLocation,MinuteRvol,CumulativeRvol,RvolPriorSessions,RthCumulativeVolume,RthVwap,RthVwapSd,Atr1mTicks,Ema9_1m,Ema21_1m,Ema14_1m,Ema50_1m,Indicator1mReady,Context5mAsOf,Context5mAgeMinutes,Atr5mTicks,Adx5m,Ema9_5m,Ema21_5m,Ema14_5m,Ema50_5m,Indicator5mReady,MinutesFromOpen,WithinFirst90,DataSuspect");
            ranges = NewTable("ranges.csv", "TradingDate,ORMinutes,Start,End,FrozenObservedAt,Valid,ObservedMinutes,CompleteMinutes,Open,High,Low,Close,WidthTicks,Volume,Vwap,Efficiency,BodyTicks,OpenLocation,CloseLocation,StartDelayMilliseconds");
            candidates = NewTable("candidates.csv", "CandidateId,TradingDate,Model,Direction,ORMinutes,BufferTicks,Attempt,SignalTime,DecisionObservedAt,SignalSeq,SignalPrice,OriginBreakoutTime,SignalMinutesFromOpen,SignalEligible90,ORHigh,ORLow,ORWidthTicks,ORVolume,TriggerPrice,Status,EntryTime,EntrySeq,EntryLastProxy,EntryEligible90,EntryDelayMilliseconds,EntryMinusSignalSignedTicks," + Features);
            paths = NewTable("paths.csv", "CandidateId,Horizon,PlannedCutoff,ObservedThrough,LastTickTime,LastSeq,Complete,Reason,EntryLastProxy,LastPrice,ReturnTicks,MfeTicks,MaeTicks,MfeTime,MfeSeq,MaeTime,MaeSeq,FirstReturnInsideTime,FirstReturnInsideSeq,FirstOppositeBoundaryTime,FirstOppositeBoundarySeq");
            barriers = NewTable("barriers.csv", "CandidateId,StopModel,StopTicks,TargetTicks,RequestedR,StopPrice,TargetPrice,RiskUsdOneES,FirstHit,FirstStopTime,FirstStopSeq,FirstStopObservedPrice,FirstTargetTime,FirstTargetSeq,FirstTargetObservedPrice,ObservedThrough,LastTickTime,ObservationComplete,TerminationReason");
            sessions = NewTable("sessions.csv", "TradingDate,FirstTick,LastTick,WindowEnd,TickCount,ObservedMinutes,CompleteMinutes,ExpectedMinutes,WindowComplete,SuspiciousGapCount,OutOfOrderTicks,InvalidTicks,CandidateRows,EnteredCandidates,CandidatesSuppressedByCap,RthOpen,OpenDelayMilliseconds,WindowHigh,WindowLow,WindowLast,WindowVolume,WindowVwap,OvernightHigh,OvernightLow,OvernightVolume,OvernightObservedMinutes,OvernightComplete,PremarketHigh,PremarketLow,PremarketVolume,PremarketObservedMinutes,PremarketComplete,PriorRthDate,PriorRthHigh,PriorRthLow,PriorRthClose,PriorRthComplete,RawTicksFile,FinalizeReason");
            quality = NewTable("quality.csv", "Time,Seq,Code,Detail");
            WriteManifest(run);
        }
        private Table NewTable(string name, string header)
        {
            var t = new Table(Path.Combine(DirectoryPath,name), header, false); tables.Add(t); return t;
        }
        private void WriteManifest(string run)
        {
            manifest.Write("Collector", "NinjexEsOrbResearchCollector"); manifest.Write("Version", "1.0.0");
            manifest.Write("Schema", "orb-research-1; not the overnight Research Explorer schema");
            manifest.Write("Baseline", "NinjexEsMarketResearchCollector 1.2.0 context-asof-1; independent stream engine");
            manifest.Write("RunId",run); manifest.Write("Instrument",cfg.Instrument);
            manifest.Write("PlatformTimeZoneId",cfg.PlatformZone);manifest.Write("PrimaryTradingHours",cfg.TradingHours);
            manifest.Write("TickTradingHours",cfg.TickTradingHours);manifest.Write("PrimaryTickReplay",cfg.TickReplay);
            manifest.Write("TickSize",cfg.TickSize); manifest.Write("PointValue",cfg.PointValue);
            manifest.Write("Clock", "NinjaTrader platform MUST use US Eastern EST/EDT; no timezone conversion");
            manifest.Write("Window", "[09:30:00,11:30:00); end-boundary tick never enters prices, ranges or outcomes");
            manifest.Write("MinuteBuckets", "Start-inclusive/end-exclusive; end stamps 09:31 through 11:30 inclusive");
            manifest.Write("Source", "BIP1 Last 1-tick bars; primary must be ES5min ETH; all study bars constructed from ticks");
            manifest.Write("OutsideWindow", "Background ETH indicator/key-level warmup only; no raw tick or minute market-data exports outside window");
            manifest.Write("OpeningRangesMinutes",string.Join(";",OrMinutes));
            manifest.Write("BreakoutBuffersTicks",string.Join(";",Buffers));
            manifest.Write("Models", "TouchBreakout (0 buffer means touch); CloseBreakout; RetestHoldClose");
            manifest.Write("EntryProxy", "Last price: next sequence for touch; first available tick after completed bar for close/retest; no orders or fill claims");
            manifest.Write("Barriers", "Observed Last tick ordering; target touch is NOT a guaranteed limit fill; stops may gap beyond their price");
            manifest.Write("FixedStopTicks",string.Join(";",FixedStops));
            manifest.Write("OtherStops", "ORWidth0.25/0.5/1; oppositeOR+1tick; signalBarExtreme+1tick; ATR1m1x; ATR5m0.5x");
            manifest.Write("RewardMultiples",string.Join(";",Rewards.Select(x=>F(x))));
            manifest.Write("HorizonsMinutes",string.Join(";",Horizons));
            manifest.Write("HardCutoffs", "11:00 for 90-minute studies; 11:30 for 120-minute studies; half-open paths, last-before-cutoff mark");
            manifest.Write("HorizonConvention", "Entry+horizon, clipped at 11:30; clipped horizons marked incomplete/censored");
            manifest.Write("Indicators", "Tick-built 1m/5m; EMA9/21 and EMA14/50 exported together; seeded at first close, alpha=2/(n+1); ATR14 Wilder; ADX14 Wilder");
            manifest.Write("IndicatorReady", "At least 50 completed bars; incomplete bars do not advance indicators; as-of timestamp and age exported");
            manifest.Write("RVOL", "Same clock minute and cumulative-through-minute vs prior COMPLETE 120-minute sessions only; prior days="+cfg.VolumeDays+"; minimum="+cfg.MinimumVolumeDays);
            manifest.Write("PreOpenWindows", "Overnight [prior18:00,09:30), premarket [03:00,09:30); prior RTH [09:30,16:00)");
            manifest.Write("Coverage", "Complete means observed boundary witnesses/120 minute coverage/no flagged intervals; does NOT certify every vendor tick was delivered");
            manifest.Write("TickRuleVolume", "Up/down/unchanged vs previous print; NOT aggressor delta. No synthetic bid/ask or spread.");
            manifest.Write("GapSeconds",cfg.GapSeconds);manifest.Write("MaxEntryDelaySeconds",cfg.EntryDelaySeconds);
            manifest.Write("MaxEventsPerFamilyDirection",cfg.MaxEvents);manifest.Write("RetestToleranceTicks",cfg.RetestTolerance);
            manifest.Write("RetestExpiryMinutes",cfg.RetestMinutes);manifest.Write("RawTicks",cfg.RawTicks);
            manifest.Write("FirstResearchDate",cfg.FirstDate.ToString("yyyy-MM-dd"));manifest.Write("LastResearchDate",cfg.LastDate.ToString("yyyy-MM-dd"));
            manifest.Write("Causality", "Freeze OR only after its interval; signal features captured before entry; completed context only; same timestamp ordered by Seq");
            manifest.Write("NoTradeSimulation", "Overlapping candidates; no position state, commissions, daily caps, payout rules or portfolio PnL. Use raw ticks for causal strategy simulation.");
            manifest.Flush();
        }
        internal void Accept(DateTime time, double price, double volume, string sourceState)
        {
            if (finished) throw new InvalidOperationException("Collector already finalized.");
            seq++;
            if (!Finite(price) || price <= 0 || !Finite(volume) || volume < 0)
            {
                quality.Write(time,seq,"InvalidTick","Nonfinite/nonpositive price or invalid volume");
                if (minute!=null) minute.HasGap=true; if(fiveBar!=null) fiveBar.HasGap=true;
                if (session != null && !session.Finished) { session.InvalidTicks++; session.Suspect=true; CensorPaths("InvalidTick"); }
                return;
            }
            if (lastTime != DateTime.MinValue && time < lastTime)
            {
                quality.Write(time,seq,"OutOfOrderTick","Rejected; prior accepted="+Stamp(lastTime));
                if (minute!=null) minute.HasGap=true; if(fiveBar!=null) fiveBar.HasGap=true;
                if (session != null && !session.Finished) { session.OutOfOrder++;session.Suspect=true;CensorPaths("OutOfOrderTick"); }
                return;
            }
            bool gap = lastTime != DateTime.MinValue && (time-lastTime).TotalSeconds > cfg.GapSeconds;
            bool missingMinute = lastTime != DateTime.MinValue && Floor(time,1)>Floor(lastTime,1).AddMinutes(1);
            // Censor BEFORE closing bars or maturing horizons across a suspect interval.
            if ((gap || missingMinute) && session != null && !session.Finished && lastTime >= session.OpenTime && lastTime < session.EndTime)
            {
                session.Gaps++;session.Suspect=true;
                string gapReason=gap?"SuspiciousNoTickInterval":"MissingMinuteBuckets";
                quality.Write(time,seq,gapReason,"Last="+Stamp(lastTime)+"; seconds="+F((time-lastTime).TotalSeconds));
                CensorPaths(gapReason);
            }
            // Complete bars by END timestamp. At equal ends, 5m goes first.
            // Across gaps, a later 5m end must never leak into an earlier 1m signal.
            if (minute != null && fiveBar != null && time>=fiveBar.End && minute.End<fiveBar.End)
            {
                minute.Seal(time,cfg.GapSeconds); CompleteMinute(minute,time); minute=null;
            }
            if (fiveBar != null && time >= fiveBar.End)
            {
                fiveBar.Seal(time,cfg.GapSeconds);
                if (fiveBar.Complete) five.Add(fiveBar);
                fiveBar=null;
            }
            if (minute != null && time >= minute.End)
            {
                minute.Seal(time,cfg.GapSeconds); CompleteMinute(minute,time); minute=null;
            }
            if (session != null && !session.Finished && time >= session.EndTime)
                FinishSession("WindowEnd",session.EndTime);
            DateTime tradeDate = time.TimeOfDay >= TimeSpan.FromHours(18) ? time.Date.AddDays(1) : time.Date;
            if (background == null || background.Date != tradeDate)
            {
                if (session != null && !session.Finished) FinishSession("NewTradingDate",lastTime);
                if (background != null && background.Rth.Count > 0) { priorRth=background.Rth.Copy();priorRthDate=background.Date; }
                background = new Day(tradeDate);
            }
            background.Update(time,price,volume);
            bool inWindow = time >= tradeDate.AddHours(9.5) && time < tradeDate.AddHours(11.5);
            if (inWindow && tradeDate >= cfg.FirstDate && tradeDate <= cfg.LastDate)
            {
                if (session == null || session.Date != tradeDate)
                    StartSession(tradeDate,time);
                // A late/out-of-order replay segment cannot reopen an already finalized session.
                if (!session.Finished)
                {
                    FreezeRanges(time);
                    if (raw != null) raw.Write(time,seq,price,volume,sourceState);
                    // Mature snapshots before consuming ticks AT their cutoff.
                    foreach (var path in session.Active) AdvancePath(path,time,price);
                    EnterPending(time,price);
                    session.Window.Add(price,volume);session.LastTick=time;session.Ticks++;
                    ScanTouchBreakouts(time,price);
                }
            }
            if (minute == null) minute=new Bar(Floor(time,1),1,time,lastTime,cfg.GapSeconds);
            if (fiveBar == null) fiveBar=new Bar(Floor(time,5),5,time,lastTime,cfg.GapSeconds);
            minute.Add(time,price,volume,seq,lastTime,lastPrice,gap,cfg.TickSize);
            fiveBar.Add(time,price,volume,seq,lastTime,lastPrice,gap,cfg.TickSize);
            lastTime=time;lastPrice=price;
        }
        private void StartSession(DateTime date, DateTime first)
        {
            session=new Session(date,first,background.On.Copy(),background.Pm.Copy(),priorRth==null?null:priorRth.Copy(),priorRthDate);
            if (cfg.RawTicks)
            {
                session.RawFile="ticks_"+date.ToString("yyyyMMdd")+".csv.gz";
                raw=new Table(Path.Combine(DirectoryPath,session.RawFile),"Time,Seq,Last,Volume,SourceState",true);
            }
            if ((first-session.OpenTime).TotalSeconds > cfg.GapSeconds)
            { session.Suspect=true;quality.Write(first,seq,"LateWindowStart","SecondsAfterOpen="+F((first-session.OpenTime).TotalSeconds)); }
            log("SESSION "+date.ToString("yyyy-MM-dd")+" first="+Stamp(first));
        }
        private void CompleteMinute(Bar b, DateTime observedAt)
        {
            if (b.Complete) one.Add(b);
            if (background != null) background.AddMinute(b);
            if (session==null || session.Finished || b.Start<session.OpenTime || b.End>session.EndTime) return;
            session.MinuteCount++;
            if (b.Complete) session.CompleteMinutes++;
            else { session.Suspect=true;quality.Write(b.End,seq,"IncompleteMinute",Stamp(b.Start)); }
            foreach (var r in session.Ranges) if (b.Start>=r.Start && b.End<=r.End) r.Add(b);
            int idx=(int)(b.Start-session.OpenTime).TotalMinutes;
            session.MinuteVolumes[idx]=b.Volume;
            session.CumulativeVolumes[idx]=session.Window.Volume;
            int historyCount=volumeHistory[idx].Count;
            double rv=Relative(b.Volume,volumeHistory[idx]), crv=Relative(session.Window.Volume,cumulativeHistory[idx]);
            session.LastMinute=b;session.LastRvol=rv;session.LastRvolCount=historyCount;
            minutes.Write(session.Date,b.Start,b.End,b.First,b.Last,b.FirstSeq,b.LastSeq,b.Complete,b.Count,b.Open,b.High,b.Low,b.Close,b.Volume,b.Vwap,b.Sd,
                b.UpVolume,b.DownVolume,b.SameVolume,b.UpVolume-b.DownVolume,b.PathTicks,b.MaxGapMs,
                (b.Close-b.Open)/cfg.TickSize,(b.High-Math.Max(b.Open,b.Close))/cfg.TickSize,(Math.Min(b.Open,b.Close)-b.Low)/cfg.TickSize,
                Location(b.Close,b.Low,b.High),rv,crv,historyCount,session.Window.Volume,session.Window.Vwap,session.Window.Sd,
                one.Atr/cfg.TickSize,one.E9,one.E21,one.E14,one.E50,one.Ready,five.AsOf,Age(b.End,five.AsOf),five.Atr/cfg.TickSize,five.Adx,
                five.E9,five.E21,five.E14,five.E50,five.Ready,(int)(b.End-session.OpenTime).TotalMinutes,b.End<=session.Ninety,session.Suspect);
            if (b.Complete && b.End<session.EndTime) ScanCloseBreakouts(b,observedAt);
            if (idx%5==4) Flush();
        }
        private void FreezeRanges(DateTime time)
        {
            foreach (var r in session.Ranges)
            {
                if (r.Frozen || time<r.End) continue;
                r.Frozen=true;r.Valid=r.Count==r.Minutes && r.CompleteCount==r.Minutes && r.High>r.Low;
                ranges.Write(session.Date,r.Minutes,r.Start,r.End,time,r.Valid,r.Count,r.CompleteCount,r.Open,r.High,r.Low,r.Close,
                    r.Width/cfg.TickSize,r.Volume,r.Vwap,r.Travel>0?Math.Abs(r.Close-r.Open)/r.Travel:double.NaN,
                    (r.Close-r.Open)/cfg.TickSize,Location(r.Open,r.Low,r.High),Location(r.Close,r.Low,r.High),(session.FirstTick-r.Start).TotalMilliseconds);
            }
        }
        private void ScanTouchBreakouts(DateTime t,double p)
        {
            foreach (var r in session.Ranges.Where(x=>x.Valid))
                foreach (var s in r.Sides)
                {
                    double level=s.Direction>0 ? r.High+s.Buffer*cfg.TickSize : r.Low-s.Buffer*cfg.TickSize;
                    bool outside=s.Direction*(p-level)>=-cfg.TickSize*1e-6;
                    if (outside && !s.Outside)
                        QueueCandidate(r,s,"TouchBreakout",t,t,p,level,seq+1);
                    s.Outside=outside;
                }
        }
        private void ScanCloseBreakouts(Bar b,DateTime seen)
        {
            foreach (var r in session.Ranges.Where(x=>x.Valid && b.Start>=x.End))
                foreach (var s in r.Sides)
                {
                    double level=s.Direction>0?r.High+s.Buffer*cfg.TickSize:r.Low-s.Buffer*cfg.TickSize;
                    double signed=s.Direction*(b.Close-level);
                    bool outside=s.Buffer==0 ? signed>cfg.TickSize*1e-6 : signed>=-cfg.TickSize*1e-6;
                    if (s.RetestArmed && b.End>s.BreakTime)
                    {
                        if ((b.End-s.BreakTime).TotalMinutes>cfg.RetestMinutes) s.RetestArmed=false;
                        else
                        {
                            bool touches=b.Low<=level+cfg.RetestTolerance*cfg.TickSize && b.High>=level-cfg.RetestTolerance*cfg.TickSize;
                            if (touches) s.RetestTouched=true;
                            if (s.RetestTouched && s.Direction*(b.Close-level)>0)
                            { QueueCandidate(r,s,"RetestHoldClose",b.End,seen,b.Close,level,seq);s.RetestArmed=false; }
                            else if (s.Direction*(b.Close-level)<-cfg.RetestTolerance*cfg.TickSize) s.RetestArmed=false;
                        }
                    }
                    if (outside && !s.CloseOutside)
                    {
                        QueueCandidate(r,s,"CloseBreakout",b.End,seen,b.Close,level,seq);
                        s.RetestArmed=true;s.RetestTouched=false;s.BreakTime=b.End;
                    }
                    s.CloseOutside=outside;
                }
        }
        private void QueueCandidate(OpeningRange r,Side s,string model,DateTime signal,DateTime seen,double p,double level,long earliestSeq)
        {
            string key=model+":"+s.Direction+":"+s.Buffer;
            int count; r.Attempts.TryGetValue(key,out count);
            if (count>=cfg.MaxEvents) {session.Suppressed++;return;}
            r.Attempts[key]=++count;
            var snap=CaptureSnapshot(signal,p);
            var pending=new Pending { Id=++candidateId,Range=r,Direction=s.Direction,Buffer=s.Buffer,Model=model,Attempt=count,
                SignalTime=signal,Seen=seen,SignalSeq=seq,SignalPrice=p,OriginBreakTime=model=="RetestHoldClose"?s.BreakTime:DateTime.MinValue,Level=level,MinSeq=earliestSeq,Snapshot=snap };
            session.Pending.Add(pending);
        }
        private Snapshot CaptureSnapshot(DateTime t,double p)
        {
            var b=session.LastMinute;
            var previous=session.Prior;
            double openDelay=(session.FirstTick-session.OpenTime).TotalMilliseconds;
            var snap=new Snapshot {
                Atr1=one.Ready?one.Atr/cfg.TickSize:double.NaN,Atr5=five.Ready?five.Atr/cfg.TickSize:double.NaN,
                BarHigh=b==null?double.NaN:b.High,BarLow=b==null?double.NaN:b.Low
            };
            snap.Values=new object[] { b==null?DateTime.MinValue:b.End,b==null?double.NaN:b.Open,b==null?double.NaN:b.High,
                b==null?double.NaN:b.Low,b==null?double.NaN:b.Close,b==null?double.NaN:b.Volume,
                b==null?double.NaN:(b.Close-b.Open)/cfg.TickSize,b==null?double.NaN:(b.High-Math.Max(b.Open,b.Close))/cfg.TickSize,
                b==null?double.NaN:(Math.Min(b.Open,b.Close)-b.Low)/cfg.TickSize,b!=null&&b.Complete,session.LastRvol,session.LastRvolCount,
                session.Window.Vwap,session.Window.Sd,session.Window.Volume,(p-session.Window.Vwap)/cfg.TickSize,
                one.Atr/cfg.TickSize,one.E9,one.E21,one.E14,one.E50,one.Ready,five.AsOf,Age(t,five.AsOf),five.Atr/cfg.TickSize,five.Adx,
                five.E9,five.E21,five.E14,five.E50,five.Slope9/cfg.TickSize,five.Ready,session.Window.Open,openDelay,
                session.On.High,session.On.Low,session.On.CompleteMinutes==930,session.Pm.High,session.Pm.Low,session.Pm.CompleteMinutes==390,
                session.PriorDate,previous==null?double.NaN:previous.High,previous==null?double.NaN:previous.Low,previous==null?double.NaN:previous.Close,
                previous!=null&&previous.CompleteMinutes==390,previous==null?double.NaN:(session.Window.Open-previous.Close)/cfg.TickSize,session.Suspect };
            return snap;
        }
        private void EnterPending(DateTime t,double p)
        {
            if (session.Pending.Count==0) return;
            foreach (var q in session.Pending.ToArray())
            {
                if (seq<q.MinSeq) continue;
                session.Pending.Remove(q);
                if (t<q.SignalTime) {WriteCandidate(q,"ClockBeforeSignal",DateTime.MinValue,0,double.NaN);continue;}
                if ((t-q.SignalTime).TotalSeconds>cfg.EntryDelaySeconds)
                {WriteCandidate(q,"EntryDelayExceeded",DateTime.MinValue,0,double.NaN);continue;}
                WriteCandidate(q,"EnteredLastProxy",t,seq,p);
                var path=new Track { Candidate=q,EntryTime=t,EntrySeq=seq,Entry=p,Last=p,LastTime=t,LastSeq=seq,MfeTime=t,MaeTime=t,MfeSeq=seq,MaeSeq=seq };
                foreach (int h in Horizons)
                {
                    DateTime requested=t.AddMinutes(h);
                    path.Deadlines.Add(new Deadline { Name="M"+h,Planned=requested,Due=requested<session.EndTime?requested:session.EndTime,Clipped=requested>session.EndTime });
                }
                if (t<session.Ninety) path.Deadlines.Add(new Deadline {Name="Window90",Planned=session.Ninety,Due=session.Ninety});
                path.Deadlines.Add(new Deadline {Name="Window120",Planned=session.EndTime,Due=session.EndTime});
                path.Deadlines=path.Deadlines.OrderBy(x=>x.Due).ToList();
                AddBarriers(path);
                session.Active.Add(path);session.Entered++;
            }
        }
        private void WriteCandidate(Pending q,string status,DateTime entryTime,long entrySeq,double entry)
        {
            var r=q.Range;
            var prefix=new object[] {q.Id,session.Date,q.Model,q.Direction>0?"Long":"Short",r.Minutes,q.Buffer,q.Attempt,q.SignalTime,q.Seen,q.SignalSeq,q.SignalPrice,q.OriginBreakTime,
                (q.SignalTime-session.OpenTime).TotalMinutes,q.SignalTime<session.Ninety,r.High,r.Low,r.Width/cfg.TickSize,r.Volume,q.Level,status,
                entryTime,entrySeq==0?(object)null:entrySeq,entry,entryTime==DateTime.MinValue?(object)null:entryTime<session.Ninety,entryTime==DateTime.MinValue?double.NaN:(entryTime-q.SignalTime).TotalMilliseconds,
                q.Direction*(entry-q.SignalPrice)/cfg.TickSize};
            candidates.Write(prefix.Concat(q.Snapshot.Values).ToArray());session.CandidateRows++;
        }
        private void AddBarriers(Track p)
        {
            var q=p.Candidate;
            foreach (int stop in FixedStops) AddGeometry(p,"Fixed"+stop,stop);
            foreach (double f in new[]{.25,.5,1.0}) AddGeometry(p,"ORWidth"+F(f),Math.Ceiling(q.Range.Width/cfg.TickSize*f));
            double opposite=q.Direction>0?q.Range.Low-cfg.TickSize:q.Range.High+cfg.TickSize;
            AddGeometry(p,"OppositeORPlus1",q.Direction*(p.Entry-opposite)/cfg.TickSize);
            if (q.Model!="TouchBreakout")
            {
                double extreme=q.Direction>0?q.Snapshot.BarLow-cfg.TickSize:q.Snapshot.BarHigh+cfg.TickSize;
                AddGeometry(p,"SignalBarExtremePlus1",q.Direction*(p.Entry-extreme)/cfg.TickSize);
            }
            AddGeometry(p,"ATR1m1x",Math.Ceiling(q.Snapshot.Atr1));
            AddGeometry(p,"ATR5m0.5x",Math.Ceiling(q.Snapshot.Atr5*.5));
        }
        private void AddGeometry(Track p,string name,double stop)
        {
            if (!Finite(stop) || stop<1) return;
            stop=Math.Ceiling(stop-1e-7);
            foreach (double r in Rewards)
            {
                double target=Math.Ceiling(stop*r-1e-7);
                p.Barriers.Add(new Barrier {Model=name,StopTicks=stop,TargetTicks=target,R=r,
                    StopPrice=p.Entry-p.Candidate.Direction*stop*cfg.TickSize,TargetPrice=p.Entry+p.Candidate.Direction*target*cfg.TickSize});
            }
        }
        private void AdvancePath(Track path,DateTime time,double price)
        {
            while (path.NextDeadline<path.Deadlines.Count && time>=path.Deadlines[path.NextDeadline].Due)
            {
                var d=path.Deadlines[path.NextDeadline++];
                WritePath(path,d,d.Due,!d.Clipped,d.Clipped?"WindowEndCensored":"HorizonReached");d.Written=true;
            }
            // All paths end at 11:30. Accept() calls FinishSession before any outside-window tick.
            if (time>=session.EndTime || seq<=path.EntrySeq) return;
            path.Last=price;path.LastTime=time;path.LastSeq=seq;
            double move=path.Candidate.Direction*(price-path.Entry)/cfg.TickSize;
            bool newHigh=move>path.Mfe, newLow=-move>path.Mae;
            if (newHigh) {path.Mfe=move;path.MfeTime=time;path.MfeSeq=seq;}
            if (newLow) {path.Mae=-move;path.MaeTime=time;path.MaeSeq=seq;}
            var r=path.Candidate.Range;
            if (path.ReturnTime==DateTime.MinValue && price>r.Low && price<r.High) {path.ReturnTime=time;path.ReturnSeq=seq;}
            bool opposite=path.Candidate.Direction>0?price<=r.Low:price>=r.High;
            if (path.OppositeTime==DateTime.MinValue && opposite) {path.OppositeTime=time;path.OppositeSeq=seq;}
            if (newHigh || newLow) foreach (var b in path.Barriers)
            {
                if (b.StopTime==DateTime.MinValue && move<=-b.StopTicks+1e-6) {b.StopTime=time;b.StopSeq=seq;b.StopObserved=price;}
                if (b.TargetTime==DateTime.MinValue && move>=b.TargetTicks-1e-6) {b.TargetTime=time;b.TargetSeq=seq;b.TargetObserved=price;}
            }
        }
        private void WritePath(Track p,Deadline d,DateTime through,bool complete,string reason)
        {
            paths.Write(p.Candidate.Id,d.Name,d.Planned,through,p.LastTime,p.LastSeq,complete,reason,p.Entry,p.Last,
                p.Candidate.Direction*(p.Last-p.Entry)/cfg.TickSize,p.Mfe,p.Mae,p.MfeTime,p.MfeSeq,p.MaeTime,p.MaeSeq,
                p.ReturnTime,p.ReturnSeq==0?(object)null:p.ReturnSeq,p.OppositeTime,p.OppositeSeq==0?(object)null:p.OppositeSeq);
        }
        private void EndPath(Track p,DateTime through,string reason,bool boundaryObserved)
        {
            foreach (var d in p.Deadlines.Where(x=>!x.Written))
            {
                bool reached=boundaryObserved && through>=d.Due;
                WritePath(p,d,reached?d.Due:through,reached&&!d.Clipped,reached?(d.Clipped?"WindowEndCensored":"WindowEnd"):reason);d.Written=true;
            }
            foreach (var b in p.Barriers)
            {
                string first=b.StopSeq==0?(b.TargetSeq==0?"Neither":"Target"):(b.TargetSeq==0||b.StopSeq<b.TargetSeq?"Stop":"Target");
                barriers.Write(p.Candidate.Id,b.Model,b.StopTicks,b.TargetTicks,b.R,b.StopPrice,b.TargetPrice,b.StopTicks*cfg.TickSize*cfg.PointValue,first,
                    b.StopTime,b.StopSeq==0?(object)null:b.StopSeq,b.StopObserved,b.TargetTime,b.TargetSeq==0?(object)null:b.TargetSeq,b.TargetObserved,
                    through,p.LastTime,boundaryObserved,reason);
            }
        }
        private void CensorPaths(string reason)
        {
            if (session==null) return;
            foreach (var p in session.Active) EndPath(p,p.LastTime,reason,false);
            session.Active.Clear();
            foreach (var q in session.Pending) WriteCandidate(q,reason,DateTime.MinValue,0,double.NaN);
            session.Pending.Clear();
        }
        private void FinishSession(string reason,DateTime through)
        {
            if (session==null || session.Finished) return;
            bool atEnd=reason=="WindowEnd";
            foreach (var q in session.Pending) WriteCandidate(q,atEnd?"NoEntryBeforeWindowEnd":reason,DateTime.MinValue,0,double.NaN);
            session.Pending.Clear();
            foreach (var p in session.Active) EndPath(p,atEnd?session.EndTime:p.LastTime,reason,atEnd);
            session.Active.Clear();
            // Emit incomplete ranges as explicit invalid snapshots if termination occurs during formation.
            foreach (var r in session.Ranges.Where(x=>!x.Frozen))
            {
                ranges.Write(session.Date,r.Minutes,r.Start,r.End,through,false,r.Count,r.CompleteCount,r.Open,r.High,r.Low,r.Close,r.Width/cfg.TickSize,
                    r.Volume,r.Vwap,double.NaN,(r.Close-r.Open)/cfg.TickSize,Location(r.Open,r.Low,r.High),Location(r.Close,r.Low,r.High),
                    (session.FirstTick-session.OpenTime).TotalMilliseconds);
                r.Frozen=true;
            }
            bool complete=atEnd && session.CompleteMinutes==120 && !session.Suspect;
            var prior=session.Prior;
            sessions.Write(session.Date,session.FirstTick,session.LastTick,session.EndTime,session.Ticks,session.MinuteCount,session.CompleteMinutes,120,complete,
                session.Gaps,session.OutOfOrder,session.InvalidTicks,session.CandidateRows,session.Entered,session.Suppressed,
                session.Window.Open,(session.FirstTick-session.OpenTime).TotalMilliseconds,session.Window.High,session.Window.Low,session.Window.Close,session.Window.Volume,session.Window.Vwap,
                session.On.High,session.On.Low,session.On.Volume,session.On.Minutes,session.On.CompleteMinutes==930,
                session.Pm.High,session.Pm.Low,session.Pm.Volume,session.Pm.Minutes,session.Pm.CompleteMinutes==390,
                session.PriorDate,prior==null?double.NaN:prior.High,prior==null?double.NaN:prior.Low,prior==null?double.NaN:prior.Close,prior!=null&&prior.CompleteMinutes==390,
                session.RawFile,reason);
            if (complete)
                for (int i=0;i<120;i++) { Push(volumeHistory[i],session.MinuteVolumes[i]);Push(cumulativeHistory[i],session.CumulativeVolumes[i]); }
            session.Finished=true;
            if (raw!=null) {raw.Dispose();raw=null;}
            Flush();log("END "+session.Date.ToString("yyyy-MM-dd")+" complete="+complete+" minutes="+session.CompleteMinutes+"/120 candidates="+session.CandidateRows+" reason="+reason);
        }
        internal void Finish(string reason)
        {
            if (finished) return;
            // An unfinished last bucket is retained as partial, never promoted to a completed OR.
            if (minute!=null && session!=null && !session.Finished && minute.Start>=session.OpenTime && minute.End<=session.EndTime)
            {minute.Complete=false;CompleteMinute(minute,lastTime);minute=null;}
            FinishSession(reason,lastTime);manifest.Write("FinalStatus",reason);manifest.Write("AcceptedThrough",lastTime);manifest.Write("SequenceCount",seq);
            Flush();finished=true;
        }
        private void Flush() {foreach (var t in tables) t.Flush();if(raw!=null)raw.Flush();}
        public void Dispose()
        {
            try {if(!finished)Finish("Disposed");}
            finally {foreach(var t in tables)t.Dispose();if(raw!=null){raw.Dispose();raw=null;}}
        }
        private void Push(Queue<double> q,double v) {q.Enqueue(v);while(q.Count>cfg.VolumeDays)q.Dequeue();}
        private double Relative(double v,Queue<double> q) {return q.Count>=cfg.MinimumVolumeDays && q.Average()>0?v/q.Average():double.NaN;}
        private static double Age(DateTime t,DateTime context) {return context==DateTime.MinValue?double.NaN:(t-context).TotalMinutes;}
        private static DateTime Floor(DateTime t,int minutes) {return new DateTime(t.Year,t.Month,t.Day,t.Hour,t.Minute/minutes*minutes,0);}
        private static bool Finite(double x) {return !double.IsNaN(x)&&!double.IsInfinity(x);}
        private static double Location(double p,double low,double high) {return high>low?(p-low)/(high-low):double.NaN;}
        private static string F(double x) {return Finite(x)?x.ToString("0.########",Inv):"";}
        private static string Stamp(DateTime d) {return d==DateTime.MinValue?"":d.ToString("yyyy-MM-dd HH:mm:ss.fffffff",Inv);}
        private static string Safe(string s) {foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return s.Replace(' ','_');}

        private sealed class Table : IDisposable
        {
            private readonly StreamWriter writer;
            private readonly int columns;
            internal Table(string path,string header,bool gzip)
            {
                Stream stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read,65536);
                if(gzip)stream=new GZipStream(stream,CompressionLevel.Fastest);
                writer=new StreamWriter(stream,new UTF8Encoding(false),65536);
                columns=header.Split(',').Length;writer.WriteLine(header);
            }
            internal void Write(params object[] values)
            {
                if(values.Length!=columns)throw new InvalidOperationException("CSV column mismatch: expected "+columns+", got "+values.Length);
                writer.WriteLine(string.Join(",",values.Select(Cell)));
            }
            private static string Cell(object o)
            {
                string s=o==null?"":o is DateTime?Stamp((DateTime)o):o is double?F((double)o):o is bool?((bool)o?"1":"0"):Convert.ToString(o,Inv);
                return s.IndexOfAny(new[]{',','"','\r','\n'})>=0?"\""+s.Replace("\"","\"\"")+"\"":s;
            }
            internal void Flush(){writer.Flush();}
            public void Dispose(){writer.Dispose();}
        }
        private class Stats
        {
            internal long Count;
            internal int Minutes,CompleteMinutes;
            internal double Open=double.NaN,High=double.NaN,Low=double.NaN,Close=double.NaN,Volume,PV,P2V;
            internal double Vwap {get{return Volume>0?PV/Volume:double.NaN;}}
            internal double Sd {get{return Volume>0?Math.Sqrt(Math.Max(0,P2V/Volume-Vwap*Vwap)):double.NaN;}}
            internal void Add(double p,double v)
            {if(Count++==0){Open=High=Low=p;}High=Math.Max(High,p);Low=Math.Min(Low,p);Close=p;Volume+=v;PV+=p*v;P2V+=p*p*v;}
            internal Stats Copy(){return (Stats)MemberwiseClone();}
        }
        private sealed class Bar : Stats
        {
            internal DateTime Start,End,First,Last;
            internal long FirstSeq,LastSeq;
            internal bool LeftObserved,HasGap,Complete;
            internal double UpVolume,DownVolume,SameVolume,PathTicks,MaxGapMs;
            internal Bar(DateTime start,int duration,DateTime first,DateTime prior,int gapSeconds)
            {
                Start=start;End=start.AddMinutes(duration);First=first;
                LeftObserved=first==start || (prior!=DateTime.MinValue && prior<=start && (first-prior).TotalSeconds<=gapSeconds);
            }
            internal void Add(DateTime t,double p,double v,long sequence,DateTime priorTime,double priorPrice,bool gap,double tick)
            {
                if(Count==0)FirstSeq=sequence;
                LastSeq=sequence;Last=t;
                if(Finite(priorPrice))
                {if(p>priorPrice)UpVolume+=v;else if(p<priorPrice)DownVolume+=v;else SameVolume+=v;PathTicks+=Math.Abs(p-priorPrice)/tick;}
                if(priorTime!=DateTime.MinValue)MaxGapMs=Math.Max(MaxGapMs,(t-priorTime).TotalMilliseconds);
                if(gap && priorTime>=Start)HasGap=true;
                base.Add(p,v);
            }
            internal void Seal(DateTime witness,int gapSeconds)
            {Complete=LeftObserved&&!HasGap&&(witness-Last).TotalSeconds<=gapSeconds;}
        }
        private sealed class Day
        {
            internal DateTime Date;
            internal Stats On=new Stats(),Pm=new Stats(),Rth=new Stats();
            internal Day(DateTime d){Date=d;}
            internal void Update(DateTime t,double p,double v)
            {
                if(t>=Date.AddDays(-1).AddHours(18)&&t<Date.AddHours(9.5))On.Add(p,v);
                if(t>=Date.AddHours(3)&&t<Date.AddHours(9.5))Pm.Add(p,v);
                if(t>=Date.AddHours(9.5)&&t<Date.AddHours(16))Rth.Add(p,v);
            }
            internal void AddMinute(Bar b)
            {
                if(b.Start>=Date.AddDays(-1).AddHours(18)&&b.End<=Date.AddHours(9.5)){On.Minutes++;if(b.Complete)On.CompleteMinutes++;}
                if(b.Start>=Date.AddHours(3)&&b.End<=Date.AddHours(9.5)){Pm.Minutes++;if(b.Complete)Pm.CompleteMinutes++;}
                if(b.Start>=Date.AddHours(9.5)&&b.End<=Date.AddHours(16)){Rth.Minutes++;if(b.Complete)Rth.CompleteMinutes++;}
            }
        }
        private sealed class Session
        {
            internal DateTime Date,OpenTime,EndTime,Ninety,FirstTick,LastTick,PriorDate;
            internal Stats Window=new Stats(),On,Pm,Prior;
            internal bool Suspect,Finished;
            internal int MinuteCount,CompleteMinutes,Gaps,OutOfOrder,InvalidTicks,CandidateRows,Entered,Suppressed,LastRvolCount;
            internal long Ticks;
            internal string RawFile="";
            internal double[] MinuteVolumes=new double[120],CumulativeVolumes=new double[120];
            internal double LastRvol=double.NaN;
            internal Bar LastMinute;
            internal List<OpeningRange> Ranges=new List<OpeningRange>();
            internal List<Pending> Pending=new List<Pending>();
            internal List<Track> Active=new List<Track>();
            internal Session(DateTime d,DateTime first,Stats on,Stats pm,Stats prior,DateTime priorDate)
            {
                Date=d;OpenTime=d.AddHours(9.5);EndTime=d.AddHours(11.5);Ninety=d.AddHours(11);FirstTick=first;LastTick=first;
                On=on;Pm=pm;Prior=prior;PriorDate=priorDate;
                foreach(int m in OrMinutes)Ranges.Add(new OpeningRange(OpenTime,m));
            }
        }
        private sealed class OpeningRange
        {
            internal DateTime Start,End;
            internal int Minutes,Count,CompleteCount;
            internal bool Frozen,Valid;
            internal double Open=double.NaN,High=double.NaN,Low=double.NaN,Close=double.NaN,Volume,PV,Travel;
            internal double Width {get{return High-Low;}}
            internal double Vwap {get{return Volume>0?PV/Volume:double.NaN;}}
            internal List<Side> Sides=new List<Side>();
            internal Dictionary<string,int> Attempts=new Dictionary<string,int>();
            internal OpeningRange(DateTime start,int length)
            {Start=start;End=start.AddMinutes(length);Minutes=length;foreach(int d in new[]{1,-1})foreach(int b in Buffers)Sides.Add(new Side{Direction=d,Buffer=b});}
            internal void Add(Bar b)
            {
                if(Count++==0){Open=b.Open;High=b.High;Low=b.Low;Travel=Math.Abs(b.Close-b.Open);}
                else Travel+=Math.Abs(b.Close-Close);
                High=Math.Max(High,b.High);Low=Math.Min(Low,b.Low);Close=b.Close;Volume+=b.Volume;PV+=b.PV;
                if(b.Complete)CompleteCount++;
            }
        }
        private sealed class Side
        {internal int Direction,Buffer;internal bool Outside,CloseOutside,RetestArmed,RetestTouched;internal DateTime BreakTime;}
        private sealed class Snapshot
        {internal object[] Values;internal double Atr1,Atr5,BarHigh,BarLow;}
        private sealed class Pending
        {
            internal long Id,SignalSeq,MinSeq;internal OpeningRange Range;internal Snapshot Snapshot;
            internal int Direction,Buffer,Attempt;internal string Model;
            internal DateTime SignalTime,Seen,OriginBreakTime;internal double SignalPrice,Level;
        }
        private sealed class Deadline
        {internal string Name;internal DateTime Planned,Due;internal bool Clipped,Written;}
        private sealed class Track
        {
            internal int NextDeadline;
            internal Pending Candidate;internal DateTime EntryTime,LastTime,MfeTime,MaeTime,ReturnTime,OppositeTime;
            internal long EntrySeq,LastSeq,MfeSeq,MaeSeq,ReturnSeq,OppositeSeq;
            internal double Entry,Last,Mfe,Mae;
            internal List<Deadline> Deadlines=new List<Deadline>();internal List<Barrier> Barriers=new List<Barrier>();
        }
        private sealed class Barrier
        {
            internal string Model;internal double StopTicks,TargetTicks,R,StopPrice,TargetPrice;
            internal DateTime StopTime,TargetTime;internal long StopSeq,TargetSeq;
            internal double StopObserved=double.NaN,TargetObserved=double.NaN;
        }
        private sealed class Tech
        {
            internal int Count;
            internal DateTime AsOf;
            internal double E9=double.NaN,E21=double.NaN,E14=double.NaN,E50=double.NaN,Atr=double.NaN,Adx=double.NaN,Slope9;
            private double prevClose,prevHigh,prevLow,trSum,plusSum,minusSum,dxSum;
            private int dxCount;
            internal bool Ready {get{return Count>=50;}}
            internal void Add(Bar b)
            {
                double old=E9;
                E9=Ema(E9,b.Close,9);E21=Ema(E21,b.Close,21);E14=Ema(E14,b.Close,14);E50=Ema(E50,b.Close,50);
                Slope9=Finite(old)?E9-old:0;
                double tr=Count==0?b.High-b.Low:Math.Max(b.High-b.Low,Math.Max(Math.Abs(b.High-prevClose),Math.Abs(b.Low-prevClose)));
                double up=Count==0?0:b.High-prevHigh,down=Count==0?0:prevLow-b.Low;
                double plus=up>down&&up>0?up:0,minus=down>up&&down>0?down:0;
                Count++;
                if(Count<=14){trSum+=tr;plusSum+=plus;minusSum+=minus;Atr=trSum/Count;}
                else{trSum=trSum-trSum/14+tr;plusSum=plusSum-plusSum/14+plus;minusSum=minusSum-minusSum/14+minus;Atr=trSum/14;}
                if(Count>=14)
                {
                    double dx=plusSum+minusSum>0?100*Math.Abs(plusSum-minusSum)/(plusSum+minusSum):0;
                    dxCount++;
                    if(dxCount<=14){dxSum+=dx;Adx=dxSum/dxCount;}else Adx=(Adx*13+dx)/14;
                }
                prevClose=b.Close;prevHigh=b.High;prevLow=b.Low;AsOf=b.End;
            }
            private static double Ema(double old,double close,int period){return Finite(old)?old+2.0/(period+1)*(close-old):close;}
        }
    }
}
