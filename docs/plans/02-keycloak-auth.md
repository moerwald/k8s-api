# Feature 2: Keycloak authentication

Status: **Done** (reader role only)

## Goal

Every API call (except `/healthz`) needs a valid Keycloak token.
Roles decide what a user may do.

## How it works

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
- [ ] Writer policy for commands (needed by feature 6)
- [ ] Tests for role mapping (see feature 4)

## Open points

- Realm roles or client roles (`resource_access.<client>.roles`)? Today only realm roles are used.
- Role name for commands, e.g. `k8s-api-operator`?
