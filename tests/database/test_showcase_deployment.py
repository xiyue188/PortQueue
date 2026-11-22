"""Read-only rendering guard. Never reset a populated database to test deployment."""
import subprocess
import unittest
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]

class ShowcaseDeploymentTests(unittest.TestCase):
    def test_plan_preserves_sources_and_uses_only_dedicated_database(self):
        sources={p:p.read_bytes() for p in (ROOT/'database').rglob('*.sql')}
        result=subprocess.run(['pwsh','-NoProfile','-File',str(ROOT/'database/deploy-mobile-showcase.ps1'),'-PlanOnly'],capture_output=True,text=True,encoding='utf-8',errors='replace')
        self.assertEqual(0,result.returncode,result.stderr)
        self.assertIn('no SQL executed',result.stdout)
        for path,content in sources.items(): self.assertEqual(content,path.read_bytes())
        files=list((ROOT/'artifacts/showcase/sql').glob('*.sql'))
        self.assertGreaterEqual(len(files),18)
        for p in files: self.assertNotIn('USE [PortQueue];',p.read_text(encoding='utf-8-sig'))
        scenario=(ROOT/'artifacts/showcase/sql/13_seed_port_scenarios.sql').read_text(encoding='utf-8-sig')
        self.assertIn('2026-11-22T00:00:00',scenario)
        self.assertIn("CONCAT('99002'",scenario)

    def test_future_event_date_is_rejected_before_sql(self):
        result=subprocess.run(['pwsh','-NoProfile','-File',str(ROOT/'database/deploy-mobile-showcase.ps1'),'-SceneDate','2099-01-01','-PlanOnly'],capture_output=True,text=True,encoding='utf-8',errors='replace')
        self.assertNotEqual(0,result.returncode)
        self.assertIn('future',result.stderr)

if __name__=='__main__': unittest.main()
