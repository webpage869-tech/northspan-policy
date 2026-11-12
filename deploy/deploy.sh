#!/usr/bin/env bash
# Runs on the Hetzner server from /opt/northspan-policy after GitHub Actions has
# copied in the config files and loaded the new northspan-policy:latest image.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  echo "Missing .env (copy .env.example and add ANTHROPIC_API_KEY)." >&2
  exit 1
fi

# Check .env ourselves first so a typo never makes Docker print the file (and the API key) into the deploy log.
bad=$(grep -nvE '^[[:space:]]*(#.*)?$|^[A-Za-z_][A-Za-z0-9_]*=' .env | cut -d: -f1 | tr '\n' ' ' || true)
if [[ -n "$bad" ]]; then
  echo ".env has a badly formatted line (line number: $bad). Each line must look like NAME=value with no spaces in NAME." >&2
  exit 1
fi
if ! grep -qE '^ANTHROPIC_API_KEY=.+' .env; then
  echo ".env is missing the ANTHROPIC_API_KEY=... line." >&2
  exit 1
fi

docker compose up -d --remove-orphans
docker image prune -f >/dev/null

# Wait for the app to answer its health check.
for i in $(seq 1 30); do
  if curl -fsS http://127.0.0.1:5081/health >/dev/null; then
    echo "Policy app is healthy."
    exit 0
  fi
  sleep 2
done
echo "Policy app did not become healthy." >&2
docker compose logs --tail=50 policy >&2
exit 1
