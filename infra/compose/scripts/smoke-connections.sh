#!/usr/bin/env sh
set -eu

COMPOSE_FILE="${COMPOSE_FILE:-infra/compose/compose.yml}"
ENV_FILE="${ENV_FILE:-.env}"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-divinity-vs1}"

if [ ! -f "$ENV_FILE" ]; then
  ENV_FILE=".env.example"
fi

set -a
. "$ENV_FILE"
set +a

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p "$PROJECT_NAME" "$@"
}

echo "Checking PostgreSQL with Platform API development credentials..."
compose exec -T postgres psql -v ON_ERROR_STOP=1 -U "${POSTGRES_USER}" -d "${POSTGRES_DB}" -c "select 1 as platform_api_can_connect;"

echo "Checking Valkey..."
compose exec -T valkey sh -c 'REDISCLI_AUTH="$VALKEY_PASSWORD" valkey-cli ping | grep -q PONG'

echo "Checking NATS JetStream..."
compose exec -T nats sh -c "wget -qO- 'http://127.0.0.1:8222/healthz?js-enabled-only=true' | grep -qi ok"

echo "VS-002 dependency smoke checks passed."
