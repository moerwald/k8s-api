# Feature 10: Cluster metrics every 30 seconds – Specification

Status: **DRAFT** – not agreed yet. Points marked **(open)** need a decision from the owner.

This is the **spec** (what the feature must do). Plan and background: [plan 10](../plans/10-cluster-metrics.md).
Tests will check these rules. Each test name starts with the rule id (for example `C3_...`).
If the code and this spec do not match, the spec wins.

## Overview

A background service in the API reads values from Kubernetes every 30 seconds
and saves them in PostgreSQL (CNPG cluster `k8s-api-db`). Data is kept for 24 hours.
There is no REST endpoint in this feature (that is feature 11).

## Polling

| Id | Rule |
|----|------|
| P1 | Polling runs inside the API process as a background service. |
| P2 | The first poll starts right after the API has started (and after the migration, see M1). Then one poll every `Metrics:Interval` (default **30 s**). |
| P3 | Never two polls at the same time. If a poll takes longer than the interval, the missed ticks are skipped (no catch-up). |
| P4 | A poll may take at most `Metrics:PollTimeout` (default **20 s**). After that it is stopped and counts as failed (title `Kubernetes API timeout`). |
| P5 | A failed poll never stops the service. The next tick polls again. |
| P6 | `Metrics:Enabled = false` → no polling, no migration, no cleanup. The rest of the API works as before. |
| P7 | When the API shuts down, a running poll is cancelled. It is not saved. This is logged as **Information**, not as an error. |

## Collected values

All values come from the Kubernetes API (no metrics-server). All namespaces are read.

### Cluster (one row per poll)

| Id | Rule |
|----|------|
| C1 | `node_count` = number of nodes. |
| C2 | `ready_node_count` = number of nodes with condition `Ready` = `True`. `False`, `Unknown` or no condition → not ready. |
| C3 | Pods are counted per `status.phase`: `Running`, `Pending`, `Succeeded`, `Failed`, `Unknown`. A pod without phase or with another value counts as `Unknown`. |

### Node (one row per node per poll)

| Id | Rule |
|----|------|
| N1 | One row for each node that exists at poll time. |
| N2 | `ready`: same rule as C2. |
| N3 | `cpu_allocatable_millicores` and `memory_allocatable_bytes` come from `status.allocatable`. Missing value → `0`. |
| N4 | `cpu_requested_millicores` / `memory_requested_bytes` = sum of the `resources.requests` of the **normal containers** of all pods on this node (`spec.nodeName`) with phase `Pending` or `Running`. A container without a request counts as `0`. Init containers are not counted. **(open: count init containers?)** |
| N5 | `pod_count` = number of pods on this node with phase `Pending` or `Running`. |
| N6 | Pods that are not scheduled yet (no `spec.nodeName`) are not counted for any node. They are counted in C3. |
| N7 | Quantities are converted correctly: CPU `250m` → 250, `2` → 2000, `0.5` → 500. Memory `1Gi` → 1073741824, `500M` → 500000000, `128974848` → 128974848. CPU values are rounded **up** to whole millicores. |

### Deployments (one row per deployment per poll)

| Id | Rule |
|----|------|
| W1 | One row for each Deployment in all namespaces. `kind` = `Deployment`. |
| W2 | `desired_replicas` = `spec.replicas`. Missing → `1` (Kubernetes default). |
| W3 | `ready_replicas` = `status.readyReplicas`, `available_replicas` = `status.availableReplicas`. Missing → `0`. |
| W4 | `restart_count` = sum of `restartCount` of all normal containers of all pods that belong to the deployment. A pod belongs to it if its owner is a ReplicaSet whose owner is this Deployment (owner references). **(open: owner references need `list replicasets`; ok? Alternative: deployment label selector, less exact.)** |
| W5 | A deployment without pods has `restart_count` = `0`. |

## Saving

| Id | Rule |
|----|------|
| S1 | Each poll creates one row in `metric_snapshots`. `collected_at` = start time of the poll in UTC. `duration_ms` = how long the poll took. |
| S2 | Successful poll: `success = true`, `error = null`, plus 1 `cluster_metrics` row, N `node_metrics` rows and W `workload_metrics` rows. |
| S3 | All rows of one poll are saved in **one transaction**. Either all rows are saved, or none. |
| S4 | **All or nothing:** if any Kubernetes call of a poll fails, the poll is failed. Only the snapshot row is saved with `success = false` and no child rows. No half data. |
| S5 | `error` holds only a short title, the same titles as feature 1 (E1–E6): `Kubernetes API not reachable`, `Kubernetes API access forbidden`, `Kubernetes API authentication failed`, `Kubernetes API timeout`, `Kubernetes API sent invalid data`. Other errors: `Internal error`. Never exception text, URLs or secrets. |
| S6 | If the DB itself is not reachable, nothing can be saved (also no failed snapshot). This is logged (L3). |

## Retention

| Id | Rule |
|----|------|
| T1 | Snapshots older than `Metrics:Retention` (default **24 h**) are deleted, with all their child rows. Failed snapshots too. |
| T2 | The cleanup runs every **10 minutes** (and once after start). |
| T3 | Snapshots inside the retention time are never deleted. |
| T4 | A failed cleanup is logged and tried again next time. It does not stop polling. |

## Database migration

| Id | Rule |
|----|------|
| M1 | Before the first poll the service applies all missing EF Core migrations. |
| M2 | DB not reachable → log a **Warning**, try again at the next tick. No poll and no cleanup until the migration worked. |
| M3 | The rest of the API (e.g. HTTPRoutes endpoint) is not blocked by the migration. |

## Health endpoints

| Id | Rule |
|----|------|
| H1 | `GET /readyz` (no login needed): DB reachable → `200`, body `Healthy`. |
| H2 | DB not reachable → `503`, body `Unhealthy`. No error details in the body. |
| H3 | `GET /healthz` does not change: always `200` while the process runs. No DB check. |
| H4 | `Metrics:Enabled = false` → `/readyz` does not check the DB and returns `200`. **(open: ok?)** |

## Settings

| Id | Rule |
|----|------|
| G1 | Settings with defaults: `Metrics:Enabled` = `true`, `Metrics:Interval` = `00:00:30`, `Metrics:PollTimeout` = `00:00:20`, `Metrics:Retention` = `1.00:00:00`. |
| G2 | DB settings: `MetricsDb:Host`, `Port` (default `5432`), `Database`, `Username`, `Password`, `SslMode` (default `Require`). In the cluster they come from the CNPG Secret `k8s-api-db-app`. |
| G3 | Wrong settings stop the app at startup with a clear error (like the kubeconfig check): `Interval` < 10 s, `PollTimeout` ≥ `Interval`, `Retention` ≤ 0, or (when enabled) `Host`, `Database`, `Username` or `Password` missing. |
| G4 | The password is never logged. |

## Logging

| Id | Rule |
|----|------|
| L1 | Successful poll → **Debug** with duration and counts (nodes, deployments). |
| L2 | Failed poll → **Error** with the exception and the title from S5. |
| L3 | DB not reachable while saving → **Error** `Metrics database not reachable` with the exception. |
| L4 | Cleanup deleted rows → **Information** with the number of deleted snapshots. Nothing deleted → no log. |
| L5 | Migration applied → **Information** with the migration names. Migration failed → **Warning** (M2). |

## Rights (RBAC)

| Id | Rule |
|----|------|
| R1 | Extra rights: `get`, `list` on `nodes`, `pods` (core) and `deployments` (apps). |
| R2 | If W4 uses owner references: also `get`, `list` on `replicasets` (apps). **(open)** |
| R3 | A missing right makes the poll fail with `Kubernetes API access forbidden` (S5). |

## Open points

1. N4: count init containers in "requested"? Proposal: no (keep it simple).
2. W4: match pods to deployments via owner references (exact, needs `replicasets`) or label selector? Proposal: owner references.
3. H4: `/readyz` without DB check when metrics are disabled? Proposal: yes.
4. Any rule above that you want different?
