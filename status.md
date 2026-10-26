# Project status

Read this file first in a new session. It shows what is done and what is next.
Each feature has a plan in `docs/plans/` and (when agreed) a spec in `docs/features/`.
Update this file when a feature changes.

Last update: 2026-10-26

## Goal

A C# REST API (.NET 10) to query Kubernetes objects and send commands (e.g. restart a deployment).
Login with Keycloak. Install with Zarf and Helm.

## Features

| # | Feature | Status | Plan |
|---|---------|--------|------|
| 1 | Discoverable HTTPRoutes (`GET /api/v1/httproutes/discoverable`) | Done, tests added (see feature 4) | [01](docs/plans/01-discoverable-httproutes.md) |
| 2 | Keycloak authentication (JWT, realm roles) | Done (reader role only) | [02](docs/plans/02-keycloak-auth.md) |
| 3 | Cluster connection with kubeconfig | Done | [03](docs/plans/03-cluster-connection.md) |
| 4 | Automated tests + CI | In progress | [04](docs/plans/04-tests.md) |
| 5 | Query Kubernetes objects (deployments, pods, ...) | Not started | [05](docs/plans/05-query-objects.md) |
| 6 | Commands: restart deployment | Not started | [06](docs/plans/06-restart-deployment.md) |
| 7 | Container image (Dockerfile) | Not started | [07](docs/plans/07-container-image.md) |
| 8 | Helm chart | Not started | [08](docs/plans/08-helm-chart.md) |
| 9 | Zarf package | Not started | [09](docs/plans/09-zarf-package.md) |
| 10 | Cluster metrics every 30 s into Postgres (EF Core) | Not started (plan: Option A data model, keep data 1 day; other questions still open) | [10](docs/plans/10-cluster-metrics.md) |

Status values: `Not started`, `In progress`, `Done`, `Blocked`.

## Known gaps

- CI (GitHub Actions) builds and runs tests on every push: `.github/workflows/ci.yml`.
- The README shows a CI badge. Green = build and all tests passed on `main`. Red = something failed.
- Error spec E1–E11 is agreed and tested with endpoint tests (`tests/K8sApi.Tests/Api/ApiFactory.cs`).
- Specs for auth and cluster connection are not agreed yet.
- Other Kubernetes error codes (500, 503, ...) are not in the spec. The code answers them with 502 `Kubernetes API not reachable`.
- `UrlBuilder` no longer uses `yield`. All helper methods return lists. This is a style change only, so the existing tests cover it.
- `deploy/rbac.yaml` is an example. It will move into the Helm chart (feature 8).

## Next step

Feature 4: get the auth spec from the owner (roles, 401/403), then add auth endpoint tests.
After that: feature 7 and 8, so the API can run in a cluster.

Feature 10: the owner must answer the open questions in the plan before any code is written.

## How to work on this repo

- Ask before changing code. Only change what the owner allows.
- For a new feature: add a plan in `docs/plans/`, add a row above, then build it.
- Tests are written from the spec (`docs/features/`), not from the code. Ask the owner for the spec first.
- If a test shows the code does not match the spec, report it. Do not fix code without the owner's OK.
- Keep answers and docs in simple English.
