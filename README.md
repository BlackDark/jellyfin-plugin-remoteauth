# Jellyfin Remote Auth Plugin

Trusted-header SSO for Jellyfin (forward auth / remote user authentication).

Use this when a reverse proxy (Authentik, Authelia, Traefik ForwardAuth, Caddy, …) already authenticates users and can inject identity headers. The plugin reads those headers, provisions the Jellyfin user, applies role-based library access, and issues a session.

Prerequisite: working reverse proxy + identity provider (IdP). This plugin does not replace the IdP.

## Quick start

1. **Install** the plugin ([Catalog](#installation)), then restart Jellyfin.
2. Open **Admin Dashboard → Plugins → Remote Auth**.
3. Set **Secret Header Value** to the output of a long random generator (example: `openssl rand -hex 32`). Leave header names at defaults unless your proxy needs different names.
4. On the **Role Mappings** tab, add at least one mapping whose **Role / Group Name** matches an IdP group as it appears in the groups header (matching is case-insensitive; example `jellyfin-users`). Enable the libraries that group should see.
5. Leave **Default Role** blank to deny unmatched users (`403`), or set it to a Role Mapping name for a fallback.
6. Wire the reverse proxy ([default: Authentik + Traefik](docs/proxy.md#authentik-and-traefik-default)): authenticate, inject username/groups headers, inject the **same** secret as step 3, and [isolate SSO endpoints](docs/security.md).
7. Send the browser to **`/sso/RemoteAuth/Login`**. Headers on ordinary Jellyfin pages are not enough. Easiest path: [branding SSO button](docs/branding.md) (**Dashboard → Branding**).
8. **Verify:** SSO button (or `/sso/RemoteAuth/Login` behind the proxy) → Jellyfin home while logged in. Failures → [Troubleshooting](#troubleshooting).

Other proxies (Caddy, Authelia, Kubernetes Ingress): [docs/proxy.md](docs/proxy.md).

Existing users / permission overwrite / disabled-user behavior: [docs/migration.md](docs/migration.md).

## How it works

```
Browser          Reverse Proxy           Jellyfin Plugin
   |                   |                       |
   |-- request ------->|                       |
   |                   |-- forward auth check ->| IdP
   |                   |<-- 200 + headers ------|
   |                   |                       |
   |-- GET /sso/RemoteAuth/Login -------------->|
   |   (proxy injects identity + secret headers)|
   |                   |                       |
   |<-- HTML (stores token, redirects to /) ----|
   |-- authenticated -->                       |
```

1. User opens Jellyfin (branding SSO button or redirect to Login).
2. Reverse proxy authenticates via the IdP and attaches identity headers.
3. Browser `GET /sso/RemoteAuth/Login` reaches Jellyfin with secret + identity headers.
4. Plugin validates the secret, syncs the user, applies RBAC, returns HTML that stores the session token and redirects home.

## Security

Two layers, both required:

1. **Shared secret** on `/sso/RemoteAuth/Login`, `/QuickConnect`, and `/QuickConnect/Authorize` (wrong/missing → `401`; blank config secret → `503`).
2. **Network isolation** so only the reverse proxy can reach those endpoints.

Details and Docker / Kubernetes NetworkPolicy examples: [docs/security.md](docs/security.md).

## Installation

1. In Jellyfin: **Admin Dashboard → Plugins → Repositories**
2. Add a repository:
   - **Name:** `Remote Auth`
   - **URL:** `https://raw.githubusercontent.com/BlackDark/jellyfin-plugin-remoteauth/main/manifest.json`
3. **Catalog** → Authentication → install **Remote Auth**
4. Restart Jellyfin

Zip / from-source installs: [docs/install.md](docs/install.md).

## Configuration

**Admin Dashboard → Plugins → Remote Auth**

### General tab

| Field | Default | Description |
|-------|---------|-------------|
| Enable Remote Auth | true | Master on/off (`Enabled` in XML). Off → `503` on SSO endpoints |
| Secret Header Name | `X-Remote-Auth-Secret` | Shared secret header name |
| Secret Header Value | *(empty)* | Shared secret; blank refuses all requests (`503`) |
| Username Header | `X-Remote-Auth-User` | Required; must match Jellyfin username exactly (case-sensitive) |
| Email Header | `X-Remote-Auth-Email` | Unused; kept for proxy compat |
| Display Name Header | `X-Remote-Auth-Name` | Not written to the Jellyfin user; kept for proxy compat |
| Groups Header | `X-Remote-Auth-Groups` | Pipe-delimited IdP groups |
| Groups Delimiter | `\|` | Delimiter in the groups header |
| Admin Group | *(empty)* | Members always get admin + all libraries |
| Auto-create Users | true | Create Jellyfin accounts on first login |
| Allow password login | true | Hybrid: keep `AuthenticateByName` (Infuse). Off = Remote Auth-only after SSO |
| Default Role | *(empty)* | Fallback Role Mapping name; **blank = deny (`403`)** |

### Role Mappings tab

Map IdP group names to Jellyfin permissions. Group/role matching is case-insensitive. Multiple matches merge with union semantics (most permissive wins). **Priority** only sorts merge order.

Minimal example: Role / Group Name `jellyfin-users`, All Libraries on.

Manual user-editor permission edits are overwritten on the next successful SSO. More detail: [docs/rbac.md](docs/rbac.md), [docs/migration.md](docs/migration.md).

XML paths, backup, and GitOps example: [docs/config-file.md](docs/config-file.md).

## Apps: password and Quick Connect

| Client | How to sign in |
|--------|----------------|
| Browser (proxy SSO) | `GET /sso/RemoteAuth/Login` with headers + RBAC match |
| Infuse / username+password apps | **Allow password login** on (default). Set a known Jellyfin password. New auto-created users get a random unknown password until you set one. |
| Official apps with Quick Connect | Optional when hybrid password is on. See steps below. |

**Allow password login** (default **on**): hybrid SSO + local password. Password login bypasses IdP MFA. Turn **off** for Remote Auth-only accounts.

### Quick Connect

When password login is off, or for clients that support it:

1. Enable Quick Connect in Jellyfin (**Dashboard → General → Quick Connect**).
2. Protect `/sso/RemoteAuth/QuickConnect` and `POST .../Authorize` like Login (secret + identity headers).
3. Open `/sso/RemoteAuth/QuickConnect` in a browser behind the proxy.
4. Enter the code from the app. Username in headers must match the QC session. After 5 bad codes the session is invalidated.

Infuse does not support Quick Connect. Use hybrid password login. Unmatched users still get **403**.

## Troubleshooting

| Symptom | Likely cause |
|---------|----------------|
| `503` | Plugin disabled, or **Secret Header Value** blank |
| `401` | Wrong/missing secret, or missing username header |
| `403` | Groups header does not match Role Mapping / Default Role / Admin Group (check delimiter `|` vs `,`) |
| Login works for some users only | Username must match Jellyfin username exactly (case-sensitive). See [docs/migration.md](docs/migration.md) |
| Headers present but no session | Never hit `/sso/RemoteAuth/Login` ([branding](docs/branding.md) or redirect) |
| Infuse fails after SSO | Set a known Jellyfin password; keep **Allow password login** on |

## More docs

| Doc | Contents |
|-----|----------|
| [docs/security.md](docs/security.md) | Secret + network isolation (Docker / NetworkPolicy) |
| [docs/proxy.md](docs/proxy.md) | Authentik/Traefik, Caddy, Authelia, K8s Ingress |
| [docs/branding.md](docs/branding.md) | Login-page SSO button |
| [docs/rbac.md](docs/rbac.md) | Merge rules, Default Role, Admin Group, Base URL |
| [docs/config-file.md](docs/config-file.md) | XML paths, backup, automation |
| [docs/migration.md](docs/migration.md) | Takeover, overwrite, disabled users |
| [docs/install.md](docs/install.md) | Zip / from-source install |
| [docs/api.md](docs/api.md) | Endpoint table |
| [docs/developing.md](docs/developing.md) | Releases, project structure |

## License

GPLv3 (required by linking against Jellyfin's GPLv3 libraries)
