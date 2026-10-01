using System;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private bool liveDailyStateRecoveryStarted;

        /// <summary>
        /// Production V1 uses the four-model candidate hook immediately before
        /// CanTakeNewTrade(). Recover live counters there so a restarted strategy
        /// cannot evaluate a signal with reset/historical-only daily state.
        ///
        /// The completed 5-minute context hook also calls this method, which
        /// normally completes recovery before the 09:35 entry window opens.
        /// </summary>
        private void EnsureLiveDailyStateRecoveryForSignal(
            DateTime signalTime)
        {
            if (State != State.Realtime)
                return;

            if (!liveDailyStateRecoveryStarted
                || liveDailyStateRecoveryDate
                    != signalTime.Date)
            {
                liveDailyStateRecoveryStarted = true;
                BeginLiveDailyStateRecovery();
            }

            if (EnsureLiveDailyStateRecoveryReady(
                    signalTime,
                    true))
            {
                return;
            }

            // Fail closed. CanTakeNewTrade() runs immediately after the
            // four-model candidate capture, so make every enabled daily cap
            // appear reached until authoritative live state is recovered.
            // ApplyRecoveredLiveDailyState() restores the real counters on the
            // next successful recovery attempt.
            if (MaxTradesPerDay > 0)
            {
                tradesToday =
                    Math.Max(
                        tradesToday,
                        MaxTradesPerDay);
            }

            if (MaxWinnersPerDay > 0)
            {
                winnersToday =
                    Math.Max(
                        winnersToday,
                        MaxWinnersPerDay);
            }

            if (MaxLossesPerDay > 0)
            {
                lossesToday =
                    Math.Max(
                        lossesToday,
                        MaxLossesPerDay);
            }
        }
    }
}
