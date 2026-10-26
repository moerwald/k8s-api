# Feature 3: Cluster connection with kubeconfig

Status: **Done**

## Goal

The API connects to Kubernetes with a kubeconfig file. The path comes from settings.

## How it works

- Setting `Kubernetes:KubeconfigPath` (env `Kubernetes__KubeconfigPath`) is required.
- The `current-context` of the file is used.
- Missing setting or missing file → the app stops at startup with a clear error.

## Code

- `src/K8sApi/Program.cs`

## Steps

- [x] Read path from settings
- [x] Fail fast at startup
- [x] Document in README

## Open points

- In-cluster mode (service account token) instead of a kubeconfig? Today only kubeconfig works.
  The Helm chart (feature 8) must mount the kubeconfig, e.g. from a Secret.
