# Local registration and recovery operations

Default mode: invitations. Set `Lajur__Registration__Mode` to `invite`, `closed`
or `open`; set `Lajur__Registration__MaxAccounts` (default 100) for host capacity.
Existing active users can issue an invitation from Akun/Android Settings after
password reauthentication. A code can create one account within seven days.
Each issuer may have at most five unused, unexpired invitations.

The first owner is created with the existing operator CLI, not public signup.
No manual invitation database edits are required. An owner with no recovery
codes should generate and save a set through Akun after normal login.

## Install the neutral template before rollout

Registered users never use `finance-template.xlsm`. Prepare their separate
template from the audited, committed synthetic fixture:

```bash
python3 services/api/deploy/prepare-registration-template.py \
  tests/workbook-fixtures/LajurWorkbookFixture.xlsm /private/staging/registration-template.xlsm
```

The target must not already exist. Verify preparation/preservation tests and
normal generated-year validation, then install as
`/srv/lajur/templates/registration-template.xlsm`, owner root, group 1654,
mode 0640. It is mounted read-only with the existing templates directory.
Do not pass a private owner workbook as the preparation source. Literal category
budgets and projection amounts are zeroed; fixture categories/payments are
synthetic defaults. The normal year generator clears synthetic monthly rows and
refreshes year/system metadata. Existing operator books/templates are unchanged.
Missing registration template deliberately blocks generation; it does not fall
back to private defaults. Workbook import remains an explicit user choice.

## Auth metadata upgrade

At startup, the initializer transactionally installs auth schema v1 tables
`RecoveryCodes`, `AccountInvitations`, `SelfRegisteredAccounts`, and
`AuthSchemaVersions`. This is additive: no existing account UUID, password,
session or finance file is replaced. The pre-deploy gate's consistent snapshot
covers auth before startup. Test upgrade on a synthetic legacy database first.
Image rollback can ignore the added tables; no destructive downgrade is needed.

## Recovery behavior

Eight high-entropy codes are shown only on registration/regeneration. Store
only purpose-separated hashes in SQLite. Recovery cancels all old codes and
sessions; after normal login the user generates a new set. A lost response can
mean the operation completed: try normal login with the new password before
repeating, then regenerate codes after reauthentication if needed.

Changing a known password ends sessions but retains saved codes. Operator reset
is different: verify account ownership independently, then use the existing
`account reset --username ...` CLI with password privately on stdin. This reset
invalidates codes and unused invitations. Never offer a public reset that trusts
username alone. Recovery also invalidates the recovered user's unused invitations.

Account requests accept at most 4 KiB. Mutation attempts are limited to ten per
ten minutes per IP across browser/API. A 429 means waiting for that window, not
creating another account. `no-store` responses prevent ordinary caching of codes.
Never log response bodies, recovery codes, invitations or plaintext passwords.

Users losing every recovery method require operator verification; no email
delivery exists. Off-host backup stays deferred at the owner's request; local
pre-deploy snapshots remain in place for metadata upgrades.
