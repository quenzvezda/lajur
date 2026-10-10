# Monthly web transaction CRUD smoke test

Use a disposable account with the sanitized fixture or a generated test workbook.
Never run destructive acceptance steps against the owner's real ledger. The web
entry point is `/masuk`, then `/buku/{year}#okt` (or another test month).

## Entry and editing

1. Open an empty month. Confirm the six-field editor is above the table and its
   date belongs to that month. Existing Master values populate type/payment and
   the category suggestions; payment may be left empty.
2. Enter a known expense category, `15000` (or `15.000`) and a synthetic description.
   Save with Ctrl+Enter. Confirm one row, exact `15.000`, refreshed totals/pivot,
   success status and cleared amount/description.
   Check that typing `123123` displays `123.123`; inserting/deleting in the middle
   retains the caret and exact amount. Deleting next to a separator must not get
   stuck. Pasted decimal/scientific text must not become a valid integer.
3. Toggle the lock beside Pembayaran or Kategori. Test payment only, category
   only, both and neither; only locked selections remain after save, while amount
   and description clear. Inputs stay editable; buttons expose their pressed
   state and meaningful keyboard/hover labels. Tab/Shift+Tab work.
4. Duplicate a row, change its amount/description, and save. Confirm a second ID
   and no change to the original. Duplicate is a draft until saved.
5. Sort descending by amount and filter by the second description. Edit that row
   and save. Confirm the correct UUID changed, the first row did not, and sorting,
   search/filter and totals retain their meanings.
6. Change type in a draft. An incompatible category must clear and require review.
   Unknown category, noninteger/nonpositive amount and overlong description must
   produce a correctable error without losing the rest of the draft.
7. Edit a date to another month in the same year; review the move confirmation.
   Confirm the same ID/revision advances in the destination. `Lihat transaksi`
   opens the destination and explicitly clears that destination's filters.
8. Delete a disposable row through the dialog. Check the date/category/amount and
   confirm deletion. Totals update; the workbook contains a tombstone and audit.
9. With the default sort, create dates 10, 6 and 7. Confirm the order is 10, 7, 6;
   another day-10 row appears before the earlier day-10 row. Reset restores this
   order; an explicit alternative sort is retained after save.
10. Search Master categories in the application combobox; select with pointer
    or ArrowDown/Enter. Escape/blur closes it. Changing type scopes the options
    and clears an incompatible category. Native datalist must not appear.
11. In the Jumlah (Rp) filter, type `100000` and `1000000` in Dari/Sampai.
    Confirm `100.000`/`1.000.000` while typing and after reopening; apply and check
    exact inclusive results. Empty bounds and zero work; decimals and reversed
    ranges require correction. Entry and filter monetary inputs align right.

## Conflict, retry and draft retention

- Hold the first create response, then submit five valid transactions. The form
  stays enabled and clears immediately; one is sending and four are waiting.
  Release the response and confirm FIFO, five distinct IDs and five saved rows.
  Type an unsent sixth draft during sending; its fields, focus and caret survive
  report refreshes. Category/payment retention still applies.
- The compact queue summary remains collapsed during sends and shows pending,
  saved or needs-attention counts. Click/keyboard opens original details and
  recovery actions; outside click, Escape and Tutup close it. Live updates keep
  it open and preserve action focus. Check its desktop/mobile bounds and dark
  theme. Completed history stays behind the summary until explicitly cleared.
- Lose a queued create response after server acceptance. Later entries wait;
  inspect the outcome, confirm one canonical row, then resume later commands.
  Switch months while paused: the queue remains visible and retains its data.
- Reject a create, correct it from the queue, and confirm the same ID is reused.
  Retry/discard are explicit; uncertain/sending entries cannot be discarded.
  Clearing completed entries removes history only, not canonical transactions.

- Open a transaction in the browser, change it from Android, then save the stale
  browser draft. The conflict shows starting, server and draft values. Choosing
  server discards the draft only after confirmation; retaining the draft requires
  explicit review and a second save against the displayed server revision.
- A stale delete must show the changed server version for review before another
  delete attempt. It must not automatically delete the latest version.
- Simulate a response lost after a server create/update/delete. The UI must
  distinguish an unknown outcome and offer inspection. Exact stored content
  confirms a save; absence confirms a delete. Retry create retains its UUID;
  inspection must not duplicate a row or increment a completed update again.
- Switch months and return while a draft is unsaved. It remains in tab memory.
  `Muat ulang data` refreshes the canonical report without discarding the draft.
  Navigating away/reloading the whole page warns; drafts are not persisted to
  localStorage and cannot survive an explicit discard/reload.
- An unindexed or mismatched legacy row remains readable; edit/delete are disabled
  with a reconciliation hint. Page/report GETs must not write the XLSM.

## Screens and security

- At 390 px, inputs stack vertically, remain labelled and readable, and the page
  has no horizontal overflow outside the table's own scrolling area. Check light
  and dark appearance, keyboard focus, touch actions and delete dialog.
- Browser-only cookies cannot call `/api/v1`; JWT-only clients cannot call `/web`.
  Unsafe browser methods without valid antiforgery return 400. Anonymous/revoked
  browser JSON requests return 401; no financial JSON is cached.
- Web login and edits leave the Android device session active. Neither path can
  access another account's workbook.
- Choose Terang/Gelap/Sistem in the theme picker. Explicit choice overrides the
  OS, survives reload, and applies across browser pages; switching keeps drafts.
- Save toast disappears after six seconds, pauses on hover/focus and can be
  closed immediately. Its action and message have visible spacing.
- Hover the daily/year chart: tooltip appears only over a hit target and closes
  on pointer exit, scroll, Escape, lost window focus or a sheet change.
- In a wide window, the daily chart follows the table with the normal 16 px gap,
  regardless of category/sidebar height. At narrow widths the columns stack.
  Check edit/copy/delete square SVG buttons, hover titles, keyboard labels and
  44 px mobile touch targets; each icon must retain its existing action.

## Recorded validation — 2026-10-10

Backend automated checks cover browser CRUD/idempotency, CSRF/auth separation,
tenant isolation, web/Android revision conflicts, invalid amount/year/category,
exact integer strings, cache freshness and audit actor. Workbook tests cover
duplicate-content identities, shifted/moved rows, legacy migration, mismatched
content and preservation. Real-browser acceptance uses isolated local test
storage with only synthetic fixture data; screenshots and temporary tooling stay
under ignored `private/output/`, outside production source and CI requirements.

The completed run passed 18 unit, 43 integration and 68 workbook tests, plus
Chromium keyboard CRUD, sorted/filtered identity, web/Android conflict, month
movement, stale-delete review, lost create/update/delete responses, draft
navigation/refresh, and 390 px light/dark checks without page errors.
