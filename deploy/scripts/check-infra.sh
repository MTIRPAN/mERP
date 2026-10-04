#!/usr/bin/env bash
# Waits until every docker-compose service with a healthcheck is healthy, then probes the Aspire Dashboard
# (its chiseled image cannot run a healthcheck). Usage: deploy/scripts/check-infra.sh [timeout-seconds]
set -euo pipefail

TIMEOUT="${1:-180}"
SERVICES=(postgres valkey rabbitmq keycloak mailpit gotenberg)
deadline=$(( $(date +%s) + TIMEOUT ))

for service in "${SERVICES[@]}"; do
  while true; do
    container=$(docker compose ps -q "$service")
    status=$( [[ -n "$container" ]] && docker inspect -f '{{.State.Health.Status}}' "$container" || echo "missing")
    if [[ "$status" == "healthy" ]]; then
      echo "OK   $service"
      break
    fi
    if (( $(date +%s) > deadline )); then
      echo "FAIL $service: $status"
      exit 1
    fi
    sleep 2
  done
done

until curl -fsS -o /dev/null "http://localhost:${ASPIRE_UI_PORT:-18888}/"; do
  if (( $(date +%s) > deadline )); then echo "FAIL aspire-dashboard"; exit 1; fi
  sleep 2
done
echo "OK   aspire-dashboard"
