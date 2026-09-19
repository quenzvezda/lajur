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

## 6. Key backup

Server signing/Data Protection keys must be included in protected infrastructure backup when required to maintain sessions/decryption.

Loss of signing keys may invalidate sessions; loss of keys used to encrypt durable data could be worse. Document the exact impact once implementation selects key mechanisms.
