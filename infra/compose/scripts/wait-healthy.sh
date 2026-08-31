#!/usr/bin/env sh
set -eu

COMPOSE_FILE="${COMPOSE_FILE:-infra/compose/compose.yml}"
ENV_FILE="${ENV_FILE:-.env}"
PROJECT_NAME="${COMPOSE_PROJECT_NAME:-divinity-vs1}"
TIMEOUT_SECONDS="${HEALTH_TIMEOUT_SECONDS:-300}"
SERVICES="${SERVICES:-postgres keycloak valkey nats otel-collector}"

if [ ! -f "$ENV_FILE" ]; then
  ENV_FILE=".env.example"
fi

compose() {
  docker compose --env-file "$ENV_FILE" -f "$COMPOSE_FILE" -p "$PROJECT_NAME" "$@"
}

deadline=$(( $(date +%s) + TIMEOUT_SECONDS ))

while :; do
  unhealthy=""

  for service in $SERVICES; do
    container_id="$(compose ps -q "$service")"

    if [ -z "$container_id" ]; then
      unhealthy="$unhealthy $service:not-created"
      continue
    fi

    status="$(docker inspect --format '{{if .State.Health}}{{.State.Health.Status}}{{else}}{{.State.Status}}{{end}}' "$container_id")"

    if [ "$status" != "healthy" ] && [ "$status" != "running" ]; then
      unhealthy="$unhealthy $service:$status"
    fi
  done

  if [ -z "$unhealthy" ]; then
    echo "All requested services are healthy: $SERVICES"
    exit 0
  fi

  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "Timed out waiting for healthy services:$unhealthy" >&2
    compose ps
    exit 1
  fi

  echo "Waiting for healthy services:$unhealthy"
  sleep 5
done
