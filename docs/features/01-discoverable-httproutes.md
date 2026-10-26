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

Not specified yet. Open questions for the owner:

- Which role is needed, where is it in the token (realm roles, client roles)?
- Status codes for: no token, bad or expired token, token without the role, broken role data.
- Kubernetes not reachable, or Kubernetes says "forbidden": which status code, which body?
