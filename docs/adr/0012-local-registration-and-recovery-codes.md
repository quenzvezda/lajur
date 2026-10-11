# ADR 0012 — Local registration, invitations and recovery codes

Status: accepted for implementation on 2026-10-11.

## Decision

Extend operator-only accounts with local self-registration and no-email recovery.
The owner selected invitation registration and permits all existing users to
issue invitations after password reauthentication. Defaults: `invite` mode,
100-account host cap, five active invitations per issuer, expiry seven days,
single use. `closed` and `open` remain host configuration choices.

Registration creates auth metadata only and returns eight 128-bit random recovery
codes once. Accounts explicitly log in afterward and use normal workbook
onboarding. Recovery proves possession of an existing code, sets a new password,
invalidates the whole code set and Android/browser sessions, then requires login.
Existing accounts can generate codes after password reauthentication. Regenerating
codes replaces the old set; changing a known password preserves saved recovery
codes. Operator-assisted reset cancels recovery codes and unused invitations.

Recovery/invitation secrets are purpose-separated hashes in auth SQLite. Codes
are never retrievable in plaintext after the response. Single-use consumption
and password/session changes occur in a SQLite transaction. Anonymous/account
requests have bounded input and rate limits; error responses do not reveal
whether the recovery username exists. Username availability can be reported
during registration because usernames are the public account identifiers.

Keep existing immutable UUIDs, password hashes and workbook ownership. Auth
schema v1 adds only recovery, invitation, self-registration marker and version
tables transactionally; old binaries can ignore them. Do not delete/recreate
auth storage. This additive infrastructure upgrade may run at startup; it does
not modify workbooks. Later non-additive upgrades need a separate migration plan.

Self-registered accounts require a separate approved `registration-template.xlsm`;
there is no fallback to the owner's private template. It is derived from the
committed synthetic fixture with neutral budget/projection amounts. Normal year
generation clears fixture transaction rows. Existing operator accounts retain
their template and existing workbooks remain untouched.

## UX and limits

Web: `/daftar`, `/pulihkan`, `/akun`; browser commands use antiforgery and password
reauthentication. Android: Login → create/recover, Settings → account security.
Sensitive screens prevent Android screenshots and keep passwords/codes in memory,
not saved-state or Room. Copying codes is explicit and marked sensitive in the
Android clipboard. Users must save an independent copy; loss of all recovery
evidence still requires operator verification. No fake email reset or security
questions are offered.

No Google/Firebase/SMTP integration in this slice. No changes to offline outbox,
one-active-Android-device policy, financial storage or cloud inference policy.
After a lost registration/code-generation response, users can log in and generate
a new set; the UI warns against assuming a timed-out mutation did not complete.

References: [research](../reference/ACCOUNT_REGISTRATION_RECOVERY_RESEARCH.md),
[OWASP recovery](https://cheatsheetseries.owasp.org/cheatsheets/Forgot_Password_Cheat_Sheet.html).
