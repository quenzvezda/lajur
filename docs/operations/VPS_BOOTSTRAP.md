# First VPS deployment

## Scope

The owner authorized first deployment on 2026-10-11. Production runs on Ubuntu
24.04 ARM64 behind the existing host Nginx. Other host services retain their
configuration. The owner's PuTTY key is used by path for SSH authentication;
its contents are not read, copied, converted, or placed in GitHub secrets.

The first image is built natively from verified source commit
`6bf3824d67e048dc0f137179ebab0670aced0507`, with OCI revision/source labels.
Registry publication and restricted CI deployment are separate follow-ups.

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
