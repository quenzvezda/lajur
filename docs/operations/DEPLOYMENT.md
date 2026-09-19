# Deployment

## 1. Target environment

- Ubuntu Linux VPS;
- Docker already available;
- existing domain;
- existing reverse proxy/Certbot automatic TLS renewal.

Therefore v1 should use Docker rather than a direct systemd-hosted .NET process.

## 2. Network shape

```text
Internet
   │
 :443 HTTPS
   ▼
Host Nginx / existing reverse proxy
   │
   │ http://127.0.0.1:<private-port>
   ▼
Docker: lajur-api
```

The API container port should not be publicly exposed.

HTTP/80 may redirect to HTTPS.

Suggested host:

`finance-api.<domain>`

Actual domain remains deployment configuration.

## 3. Docker persistence

Mount host directories:

```text
/srv/lajur/data
/srv/lajur/backups
/srv/lajur/templates
/srv/lajur/auth
/srv/lajur/keys
```

Container replacement must not remove data/session keys.

## 4. GitHub Actions

Recommended pipeline:

```text
push/merge to main
  ↓
dotnet restore/build
  ↓
unit tests
  ↓
integration/workbook tests
  ↓
docker build
  ↓
push image to registry (e.g. GHCR)
  ↓
SSH deployment step
  ↓
docker compose pull
docker compose up -d
  ↓
health check
```

Android CI can be separate:

```text
Gradle lint/test/build debug/release candidate
```

Do not require emulator UI E2E for ordinary deployment.

## 5. Deployment safety

A backend deployment must not mutate workbooks just because the application starts.

Schema/workbook migration must be an explicit controlled operation with backup and validation.

Never store workbooks inside the Docker image.

## 6. Secrets

Use GitHub Actions secrets and/or host-only environment/secrets.

Do not commit:

- JWT/signing keys;
- production passwords;
- SSH keys;
- real workbooks;
- receipts;
- refresh tokens.

## 7. Logs

Prefer structured stdout logs and Docker log rotation.

Suggested Docker logging bounds:

- max size around 10 MB/file;
- a small retained file count such as 5.

Exact values may be adjusted.

Financial payloads should not be logged at info level.

## 8. Health checks

CI/deploy should verify:

- container up;
- `/health/live`;
- `/health/ready`.

A rollback mechanism should be available by redeploying the prior image tag. Data directories remain version-independent.

## 9. Development environments

Use sanitized workbook fixtures in repository tests.

Do not use the owner's real financial workbook as a committed test fixture.
