using System;
using System.Globalization;
using System.IO;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private const string PortfolioRiskStateVersion = "2";

        private bool portfolioRiskReady;
        private string portfolioRiskStatePath = string.Empty;
        private string portfolioRiskAuditPath = string.Empty;
        private bool portfolioRiskAuditFaulted;

        private double portfolioRiskCumulativeRealized;
        private double portfolioRiskPeakEquity;
        private double portfolioRiskMaxIntratradeDrawdown;
        private DateTime portfolioRiskLastEventTime = Core.Globals.MinDate;
        private DateTime portfolioRiskActiveEntryFillTime = Core.Globals.MinDate;

        private void InitializePortfolioRiskGuard()
        {
            portfolioRiskReady = State != State.Realtime;
            portfolioRiskStatePath = string.Empty;
            portfolioRiskAuditPath = string.Empty;
            portfolioRiskAuditFaulted = false;
            portfolioRiskCumulativeRealized = 0.0;
            portfolioRiskPeakEquity = 0.0;
            portfolioRiskMaxIntratradeDrawdown = 0.0;
            portfolioRiskLastEventTime = Core.Globals.MinDate;
            portfolioRiskActiveEntryFillTime = Core.Globals.MinDate;

            if (!EnableResearchTelemetry)
                return;

            try
            {
                var directory = Path.Combine(
                    Core.Globals.UserDataDir,
                    "NinjexResearch",
                    "OvernightEdgePortfolio");

                Directory.CreateDirectory(directory);

                var instrumentName = Instrument == null
                    ? "UnknownInstrument"
                    : SanitizeFileName(Instrument.FullName);

                portfolioRiskAuditPath = Path.Combine(
                    directory,
                    "overnight_edge_portfolio_risk_"
                    + instrumentName
                    + "_"
                    + researchRunId
                    + ".csv");

                File.WriteAllText(
                    portfolioRiskAuditPath,
                    "RunId,Instrument,EventTimeET,EventType,Signal,LastPrice,"
                    + "CumulativeRealized,OpenUnrealized,StrategyEquity,PeakEquity,"
                    + "CurrentDrawdown,MaxIntratradeDrawdown,ProspectiveRisk,"
                    + "ProspectiveDrawdown,Limit,Reason,ProcessingState"
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                portfolioRiskAuditFaulted = true;
                Diagnostic(
                    DateTime.Now,
                    "PORTFOLIO RISK AUDIT DISABLED InitError={0}",
                    ex.Message);
            }
        }

        private void ResetPortfolioRiskPersistenceOnRealtimeTransition()
        {
            // Load lazily from the first market event. Playback timestamps can
            // be historical, so DateTime.Now cannot be used to detect rewinds.
            portfolioRiskReady = false;
            portfolioRiskStatePath = string.Empty;
            portfolioRiskActiveEntryFillTime = Core.Globals.MinDate;
        }

        private bool EnsurePortfolioRiskStateReady(DateTime eventTime)
        {
            if (State != State.Realtime)
                return true;

            if (portfolioRiskReady)
                return true;

            try
            {
                portfolioRiskStatePath = BuildPortfolioRiskStatePath();

                double realized;
                double peak;
                double maxDrawdown;
                DateTime lastEvent;

                var loaded = TryReadPortfolioRiskState(
                    out realized,
                    out peak,
                    out maxDrawdown,
                    out lastEvent);

                if (loaded
                    && lastEvent != Core.Globals.MinDate
                    && eventTime <= lastEvent)
                {
                    // Market Replay was rewound/restarted to an earlier point.
                    // Start a fresh equity path instead of contaminating the
                    // new run with a later replay's state.
                    loaded = false;

                    Diagnostic(
                        eventTime,
                        "PORTFOLIO RISK STATE RESET Reason=ReplayRewind "
                        + "StoredLast={0:yyyy-MM-dd HH:mm:ss.fff}",
                        lastEvent);
                }

                if (loaded)
                {
                    portfolioRiskCumulativeRealized = realized;
                    portfolioRiskPeakEquity = Math.Max(peak, realized);
                    portfolioRiskMaxIntratradeDrawdown =
                        Math.Max(0.0, maxDrawdown);
                    portfolioRiskLastEventTime = lastEvent;

                    Diagnostic(
                        eventTime,
                        "PORTFOLIO RISK STATE RESTORED "
                        + "Realized={0:0.00} Peak={1:0.00} MaxDD={2:0.00} "
                        + "Last={3:yyyy-MM-dd HH:mm:ss.fff}",
                        portfolioRiskCumulativeRealized,
                        portfolioRiskPeakEquity,
                        portfolioRiskMaxIntratradeDrawdown,
                        portfolioRiskLastEventTime);
                }
                else
                {
                    portfolioRiskCumulativeRealized = 0.0;
                    portfolioRiskPeakEquity = 0.0;
                    portfolioRiskMaxIntratradeDrawdown = 0.0;
                    portfolioRiskLastEventTime = eventTime;

                    PersistPortfolioRiskState(
                        eventTime,
                        "Initialize");
                }

                portfolioRiskReady = true;
                return true;
            }
            catch (Exception ex)
            {
                portfolioRiskReady = false;

                Diagnostic(
                    eventTime,
                    "PORTFOLIO RISK STATE ERROR Message={0}",
                    ex.Message);

                // Risk state uncertainty must fail closed in Realtime,
                // including Playback.
                return false;
            }
        }

        private bool CanPassProspectiveDrawdownGuard(
            DateTime time,
            int stopTicks,
            string signalName,
            out string reason)
        {
            reason = string.Empty;

            if (MaximumProspectiveDrawdownDollars <= 0)
                return true;

            if (!EnsurePortfolioRiskStateReady(time))
            {
                reason = "PortfolioRiskStateNotReady";
                return false;
            }

            if (stopTicks <= 0
                || TickSize <= 0
                || Instrument == null)
            {
                reason = "InvalidProspectiveRisk";
                return false;
            }

            var currentDrawdown =
                Math.Max(
                    0.0,
                    portfolioRiskPeakEquity
                    - portfolioRiskCumulativeRealized);

            var prospectiveRisk =
                stopTicks
                * TickSize
                * Instrument.MasterInstrument.PointValue
                * Math.Max(1, OrderQuantity);

            var prospectiveDrawdown =
                currentDrawdown + prospectiveRisk;

            var allowed =
                prospectiveDrawdown
                <= MaximumProspectiveDrawdownDollars + 0.0001;

            if (!allowed)
            {
                reason = "ProspectiveDrawdownLimit";

                Diagnostic(
                    time,
                    "PORTFOLIO RISK BLOCK "
                    + "Signal={0} CurrentDD={1:0.00} Risk={2:0.00} "
                    + "ProspectiveDD={3:0.00} Limit={4:0.00}",
                    signalName,
                    currentDrawdown,
                    prospectiveRisk,
                    prospectiveDrawdown,
                    MaximumProspectiveDrawdownDollars);
            }

            WritePortfolioRiskAudit(
                time,
                allowed ? "ProspectiveRiskPass" : "ProspectiveRiskBlock",
                signalName,
                double.NaN,
                0.0,
                currentDrawdown,
                prospectiveRisk,
                prospectiveDrawdown,
                reason);

            return allowed;
        }

        private void RecordPortfolioRiskEntryFill(
            DateTime time,
            double entryPrice)
        {
            if (!EnsurePortfolioRiskStateReady(time))
                return;

            portfolioRiskActiveEntryFillTime = time;
            portfolioRiskLastEventTime = MaxTime(
                portfolioRiskLastEventTime,
                time);

            WritePortfolioRiskAudit(
                time,
                "TradeEntry",
                activeEntrySignal,
                entryPrice,
                0.0,
                Math.Max(
                    0.0,
                    portfolioRiskPeakEquity
                    - portfolioRiskCumulativeRealized),
                0.0,
                0.0,
                string.Empty);

            PersistPortfolioRiskState(
                time,
                "EntryFill");
        }

        private void RecordPortfolioRiskTick(
            DateTime time,
            double lastPrice)
        {
            if (!IsFinite(lastPrice)
                || !EnsurePortfolioRiskStateReady(time))
            {
                return;
            }

            var openUnrealized = 0.0;

            if (activeTradeCounted
                && Position.MarketPosition
                    != NinjaTrader.Cbi.MarketPosition.Flat)
            {
                if (portfolioRiskActiveEntryFillTime
                        != Core.Globals.MinDate
                    && time < portfolioRiskActiveEntryFillTime)
                {
                    return;
                }

                var averageEntry =
                    GetActiveAverageEntryPrice();

                if (IsFinite(averageEntry))
                {
                    var points =
                        activeTradeDirection == PendingDirection.Long
                            ? lastPrice - averageEntry
                            : averageEntry - lastPrice;

                    openUnrealized =
                        points
                        * Instrument.MasterInstrument.PointValue
                        * Math.Max(1, Position.Quantity);
                }
            }

            UpdatePortfolioRiskEquity(
                time,
                lastPrice,
                openUnrealized,
                "Tick");
        }

        private void RecordPortfolioRiskTradeCompleted(
            DateTime time,
            double exitPrice,
            double tradeGross)
        {
            if (!EnsurePortfolioRiskStateReady(time))
                return;

            portfolioRiskCumulativeRealized += tradeGross;
            portfolioRiskActiveEntryFillTime = Core.Globals.MinDate;

            UpdatePortfolioRiskEquity(
                time,
                exitPrice,
                0.0,
                "TradeExit");

            PersistPortfolioRiskState(
                time,
                "TradeComplete");
        }

        private void UpdatePortfolioRiskEquity(
            DateTime time,
            double lastPrice,
            double openUnrealized,
            string eventType)
        {
            var equity =
                portfolioRiskCumulativeRealized
                + openUnrealized;

            var newPeak = false;
            var newMaxDrawdown = false;

            if (equity > portfolioRiskPeakEquity)
            {
                portfolioRiskPeakEquity = equity;
                newPeak = true;
            }

            var currentDrawdown =
                Math.Max(
                    0.0,
                    portfolioRiskPeakEquity - equity);

            if (currentDrawdown
                > portfolioRiskMaxIntratradeDrawdown)
            {
                portfolioRiskMaxIntratradeDrawdown =
                    currentDrawdown;
                newMaxDrawdown = true;
            }

            portfolioRiskLastEventTime = MaxTime(
                portfolioRiskLastEventTime,
                time);

            if (eventType == "TradeEntry"
                || eventType == "TradeExit"
                || newPeak
                || newMaxDrawdown)
            {
                WritePortfolioRiskAudit(
                    time,
                    eventType,
                    activeEntrySignal,
                    lastPrice,
                    openUnrealized,
                    currentDrawdown,
                    0.0,
                    0.0,
                    string.Empty);
            }

            // Prop trailing limits can ratchet on unrealized equity. Persist
            // new peaks so an NT/VPS restart cannot forget a higher watermark.
            if (newPeak
                || eventType == "TradeExit")
            {
                PersistPortfolioRiskState(
                    time,
                    newPeak ? "NewPeak" : "TradeExit");
            }
        }

        private void DisposePortfolioRiskGuard()
        {
            if (portfolioRiskReady
                && portfolioRiskLastEventTime
                    != Core.Globals.MinDate)
            {
                PersistPortfolioRiskState(
                    portfolioRiskLastEventTime,
                    "Terminate");
            }

            Diagnostic(
                DateTime.Now,
                "PORTFOLIO RISK SUMMARY "
                + "Realized={0:0.00} Peak={1:0.00} "
                + "MaxIntratradeDD={2:0.00} Limit={3:0.00}",
                portfolioRiskCumulativeRealized,
                portfolioRiskPeakEquity,
                portfolioRiskMaxIntratradeDrawdown,
                MaximumProspectiveDrawdownDollars);
        }

        private string BuildPortfolioRiskStatePath()
        {
            var directory = Path.Combine(
                Core.Globals.UserDataDir,
                "NinjexState",
                "OvernightEdgePortfolio");

            Directory.CreateDirectory(directory);

            return Path.Combine(
                directory,
                "portfolio_risk_"
                + SanitizeLiveStateFileName(
                    GetLiveRecoveryAccountName())
                + ".state");
        }

        private bool TryReadPortfolioRiskState(
            out double realized,
            out double peak,
            out double maxDrawdown,
            out DateTime lastEvent)
        {
            realized = 0.0;
            peak = 0.0;
            maxDrawdown = 0.0;
            lastEvent = Core.Globals.MinDate;

            if (string.IsNullOrEmpty(portfolioRiskStatePath)
                || !File.Exists(portfolioRiskStatePath))
            {
                return false;
            }

            var lines = File.ReadAllLines(
                portfolioRiskStatePath);

            string version = null;
            string account = null;
            string realizedText = null;
            string peakText = null;
            string maxDrawdownText = null;
            string lastEventText = null;

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var separator = line.IndexOf('=');

                if (separator <= 0)
                    continue;

                var key = line.Substring(0, separator);
                var value = line.Substring(separator + 1);

                if (key == "Version")
                    version = value;
                else if (key == "Account")
                    account = value;
                else if (key == "Realized")
                    realizedText = value;
                else if (key == "Peak")
                    peakText = value;
                else if (key == "MaxIntratradeDrawdown")
                    maxDrawdownText = value;
                else if (key == "LastEventTime")
                    lastEventText = value;
            }

            if (version != PortfolioRiskStateVersion
                || !string.Equals(
                    account,
                    GetLiveRecoveryAccountName(),
                    StringComparison.Ordinal)
                || !double.TryParse(
                    realizedText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out realized)
                || !double.TryParse(
                    peakText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out peak)
                || !double.TryParse(
                    maxDrawdownText,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out maxDrawdown)
                || !DateTime.TryParseExact(
                    lastEventText,
                    "O",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind,
                    out lastEvent))
            {
                return false;
            }

            return true;
        }

        private void PersistPortfolioRiskState(
            DateTime eventTime,
            string reason)
        {
            if (State != State.Realtime)
                return;

            try
            {
                if (string.IsNullOrEmpty(portfolioRiskStatePath))
                    portfolioRiskStatePath =
                        BuildPortfolioRiskStatePath();

                var tempPath =
                    portfolioRiskStatePath + ".tmp";

                File.WriteAllLines(
                    tempPath,
                    new[]
                    {
                        "Version=" + PortfolioRiskStateVersion,
                        "Account=" + GetLiveRecoveryAccountName(),
                        "Realized="
                            + portfolioRiskCumulativeRealized.ToString(
                                "0.########",
                                CultureInfo.InvariantCulture),
                        "Peak="
                            + portfolioRiskPeakEquity.ToString(
                                "0.########",
                                CultureInfo.InvariantCulture),
                        "MaxIntratradeDrawdown="
                            + portfolioRiskMaxIntratradeDrawdown.ToString(
                                "0.########",
                                CultureInfo.InvariantCulture),
                        "LastEventTime="
                            + eventTime.ToString(
                                "O",
                                CultureInfo.InvariantCulture),
                        "Reason=" + (reason ?? string.Empty)
                    });

                if (File.Exists(portfolioRiskStatePath))
                {
                    try
                    {
                        File.Replace(
                            tempPath,
                            portfolioRiskStatePath,
                            null);
                    }
                    catch
                    {
                        File.Copy(
                            tempPath,
                            portfolioRiskStatePath,
                            true);
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(
                        tempPath,
                        portfolioRiskStatePath);
                }
            }
            catch (Exception ex)
            {
                portfolioRiskReady = false;

                Diagnostic(
                    eventTime,
                    "PORTFOLIO RISK STATE PERSIST ERROR "
                    + "Reason={0} Message={1}",
                    reason ?? string.Empty,
                    ex.Message);
            }
        }

        private void WritePortfolioRiskAudit(
            DateTime time,
            string eventType,
            string signal,
            double lastPrice,
            double openUnrealized,
            double currentDrawdown,
            double prospectiveRisk,
            double prospectiveDrawdown,
            string reason)
        {
            if (!EnableResearchTelemetry
                || portfolioRiskAuditFaulted
                || string.IsNullOrEmpty(portfolioRiskAuditPath))
            {
                return;
            }

            try
            {
                var equity =
                    portfolioRiskCumulativeRealized
                    + openUnrealized;

                File.AppendAllText(
                    portfolioRiskAuditPath,
                    string.Join(
                        ",",
                        CsvPortfolioRisk(researchRunId),
                        CsvPortfolioRisk(
                            Instrument == null
                                ? string.Empty
                                : Instrument.FullName),
                        CsvPortfolioRisk(
                            time.ToString(
                                "yyyy-MM-dd HH:mm:ss.fff",
                                CultureInfo.InvariantCulture)),
                        CsvPortfolioRisk(eventType),
                        CsvPortfolioRisk(signal),
                        NumberPortfolioRisk(lastPrice),
                        NumberPortfolioRisk(
                            portfolioRiskCumulativeRealized),
                        NumberPortfolioRisk(openUnrealized),
                        NumberPortfolioRisk(equity),
                        NumberPortfolioRisk(
                            portfolioRiskPeakEquity),
                        NumberPortfolioRisk(currentDrawdown),
                        NumberPortfolioRisk(
                            portfolioRiskMaxIntratradeDrawdown),
                        NumberPortfolioRisk(prospectiveRisk),
                        NumberPortfolioRisk(prospectiveDrawdown),
                        NumberPortfolioRisk(
                            MaximumProspectiveDrawdownDollars),
                        CsvPortfolioRisk(reason),
                        CsvPortfolioRisk(State.ToString()))
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                portfolioRiskAuditFaulted = true;

                Diagnostic(
                    time,
                    "PORTFOLIO RISK AUDIT DISABLED WriteError={0}",
                    ex.Message);
            }
        }

        private static DateTime MaxTime(
            DateTime left,
            DateTime right)
        {
            return left >= right ? left : right;
        }

        private static string NumberPortfolioRisk(
            double value)
        {
            return double.IsNaN(value)
                   || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(
                    "0.########",
                    CultureInfo.InvariantCulture);
        }

        private static string CsvPortfolioRisk(
            string value)
        {
            return """
                + (value ?? string.Empty)
                    .Replace(""", """")
                + """;
        }
    }
}
