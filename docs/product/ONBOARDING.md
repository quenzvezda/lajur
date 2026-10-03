# Onboarding

## 1. Preconditions

The account exists on the server. v1 does not require public self-registration, email verification, or “forgot password” flows.

An operator/admin can create accounts using a CLI/admin procedure implemented by the backend project.

## 2. First login

User enters:

- server-known username;
- password.

Server returns an access/refresh session. The Android client stores the refresh credential using Keystore-backed protection.

If the account has no initialized workbook, proceed to workbook onboarding.

## 3. Workbook onboarding

Present two choices.

### Create New Workbook

Server:

1. selects the approved workbook template/schema version;
2. generates the requested/current year workbook;
3. patches year-dependent text/formulas;
4. initializes hidden application metadata sheets;
5. validates the resulting package;
6. atomically promotes it to the active workbook.

### Import Existing Workbook

Used for the user's existing history.

Flow:

1. Android chooses `.xlsm`.
2. Upload to a staging endpoint.
3. Server enforces upload size/type/security limits.
4. Parse and validate required workbook structure.
5. Infer/confirm year.
6. Preserve existing VBA/package content.
7. Add or upgrade hidden system metadata without altering financial meaning.
8. Reconcile IDs/audit metadata where possible.
9. Create a pre-import backup if replacing an existing year.
10. Atomically promote only after every required validation passes.

If any step fails, the active workbook remains unchanged.

Android now exposes this flow in onboarding and Settings → Data / Workbook.
Choosing a file only stages a bounded private copy (20 MiB maximum); an explicit
year and confirmation initiate upload. Replacing an existing year additionally
requires opt-in. Any outstanding outbox operation blocks import. If the network
response is lost, the server outcome is uncertain; check canonical data before
retrying. Android invalidates that year's synchronized cache before upload and
refetches afterward, including after a failed attempt.

## 4. Importing the supplied 2026 workbook

The initial migration fixture is `Keuangan_Pribadi_2026.xlsm`.

Expected observations are documented in `docs/reference/WORKBOOK_OBSERVED_STRUCTURE.md`.

The migration must account for:

- existing VBA project;
- 12 monthly tables at `A:F`;
- right-side pivot/projection/helper content;
- defined names from `Master`;
- hard-coded year references such as `DATE(2026, month, ...)`;
- `Rekap` year text.

Do not implement migration by reconstructing the workbook from scratch.

## 5. Biometric enablement

After successful login, if biometric capability exists, prompt once to enable quick unlock.

Default: enabled if user accepts.

Biometric is a local app-lock mechanism, not a replacement server account credential.

## 6. New device

v1 one-active-device policy:

1. user logs in with username/password;
2. server creates new device/session;
3. server revokes previous device refresh sessions;
4. old device may retain local cached data but cannot successfully synchronize after revocation.

## 7. New calendar year

When a requested transaction belongs to a year with no workbook:

- backend may offer/perform year creation from the approved template;
- do not silently route the transaction to the previous year's workbook;
- generation must update year-dependent formulas/text and initialize system metadata.

If an existing year workbook should be imported instead, import it before relying on automatic generation.
