# Authentication and app-lock security

## 1. Two separate layers

### Server authentication

Answers:

> Is this Android installation allowed to call this account's API?

Uses username/password initially, then access + refresh sessions.

### Local app lock

Answers:

> May the person holding the already-authenticated phone view Lajur right now?

Uses Android biometric/device credential.

Do not conflate these layers.

## 2. First login

1. user enters username/password;
2. HTTPS API verifies password hash;
3. server registers/currently activates the device;
4. server returns short-lived access token + rotating refresh token;
5. Android stores refresh credential with Keystore-backed encryption;
6. user may enable biometric quick unlock.

The server never receives fingerprint data.

## 3. Token defaults

Recommended initial configuration:

- access token: **15 minutes**;
- refresh token idle timeout: **30 days**;
- refresh token absolute lifetime: **180 days**;
- app auto-lock: **5 minutes**.

These are server/user configuration values, not scattered magic constants.

## 4. Refresh-token rotation

Each successful refresh:

1. validates current token hash/session;
2. revokes/replaces it;
3. issues a new opaque high-entropy refresh token;
4. updates last-use/expiry state.

Re-use of a replaced token should be treated as suspicious and revoke the relevant session family when practical.

## 5. One-active-device policy

v1:

- successful login from a new device revokes old refresh sessions for the account;
- old device may still have an unexpired access token for a short interval; sensitive endpoints should also enforce device/session status where practical;
- after revocation is observed, old client requires username/password login.

The policy can be relaxed later via ADR.

## 6. App unlock

Foreground unlock uses `BiometricPrompt`.

Default allowed authenticators:

- strong biometric;
- device credential fallback when enabled.

Suggested user setting:

- Biometric unlock ON;
- Device credential fallback ON;
- Auto lock 5 minutes.

The app does not ask for fingerprint on every screen transition.

## 7. Background sync

Background sync must not trigger biometric UI.

A refresh credential needed for background networking must be decryptable by an appropriate Keystore-backed mechanism without requiring interactive biometric authentication each time.

The app lock protects foreground disclosure, not network scheduling.

## 8. Share intent

Incoming image share:

- if local app session is unlocked, proceed to scan;
- if locked, hold only the minimum temporary URI state and show the app-lock gate;
- after unlock, process;
- on cancellation, do not display sensitive content.

## 9. Password storage

Never store plaintext/reversible passwords.

Use ASP.NET Core password hashing/Identity facilities with current secure defaults. Do not invent a hashing scheme.

## 10. Transport

Production base URL:

`https://finance-api.<operator-domain>`

(or another dedicated subdomain chosen by the operator).

The existing host reverse proxy/Certbot terminates TLS.

Recommended:

- public ports: 80 only for HTTPS redirect, 443 for HTTPS;
- API container port bound to loopback/private Docker networking only;
- no plain HTTP Android API configuration in production.

Certificate pinning is not required for v1 because its operational rotation cost is not justified by the threat model.

## 11. Server keys/secrets

Persist outside ephemeral container filesystem:

- JWT/signing material;
- ASP.NET Data Protection keys (`keys/data-protection`, used by browser cookies/antiforgery);
- production secrets;
- auth SQLite;
- workbook data/backups.

Do not bake secrets into the Docker image or repository.

## 12. Password recovery

Local registration/recovery is defined by ADR 0012. Registration defaults to
single-use invitations; every active existing user may issue an invitation after
password reauthentication. New accounts receive eight one-time recovery codes.
Forgotten-password recovery requires username plus a saved code, never username
alone. Recovery cancels the old code set and Android/browser sessions and requires
normal login. Regenerating codes requires the current password.

Operator-assisted CLI reset remains the fallback when all recovery evidence is
lost; verify ownership separately. It invalidates recovery codes and unused
invitations. Email/Google login are not implemented in this slice. Changing a
known password revokes sessions but retains saved recovery codes.

Auth schema upgrades are additive/versioned metadata only; account UUIDs and
existing financial workbooks remain untouched. Anonymous and authenticated
account commands have bounded input and rate limits, and sensitive responses are
`no-store`. Browser commands use antiforgery.

## 13. Browser (web viewer) session

See ADRs 0008 and 0009. The workbook web UI uses its own cookie scheme, separate from Android JWTs:

- browser login only verifies the password; it never creates, rotates or revokes Android devices
  or refresh sessions, so signing in on a PC does not sign the phone out;
- cookie `__Host-lajur-web`: HttpOnly, Secure, SameSite=Lax, 12 h sliding, non-persistent;
- the cookie holds a security stamp derived from the password hash, revalidated per request, so a
  password reset or disabled account ends every browser session;
- login/logout forms use antiforgery tokens; failed and successful credential POSTs share the
  per-IP login limit; return URLs must be local;
- `/api/v1` does not accept the browser cookie and keeps its JSON 401 contract;
- browser report/transaction routes under `/web/books/{year}` require `LajurWeb`,
  return 401 when the browser session ends, and never accept a bearer token alone;
- browser POST/PUT/DELETE require the page antiforgery token in `X-Lajur-CSRF`;
  the authenticated user supplies ownership and writes carry source `WEB`, with
  no Android device ID. Financial JSON responses are `no-store`;
- HTML pages send CSP (`default-src 'self'`, no inline script), `frame-ancestors 'none'`,
  `nosniff`, `no-referrer` and `Cache-Control: no-store`.

## 14. Rate limiting

Apply rate limits at minimum to:

- login;
- refresh abuse patterns;
- import endpoints.

Return generic auth failure messages that do not disclose whether a username exists.
