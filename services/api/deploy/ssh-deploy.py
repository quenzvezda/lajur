"""Actions client: secret key and registry token never appear in argv/logs."""
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile


def main():
    host = os.environ['DEPLOY_HOST']
    if not re.fullmatch(r'[a-zA-Z0-9.-]+', host):
        raise ValueError('Invalid deployment host')
    digest, revision, run = (os.environ[name] for name in ('DEPLOY_DIGEST', 'GITHUB_SHA', 'GITHUB_RUN_NUMBER'))
    if not re.fullmatch(r'sha256:[0-9a-f]{64}', digest) or not re.fullmatch(r'[0-9a-f]{40}', revision) or not run.isdigit():
        raise ValueError('Invalid deployment identity')
    os.umask(0o077)
    with tempfile.TemporaryDirectory(prefix='lajur-ssh-', dir=os.environ['RUNNER_TEMP']) as directory:
        key, known_hosts = Path(directory)/'key', Path(directory)/'known_hosts'
        key.write_text(os.environ.pop('DEPLOY_SSH_KEY').rstrip()+'\n')
        known_hosts.write_text(os.environ.pop('DEPLOY_KNOWN_HOSTS').rstrip()+'\n')
        token = os.environ.pop('REGISTRY_TOKEN')
        command = ['ssh', '-i', str(key), '-o', 'IdentitiesOnly=yes', '-o', 'BatchMode=yes',
                   '-o', 'StrictHostKeyChecking=yes', '-o', f'UserKnownHostsFile={known_hosts}',
                   '-o', 'ConnectTimeout=15', '-o', 'ServerAliveInterval=15', '-o', 'ServerAliveCountMax=4',
                   f'lajur-deploy@{host}', f'deploy {digest} {revision} {run}']
        subprocess.run(command, input=json.dumps({'registryToken': token}), text=True, check=True, timeout=900)


if __name__ == '__main__':
    main()
