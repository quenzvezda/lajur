# ADR 0004 — Server authentication and biometric app lock are separate

**Status:** Accepted

## Context

The desired UX resembles banking/e-wallet apps: username/password establishes account login, while fingerprint/device credential provides convenient subsequent unlock with an idle timeout.

Background sync still needs to work while the UI is locked.

## Decision

Use:

- server username/password + access/refresh sessions for API identity;
- local BiometricPrompt/device credential for foreground app unlock.

Do not require biometric interaction for background WorkManager sync.

## Consequences

- good day-to-day UX;
- refresh credentials need secure non-interactive storage for background use;
- local lock timeout is independent of access-token lifetime.
