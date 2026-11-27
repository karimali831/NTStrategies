#!/usr/bin/env python3
"""Ninjex AI v3: TRAIN-ONLY, predefined ES bracket geometry study.

Compares fixed 12/24 ticks at 30m with ATR 0.75/1.50 at 30m and
ATR 1.0/2.0 at 60m. One matched hourly entry per dated contract,
both LONG and SHORT at identical timestamps. Not a strategy, ML model,
independent validation or tick-accurate trade simulation.

Only feature rows with researchSplit=train are scored; previous
validation and June-2026+ holdout remain unscored. No source files
are modified. Conservative stop-first if one minute hits both.
"""
import argparse
import datetime as dt
import json
import math
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np
import pandas as pd

if __package__:
    from tools.ai_market_ml_features import NY, read_bars
    from tools.ai_market_bracket_research import close_time, summarize
    from tools.ai_market_train_baseline import read_verified_csv
else:
    from ai_market_ml_features import NY, read_bars
    from ai_market_bracket_research import close_time, summarize
    from ai_market_train_baseline import read_verified_csv

TICK = 0.25
POINT_VALUE = 50.0
SLIPPAGE = 1
COMMISSION = 5.0
TRAIN_END = dt.date(2025, 9, 1)
TRAIN_CHALLENGE = dt.date(2024, 9, 1)
MAX_STOP_TICKS = 16
POLICIES = (
    ("fixed_12_24_30m", None, None, 30),
    ("atr_075_150_30m", .75, 1.5, 30),
    ("atr_100_200_60m", 1., 2., 60),
)


def tick_round(points):
    if not math.isfinite(points) or points <= 0:
        raise ValueError("ATR distance must be positive and finite")
    return int(math.ceil(points / TICK - 1e-10))


def bracket(policy, atr):
    _, stop_mult, target_mult, horizon = policy
    if stop_mult is None:
        return (12, 24, horizon)
    stop = max(8, tick_round(atr * stop_mult))
    target = tick_round(atr * target_mult)
    return None if stop > MAX_STOP_TICKS else (stop, target, horizon)


def future_bars(source, close):
    times = (close + dt.timedelta(minutes=i) for i in range(1, 61))
    rows = [source.get(t) for t in times]
    return None if any(x is None or "INVALID" in x.flags for x in rows) else rows


def simulate(bars, side, stop_ticks, target_ticks, horizon):
    if side not in (1, -1) or len(bars) < horizon:
        raise ValueError("Invalid side or future one-minute path")
    raw = bars[0].open
    entry = raw + side * SLIPPAGE * TICK
    stop = raw - side * stop_ticks * TICK
    target = raw + side * target_ticks * TICK
    exit_price = bars[horizon - 1].close
    reason = "TIME"
    ambiguous = False
    for b in bars[:horizon]:
        stop_open = b.open <= stop if side == 1 else b.open >= stop
        target_open = b.open >= target if side == 1 else b.open <= target
        stop_hit = b.low <= stop if side == 1 else b.high >= stop
        target_hit = b.high >= target if side == 1 else b.low <= target
        if stop_open:
            exit_price, reason = b.open, "STOP_GAP"
        elif target_open:
            exit_price, reason = target, "TARGET"
        elif stop_hit and target_hit:
            exit_price, reason, ambiguous = stop, "AMBIGUOUS_STOP_FIRST", True
        elif stop_hit:
            exit_price, reason = stop, "STOP"
        elif target_hit:
            exit_price, reason = target, "TARGET"
        else:
            continue
        break
    pnl = side * (exit_price - side * SLIPPAGE * TICK - entry) * POINT_VALUE - COMMISSION
    return {"netUsd": round(pnl, 2), "exitReason": reason, "bothTouched1m": ambiguous}


def train_hourly(df):
    train = df.loc[df["researchSplit"] == "train"].copy()
    utc = pd.to_datetime(train["decisionUtc"], utc=True)
    dates = utc.dt.tz_convert(NY).dt.date
    if any(x >= TRAIN_END for x in dates):
        raise ValueError("Train split contains later market data")
    valid = ((utc.dt.minute == 0) &
             (pd.to_numeric(train["fine1mComplete"], errors="coerce") == 1) &
             (train["qualityFlags"].fillna("") == "OK"))
    return train.loc[valid].copy()


def session(row):
    if int(row["sessionLondon"]) and int(row["sessionNewYork"]):
        return "LONDON_NY_OVERLAP"
    if int(row["sessionNewYork"]):
        return "NEW_YORK"
    if int(row["sessionLondon"]):
        return "LONDON"
    if int(row["sessionAsia"]):
        return "ASIA"
    return "OTHER"


def summary(rows):
    stats = summarize(rows)
    stats["targetRate"] = (round(sum(r["exitReason"] == "TARGET" for r in rows) / len(rows), 5)
                           if rows else None)
    return stats


def perform(features, audit, output):
    meta = json.loads(features.with_suffix(".manifest.json").read_text("utf-8-sig"))
    basket = json.loads(audit.read_text("utf-8-sig"))
    if basket.get("schema") != "ninjex-basket-audit-v1" or basket.get("datasetTag") != meta.get("datasetTag"):
        raise ValueError("Mismatched source provenance")
    data = read_verified_csv(features, meta)
    ignored = {key: int((data["researchSplit"] == key).sum()) for key in ("validation", "holdout")}
    train = train_hourly(data)
    del data
    if train.empty:
        raise ValueError("No train-only hourly samples")
    early_times = pd.to_datetime(train["decisionUtc"], utc=True).dt.tz_convert(NY).dt.date
    early = train.loc[early_times < TRAIN_CHALLENGE]
    if len(early) < 100:
        raise ValueError("Insufficient early-training volatility reference data")
    ratio = (pd.to_numeric(early["atr14_5m"]) / pd.to_numeric(early["close"])).to_numpy(float)
    cuts = np.nanquantile(ratio, [1/3, 2/3])
    if not np.all(np.isfinite(cuts)):
        raise ValueError("Invalid reference volatility")
    folders = {x["contract"]: Path(x["folders"][0]) for x in basket["contracts"]
               if len(x.get("folders", [])) == 1}
    results = defaultdict(list)
    excluded = Counter()
    for contract, group in train.groupby("contract", sort=True):
        if contract not in folders:
            raise ValueError("Missing dated-contract export " + contract)
        folder = folders[contract]
        raw_meta = json.loads((folder / "manifest.json").read_text("utf-8-sig"))
        if raw_meta.get("contract") != contract or raw_meta.get("datasetTag") != meta["datasetTag"]:
            raise ValueError("Wrong source contract or tag")
        source = {b.end: b for b in read_bars(folder, 1, contract)}
        for _, row in group.iterrows():
            t = close_time(row["decisionUtc"])
            et_date = t.astimezone(NY).date()
            if (t + dt.timedelta(minutes=60)).astimezone(NY).date() >= TRAIN_END:
                excluded["crossSplit"] += 1
                continue
            atr = float(row["atr14_5m"])
            if not math.isfinite(atr) or atr <= 0:
                excluded["invalidATR"] += 1
                continue
            configs = [bracket(p, atr) for p in POLICIES]
            if any(c is None for c in configs):
                excluded["riskCap"] += 1
                continue
            future = future_bars(source, t)
            if future is None:
                excluded["missingFuture1m"] += 1
                continue
            period = "EARLY_TRAIN" if et_date < TRAIN_CHALLENGE else "LATE_TRAIN"
            rvol = atr / float(row["close"])
            vol = "LOW" if rvol <= cuts[0] else "MID" if rvol <= cuts[1] else "HIGH"
            props = {"decisionUtc": t.isoformat(), "month": str(et_date)[:7],
                     "contract": contract, "period": period, "session": session(row),
                     "volatility": vol}
            for policy, (stop, target, horizon) in zip(POLICIES, configs):
                for side, direction in ((1, "LONG"), (-1, "SHORT")):
                    result = simulate(future, side, stop, target, horizon)
                    results[policy[0] + "_" + direction].append({**props, **result})
    if not results or len({len(items) for items in results.values()}) != 1:
        raise ValueError("No matched results or source policies disagree")
    n = len(next(iter(results.values())))
    output.mkdir(parents=True, exist_ok=True)
    report = {
        "schema": "ninjex-ai-atr-landscape-v3",
        "status": "TRAIN_ONLY_DIAGNOSTIC_NOT_PROVEN_EDGE",
        "sourceFeatures": str(features), "sourceAudit": str(audit),
        "validationRowsNotScored": ignored["validation"],
        "holdoutRowsNotScored": ignored["holdout"],
        "trainHourlyCandidates": len(train), "matchedDecisionTimestamps": n,
        "excluded": dict(excluded),
        "volatilityCutsFromEarlyTraining": [float(x) for x in cuts],
        "policies": [{"name": n, "stopAtrMultiple": a, "targetAtrMultiple": b,
                      "minutes": h} for n, a, b, h in POLICIES],
        "riskPolicy": "8-16 stop ticks allowed for ATR setups; fixed 12-tick stop; next 1m open, 1tick/side adverse slip, 5 USD roundtrip fee",
        "results": {},
        "warnings": [
            "No ML trained, no trading strategy selected, and no validation/holdout scored.",
            "Predefined hourly sample avoids overlapping 60-minute paths but omits other minute closes.",
            "All sides and brackets compared on identical, ATR-risk-eligible timestamps.",
            "One-minute OHLC does not prove intraminute order or actual exchange fills.",
            "Regime and session subgroup results are hypothesis generation, not independent performance evidence.",
            "Closed-equity drawdown is not intraday trailing prop-firm drawdown.",
            "Purchased tick-level history is not read: only historical 1m OHLC CSVs."
        ],
    }
    for key, rows in sorted(results.items()):
        breakdown = {}
        for dimension in ("period", "session", "volatility", "contract"):
            breakdown[dimension] = {
                value: summary([r for r in rows if r[dimension] == value])
                for value in sorted({r[dimension] for r in rows})
            }
        report["results"][key] = {"all": summary(rows), "by": breakdown}
    (output / "es-atr-landscape-v3.json").write_text(json.dumps(report, indent=2), "utf-8")
    md = ["# Ninjex AI - ATR bracket research (training history only)", "",
          "NOT an ML model, verified setup, or independent trading backtest.", "",
          "Matched observations: " + str(n) + " (one per hour, risk-cap screened).",
          "", "| Geometry and side | Trades | Net USD | Mean per trade | PF | Closed DD | TP-first % |",
          "|---|---:|---:|---:|---:|---:|---:|"]
    for name, item in report["results"].items():
        s = item["all"]
        md.append("| {} | {} | {} | {} | {} | {} | {} |".format(
            name, s["trades"], s["netUsd"], s["meanPerTradeUsd"],
            s["profitFactor"], s["maxClosedEquityDrawdownUsd"],
            round(100*s["targetRate"], 2)))
    md.extend(["", "## Early vs late training (descriptive only)", ""])
    for name, item in report["results"].items():
        md.append("### " + name)
        for phase, data in item["by"]["period"].items():
            md.append("- {}: {} entries, net USD {}, mean {}, PF {}".format(
                phase, data["trades"], data["netUsd"],
                data["meanPerTradeUsd"], data["profitFactor"]))
    md.extend(["", "Full session, contract and volatility breakdowns are in JSON.",
               "All discovered patterns require future independent validation.", ""])
    (output / "es-atr-landscape-v3.md").write_text("\n".join(md), "utf-8")
    return report


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--features", required=True, type=Path)
    parser.add_argument("--audit", required=True, type=Path)
    parser.add_argument("--output-dir", required=True, type=Path)
    args = parser.parse_args()
    report = perform(args.features, args.audit, args.output_dir)
    print("[NinjexAI-ATR] Matched:", report["matchedDecisionTimestamps"])
    print("[NinjexAI-ATR] Validation NOT scored:", report["validationRowsNotScored"])
    print("[NinjexAI-ATR] Holdout NOT scored:", report["holdoutRowsNotScored"])
    print("[NinjexAI-ATR] Report:", args.output_dir / "es-atr-landscape-v3.json")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
