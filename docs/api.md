# API endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/sso/RemoteAuth/Login` | Secret + identity headers | Web login (browser or proxy-triggered) |
| GET | `/sso/RemoteAuth/QuickConnect` | Secret + identity headers | QC UI after header auth + RBAC |
| POST | `/sso/RemoteAuth/QuickConnect/Authorize` | Secret + identity headers + session token | Authorize a Quick Connect code |
| GET | `/sso/RemoteAuth/Config/Libraries` | Admin session | List libraries |
| GET | `/sso/RemoteAuth/Config/Status` | Admin session | Plugin status |
