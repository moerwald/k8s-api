# Project status

Read this file first in a new session. It shows what is done and what is next.
Each feature has a plan in `docs/plans/`. Update this file when a feature changes.

Last update: 2026-10-26 (commit `8f3330e`)

## Goal

A C# REST API (.NET 10) to query Kubernetes objects and send commands (e.g. restart a deployment).
Login with Keycloak. Install with Zarf and Helm.

## Features

| # | Feature | Status | Plan |
|---|---------|--------|------|
| 1 | Discoverable HTTPRoutes (`GET /api/v1/httproutes/discoverable`) | Done (no tests yet) | [01](docs/plans/01-discoverable-httproutes.md) |
| 2 | Keycloak authentication (JWT, realm roles) | Done (reader role only) | [02](docs/plans/02-keycloak-auth.md) |
| 3 | Cluster connection with kubeconfig | Done | [03](docs/plans/03-cluster-connection.md) |
| 4 | Automated tests | Not started | [04](docs/plans/04-tests.md) |
| 5 | Query Kubernetes objects (deployments, pods, ...) | Not started | [05](docs/plans/05-query-objects.md) |
| 6 | Commands: restart deployment | Not started | [06](docs/plans/06-restart-deployment.md) |
| 7 | Container image (Dockerfile) | Not started | [07](docs/plans/07-container-image.md) |
| 8 | Helm chart | Not started | [08](docs/plans/08-helm-chart.md) |
| 9 | Zarf package | Not started | [09](docs/plans/09-zarf-package.md) |

Status values: `Not started`, `In progress`, `Done`, `Blocked`.

## Known gaps

- The build was not checked in a CI pipeline. There is no CI yet.
- `deploy/rbac.yaml` is an example. It will move into the Helm chart (feature 8).

## Next step

Not decided yet. Suggestion: feature 4 (tests), then feature 7 and 8, so the API can run in a cluster.

## How to work on this repo

- Ask before changing code. Only change what the owner allows.
- For a new feature: add a plan in `docs/plans/`, add a row above, then build it.
- Keep answers and docs in simple English.
