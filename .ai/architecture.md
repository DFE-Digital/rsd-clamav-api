# Architecture (ClamAV API)

Entry: `src/GovUK.Dfe.ClamAV/Program.cs` — Kestrel `:8080`. Endpoints: `Endpoints/ScanEndpoints.cs`, `HealthEndpoints.cs`. Handlers: sync stream, async file, async URL (`UrlScanHandler`, `ScanProcessingService`). Jobs: `IScanJobService` — `RedisScanJobService` when `ConnectionStrings:Redis` is set (job JSON under `{ScanJobs:RedisKeyPrefix}{jobId}` with a 24h expiry, plus a sorted-set index for `GET /scan/jobs`), otherwise the in-memory `ScanJobService` with a startup warning. Cleanup: `JobCleanupService` (24h retention; with Redis it only trims the index). Downloads and scans still run on an in-memory CoreLibs channel on the replica that accepted the request, so a job whose replica dies stays in its last status until the caller's polling times out. Dockerfile installs ClamAV; `scripts/start.sh` runs freshclam → clamd → dotnet.

Client: `GovUK.Dfe.ClamAV.Api.Client` (NSwag) with `BearerTokenHandler` (secret or managed identity).
