using System;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private bool CanTakeNewTrade(
            DateTime time,
            bool allowExistingPendingEntry)
        {
            // Historical processing is ignored by restart recovery. In State.Realtime,
            // live/Playback counters must be authoritative before a new entry is allowed.
            if (!EnsureLiveDailyStateReady(time))
                return false;

            if (Position.MarketPosition != MarketPosition.Flat
                || entryOrderPending
                || manualExitPending
                || (!allowExistingPendingEntry
                    && pendingDirection != PendingDirection.None))
            {
                return false;
            }

            if (MaxTradesPerDay > 0 && tradesToday >= MaxTradesPerDay)
                return false;

            if (MaxWinnersPerDay > 0 && winnersToday >= MaxWinnersPerDay)
                return false;

            if (MaxLossesPerDay > 0 && lossesToday >= MaxLossesPerDay)
                return false;

            return true;
        }


        private void EnsureTradingDate(
            DateTime date,
            DateTime eventTime)
        {
            date =
                date.Date;


            if (activeTradingDate
                == date)
            {
                return;
            }


            if (activeTradingDate
                != Core.Globals.MinDate)
            {
                Diagnostic(
                    eventTime,
                    "DAY SUMMARY " +
                    "Date={0:yyyy-MM-dd} " +
                    "Trades={1} Winners={2} Losses={3} " +
                    "GrossPnl={4:0.00}",
                    activeTradingDate,
                    tradesToday,
                    winnersToday,
                    lossesToday,
                    grossPnlToday);
            }


            activeTradingDate =
                date;

            tradesToday = 0;
            winnersToday = 0;
            lossesToday = 0;

            grossPnlToday = 0;

            ResetResearchTelemetryDailyState();


            ClearPendingEntry(
                eventTime,
                "NewTradingDate");


            Diagnostic(
                eventTime,
                "NEW TRADING DATE {0:yyyy-MM-dd}",
                activeTradingDate);
        }
    }
}
