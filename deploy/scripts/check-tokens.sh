#!/usr/bin/env bash
# Obtains a token for every development user from the local Keycloak and verifies the "tenant" claim.
# Usage: deploy/scripts/check-tokens.sh [keycloak-url]
set -euo pipefail

KEYCLOAK_URL="${1:-${KEYCLOAK_URL:-http://localhost:8080}}"
TOKEN_URL="$KEYCLOAK_URL/realms/merp/protocol/openid-connect/token"
EXPECTED_TENANT="demo"
USERS=(admin ik yonetici calisan)

decode_jwt_payload() {
  local payload
  payload=$(cut -d. -f2 <<<"$1" | tr '_-' '/+')
  # base64url has no padding; add it back
  while (( ${#payload} % 4 != 0 )); do payload="$payload="; done
  base64 -d <<<"$payload" 2>/dev/null
}

failed=0
for user in "${USERS[@]}"; do
  response=$(curl -sS -X POST "$TOKEN_URL" \
    -d grant_type=password -d client_id=merp-web -d scope=openid \
    -d "username=$user" -d "password=$user") || { echo "FAIL $user: Keycloak unreachable at $KEYCLOAK_URL"; failed=1; continue; }

  access_token=$(sed -n 's/.*"access_token":"\([^"]*\)".*/\1/p' <<<"$response")
  if [[ -z "$access_token" ]]; then
    echo "FAIL $user: no access token ($response)"
    failed=1
    continue
  fi

  claims=$(decode_jwt_payload "$access_token")
  if grep -q "\"tenant\":\"$EXPECTED_TENANT\"" <<<"$claims"; then
    echo "OK   $user: tenant=$EXPECTED_TENANT"
  else
    echo "FAIL $user: tenant claim missing or not '$EXPECTED_TENANT'"
    failed=1
  fi
done

exit $failed
