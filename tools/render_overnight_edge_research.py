#!/usr/bin/env python3
"""Render NinjaTrader Overnight Edge research bar/event CSVs to daily PNGs."""
import argparse
import csv
from collections import defaultdict
from datetime import datetime, time
from pathlib import Path

import matplotlib
matplotlib.use("Agg")
import matplotlib.dates as mdates
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

FMT = "%Y-%m-%d %H:%M:%S.%f"
LEVELS = [
    ("PremarketHigh", "PMH", "#bd539b"),
    ("PremarketLow", "PML", "#3685b3"),
    ("OvernightHigh", "ONH", "#7b8297"),
    ("OvernightLow", "ONL", "#7b8297"),
    ("PriorDayClose", "PDC", "#c58b3f"),
    ("RthOpen", "RTH", "#248a65"),
]
COLORS = {
    "PMH-REJECT-S": "#bb4b98", "PML-BREAK-S": "#3685b3",
    "PDC-RECLAIM-L": "#238b65", "RTHOPEN-BREAK-S": "#bd8740",
}


def rows(path):
    with path.open(encoding="utf-8-sig", newline="") as handle:
        return list(csv.DictReader(handle))


def dt(value):
    if not value:
        return None
    return datetime.strptime(value, FMT)


def num(value):
    try:
        return float(value) if value not in ("", None) else None
    except (ValueError, TypeError):
        return None


def load(input_dir, selected_run):
    bar_groups = defaultdict(list)
    for path in sorted(input_dir.glob("overnight_edge_bars_*.csv")):
        for row in rows(path):
            rid = row.get("RunId", "")
            if selected_run is not None and rid != selected_run:
                continue
            timestamp = dt(row.get("BarEndET"))
            if timestamp is None:
                continue
            row["_time"] = timestamp
            key = (rid, row["Instrument"], timestamp.date().isoformat())
            bar_groups[key].append(row)
    if not bar_groups:
        raise ValueError("No matching session bars; check --run-id or --input")

    event_groups = defaultdict(list)
    for path in sorted(input_dir.glob("overnight_edge_research_*.csv")):
        for row in rows(path):
            # Never mix historical files with no RunId into a newer run.
            if not row.get("RunId") or (selected_run is not None and row.get("RunId") != selected_run):
                continue
            timestamp = dt(row.get("EventTime"))
            if timestamp is None:
                continue
            row["_time"] = timestamp
            event_groups[(row["RunId"], row["Instrument"], timestamp.date().isoformat())].append(row)
    sessions = {}
    for path in sorted(input_dir.glob("overnight_edge_sessions_*.csv")):
        for row in rows(path):
            if row.get("RunId") and (selected_run is None or row["RunId"] == selected_run):
                sessions[(row["RunId"], row["Instrument"], row["TradingDate"])] = row
    return bar_groups, event_groups, sessions


def candles(ax, selected):
    for row in selected:
        op, hi, lo, cl = [num(row.get(key)) for key in ("Open", "High", "Low", "Close")]
        if any(value is None for value in (op, hi, lo, cl)):
            continue
        x = mdates.date2num(row["_time"])
        color = "#15856c" if cl >= op else "#c84d58"
        ax.vlines(x, lo, hi, color=color, linewidth=0.8, zorder=3)
        height = max(abs(cl - op), 0.06)
        bottom = min(op, cl) if cl != op else cl - height / 2
        ax.add_patch(Rectangle((x - 0.34/1440, bottom), 0.68/1440, height,
                               edgecolor=color, facecolor=color, linewidth=0.4))


def levels(ax, selected):
    lows = [num(row.get("Low")) for row in selected]
    highs = [num(row.get("High")) for row in selected]
    lows = [value for value in lows if value is not None]
    highs = [value for value in highs if value is not None]
    if not lows or not highs:
        return
    low, high = min(lows), max(highs)
    allowance = max(8, (high-low)*0.25)
    for key, label, color in LEVELS:
        values = [num(row.get(key)) for row in selected]
        values = [value for value in values if value is not None]
        if not values:
            continue
        price = values[-1]
        if low-allowance <= price <= high+allowance:
            ax.axhline(price, ls="--", lw=0.8, color=color, alpha=0.8)
            ax.annotate(label+" "+format(price, ".2f"), (0.994, price),
                        xycoords=("axes fraction", "data"), va="bottom",
                        ha="right", fontsize=8, color=color,
                        bbox={"facecolor": "white", "alpha": 0.7, "edgecolor": "none"})


def markers(ax, events, left, right):
    for row in events:
        t = row["_time"]
        if not left <= t <= right:
            continue
        kind, signal = row.get("EventType"), row.get("Signal", "")
        color = COLORS.get(signal, "#444b57")
        x = mdates.date2num(t)
        if kind == "Candidate" and signal == "PMH-REJECT-S" and row.get("Qualified") == "1":
            y = num(row.get("SignalHigh"))
            if y is not None:
                ax.scatter([x], [y], marker="o", s=14, facecolors="none",
                           edgecolors=color, zorder=7)
        elif kind == "TradeEntry":
            y = num(row.get("EntryPrice"))
            if y is not None:
                shape = "^" if row.get("Direction") == "Long" else "v"
                ax.scatter([x], [y], marker=shape, s=85, c=[color], zorder=8)
                ax.annotate(signal.split("-")[0], (x,y), xytext=(3,10),
                            textcoords="offset points", color=color, fontsize=8)
        elif kind == "TradeExit":
            y = num(row.get("ExitPrice"))
            if y is not None:
                ax.scatter([x], [y], marker="X", s=70, c=["#303846"], zorder=8)
                pnl = num(row.get("TradeGross"))
                if pnl is not None:
                    ax.annotate("USD "+format(pnl, "+.2f"), (x,y), xytext=(2,-16),
                                textcoords="offset points", fontsize=8)


def render(target_dir, key, bars, events, summary):
    run_id, instrument, day = key
    d = datetime.strptime(day, "%Y-%m-%d").date()
    left = datetime.combine(d, time(9,30))
    right = datetime.combine(d, time(16,5))
    morning = datetime.combine(d, time(11,45))
    rth = [row for row in bars if left <= row["_time"] <= right]
    if not rth:
        return False

    fig, axs = plt.subplots(2, 1, figsize=(24,14), dpi=110,
                            gridspec_kw={"height_ratios":[1.6,1]},
                            constrained_layout=True)
    fig.patch.set_facecolor("#f8fafc")
    for ax, end, title in [(axs[0],right,"Full session"),
                            (axs[1],morning,"PMH window")]:
        subset = [row for row in rth if row["_time"] <= end]
        ax.set_facecolor("white")
        candles(ax, subset)
        levels(ax, subset)
        markers(ax, events, left, end)
        ax.set_xlim(mdates.date2num(left), mdates.date2num(end))
        ax.xaxis.set_major_locator(mdates.MinuteLocator(byminute=[0,30]))
        ax.xaxis.set_major_formatter(mdates.DateFormatter("%H:%M"))
        ax.yaxis.tick_right()
        ax.grid(alpha=0.16, ls=":")
        ax.set_title(title, loc="left", weight="bold")
        ax.set_ylabel("ES price")

    trades = sum(row.get("EventType")=="TradeEntry" for row in events
                 if left <= row["_time"] <= right)
    coverage = " · RTH bars "+summary.get("RthMinuteBars","?")+"/390" if summary else ""
    fig.suptitle(day+"   "+instrument+"   "+str(trades)+" executed trades"+coverage,
                 fontsize=17, weight="bold")
    fig.text(0.015,0.008,"Run "+run_id+" · ET timestamps · hollow circles = qualified PMH",
             fontsize=8, color="#657080")
    target_dir.mkdir(parents=True, exist_ok=True)
    name = day+"_"+instrument.replace(" ","_").replace("/","_")+"_"+run_id+"_research.png"
    fig.savefig(target_dir/name, facecolor=fig.get_facecolor())
    plt.close(fig)
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--run-id")
    parser.add_argument("--date", help="Only YYYY-MM-DD")
    parser.add_argument("--limit", type=int, default=0)
    args = parser.parse_args()
    bars, events, summaries = load(args.input, args.run_id)
    produced = 0
    for key in sorted(bars):
        if args.date and key[2] != args.date:
            continue
        if render(args.output, key, bars[key],
                  events.get(key,[]), summaries.get(key)):
            produced += 1
            print("Rendered",key)
        if args.limit and produced >= args.limit:
            break
    if produced == 0:
        raise SystemExit("No matching sessions")
    print("Completed",produced,"PNG images")


if __name__ == "__main__":
    main()
