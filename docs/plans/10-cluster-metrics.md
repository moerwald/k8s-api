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
| Node | CPU and memory allocatable | `nodes` → `status.allocatable` |
| Node | CPU and memory **requested** (sum of the requests of all pods on the node) | `pods` → `spec.containers[].resources.requests` |
| Node | Ready condition, pod count | `nodes`, `pods` |
| Workload | Deployment: desired / ready / available replicas | `deployments` (apps/v1) |
| Workload | Sum of container restarts of the pods | `pods` → `containerStatuses.restartCount` |

**Decision (owner, 2026-10-27): metrics-server is not installed.** So there is no real CPU/memory **usage**
(`metrics.k8s.io` does not exist). All data comes from the normal Kubernetes API.

Instead of usage we store **requested** CPU/memory per node. Compared with allocatable, this shows how
"full" a node is for the scheduler. It does not show how busy the node really is.

Ways to get real usage later (not part of this feature):

| Way | Note |
|-----|------|
| Install metrics-server | Easiest. Then add `cpu_usage_millicores` / `memory_usage_bytes` with a new migration. |
| Kubelet `/stats/summary` via the API server proxy | Needs RBAC `nodes/proxy`. This right is very powerful (it can reach the kubelet API, also exec). Not recommended. |
| Read from Prometheus, if it exists | Then this feature would mostly duplicate Prometheus. |

Start small (cluster + nodes + deployments).

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
| cpu_allocatable_millicores | bigint | |
| cpu_requested_millicores | bigint | Sum of the requests of running/pending pods on the node |
| memory_allocatable_bytes | bigint | |
| memory_requested_bytes | bigint | |
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
| ~~c) Separate worker deployment~~ | Not chosen (owner decision 2026-10-27: polling runs inside the API). | |
| **d) Only 1 replica** | Nothing to build. | **Chosen (owner decision 2026-10-27).** |

**Decision:** the API runs with 1 replica. The Helm chart sets `replicas: 1`.
If more replicas are needed later, add option a) (Lease) first, or data will be saved twice.

## 6. Database setup

- NuGet: `Npgsql.EntityFrameworkCore.PostgreSQL` (version matching .NET 10 / EF Core 10).
- `MetricsDbContext` with the 4 entities. Table and column names in `snake_case`
  (e.g. with `EFCore.NamingConventions`), so plain SQL is easy.
- Connection: see section 6a (CNPG).
- `DateTime` values in UTC → Postgres `timestamptz`.
- Migrations: do **not** run `Database.Migrate()` at startup when there are several replicas.
  Better: an EF migration bundle or an idempotent SQL script, run by a Helm hook Job or init container.
- Health: add a DB check to the readiness probe? (see open questions)

## 6a. PostgreSQL with the CNPG operator

**Decision (owner, 2026-10-27):** the cluster runs the CloudNativePG (CNPG) operator. The database is a CNPG `Cluster`.

**Decision (owner, 2026-10-27):** the Helm chart of the API (feature 8) creates the CNPG `Cluster`. One instance. No backups.

```yaml
apiVersion: postgresql.cnpg.io/v1
kind: Cluster
metadata:
  name: k8s-api-db
spec:
  instances: 1            # owner decision: one instance
  storage:
    size: 2Gi
  bootstrap:
    initdb:
      database: k8s_metrics
      owner: k8s_api
```

What CNPG gives us:

- Service `k8s-api-db-rw` → always the primary. The API connects to this one.
- Secret `k8s-api-db-app` with the keys `host`, `port`, `dbname`, `username`, `password` (and `uri`).
- The `owner` user owns the database, so it can also run the EF migrations. No superuser needed.
- TLS is on by default. CA is in Secret `k8s-api-db-ca`.

How the API gets the connection:

- The Deployment maps the Secret keys to env variables (`secretKeyRef`), e.g. `MetricsDb__Host`, `MetricsDb__Port`,
  `MetricsDb__Database`, `MetricsDb__Username`, `MetricsDb__Password`.
- The app builds the connection string with `NpgsqlConnectionStringBuilder`.
  (We do not use the `uri` key: it has the `postgresql://` format, which Npgsql does not read directly.)
- TLS: `SslMode=Require` at the start. Optional later: mount `k8s-api-db-ca` and use `VerifyFull`.
- CNPG changes the password? The pod gets the new Secret only after a restart. Fine for now.

Zarf (feature 9): the Postgres image (`ghcr.io/cloudnative-pg/postgresql:<version>`) must be in the Zarf package,
because CNPG pulls it for the database pod.

Backups: none (owner decision). We keep only 1 day of data, and losing it is not critical.

Note: with one instance, the DB is down for a short time when its pod or node restarts.
The polling service logs the error and saves nothing in that time. The next poll works again.

## 7. Settings (proposal)

```json
"Metrics": {
  "Enabled": true,
  "Interval": "00:00:30",
  "PollTimeout": "00:00:20",
  "Retention": "1.00:00:00"
},
"MetricsDb": {
  "Host": "k8s-api-db-rw",
  "Port": 5432,
  "Database": "k8s_metrics",
  "Username": "from secret",
  "Password": "from secret",
  "SslMode": "Require"
}
```

## 8. RBAC (extra rights)

- `""` (core): `nodes`, `pods` → `get`, `list`
- `apps`: `deployments` → `get`, `list`

## 9. Steps

- [ ] Owner answers the open questions
- [ ] Write the spec in `docs/features/10-cluster-metrics.md` with the owner
- [ ] Add EF Core + Npgsql, `MetricsDbContext`, entities, first migration
- [ ] `IClusterMetricsReader` + implementation (Kubernetes calls)
- [ ] `MetricsCollector` (map + save)
- [ ] `MetricsPollingService` (timer, timeout, error handling)
- [ ] Retention cleanup (delete data older than 24 h)
- [ ] Helm chart: `replicas: 1` (no leader election needed)
- [ ] RBAC in `deploy/rbac.yaml` (later Helm chart, feature 8)
- [ ] Tests (see below), README

## 10. Tests

- Collector: fake `IClusterMetricsReader` → check the saved rows (CPU/memory quantity parsing like `250m`, `1Gi`; sum of requests per node; finished pods not counted; failed poll).
- Database: real Postgres with Testcontainers (EF InMemory does not behave like Postgres).
- Polling service: interval, timeout and "error does not stop the loop" with a short interval.
- Retention: old rows are deleted, new rows stay.

## Open questions

1. Which metrics do you need? Is the list in section 1 right (with "requested" instead of real usage)?
2. ~~Is metrics-server installed?~~ **Answered:** no. We store requested CPU/memory instead of usage.
3. ~~Data model: Option A ok?~~ **Answered:** Option A.
4. ~~How long to keep data?~~ **Answered:** max. 1 day, no long-term summaries.
5. ~~Where does Postgres come from?~~ **Answered:** CNPG operator is in the cluster.
   - 5a. ~~Who creates the CNPG `Cluster`?~~ **Answered:** our Helm chart.
   - 5b. ~~How many instances?~~ **Answered:** 1. Storage class and size: Helm values (default: cluster default class, `2Gi`).
   - 5c. ~~Backups?~~ **Answered:** no.
6. ~~Replicas?~~ **Answered:** 1 replica, no leader election.
7. ~~Polling inside the API or separate worker?~~ **Answered:** inside the API process (`BackgroundService`).
8. Should the API also offer REST endpoints to read the stored metrics (with the reader role)? If yes, that is a separate feature.
9. Should a failing DB make the pod "not ready", or only be logged?
