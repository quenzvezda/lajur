# ADR 0005 — One active Android device per account in v1

**Status:** Accepted

## Context

The expected real usage is one person with one phone. Multiple people may use the service, but they have different accounts and different workbooks.

Supporting multiple simultaneous devices per account would add conflict/state complexity with little initial value.

## Decision

v1 permits one active Android device/session family per account.

A successful login from a new device revokes old refresh sessions.

## Consequences

- simpler sync conflict model;
- backend remains multi-tenant;
- transaction revisions remain because stale data/retries/imports still exist;
- multi-device support requires a future ADR.
