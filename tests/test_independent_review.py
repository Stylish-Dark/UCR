"""Offline regression tests for the independent, fail-closed reviewer."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

MODULE = Path(__file__).resolve().parents[1] / 'build' / 'quality' / 'independent_review.py'
spec = importlib.util.spec_from_file_location('independent_review', MODULE)
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)


class ReviewerTests(unittest.TestCase):
    def test_complete_approval_is_valid(self):
        x = {'approve': True, 'issues': [], 'coverage_gaps': [], 'rationale': 'Captured states are coherent.'}
        self.assertEqual(mod.validated_verdict(x), x)

    def test_approval_cannot_contain_unresolved_issue(self):
        x = {'approve': True, 'issues': [
            {'location': 'preview.png', 'severity': 'major', 'problem': 'Clipped text',
             'recommended_fix': 'Allow measure'}], 'coverage_gaps': [], 'rationale': 'Mixed verdict'}
        with self.assertRaises(ValueError):
            mod.validated_verdict(x)

    def test_missing_screenshot_cannot_pass(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d)
            (p / 'manifest').write_text('one.png\ntwo.png\n')
            (p / 'one.png').write_bytes(b'\x89PNG\r\n\x1a\n' + b'0' * 6000)
            with self.assertRaises(FileNotFoundError):
                mod.images_from_manifest(p, p / 'manifest')

    def fixture(self, p):
        (p / 'images').mkdir()
        (p / 'images' / 'one.png').write_bytes(b'\x89PNG\r\n\x1a\n' + b'0' * 6000)
        (p / 'expected').write_text('one.png\n')
        (p / 'constitution').write_text('No clipping.')
        (p / 'diff').write_text('diff --git a/x b/x\n')
        (p / 'meta').write_text(json.dumps({'source_sha': 'f' * 40,
            'windows_build_run_id': '123', 'windows_build_conclusion': 'success'}))
        from argparse import Namespace
        return Namespace(screenshots=str(p / 'images'), expected=str(p / 'expected'),
            constitution=str(p / 'constitution'), diff=str(p / 'diff'),
            metadata=str(p / 'meta'), output=str(p / 'out'))

    def test_missing_key_fails_closed(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d)
            args = self.fixture(p)
            with patch.dict('os.environ', {'OPENAI_API_KEY': ''}):
                rc = mod.run(args)
            self.assertEqual(rc, 1)
            data = json.loads((p / 'out' / 'verdict.json').read_text())
            self.assertEqual(data['status'], 'NOT APPROVED')
            self.assertIn('OPENAI_API_KEY', data['error'])

    def test_second_reviewer_can_veto_first(self):
        with tempfile.TemporaryDirectory() as d:
            p = Path(d)
            args = self.fixture(p)
            approved = {'approve': True, 'issues': [], 'coverage_gaps': [], 'rationale': 'Good evidence'}
            rejected = {'approve': False, 'issues': [
                {'location': 'Mapping path', 'severity': 'major', 'problem': 'Untested transition',
                 'recommended_fix': 'Add regression test'}],
                'coverage_gaps': [], 'rationale': 'Missing test'}
            with patch.dict('os.environ', {'OPENAI_API_KEY': 'test-only'}), \
                 patch.object(mod, 'review', side_effect=[approved, rejected]) as mock:
                rc = mod.run(args)
            self.assertEqual(rc, 1)
            self.assertEqual(mock.call_count, 2)
            data = json.loads((p / 'out' / 'verdict.json').read_text())
            self.assertEqual(data['status'], 'REJECTED')


if __name__ == '__main__':
    unittest.main()
