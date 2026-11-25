# AI Market Publisher · reliable accelerated Playback v1

**Never places orders.** Works with the corresponding NinjexApp
\`observe-batch\` endpoint. Update **both** repositories before using.

## Configure
- Use 5-minute ES 12-26 chart, CME US Index Futures ETH.
- NinjaTrader Tools > Options > General > time zone = Eastern Time.
- Set matching 32+ character \`NINJEX_AI_PUBLISHER_KEY\` in Windows user environment.
- Set strategy Backend URL = \`https://localhost:7016\` for local Rider development.
- Enable publishing = true. Strategy Enabled = true.
- Load enough history for 100 completed bars each of 5m/15m/60m.

## What changes at high speed
On each completed 5m bar, the NinjaScript strategy serializes 100
historical bars per frame and **writes one JSON record to disk before
returning from OnBarUpdate**. The background uploader sends up to ten
observations per HTTPS POST. Slow, failing or disconnected HTTP requests
cannot directly cause a candle to be dropped by the network publisher.
Playback may slow to the disk/serialization throughput; this is intentional
backpressure rather than silent data loss.

Spools are saved under:
\`%LOCALAPPDATA%\Ninjex\AiMarket\Spool\localhost_7016\`
(or the folder corresponding to the configured backend URL).
The publisher prints the actual location on startup. Each session/day
has a JSONL file and a separate \`.ack\` count checkpoint. Pending
records from an interrupted run are retried when a new publisher
instance starts. Backend duplicates are counted but not inserted again.

**Do not delete spool files before auditing the 2-day run.** Once the
backend has acknowledged all records and SQL counts have been verified,
the processed files may be archived outside the live spool folder.

## Recommended first verification
1. Compile strategy locally (Windows NT8; not built in Linux GitHub CI).
2. Play October 1–2 2026 at 1x-5x; check chart closes and UTC→ET.
3. Repeat a *fresh, nonoverlapping interval* at 50x, then 500x, then Max.
   Old server records will deduplicate if you replay the exact same interval;
   use spool counts and server duplicates to distinguish reruns.
4. Check \`CAPTURE FAILED\`, \`Batch HTTP\`, pending upload and SQL.
5. Stop Playback and wait until pending=0 with NinjaTrader still running;
   after stopping the strategy the uploader stops and resumes on the
   next activation. If pending never decreases, inspect failed HTTP logs.
6. Verify individual eligible 5m close timestamps against the chart,
   respecting CME daily breaks and all higher-frame end timestamps.

**No guarantees at Max without empirical validation.** 3-year ML history
still needs an incremental OHLCV research exporter rather than storing
three overlapping 100-bar windows for every 5-minute observation.


## Session-boundary recovery (November 2026)

The 2026-10-01 17:00 ET session boundary caused the API to reject stale
or future higher-timeframe frames. The previous batch endpoint rejected
the entire batch, blocking all later stored records. Updated code pairs:

* NinjexApp API: validates items individually, returns \`rejected\`
  and indexed \`rejections\` with the exact 5m and higher-frame timestamps.
* NT publisher: chooses the latest 15m/60m frame ending no later than the
  current 5m close. If nothing sufficiently fresh exists, it writes an
  explicit entry under \`Spool/<host>/Skipped\`, logs a skip and does NOT
  manufacture a market classification for that bar.
* Existing bad JSONL observations: are uploaded again; the backend
  quarantines invalid entries individually, while valid newer items
  are allowed through. Each raw invalid observation and reason are
  preserved under \`Spool/<host>/Quarantine\` **before** local ACK advances.

Review \`skippedFrameUnavailable\`, \`rejectedQuarantined\`,
\`captureFailures\` and \`pendingUpload\` separately. Only successful
server insertions/duplicates contribute to observation completeness.
Never delete original spool files before auditing.

**Recovery instructions:** Stop the old NT publisher; retain/bakcup the
current \`%LOCALAPPDATA%/Ninjex/AiMarket/Spool/localhost_7016\`.
Update and restart the **backend first**. Update NinjaScript and recompile,
then enable it on a chart. Previously pending files will be retried; they
may include invalid rows in Quarantine. Inspect those reasons and only
then rerun the skipped part of Playback on the updated publisher.
Do not run simultaneous publisher instances pointing to the same spool.
