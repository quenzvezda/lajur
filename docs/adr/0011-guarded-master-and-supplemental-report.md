# ADR 0011 — Guarded Master mutations and supplemental native reporting

**Status:** Accepted for the owner-requested Master CRUD slice, 2026-10-10.

Master categories/payment methods are canonical workbook configuration. Browser
commands add records, change unused identities and delete only dependency-free
records, with a current version, command UUID, preview and atomic backup/save.
The existing cookie/CSRF boundary and settings audit receipt mechanism apply.

Existing Rekap and monthly panels contain fixed labels and row-address helpers;
extending those silently would alter the owner's business formulas and charts.
Create a separate visible managed `LajurMaster` sheet on successful Master writes.
It supplies a complete annual expense-category and payment matrix over the
existing twelve A:F tables, including outside-Master expenses and blank payment.
Its owned formula/layout contract is checked before regeneration; customized or
colliding sheets reject mutation. Excel shared formula serialization is recognized.
All existing reports, chart/pivot/VBA parts and transaction tables are preserved.

Keep category/payment lists contiguous within the recognized OFFSET definitions
and the reader bounds (Master A5:A81 and G5:G24). Append only to empty slots;
delete by compacting only A:C or G after checking references to affected rows.
Unknown/custom bindings, duplicate names/case variants and populated spare slots
are rejected. New names reject Excel criteria wildcards and control characters.

Rename/type changes require no current transactions, transaction-audit history
or native formula/label/address dependencies. Delete additionally requires no
configured budget and no dependent shifted rows. Known aggregate budget ranges
and built-in dynamic dropdown names are safe structural consumers; arbitrary
custom references remain blockers. Server checks run again under the workbook
lock, so a concurrent Android transaction cannot bypass a stale usage display.
No cascade, archive, automatic remapping or identity merge is introduced.

Payment icons use a small local SVG catalogue, with explicit mappings stored in
very-hidden `__MasterIcons` (PaymentMethod, Icon). Name-based defaults are display
only and do not write on GET. Icons can change on used values because financial
identity remains unchanged. The browser report exposes mappings; Android's
existing payment/category contract stays compatible. Refresh updates its cached
choices, while invalid stale offline values remain failed user-review commands
with their original draft, not silently renamed or discarded.

Master snapshots hash its visible configuration, named ranges, icon mappings and
latest `SETTINGS_MASTER` audit ID. Ledger usage is rechecked separately. Exact
command replay returns current data without another audit, backup or file rewrite.
BeforeJson records the affected entry and prior version, avoiding oversized Excel
audit cells at full Master capacity; AfterJson records the exact command. Index
tombstones and transaction audit snapshots both guard historical identities.
