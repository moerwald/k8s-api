# Feature 1: Discoverable HTTPRoutes

Status: **Done** (no tests yet)

## Goal

Return all URLs of HTTPRoutes that are marked as discoverable, grouped by domain.

## Endpoint

`GET /api/v1/httproutes/discoverable` – needs realm role `k8s-api-reader`.

## How it works

1. Read HTTPRoutes in all namespaces with label selector `discoverable=true,domain`.
2. Read all Gateways.
3. Build URLs `scheme://hostname[:port]/path` from route + Gateway listener.
4. Group by the value of the `domain` label. Sort and remove duplicates.

Rules (wildcards, regex paths, accepted status, ports) are listed in `README.md`.

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
- [ ] Unit tests for `UrlBuilder` (see feature 4)
- [ ] Test against a real cluster

## Open points

- Listener `allowedRoutes.namespaces.from: Selector` is not checked. The code trusts the route's `Accepted` status.
