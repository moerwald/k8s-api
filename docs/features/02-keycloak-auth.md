# Feature 2: Keycloak authentication – Specification

This is the **spec** (what the feature must do). It was written with the owner, not taken from the code.
The tests in `tests/K8sApi.Tests/Auth/AuthRuleTests.cs` check this spec. Each test name starts with the rule id.

If the code and this spec do not match, the spec wins. Report the mismatch to the owner before you change code.

## Background (short)

- **Audience** (`aud` in the token): says which app the token is for. Only tokens for `k8s-api` are accepted.
- **Client role**: a role that belongs to one Keycloak client (app). In the token it is in
  `resource_access.<client>.roles`. A **realm role** (`realm_access.roles`) belongs to the whole realm.

Example token payload:

```json
{
  "iss": "https://keycloak.example.com/realms/my-realm",
  "aud": ["k8s-api", "account"],
  "resource_access": { "k8s-api": { "roles": ["reader"] } }
}
```

## Roles

| Id | Rule |
|----|------|
| A1 | The Keycloak client is `k8s-api`. It is also the audience. |
| A2 | Read endpoints need the **client role** `reader` on client `k8s-api`. |
| A3 | The realm role `k8s-api-reader` (old way) is **not** accepted → `403`. |
| A4 | Role `reader` on **another** client is not accepted → `403`. |
| A5 | Later, commands (feature 6, e.g. restart) need the client role `operator`. Spec comes with feature 6. |

## Status codes

| Id | Case | Answer |
|----|------|--------|
| A6 | No token | `401` |
| A7 | Bad token (wrong signature, wrong issuer, wrong audience) | `401` |
| A8 | Expired token | `401` |
| A9 | Valid token, but without the role | `403` |
| A10 | Valid token, but broken role data | `403`, and a **Warning** in the log |
| A11 | `/healthz` | No token needed → `200` |

## Body

| Id | Rule |
|----|------|
| A12 | Body for `401` and `403` is problem JSON (`application/problem+json`) with only `title`, `status` and `traceId`. Titles: `Unauthorized` and `Forbidden`. |
