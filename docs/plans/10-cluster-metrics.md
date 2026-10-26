# Feature 10: Collect cluster metrics every 30 seconds

Status: **Not started** (proposal, some open questions answered)

## Goal

The API reads important cluster metrics every 30 seconds and stores them in a PostgreSQL database.
Data access uses Entity Framework Core (EF Core) with the Npgsql provider.

This file is a proposal. No code is written yet. The open questions at the end must be answered first.

## 1. Which metrics (proposal)

| Level | Metric | Source |
|-------|--------|--------|
| Cluster | Number of nodes, ready nodes | `nodes` (core/v1) |
| Cluster | Pods per phase (Running, Pending, Failed, ...) | `pods` (core/v1) |
| Node | CPU usage (millicores), memory usage (bytes) | `metrics.k8s.io` (needs metrics-server) |
| Node | CPU and memory allocatable | `nodes` → `status.allocatable` |
| Node | Ready condition, pod count | `nodes`, `pods` |
| Workload | Deployment: desired / ready / available replicas | `deployments` (apps/v1) |
| Workload | Sum of container restarts of the pods | `pods` → `containerStatuses.restartCount` |

The KubernetesClient library already has `GetKubernetesNodesMetricsAsync()` and
`GetKubernetesPodsMetricsAsync()` for `metrics.k8s.io`.

Start small (cluster + nodes + deployments). Pod-level usage creates many rows (see section 4), so add it later only if needed.

## 2. Data model – options

### Option A: Snapshot + typed tables (recommended)

Each poll creates one **snapshot** row. All values of that poll point to it.

```
metric_snapshots 1 ──── n node_metrics
                 1 ──── n workload_metrics
                 1 ──── 1 cluster_metrics
```

**metric_snapshots**

| Column | Type | Note |
|--------|------|------|
| id | bigint (identity) | PK |
| collected_at | timestamptz | UTC, start of the poll. Index |
| duration_ms | int | How long the poll took |
| success | bool | false if the poll failed |
| error | text, null | Short error text, no secrets |

**cluster_metrics**

| Column | Type |
|--------|------|
| snapshot_id | bigint (PK + FK) |
| node_count / ready_node_count | int |
| pods_running / pods_pending / pods_failed / pods_succeeded / pods_unknown | int |

**node_metrics**

| Column | Type | Note |
|--------|------|------|
| id | bigint | PK |
| snapshot_id | bigint | FK |
| node_name | text | Index (node_name, snapshot_id) |
| ready | bool | |
| cpu_usage_millicores | bigint, null | null if metrics-server is missing |
| cpu_allocatable_millicores | bigint | |
| memory_usage_bytes | bigint, null | |
| memory_allocatable_bytes | bigint | |
| pod_count | int | |

**workload_metrics**

| Column | Type | Note |
|--------|------|------|
| id | bigint | PK |
| snapshot_id | bigint | FK |
| namespace / name | text | Index (namespace, name, snapshot_id) |
| kind | text | `Deployment` now, later `StatefulSet`, `DaemonSet` |
| desired_replicas / ready_replicas / available_replicas | int | |
| restart_count | int | Sum over the pods of the workload |

Good: clear types, easy LINQ queries, easy EF migrations, you see failed polls.
Bad: a new metric needs a new column (a migration).

### Option B: One generic table (like Prometheus)

`metric_samples(collected_at, name, value double, labels jsonb)`

Good: new metrics need no migration.
Bad: hard to query with LINQ, no type safety, labels repeat in every row (more space).
If you want this, a real time-series system (Prometheus) is usually the better tool.

### Option C: Option A + a `raw jsonb` column

Typed columns for the important values, plus raw JSON for "maybe later" data.
Good for flexibility, but the table grows fast.

**Decision (owner, 2026-10-26):** Option A. The metrics list is small and fixed, and EF Core works best with typed tables.

**Decision (owner, 2026-10-26):** `cluster_metrics` stays a separate table (1:1 with `metric_snapshots`). It is not merged into the snapshot row.

## 3. Background polling every 30 seconds

Use a .NET `BackgroundService` with a `PeriodicTimer`. No extra library (Quartz, Hangfire) is needed.

```csharp
// Sketch only – not final code.
public sealed class MetricsPollingService(
    IServiceScopeFactory scopes, IOptions<MetricsOptions> options, ILogger<MetricsPollingService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Value.Interval);   // 30 s
        do
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            timeout.CancelAfter(options.Value.PollTimeout);             // e.g. 20 s
            try
            {
                await using var scope = scopes.CreateAsyncScope();     // DbContext is scoped
                var collector = scope.ServiceProvider.GetRequiredService<MetricsCollector>();
                await collector.CollectAndSaveAsync(timeout.Token);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                log.LogError(ex, "Metrics poll failed");              // never stop the loop
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
```

Why this design:

- `PeriodicTimer` does not start a second poll while one is still running. A slow poll just skips a tick.
- One poll = one DI scope = one `DbContext`. `DbContext` is not thread-safe and must not live forever.
- A timeout per poll, so a hanging Kubernetes call does not block everything.
- Errors are logged and saved as a snapshot with `success = false`. The loop keeps running.
- The Kubernetes calls run in parallel (`Task.WhenAll`): nodes, node metrics, pods, deployments.
- All values of one poll are saved with one `SaveChangesAsync` (one transaction).
- Split the code like feature 1: `IClusterMetricsReader` (Kubernetes access, easy to fake in tests),
  `MetricsCollector` (maps data to entities and saves), `MetricsPollingService` (only the timer).
- Setting `Metrics:Enabled` turns polling on or off (off in tests and for local work).

## 4. Data volume and retention

Example: 10 nodes, 200 deployments, poll every 30 s = 2,880 polls per day.

| Table | Rows per day (= max rows in the DB) |
|-------|-------------------------------------|
| metric_snapshots | 2,880 |
| node_metrics | ~29,000 |
| workload_metrics | ~576,000 |

**Decision (owner, 2026-10-26): keep data for max. 1 day.**

- Setting `Metrics:Retention` = `1.00:00:00` (24 hours).
- Every 10 minutes the service deletes snapshots older than 24 hours with `ExecuteDeleteAsync`, in batches (no huge locks).
  The FK uses `ON DELETE CASCADE`, so deleting snapshots deletes the child rows.
- Index on `metric_snapshots.collected_at` makes the delete fast.
- With only 1 day of data the tables stay small. No partitioning, TimescaleDB or summary tables needed.

## 5. More than one API replica

If Helm runs 2 or more replicas, every replica would poll. That gives duplicate data.

| Option | How | Note |
|--------|-----|------|
| a) Kubernetes Lease (leader election) | KubernetesClient has `LeaderElector` with `LeaseLock`. Only the leader polls. | Needs RBAC on `leases`. Recommended. |
| b) Postgres advisory lock | `pg_try_advisory_lock` before each poll. | Simple, no extra RBAC. |
| c) Separate worker deployment | Own Deployment with `replicas: 1` and `Metrics:Enabled=true`; API pods have it off. | Clean split, one more pod. |
| d) Only 1 replica | Nothing to build. | Fine for the start. |

## 6. Database setup

- NuGet: `Npgsql.EntityFrameworkCore.PostgreSQL` (version matching .NET 10 / EF Core 10).
- `MetricsDbContext` with the 4 entities. Table and column names in `snake_case`
  (e.g. with `EFCore.NamingConventions`), so plain SQL is easy.
- Connection string: `ConnectionStrings:MetricsDb` (env `ConnectionStrings__MetricsDb`), from a Kubernetes Secret.
- `DateTime` values in UTC → Postgres `timestamptz`.
- Migrations: do **not** run `Database.Migrate()` at startup when there are several replicas.
  Better: an EF migration bundle or an idempotent SQL script, run by a Helm hook Job or init container.
- Health: add a DB check to the readiness probe? (see open questions)

## 7. Settings (proposal)

```json
"Metrics": {
  "Enabled": true,
  "Interval": "00:00:30",
  "PollTimeout": "00:00:20",
  "Retention": "1.00:00:00"
},
"ConnectionStrings": {
  "MetricsDb": "Host=...;Database=k8s_metrics;Username=...;Password=..."
}
```

## 8. RBAC (extra rights)

- `""` (core): `nodes`, `pods` → `get`, `list`
- `apps`: `deployments` → `get`, `list`
- `metrics.k8s.io`: `nodes` (and later `pods`) → `get`, `list`
- `coordination.k8s.io`: `leases` → `get`, `create`, `update` (only for option 5a)

## 9. Steps

- [ ] Owner answers the open questions
- [ ] Write the spec in `docs/features/10-cluster-metrics.md` with the owner
- [ ] Add EF Core + Npgsql, `MetricsDbContext`, entities, first migration
- [ ] `IClusterMetricsReader` + implementation (Kubernetes calls)
- [ ] `MetricsCollector` (map + save)
- [ ] `MetricsPollingService` (timer, timeout, error handling)
- [ ] Retention cleanup (delete data older than 24 h)
- [ ] Multi-replica protection (option from section 5)
- [ ] RBAC in `deploy/rbac.yaml` (later Helm chart, feature 8)
- [ ] Tests (see below), README

## 10. Tests

- Collector: fake `IClusterMetricsReader` → check the saved rows (CPU quantity parsing, null when metrics-server is missing, failed poll).
- Database: real Postgres with Testcontainers (EF InMemory does not behave like Postgres).
- Polling service: interval, timeout and "error does not stop the loop" with a short interval.
- Retention: old rows are deleted, new rows stay.

## Open questions

1. Which metrics do you need? Is the list in section 1 right? Pod-level CPU/memory too?
2. Is metrics-server installed in the target cluster?
3. ~~Data model: Option A ok?~~ **Answered:** Option A.
4. ~~How long to keep data?~~ **Answered:** max. 1 day, no long-term summaries.
5. Where does Postgres come from: existing DB, or part of the Zarf package / Helm chart?
6. Replicas: which option from section 5?
7. Polling inside the API process, or a separate worker deployment?
8. Should the API also offer REST endpoints to read the stored metrics (with the reader role)? If yes, that is a separate feature.
9. Should a failing DB make the pod "not ready", or only be logged?
