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

`lajur.solusi-program.site` (chosen for the first deployment on 2026-10-10)

Actual domain remains deployment configuration. The first ARM64 production
bootstrap uses the separate configuration under `services/api/deploy/` and
loopback port `18080`; see [the VPS runbook](VPS_BOOTSTRAP.md) for deployment
state, TLS, maintenance, and rollback.

## 3. Docker persistence

Mount host directories:

```text
/srv/lajur/data
/srv/lajur/backups
/srv/lajur/templates
/srv/lajur/auth
/srv/lajur/keys
/srv/lajur/staging
```

Container replacement must not remove data/session keys. Every mounted directory except
`templates` must be writable by the container user (`APP_UID`, 1654 in the .NET images);
`/health/ready` fails when one is not.

## 3a. Reverse proxy and forwarded headers

The API trusts `X-Forwarded-For`/`X-Forwarded-Proto` only from loopback and from the networks in
`Lajur:ReverseProxy:TrustedNetworks`. The development compose uses
`LAJUR_TRUSTED_PROXY_NETWORK`; production uses `LAJUR_TRUSTED_PROXY_CIDR`, set to
the dedicated Docker network gateway `/32` rather than a broad bridge range.
This gives per-client login rate limits instead of one shared limit for everyone behind the proxy,
and the `https` scheme the browser cookie (`__Host-`, `Secure`) requires.

Minimal host Nginx location (TLS/Certbot config stays as already managed on the VPS):

```nginx
location / {
    proxy_pass http://127.0.0.1:18080;
    proxy_set_header Host $host;
    proxy_set_header X-Forwarded-For $remote_addr;
    proxy_set_header X-Forwarded-Proto $scheme;
    client_max_body_size 30m;   # workbook uploads
}
```

Browser login to the web viewer requires HTTPS; plain `http://localhost` works only on the
machine itself.
The production configuration assumes DNS-only access to Nginx. Enabling a
Cloudflare proxy requires scoped, trusted real-IP handling first.

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

The current CI builds a verified signed APK after checks pass on `main`.
Successful main builds are automatically published to public GitHub Releases.
The Android app checks these releases and supports verified download/install
from Login and Settings. See [Android release operations](ANDROID_RELEASE.md).
Container registry publication and VPS deployment remain upcoming stages.

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

## 10. Agent-assisted first bootstrap and routine releases

The recommended first deployment is a supervised, one-time bootstrap. The agent
prepares a reviewable host configuration and executes it through temporary SSH
access after the owner grants that access. Inspect the host first, keep the
owner's emergency access available, verify the new service and TLS, then revoke
the temporary bootstrap key. An MCP SSH tool can provide the interface, but
server-side SSH and Linux permissions determine its actual authority.

Routine deployment should use a separate CI key restricted on the VPS to a
root-owned Lajur deploy operation. It should accept only a validated image
digest, update only the API service, check health, and support rollback. Do not
give the deploy identity unrestricted shell, sudo, or Docker daemon access;
membership in the `docker` group grants root-level privileges. Keep the
production Compose configuration distinct from the current local `build:`
configuration, which uses `lajur-api:local`.

The proposed CI sequence is: test on merge to `main`, build and push an image
to the chosen registry, invoke the restricted deploy operation with its digest,
then check readiness. Ordinary image releases should require no interactive
VPS session. Host administration, secret rotation, recovery, and explicit
workbook migrations remain separate operations.

See [the VPS agent access research](../reference/VPS_AGENT_DEPLOYMENT_RESEARCH.md)
for the security reasoning, implementation options, and source references.
