# Feature 1: Discoverable HTTPRoutes – Specification

This is the **spec** (what the feature must do). It was written with the owner, not taken from the code.
The tests in `tests/K8sApi.Tests/HttpRoutes/` check this spec. Each test name starts with the rule id (for example `D2_...`).

If the code and this spec do not match, the spec wins. Report the mismatch to the owner before you change code.

## Endpoint

`GET /api/v1/httproutes/discoverable`

Returns all URLs of discoverable HTTPRoutes in all namespaces, grouped by domain.

## Which routes are discoverable

| Id | Rule |
|----|------|
| D1 | A route is discoverable if it has the label `discoverable` = `true` **and** a label `domain` that is not empty. |
| D2 | The value `true` is not case-sensitive: `true`, `TRUE` and `True` all count. |
| D3 | `discoverable` with any other value (for example `false`) → not discoverable. |
| D4 | No `discoverable` label → not discoverable. |
| D5 | No `domain` label → not discoverable. |
| D6 | `domain` is empty or only spaces → not discoverable. |

Note: Kubernetes label selectors are case-sensitive. A selector `discoverable=true` does not find `TRUE`.
So rule D2 cannot be done with the label selector alone.

## Answer (JSON)

```json
{
  "domains": [
    { "domain": "shop", "urls": ["https://shop.example.com/cart", "https://shop.example.com/checkout"] }
  ]
}
```

| Id | Rule |
|----|------|
| R1 | Routes are grouped by the value of the `domain` label. One entry per domain. |
| R2 | Domains are sorted A–Z. URLs inside a domain are sorted A–Z. |
| R3 | The JSON uses the names `domains`, `domain` and `urls` (camelCase). |
| R4 | No discoverable route at all → `{"domains": []}`. |
| R5 | A discoverable route that gives no URL is hidden. A domain without any URL is hidden too. |

## How a URL is built

Form: `scheme://hostname[:port]path`

| Id | Rule |
|----|------|
| U1 | Scheme comes from the Gateway listener protocol: `HTTPS` → `https`, `HTTP` → `http`. |
| U2 | The port is shown only if it is not the default (80 for http, 443 for https). |
| U3 | Route has no hostname → use the listener hostname. Both missing → no URL. |
| U4 | Wildcard hostname (`*.example.com`) → no URL. |
| U5 | Route has no path → use `/`. |
| U6 | Regex path → no URL for this path. Other paths of the same route are still used. |
| U7 | The Gateway did not accept the route (no `Accepted=True` in the route status) → no URL. |
| U8 | The same URL from two routes is shown only once. |

## Logging

| Id | Rule |
|----|------|
| L1 | A discoverable route that gives no URL is logged as a **Warning**. The log has the route namespace/name and the reason. Example: `HTTPRoute shop/cart (domain shop) gives no URL: hostname is a wildcard (*.example.com)` |
| L2 | A route that gives at least one URL is not logged as a warning. |

## Errors

Agreed with the owner. Tests: `tests/K8sApi.Tests/HttpRoutes/ErrorRuleTests.cs`.

### Kubernetes errors

| Id | Case | Status | Title |
|----|------|--------|-------|
| E1 | Kubernetes not reachable (network down, DNS) | 502 | `Kubernetes API not reachable` |
| E2 | Kubernetes says "forbidden" (missing RBAC) | 502 | `Kubernetes API access forbidden` |
| E3 | Kubernetes says "unauthorized" (bad kubeconfig credentials) | 502 | `Kubernetes API authentication failed` |
| E4 | Gateway API not installed in the cluster (no HTTPRoute CRD) | 502 | `Gateway API not installed` |
| E5 | Kubernetes is too slow: the API waits at most **10 seconds** | 504 | `Kubernetes API timeout` |
| E6 | Kubernetes sends data the API can't read (broken JSON) | 502 | `Kubernetes API sent invalid data` |

### Other errors

| Id | Case | Rule |
|----|------|------|
| E7 | Bug in the API (unexpected exception) | 500, title `Internal server error` |
| E8 | The client cancels the request | Log as **Information**. No error log, no answer body. |

### Body and logging (all errors)

| Id | Rule |
|----|------|
| E9 | Body is "problem details" JSON (`application/problem+json`) with `title`, `status` and `traceId`. |
| E10 | No internal details in the body: no Kubernetes error text, no stack trace, no cluster URL. Only `title`, `status` and `traceId`. |
| E11 | Each error (E1–E7) is logged as **Error** with the exception and the same `traceId`. |

Not in the spec yet: other Kubernetes error codes (for example 500, 503). The code answers them with 502 `Kubernetes API not reachable`.

## Auth errors

See `docs/features/02-keycloak-auth.md` (rules A1–A12). This endpoint needs the client role `reader`.
