# K8s API

A C# REST API to query Kubernetes objects and (later) send commands.
Login is done with Keycloak. Install is planned with Zarf and Helm.

## Feature 1: Discoverable HTTPRoutes

`GET /api/v1/httproutes/discoverable`

- Needs a Keycloak token with the **client role** `reader` on client `k8s-api`.
- Finds all HTTPRoutes in all namespaces with the label `discoverable=true` (any case, e.g. `TRUE`)
  and a non-empty `domain` label.
- Groups them by the value of the `domain` label.
- Returns absolute URLs: `scheme://hostname[:port]/path`.

Example response:

```json
{
  "domains": [
    { "domain": "shop", "urls": ["https://shop.example.com/cart", "https://shop.example.com/checkout"] }
  ]
}
```

### URL rules

| Case | Result |
|------|--------|
| Scheme | From the Gateway listener protocol: `HTTPS` → `https`, `HTTP` → `http` |
| Port | Added only if not 80 (http) or 443 (https) |
| Route has no hostname | Listener hostname is used. None at all → no URL |
| Wildcard hostname (`*.example.com`) | Skipped |
| No path | `/` |
| Regex path | Skipped |
| Gateway did not accept the route (status) | Skipped |
| Same URL from two routes | Shown once, list is sorted |
| Route gives no URL at all | Hidden. Logged as warning with namespace/name and reason |
| Domain has no URL at all | Hidden |
| No discoverable route | `{"domains": []}` |

### Errors

- `401` no, bad or expired token (title `Unauthorized`)
- `403` client role `reader` missing, or broken role data in the token (title `Forbidden`)
- `502` / `504` / `500` Kubernetes or API errors, see the table below

| Case | Status | Title |
|------|--------|-------|
| Kubernetes not reachable | 502 | `Kubernetes API not reachable` |
| Kubernetes says forbidden (RBAC) | 502 | `Kubernetes API access forbidden` |
| Kubernetes says unauthorized (kubeconfig) | 502 | `Kubernetes API authentication failed` |
| Gateway API CRDs not installed | 502 | `Gateway API not installed` |
| Kubernetes slower than 10 seconds | 504 | `Kubernetes API timeout` |
| Kubernetes sends data that can't be read | 502 | `Kubernetes API sent invalid data` |
| Bug in the API | 500 | `Internal server error` |

Error bodies are `application/problem+json` with only `title`, `status` and `traceId`.
Internal details are only in the log (level Error, same trace id). A client cancel is logged as Information.

## Configuration

`appsettings.json` or environment variables:

| Setting | Env variable | Example |
|---------|--------------|---------|
| `Kubernetes:KubeconfigPath` (required) | `Kubernetes__KubeconfigPath` | `/etc/k8s-api/kubeconfig` |
| `Keycloak:Authority` | `Keycloak__Authority` | `https://keycloak.example.com/realms/my-realm` |
| `Keycloak:Audience` | `Keycloak__Audience` | `k8s-api` |

## Keycloak setup

- Client `k8s-api`. Its id is also the audience (`Keycloak:Audience`).
- The token must have `k8s-api` in its `aud` claim (use an audience mapper on the client).
- Client role `reader` on client `k8s-api` for read endpoints. Give it to users or groups.
- Realm roles (for example the old `k8s-api-reader`) are **not** used.

Spec: `docs/features/02-keycloak-auth.md`.

## Cluster connection

The API connects with a kubeconfig file. It always uses the file's `current-context`.
If the path is not set or the file does not exist, the API stops at startup with an error.

## Kubernetes rights

The user in the kubeconfig needs `get` and `list` on `httproutes` and `gateways`.
`deploy/rbac.yaml` shows this for a service account. Change the subject if your kubeconfig uses another user.

## Run locally

```bash
cd src/K8sApi
Kubernetes__KubeconfigPath=$HOME/.kube/config dotnet run
```
