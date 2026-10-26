# Feature 2: Keycloak authentication

Status: **In progress** (spec agreed, code must move to client roles)

## Goal

Every API call (except `/healthz`) needs a valid Keycloak token.
Roles decide what a user may do.

## Spec

`docs/features/02-keycloak-auth.md` (rules A1–A12), agreed with the owner.
Read endpoints need the **client role** `reader` on client `k8s-api`. Commands later need `operator`.

## How it works (today)

- JWT bearer check with `Keycloak:Authority` and `Keycloak:Audience`.
- `KeycloakRoleClaims` copies `realm_access.roles` into normal role claims.
- Policy `k8s-api-reader` protects all read endpoints.

## Code

- `src/K8sApi/Auth/KeycloakRoleClaims.cs`
- `src/K8sApi/Program.cs` (auth setup)

## Steps

- [x] JWT validation against Keycloak
- [x] Map realm roles
- [x] Reader policy
- [x] Spec A1–A12 agreed with owner
- [x] Tests for A1–A12 (`tests/K8sApi.Tests/Auth/AuthRuleTests.cs`)
- [ ] Code: client role `reader` instead of realm role, problem JSON for 401/403, warning for broken role data (needs owner's OK)
- [ ] Operator policy for commands (needed by feature 6)

## Open points

- None. Decided: client roles (`resource_access.k8s-api.roles`), roles `reader` and later `operator`.
