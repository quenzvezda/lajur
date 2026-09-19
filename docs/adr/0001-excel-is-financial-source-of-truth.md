# ADR 0001 — Excel is the financial source of truth

**Status:** Accepted

## Context

The owner already has a macro-enabled annual finance workbook with monthly tables, Master data, formulas, reports, and macros. The purpose of Lajur is to make mobile input easier, not replace the workbook with a conventional database.

## Decision

Canonical financial transactions remain in per-user/per-year `.xlsm` workbooks.

Server SQLite may store authentication/infrastructure metadata but not a canonical finance ledger.

Android Room is a cache/outbox only.

## Consequences

Positive:

- exported workbook remains independently useful;
- no app vendor lock-in;
- existing reporting investment remains useful.

Costs:

- workbook writes require strong serialization/backup/integrity tests;
- formula recalculation cannot be treated like SQL computed columns;
- multi-writer concurrency is intentionally constrained.
