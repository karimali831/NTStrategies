# Overnight Edge — persistent daily diagnostics

This feature is observational. It does not change orders, signals, eligibility, replay fills or risk limits.

## Location

Logs are written under the NinjaTrader 8 **user data** directory (usually `Documents\NinjaTrader 8`), not the Git checkout:

```text
NinjexLogs\OvernightEdgePortfolio\<strategy-version>\<event-date>\
  OvernightEdge_<strategy-version>_<instrument>_<account>_<event-date>_<run-id>.log
```

Example (illustrative):

```text
Documents\NinjaTrader 8\NinjexLogs\OvernightEdgePortfolio\1.4.0-run8-production\2026-10-14\
  OvernightEdge_1.4.0-run8-production_ES_12-26_PA-123_2026-10-14_a12bc3456d78.log
```

- UTF-8, append-only, flushed after **every line**.
- Every strategy instance receives a unique run ID. Restarts create separate files, preserving the previous session instead of overwriting it. Different charts, instruments and accounts do not contend for the same file.
- Filename date is based on the diagnostic **event date**; historical playback therefore produces files for the historical dates. Event timestamps use NinjaTrader's chart timezone (ET in this project). Startup `READY` lines use the Windows machine clock and may appear under the machine's local date.
- When an event date changes, the strategy closes the prior writer and creates/opens that date's file. A holiday or no-output date does not create an empty log.
- Each file has an identifying header with version, instance ID, contract, account and creation time in UTC.
- `Enable Daily File Logging` defaults **on**, independent from `Enable Diagnostics` (the latter controls only NinjaScript Output).
- If disk writes fail, one warning is sent to NinjaScript Output and NT Log, and further writes for that instance stop. **Trading and risk logic keep running.** If logging is mandatory for your risk policy, monitor for this warning externally.

## Validation on a simulation VPS

1. Pull the changes and compile the NinjaTrader strategy, ensuring `DailyFileLogging.cs` is compiled (it is explicitly included in `NTStrategies.csproj`).
2. Enable strategy in a simulation account; verify `Enable Daily File Logging = True`.
3. Verify `READY Version=...`, `OVERNIGHT READY`, `FOUR CHECK`, `SIGNAL BLOCK` and execution diagnostics are appended to a .log file under the directory above.
4. Stop the strategy and start it again. Check that the first log file is intact and a new run-ID log appears.
5. Run playback across midnight ET. Verify one dated folder/file group per event date. Disable `Enable Diagnostics` while leaving `Enable Daily File Logging` on; file output should continue.
6. Compare number/content of diagnostic lines against NinjaScript Output before deploying.

## Operational notes

The logger writes *existing diagnostics*: it cannot reconstruct output lost before this version was installed, nor any event that did not call `Diagnostic`. It does not replace broker execution records, the NinjaTrader native log, or live daily-state recovery.

Over long playback runs file logging produces many files; turn it off for performance-sensitive bulk replays once diagnostic capture is no longer needed. Files are not automatically deleted; archive/retain them under your own policy.

**Do not deploy uncompiled or untested source to funded accounts.** Keep the known stable binary until simulation checks pass.
