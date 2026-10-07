# ADR 0008 — Read-only web workbook viewer with a separate browser session

**Status:** Proposed (implemented as an MVP slice on 2026-10-07; awaiting owner review)

## Context

The owner wants to see workbook information from a PC browser (monthly tables, category pivot,
Rekap, analytics) without OneDrive/Azure/Microsoft Graph. ADR 0005 allows one active Android
device per account, and Android login revokes earlier device sessions. A browser login that reused
`LoginAsync` with a new device ID would sign the phone out. Accepting a cookie on the Android API
without session validation would also widen the API's attack surface.

## Decision

1. The viewer is served by the existing ASP.NET Core host as Razor Pages (`/masuk`, `/buku/{year}`,
   `/keluar`) plus local static files. No new service, container, CDN or frontend build.
2. Browser authentication is a **separate cookie scheme** (`LajurWeb`) that only verifies the
   password (`IBrowserAuthService`). It never creates, rotates or revokes Android devices or
   refresh sessions. A browser session does not count as the "one active Android device".
3. The JWT bearer scheme stays the default for `/api/v1`. Browser cookies are not accepted by the
   API; unauthenticated API calls keep the existing JSON 401 contract.
4. The cookie carries a security stamp derived from the stored password hash and is revalidated on
   every request: password reset or account disablement ends all browser sessions.
5. Cookie: `__Host-` prefix, HttpOnly, Secure, SameSite=Lax, 12 h sliding, non-persistent.
   Login/logout use antiforgery tokens; credential POSTs are rate-limited per IP; return URLs must
   be local. Data Protection keys persist under `<storage>/keys/data-protection`.
6. The viewer is **view-only**. A page view reads Master, all twelve A:F tables and the two manual
   balance inputs (B1/D1) in one read under the per-workbook lock, then recomputes the aggregates.
   It never writes, imports, migrates or recalculates the workbook and never trusts cached formula
   or pivot values. Blocks whose formulas are not mapped yet are shown as "Belum tersedia di web".

## Consequences

- Phone and browser can be signed in at the same time; ADR 0005 still governs Android devices.
- Browser sign-in does not rehash passwords, so legacy hashes are only upgraded by Android login.
- Browser sessions cannot be listed or revoked individually yet; password reset is the kill switch.
- Mutation from the web, if ever added, needs a new ADR (audit source, CSRF, conflict handling).
