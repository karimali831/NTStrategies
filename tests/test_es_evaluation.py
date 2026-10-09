import csv
import gzip
import importlib.util
import tempfile
import unittest
from datetime import date, datetime, timedelta
from pathlib import Path

spec = importlib.util.spec_from_file_location('evaluation', Path(__file__).parents[1] / 'src/Tools/analyze_es_evaluation.py')
m = importlib.util.module_from_spec(spec); spec.loader.exec_module(m)


class EvaluationTests(unittest.TestCase):
    def test_intratrade_peak_breach_before_closed_loss(self):
        a = m.Attempt(date(2025,9,15),0)
        a.accept(datetime(2025,9,15,10),-5,1400,2)
        a.accept(datetime(2025,9,15,10,1),-5,-600,2)
        self.assertEqual(a.status,'Failed')
        self.assertEqual(a.max_dd,2000)

    def test_pass_requires_flat_and_net_target(self):
        a = m.Attempt(date(2025,9,15),0)
        a.accept(datetime(2025,9,15,10),2995,4000,1)
        self.assertEqual(a.status,'Open')
        a.accept(datetime(2025,9,15,10,1),2995,2995,0)
        self.assertEqual(a.status,'Open')
        a.accept(datetime(2025,9,15,10,2),3000,3000,0)
        self.assertEqual(a.status,'Passed')

    def test_calendar_deadline_not_fifteen_observations(self):
        a = m.Attempt(date(2025,9,15),0)
        a.accept(datetime(2025,10,6,9,35),3000,3000,0)
        self.assertEqual(a.status,'Timeout')

    def test_eod_peak_and_intraday_floor(self):
        a = m.Attempt(date(2025,9,15),0,model='eod')
        a.accept(datetime(2025,9,15,10),0,1500,1)
        a.accept(datetime(2025,9,15,15,29),1000,1000,0)
        self.assertEqual(a.peak,0)
        a.accept(datetime(2025,9,16,10),1000,-1000,1)
        self.assertEqual(a.status,'Failed')

    def make_run(self, root, name, observations):
        p = root/name; p.mkdir()
        with gzip.open(p/'equity.csv.gz','wt',newline='') as f:
            w = csv.writer(f); w.writerow(['Time','State','RealizedNet','UnrealizedGross','LiquidationNet','OpenQuantity','Signal'])
            for t,net,qty in observations: w.writerow([t.isoformat(),'Realtime',net,0,net,qty,''])
        for table, header in {
            'quality':'Time,State,Event,Detail', 'manifest':'Key,Value',
            'candidates':'Time,State,Decision', 'trades':'Net',
            'shadows':'Model,StopTicks,RewardRisk,Outcome'}.items():
            (p/(table+'.csv')).write_text(header+'\n')
        return p

    def test_stitch_preserves_first_entry_commission(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            a=self.make_run(root,'a',[(datetime(2025,9,15,9,35),0,0),(datetime(2025,9,15,15,29),500,0)])
            b=self.make_run(root,'b',[(datetime(2025,9,16,9,35),-5,1),(datetime(2025,9,16,15,29),100,0)])
            values=list(m.equity_stream([b,a]))
            self.assertEqual(values[2][1],495)
            self.assertEqual(values[-1][1],600)

    def test_overlapping_contract_runs_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            a=self.make_run(root,'a',[(datetime(2025,9,15,9,35),0,0),(datetime(2025,9,16,15,29),500,0)])
            b=self.make_run(root,'b',[(datetime(2025,9,16,9,35),0,0)])
            with self.assertRaises(ValueError): list(m.equity_stream([a,b]))

    def test_mature_denominator_excludes_late_censored_starts(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); obs=[]; d=date(2025,9,15); net=0
            for n in range(40):
                x=d+timedelta(days=n)
                if x.weekday()>4: continue
                obs.append((datetime.combine(x,datetime.min.time()).replace(hour=9,minute=35),net,0))
                net+=500
                obs.append((datetime.combine(x,datetime.min.time()).replace(hour=15,minute=29),net,0))
            run=self.make_run(root,'run',obs)
            result=m.analyze([run],root/'output')
            self.assertGreater(result['mature_quality_qualified_attempts'],0)
            self.assertEqual(result['qualified_pass_rate'],1)
            self.assertGreater(result['all_attempt_outcomes']['Censored'],0)

    def test_missing_weekday_excludes_validation(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp); obs=[]; start=date(2025,9,15)
            for n in range(30):
                d=start+timedelta(days=n)
                if d.weekday()>4 or n==1: continue
                obs.extend([(datetime.combine(d,datetime.min.time()).replace(hour=9,minute=35),0,0),
                            (datetime.combine(d,datetime.min.time()).replace(hour=15,minute=29),0,0)])
            run=self.make_run(root,'run',obs); m.analyze([run],root/'output')
            records=list(m.rows(root/'output/attempts_intraday.csv'))
            self.assertIn('missing_weekday:2025-09-16',records[0]['QualityFlags'])


if __name__=='__main__': unittest.main()
