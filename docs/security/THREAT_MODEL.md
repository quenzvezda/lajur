# Concise threat model

The intent is practical security for a private/self-hosted finance app, not a certification exercise.

## 1. Assets

High-value assets:

- annual `.xlsm` workbooks;
- workbook backups;
- account password hashes;
- refresh-token/session material;
- Android cached financial data;
- receipt/payment screenshots while being processed;
- server signing/Data Protection keys.

## 2. Trust boundaries

- Android device ↔ public internet;
- public reverse proxy ↔ private API container;
- API ↔ filesystem;
- API ↔ auth SQLite;
- uploaded workbook ↔ parser;
- shared image URI ↔ Android scan pipeline.

## 3. Main threats and mitigations

### Stolen phone

Risks:

- viewing cached finance data;
- using existing session.

Mitigations:

- biometric/device-credential app lock;
- short foreground grace period;
- Recents preview hiding;
- encrypted sensitive credential storage;
- server device revocation.

### Stolen refresh token

Mitigations:

- opaque high-entropy token;
- store hash server-side;
- rotate on use;
- idle + absolute expiry;
- device/session revocation;
- never log raw token.

### Password attacks

Mitigations:

- established ASP.NET password hasher;
- login rate limiting;
- generic login failure;
- TLS only.

### Cross-user filesystem access

Mitigations:

- authorization on every workbook operation;
- path derived from immutable server user ID;
- never concatenate username or uploaded filename into canonical paths;
- integration tests proving tenant isolation.

### Workbook corruption during save

Mitigations:

- per-user/year lock;
- temp-copy mutation;
- validation;
- backup;
- same-filesystem atomic replace.

### Malicious/corrupt workbook upload

Mitigations:

- strict size limits;
- safe ZIP/OPC parsing;
- decompression limits;
- required-structure validation;
- staging;
- no macro execution;
- active workbook unchanged on failure.

### Cloud privacy leakage

Mitigations:

- no cloud OCR/LLM;
- receipt image not uploaded by default;
- logs redact OCR/financial payloads.

### Server loss/disk failure

Mitigations:

- local version backups;
- recommended external encrypted backup target;
- export capability.

## 4. Explicitly accepted risks in v1

- a rooted/fully compromised Android device can defeat many local protections;
- a fully compromised VPS can access workbooks and auth state;
- same-host backups do not protect against total VPS/disk loss;
- direct concurrent editing of the active server workbook is not supported.

## 5. Security change rule

Any change that introduces:

- cloud inference;
- shared workbook access;
- multi-device concurrency;
- public registration;
- direct network-file editing;

requires threat-model and ADR updates.
