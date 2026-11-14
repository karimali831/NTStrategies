using System;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        // Observational only. This tracker never gates, sizes, submits,
        // cancels, or modifies an order.
        private double researchPortfolioRealized;
        private double researchPortfolioPeakEquity;
        private double researchPortfolioMaxDrawdown;
        private DateTime researchPortfolioEntryFillTime = Core.Globals.MinDate;
        private string researchPortfolioDrawdownPath = string.Empty;
        private bool researchPortfolioDrawdownFaulted;

        private void InitializeResearchPortfolioDrawdown()
        {
            researchPortfolioRealized = 0.0;
            researchPortfolioPeakEquity = 0.0;
            researchPortfolioMaxDrawdown = 0.0;
            researchPortfolioEntryFillTime = Core.Globals.MinDate;
            researchPortfolioDrawdownPath = string.Empty;
            researchPortfolioDrawdownFaulted = false;

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

                researchPortfolioDrawdownPath = Path.Combine(
                    directory,
                    "overnight_edge_portfolio_drawdown_"
                    + instrumentName
                    + "_"
                    + researchRunId
                    + ".csv");

                File.WriteAllText(
                    researchPortfolioDrawdownPath,
                    "RunId,Instrument,EventTimeET,EventType,Signal,LastPrice,"
                    + "CumulativeRealized,OpenUnrealized,StrategyEquity,PeakEquity,"
                    + "IntratradeDrawdown,MaxIntratradeDrawdown,ProcessingState"
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                researchPortfolioDrawdownFaulted = true;
                Diagnostic(
                    DateTime.Now,
                    "RESEARCH DRAWDOWN DISABLED InitError={0}",
                    ex.Message);
            }
        }

        private void RecordResearchPortfolioDrawdownEntryFill(
            DateTime time,
            double entryPrice)
        {
            if (!EnableResearchTelemetry || researchPortfolioDrawdownFaulted)
                return;

            researchPortfolioEntryFillTime = time;

            UpdateResearchPortfolioEquity(
                time,
                entryPrice,
                0.0,
                "TradeEntry");
        }

        private void RecordResearchPortfolioDrawdownTick(
            DateTime time,
            double lastPrice)
        {
            if (!EnableResearchTelemetry
                || researchPortfolioDrawdownFaulted
                || !IsFinite(lastPrice))
            {
                return;
            }

            var openUnrealized = 0.0;

            if (activeTradeCounted
                && Position.MarketPosition != MarketPosition.Flat)
            {
                // Playback can dispatch a cached tick after the execution
                // callback even when that tick timestamp precedes the fill.
                if (researchPortfolioEntryFillTime != Core.Globals.MinDate
                    && time < researchPortfolioEntryFillTime)
                {
                    return;
                }

                var averageEntry = GetActiveAverageEntryPrice();

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

            UpdateResearchPortfolioEquity(
                time,
                lastPrice,
                openUnrealized,
                "Tick");
        }

        private void RecordResearchPortfolioDrawdownTradeCompleted(
            DateTime time,
            double exitPrice,
            double tradeGross)
        {
            if (!EnableResearchTelemetry || researchPortfolioDrawdownFaulted)
                return;

            researchPortfolioRealized += tradeGross;
            researchPortfolioEntryFillTime = Core.Globals.MinDate;

            UpdateResearchPortfolioEquity(
                time,
                exitPrice,
                0.0,
                "TradeExit");
        }

        private void UpdateResearchPortfolioEquity(
            DateTime time,
            double lastPrice,
            double openUnrealized,
            string eventType)
        {
            var equity =
                researchPortfolioRealized + openUnrealized;

            var newPeak = false;
            var newMaxDrawdown = false;

            if (equity > researchPortfolioPeakEquity)
            {
                researchPortfolioPeakEquity = equity;
                newPeak = true;
            }

            var drawdown =
                Math.Max(0.0, researchPortfolioPeakEquity - equity);

            if (drawdown > researchPortfolioMaxDrawdown)
            {
                researchPortfolioMaxDrawdown = drawdown;
                newMaxDrawdown = true;
            }

            if (eventType == "TradeEntry"
                || eventType == "TradeExit"
                || newPeak
                || newMaxDrawdown)
            {
                WriteResearchPortfolioDrawdown(
                    time,
                    eventType,
                    lastPrice,
                    openUnrealized,
                    equity,
                    drawdown);
            }
        }

        private void WriteResearchPortfolioDrawdown(
            DateTime time,
            string eventType,
            double lastPrice,
            double openUnrealized,
            double equity,
            double drawdown)
        {
            if (researchPortfolioDrawdownFaulted
                || string.IsNullOrEmpty(researchPortfolioDrawdownPath))
            {
                return;
            }

            try
            {
                File.AppendAllText(
                    researchPortfolioDrawdownPath,
                    string.Join(
                        ",",
                        CsvResearchDrawdown(researchRunId),
                        CsvResearchDrawdown(
                            Instrument == null
                                ? string.Empty
                                : Instrument.FullName),
                        CsvResearchDrawdown(
                            time.ToString(
                                "yyyy-MM-dd HH:mm:ss.fff",
                                CultureInfo.InvariantCulture)),
                        CsvResearchDrawdown(eventType),
                        CsvResearchDrawdown(activeEntrySignal),
                        NumberResearchDrawdown(lastPrice),
                        NumberResearchDrawdown(researchPortfolioRealized),
                        NumberResearchDrawdown(openUnrealized),
                        NumberResearchDrawdown(equity),
                        NumberResearchDrawdown(researchPortfolioPeakEquity),
                        NumberResearchDrawdown(drawdown),
                        NumberResearchDrawdown(researchPortfolioMaxDrawdown),
                        CsvResearchDrawdown(State.ToString()))
                    + Environment.NewLine);
            }
            catch (Exception ex)
            {
                researchPortfolioDrawdownFaulted = true;
                Diagnostic(
                    time,
                    "RESEARCH DRAWDOWN DISABLED WriteError={0}",
                    ex.Message);
            }
        }

        private void DisposeResearchPortfolioDrawdown()
        {
            if (!EnableResearchTelemetry)
                return;

            Diagnostic(
                DateTime.Now,
                "RESEARCH DRAWDOWN SUMMARY Realized={0:0.00} "
                + "Peak={1:0.00} MaxIntratradeDD={2:0.00}",
                researchPortfolioRealized,
                researchPortfolioPeakEquity,
                researchPortfolioMaxDrawdown);
        }

        private static string NumberResearchDrawdown(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? string.Empty
                : value.ToString(
                    "0.########",
                    CultureInfo.InvariantCulture);
        }

        private static string CsvResearchDrawdown(string value)
        {
            return "\""
                + (value ?? string.Empty)
                    .Replace("\"", "\"\"")
                + "\"";
        }
    }
}
