# rsd-clamav-api — Agent entry point

Dockerised **ClamAV (`clamd`) + .NET 8 minimal API**. FlexForms never calls this directly. `rsd-file-scanner-function` POSTs `/scan/async/url` with a Base64 SAS (or `file://`) URL; this API downloads and scans.

Human doc: [`readme.md`](readme.md) (note: it may still mention `BackgroundScanService.cs` — implementation uses CoreLibs `AddBackgroundServiceWithParallelism`).

## Constraints

1. Do not add FlexForms/TenantConfig knowledge here. This is a generic scanner.
2. Async job state lives in **Redis** when `ConnectionStrings:Redis` is set (`RedisScanJobService`), otherwise in an **in-memory** `ConcurrentDictionary` that replicas do not share. Never run more than one replica without Redis. The scan work itself is always in-process on the accepting replica.
3. `/healthz` is anonymous; scan endpoints require JWT role `API.Read` (`CanRead`).
4. Large files: use async URL/file, not sync `POST /scan` (loads stream via NClam).
5. NSwag client regenerates when `CI != true`. docker-compose sets `CI: true` (skips regen).
6. No Terraform in this repo. Deploy: `.github/workflows/deploy.yml` → Azure Container Apps. Client pack: `ci-pack-api-client.yml` (trusted publishing per repo if configured).
7. **No test project** today. Don’t assume CI runs tests.

## Knowledge map

[`.ai/architecture.md`](.ai/architecture.md) · [`.ai/integrations.md`](.ai/integrations.md) · [`.ai/security.md`](.ai/security.md) · [`.ai/known-pitfalls.md`](.ai/known-pitfalls.md) · [`.ai/features/scan-api.md`](.ai/features/scan-api.md) · [`.ai/history-lessons.md`](.ai/history-lessons.md)

Platform decision: flexforms-api `docs/adrs/20251125_use_clamav_for_virus_scanning.md`.

## Knowledge maintenance

Trust code over docs, and fix the docs when they disagree.

### Definition of done (mandatory)

This is enforced in CI: the **Knowledge base check** workflow fails a pull request that changes `src/` without touching `readme.md`, `AGENTS.md` or `.ai/`, unless the PR has the `no-docs` label (adding the label re-runs it automatically).

A task is **not complete** until you have checked whether it changed anything the knowledge base describes. Before finishing, update every affected doc in the same change, then list them in your final summary or PR description — **or** state “No knowledge impact” with a one-line reason.

| If you changed… | Update |
|-----------------|--------|
| Endpoints or request/response shape | `.ai/features/scan-api.md`, `readme.md`, and flag rsd-file-scanner-function (it uses the generated client) |
| Job storage, background processing, limits | `.ai/architecture.md`, `.ai/known-pitfalls.md` |
| Auth or roles | `.ai/security.md` |
| Docker, deploy or client packaging | `.ai/development-workflows.md` |
| Something that makes an existing statement wrong | Correct or delete it — never leave it stale |

This repository is **public**. Never copy secrets from `appsettings.*.json` or environment config, and never describe unfixed security weaknesses in docs.
