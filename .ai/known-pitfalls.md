# Known pitfalls

- **Job store vs multiple replicas.** Without `ConnectionStrings:Redis` jobs are in memory: a status poll that reaches another replica gets 404, and every restart, deploy or scale-in loses in-flight jobs. Set Redis before running more than one replica. rsd-file-scanner-function tolerates 404 while polling, but that only helps until its polling timeout.
- **Work queue is still per process.** Even with Redis, the download/scan itself runs on the accepting replica's in-memory channel (`AddBackgroundServiceWithParallelism`). A replica that dies mid-scan leaves the job in `queued`/`downloading`/`scanning`; the caller sees a timeout, not a result.
- README drift (`BackgroundScanService`).
- Sync scan on large files.
- Production appsettings may contain Azure AD **app IDs** — still override with env in ACA.
- Scanner README documents `VirusScannerApi:BaseUrl` but the generated client uses `ClamAvApiClient:*` (**scanner repo** pitfall).
