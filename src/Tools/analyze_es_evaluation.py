#!/usr/bin/env python3
"""Stream actual-fill liquidation equity; generic $3k/$2k rolling evaluations.
No fitted parameters, no synthetic performance substituted for actual orders.
"""
import argparse
import csv
import gzip
import json
from collections import Counter
from datetime import datetime, timedelta, time
from pathlib import Path


def stamp(s):
    # NT DateTime carries 7 fractional digits; Python keeps microseconds.
    return datetime.fromisoformat(s)


def rows(path):
    opener = gzip.open if str(path).endswith('.gz') else open
    with opener(path, 'rt', encoding='utf-8-sig', newline='') as f:
        yield from csv.DictReader(f)


def equity_stream(folders):
    """Join disjoint contract runs using realized net offsets, never normalize away fees."""
    runs = []
    for folder in folders:
        path = folder / 'equity.csv.gz'
        first = next((r for r in rows(path) if r['State'] == 'Realtime'), None)
        if first:
            runs.append((stamp(first['Time']), path))
    runs.sort()
    offset, last_time, last_realized, last_qty = 0.0, None, 0.0, 0
    for _, path in runs:
        if last_qty:
            raise ValueError('Cannot stitch contract runs with an open strategy position')
        offset = last_realized
        first = True
        last_sequence=0
        for row in rows(path):
            if row['State'] != 'Realtime':
                continue
            t = stamp(row['Time'])
            if 'Sequence' in row:
                sequence=int(row['Sequence'])
                if sequence<=last_sequence: raise ValueError('Non-increasing equity observation sequence')
                last_sequence=sequence
            if first and last_time is not None and t <= last_time:
                raise ValueError('Overlapping run timestamps; use disjoint replay allocations')
            first = False
            if last_time is not None and t < last_time:
                # v1 Playback execution clocks rounded up to the next second.
                # Preserve written observation order; never sort prices/fills into a new path.
                if (last_time-t).total_seconds()>1 or 'Sequence' in row:
                    raise ValueError('Out-of-order equity records beyond legacy one-second callback rounding')
                t=last_time
            realized = offset + float(row['RealizedNet'])
            equity = offset + float(row['LiquidationNet'])
            qty = int(row['OpenQuantity'])
            yield t, realized, equity, qty
            last_time, last_realized, last_qty = t, realized, qty


class Attempt:
    def __init__(self, start, baseline, target=3000, drawdown=2000, model='intraday'):
        self.start = start
        self.deadline = start + timedelta(days=21)
        self.baseline = baseline
        self.target, self.drawdown, self.model = target, drawdown, model
        self.peak = self.max_dd = 0.0
        self.status = 'Open'
        self.end = None
        self.net = 0.0
        self.previous_day = None
        self.previous_closed = 0.0

    def accept(self, t, realized, equity, qty):
        if self.status != 'Open':
            return
        if t.date() >= self.deadline:
            self.status, self.end = 'Timeout', self.deadline.isoformat()
            return
        value = equity - self.baseline
        closed = realized - self.baseline
        if self.model == 'eod' and self.previous_day is not None and t.date() != self.previous_day:
            self.peak = max(self.peak, self.previous_closed)
        if self.model == 'intraday':
            self.peak = max(self.peak, value)
        self.max_dd = max(self.max_dd, self.peak - value)
        self.net = closed
        # Failure wins any same-observation collision. Equality breaches the floor.
        if self.peak - value >= self.drawdown:
            self.status, self.end = 'Failed', t.isoformat()
        elif qty == 0 and closed >= self.target:
            self.status, self.end = 'Passed', t.isoformat()
        self.previous_day, self.previous_closed = t.date(), closed


def analyze(folders, output, model='intraday', closed_dates=()):
    coverage, quality = {}, Counter()
    last = None
    for t, realized, equity, qty in equity_stream(folders):
        d = t.date()
        if d not in coverage:
            coverage[d] = [t, t, qty]
        coverage[d][1] = t
        last = t
    if not coverage:
        raise ValueError('No realtime equity. Run Market Replay with the strategy enabled first.')
    suspect = set()
    for folder in folders:
        lock_dates = []
        for r in rows(folder / 'quality.csv'):
            if r['State'] == 'Realtime':
                quality[r['Event']] += 1
                if r['Event'] in {'RthTickGap', 'OutOfOrder', 'EvaluationLocked', 'OrderFault'} or (r['Event']=='TickGap' and time(9,30)<=stamp(r['Time']).time()<time(16)):
                    suspect.add(stamp(r['Time']).date())
                    if r['Event'] in {'OrderFault', 'EvaluationLocked'}:
                        lock_dates.append(stamp(r['Time']).date())
        if lock_dates:
            for r in rows(folder / 'context.csv'):
                d = stamp(r['Time']).date()
                if d >= min(lock_dates): suspect.add(d)
        manifest = {r['Key']: r['Value'] for r in rows(folder / 'manifest.csv')}
        if int(manifest.get('TerminatedWithOpenQuantity', '0')):
            suspect.add(last.date())
    starts = sorted(coverage)
    # Coverage from window-bounded equity is a useful diagnostic, not proof of complete ticks.
    for d, (first, end, qty) in coverage.items():
        if first.time() > time(9,36) or end.time() < time(15,29) or qty:
            suspect.add(d)
    active, attempts = [], []
    previous_realized = 0.0
    previous_date = None
    for t, realized, equity, qty in equity_stream(folders):
        if t.date() != previous_date:
            a = Attempt(t.date(), previous_realized, model=model)
            attempts.append(a)
            active.append(a)
            previous_date = t.date()
        for a in active:
            a.accept(t, realized, equity, qty)
        active = [a for a in active if a.status == 'Open']
        previous_realized = realized
    records = []
    closed_dates = set(closed_dates)
    for a in attempts:
        complete = last.date() >= a.deadline or a.status in {'Passed', 'Failed'}
        if a.status == 'Open':
            a.status = 'Timeout' if complete else 'Censored'
        flags = []
        stop = min(a.deadline - timedelta(days=1), stamp(a.end).date() if a.end else last.date())
        d = a.start
        while d <= stop:
            if d in suspect:
                flags.append('suspect:' + d.isoformat())
            elif d.weekday() < 5 and d not in coverage and d not in closed_dates:
                flags.append('missing_weekday:' + d.isoformat())
            d += timedelta(days=1)
        records.append({'Start': a.start.isoformat(), 'DeadlineExclusive': a.deadline.isoformat(),
                        'Outcome': a.status, 'End': a.end or '', 'NetAtEnd': round(a.net,2),
                        'MaxTrailingDrawdown': round(a.max_dd,2), 'QualityFlags': ';'.join(flags),
                        'DaysToPass': (stamp(a.end).date()-a.start).days+1 if a.status=='Passed' else ''})
    output.mkdir(parents=True, exist_ok=True)
    with open(output / ('attempts_' + model + '.csv'), 'w', newline='') as f:
        w = csv.DictWriter(f, fieldnames=list(records[0]))
        w.writeheader(); w.writerows(records)
    qualified = [r for r in records if r['Outcome'] != 'Censored' and not r['QualityFlags']]
    # Do not count immature windows only because they passed early: report mature starts too.
    mature = [r for r in qualified if datetime.fromisoformat(r['DeadlineExclusive']).date() <= last.date()]
    outcomes = Counter(r['Outcome'] for r in mature)
    decisions, shadow_outcomes = Counter(), Counter()
    trades, trade_net = 0, 0.0
    for folder in folders:
        for r in rows(folder / 'candidates.csv'):
            if r['State'] == 'Realtime': decisions[r['Decision']] += 1
        for r in rows(folder / 'trades.csv'):
            trades += 1; trade_net += float(r['Net'])
        for r in rows(folder / 'shadows.csv'):
            shadow_outcomes[r['Model'] + '/' + r['StopTicks'] + '/' + r['RewardRisk'] + '/' + r['Outcome']] += 1
    result = {'drawdown_model': model, 'target': 3000, 'drawdown_limit': 2000,
              'calendar_horizon_days': 21, 'observed_sessions': len(coverage), 'actual_trades': trades,
              'actual_closed_net': round(trade_net,2), 'trades_per_observed_session': trades/len(coverage),
              'all_attempt_outcomes': dict(Counter(r['Outcome'] for r in records)),
              'mature_quality_qualified_attempts': len(mature), 'qualified_outcomes': dict(outcomes),
              'qualified_pass_rate': outcomes['Passed']/len(mature) if mature else None,
              'passes_by_7_14_21_calendar_days': {str(n):sum(r['Outcome']=='Passed' and r['DaysToPass']<=n for r in mature) for n in [7,14,21]},
              'quality_events':dict(quality), 'entry_decisions':dict(decisions),
              'hypothetical_last_price_barriers':dict(shadow_outcomes),
              'limitations':['Overlapping rolling starts are correlated, not independent trials.',
                            'Continuous fixed-policy fills reused; failed/passed attempts stop analytically.',
                            'Daily risk decisions/sizing are not re-simulated for independent account state.',
                            'Generic uncapped trailing high-water model, not a claim about any firm rules.',
                            'Fees are configured estimates; fills carry replay/simulator assumptions.',
                            'First/last window ticks and gap checks cannot prove complete market data.',
                            'Legacy callback clock reversals <=1 second are clamped in recorded order; larger reversals fail.',
                            'Candidates/shadows are research diagnostics and never actual pass-rate evidence.']}
    (output / ('summary_' + model + '.json')).write_text(json.dumps(result, indent=2))
    return result


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('folders', nargs='+', type=Path, help='Extracted ES_Evaluation_* run folders')
    p.add_argument('--output', type=Path, required=True)
    p.add_argument('--drawdown-model', choices=['intraday','eod'], default='intraday')
    p.add_argument('--closed-date', action='append', default=[], help='Verified nontrading date YYYY-MM-DD; repeat as needed')
    args = p.parse_args()
    summary = analyze(args.folders,args.output,args.drawdown_model,
                      [datetime.fromisoformat(d).date() for d in args.closed_date])
    print(json.dumps({k:v for k,v in summary.items() if k != 'hypothetical_last_price_barriers'}, indent=2))
