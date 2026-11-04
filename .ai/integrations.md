# Integrations

**Inbound:** scanner Function `VirusScannerService.ScanFileByUrlAsync` → `POST /scan/async/url` then poll `GET /scan/async/{jobId}`.

**Not used:** CoreLibs FileStorage. This API HTTP-downloads the URL. Network must reach Azure File Share SAS endpoints.

No Service Bus in this repo.
