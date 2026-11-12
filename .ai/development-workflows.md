# Development

`docker-compose.yml` local port 8080, volume `clamav-db`. `MAX_FILE_SIZE_MB` default 200. Locally the job store is in memory unless `ConnectionStrings__Redis` is set; deployed environments with more than one replica must set it (ACA secret/env). The job key prefix is not secret and is already set per environment in `appsettings.{Environment}.json` (`ScanJobs:RedisKeyPrefix`, e.g. `ClamAV:Test:Jobs:`), so environments sharing a Redis stay separate without extra configuration.

No tests. Deploy workflow on `main`. GitVersion ContinuousDelivery.

Versioning uses **GitVersion 6** (`gittools/actions/gitversion/setup@v4.5.0`, `versionSpec: 6.7.x`), matching DfE.CoreLibs. `GitVersion.yml` uses the v6 key `label` (v5's `tag` is rejected). Client versions come from tags `GovUK.Dfe.ClamAV.Api.Client-{version}` via `/overrideconfig tag-prefix`; PR builds produce `{x.y.z}-prerelease.{n}` (older v5 prereleases used `-prerelease-{n}`). The v0.9 action no longer runs on GitHub runners (it relies on the removed `set-env` command).

CODEOWNERS: `.github/CODEOWNERS`.

Client pack (`ci-pack-api-client.yml`): the repo's default `GITHUB_TOKEN` is read-only, and a reusable workflow cannot raise permissions above its caller. The `pack-and-release` job therefore grants `contents: write` (tag + GitHub release) and `packages: write` itself; drop either and GitHub rejects the whole workflow file as invalid.

## Runtime and image (**Verified**)

- API and client target `net10.0`; CI templates (`build-test-template.yml`, `pack-template.yml`) install the `10.0.x` SDK.
- `Dockerfile` uses `mcr.microsoft.com/dotnet/sdk:10.0` / `aspnet:10.0`. From .NET 10 these default tags are **Ubuntu 24.04**, not Debian, so the ClamAV version is whatever Ubuntu's apt ships (ClamAV 1.5.x at the time of the upgrade). `conf/clamd.conf` and `conf/freshclam.conf` work unchanged on it.
- NSwag regeneration uses `$(NSwagExe_Net100)` and `nswag.json` `runtime: Net100` / `targetFramework: net10.0`. Keep all three in step when the framework changes.
- Swagger UI uses Swashbuckle 11 / Microsoft.OpenApi 3: types live in the `Microsoft.OpenApi` namespace and security requirements are keyed by `OpenApiSecuritySchemeReference`. The client's `swagger.json` comes from NSwag (`AddOpenApiDocument`), not Swashbuckle, so Swashbuckle upgrades do not change the generated client.
- Behind a TLS-inspecting proxy, `freshclam` fails certificate validation, finds no database and `clamd` exits. Mount the proxy's root CA into `/usr/local/share/ca-certificates/`; `scripts/start.sh` runs `update-ca-certificates` before `freshclam`.
