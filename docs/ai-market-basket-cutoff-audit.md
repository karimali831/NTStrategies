# Full ES basket audit when earlier pilot exports share a dataset tag

The raw exporter creates unique folder names using UTC timestamps,
so existing files can remain untouched even if the user accidentally
reused the same Dataset tag for a later 13-contract run.

Use the batch auditor's optional --run-after-utc to include only
export folders created at or after a specified UTC instant. It selects
by the timestamp in the **original exporter folder name**, not the
filesystem modification time, which can change after copying.

For the 26 November 2026 full basket captured around 13:36-13:37 UTC:

    python tools/ai_market_batch_audit.py --root "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research" --tag "YOUR_MANIFEST_DATASET_TAG" --from-et "2023-09-10" --through-et "2026-11-25" --run-after-utc "2026-11-26T13:35:00Z" --calendars tools/ai_market_calendars --allow-sparse-1m --output "C:\Users\Administrator\Documents\Ninjex\AiMarket\Research\es-full-basket-audit.json"

The report includes runAfterUtc and invalidFolderTimestamps. A folder
with an unrecognized export timestamp is excluded and reported, not
silently used. Duplicate contracts **within** the cutoff still fail
rather than choosing an arbitrary run.

The full multi-year calendar is not yet exhaustive: missing time on
2024-2026 holidays will be listed as exceptions until documented.
This is expected and must not be confused with a healthy or
corrupted data series before comparison to official market hours.

The isolated February 2, 2024 at 16:25 ET 5m absence remains a data
quality exception, and its affected short-horizon ML windows require
masking; never fabricate data to satisfy the report.
