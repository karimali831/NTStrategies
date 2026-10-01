using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using NinjaTrader.Cbi;

namespace NinjaTrader.NinjaScript.Strategies
{
    public partial class NinjexOvernightEdgePortfolio
    {
        private const string LiveDailyStateSnapshotVersion = "2";

        private bool liveDailyStateRecoveryComplete = true;
        private bool liveDailyStateRecoveryBlockLogged;
        private DateTime liveDailyStateRecoveryDate = Core.Globals.MinDate;
        private string liveDailyStatePath = string.Empty;

        private sealed class LiveDailyStateSnapshot
        {
            public DateTime TradingDate = Core.Globals.MinDate;
            public int Trades;
            public int Winners;
            public int Losses;
            public double GrossPnl;
            public bool OpenTrade;
        }

        private sealed class LiveRecoveryOpenTrade
        {
            public string EntrySignal = string.Empty;
            public PendingDirection Direction = PendingDirection.None;
            public int OpenQuantity;
            public int EntryQuantity;
            public double EntryPriceQuantity;
            public double GrossPnl;
        }

        /// <summary>
        /// Called when NinjaTrader transitions the strategy from historical
        /// processing to real time. The first real-time tick will perform the
        /// actual recovery using the strategy's ET data timestamp.
        /// </summary>
        private void ResetLiveDailyStateRecoveryOnRealtimeTransition()
        {
            liveDailyStateRecoveryComplete = false;
            liveDailyStateRecoveryBlockLogged = false;
            liveDailyStateRecoveryDate = Core.Globals.MinDate;
            liveDailyStatePath = string.Empty;
        }

        /// <summary>
        /// Ensures the daily portfolio counters are authoritative before live
        /// entries can be evaluated. Historical processing and Playback are
        /// deliberately ignored: uninterrupted Market Replay remains identical
        /// to Run 4 / Run 5 and never reads or writes persistent live state.
        /// </summary>
        private bool EnsureLiveDailyStateReady(DateTime eventTime)
        {
            if (!ShouldUseLiveDailyStateRecovery())
                return true;

            var tradingDate = eventTime.Date;

            if (liveDailyStateRecoveryComplete
                && liveDailyStateRecoveryDate == tradingDate)
            {
                return true;
            }

            liveDailyStateRecoveryComplete = false;
            liveDailyStateRecoveryDate = tradingDate;
            liveDailyStatePath = BuildLiveDailyStatePath();

            LiveDailyStateSnapshot persisted;
            if (TryReadLiveDailyStateSnapshot(tradingDate, out persisted))
            {
                // A snapshot written while a strategy trade was open is not
                // sufficient by itself. Reconcile it from broker executions;
                // otherwise fail closed for the remainder of the uncertainty.
                if (persisted.OpenTrade)
                {
                    LiveDailyStateSnapshot reconciledOpen;
                    string reconcileOpenError;
                    if (!TryRebuildLiveDailyStateFromAccountExecutions(
                            tradingDate,
                            out reconciledOpen,
                            out reconcileOpenError))
                    {
                        return FailClosedLiveDailyState(
                            eventTime,
                            "PersistedOpenTrade:" + reconcileOpenError);
                    }

                    ApplyRecoveredLiveDailyState(
                        reconciledOpen,
                        eventTime,
                        "AccountExecutionsAfterOpenSnapshot");

                    PersistLiveDailyState(
                        eventTime,
                        false,
                        "RecoveredOpenTrade");

                    return true;
                }

                // If the broker execution collection is already populated and
                // contains newer completed strategy trades, prefer it. Never
                // downgrade a durable snapshot merely because executions have
                // not yet repopulated after reconnect.
                LiveDailyStateSnapshot accountState;
                string accountError;
                if (TryRebuildLiveDailyStateFromAccountExecutions(
                        tradingDate,
                        out accountState,
                        out accountError))
                {
                    if (accountState.Trades > persisted.Trades)
                    {
                        ApplyRecoveredLiveDailyState(
                            accountState,
                            eventTime,
                            "AccountExecutionsNewer");

                        PersistLiveDailyState(
                            eventTime,
                            false,
                            "RecoveredFromNewerExecutions");

                        return true;
                    }

                    if (accountState.Trades == persisted.Trades
                        && accountState.Trades > 0
                        && !LiveDailyStatesEquivalent(
                            persisted,
                            accountState))
                    {
                        return FailClosedLiveDailyState(
                            eventTime,
                            "PersistedAccountMismatch");
                    }
                }

                ApplyRecoveredLiveDailyState(
                    persisted,
                    eventTime,
                    "PersistedSnapshot");

                return true;
            }

            // First deployment during an already-active day can still recover
            // from NinjaTrader's live account execution collection.
            LiveDailyStateSnapshot rebuilt;
            string rebuildError;
            if (TryRebuildLiveDailyStateFromAccountExecutions(
                    tradingDate,
                    out rebuilt,
                    out rebuildError)
                && rebuilt.Trades > 0)
            {
                ApplyRecoveredLiveDailyState(
                    rebuilt,
                    eventTime,
                    "AccountExecutions");

                PersistLiveDailyState(
                    eventTime,
                    false,
                    "InitialRecoveryFromExecutions");

                return true;
            }

            // A strategy that has been running before the entry window can
            // safely establish the day's zero baseline. Once the entry window
            // has opened, an absent snapshot plus zero visible executions is
            // ambiguous after a restart, so fail closed rather than assume zero.
            if (ToTimeValue(eventTime) < EntryStartTime)
            {
                var zero = new LiveDailyStateSnapshot
                {
                    TradingDate = tradingDate,
                    Trades = 0,
                    Winners = 0,
                    Losses = 0,
                    GrossPnl = 0,
                    OpenTrade = false
                };

                ApplyRecoveredLiveDailyState(
                    zero,
                    eventTime,
                    "NewDayZero");

                PersistLiveDailyState(
                    eventTime,
                    false,
                    "NewDayZero");

                return true;
            }

            return FailClosedLiveDailyState(
                eventTime,
                string.IsNullOrEmpty(rebuildError)
                    ? "NoSnapshotAfterEntryStart"
                    : "NoSnapshotAfterEntryStart:" + rebuildError);
        }

        private bool FailClosedLiveDailyState(
            DateTime eventTime,
            string reason)
        {
            liveDailyStateRecoveryComplete = false;

            if (!liveDailyStateRecoveryBlockLogged)
            {
                liveDailyStateRecoveryBlockLogged = true;

                Diagnostic(
                    eventTime,
                    "LIVE DAILY STATE RECOVERY FAILED " +
                    "Date={0:yyyy-MM-dd} Reason={1}",
                    eventTime.Date,
                    reason ?? string.Empty);
            }

            return false;
        }

        private void ApplyRecoveredLiveDailyState(
            LiveDailyStateSnapshot snapshot,
            DateTime eventTime,
            string source)
        {
            if (snapshot == null)
                return;

            activeTradingDate = snapshot.TradingDate.Date;
            tradesToday = snapshot.Trades;
            winnersToday = snapshot.Winners;
            lossesToday = snapshot.Losses;
            grossPnlToday = snapshot.GrossPnl;

            liveDailyStateRecoveryDate = snapshot.TradingDate.Date;
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

        /// <summary>
        /// Persist the authoritative live counters after entry and completion.
        /// The OpenTrade flag prevents a crash/restart during a position from
        /// silently restoring incomplete winner/loss state.
        /// </summary>
        private void PersistLiveDailyState(
            DateTime eventTime,
            bool openTrade,
            string reason)
        {
            if (!ShouldUseLiveDailyStateRecovery()
                || activeTradingDate == Core.Globals.MinDate)
            {
                return;
            }

            try
            {
                if (string.IsNullOrEmpty(liveDailyStatePath))
                    liveDailyStatePath = BuildLiveDailyStatePath();

                if (string.IsNullOrEmpty(liveDailyStatePath))
                    return;

                var directory = Path.GetDirectoryName(liveDailyStatePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                var lines = new[]
                {
                    "Version=" + LiveDailyStateSnapshotVersion,
                    "TradingDate=" + activeTradingDate.ToString(
                        "yyyy-MM-dd",
                        CultureInfo.InvariantCulture),
                    "Account=" + GetLiveRecoveryAccountName(),
                    "Instrument=" + GetLiveRecoveryInstrumentName(),
                    "Trades=" + tradesToday.ToString(CultureInfo.InvariantCulture),
                    "Winners=" + winnersToday.ToString(CultureInfo.InvariantCulture),
                    "Losses=" + lossesToday.ToString(CultureInfo.InvariantCulture),
                    "GrossPnl=" + grossPnlToday.ToString(
                        "0.########",
                        CultureInfo.InvariantCulture),
                    "OpenTrade=" + (openTrade ? "1" : "0"),
                    "UpdatedUtc=" + DateTime.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture),
                    "Reason=" + (reason ?? string.Empty)
                };

                var tempPath = liveDailyStatePath + ".tmp";
                File.WriteAllLines(tempPath, lines);

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
                        File.Delete(tempPath);
                    }
                }
                else
                {
                    File.Move(
                        tempPath,
                        liveDailyStatePath);
                }

                liveDailyStateRecoveryDate = activeTradingDate.Date;
                liveDailyStateRecoveryComplete = true;

                Diagnostic(
                    eventTime,
                    "LIVE DAILY STATE PERSIST " +
                    "Reason={0} Date={1:yyyy-MM-dd} " +
                    "Trades={2} Winners={3} Losses={4} " +
                    "GrossPnl={5:0.00} OpenTrade={6}",
                    reason ?? string.Empty,
                    activeTradingDate,
                    tradesToday,
                    winnersToday,
                    lossesToday,
                    grossPnlToday,
                    openTrade);
            }
            catch (Exception ex)
            {
                // Persistence failure is safety relevant. Mark recovery
                // incomplete so the next trade check fails closed.
                liveDailyStateRecoveryComplete = false;

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

                foreach (var line in File.ReadAllLines(liveDailyStatePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var separator = line.IndexOf('=');
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
                string openTradeText;

                if (!values.TryGetValue("Version", out version)
                    || version != LiveDailyStateSnapshotVersion
                    || !values.TryGetValue("TradingDate", out dateText)
                    || !values.TryGetValue("Account", out account)
                    || !values.TryGetValue("Instrument", out instrument)
                    || !values.TryGetValue("Trades", out tradesText)
                    || !values.TryGetValue("Winners", out winnersText)
                    || !values.TryGetValue("Losses", out lossesText)
                    || !values.TryGetValue("GrossPnl", out grossText)
                    || !values.TryGetValue("OpenTrade", out openTradeText))
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
                        GetLiveRecoveryAccountName(),
                        StringComparison.Ordinal)
                    || !string.Equals(
                        instrument,
                        GetLiveRecoveryInstrumentName(),
                        StringComparison.OrdinalIgnoreCase)
                    || trades < 0
                    || winners < 0
                    || losses < 0
                    || winners + losses > trades)
                {
                    return false;
                }

                snapshot = new LiveDailyStateSnapshot
                {
                    TradingDate = date.Date,
                    Trades = trades,
                    Winners = winners,
                    Losses = losses,
                    GrossPnl = gross,
                    OpenTrade = openTradeText == "1"
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
            snapshot = new LiveDailyStateSnapshot
            {
                TradingDate = tradingDate.Date
            };

            error = string.Empty;

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

            var relevant = new List<Execution>();

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
                            || execution.Time.Date != tradingDate.Date
                            || !string.Equals(
                                execution.Instrument.FullName,
                                Instrument.FullName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var fromEntry =
                            execution.Order.FromEntrySignal
                            ?? string.Empty;

                        if (IsEntrySignalName(execution.Order.Name)
                            || IsEntrySignalName(fromEntry)
                            || IsKnownLiveRecoveryExitName(
                                execution.Order.Name))
                        {
                            relevant.Add(execution);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "AccountExecutionsReadError:" + ex.Message;
                return false;
            }

            relevant.Sort(
                delegate(Execution left, Execution right)
                {
                    var compare =
                        left.Time.CompareTo(right.Time);

                    if (compare != 0)
                        return compare;

                    return string.Compare(
                        left.ExecutionId ?? string.Empty,
                        right.ExecutionId ?? string.Empty,
                        StringComparison.Ordinal);
                });

            var openTrade = new LiveRecoveryOpenTrade();
            var pointValue =
                Instrument.MasterInstrument.PointValue;

            foreach (var execution in relevant)
            {
                var order = execution.Order;

                if (IsEntrySignalName(order.Name))
                {
                    var direction =
                        IsLongEntrySignalName(order.Name)
                            ? PendingDirection.Long
                            : PendingDirection.Short;

                    if (openTrade.OpenQuantity <= 0)
                    {
                        openTrade = new LiveRecoveryOpenTrade
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
                        error = "OverlappingStrategyEntries";
                        return false;
                    }

                    openTrade.OpenQuantity += execution.Quantity;
                    openTrade.EntryQuantity += execution.Quantity;
                    openTrade.EntryPriceQuantity +=
                        execution.Price * execution.Quantity;

                    continue;
                }

                var fromEntrySignal =
                    order.FromEntrySignal
                    ?? string.Empty;

                var looksLikeExit =
                    IsEntrySignalName(fromEntrySignal)
                    || IsKnownLiveRecoveryExitName(order.Name);

                if (!looksLikeExit)
                    continue;

                if (openTrade.OpenQuantity <= 0
                    || openTrade.EntryQuantity <= 0)
                {
                    error = "ExitWithoutRecoverableEntry";
                    return false;
                }

                if (!string.IsNullOrEmpty(fromEntrySignal)
                    && IsEntrySignalName(fromEntrySignal)
                    && !string.Equals(
                        fromEntrySignal,
                        openTrade.EntrySignal,
                        StringComparison.Ordinal))
                {
                    error = "ExitEntrySignalMismatch";
                    return false;
                }

                if (execution.Quantity > openTrade.OpenQuantity)
                {
                    error = "ExitQuantityExceedsRecoveredPosition";
                    return false;
                }

                var averageEntry =
                    openTrade.EntryPriceQuantity
                    / openTrade.EntryQuantity;

                var points =
                    openTrade.Direction == PendingDirection.Long
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
                        new LiveRecoveryOpenTrade();
                }
            }

            if (openTrade.OpenQuantity > 0)
            {
                error = "RecoveredStrategyTradeStillOpen";
                return false;
            }

            if (snapshot.Winners + snapshot.Losses
                > snapshot.Trades)
            {
                error = "RecoveredCounterInvariantFailed";
                return false;
            }

            snapshot.OpenTrade = false;
            return true;
        }

        private static bool LiveDailyStatesEquivalent(
            LiveDailyStateSnapshot left,
            LiveDailyStateSnapshot right)
        {
            return left != null
                   && right != null
                   && left.TradingDate.Date
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
                        StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return false;
                }
            }
            catch
            {
                // If connection metadata is temporarily unavailable on a
                // genuinely real-time strategy, prefer the safer recovery path.
            }

            return true;
        }

        private static bool IsKnownLiveRecoveryExitName(
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
                    + SanitizeLiveStateFileName(
                        GetLiveRecoveryAccountName())
                    + "_"
                    + SanitizeLiveStateFileName(
                        GetLiveRecoveryInstrumentName())
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

        private string GetLiveRecoveryAccountName()
        {
            return Account == null
                ? string.Empty
                : Account.Name ?? string.Empty;
        }

        private string GetLiveRecoveryInstrumentName()
        {
            return Instrument == null
                ? string.Empty
                : Instrument.FullName ?? string.Empty;
        }

        private static string SanitizeLiveStateFileName(
            string value)
        {
            var result = value ?? string.Empty;

            foreach (var invalid in Path.GetInvalidFileNameChars())
                result = result.Replace(invalid, '_');

            return result.Replace(' ', '_');
        }
    }
}
