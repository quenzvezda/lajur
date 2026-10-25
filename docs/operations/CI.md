# CI component selection

The existing `CI` workflow stays in `.github/workflows/ci.yml` to preserve its
Android version and backend deployment run-number sequence. Every push/PR runs
lightweight CI-script/publication-guard tests and change detection; only affected
application jobs run. Pull requests and non-main branches only validate. Official
main pushes also publish/deploy the selected components after their checks pass.

| Changed input | Application checks | Main publication/deployment |
| --- | --- | --- |
| Markdown, `docs/**`, agent tooling | None | None |
| `apps/android/**`, `tests/smart-scan-eval/**` | Android | Signed APK and GitHub Release |
| Android publication script/tests | Android | Signed APK and GitHub Release |
| `services/api/**`, `tests/workbook-fixtures/**` | Backend, container | Backend/web image and VPS deploy |
| `global.json`, root Docker/Compose/environment inputs, root .NET build/package config | Backend, container | Backend/web image and VPS deploy |
| CI workflow/scripts, `.gitattributes`, other unclassified inputs | Both | Both |

Markdown files under application directories are also documentation-only.
`.agents/**`, `.claude/**`, `.gitignore`, `.editorconfig` and `skills-lock.json`
do not select application jobs. Documentation branding sources are not packaged
directly; changing the generated/copied Android or web assets selects their owner.
Unknown paths select both components so new build inputs cannot silently bypass
checks. A change to CI selection/workflow itself therefore runs both pipelines.

The browser UI is served from the ASP.NET API's Razor Pages/static assets and
ships in the same Docker image. Android-only changes do not rebuild that image.
API changes do not create a new APK. If a change requires client/server contract
changes, include both implementations in the change; a specification Markdown
edit alone does not select either build.

## Change ranges

Push detection compares the event's `before` and `after` commits, covering every
commit in that push. PR detection compares the PR head to its merge base with the
event's base commit, excluding unrelated changes made only on the base branch.
Checkout retrieves full history. Deletions are included; renames are treated as
deletion/addition so both the old and new component are considered. Filenames are
NUL-delimited. Initial pushes or unavailable comparison revisions select both
components rather than silently skipping validation.

## Manual runs and versions

In **Actions → CI → Run workflow**, choose a branch and `target`:

- `android`: run Android checks; on official `main`, also sign/publish the APK.
- `backend`: run backend/container checks; on official `main`, also publish/deploy.
- `all` (default): run both pipelines, including both publications on official `main`.

Manual dispatch forces the target regardless of changed paths. Existing trusted
repository/main/environment restrictions still apply to signing and deployment.

Android keeps `versionCode = 100000 + github.run_number` and
`versionName = 1.0.<run_number>`. A skipped Android pipeline does not create an APK,
tag, release or update manifest. Future Android release numbers can have gaps.
Do not replace/reset the workflow's run-number sequence without planning larger
Android version codes and accounting for the VPS stale-deployment guard.

## Required checks

`CI check` always runs after detection and the application checks. It succeeds
only if detection/tests succeeded, every selected application job succeeded, and
unselected jobs were skipped. Missing outputs, failed/cancelled jobs or an
unexpected skip fail this check. Documentation-only runs pass without application
builds. Release/deploy jobs retain independent component prerequisites.

Configure branch protection/rulesets to require **CI check**. The workflow has no
top-level path filter, so this check is also reported for documentation-only PRs.
Existing Backend/Android/Container requirements can be removed after requiring
CI check. This repository change does not itself edit GitHub branch protection.

## Local validation

Run `python -m unittest discover -s .github/scripts` and
`actionlint .github/workflows/ci.yml`. The Python suite exercises real Git ranges,
PR merge bases, renames/deletions, manual selection, output files and failure/skip
handling without building apps, accessing signing keys or publishing releases.
