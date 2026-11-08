# First VPS deployment

## Scope

The owner authorized first deployment on 2026-10-11. Production runs on Ubuntu
24.04 ARM64 behind the existing host Nginx. Other host services retain their
configuration. The owner's PuTTY key is used by path for SSH authentication;
its contents are not read, copied, converted, or placed in GitHub secrets.

The first image is built natively from verified source commit
`6bf3824d67e048dc0f137179ebab0670aced0507`, with OCI revision/source labels.
Registry publication and restricted routine CI deployment are described in
[backend release operations](BACKEND_RELEASE.md).

## Host configuration

Repository templates are in `services/api/deploy/`:

- `compose.production.yml`: exact local image ID, dedicated external network
  `lajur-production`, loopback-only `127.0.0.1:18080`, readiness probe, resource
  limits, read-only root filesystem and bounded logs.
- `production.env.example`: nonsecret placeholders. Actual configuration is
  `/etc/lajur/production.env`, root-owned mode `0600`.
- `nginx.http.conf`: temporary ACME route with a maintenance response.
- `nginx.https.conf`: HTTPS proxy and HTTP redirect for this hostname only.

Installed Compose file: `/etc/lajur/compose.production.yml`.
Nginx site: `/etc/nginx/sites-available/lajur.solusi-program.site`, linked in
`sites-enabled`. Run `nginx -t` before every reload; restore this site's prior
file if validation fails. Do not replace the global Nginx configuration.

The actual `LAJUR_TRUSTED_PROXY_CIDR` is the dedicated Docker gateway `/32`.
Nginx overwrites client-supplied forwarded headers. Preserve this restriction
when changing networks.

Persistent directories live under `/srv/lajur`. Writable directories
`data`, `backups`, `auth`, `keys`, and `staging` belong to UID/GID `1654`.
The parent is root-owned with group `1654`, mode `0750`. Templates are read-only
to the container, directory mode `0750`, file mode `0640`.
The private clean V4 template is transferred over SSH and verified by SHA-256.
Existing local account databases and finance ledgers are not imported.

## Cloudflare and TLS

The owner creates an A record named `lajur`, pointing to the VPS, TTL Auto,
with **DNS only** (gray cloud). Leave unrelated records unchanged. Verify
external A resolution and absence of a conflicting AAAA record before issuance.

Nginx serves `/.well-known/acme-challenge/` from `/var/lib/letsencrypt-lajur`.
Verify an external HTTP probe reaches that directory. Issue only this hostname:

```bash
sudo certbot certonly --webroot -w /var/lib/letsencrypt-lajur \
  --cert-name lajur.solusi-program.site -d lajur.solusi-program.site \
  --non-interactive --agree-tos \
  --deploy-hook 'nginx -t && systemctl reload nginx'
```

This command reuses the host's existing Certbot account. A new host needs its
own account setup. Activate the HTTPS site only after issuance succeeds.
Certificate paths are `/etc/letsencrypt/live/lajur.solusi-program.site/`.
The existing `certbot.timer` performs renewal; test this certificate in isolation:

```bash
sudo certbot renew --cert-name lajur.solusi-program.site --dry-run
```

Do not enable the Cloudflare orange cloud without first configuring trusted
Cloudflare real-IP ranges for this site. If enabling it later, use Full (strict)
with the valid origin certificate. DNS-only deployment does not require a
zone-wide SSL setting change.

Sources: [Cloudflare subdomain records](https://developers.cloudflare.com/dns/manage-dns-records/how-to/create-subdomain/),
[Let's Encrypt challenges](https://letsencrypt.org/docs/challenge-types/),
[Cloudflare Full (strict)](https://developers.cloudflare.com/ssl/origin-configuration/ssl-modes/full-strict/).

## Health and maintenance

For the additional `.online` hostname, see the rollout record below. The original
`.site` hostname and its certificate remain active.

Acceptance on 2026-10-11: container healthy, external HTTPS live/ready `200`,
HTTP redirect `308`, login form available, unauthenticated API `401`, correct
TLS SAN and certificate expiry 2027-01-09. Certbot's isolated dry-run completed
with all simulated renewals successful; the host renewal timer is active.

```bash
sudo docker compose --env-file /etc/lajur/production.env \
  -f /etc/lajur/compose.production.yml ps
curl --fail https://lajur.solusi-program.site/health/live
curl --fail https://lajur.solusi-program.site/health/ready
```

Web: `https://lajur.solusi-program.site/`.
Android API: `https://lajur.solusi-program.site/api/v1/`.
The owner authorized a new production account, created through the operator
command with a generated password supplied privately via stdin. Credentials
stay in a local ignored directory restricted to the owner and Windows SYSTEM.
Do not put passwords in shell arguments or logs. No local ledger migration is
part of account creation. An external CSRF-protected browser login succeeded
and issued the expected HTTPS-only session cookie; no workbook was changed.

For later deployments, retain the previous exact image ID, back up persistent
data using [the recovery runbook](BACKUP_RECOVERY.md), validate a newly built
image's source/architecture, update only `LAJUR_IMAGE`, and run Compose `up -d
--wait`. If health fails, restore the previous image ID and rerun Compose.
Never use `down -v`, prune unrelated images, or alter workbook data during boot.
Image rollback does not roll back workbook data; recover data separately when
an explicit migration requires it.

Before relying on production as the only copy, configure off-host encrypted
backups and verify a restore. This bootstrap does not claim those are installed.

## Additional .online domains (2026-10-25)

The owner authorized Nginx changes and certificate issuance for both applications,
with `.site` kept active. Separate sites were added under `/etc/nginx/sites-available/`
and linked in `sites-enabled`:

| Hostname | Upstream | DNS mode |
| --- | --- | --- |
| `app.solusi-program.online` | ERP `http://localhost:8080` | Cloudflare proxied |
| `lajur.solusi-program.online` | Lajur `http://127.0.0.1:18080` | Cloudflare proxied |

The ERP site preserves its existing `/minio/` proxy to `localhost:9000`.
Neither original `.site` site redirects to `.online`; existing Android API
addresses remain usable. Browser sessions are hostname-specific, so users should
log in again on the new hostname.

Both new HTTP sites serve ACME challenges from `/var/lib/letsencrypt-online`
and redirect other paths to HTTPS on the same hostname. Separate Let's Encrypt
certificates were issued using the existing account and `certonly --webroot`.
Their lineages are `/etc/letsencrypt/live/app.solusi-program.online/` and
`/etc/letsencrypt/live/lajur.solusi-program.online/`; both expire 2027-01-23.
The existing `certbot.timer` handles renewal. Each new renewal configuration
uses the shared webroot and `nginx -t && systemctl reload nginx` deploy hook.

Only the two new HTTPS server blocks include
`/etc/nginx/snippets/cloudflare-online-realip.conf`. It trusts the official
Cloudflare IPv4/IPv6 networks fetched on 2026-10-25, reads `CF-Connecting-IP`,
and uses `real_ip_recursive off`. Upstream `X-Forwarded-For` is overwritten with
the resolved `$remote_addr`; direct origin requests cannot supply a trusted
Cloudflare client address. Refresh the scoped network list from
`https://www.cloudflare.com/ips-v4` and `/ips-v6` when Cloudflare changes it,
then validate Nginx before reloading. The `.site` Nginx files were left byte-identical.

`/etc/lajur/compose.production.yml` now allows both Lajur hostnames as well as
localhost/loopback. Only the API container was recreated, using the same image
and persistent mounts. Host operations acquire `/run/lajur-deploy/lock` to avoid
overlapping CI deployments. Cloudflare account SSL settings were not changed;
the new origin certificates support Full (strict), which the owner should verify
in the Cloudflare dashboard.

Before any host configuration changes, a root-only backup was created at
`/var/backups/lajur-domain-online-20261025T053332Z/`. Its mode-0600
`config-before.tar.gz` contains `/etc/nginx`, the production Compose file and
Certbot renewal configurations. A separate original Compose copy and `.site`
configuration checksums are stored alongside it. Rollback removes only the two
new `sites-enabled` links, restores the saved Compose file, validates/reloads
Nginx and recreates only the API service under the deployment lock. Issued
certificates can remain on disk; do not overwrite existing `.site` certificates
or restore financial data as part of this configuration rollback.

Acceptance: Nginx syntax, archive integrity, unchanged `.site` checksums, direct
origin TLS hostname verification for both new domains, public ERP pages on both
suffixes, public Lajur live/ready and login page on `.online`, `.site` readiness,
same-host HTTPS redirects, and unauthenticated Lajur API `401` passed. User login,
ERP business operations and Android synchronization were not exercised.
Isolated `certbot renew --cert-name <hostname> --dry-run
--no-random-sleep-on-renew` succeeded for each new certificate. The deploy hook
was syntax-checked and run during issuance; dry-run renewal did not execute it.

### MinIO alias and ERP configuration observation

The owner subsequently authorized adding `minio.solusi-program.online` and
inspecting the ERP systemd/configuration paths. A separate Nginx site proxies
to `http://localhost:9000`, preserving the existing MinIO `Host localhost:9000`
behavior, with the same scoped Cloudflare real-IP snippet used by the other
`.online` sites. HTTP ACME uses `/var/lib/letsencrypt-online`. The separate
webroot certificate expires 2027-01-23 and uses the existing timer and
validation/reload renewal hook. MinIO `.site` remains unchanged.

Backup: `/var/backups/minio-domain-online-20261025T053859Z/config-before.tar.gz`
(root-only), containing pre-change Nginx and renewal configurations. Checksums
also verify unchanged `.site` Nginx files, ERP unit and external properties file.
Public `.online` and `.site` MinIO `/minio/health/live` checks, direct-origin
`.online` TLS verification, ERP `.online` availability, Nginx validation and
isolated MinIO certificate renewal dry-run passed. Signed object downloads and
uploads were not tested.

ERP runs through `/etc/systemd/system/erp.service`, with working directory
`/home/ubuntu/app` and JAR `/home/ubuntu/app/app.jar`. Its external
`/home/ubuntu/app/application.properties` contains
`storage.minio.endpoint=https://minio.solusi-program.site`. However, the deployed
JAR's `MinioProperties` annotation binds prefix `minio`; its bundled
`BOOT-INF/classes/application.yaml` defines `minio.endpoint` and
`minio.presigned-endpoint`, defaulting to `http://localhost:9000` via environment
placeholders. No matching process environment overrides or explicit Spring
configuration/profile overrides were found during this inspection. This suggests
stale external property naming; effective storage behavior needs a focused ERP
follow-up before changing endpoints. Credentials were not displayed. ERP
configuration was left unchanged and the service was not restarted.
