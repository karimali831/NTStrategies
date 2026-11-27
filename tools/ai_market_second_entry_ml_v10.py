#!/usr/bin/env python3
"""Train-only AI ranker for v10 H2/L2 mechanical signals (NOT discovery).

Fit fixed regularized Ridge to after-cost PnL on EARLY_TRAIN
2023-09..2024-08 H2/L2 events, score LATE_TRAIN 2024-09..2025-08.
Never read/score the already-used validation or 2026+ final holdout.
When data are insufficient, abstain rather than train/choose thresholds.
"""
import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd
from sklearn.impute import SimpleImputer
from sklearn.linear_model import Ridge
from sklearn.metrics import mean_absolute_error
from sklearn.pipeline import make_pipeline
from sklearn.preprocessing import StandardScaler

if __package__:
    from tools.ai_market_bracket_research import summarize
else:
    from ai_market_bracket_research import summarize

FEATURES=("emaSlope5Atr","closeFromEmaAtr","bodyFraction",
          "closeLocation","pullbackBars","failedFirstDepthAtr",
          "atr14","volumeVsPrior20","efficiency12","nominalStopTicks")
MIN_EARLY=50
MIN_LATE=30
MIN_FORECAST_USD=20.


def prepare(frame,manifest):
    if (manifest.get("schema")!="ninjex-es-second-entry-21ema-v10" or
            manifest.get("status")!="TRAIN_ONLY_MECHANICAL_BENCHMARK_NOT_AI_DISCOVERY"):
        raise ValueError("Unknown source/benchmark manifest")
    if frame.empty:
        return frame
    if "type" not in frame:
        raise ValueError("Missing type in event research CSV")
    h2=frame.loc[frame["type"]=="H2"].copy()
    if h2.empty:
        return h2
    columns=set(FEATURES)|{"decisionUtc","easternDate","period","side",
                         "netUsd","month","clockEtMinute",
                         "exitReason","bothTouched1m"}
    if not columns.issubset(h2.columns):
        raise ValueError("Missing as-of context or labels "+str(columns-set(h2)))
    if h2.duplicated(["decisionUtc","side"]).any():
        raise ValueError("Duplicate H2 signal")
    day=pd.to_datetime(h2["easternDate"])
    if (day>=pd.Timestamp("2025-09-01")).any():
        raise ValueError("Future validation/holdout event present")
    split=np.where(day<pd.Timestamp("2024-09-01"),
                   "EARLY_TRAIN","LATE_TRAIN")
    if not np.array_equal(split,h2["period"].to_numpy()):
        raise ValueError("Training split dates invalid")
    return h2.sort_values("decisionUtc").copy()


def asof_features(h2):
    x=h2.loc[:,FEATURES].apply(pd.to_numeric,errors="coerce")
    x["isLong"]=(h2["side"]=="LONG").astype(float)
    minutes=pd.to_numeric(h2["clockEtMinute"],errors="coerce")
    x["timeSin"]=np.sin(2*np.pi*minutes/1440)
    x["timeCos"]=np.cos(2*np.pi*minutes/1440)
    if x.isna().mean().max()>.1 or np.isinf(x.to_numpy(dtype=float)).any():
        raise ValueError("Insufficient or invalid pre-trade model features")
    return x


def fixed_model():
    return make_pipeline(SimpleImputer(strategy="median"),
                         StandardScaler(),Ridge(alpha=100.))


def summarize_cohort(frame):
    return summarize([{"decisionUtc":str(r["decisionUtc"]),
                       "month":str(r["month"]),
                       "netUsd":float(r["netUsd"]),
                       "exitReason":str(r["exitReason"]),
                       "bothTouched1m":bool(r["bothTouched1m"])}
                      for _,r in frame.iterrows()])


def select(scored,gate):
    picked=[]
    used_days=set()
    for index,row in scored.sort_values("decisionUtc").iterrows():
        if row["predictedNetUsd"]<gate:
            continue
        date=str(row["easternDate"])
        if date in used_days:
            continue
        used_days.add(date)
        picked.append(index)
    return scored.loc[picked]


def evaluate(events_path,benchmark_path,out):
    benchmark=json.loads(benchmark_path.read_text(encoding="utf-8-sig"))
    frame=(pd.read_csv(events_path,low_memory=False)
           if events_path.exists() and events_path.stat().st_size else pd.DataFrame())
    events=prepare(frame,benchmark)
    report={
        "schema":"ninjex-ai-h2-ranker-v10",
        "status":"TRAIN_ONLY_INTERNAL_CHALLENGE_NOT_A_VERIFIED_EDGE",
        "trained":False,
        "h2L2SourceEvents":len(events),
        "validationRowsNotScored":benchmark["validationRowsNotScored"],
        "finalHoldoutRowsNotScored":benchmark["finalHoldoutRowsNotScored"],
        "frozenModel":"SimpleImputer + StandardScaler + Ridge(alpha=100), fit early TRAIN only",
        "minimumFitCount":MIN_EARLY,"minimumChallengeCount":MIN_LATE,
        "fixedGate":"max(20 USD after-cost, 80th percentile of EARLY fit forecasts), 1 signal/day",
        "asOfFeatures":list(FEATURES)+["isLong","timeSin","timeCos"],
        "warnings":[
            "Supervised ML ranks deterministic second-entry events; NOT autonomous multi-strategy tick discovery.",
            "Later training year is an INTERNAL challenge; previously-used validation and final holdout never scored.",
            "Fixed model and forecast threshold, no optimisation on later outcomes.",
            "1m OHLC execution and gap assumptions are not tick/quote-accurate.",
            "A high after-cost mean on a small sample cannot certify the user's $10k/year and $2k intraday DD target."
        ],
    }
    if not events.empty:
        earlier=events["period"]=="EARLY_TRAIN"
        later=events["period"]=="LATE_TRAIN"
        report["earlyFitCount"]=int(earlier.sum())
        report["lateChallengeCount"]=int(later.sum())
        if earlier.sum()>=MIN_EARLY and later.sum()>=MIN_LATE:
            x=asof_features(events)
            outcome=events["netUsd"].to_numpy(float)
            if not np.isfinite(outcome).all():
                raise ValueError("Nonfinite training labels")
            predictor=fixed_model()
            predictor.fit(x.loc[earlier],outcome[earlier])
            train_preds=predictor.predict(x.loc[earlier])
            cutoff=max(MIN_FORECAST_USD,float(np.quantile(train_preds,.8)))
            challenge=events.loc[later].copy()
            challenge["predictedNetUsd"]=predictor.predict(x.loc[later])
            chosen=select(challenge,cutoff)
            report.update({
                "trained":True,"fixedForecastGateUsd":round(cutoff,4),
                "modelMaeUsd":round(float(mean_absolute_error(
                    outcome[later],challenge["predictedNetUsd"])),3),
                "constantEarlyMeanMaeUsd":round(float(mean_absolute_error(
                    outcome[later],np.repeat(outcome[earlier].mean(),int(later.sum())))),3),
                "allLateH2":summarize_cohort(challenge),
                "selectedLateH2":summarize_cohort(chosen),
                "rejectedLateH2":summarize_cohort(challenge.drop(chosen.index)),
                "selectedCount":len(chosen),
                "meanPredictedSelectedUsd":round(
                    float(chosen["predictedNetUsd"].mean()),3)
                    if len(chosen) else None
            })
            out.mkdir(parents=True,exist_ok=True)
            challenge.to_csv(out/"es-second-entry-v10-ai-predictions.csv",index=False)
        else:
            report["insufficientData"]="Fewer than 50 early or 30 later training H2/L2 filled trades"
    else:
        report["insufficientData"]="No H2/L2 fills"
    out.mkdir(parents=True,exist_ok=True)
    (out/"es-second-entry-v10-ai.json").write_text(
        json.dumps(report,indent=2),encoding="utf-8")
    md=["# Ninjex AI v10 — H2/L2 conditional expectancy model","",
        "**ML trained only on earlier TRAIN, challenged on later TRAIN; not independent verification.**",
        "H2/L2 event labels: {}; trained: {}.".format(len(events),report["trained"]),
        "Prior validation and final holdout untouched.",""]
    if report["trained"]:
        a,b=report["allLateH2"],report["selectedLateH2"]
        md+=["| Benchmark | Trades | Net USD | Average USD | PF | Closed DD |",
             "|---|---:|---:|---:|---:|---:|",
             "| All H2 | {} | {} | {} | {} | {} |".format(
                 a["trades"],a["netUsd"],a["meanPerTradeUsd"],a["profitFactor"],
                 a["maxClosedEquityDrawdownUsd"]),
             "| AI selected | {} | {} | {} | {} | {} |".format(
                 b["trades"],b["netUsd"],b["meanPerTradeUsd"],b["profitFactor"],
                 b["maxClosedEquityDrawdownUsd"]),
             "",
             "Fixed forecast gate: USD {}. MAE model: {} vs constant: {}.".format(
                 report["fixedForecastGateUsd"],report["modelMaeUsd"],
                 report["constantEarlyMeanMaeUsd"])]
    else:
        md+=["No model fitted: "+report["insufficientData"]]
    md+=["","Do not alter threshold after inspecting late TRAIN, do not trade live.",
         "This is not autonomous strategy discovery from raw ES ticks.",""]
    (out/"es-second-entry-v10-ai.md").write_text(
        "\n".join(md),encoding="utf-8")
    return report


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument("--events",required=True,type=Path)
    p.add_argument("--benchmark",required=True,type=Path)
    p.add_argument("--output-dir",required=True,type=Path)
    a=p.parse_args()
    result=evaluate(a.events,a.benchmark,a.output_dir)
    print("[NinjexAI-H2] AI trained:",result["trained"])
    print("[NinjexAI-H2] source H2:",result["h2L2SourceEvents"])
    print("[NinjexAI-H2] report:",a.output_dir/"es-second-entry-v10-ai.json")
    return 0


if __name__=="__main__":
    raise SystemExit(main())
