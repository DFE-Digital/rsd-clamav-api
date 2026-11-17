# Architecture (ClamAV API)

Entry: `src/GovUK.Dfe.ClamAV/Program.cs` — Kestrel `:8080`. Endpoints: `Endpoints/ScanEndpoints.cs`, `HealthEndpoints.cs`. Handlers: sync stream, async file, async URL (`UrlScanHandler`, `ScanProcessingService`). Jobs: `IScanJobService` — `RedisScanJobService` when `ConnectionStrings:Redis` is set (job JSON under `{ScanJobs:RedisKeyPrefix}{jobId}` with a 24h expiry, plus a sorted-set index for `GET /scan/jobs`), otherwise the in-memory `ScanJobService` with a startup warning. Cleanup: `JobCleanupService` (24h retention; with Redis it only trims the index). Downloads and scans still run on an in-memory CoreLibs channel on the replica that accepted the request, so a job whose replica dies stays in its last status until the caller's polling times out.

Redis job store rules (**Verified**, `RedisScanJobService`): updates use `SET ... KEEPTTL XX` (`When.Exists`) so a job that expired between read and write is not recreated without an expiry; `GET /scan/jobs` reads jobs with one `GET` each, not `MGET`, because job keys hash to different slots on Redis Cluster. `ScanProcessingService` deletes the temp file in a `finally` and records failures through `TryMarkFailedAsync`, which never throws, so a Redis outage while recording an error cannot leave downloaded files on disk. Dockerfile installs ClamAV; `scripts/start.sh` runs freshclam → clamd → dotnet.

Client: `GovUK.Dfe.ClamAV.Api.Client` (NSwag) with `BearerTokenHandler` (secret or managed identity).

## Code map (**Verified** 2026-11-17)

| Path (`src/GovUK.Dfe.ClamAV/`) | Role |
|------|------|
| `Program.cs` | Config (appsettings → env → args), `MAX_FILE_SIZE_MB` request limit (default 200), Microsoft.Identity.Web JWT (`AzureAd` section), CoreLibs `AddApplicationAuthorization` (policy `CanRead`), Swagger + NSwag document, job store choice (Redis vs memory, warning when memory), `AddBackgroundServiceWithParallelism` (bounded in-process work channel), `JobCleanupService` |
| `Endpoints/ScanEndpoints.cs` | `POST /scan` (sync multipart), `POST /scan/async` (multipart → 202), `POST /scan/async/url` (`{ url, isBase64 }` → 202), `GET /scan/async/{jobId}`, `GET /scan/jobs`; all `RequireAuthorization("CanRead")` |
| `Endpoints/HealthEndpoints.cs` | `GET /healthz` anonymous, `GET /version` (auth) |
| `Handlers/FileScanHandler.cs`, `UrlScanHandler.cs` | Validate, create job, enqueue work |
| `Services/ScanProcessingService.cs` | Download (URL) to temp file, stream to `clamd` (`CLAMD_HOST` 127.0.0.1, `CLAMD_PORT` 3310, NClam), update job, always delete temp file |
| `Services/ScanJobService.cs` / `RedisScanJobService.cs` | `IScanJobService` in memory / Redis (`ScanJobs:RedisKeyPrefix`, per environment e.g. `ClamAV:Test:Jobs:`) |
| `Services/ClamAvInfoService.cs` | `clamd` version for `/version` |
| `Models/ScanJob.cs` | `JobId`, `FileName`, `FileSize`, `Status` (`queued` → `downloading` → `scanning` → `clean`/`infected`/`error`), `Malware`, `Error`, `Engine`, `CreatedAt`, `CompletedAt` |

Client package `src/GovUK.Dfe.ClamAV.Api.Client/`: NSwag `Generated/Client.g.cs` + `Contracts.g.cs`, `Extensions/ServiceCollectionExtensions.AddClamAvApiClient<TInterface, TImpl>(config)` bound to `ClamAvApiClient` (`BaseUrl`, `ClientId`, `ClientSecret`, `Authority`, `Scope`, `MiClientId`), `Security/TokenAcquisitionService` (client secret or managed identity) + `BearerTokenHandler`. Only consumer in this workspace: rsd-file-scanner-function (1.0.0).

Platform context: [`ecosystem.md`](ecosystem.md).
