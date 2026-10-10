# Browser Master CRUD acceptance

Use a disposable account with the synthetic fixture. Keep exported books and
screenshots under ignored `private/output/`; never edit the owner's ledger as
an acceptance test.

1. In Master, open Kelola kategori or Kelola pembayaran. Search by name. Check
   visible usage/history/reference counts, capacity and blocked deletion reasons.
2. Add an expense category with optional budget. Money groups while typing.
   Preview before saving. Duplicate/case-only duplicates, invalid names, wildcard
   characters, noninteger/negative/overflow budget and unsupported types reject
   without mutation. Income categories do not carry expense budgets.
3. Rename a new unused category, change its type where permitted and clear its
   budget. Delete only after its budget is unset and no dependency remains.
   A configured zero remains a budget and must also be explicitly cleared.
4. Add a payment method, select a local icon and preview/save. Rename/delete an
   unused method. Removing a middle record compacts only its Master list; the
   remaining dropdown is contiguous and icon mappings follow their names.
5. New choices appear in the web transaction editor. Record a disposable row;
   rename/delete now block even from a stale management snapshot. Used names
   stay locked, while category budget or payment icon can still be edited.
   Canonical transaction text/identity/history are never cascaded or remapped.
6. A concurrent tab's Master mutation requires loading current data and a new
   preview; its draft stays available. Losing a response after acceptance is
   recovered by operation receipt. A receipt 404 enables retry with the same UUID.
   Close/reopen preserves in-memory draft/uncertain command; leaving warns.
7. Check keyboard labels, search focus, Escape/close, preview/back actions, 390 px
   light/dark layouts and usable buttons. Transaction drafts survive management
   save/refresh, and monthly/all-transactions payment cells display the selected
   SVG icon beside the unchanged method text.
8. Unknown/custom named ranges, Master gaps, populated spare slots, capacity
   limits and custom/changed LajurMaster formulas reject structural writes.
   Direct/shared/range/defined-name/dynamic dependencies block unsafe identities.
9. Android Master GET after refresh exposes the new values. A stale offline
   create against a removed unused value receives a permanent validation error;
   no ledger row is created. Existing outbox handling keeps it for user correction.
10. Export the changed synthetic workbook. Native Excel opens/recalculates and
    RefreshAll completes. Verify the new category/payment in LajurMaster monthly
    and annual totals. Existing Rekap, monthly cells, VBA, pivot/chart/control
    parts, formulas and names stay preserved; no new formula errors appear.

The complete report is a managed additional sheet; the original fixed reports
are retained. Arbitrary identity merge, archive and rename of used values remain
outside this slice.

## Recorded validation — 2026-10-10

Backend coverage totals 172 cases: 32 unit, 49 integration and 91 workbook,
including focused final Master checks. Chromium passes the management, dependency,
dropdown/icon, draft, conflict, receipt/retry and responsive flows without page
errors. Native Excel verifies monthly/annual coverage of new Master values and
complete totals, with no additional formula errors versus the synthetic fixture.
Original package/report/VBA preservation is checked in workbook tests. Docker
readiness is 200, health is healthy and served assets match the source.
