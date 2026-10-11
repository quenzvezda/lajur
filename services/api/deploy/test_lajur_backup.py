import io
from contextlib import closing
from pathlib import Path
import sqlite3
import tarfile
import tempfile
import unittest
import zipfile
from importlib.util import spec_from_file_location, module_from_spec
from verify_lajur_backup import verify_archive

spec = spec_from_file_location('backup_lajur', Path(__file__).with_name('backup-lajur.py'))
backup = module_from_spec(spec)
spec.loader.exec_module(backup)


class BackupTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.storage = self.root / 'storage'
        for name in ('data', 'auth', 'keys', 'templates', 'backups'):
            (self.storage / name).mkdir(parents=True)
        with closing(sqlite3.connect(self.storage / 'auth/lajur-auth.db')) as db:
            db.execute('CREATE TABLE Users (Id TEXT PRIMARY KEY)')
            db.execute("INSERT INTO Users VALUES ('synthetic-user')")
            db.commit()
        for user in ('synthetic-user', 'second-user'):
            path = self.storage / f'data/{user}/2026/finance.xlsm'
            path.parent.mkdir(parents=True)
            with zipfile.ZipFile(path, 'w') as book:
                for name in ('[Content_Types].xml', 'xl/workbook.xml', 'xl/vbaProject.bin'):
                    book.writestr(name, b'synthetic fixture')
        (self.storage / 'keys/test-key').write_text('synthetic key')
        self.output = self.root / 'snapshot'
        self.output.mkdir()

    def archive(self):
        self.assertEqual(2, backup.make_snapshot(self.storage, self.output))
        archive = self.root / 'backup.tar.gz'
        with tarfile.open(archive, 'w:gz') as package:
            for path in self.output.rglob('*'):
                if path.is_file():
                    package.add(path, arcname=path.relative_to(self.output).as_posix())
        return archive

    def test_all_users_roundtrip_and_existing_target_protected(self):
        archive = self.archive()
        restore = self.root / 'restore'
        self.assertEqual(2, verify_archive(archive, restore))
        for path in self.storage.rglob('*.xlsm'):
            self.assertEqual(path.read_bytes(), (restore / path.relative_to(self.storage)).read_bytes())
        with self.assertRaises(FileExistsError):
            verify_archive(archive, restore)

    def test_corruption_rejected_before_restore(self):
        archive = self.archive()
        damaged = self.root / 'damaged.tar.gz'
        with tarfile.open(archive) as source, tarfile.open(damaged, 'w:gz') as destination:
            for member in source.getmembers():
                content = source.extractfile(member).read()
                if member.name == 'keys/test-key':
                    content = b'tampered'
                member.size = len(content)
                destination.addfile(member, io.BytesIO(content))
        with self.assertRaises(ValueError):
            verify_archive(damaged, self.root / 'restore')
        self.assertFalse((self.root / 'restore').exists())

    def test_path_traversal_rejected(self):
        archive = self.root / 'unsafe.tar.gz'
        with tarfile.open(archive, 'w:gz') as package:
            for name in ('manifest.json', '../escape'):
                member = tarfile.TarInfo(name); member.size = 2
                package.addfile(member, io.BytesIO(b'{}'))
        with self.assertRaises(ValueError):
            verify_archive(archive)

    def test_snapshot_rejects_invalid_workbook(self):
        next(self.storage.rglob('*.xlsm')).write_bytes(b'not a workbook')
        with self.assertRaises(zipfile.BadZipFile):
            backup.make_snapshot(self.storage, self.output)

    def test_online_database_snapshot_includes_wal(self):
        with closing(sqlite3.connect(self.storage / 'auth/lajur-auth.db')) as db:
            db.execute('PRAGMA journal_mode=WAL')
            db.execute("INSERT INTO Users VALUES ('wal-user')")
            db.commit()
            archive = self.archive()
            restore = self.root / 'restored'
            verify_archive(archive, restore)
            with closing(sqlite3.connect(restore / 'auth/lajur-auth.db')) as restored:
                self.assertEqual(2, restored.execute('SELECT count(*) FROM Users').fetchone()[0])


if __name__ == '__main__':
    unittest.main()
