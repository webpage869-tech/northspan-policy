#!/usr/bin/env bash
# Runs on the Hetzner server from /opt/northspan-policy after the new code is copied in.
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  echo "Missing .env (copy .env.example and add ANTHROPIC_API_KEY)." >&2
  exit 1
fi

docker compose up -d --build --remove-orphans
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
