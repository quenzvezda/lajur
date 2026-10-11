#!/usr/bin/env python3
"""Verify/extract a backup into a new isolated folder; never activate it."""
import argparse
from contextlib import closing
import hashlib
import json
from pathlib import Path, PurePosixPath
import shutil
import sqlite3
import tarfile
import tempfile
import zipfile


def verify_workbook(path):
    with zipfile.ZipFile(path) as package:
        if package.testzip() is not None:
            raise ValueError('Workbook ZIP checksum failed')
        required = {'[Content_Types].xml', 'xl/workbook.xml', 'xl/vbaProject.bin'}
        if not required.issubset(package.namelist()):
            raise ValueError('Workbook package is incomplete')


def verify_archive(archive, restore_to=None):
    # No extractall: validate all names/types first and write only verified regular files.
    with tarfile.open(archive, 'r:gz') as package:
        members = package.getmembers()
        names = [member.name for member in members]
        if len(names) != len(set(names)) or 'manifest.json' not in names:
            raise ValueError('Invalid or duplicate package members')
        for member in members:
            path = PurePosixPath(member.name)
            if not member.isfile() or path.is_absolute() or '..' in path.parts or '\\' in member.name:
                raise ValueError('Unsafe package path/type')
            if member.name != 'manifest.json' and path.parts[0] not in ('data', 'auth', 'keys', 'templates', 'backups'):
                raise ValueError('Unexpected storage path')
        manifest = json.load(package.extractfile('manifest.json'))
        if manifest.get('format') != 1 or set(manifest['files']) != set(names) - {'manifest.json'}:
            raise ValueError('Manifest does not match archive')
        with tempfile.TemporaryDirectory() as temporary:
            output = Path(temporary)
            for member in members:
                content = package.extractfile(member).read()
                if member.name != 'manifest.json' and hashlib.sha256(content).hexdigest() != manifest['files'][member.name]:
                    raise ValueError('File hash differs')
                target = output / member.name
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(content)
                if target.suffix == '.xlsm':
                    verify_workbook(target)
            db = output / 'auth/lajur-auth.db'
            with closing(sqlite3.connect(db.as_uri() + '?mode=ro', uri=True)) as connection:
                if connection.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
                    raise ValueError('Auth snapshot invalid')
            if restore_to is not None:
                destination = Path(restore_to)
                # copytree refuses an existing destination, including a symlink.
                shutil.copytree(output, destination)
        return sum(name.startswith('data/') and name.endswith('.xlsm') for name in names)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('archive', type=Path)
    parser.add_argument('--restore-to', type=Path)
    args = parser.parse_args()
    try:
        count = verify_archive(args.archive, args.restore_to)
        print(f'PASS archive hashes, workbook ZIP/VBA and SQLite integrity; active_workbooks={count}')
    except Exception as error:
        print(f'FAIL archive verification: {type(error).__name__}; no active data changed')
        raise SystemExit(1)
