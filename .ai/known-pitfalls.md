# Known pitfalls

- **Job store vs multiple replicas.** Without `ConnectionStrings:Redis` jobs are in memory: a status poll that reaches another replica gets 404, and every restart, deploy or scale-in loses in-flight jobs. Set Redis before running more than one replica. rsd-file-scanner-function tolerates 404 while polling, but that only helps until its polling timeout.
- **Work queue is still per process.** Even with Redis, the download/scan itself runs on the accepting replica's in-memory channel (`AddBackgroundServiceWithParallelism`). A replica that dies mid-scan leaves the job in `queued`/`downloading`/`scanning`; the caller sees a timeout, not a result.
- README drift (`BackgroundScanService`).
- Sync scan on large files.
- Production appsettings may contain Azure AD **app IDs** — still override with env in ACA.
- **Base image distro changed with .NET 10.** `aspnet:10.0` is Ubuntu 24.04, so ClamAV jumped from Debian's 1.0.x to Ubuntu's 1.5.x. Re-check `clamd.conf` options and run an EICAR scan after any base image or distro change.
- **Library-held packages.** `Microsoft.Identity.Web` is referenced directly so its patched `System.Security.Cryptography.Pkcs` wins over the older version pulled in via `GovUK.Dfe.CoreLibs.Security`. Remove the direct reference only once CoreLibs.Security ships a fixed Identity.Web.
- Scanner README documents `VirusScannerApi:BaseUrl` but the generated client uses `ClamAvApiClient:*` (**scanner repo** pitfall).
