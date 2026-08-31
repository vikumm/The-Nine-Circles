# Divinity MMO-VS1

This repository implements the MMO vertical slice defined by `MMO_GDD_Tecnico_Vertical_Slice_v1.md`.

## Current Scope

Implemented tasks:

- VS-001: monorepo, governance and solution bootstrap.
- VS-002: local Docker Compose dependencies.

Do not start gameplay, identity, movement, combat, loot, inventory or reconnect work until the required GDD gates are green and the next task is explicitly authorized.

## Local Dependencies

Copy the example environment file when you want local overrides:

```sh
cp .env.example .env
```

Validate Compose:

```sh
docker compose --env-file .env.example -f infra/compose/compose.yml config
```

Start core dependencies:

```sh
docker compose --env-file .env -f infra/compose/compose.yml up -d postgres keycloak valkey nats otel-collector
sh infra/compose/scripts/wait-healthy.sh
sh infra/compose/scripts/smoke-connections.sh
```

Start optional observability:

```sh
docker compose --env-file .env -f infra/compose/compose.yml --profile observability up -d
SERVICES="postgres keycloak valkey nats otel-collector prometheus grafana" sh infra/compose/scripts/wait-healthy.sh
```

Stop and remove local development containers and volumes:

```sh
docker compose --env-file .env -f infra/compose/compose.yml down -v
```

## Documented Ports

All published ports bind to `127.0.0.1` by default.

| Service | Port | Purpose |
|---|---:|---|
| PostgreSQL | 5432 | Platform API local database |
| Keycloak | 8080 | Local OIDC provider |
| Valkey | 6379 | Short-lived local state |
| NATS | 4222 | Client protocol |
| NATS monitor | 8222 | Local health/JetStream monitor |
| OTel gRPC | 4317 | OTLP gRPC ingest |
| OTel HTTP | 4318 | OTLP HTTP ingest |
| OTel health | 13133 | Collector health endpoint |
| OTel Prometheus | 9464 | Collector metrics scrape |
| Prometheus | 9090 | Optional observability profile |
| Grafana | 3000 | Optional observability profile |

## Keycloak Dev Realm

Compose imports realm `divinity-dev` from `infra/compose/keycloak/realms/divinity-dev-realm.json`.

The local public client is `divinity-launcher-dev`. It is reserved for VS-005 and is configured for Authorization Code with PKCE S256, loopback redirect URIs and no direct password grant.

No real users or production credentials are versioned.

## Verification

VS-001:

```sh
dotnet restore Divinity.sln
dotnet build Divinity.sln --configuration Release --no-restore
dotnet run --project packages/test-fixtures/smoke-tests/Divinity.SmokeTests.csproj --configuration Release --no-build
```

VS-002:

```sh
docker compose --env-file .env.example -f infra/compose/compose.yml config
docker compose --env-file .env.example -f infra/compose/compose.yml -p divinity-vs1-check up -d postgres keycloak valkey nats otel-collector
COMPOSE_PROJECT_NAME=divinity-vs1-check ENV_FILE=.env.example sh infra/compose/scripts/wait-healthy.sh
COMPOSE_PROJECT_NAME=divinity-vs1-check ENV_FILE=.env.example sh infra/compose/scripts/smoke-connections.sh
docker compose --env-file .env.example -f infra/compose/compose.yml -p divinity-vs1-check down -v
```
