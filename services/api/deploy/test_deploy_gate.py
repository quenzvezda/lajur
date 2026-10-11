import importlib.util
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('gate', Path(__file__).with_name('deploy-gate.py'))
gate = importlib.util.module_from_spec(spec)
spec.loader.exec_module(gate)
DIGEST = 'sha256:' + 'a' * 64
REVISION = 'b' * 40
IMAGE = gate.IMAGE + '@' + DIGEST


class FakeDeployment(gate.Deployment):
    def __init__(self, root, failure=None):
        super().__init__(root/'config', root/'storage', root/'runtime')
        for folder in (self.config, self.storage, self.runtime):
            folder.mkdir()
        self.old_env = 'LAJUR_IMAGE=sha256:old\nLAJUR_TRUSTED_PROXY_CIDR=172.18.0.1/32\n'
        (self.config/'production.env').write_text(self.old_env)
        self.calls, self.failure = [], failure

    def call(self, *args, **kwargs):
        self.calls.append((args, kwargs))
        if self.failure == 'pull' and 'pull' in args:
            raise RuntimeError('Registry unavailable')
        if self.failure == 'health' and args[0] == 'curl':
            raise RuntimeError('New image not ready')
        if args[:3] == ('docker', 'image', 'inspect'):
            return json.dumps([{'Architecture': 'arm64', 'Config': {'User': '1654', 'Labels': {
                'org.opencontainers.image.revision': REVISION, 'org.opencontainers.image.source': gate.SOURCE}}}])
        return ''

    def compose(self, *args):
        self.calls.append((('compose', *args), {}))
        return ''

    def snapshot(self, run):
        self.calls.append((('snapshot',), {}))
        if self.failure == 'snapshot':
            raise RuntimeError('Backup disk full')
        folder = self.storage/'deploy-backups'
        folder.mkdir(exist_ok=True)
        backup = folder/f'{run}-20261011T000000Z.tar.gz'
        backup.write_bytes(b'test snapshot')
        return backup


class DeployGuards(unittest.TestCase):
    def test_exact_request_and_reject_shell_or_alternate_registry(self):
        self.assertEqual((IMAGE, REVISION, 10), gate.parse_request(f'deploy {DIGEST} {REVISION} 10'))
        for request in ('id', f'deploy {DIGEST} {REVISION} 10; id',
                        f'deploy evil/image@{DIGEST} {REVISION} 10', f'deploy {DIGEST} {REVISION} 0',
                        f'deploy {DIGEST} {REVISION} 10\n', f'deploy latest {REVISION} 10'):
            with self.subTest(request=request), self.assertRaises(ValueError):
                gate.parse_request(request)

    def test_bounded_registry_payload(self):
        self.assertEqual('temporary-token', gate.parse_credentials(io.StringIO('{"registryToken":"temporary-token"}')))
        for payload in ('x'*4097, '[]', '{"registryToken":"x","command":"id"}',
                        '{"registryToken":""}', '{"registryToken":"with space"}'):
            with self.subTest(payload=payload[:40]), self.assertRaises((ValueError, json.JSONDecodeError)):
                gate.parse_credentials(io.StringIO(payload))

    def test_pull_failure_keeps_running_service_and_cleans_credentials(self):
        with tempfile.TemporaryDirectory() as directory:
            deploy = FakeDeployment(Path(directory), 'pull')
            with self.assertRaises(RuntimeError):
                deploy.run(IMAGE, REVISION, 10, 'secret')
            self.assertEqual(deploy.old_env, (deploy.config/'production.env').read_text())
            self.assertFalse(any(args[0] == 'compose' for args, _ in deploy.calls))
            self.assertEqual([], list(deploy.runtime.iterdir()))

    def test_health_failure_rolls_back_image_without_restoring_finance_data(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(gate.signal, 'signal'):
            deploy = FakeDeployment(Path(directory), 'health')
            with self.assertRaises(RuntimeError):
                deploy.run(IMAGE, REVISION, 10, 'secret')
            self.assertEqual(deploy.old_env, (deploy.config/'production.env').read_text())
            self.assertEqual(2, sum(args[:3] == ('compose', 'up', '-d') for args, _ in deploy.calls))
            self.assertFalse((deploy.config/'deployment.json').exists())
            self.assertTrue((deploy.storage/'deploy-backups/10-20261011T000000Z.tar.gz').exists())

    def test_snapshot_failure_restarts_prior_image(self):
        with tempfile.TemporaryDirectory() as directory, patch.object(gate.signal, 'signal'):
            deploy = FakeDeployment(Path(directory), 'snapshot')
            with self.assertRaises(RuntimeError):
                deploy.run(IMAGE, REVISION, 10, 'secret')
            self.assertEqual(deploy.old_env, (deploy.config/'production.env').read_text())
            self.assertTrue(any(args[:3] == ('compose', 'up', '-d') for args, _ in deploy.calls))

    def test_success_commits_state_only_after_snapshot_and_health(self):
        with tempfile.TemporaryDirectory() as directory:
            deploy = FakeDeployment(Path(directory))
            deploy.run(IMAGE, REVISION, 10, 'secret')
            state = json.loads((deploy.config/'deployment.json').read_text())
            self.assertEqual(IMAGE, state['image'])
            self.assertEqual(10, state['run'])
            calls = [args for args, _ in deploy.calls]
            self.assertLess(calls.index(('compose', 'stop', 'api')), calls.index(('snapshot',)))
            self.assertEqual(2, sum(args[0] == 'curl' for args in calls))
            self.assertEqual([], list(deploy.runtime.iterdir()))

    def test_stale_or_conflicting_run_never_contacts_docker(self):
        for run, image in ((9, IMAGE), (10, gate.IMAGE+'@sha256:'+'c'*64)):
            with tempfile.TemporaryDirectory() as directory:
                deploy = FakeDeployment(Path(directory))
                (deploy.config/'deployment.json').write_text(json.dumps({'run': 10, 'image': IMAGE, 'revision': REVISION}))
                with self.assertRaises(ValueError):
                    deploy.run(image, REVISION, run, 'secret')
                self.assertEqual([], deploy.calls)

    def test_image_identity_validation(self):
        for change in ({'Architecture': 'amd64'}, {'Config': {'User': '0', 'Labels': {}}}):
            with tempfile.TemporaryDirectory() as directory:
                deploy = FakeDeployment(Path(directory))
                details = {'Architecture': 'arm64', 'Config': {'User': '1654', 'Labels': {}}}
                details.update(change)
                with patch.object(deploy, 'call', return_value=json.dumps([details])), self.assertRaises(ValueError):
                    deploy.verify_image(IMAGE, REVISION)


if __name__ == '__main__':
    unittest.main()
