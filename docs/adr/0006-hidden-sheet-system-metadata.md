# ADR 0006 — Store transaction system metadata on hidden sheets

**Status:** Accepted

## Context

A prior design considered adding hidden `TransactionId`, `Revision`, and timestamp columns to monthly transaction tables.

Inspection of the real workbook shows the transaction table occupies A:F but the same monthly sheet uses columns to the right for pivots/projections/helper content. Widening the table could shift or collide with those structures.

## Decision

Keep visible monthly transaction tables exactly A:F.

Store application identity/revision data in hidden `__AppIndex` and history in hidden `__AuditLog`.

## Consequences

- visible workbook layout remains stable;
- metadata reconciliation is more complex than a transaction-ID column;
- import/export carries identity/audit data without exposing it in the normal transaction grid;
- direct concurrent external editing remains out of scope; export/edit/import is the supported manual-edit path.
