# Feature: Scan HTTP API

`POST /scan` sync; `POST /scan/async` file; `POST /scan/async/url` JSON `{ url, isBase64 }`; `GET /scan/async/{jobId}`; `GET /scan/jobs` (cap 100); `GET /version` (auth).

URL scan: job status `downloading` then scan. FlexForms path is **async URL only**.
