# Feature 1: Discoverable HTTPRoutes

Status: **Done** (tests added, see spec)

## Goal

Return all URLs of HTTPRoutes that are marked as discoverable, grouped by domain.

## Endpoint

`GET /api/v1/httproutes/discoverable` – needs realm role `k8s-api-reader`.

## How it works

1. Read HTTPRoutes in all namespaces with label selector `discoverable,domain` (both labels exist).
   Then keep only routes where `discoverable` is `true` (any case) and `domain` is not empty.
   This is done in C#, because label selectors are case-sensitive.
2. Read all Gateways.
3. Build URLs `scheme://hostname[:port]/path` from route + Gateway listener.
4. Group by the value of the `domain` label. Sort and remove duplicates.

Rules (wildcards, regex paths, accepted status, ports) are listed in `README.md`.
The agreed spec is `docs/features/01-discoverable-httproutes.md`.

## Code

- `src/K8sApi/HttpRoutes/DiscoverableRouteService.cs` – service and endpoint
- `src/K8sApi/HttpRoutes/GatewayApiReader.cs` – reads from Kubernetes, with paging
- `src/K8sApi/HttpRoutes/UrlBuilder.cs` – pure logic, builds the URLs
- `src/K8sApi/HttpRoutes/GatewayApiModels.cs` – small Gateway API models

## Steps

- [x] Read HTTPRoutes and Gateways (paging, all namespaces)
- [x] Build URLs from listeners and hostnames
- [x] Return 502 if Kubernetes is not reachable
- [x] Document in README
- [x] Spec tests (see feature 4, `tests/K8sApi.Tests/HttpRoutes/`)
- [x] Refactor: `UrlBuilder` helper methods return lists instead of using `yield` (no behavior change)
- [ ] Test against a real cluster

## Open points

- Listener `allowedRoutes.namespaces.from: Selector` is not checked. The code trusts the route's `Accepted` status.
