# Backup and recovery

## 1. Goals

Protect primarily against:

- interrupted/corrupt workbook writes;
- bad application migration;
- accidental import replacement;
- accidental transaction changes where workbook-level restore is appropriate.

A second external backup is recommended for total VPS/disk loss.

## 2. Mutation safety

Every mutating workbook operation uses:

```text
lock
  ↓
copy active → temp
  ↓
mutate temp
  ↓
validate temp
  ↓
backup active if policy requires
  ↓
atomic replace active
  ↓
unlock
```

Temp and active files must be on the same filesystem for atomic rename semantics.

## 3. Retention

Simple v1 recommendation:

### Routine rolling versions

Keep the latest **30** mutation backups per workbook/year, with coalescing if transaction volume would otherwise generate unreasonable storage growth.

Implementation may choose backup-at-interval rather than every transaction if atomic writes plus audit history are sufficient.

### Protected backups

Always create a protected backup before:

- workbook import replacement;
- workbook schema migration;
- template/year-generation migration affecting an existing file.

Retain protected backups for **90 days** initially.

The Phase 4 import path writes protected backups beneath
`backups/<immutable-user-id>/<year>/` immediately before a validated replacement
is promoted. Filenames contain a UTC timestamp, the `pre-import` purpose, and a
random suffix. Retention pruning and an operator restore command remain later
operational work; the import path does not delete protected backups.

The exact storage policy can be tuned after observing file size/change frequency.

## 4. Restore

Operator/API restore procedure:

1. place account/year into maintenance lock;
2. verify backup package;
3. create backup of current active workbook;
4. atomically promote selected backup;
5. invalidate/reload Android canonical cache;
6. preserve/record an operational restore event.

Do not silently merge a backup with current transactions.

## 5. External backup

Same-VPS backups do not protect against VPS loss.

Recommended follow-up:

- daily encrypted copy of `/srv/lajur/data`, `/srv/lajur/auth`, `/srv/lajur/keys`, and protected backups;
- target controlled by operator (another VPS/NAS/object store if acceptable);
- restore procedure tested occasionally.

External backup destination is intentionally not locked in v1.

### Installed operator backup — 2026-10-11

The operator authorized using the VPS's existing Google Drive rclone remote.
The existing database/Minecraft cron jobs do not include Lajur and are preserved.
Lajur has its own `/etc/cron.d/lajur-backup`: daily at 21:00 UTC (**04:00 WIB**).
The installed command is:

```sh
sudo python3 /usr/local/lib/lajur-backup/backup-lajur.py
```

Source lives in `services/api/deploy/backup-lajur.py` and
`verify_lajur_backup.py`. It copies all users' active `.xlsm` files from `data`,
protected `.xlsm` copies from `backups`, templates and server keys, and creates
an online SQLite snapshot of `auth/lajur-auth.db`. Auth is included primarily to
preserve account UUIDs and workbook ownership; it is not a SQL financial ledger.
No receipt, staging, environment file or rclone credential is archived.

The API remains running. An open file descriptor pins one complete workbook
across the adapter's atomic replacement; separate user/year files need not share
one global timestamp. SQLite's backup API includes committed WAL transactions.
Concurrent direct editing of the active XLSM remains unsupported. This backup
protects synchronized data; Android operations still pending on a phone are not
yet present on the server.

Versioned archives go to the operator's existing `gdrive:lajur-backup` folder.
Each includes a manifest of SHA-256 hashes. The job validates XLSM ZIP checksums,
required workbook/VBA parts and SQLite integrity, uploads with `rclone copyto`,
downloads the uploaded archive, and verifies it again. `copyto` does not delete
destination files as part of copying. See the [rclone documentation](https://rclone.org/commands/rclone_copyto/).
Only after successful verification does retention remove the oldest exact
`lajur-<UTC>-<random>.tar.gz` filenames: retain **30 versions in Drive**, **7 in
`/srv/lajur/offsite-backups`**. Manual runs count as versions, not calendar days.
Files belonging to existing backup jobs are outside this retention scope.

Status is `/srv/lajur/offsite-backups/last-success.json`; generic success/failure
messages go to `backup.log` in that directory. A nonblocking flock prevents
overlapping Lajur jobs. This is a cron backup, without external alerting; a failed
job does not advance last-success. Monitoring remains deferred by the operator.

Local archives and status are root-only. Drive access uses the existing operator
account. **The archives do not have an additional application encryption layer**;
they contain private workbook data, password hashes and server keys. Keep this
Drive folder private; never publish its contents or commit them to GitHub.
Encryption with separately stored recovery material remains a possible later
improvement, rather than claiming this initial installation is encrypted.

### Isolated restore exercise on Windows

A second VPS is unnecessary. Download an archive from the private Drive folder
into an ignored private local directory, then run:

```powershell
python services/api/deploy/verify_lajur_backup.py private/output/backup.tar.gz --restore-to private/output/restore-exercise
```

The destination **must not exist**. The verifier rejects path traversal, links,
duplicate members, manifest/hash mismatches, damaged XLSM packages and invalid
SQLite snapshots before copying verified files into a fresh directory. It prints
counts only, without financial values, account identities or credentials.
Keep downloaded archives and restored files in a local folder restricted to the
operator. Do not open an archive as an active application storage directory.

This exercise proves downloaded bytes and package integrity plus isolated file
recovery. It does not prove all Excel formulas recalculate, nor a full production
account/session restoration. For ordinary financial recovery, select the correct
user/year workbook from the recovered `data` tree and use that account's existing
validated workbook import flow; preserve a current export first. An actual import
replacement is a separately authorized action, not part of the restore test.
Account-wide disaster recovery with SQLite/keys needs a maintenance window and
an explicit operator decision; never mix arbitrary account UUIDs with recovered
workbook directories.

## 6. Key backup

Server signing/Data Protection keys must be included in protected infrastructure backup when required to maintain sessions/decryption.

Loss of signing keys may invalidate sessions; loss of keys used to encrypt durable data could be worse. Document the exact impact once implementation selects key mechanisms.
