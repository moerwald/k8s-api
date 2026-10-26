# K8s API

A C# REST API to query Kubernetes objects and (later) send commands.
Login is done with Keycloak. Install is planned with Zarf and Helm.

## Feature 1: Discoverable HTTPRoutes

`GET /api/v1/httproutes/discoverable`

- Needs a Keycloak token with the realm role `k8s-api-reader`.
- Finds all HTTPRoutes in all namespaces with label selector `discoverable=true,domain`.
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

### Errors

- `401` no or bad token
- `403` role `k8s-api-reader` missing
- `502` Kubernetes API not reachable

## Configuration

`appsettings.json` or environment variables:

| Setting | Env variable | Example |
|---------|--------------|---------|
| `Kubernetes:KubeconfigPath` (required) | `Kubernetes__KubeconfigPath` | `/etc/k8s-api/kubeconfig` |
| `Keycloak:Authority` | `Keycloak__Authority` | `https://keycloak.example.com/realms/my-realm` |
| `Keycloak:Audience` | `Keycloak__Audience` | `k8s-api` |

In Keycloak, the token must have `k8s-api` in its `aud` claim (use an audience mapper on the client).

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
