# Known pitfalls

- In-memory jobs vs multiple replicas.
- README drift (`BackgroundScanService`).
- Sync scan on large files.
- Production appsettings may contain Azure AD **app IDs** — still override with env in ACA.
- Scanner README documents `VirusScannerApi:BaseUrl` but the generated client uses `ClamAvApiClient:*` (**scanner repo** pitfall).
