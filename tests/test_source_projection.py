import importlib.util
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location('source_export', ROOT / 'tools/Export-FrameScopeSource.py')
EXPORT = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(EXPORT)


class OfflineSourceProjectionTests(unittest.TestCase):
    def test_upload_only_entrypoints_are_absent_from_public_runtime(self):
        for name in ['desktop-preview/src-tauri/src/backend.rs',
                     'src/reporting/FrameScopeReportGenerator.cs',
                     'src/reporting/FrameScopeReportGenerator.Cli.cs',
                     'tests/FrameScopeReportManifestTests.cs']:
            value = EXPORT.project(name, (ROOT / name).read_text(encoding='utf-8-sig'))
            for symbol in ['ExportBenchmark', 'report_benchmark_export', '--export-benchmark']:
                self.assertNotIn(symbol, value, name)

    def test_local_report_and_recorded_hardware_are_preserved(self):
        value = EXPORT.project('src/reporting/FrameScopeReportGenerator.Cli.cs',
                               (ROOT / 'src/reporting/FrameScopeReportGenerator.Cli.cs').read_text(encoding='utf8'))
        self.assertIn('LoadRecordedHardware', value)
        self.assertIn('GetArgValue', value)

    def test_projected_sources_can_be_exported_again(self):
        for name in ['desktop-preview/src-tauri/src/backend.rs',
                     'src/reporting/FrameScopeReportGenerator.cs',
                     'src/reporting/FrameScopeReportGenerator.Cli.cs',
                     'tests/FrameScopeReportManifestTests.cs',
                     'desktop-preview/src/config/search-index.ts']:
            first = EXPORT.project(name, (ROOT / name).read_text(encoding='utf-8-sig'))
            self.assertEqual(first, EXPORT.project(name, first), name)


if __name__ == '__main__':
    unittest.main()
