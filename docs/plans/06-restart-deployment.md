# Feature 6: Command – restart deployment

Status: **Not started**

## Goal

Restart a deployment via REST, like `kubectl rollout restart`.

## Plan (draft)

1. Endpoint `POST /api/v1/namespaces/{ns}/deployments/{name}/restart`.
2. Patch the pod template annotation `kubectl.kubernetes.io/restartedAt` with the current time
   (same as `kubectl rollout restart`).
3. New policy for commands, e.g. role `k8s-api-operator` (see feature 2).
4. RBAC: `patch` on `deployments` (apps group).
5. Answers: `202` started, `404` not found, `403` no role, `502` Kubernetes error.
6. Log who restarted what (user name from token).
7. Tests and README.

## Open questions

- Role name for commands?
- Allow restart in all namespaces, or only some?
- Also StatefulSets and DaemonSets?
- Should the API wait for the rollout to finish?
