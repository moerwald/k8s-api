# Feature 11: REST endpoints to read stored metrics

Status: **Not started** (proposal, some open questions answered)

## Goal

Read the metrics that feature 10 stores in Postgres via REST.
Needs feature 10 (data model, `MetricsDbContext`).

## 1. Endpoints

**Decision (owner, 2026-10-27):** the endpoint list below is agreed.

All endpoints: `GET`, policy `k8s-api-reader`, JSON, times in UTC (ISO 8601).

| Endpoint | Returns |
|----------|---------|
| `/api/v1/metrics/latest` | The newest successful snapshot: cluster values, all nodes, all workloads |
| `/api/v1/metrics/cluster?from=&to=` | Cluster values over time (one point per snapshot) |
| `/api/v1/metrics/nodes?from=&to=` | All nodes over time |
| `/api/v1/metrics/nodes/{name}?from=&to=` | One node over time |
| `/api/v1/metrics/namespaces/{ns}/deployments?from=&to=` | All deployments of a namespace over time |
| `/api/v1/metrics/namespaces/{ns}/deployments/{name}?from=&to=` | One deployment over time |
| `/api/v1/metrics/snapshots?from=&to=` | List of polls with `success`, `durationMs`, `error` (to see gaps) |

There is no "all deployments of all namespaces over time" endpoint on purpose:
over 24 h that is ~576,000 rows (see plan 10, section 4). Use `/latest` for the current state of all workloads.

## 2. Time range

- `from` / `to` are optional. Default: `to` = now, `from` = now − 1 h. **Decision (owner, 2026-10-27).**
- Only 24 h of data exist (feature 10 retention). A wider range just returns what is there.
- `from` after `to`, or a value that is not a date → `400`.
- Optional (later): `step=5m` to return fewer points (e.g. last value or average per 5 minutes).

## 3. Answers (examples)

`GET /api/v1/metrics/cluster`

```json
{
  "from": "2026-10-27T09:00:00Z",
  "to": "2026-10-27T10:00:00Z",
  "points": [
    {
      "collectedAt": "2026-10-27T09:00:00Z",
      "nodeCount": 3, "readyNodeCount": 3,
      "pods": { "running": 42, "pending": 1, "failed": 0, "succeeded": 5, "unknown": 0 }
    }
  ]
}
```

`GET /api/v1/metrics/nodes/node-a`

```json
{
  "name": "node-a",
  "points": [
    {
      "collectedAt": "2026-10-27T09:00:00Z",
      "ready": true,
      "cpu": { "allocatableMillicores": 4000, "requestedMillicores": 2350 },
      "memory": { "allocatableBytes": 16000000000, "requestedBytes": 9100000000 },
      "podCount": 17
    }
  ]
}
```

Failed polls have no points. A client sees them as gaps (details via `/snapshots`).

## 4. Errors (proposal, same style as feature 1: problem details with title, status, traceId)

| Case | Answer |
|------|--------|
| No token / wrong role | `401` / `403` |
| Bad `from` / `to` | `400` |
| No snapshot yet (`/latest`) | `404` |
| Node or deployment has no data in the range | `200` with empty `points` (proposal) or `404`? |
| Database not reachable | `503` "Metrics database not reachable" |
| Database timeout | `504` |

## 5. Code

- `src/K8sApi/Metrics/MetricsEndpoints.cs` (`MapMetricsEndpoints()`), like `MapDiscoverableRoutes()`.
- `MetricsQueryService` uses `MetricsDbContext` with `AsNoTracking()` and returns small response models (not the entities).
- Indexes from plan 10 cover the queries: `collected_at`, `(node_name, snapshot_id)`, `(namespace, name, snapshot_id)`.

## 6. Steps

- [ ] Owner answers the open questions
- [ ] Spec in `docs/features/11-metrics-endpoints.md` with the owner
- [ ] Query service + endpoints
- [ ] Tests: endpoint tests with a real Postgres (Testcontainers), auth tests
- [ ] README

## Open questions

1. ~~Endpoint list ok?~~ **Answered:** yes.
2. ~~Default time range 1 h ok?~~ **Answered:** yes.
3. Do you need `step` (fewer points) now or later?
4. Unknown node/deployment: `200` with empty list, or `404`?
5. Same role `k8s-api-reader`, or a new role for metrics?
