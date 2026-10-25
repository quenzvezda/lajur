# Lajur documentation index

`docs/` is the project's system of record. `AGENTS.md` should remain a short routing map.

## Product

- [Product specification](product/PRODUCT_SPEC.md)
- [UX flows](product/UX_FLOWS.md)
- [Onboarding](product/ONBOARDING.md)
- [Logo and application icons](product/branding/README.md)

## Architecture

- [Data model](architecture/DATA_MODEL.md)
- [Android](architecture/ANDROID.md)
- [Backend](architecture/BACKEND.md)
- [API contract](architecture/API_CONTRACT.md)
- [Authentication and security](architecture/AUTH_SECURITY.md)
- [Excel contract](architecture/EXCEL_CONTRACT.md)
- [Sync protocol](architecture/SYNC_PROTOCOL.md)
- [OCR + local LLM](architecture/OCR_LLM.md)
- [Settings](architecture/SETTINGS.md)

## Development

- [Local development setup](development/LOCAL_SETUP.md)
- [Phase 5/6 emulator smoke test](development/EMULATOR_PHASE5_6_SMOKE.md)
- [Phase 7 Smart Scan smoke test](development/PHASE7_SMART_SCAN_SMOKE.md)
- [Phase 8 local-model development](development/PHASE8_LOCAL_MODEL.md)
- [Smart Scan and editor UX smoke test](development/SMART_SCAN_UX_SMOKE.md)
- [Data/Workbook menu smoke test](development/WORKBOOK_MENU_SMOKE.md)
- [Home and Back navigation smoke test](development/HOME_NAVIGATION_SMOKE.md)
- [Native Android Insight smoke test](development/ANDROID_INSIGHT_SMOKE.md)
- [Android reminders, Settings and History filters](development/ANDROID_UI_POLISH_SMOKE.md)
- [Android app-update smoke test](development/ANDROID_APP_UPDATE_SMOKE.md)
- [Local registration and account recovery acceptance](development/ACCOUNT_REGISTRATION_SMOKE.md)
- [Monthly web transaction CRUD smoke test](development/WEB_TRANSACTION_CRUD_SMOKE.md)
- [Workbook settings smoke test](development/WEB_WORKBOOK_SETTINGS_SMOKE.md)
- [Master CRUD smoke test](development/WEB_MASTER_CRUD_SMOKE.md)
- [Local extraction model candidates](reference/LOCAL_MODEL_CANDIDATES.md)
- [Evidence-first Smart Scan decision](adr/0007-evidence-first-smart-scan.md)
- [Known issues](development/KNOWN_ISSUES.md)

## Operations

- [Deployment](operations/DEPLOYMENT.md)
- [First VPS deployment and HTTPS](operations/VPS_BOOTSTRAP.md)
- [Backend CI/CD and rollback](operations/BACKEND_RELEASE.md)
- [CI component selection and manual runs](operations/CI.md)
- [Local account registration and recovery operations](operations/LOCAL_ACCOUNTS.md)
- [Signed Android release builds](operations/ANDROID_RELEASE.md)
- [Backup and recovery](operations/BACKUP_RECOVERY.md)

## Security

- [Threat model](security/THREAT_MODEL.md)
- [Public repository audit, 2026-10-10](security/PUBLIC_REPOSITORY_AUDIT_2026_10_10.md)

## Testing

- [Test strategy](testing/TEST_STRATEGY.md)
- [Acceptance criteria](testing/ACCEPTANCE_CRITERIA.md)

## Decisions and plans

- [ADRs](adr/README.md)
- [Execution plans](exec-plans/README.md)
- [Active MVP plan](exec-plans/active/0001-mvp.md)
- [ADR 0008 — read-only web viewer](adr/0008-read-only-web-viewer-and-browser-session.md) (proposed)
- [ADR 0009 — browser monthly transaction mutations](adr/0009-browser-monthly-transaction-mutations.md)
- [ADR 0010 — structured workbook settings](adr/0010-structured-workbook-settings.md)
- [ADR 0011 — guarded Master and supplemental report](adr/0011-guarded-master-and-supplemental-report.md)
- [ADR 0012 — local registration and recovery codes](adr/0012-local-registration-and-recovery-codes.md)

## Reference

- [Observed workbook structure](reference/WORKBOOK_OBSERVED_STRUCTURE.md)
- [Monthly workbook normalization](reference/WORKBOOK_TEMPLATE_NORMALIZATION.md)
- [V4 generated-year verification, 2026-10-04](reference/WORKBOOK_YEAR_VERIFICATION_2026_10_04.md)
- [Updated workbook audit, 2026-10-03](reference/WORKBOOK_SOURCE_AUDIT_2026_10_03.md)
- [Glossary](reference/GLOSSARY.md)
- [Open questions](reference/OPEN_QUESTIONS.md)
- [Research notes](reference/RESEARCH_NOTES.md)
- [Web finance editing research and UX proposal](reference/WEB_FINANCE_EDITING_RESEARCH.md)
- [Agent access to VPS and deployment research](reference/VPS_AGENT_DEPLOYMENT_RESEARCH.md)
- [Registrasi, pemulihan akun, dan Google Sign-In](reference/ACCOUNT_REGISTRATION_RECOVERY_RESEARCH.md)
- [OCR baseline](reference/OCR_BASELINE.md)
