# Development

`docker-compose.yml` local port 8080, volume `clamav-db`. `MAX_FILE_SIZE_MB` default 200. Locally the job store is in memory unless `ConnectionStrings__Redis` is set; deployed environments with more than one replica must set it (ACA secret/env, alongside `ScanJobs__RedisKeyPrefix` if environments share a Redis).

No tests. Deploy workflow on `main`. GitVersion ContinuousDelivery.

CODEOWNERS: `.github/CODEOWNERS`.
