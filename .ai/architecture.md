# Architecture (ClamAV API)

Entry: `src/GovUK.Dfe.ClamAV/Program.cs` — Kestrel `:8080`. Endpoints: `Endpoints/ScanEndpoints.cs`, `HealthEndpoints.cs`. Handlers: sync stream, async file, async URL (`UrlScanHandler`, `ScanProcessingService`). Jobs: `ScanJobService`. Cleanup: `JobCleanupService` (24h retention). Dockerfile installs ClamAV; `scripts/start.sh` runs freshclam → clamd → dotnet.

Client: `GovUK.Dfe.ClamAV.Api.Client` (NSwag) with `BearerTokenHandler` (secret or managed identity).
