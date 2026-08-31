# ADR-0011: Local Dependencies Docker Compose

Status: Accepted

## Context

VS-002 requires local PostgreSQL 18, Keycloak, Valkey, NATS JetStream, OpenTelemetry Collector and an optional observability profile. The task does not permit gameplay contracts, production deployment, Kubernetes, Agones, Open Match or domain migrations.

## Decision

Local dependencies are defined in `infra/compose/compose.yml`. Core services are PostgreSQL, Keycloak, Valkey, NATS JetStream and OpenTelemetry Collector. Prometheus and Grafana are available only through the `observability` Compose profile.

All published ports bind to `127.0.0.1` by default and credentials in `.env.example` are local non-production placeholders. Keycloak imports a development realm and a public launcher client configured for Authorization Code with PKCE S256. The direct password grant is disabled.

## Consequences

- Developers can validate infrastructure with `docker compose config`, automated health checks and smoke scripts.
- Platform API database connectivity is represented by the documented local PostgreSQL database and credentials until VS-005+ add application-level identity/persistence behavior.
- Future production deployment, real secrets, migrations and application service containers require later tasks and ADRs.
