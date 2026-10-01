using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private const int LiveDailyStateRecoveryGraceMilliseconds = 1500;
        private const string LiveDailyStateSnapshotVersion = "1";

        private bool liveDailyStateRecoveryApplicable;
        private bool liveDailyStateRecoveryComplete = true;
        private bool liveDailyStateRecoveryBlockLogged;

        private DateTime liveDailyStateRecoveryDate =
            Core.Globals.MinDate;

        private DateTime liveDailyStateRecoveryNotBeforeUtc =
            Core.Globals.MinDate;

        private string liveDailyStatePath =
            string.Empty;

        private sealed class LiveDailyStateSnapshot
        {
            public DateTime TradingDate = Core.Globals.MinDate;
            public int Trades;
            public int Winners;
            public int Losses;
            public double GrossPnl;
        }

        private sealed class RecoveredOpenTrade
        {
            public string EntrySignal = string.Empty;
            public PendingDirection Direction = PendingDirection.None;
            public int OpenQuantity;
            public int EntryQuantity;
            public double EntryPriceQuantity;
            public double GrossPnl;
        }

        /// <summary>
        /// Called when the strategy transitions from historical processing into
        /// real time. Historical strategy trades are not authoritative for the
        /// live daily risk counters after a NinjaTrader/VPS restart, so replace
        /// them with persisted/account execution state before allowing entries.
        /// Playback is intentionally excluded so Market Replay remains unchanged.
        /// </summary>
        private void BeginLiveDailyStateRecovery()
        {
            liveDailyStateRecoveryApplicable =
                ShouldUseLiveDailyStateRecovery();

            liveDailyStateRecoveryComplete =
                !liveDailyStateRecoveryApplicable;

            liveDailyStateRecoveryBlockLogged = false;

            if (!liveDailyStateRecoveryApplicable)
                return;

            var eventTime =
                GetCurrentStrategyTimeForRecovery();

            var tradingDate =
                activeTradingDate != Core.Globals.MinDate
                    ? activeTradingDate.Date
                    : eventTime.Date;

            liveDailyStateRecoveryDate =
                tradingDate;

            liveDailyStatePath =
                BuildLiveDailyStatePath();

            LiveDailyStateSnapshot persisted;

            if (TryReadLiveDailyStateSnapshot(
                    tradingDate,
                    out persisted))
            {
                LiveDailyStateSnapshot accountState;
                string accountError;

                var accountRecovered =
                    TryRebuildLiveDailyStateFromAccountExecutions(
                        tradingDate,
                        out accountState,
                        out accountError);

                if (accountRecovered
                    && accountState.Trades > persisted.Trades)
                {
                    ApplyRecoveredLiveDailyState(
                        accountState,
                        eventTime,
                        "AccountExecutionsNewer");

                    PersistLiveDailyStateSnapshot(
                        eventTime,
                        "RecoveryAccountExecutionsNewer");

                    return;
                }

                if (accountRecovered
                    && accountState.Trades == persisted.Trades
                    && !LiveDailyStatesEquivalent(
                        persisted,
                        accountState))
                {
                    liveDailyStateRecoveryComplete = false;

                    Diagnostic(
                        eventTime,
                        "LIVE DAILY STATE RECOVERY FAILED " +
                        "Reason=PersistedAccountMismatch " +
                        "Persisted=T{0}/W{1}/L{2}/Pnl{3:0.00} " +
                        "Account=T{4}/W{5}/L{6}/Pnl{7:0.00}",
                        persisted.Trades,
                        persisted.Winners,
                        persisted.Losses,
                        persisted.GrossPnl,
                        accountState.Trades,
                        accountState.Winners,
                        accountState.Losses,
                        accountState.GrossPnl);

                    return;
                }

                // If the account collection is still repopulating after a
                // reconnect it may temporarily contain fewer executions than
                // the persisted state. Never downgrade the counters.
                ApplyRecoveredLiveDailyState(
                    persisted,
                    eventTime,
                    accountRecovered
                        ? "PersistedSnapshot"
                        : "PersistedSnapshotAccountUnavailable");

                return;
            }

            // No persisted state exists. Give the broker connection a short
            // grace period to repopulate Account.Executions before rebuilding.
            // Until recovery completes, CanTakeNewTrade() fails closed.
            liveDailyStateRecoveryNotBeforeUtc =
                DateTime.UtcNow.AddMilliseconds(
                    LiveDailyStateRecoveryGraceMilliseconds);

            Diagnostic(
                eventTime,
                "LIVE DAILY STATE RECOVERY PENDING " +
                "Date={0:yyyy-MM-dd} Reason=NoPersistedSnapshot",
                tradingDate);
        }

        /// <summary>
        /// Called by CanTakeNewTrade. A live strategy never submits a new entry
        /// while same-day state recovery is incomplete or ambiguous.
        /// </summary>
        private bool EnsureLiveDailyStateRecoveryReady(
            DateTime eventTime,
            bool logReason)
        {
            if (!liveDailyStateRecoveryApplicable)
                return true;

            if (liveDailyStateRecoveryComplete)
                return true;

            if (liveDailyStateRecoveryDate
                    != Core.Globals.MinDate
                && eventTime.Date
                    != liveDailyStateRecoveryDate.Date)
            {
                // The normal daily reset path will create a fresh zero-state
                // snapshot for a genuinely new ET calendar date.
                liveDailyStateRecoveryDate =
                    eventTime.Date;

                liveDailyStatePath =
                    BuildLiveDailyStatePath();
            }

            if (DateTime.UtcNow
                < liveDailyStateRecoveryNotBeforeUtc)
            {
                LogLiveDailyStateRecoveryBlock(
                    eventTime,
                    logReason,
                    "WaitingForAccountExecutions");

                return false;
            }

            LiveDailyStateSnapshot recovered;
            string error;

            if (!TryRebuildLiveDailyStateFromAccountExecutions(
                    liveDailyStateRecoveryDate,
                    out recovered,
                    out error))
            {
                LogLiveDailyStateRecoveryBlock(
                    eventTime,
                    logReason,
                    string.IsNullOrEmpty(error)
                        ? "AccountExecutionRecoveryFailed"
                        : error);

                return false;
            }

            ApplyRecoveredLiveDailyState(
                recovered,
                eventTime,
                "AccountExecutions");

            PersistLiveDailyStateSnapshot(
                eventTime,
                "RecoveryAccountExecutions");

            return true;
        }

        /// <summary>
        /// Called from EnsureTradingDate after the ordinary daily counters have
        /// been reset. In real time this establishes a durable zero-state for
        /// the new day. Historical/Playback processing is unaffected.
        /// </summary>
        private void OnLiveTradingDateReset(
            DateTime eventTime)
        {
            if (!liveDailyStateRecoveryApplicable
                || State != State.Realtime)
            {
                return;
            }

            liveDailyStateRecoveryDate =
                activeTradingDate;

            liveDailyStateRecoveryComplete = true;
            liveDailyStateRecoveryBlockLogged = false;

            liveDailyStatePath =
                BuildLiveDailyStatePath();

            PersistLiveDailyStateSnapshot(
                eventTime,
                "NewTradingDate");
        }

        /// <summary>
        /// Persist after the first fill of every new entry and again after the
        /// trade completes. This makes the risk counters survive NT/VPS restarts.
        /// </summary>
        private void PersistLiveDailyStateSnapshot(
            DateTime eventTime,
            string reason)
        {
            if (!liveDailyStateRecoveryApplicable
                || activeTradingDate == Core.Globals.MinDate)
            {
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(liveDailyStatePath))
                {
                    liveDailyStatePath =
                        BuildLiveDailyStatePath();
                }

                if (string.IsNullOrEmpty(liveDailyStatePath))
                    return;

                var directory =
                    Path.GetDirectoryName(
                        liveDailyStatePath);

                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(
                        directory);
                }

                var lines =
                    new[]
                    {
                        "Version=" + LiveDailyStateSnapshotVersion,
                        "TradingDate=" + activeTradingDate.ToString(
                            "yyyy-MM-dd",
                            CultureInfo.InvariantCulture),
                        "Account=" + GetRecoveryAccountName(),
                        "Instrument=" + GetRecoveryInstrumentName(),
                        "Trades=" + tradesToday.ToString(
                            CultureInfo.InvariantCulture),
                        "Winners=" + winnersToday.ToString(
                            CultureInfo.InvariantCulture),
                        "Losses=" + lossesToday.ToString(
                            CultureInfo.InvariantCulture),
                        "GrossPnl=" + grossPnlToday.ToString(
                            "0.########",
                            CultureInfo.InvariantCulture),
                        "UpdatedUtc=" + DateTime.UtcNow.ToString(
                            "O",
                            CultureInfo.InvariantCulture),
                        "Reason=" + (reason ?? string.Empty)
                    };

                var tempPath =
                    liveDailyStatePath + ".tmp";

                File.WriteAllLines(
                    tempPath,
                    lines);

                if (File.Exists(liveDailyStatePath))
                {
                    try
                    {
                        File.Replace(
                            tempPath,
                            liveDailyStatePath,
                            null);
                    }
                    catch
                    {
                        File.Copy(
                            tempPath,
                            liveDailyStatePath,
                            true);

                        File.Delete(
                            tempPath);
                    }
                }
                else
                {
                    File.Move(
                        tempPath,
                        liveDailyStatePath);
                }
            }
            catch (Exception ex)
            {
                Diagnostic(
                    eventTime,
                    "LIVE DAILY STATE PERSIST ERROR " +
                    "Reason={0} Message={1}",
                    reason ?? string.Empty,
                    ex.Message);
            }
        }

        private bool TryReadLiveDailyStateSnapshot(
            DateTime tradingDate,
            out LiveDailyStateSnapshot snapshot)
        {
            snapshot = null;

            try
            {
                if (string.IsNullOrEmpty(liveDailyStatePath)
                    || !File.Exists(liveDailyStatePath))
                {
                    return false;
                }

                var values =
                    new Dictionary<string, string>(
                        StringComparer.OrdinalIgnoreCase);

                foreach (var line in
                         File.ReadAllLines(liveDailyStatePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var separator =
                        line.IndexOf('=');

                    if (separator <= 0)
                        continue;

                    values[line.Substring(0, separator)] =
                        line.Substring(separator + 1);
                }

                string version;
                string dateText;
                string account;
                string instrument;
                string tradesText;
                string winnersText;
                string lossesText;
                string grossText;

                if (!values.TryGetValue("Version", out version)
                    || version != LiveDailyStateSnapshotVersion
                    || !values.TryGetValue("TradingDate", out dateText)
                    || !values.TryGetValue("Account", out account)
                    || !values.TryGetValue("Instrument", out instrument)
                    || !values.TryGetValue("Trades", out tradesText)
                    || !values.TryGetValue("Winners", out winnersText)
                    || !values.TryGetValue("Losses", out lossesText)
                    || !values.TryGetValue("GrossPnl", out grossText))
                {
                    return false;
                }

                DateTime date;
                int trades;
                int winners;
                int losses;
                double gross;

                if (!DateTime.TryParseExact(
                        dateText,
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out date)
                    || !int.TryParse(
                        tradesText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out trades)
                    || !int.TryParse(
                        winnersText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out winners)
                    || !int.TryParse(
                        lossesText,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out losses)
                    || !double.TryParse(
                        grossText,
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out gross))
                {
                    return false;
                }

                if (date.Date != tradingDate.Date
                    || !string.Equals(
                        account,
                        GetRecoveryAccountName(),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        instrument,
                        GetRecoveryInstrumentName(),
                        StringComparison.OrdinalIgnoreCase)
                    || trades < 0
                    || winners < 0
                    || losses < 0
                    || winners + losses > trades)
                {
                    return false;
                }

                snapshot =
                    new LiveDailyStateSnapshot
                    {
                        TradingDate = date.Date,
                        Trades = trades,
                        Winners = winners,
                        Losses = losses,
                        GrossPnl = gross
                    };

                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryRebuildLiveDailyStateFromAccountExecutions(
            DateTime tradingDate,
            out LiveDailyStateSnapshot snapshot,
            out string error)
        {
            snapshot =
                new LiveDailyStateSnapshot
                {
                    TradingDate = tradingDate.Date
                };

            error =
                string.Empty;

            if (Account == null)
            {
                error = "AccountUnavailable";
                return false;
            }

            if (Account.Connection == null
                || Account.Connection.Status
                    != ConnectionStatus.Connected)
            {
                error = "OrderConnectionNotConnected";
                return false;
            }

            if (Instrument == null)
            {
                error = "InstrumentUnavailable";
                return false;
            }

            var relevant =
                new List<Execution>();

            try
            {
                lock (Account.Executions)
                {
                    foreach (var execution in Account.Executions)
                    {
                        if (execution == null
                            || execution.Order == null
                            || execution.Instrument == null
                            || execution.Quantity <= 0
                            || execution.Time.Date
                                != tradingDate.Date
                            || !string.Equals(
                                execution.Instrument.FullName,
                                Instrument.FullName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var order =
                            execution.Order;

                        var fromEntrySignal =
                            order.FromEntrySignal
                            ?? string.Empty;

                        if (IsEntrySignalName(order.Name)
                            || IsEntrySignalName(fromEntrySignal)
                            || IsKnownRecoveryExitName(order.Name))
                        {
                            relevant.Add(execution);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error =
                    "AccountExecutionsReadError:" + ex.Message;

                return false;
            }

            relevant.Sort(
                delegate(Execution left, Execution right)
                {
                    var timeCompare =
                        left.Time.CompareTo(right.Time);

                    if (timeCompare != 0)
                        return timeCompare;

                    return string.Compare(
                        left.ExecutionId ?? string.Empty,
                        right.ExecutionId ?? string.Empty,
                        StringComparison.Ordinal);
                });

            var openTrade =
                new RecoveredOpenTrade();

            var pointValue =
                Instrument.MasterInstrument.PointValue;

            foreach (var execution in relevant)
            {
                var order =
                    execution.Order;

                if (IsEntrySignalName(order.Name))
                {
                    var direction =
                        IsLongEntrySignalName(order.Name)
                            ? PendingDirection.Long
                            : PendingDirection.Short;

                    if (openTrade.OpenQuantity <= 0)
                    {
                        openTrade =
                            new RecoveredOpenTrade
                            {
                                EntrySignal = order.Name,
                                Direction = direction
                            };

                        snapshot.Trades++;
                    }
                    else if (!string.Equals(
                        openTrade.EntrySignal,
                        order.Name,
                        StringComparison.Ordinal)
                        || openTrade.Direction != direction)
                    {
                        error =
                            "OverlappingStrategyEntries";

                        return false;
                    }

                    openTrade.OpenQuantity +=
                        execution.Quantity;

                    openTrade.EntryQuantity +=
                        execution.Quantity;

                    openTrade.EntryPriceQuantity +=
                        execution.Price
                        * execution.Quantity;

                    continue;
                }

                var fromEntrySignal =
                    order.FromEntrySignal
                    ?? string.Empty;

                var looksLikeStrategyExit =
                    IsEntrySignalName(fromEntrySignal)
                    || IsKnownRecoveryExitName(order.Name);

                if (!looksLikeStrategyExit)
                    continue;

                if (openTrade.OpenQuantity <= 0
                    || openTrade.EntryQuantity <= 0)
                {
                    error =
                        "ExitWithoutRecoverableEntry";

                    return false;
                }

                if (!string.IsNullOrEmpty(fromEntrySignal)
                    && IsEntrySignalName(fromEntrySignal)
                    && !string.Equals(
                        fromEntrySignal,
                        openTrade.EntrySignal,
                        StringComparison.Ordinal))
                {
                    error =
                        "ExitEntrySignalMismatch";

                    return false;
                }

                if (execution.Quantity
                    > openTrade.OpenQuantity)
                {
                    error =
                        "ExitQuantityExceedsRecoveredPosition";

                    return false;
                }

                var averageEntry =
                    openTrade.EntryPriceQuantity
                    / openTrade.EntryQuantity;

                var points =
                    openTrade.Direction
                        == PendingDirection.Long
                            ? execution.Price - averageEntry
                            : averageEntry - execution.Price;

                openTrade.GrossPnl +=
                    points
                    * pointValue
                    * execution.Quantity;

                openTrade.OpenQuantity -=
                    execution.Quantity;

                if (openTrade.OpenQuantity == 0)
                {
                    snapshot.GrossPnl +=
                        openTrade.GrossPnl;

                    if (openTrade.GrossPnl > 0)
                        snapshot.Winners++;
                    else if (openTrade.GrossPnl < 0)
                        snapshot.Losses++;

                    openTrade =
                        new RecoveredOpenTrade();
                }
            }

            if (openTrade.OpenQuantity > 0)
            {
                error =
                    "RecoveredStrategyTradeStillOpen";

                return false;
            }

            if (snapshot.Winners
                    + snapshot.Losses
                > snapshot.Trades)
            {
                error =
                    "RecoveredCounterInvariantFailed";

                return false;
            }

            return true;
        }

        private void ApplyRecoveredLiveDailyState(
            LiveDailyStateSnapshot snapshot,
            DateTime eventTime,
            string source)
        {
            if (snapshot == null)
                return;

            activeTradingDate =
                snapshot.TradingDate.Date;

            tradesToday =
                snapshot.Trades;

            winnersToday =
                snapshot.Winners;

            lossesToday =
                snapshot.Losses;

            grossPnlToday =
                snapshot.GrossPnl;

            liveDailyStateRecoveryDate =
                snapshot.TradingDate.Date;

            liveDailyStateRecoveryComplete = true;
            liveDailyStateRecoveryBlockLogged = false;

            Diagnostic(
                eventTime,
                "LIVE DAILY STATE RECOVERED " +
                "Source={0} Date={1:yyyy-MM-dd} " +
                "Trades={2} Winners={3} Losses={4} GrossPnl={5:0.00}",
                source ?? string.Empty,
                snapshot.TradingDate,
                snapshot.Trades,
                snapshot.Winners,
                snapshot.Losses,
                snapshot.GrossPnl);
        }

        private static bool LiveDailyStatesEquivalent(
            LiveDailyStateSnapshot left,
            LiveDailyStateSnapshot right)
        {
            if (left == null || right == null)
                return false;

            return left.TradingDate.Date
                       == right.TradingDate.Date
                   && left.Trades == right.Trades
                   && left.Winners == right.Winners
                   && left.Losses == right.Losses
                   && Math.Abs(
                       left.GrossPnl - right.GrossPnl)
                       < 0.01;
        }

        private bool ShouldUseLiveDailyStateRecovery()
        {
            if (State != State.Realtime
                || Account == null)
            {
                return false;
            }

            try
            {
                var accountName =
                    Account.Name ?? string.Empty;

                if (accountName.StartsWith(
                        "Playback",
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                var connectionName =
                    Account.Connection != null
                    && Account.Connection.Options != null
                        ? Account.Connection.Options.Name
                        : string.Empty;

                if (!string.IsNullOrEmpty(connectionName)
                    && connectionName.IndexOf(
                        "Playback",
                        StringComparison.OrdinalIgnoreCase)
                        >= 0)
                {
                    return false;
                }
            }
            catch
            {
                // If this is a real-time strategy and the connection metadata
                // is temporarily unavailable, prefer the safer recovery path.
            }

            return true;
        }

        private static bool IsKnownRecoveryExitName(
            string orderName)
        {
            return string.Equals(
                       orderName,
                       "Stop loss",
                       StringComparison.Ordinal)
                   || string.Equals(
                       orderName,
                       "Profit target",
                       StringComparison.Ordinal)
                   || string.Equals(
                       orderName,
                       LongEodExitSignal,
                       StringComparison.Ordinal)
                   || string.Equals(
                       orderName,
                       ShortEodExitSignal,
                       StringComparison.Ordinal)
                   || string.Equals(
                       orderName,
                       LongTimeExitSignal,
                       StringComparison.Ordinal)
                   || string.Equals(
                       orderName,
                       ShortTimeExitSignal,
                       StringComparison.Ordinal);
        }

        private DateTime GetCurrentStrategyTimeForRecovery()
        {
            try
            {
                if (CurrentBars != null
                    && CurrentBars.Length > TickSeriesIndex
                    && CurrentBars[TickSeriesIndex] >= 0)
                {
                    return Times[TickSeriesIndex][0];
                }

                if (CurrentBars != null
                    && CurrentBars.Length > SignalSeriesIndex
                    && CurrentBars[SignalSeriesIndex] >= 0)
                {
                    return Times[SignalSeriesIndex][0];
                }

                if (CurrentBars != null
                    && CurrentBars.Length > ContextSeriesIndex
                    && CurrentBars[ContextSeriesIndex] >= 0)
                {
                    return Times[ContextSeriesIndex][0];
                }
            }
            catch
            {
                // Fall through to wall-clock time only if no strategy data
                // timestamp is currently available.
            }

            return DateTime.Now;
        }

        private string BuildLiveDailyStatePath()
        {
            try
            {
                var directory =
                    Path.Combine(
                        Core.Globals.UserDataDir,
                        "NinjexState",
                        "OvernightEdgePortfolio");

                var fileName =
                    "daily_state_"
                    + SanitizeRecoveryPathPart(
                        GetRecoveryAccountName())
                    + "_"
                    + SanitizeRecoveryPathPart(
                        GetRecoveryInstrumentName())
                    + ".state";

                return Path.Combine(
                    directory,
                    fileName);
            }
            catch
            {
                return string.Empty;
            }
        }

        private string GetRecoveryAccountName()
        {
            return Account != null
                ? Account.Name ?? string.Empty
                : string.Empty;
        }

        private string GetRecoveryInstrumentName()
        {
            return Instrument != null
                ? Instrument.FullName ?? string.Empty
                : string.Empty;
        }

        private static string SanitizeRecoveryPathPart(
            string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            foreach (var invalid in
                     Path.GetInvalidFileNameChars())
            {
                value =
                    value.Replace(
                        invalid,
                        '_');
            }

            return value;
        }

        private void LogLiveDailyStateRecoveryBlock(
            DateTime eventTime,
            bool logReason,
            string reason)
        {
            if (!logReason
                && liveDailyStateRecoveryBlockLogged)
            {
                return;
            }

            liveDailyStateRecoveryBlockLogged = true;

            Diagnostic(
                eventTime,
                "TRADE BLOCK Reason=LiveDailyStateRecovery " +
                "Detail={0} Date={1:yyyy-MM-dd}",
                reason ?? string.Empty,
                liveDailyStateRecoveryDate);
        }
    }
}
