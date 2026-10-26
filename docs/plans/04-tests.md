# Feature 4: Automated tests

Status: **In progress**

## Goal

Check the features with automated tests, so changes do not break things.

## Rules (decided with the owner)

- Tests are written from the **spec**, not from the code. The owner gives the spec.
- Each feature has a spec in `docs/features/`. Each test name starts with the spec rule id (for example `U4_...`).
- If a test fails because the code does not match the spec: report it to the owner. Do not change code without the owner's OK.
- Framework: **xUnit**, with plain `Assert` (no extra assertion library).
- CI: GitHub Actions runs build + tests on every push and pull request (`.github/workflows/ci.yml`).

## Setup

- Test project: `tests/K8sApi.Tests` (references `src/K8sApi`).
- `FakeGatewayApiReader` is an in-memory cluster. It applies label selectors like the real Kubernetes API
  (case-sensitive), so tests see what a real cluster would return.
- `TestData` builds Gateways, listeners and HTTPRoutes with short code.

## Steps

- [x] Agree on framework and CI with owner
- [x] Spec for feature 1: which routes, answer shape, URL rules (`docs/features/01-discoverable-httproutes.md`)
- [x] Test project + fake cluster + test helpers
- [x] Tests for rules D1–D6, R1–R3, U1–U8
- [x] GitHub Actions pipeline
- [x] Tests for rules R4, R5 (empty result, hide routes without URL) and L1, L2 (logging)
- [x] Spec from owner for errors (E1–E11)
- [x] Endpoint test setup: `ApiFactory` (real API in memory, fake cluster, test login, log capture)
- [x] Tests for rules E1–E11 (`ErrorRuleTests.cs`)
- [x] Code fix so all error tests pass
- [x] Spec from owner for auth (A1–A12, `docs/features/02-keycloak-auth.md`)
- [x] Auth endpoint tests with real JWT check and test-signed tokens (`TestTokens`)
- [ ] Spec + tests for cluster connection (missing kubeconfig)
- [ ] New features (5, 6, ...): spec first, then tests

## Open questions

None right now.
