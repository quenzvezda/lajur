# Backend release and deployment

## Pipeline

On a trusted push to `main` affecting backend/web, or a manual CI run on `main`
with `target: backend` or `all`:

1. Backend unit/integration/workbook tests and the existing container smoke pass.
2. A GitHub-hosted `ubuntu-24.04-arm` runner builds the production ARM64 image.
3. The same image passes the synthetic container import/auth/readiness smoke.
4. A job with `packages: write` publishes `ghcr.io/quenzvezda/lajur-api:sha-<commit>`.
5. A separate `backend-production` job, with only `packages: read`, deploys the
   immutable registry digest through a restricted SSH key.
6. The VPS checks source/revision labels, ARM64 architecture and UID 1654, creates
   a consistent local snapshot, updates only the API image and verifies HTTPS
   liveness/readiness. Failed deployment restores the prior image configuration.

PRs and other branches cannot publish/deploy. The GitHub environment allows only
the `main` branch. Main workflow runs are not automatically cancelled mid-deploy;
the production job also has a non-cancelling concurrency group. The gate serializes
host operations and rejects stale run numbers or conflicting retries.

Android-only and ordinary documentation changes skip backend tests, image builds,
publication and deployment. Web Razor Pages and static assets are part of the API
image, so web changes still rebuild/deploy that image. Android signing/publication
continues independently after its own checks. See [CI component selection](CI.md)
for path rules, manual dispatch and required checks.
Backend deployment does not rebuild or replace workbook templates.

## Credentials and host setup

GitHub environment `backend-production` contains:

| Name | Kind | Purpose |
| --- | --- | --- |
| `BACKEND_SSH_PRIVATE_KEY` | Secret | Dedicated CI identity, separate from owner SSH key |
| `BACKEND_SSH_HOST` | Variable | Production SSH address |
| `BACKEND_SSH_KNOWN_HOSTS` | Variable | Host Ed25519 public key pinned through trusted owner SSH |

The deploy identity `lajur-deploy` has no Docker membership and no general sudo.
Its home and authorized keys are root-owned. The key uses `restrict` plus a
forced root-owned gate at `/usr/local/sbin/lajur-deploy`, installed from
`services/api/deploy/deploy-gate.py`. Sudo allows this gate without arguments
and preserves only the additional `SSH_ORIGINAL_COMMAND` variable.
The gate accepts exactly `deploy sha256:<64 hex> <40-hex commit> <positive run>`.
It fixes all paths, image repository and Compose arguments itself.

The deployment client's private key stays in a temporary directory on the
GitHub runner and is removed after use. Registry authentication uses this job's
short-lived `GITHUB_TOKEN`, sent through encrypted SSH stdin; passwords/tokens
are never shell arguments or printed. The root gate uses a temporary root-only
Docker auth directory for the pull and removes it before stopping the service.
The GHCR package may remain private; neither a host PAT nor public visibility is
required. No owner PPK, template, ledger or receipt enters Actions or the image.

Gate changes require a deliberate operator install; ordinary CI cannot replace
the root-owned gate, Nginx, Compose file, host configuration, or other services.
Disable deployments by removing the authorized key or GitHub environment secret.
Rotate only the dedicated CI key; preserve the owner's administrative access.

## Snapshot, downtime and rollback

Pull/identity validation happen while the current API runs. During the final
replacement, stop the API, snapshot `data`, `auth`, `keys`, `templates`, and
protected `backups`, then start the new image. There is a brief maintenance
window; Android offline entry remains available and sync can retry.

Snapshots are root-only archives under `/srv/lajur/deploy-backups`, outside the
container's mounts. Keep the five latest completed deployment snapshots.
Protected workbook mutation backups are not pruned by this retention rule.
Snapshot failure restarts the old API without applying the candidate image.

The current deployment record is `/etc/lajur/deployment.json`, including the
previous image and snapshot path. On readiness or HTTPS failure, the gate
restores the prior environment/image and checks Compose readiness. It does not
automatically restore financial data, because that could discard transactions.
If rollback itself fails, the job fails and an operator must investigate.
Retain old images; CI does not prune images on this shared host.

For manual rollback, inspect the root-owned deployment record, back up data,
replace only `LAJUR_IMAGE` in `/etc/lajur/production.env` with the previous image,
and run Compose `up -d --wait` as described in [the VPS runbook](VPS_BOOTSTRAP.md).
The next normal CI run can deploy a newer run number. Resetting deployment
history or restoring finance data requires an explicit operator decision.

These snapshots protect against local deployment failures, not total VPS loss.
An off-host encrypted backup and restore exercise remain separate work.

## Validation

`python services/api/deploy/test_deploy_gate.py` tests rejected commands/input,
stale/conflicting deploys, image identity, credential cleanup, pull failure,
snapshot failure, successful ordering/state and health-triggered image rollback.
CI runs these before building. Host acceptance also checks that the dedicated
key authenticates but cannot execute arbitrary `id`, and that a real digest
deploy retains owner login and service readiness. No deliberate broken image is
deployed to production to test rollback.

First production acceptance passed on 2026-10-11 in
[CI run 38106909234](https://github.com/quenzvezda/lajur/actions/runs/38106909234):
source `d1957ac68123c7bed49970230c12afbe4fc2a1d1`, run number `8`, image digest
`sha256:b0e061a162d619c4fccf2eb596d5ed48ecc823ac02ebf10ab0e7aec183881835`.
The host deployment record matches that revision/digest; the API is healthy and
still bound only to loopback. Both external HTTPS health checks pass, owner
browser login still succeeds with a secure session, Nginx validates, and the
unrelated Oracle container remains healthy. The snapshot is mode `0600` and
contains the persistent infrastructure/template directories. The dedicated
SSH key still rejects arbitrary shell commands. This checks backup presence and
archive structure, not an off-host restore exercise or physical-phone session
retention after deployment.

Sources: [GitHub runner labels](https://docs.github.com/en/actions/reference/runners/github-hosted-runners),
[GHCR authentication](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-container-registry),
[package permissions](https://docs.github.com/en/packages/learn-github-packages/configuring-a-packages-access-control-and-visibility).
