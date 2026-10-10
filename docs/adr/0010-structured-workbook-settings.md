# ADR 0010 — Structured workbook budget and projection settings

**Status:** Accepted for the owner-approved settings slice, 2026-10-10.

## Context

The browser can edit transactions, but budget and income estimates are still
manual Excel inputs. The owner requested monthly meal working days/daily rate,
salary/freelance estimates and payout days, and category budgets. Arbitrary cell
editing would expose formulas and unrelated workbook panels to accidental writes.

## Decision

Expose cookie-only, antiforgery-protected structured settings commands. The
server maps expense categories to Master C cells and supported monthly income
sources to literal L/M inputs. The recognized meal formula `=$M$10*$M$11`
retains its formula; only M10/M11 are writable. Formula-backed, unknown or
ambiguous bindings stay read-only. Clients never submit cell addresses/formulas.

Budget is one monthly target per expense category in the selected annual book,
used for Jan–Dec comparisons. Projection assumptions belong to one month.
Nullable input means not configured and remains distinct from zero. Estimates
never create actual income transactions. IDR travels as exact integer text.

Each scope uses an opaque version hash of mapped input/layout content and its
latest settings audit operation. This detects changed/reverted settings without
making unrelated transaction writes conflict. A command UUID and full payload
are stored in the existing append-only `__AuditLog`: its AuditId is the command
UUID, Action is `SETTINGS_BUDGET` or `SETTINGS_PROJECTION_<month>`, BeforeJson is
the prior snapshot, AfterJson is the exact structured command, and TransactionId
is empty. Identical retries return current settings without another audit;
UUID reuse with different content is rejected. A tenant-scoped receipt GET lets
the browser inspect uncertain outcomes even after later settings changes.

Settings writes share the workbook lock, temporary-copy validation and atomic
promotion. A verified pre-settings backup is required before promotion. Existing
audit/index schema and transaction revisions remain unchanged; an absent index
is initialized empty without assigning identities to legacy rows.

## Consequences

The browser retains drafts/uncertain commands only in tab memory and warns before
leaving. Conflicts show server versus draft and require a fresh preview before
another save. Unknown responses require receipt inspection before retry. Excel
recalculates preserved formulas on opening; web reports read fresh literal inputs
and compute estimates directly rather than trusting cached values.

This slice does not add category/payment CRUD or change Master named ranges.
That remains a separate guarded template/lifecycle implementation.
