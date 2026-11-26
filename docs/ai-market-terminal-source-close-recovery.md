# Ninjex AI — terminal historical close recovery (experimental)

Problem: On ES 12-23 the Strategy Analyzer historical exporter emitted the
last available bar at 2023-12-08 16:59 ET (1m), 16:55 ET (5m),
16:45 ET (15m) and 16:00 ET (60m), while NinjaTrader's stored historical
charts and 1m/tick exports included the real 17:00 ET session-closing bars.

This branch adds an **opt-in** `Recover source 17:00 close` strategy parameter.
Default is **FALSE** until verified in Windows NinjaTrader 8.

At `State.Terminated`, the exporter inspects the actual, loaded
`BarsArray[0..3]` using absolute `GetTime/GetOpen/GetHigh/GetLow/GetClose/GetVolume`
methods. It walks backward past any later Analyzer dates, then checks
that the final bar on the configured `Through ET` date is exactly 17:00 ET.
If present and not already exported, it appends the original values to
the CSV with flag `TERMINAL_FROM_LOADED_BARS`. The manifest records
`terminalRecoveredCounts` separately by timeframe.

It does NOT invent bars, manufacture volume, alter the historical source
database or apply a correction table. On missing data, invalid timestamps,
an API exception, a partial day or a holiday early-close, it logs a
diagnostic and makes no replacement. Only ordinary 17:00 ET closes
qualify; separate calendar-based treatment is needed for early closes.

## Minimal Windows verification — do this BEFORE another full-quarter export

- Pull this PR, compile NinjaScript on Windows.
- Strategy Analyzer: `ES 12-23`, 5 Minute, Last,
  CME US Index Futures ETH, Order fill resolution Standard (Fastest).
- Historical chart/editor timezone must be Eastern Time.
- Analyzer start 2023-12-08, end 2023-12-11 (extra day for full
  historical loading).
- Exporter From ET `2023-12-08`, Through ET `2023-12-08`;
  Enable export = true; **Recover source 17:00 close = true**.
- Run, inspect Output for four `Terminal Nm: ... RECOVERED_FROM_LOADED_BARS`
  messages and a nonzero `terminalRecoveredCounts` manifest.
  If the runtime lacks accessible BarsArray, it will report `unavailable`;
  do not silently conclude the source was incomplete.
- Compare four 17:00 ET bars with the verified NinjaTrader Data Box:
  1m volume 12,208; 5m 33,482; 15m 35,305; 60m 78,892.
- Inspect each row's `TERMINAL_FROM_LOADED_BARS` provenance flag.
  Do NOT merge the previously generated provisional correction CSV.

This only addresses loaded source candles at the configured final
17:00 close. It does not change sparse 1-minute bar policy, which must
remain auditable and distinct from lost traded volume. Keep raw
exports immutable and generate research-ready datasets separately.

Portable compilation and regression tests cover source-only recovery,
UTC and duplicate prevention, but Windows NT8 runtime verification is
still required.
