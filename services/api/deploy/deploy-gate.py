#!/usr/bin/python3
"""Root-owned forced-command gate. No shell, paths or Compose input from CI."""
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

IMAGE = 'ghcr.io/quenzvezda/lajur-api'
SOURCE = 'https://github.com/quenzvezda/lajur'
REQUEST = re.compile(r'deploy (sha256:[0-9a-f]{64}) ([0-9a-f]{40}) ([1-9][0-9]{0,11})')
INTERRUPT_SIGNALS = tuple(getattr(signal, name) for name in ('SIGHUP', 'SIGINT', 'SIGTERM') if hasattr(signal, name))


def parse_request(command):
    match = REQUEST.fullmatch(command)
    if not match:
        raise ValueError('Only an exact Lajur digest/revision/run deploy is permitted')
    digest, revision, run = match.groups()
    return f'{IMAGE}@{digest}', revision, int(run)


def parse_credentials(stream):
    payload = stream.read(4097)
    if len(payload) > 4096:
        raise ValueError('Registry credential payload too large')
    data = json.loads(payload)
    if not isinstance(data, dict) or set(data) != {'registryToken'}:
        raise ValueError('Invalid registry credential payload')
    token = data['registryToken']
    if not isinstance(token, str) or not 1 <= len(token) <= 2048 or any(c.isspace() for c in token):
        raise ValueError('Invalid registry token')
    return token


def write_atomic(path, content):
    descriptor, name = tempfile.mkstemp(prefix='.lajur-', dir=path.parent)
    try:
        with os.fdopen(descriptor, 'w') as output:
            output.write(content)
            output.flush()
            os.fsync(output.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


class Deployment:
    def __init__(self, config=Path('/etc/lajur'), storage=Path('/srv/lajur'), runtime=Path('/run/lajur-deploy')):
        self.config, self.storage, self.runtime = config, storage, runtime

    def call(self, *args, input=None, timeout=180):
        # Capture output: registry credentials, finance payloads and container logs
        # never enter Actions output. Arguments contain no secrets.
        result = subprocess.run(args, input=input, text=True, capture_output=True,
                                timeout=timeout, env={'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': '/root'})
        if result.returncode:
            raise RuntimeError(f'Operation failed: {args[0]} {args[1] if len(args) > 1 else ""}')
        return result.stdout

    def compose(self, *args):
        return self.call('docker', 'compose', '--env-file', str(self.config / 'production.env'),
                         '-f', str(self.config / 'compose.production.yml'), *args, timeout=240)

    def snapshot(self, run):
        # API is stopped by the caller: SQLite/WAL and XLSM files are quiescent.
        destination = self.storage / 'deploy-backups'
        destination.mkdir(mode=0o700, exist_ok=True)
        stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        backup = destination / f'{run}-{stamp}.tar.gz'
        try:
            self.call('tar', '--one-file-system', '-czf', str(backup), '-C', str(self.storage),
                      'data', 'auth', 'keys', 'templates', 'backups', timeout=600)
        except Exception:
            backup.unlink(missing_ok=True)
            raise
        return backup

    def verify_image(self, image, revision):
        details = json.loads(self.call('docker', 'image', 'inspect', image))[0]
        config = details['Config']
        labels = config.get('Labels') or {}
        if (details['Architecture'] != 'arm64' or config['User'] != '1654'
                or labels.get('org.opencontainers.image.revision') != revision
                or labels.get('org.opencontainers.image.source') != SOURCE):
            raise ValueError('Image architecture, user or source identity mismatch')

    def run(self, image, revision, run, token):
        env_path = self.config / 'production.env'
        old_env = env_path.read_text()
        values = dict(line.split('=', 1) for line in old_env.splitlines() if line and not line.startswith('#'))
        if set(values) != {'LAJUR_IMAGE', 'LAJUR_TRUSTED_PROXY_CIDR'}:
            raise ValueError('Unexpected production configuration')
        state_path = self.config / 'deployment.json'
        state = json.loads(state_path.read_text()) if state_path.exists() else {}
        if run < state.get('run', 0):
            raise ValueError('Stale deployment rejected')
        if run == state.get('run') and (image != state.get('image') or revision != state.get('revision')):
            raise ValueError('Conflicting deployment run rejected')
        # Temporary root-only Docker auth is removed after pull, even on failure.
        with tempfile.TemporaryDirectory(prefix='registry-', dir=self.runtime) as auth:
            self.call('docker', '--config', auth, 'login', 'ghcr.io', '--username', 'quenzvezda',
                      '--password-stdin', input=token + '\n')
            self.call('docker', '--config', auth, 'pull', image, timeout=300)
        self.verify_image(image, revision)
        if shutil.disk_usage(self.storage).free < 1024 ** 3:
            raise RuntimeError('At least 1 GiB free disk is required')
        self.compose('config', '--quiet')
        stopped = False
        try:
            # Set before stop so interrupted/failed stop also restarts the prior service.
            stopped = True
            self.compose('stop', 'api')
            backup = self.snapshot(run)
            write_atomic(env_path, f'LAJUR_IMAGE={image}\nLAJUR_TRUSTED_PROXY_CIDR={values["LAJUR_TRUSTED_PROXY_CIDR"]}\n')
            self.compose('up', '-d', '--wait', '--wait-timeout', '150', 'api')
            for endpoint in ('live', 'ready'):
                self.call('curl', '--fail', '--silent', '--show-error', '--retry', '3',
                          '--retry-all-errors', '--max-time', '15',
                          f'https://lajur.solusi-program.site/health/{endpoint}')
            write_atomic(state_path, json.dumps({'image': image, 'revision': revision, 'run': run,
                                                'previousImage': values['LAJUR_IMAGE'],
                                                'backup': str(backup),
                                                'deployedAt': datetime.now(timezone.utc).isoformat()}) + '\n')
        except Exception:
            if stopped:
                for number in INTERRUPT_SIGNALS:
                    signal.signal(number, signal.SIG_IGN)
                write_atomic(env_path, old_env)
                try:
                    self.compose('up', '-d', '--wait', '--wait-timeout', '150', 'api')
                except Exception as rollback_error:
                    raise RuntimeError('Deployment and image rollback failed; operator required') from rollback_error
            raise
        # Only prune this gate's completed snapshots; protected workbook backups remain.
        completed = sorted((p for p in backup.parent.iterdir()
                            if re.fullmatch(r'[1-9][0-9]{0,11}-[0-9]{8}T[0-9]{6}Z.tar.gz', p.name)
                            and p.is_file() and not p.is_symlink()), key=lambda p: p.stat().st_mtime)
        for expired in completed[:-5]:
            expired.unlink()
        print(f'DEPLOYED revision={revision} run={run}; health passed')


def main():
    import fcntl  # Production runs on Linux; guard tests also run on Windows.
    os.umask(0o077)
    image, revision, run = parse_request(os.environ.get('SSH_ORIGINAL_COMMAND', ''))
    signal.alarm(30)  # An incomplete SSH input must not leave a root process waiting.
    token = parse_credentials(sys.stdin)
    signal.alarm(0)
    def interrupted(_number, _frame):
        raise RuntimeError('Deployment interrupted')
    for number in INTERRUPT_SIGNALS:
        signal.signal(number, interrupted)
    runtime = Path('/run/lajur-deploy')
    runtime.mkdir(mode=0o700, exist_ok=True)
    with (runtime / 'lock').open('w') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        Deployment(runtime=runtime).run(image, revision, run, token)


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Only controlled messages, never credential input or raw subprocess output.
        print(f'DEPLOY_FAILED: {type(error).__name__}', file=sys.stderr)
        sys.exit(1)
