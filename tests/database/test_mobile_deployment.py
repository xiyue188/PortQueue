"""Deployment guard tests; PlanOnly renders scripts without connecting to SQL Server."""
import json
from pathlib import Path
import subprocess
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPT = ROOT / 'database/deploy-mobile-dev.ps1'


class MobileDeploymentTests(unittest.TestCase):
    def invoke(self, database):
        return subprocess.run(['pwsh', '-NoProfile', '-File', str(SCRIPT),
                               '-DatabaseName', database, '-PlanOnly'],
                              capture_output=True, encoding='utf-8', errors='replace')

    def test_protects_frozen_database_and_arbitrary_identifiers(self):
        for database in ['PortQueue', 'master', 'PortQueueMobileDev]; DROP DATABASE PortQueue;--']:
            with self.subTest(database=database):
                result = self.invoke(database)
                self.assertNotEqual(0, result.returncode)
                self.assertIn('DatabaseName', result.stderr)

    def test_renders_only_isolated_target_without_modifying_sources(self):
        before = {p: p.read_bytes() for p in (ROOT / 'database').glob('*.sql')}
        result = self.invoke('PortQueueMobileDev')
        self.assertEqual(0, result.returncode, result.stderr)
        plan = json.loads(result.stdout)
        self.assertEqual('PortQueueMobileDev', plan['database'])
        self.assertFalse(plan['executed'])
        scripts = plan['scripts']
        self.assertGreaterEqual(len(scripts), 10)
        for item in scripts:
            text = Path(item['renderedPath']).read_text(encoding='utf-8-sig')
            self.assertNotIn('[PortQueue]', text)
            self.assertNotIn("N'PortQueue'", text)
            self.assertIn('[PortQueueMobileDev]', text)
        for path, content in before.items():
            self.assertEqual(content, path.read_bytes())


if __name__ == '__main__':
    unittest.main()
