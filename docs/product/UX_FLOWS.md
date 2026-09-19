# UX flows

This document defines information architecture and behavior, not final visual styling. Exact Compose components can evolve after a usable prototype exists.

## 1. Navigation

v1 should avoid unnecessary bottom-navigation complexity.

Required destinations:

```text
Launch/Auth Gate
     │
     ├── Login (when server session unavailable)
     └── App Unlock (when locally locked)
              │
              ▼
             Home
          ┌───┼───────────────┐
          │   │               │
     Manual Add            Smart Scan
          │                   │
          └──────► Draft/Editor
                         │
                       Save
                         │
                       Home

Home transaction tap → Transaction Detail → Edit / Delete

Home settings icon → Settings
```

A later design may introduce bottom navigation only if additional primary destinations justify it.

## 2. Launch/auth gate

### Existing valid server session + local session unlocked

Open Home immediately.

### Existing valid server session + app locally locked

Show BiometricPrompt. If device credential fallback is enabled, allow it.

### No usable refresh session

Show username/password login.

Do not present both login and biometric as interchangeable server credentials. Biometrics unlock a previously established local session.

## 3. Auto-lock

Default: 5 minutes.

Suggested choices:

- immediately;
- 1 minute;
- 5 minutes;
- 15 minutes;
- 30 minutes;
- never, only if the operator explicitly permits this option.

Behavior:

- switching apps briefly within the grace period does not prompt;
- after the grace period, returning to any sensitive Lajur UI requires unlock;
- background WorkManager sync continues without a biometric prompt.

## 4. Home

Default scope: current calendar month.

Suggested content:

```text
Lajur                                      [Settings]

September 2026                           [change month]

Monthly expense
Rp x.xxx.xxx

[ + Manual ]                       [ Scan ]

Sync: ✓ / Pending 3 / Error

Today
─────────────────────────────────────────
Rp 85.000     Kebutuhan · Shopee Pay
Rp 25.000     Jajan · Cash
Rp 40.000     Transport · Bank
```

Keep dashboard information intentionally light. The workbook remains the rich reporting surface.

## 5. Manual Add

Interaction optimization:

1. Amount should be easy to reach and use numeric input.
2. Default date = today.
3. Default type = `Pengeluaran`.
4. Payment method can remember the previously used method.
5. Category is explicit.
6. Description is optional.

Save behavior:

- validate locally;
- write local transaction + outbox in one Room transaction;
- return immediate success to the user;
- synchronization is independent.

## 6. Smart Scan

Entry points:

- Home → Scan;
- image picker;
- camera;
- incoming Android image share intent.

If the app is locked, incoming share intent must pass through the same app-lock gate before sensitive content/draft is shown.

### Processing state

The user should see a single meaningful state such as:

`Analyzing transaction…`

OCR and lazy LLM initialization may happen concurrently. Do not expose internal pipeline steps unless useful for diagnostics.

### Draft UI

High-confidence values appear normally.

Low/ambiguous values should be visually marked, e.g.:

```text
Jumlah
Rp 128.500     ⚠ Periksa
```

Tapping the field can reveal alternatives:

```text
Possible values:
● Rp 128.500
○ Rp 125.000
[Enter manually]
```

Do not routinely display fake-precision percentages such as “72% confidence”.

### Missing fields

A required field can remain empty:

```text
Kategori *
[ Pilih kategori… ]
```

Save is disabled until required fields are valid.

### Important

Smart Scan never directly persists a canonical transaction without review.

## 7. Transaction detail

Show:

- date;
- type;
- payment;
- category;
- amount;
- description;
- sync state if still local/pending.

Actions:

- Edit;
- Delete.

Audit history does not need a dedicated v1 screen, though the API/data model may expose it later.

## 8. Edit

If transaction is still pending locally:

- modify local record;
- collapse/replace pending operation when safe.

If synchronized:

- create/update an outbox UPDATE command carrying expected server revision.

If API returns revision conflict:

- do not overwrite;
- fetch latest canonical transaction;
- show a concise conflict message;
- require user to review the latest state before retrying.

## 9. Delete

Delete requires confirmation.

If unsynchronized create has never reached server, delete can remove/cancel the pending create locally.

If synchronized:

- enqueue DELETE with expected revision;
- remove/hide from normal local list optimistically only if the UI clearly represents pending state;
- on permanent server failure, restore canonical view and surface the error.

No full recycle bin in v1.

## 10. Settings

Groups:

- Security
- Sync
- Smart Scan
- Transaction defaults
- Appearance
- Data

See `docs/architecture/SETTINGS.md`.

## 11. Error philosophy

Prefer actionable states:

- `Pending sync — no internet`
- `Sync failed — tap to retry`
- `Workbook import rejected — current data was not changed`
- `This transaction changed on the server — reload before editing`

Avoid raw HTTP/status/stack-trace messages in normal UI.
