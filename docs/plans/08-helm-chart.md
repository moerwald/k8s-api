# Feature 8: Helm chart

Status: **Not started**

## Goal

Install the API in a cluster with Helm.

## Plan (draft)

1. Chart in `deploy/helm/k8s-api`.
2. Templates: Deployment, Service, ServiceAccount, ClusterRole, ClusterRoleBinding
   (move `deploy/rbac.yaml` in here).
3. Values: image, Keycloak authority/audience, kubeconfig Secret name, resources.
4. Mount kubeconfig from a Secret to `/etc/k8s-api/kubeconfig` (see feature 3).
5. Liveness/readiness probe on `/healthz`.
6. Optional: HTTPRoute or Ingress to expose the API.

## Open questions

- Expose with HTTPRoute, Ingress, or not at all?
- Who creates the kubeconfig Secret?
