# Glossary

**Active workbook**  
The server-side canonical `.xlsm` for one user/year.

**App lock**  
Foreground privacy gate using biometric/device credential. Not the same as server login.

**Canonical transaction**  
A transaction that has successfully reached and been committed to the workbook.

**Draft**  
Editable transaction form, especially one produced by Smart Scan, not yet saved.

**Financial source of truth**  
The authoritative storage of finance records. In Lajur this is the Excel workbook.

**Master data**  
Categories, transaction types, payment methods, budgets, and related domain values read from workbook `Master`.

**Outbox**  
Durable Android queue of pending create/update/delete commands.

**Revision**  
Monotonic application version for a canonical transaction, stored in hidden workbook metadata.

**Smart Scan**  
Local OCR + local LLM assisted form completion.

**System sheets**  
Hidden workbook sheets such as `__AppIndex` and `__AuditLog`.

**Workbook generation**  
Creating a new annual workbook from a compatible template while patching year-dependent content.

**Workbook schema version**  
Application compatibility version for hidden metadata/template behavior; distinct from calendar year.
