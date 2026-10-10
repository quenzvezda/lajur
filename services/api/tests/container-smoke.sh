#!/usr/bin/env bash
# Container smoke test: runs the API image the way docker-compose.yml does (read-only root, dropped
# capabilities, bind-mounted storage) and exercises account creation, login, workbook import, a
# finance read and the web viewer login page. A second container without the staging mount must fail
# readiness, which guards against the 2026-10-07 import regression.
#
# Usage: services/api/tests/container-smoke.sh <image> <fixture.xlsm>
set -euo pipefail

image="${1:?image tag required}"
fixture="${2:?fixture path required}"
port="${LAJUR_SMOKE_PORT:-18080}"
broken_port=$((port + 1))
work="$(mktemp -d)"
password="smoke-password-$RANDOM-$RANDOM"
containers=(lajur-smoke lajur-smoke-unmounted)
# JSON parsing helper; skips the Windows Store "python3" stub when run locally.
python_bin="python3"
"$python_bin" -c '' >/dev/null 2>&1 || python_bin="python"

cleanup() {
  for name in "${containers[@]}"; do docker rm -f "$name" >/dev/null 2>&1 || true; done
  # App-owned nested directories can be mode 700 on Linux. Give the runner
  # access to this test's unique temporary mount before removing it.
  docker run --rm --user 0 --entrypoint /bin/sh \
    --volume "$work:/cleanup" "$image" -c 'chmod -R a+rwX /cleanup' >/dev/null 2>&1 || true
  rm -rf "$work"
}
trap cleanup EXIT

fail() {
  echo "SMOKE FAILED: $*" >&2
  docker logs lajur-smoke 2>&1 | tail -n 60 >&2 || true
  exit 1
}

mkdir -p "$work"/{data,backups,templates,auth,keys,staging}
# The image runs as a non-root app user; CI-only temp dirs are made writable for it.
chmod -R a+rwX "$work"

run_api() {
  local name="$1" host_port="$2"; shift 2
  docker run --detach --name "$name" \
    --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges:true \
    --env Lajur__Storage__RootPath=/srv/lajur \
    --publish "127.0.0.1:${host_port}:8080" \
    "$@" "$image" >/dev/null
}

mounts=()
for dir in data backups templates auth keys staging; do
  mounts+=(--volume "$work/$dir:/srv/lajur/$dir")
done

wait_ready() {
  local url="$1" expected="$2"
  for _ in $(seq 1 60); do
    code="$(curl --silent --output "$work/discard" --write-out '%{http_code}' "$url" || true)"
    [[ "$code" == "$expected" ]] && return 0
    sleep 1
  done
  return 1
}

run_api lajur-smoke "$port" "${mounts[@]}"
base="http://127.0.0.1:${port}"
wait_ready "$base/health/ready" 200 || fail "readiness did not become healthy"

printf '%s\n' "$password" | docker exec -i lajur-smoke dotnet Lajur.Api.dll account create --username smoke >/dev/null \
  || fail "account creation failed"

token="$(curl --silent --fail --header 'Content-Type: application/json' \
  --data "{\"username\":\"smoke\",\"password\":\"$password\",\"deviceLabel\":\"ci-smoke\"}" \
  "$base/api/v1/auth/login" | "$python_bin" -c 'import json,sys; print(json.load(sys.stdin)["accessToken"])')" \
  || fail "login failed"

import_status="$(curl --silent --output "$work/import.json" --write-out '%{http_code}' \
  --header "Authorization: Bearer $token" \
  --form "file=@${fixture};type=application/vnd.ms-excel.sheet.macroEnabled.12" \
  --form year=2026 --form replaceExisting=false --form confirmNoPendingChanges=true \
  "$base/api/v1/workbooks/import")"
[[ "$import_status" == "200" ]] || fail "import returned $import_status: $(cat "$work/import.json")"

count="$(curl --silent --fail --header "Authorization: Bearer $token" \
  "$base/api/v1/summary?year=2026&month=1" | "$python_bin" -c 'import json,sys; print(json.load(sys.stdin)["transactionCount"])')" \
  || fail "summary request failed"
[[ "$count" -gt 0 ]] || fail "imported workbook has no January transactions"

web_status="$(curl --silent --output "$work/discard" --write-out '%{http_code}' "$base/masuk")"
[[ "$web_status" == "200" ]] || fail "web viewer login page returned $web_status"

# Regression guard: without the staging mount the read-only root must make readiness fail.
broken_mounts=()
for dir in data backups templates auth keys; do
  broken_mounts+=(--volume "$work/$dir:/srv/lajur/$dir")
done
run_api lajur-smoke-unmounted "$broken_port" "${broken_mounts[@]}"
wait_ready "http://127.0.0.1:${broken_port}/health/ready" 503 \
  || fail "readiness stayed healthy although staging is not writable"

echo "Container smoke test passed (import, summary, web login page, staging readiness guard)."
