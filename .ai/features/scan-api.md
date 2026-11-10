# Feature: Scan HTTP API

`POST /scan` sync; `POST /scan/async` file; `POST /scan/async/url` JSON `{ url, isBase64 }`; `GET /scan/async/{jobId}`; `GET /scan/jobs` (cap 100); `GET /version` (auth).

URL scan: job status `downloading` then scan. FlexForms path is **async URL only**. `GET /scan/async/{jobId}` returns 404 when the job is unknown: expired (24h), created on another replica while the in-memory store is in use, or lost in a restart. Request/response shapes are unchanged, so the generated client does not need a new version.
