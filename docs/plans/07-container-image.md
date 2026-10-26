# Feature 7: Container image

Status: **Not started**

## Goal

Build a container image of the API.

## Plan (draft)

1. Multi-stage `Dockerfile`: build with .NET 10 SDK, run on ASP.NET 10 runtime image.
2. Run as non-root user, port 8080.
3. Optional: GitHub Actions builds and pushes the image.

## Open questions

- Which registry?
- Normal or chiseled/distroless base image?
