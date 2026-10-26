# Feature 4: Automated tests

Status: **Not started**

## Goal

Check the logic with automated tests, so changes do not break things.

## Plan

1. Add test project `tests/K8sApi.Tests` (xUnit).
2. Unit tests for `UrlBuilder`: one test per rule in the README table
   (scheme, port, no hostname, wildcard, no path, regex path, not accepted, duplicates).
3. Unit tests for `KeycloakRoleClaims` (roles found, no roles, bad JSON).
4. Endpoint tests with `WebApplicationFactory` and a fake `IGatewayApiReader`:
   401 without token, 403 without role, 200 with role, 502 when Kubernetes fails.
5. Optional: CI pipeline (GitHub Actions) that runs `dotnet build` and `dotnet test`.

## Open questions

- xUnit or another test framework?
- Add GitHub Actions now or later?
