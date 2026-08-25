#!/usr/bin/env bash
#
# ParentalTrack — build and deploy to parent.flexypdf.com
#
# Run from the repo root on your workstation (git-bash on Windows works):
#     bash deploy/deploy.sh
#
# Requires deploy/server-setup.sh to have been run once on the server first.
#
# What it does:
#   1. builds the admin panel          (npm run build, production env)
#   2. publishes the API self-contained for linux-x64  (server has no .NET 10 runtime)
#   3. folds the panel into the publish output as wwwroot — one process serves both
#   4. generates an idempotent migrations script
#   5. uploads, applies migrations, restarts the service, verifies /health/ready
#
set -euo pipefail

DOMAIN="parent.flexypdf.com"
SSH_HOST="187.127.141.107"
SSH_USER="flexyuser"
SSH_KEY="${SSH_KEY:-${HOME}/.ssh/flexypdf_deploy}"
SERVICE="parentaltrack-api"
REMOTE_ROOT="/home/${SSH_USER}/${DOMAIN}"
ENV_FILE="/etc/parentaltrack/parentaltrack-api.env"

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PUBLISH_DIR="${REPO_ROOT}/backend/artifacts/publish"
SQL_FILE="${REPO_ROOT}/backend/artifacts/migrations.sql"

SSH=(ssh -i "${SSH_KEY}" -o StrictHostKeyChecking=accept-new "${SSH_USER}@${SSH_HOST}")

say() { printf '\n\033[1;36m==> %s\033[0m\n' "$*"; }
ok()  { printf '    \033[0;32m%s\033[0m\n' "$*"; }

# ---------------------------------------------------------------------------------------------
say "1/6  Building the admin panel"
cd "${REPO_ROOT}/admin-web"
[[ -d node_modules ]] || npm ci
npm run build
ok "admin-web/dist built"

# ---------------------------------------------------------------------------------------------
say "2/6  Publishing the API (self-contained, linux-x64)"
cd "${REPO_ROOT}/backend"
rm -rf "${PUBLISH_DIR}"
dotnet publish src/ParentalTrack.Api/ParentalTrack.Api.csproj \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=false \
  -p:DebugType=none \
  -o "${PUBLISH_DIR}"
ok "published to backend/artifacts/publish"

# ---------------------------------------------------------------------------------------------
say "3/6  Folding the admin panel into wwwroot"
rm -rf "${PUBLISH_DIR}/wwwroot"
mkdir -p "${PUBLISH_DIR}/wwwroot"
cp -r "${REPO_ROOT}/admin-web/dist/." "${PUBLISH_DIR}/wwwroot/"
# The dev-only settings file has no business on a production box.
rm -f "${PUBLISH_DIR}/appsettings.Development.json"
ok "wwwroot populated"

# ---------------------------------------------------------------------------------------------
say "4/6  Generating the idempotent migrations script"
# --idempotent guards every migration with an "if not already applied" check, so the same file can
# be re-applied on every deploy without tracking which migrations the database has seen.
dotnet ef migrations script \
  --idempotent \
  --project src/ParentalTrack.Infrastructure \
  --startup-project src/ParentalTrack.Api \
  --output "${SQL_FILE}"
ok "$(wc -l < "${SQL_FILE}") lines written to backend/artifacts/migrations.sql"

# ---------------------------------------------------------------------------------------------
say "5/6  Uploading"
"${SSH[@]}" "mkdir -p '${REMOTE_ROOT}/current' '${REMOTE_ROOT}/tmp'"

# --delete keeps the remote directory an exact mirror, so files removed from a build do not linger.
rsync -az --delete \
  -e "ssh -i ${SSH_KEY} -o StrictHostKeyChecking=accept-new" \
  "${PUBLISH_DIR}/" "${SSH_USER}@${SSH_HOST}:${REMOTE_ROOT}/current/"

rsync -az \
  -e "ssh -i ${SSH_KEY} -o StrictHostKeyChecking=accept-new" \
  "${SQL_FILE}" "${SSH_USER}@${SSH_HOST}:${REMOTE_ROOT}/tmp/migrations.sql"

"${SSH[@]}" "chmod +x '${REMOTE_ROOT}/current/ParentalTrack.Api'"
ok "uploaded to ${REMOTE_ROOT}/current"

# ---------------------------------------------------------------------------------------------
say "6/6  Migrating and restarting"
"${SSH[@]}" bash -s <<REMOTE
set -euo pipefail

# PGHOST/PGUSER/PGPASSWORD come from the env file written by server-setup.sh.
set -a; . '${ENV_FILE}'; set +a

psql -v ON_ERROR_STOP=1 -q -f '${REMOTE_ROOT}/tmp/migrations.sql'
echo "    migrations applied"

sudo systemctl restart '${SERVICE}'
REMOTE

# Give the process a moment to bind before probing.
sleep 4

say "Verifying"
if "${SSH[@]}" "curl -fsS -m 10 http://127.0.0.1:5090/health/ready >/dev/null"; then
  ok "health/ready is green on 127.0.0.1:5090"
else
  echo "    health check FAILED. Recent logs:" >&2
  "${SSH[@]}" "sudo systemctl status ${SERVICE} --no-pager -n 40" >&2 || true
  exit 1
fi

printf '\n\033[1;32mDeployed.\033[0m  https://%s\n\n' "${DOMAIN}"
