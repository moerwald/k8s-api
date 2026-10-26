# Feature 9: Zarf package

Status: **Not started**

## Goal

Ship the API as a Zarf package (for air-gapped clusters).

## Plan (draft)

1. `zarf.yaml` in `deploy/zarf`.
2. Component with the Helm chart (feature 8) and the container image (feature 7).
3. Zarf variables for Keycloak authority/audience.
4. Test: `zarf package create` and `zarf package deploy`.

## Open questions

- Zarf version in use?
- Should the package also contain the kubeconfig Secret?
