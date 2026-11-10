# Development

`docker-compose.yml` local port 8080, volume `clamav-db`. `MAX_FILE_SIZE_MB` default 200. Locally the job store is in memory unless `ConnectionStrings__Redis` is set; deployed environments with more than one replica must set it (ACA secret/env). The job key prefix is not secret and is already set per environment in `appsettings.{Environment}.json` (`ScanJobs:RedisKeyPrefix`, e.g. `ClamAV:Test:Jobs:`), so environments sharing a Redis stay separate without extra configuration.

No tests. Deploy workflow on `main`. GitVersion ContinuousDelivery.

CODEOWNERS: `.github/CODEOWNERS`.
