# Feature 5: Query Kubernetes objects

Status: **Not started**

## Goal

Read Kubernetes objects via REST, e.g. deployments and pods.

## Plan (draft)

1. Decide which objects and which fields to return (see questions).
2. Add endpoints, for example:
   - `GET /api/v1/namespaces/{ns}/deployments`
   - `GET /api/v1/namespaces/{ns}/deployments/{name}`
   - `GET /api/v1/namespaces/{ns}/pods`
3. Return small response models, not the full Kubernetes object.
4. Protect with policy `k8s-api-reader`.
5. Add RBAC rights (`get`, `list`) to `deploy/rbac.yaml` / Helm chart.
6. Tests and README.

## Open questions

- Which object types are needed first?
- Small models or the full Kubernetes JSON?
- Filter by label selector in the query string?
- Limit users to some namespaces?
