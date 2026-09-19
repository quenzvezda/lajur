# v1 acceptance criteria

These are product-level acceptance criteria. They are intentionally finite and high-value.

## A. Workbook integrity

- [ ] Import of the sanitized legacy-compatible workbook succeeds.
- [ ] Existing monthly transaction tables remain A:F.
- [ ] VBA project remains present after ordinary create/update/delete operations.
- [ ] VBA bytes remain unchanged for ordinary transaction operations.
- [ ] Existing right-side monthly formulas/content are not shifted or overwritten.
- [ ] Table references expand/shrink correctly after create/delete.
- [ ] Exported `.xlsm` opens normally in desktop Excel.
- [ ] `Master` dropdown source data remains intact.
- [ ] Invalid import never replaces the active workbook.
- [ ] A generated new-year workbook has the target year in all known year-dependent formulas/text.

## B. Idempotency/revisions

- [ ] Submitting identical CREATE 10 times with one transaction ID yields exactly one canonical row.
- [ ] Same transaction ID with conflicting create payload returns conflict.
- [ ] Correct expected revision updates once and increments revision once.
- [ ] Stale expected revision does not overwrite canonical data.
- [ ] Delete removes visible monthly-table row and creates audit/tombstone metadata.

## C. Audit

- [ ] CREATE audit exists after canonical create.
- [ ] UPDATE audit records meaningful before/after state.
- [ ] DELETE audit retains deleted before-state.
- [ ] Audit timestamps are UTC.
- [ ] Export includes system audit metadata.

## D. Offline Android

- [ ] Manual transaction can be saved with airplane mode enabled.
- [ ] Pending transaction is visible locally.
- [ ] After network returns, WorkManager synchronizes without reopening the form.
- [ ] Retry after ambiguous network failure does not duplicate workbook row.
- [ ] App restart/reboot does not lose pending outbox operations.

## E. Auth/app lock

- [ ] First login requires username/password.
- [ ] Subsequent foreground entry can use biometric/device credential.
- [ ] Returning within configured grace period does not re-prompt.
- [ ] Returning after default 5-minute timeout prompts again.
- [ ] Background sync works while UI is locked.
- [ ] Login on a new device revokes old refresh session under v1 policy.
- [ ] Password change/reset revokes all refresh sessions.
- [ ] Cross-user API calls cannot access another user's workbooks.

## F. Smart Scan

- [ ] Manual-entry app startup does not initialize the LLM.
- [ ] Smart Scan works without cloud AI calls.
- [ ] After local model is installed/available, scan can run with internet disabled.
- [ ] Extraction never directly saves canonical transaction.
- [ ] Required missing fields block Save.
- [ ] Low/ambiguous field can expose alternative candidate(s).
- [ ] Category can remain null when unsupported by evidence.
- [ ] Incoming Share Intent respects app-lock state.

## G. Import/export

- [ ] First onboarding supports Create New or Import Existing.
- [ ] User can export a real annual `.xlsm`.
- [ ] User can export all years as a zip.
- [ ] Active workbook remains unchanged when upload/validation fails.
- [ ] Import while unsynchronized local outbox exists is blocked or explicitly resolved.

## H. Deployment

- [ ] Backend runs in Docker.
- [ ] Persistent data/auth/keys live outside container layer.
- [ ] API is reached through HTTPS reverse proxy.
- [ ] API container is not directly exposed publicly.
- [ ] GitHub Actions can build/test/image/deploy.
- [ ] Container upgrade does not alter workbook data merely from startup.
