# Jellyfin Remote Auth Plugin

A Jellyfin plugin for **trusted header SSO** (forward auth / remote user authentication).

Your reverse proxy (Authentik, Authelia, Traefik ForwardAuth, Caddy, nginx auth_request…) authenticates the user and injects identity headers. This plugin reads those headers, provisions the Jellyfin user, applies role-based library access, and issues a session.

## How It Works

```
Browser          Reverse Proxy           Jellyfin Plugin
   |                   |                       |
   |-- request ------->|                       |
   |                   |-- forward auth check ->|IdP
   |                   |<-- 200 + headers ------|
   |                   |                       |
   |                   |-- GET /sso/RemoteAuth/Login
   |                   |   X-Remote-Auth-Secret: <secret>
   |                   |   X-Remote-Auth-User: alice
   |                   |   X-Remote-Auth-Groups: jellyfin-users|jellyfin-4k
   |                   |                       |
   |                   |<-- HTML (stores token, redirects to /)
   |<-- redirect / ----|                       |
   |-- authenticated -->                       |
```

1. User hits the Jellyfin login page (or a protected URL).
2. Reverse proxy intercepts, authenticates via IdP (OIDC, LDAP, etc.).
3. Proxy makes a sub-request to `/sso/RemoteAuth/Login` with the secret + identity headers.
4. Plugin validates the secret, syncs the user, applies RBAC, and returns a small HTML page.
5. The HTML stores the Jellyfin session token in `localStorage` and redirects to `/`.

## Security — Read This First

This plugin uses **two layers** of security. Both are required.

### Layer 1: Shared Secret Header

Every request to `/sso/RemoteAuth/Login` must include the configured secret header. Requests without the correct value are rejected with `401`. This provides defense-in-depth if something bypasses the network layer.

The comparison is done in **constant time** (`CryptographicOperations.FixedTimeEquals`) to prevent timing attacks. Use a long random string (32+ bytes) as the secret.

### Layer 2: Network Isolation (Mandatory)

**The secret alone is not sufficient.** An attacker who can reach the endpoint directly and brute-force or guess the secret could authenticate as any user.

**You must** ensure that only your reverse proxy can reach `/sso/RemoteAuth/Login`.

**Docker / Docker Compose**

Put Jellyfin on an internal network. Only the proxy container should be on that network.

```yaml
networks:
  proxy:         # proxy → Jellyfin
    internal: false
  internal:      # Jellyfin-only services
    internal: true

services:
  proxy:
    networks: [proxy]
  jellyfin:
    networks: [proxy, internal]
    # Do NOT publish port 8096 directly
```

**Kubernetes — NetworkPolicy**

```yaml
apiVersion: networking.k8s.io/v1
kind: NetworkPolicy
metadata:
  name: jellyfin-remoteauth-ingress
  namespace: jellyfin
spec:
  podSelector:
    matchLabels:
      app: jellyfin
  policyTypes: [Ingress]
  ingress:
    # Allow Traefik/proxy to reach the SSO endpoint
    - from:
        - namespaceSelector:
            matchLabels:
              kubernetes.io/metadata.name: traefik
          podSelector:
            matchLabels:
              app: traefik
      ports:
        - port: 8096
    # Allow normal Jellyfin traffic from ingress only
    - from:
        - namespaceSelector:
            matchLabels:
              kubernetes.io/metadata.name: traefik
```

Both layers (secret + network isolation) should be used together.

## Installation

### Via Plugin Repository (Recommended)

1. In Jellyfin, go to **Admin Dashboard → Plugins → Repositories**
2. Add a new repository:
   - **Name:** `Remote Auth`
   - **URL:** `https://raw.githubusercontent.com/BlackDark/jellyfin-plugin-remoteauth/main/manifest.json`
3. Go to **Catalog**, find "Remote Auth" under Authentication, and install it
4. Restart Jellyfin

The manifest is automatically updated on each release.

### From GitHub Release

1. Download the latest `.zip` from [Releases](https://github.com/BlackDark/jellyfin-plugin-remoteauth/releases)
2. Extract to your Jellyfin plugins directory:
   ```bash
   # Linux
   unzip remote-auth_*.zip -d /var/lib/jellyfin/plugins/RemoteAuth/

   # Docker — mount or copy into container
   unzip remote-auth_*.zip -d /config/plugins/RemoteAuth/
   ```
3. Restart Jellyfin

### From Source

```bash
# With .NET 9 SDK
make build

# Copy to Jellyfin plugins directory
sudo cp dist/*.dll dist/meta.json /var/lib/jellyfin/plugins/RemoteAuth/
sudo systemctl restart jellyfin
```

### With Docker (no SDK needed)

```bash
make docker-build
sudo cp dist/*.dll dist/meta.json /var/lib/jellyfin/plugins/RemoteAuth/
sudo systemctl restart jellyfin
```

## Configuration

Go to **Admin Dashboard → Plugins → Remote Auth**.

### General Tab

| Field | Default | Description |
|-------|---------|-------------|
| Enable Remote Auth | true | Master on/off switch |
| Secret Header Name | `X-Remote-Auth-Secret` | Name of the shared secret header |
| Secret Header Value | *(empty)* | The shared secret — plugin refuses requests if blank |
| Username Header | `X-Remote-Auth-User` | Header carrying the username |
| Email Header | `X-Remote-Auth-Email` | Optional email header |
| Display Name Header | `X-Remote-Auth-Name` | Optional display name header |
| Groups Header | `X-Remote-Auth-Groups` | Header with pipe-delimited group list |
| Groups Delimiter | `\|` | Delimiter used in the groups header |
| Admin Group | *(empty)* | Shortcut: users in this group always get admin |
| Auto-create Users | true | Create Jellyfin accounts on first login |
| Default Role | *(empty)* | Fallback role mapping when no groups match |

### Role Mappings Tab

Map IdP group names to Jellyfin permissions. Multiple matched groups are merged (union semantics — most permissive wins).

## Reverse Proxy Examples

### Authentik + Traefik

**Authentik outpost** handles forward auth. Configure the provider's additional headers to inject user info.

In Authentik: go to **Providers → Your Provider → Advanced → Additional scopes/headers** and add:

```
X-Remote-Auth-User: %(username)s
X-Remote-Auth-Name: %(name)s
X-Remote-Auth-Groups: %(groups | join("|"))s
```

Traefik middleware:

```yaml
# traefik/config/middlewares.yml
http:
  middlewares:
    authentik:
      forwardAuth:
        address: "http://authentik-proxy:9000/outpost.goauthentik.io/auth/traefik"
        trustForwardHeader: true
        authResponseHeaders:
          - X-Remote-Auth-User
          - X-Remote-Auth-Name
          - X-Remote-Auth-Groups
```

Jellyfin service label:

```yaml
labels:
  - "traefik.http.routers.jellyfin.middlewares=authentik@file"
```

Proxy must also forward the secret. Add to the outpost or middleware chain:

```yaml
# Add the secret as a request header before forwarding to Jellyfin
- "traefik.http.middlewares.add-ra-secret.headers.customrequestheaders.X-Remote-Auth-Secret=your-long-random-secret"
```

Set the same value in the plugin's **Secret Header Value** field.

### Caddy

```caddy
jellyfin.example.com {
    forward_auth authentik:9000 {
        uri /outpost.goauthentik.io/auth/caddy
        copy_headers X-Remote-Auth-User X-Remote-Auth-Name X-Remote-Auth-Groups
    }

    # Inject the secret
    header X-Remote-Auth-Secret "your-long-random-secret"

    reverse_proxy jellyfin:8096
}
```

### Authelia

```yaml
# authelia config
access_control:
  rules:
    - domain: jellyfin.example.com
      policy: one_factor
```

Traefik / nginx forwards `Remote-User`, `Remote-Groups` headers. Map them in the plugin's header config fields.

## Kubernetes Ingress with Auth Middleware

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: jellyfin
  annotations:
    traefik.ingress.kubernetes.io/router.middlewares: "traefik-authentik@kubernetescrd"
spec:
  rules:
    - host: jellyfin.example.com
      http:
        paths:
          - path: /
            pathType: Prefix
            backend:
              service:
                name: jellyfin
                port:
                  number: 8096
---
apiVersion: traefik.io/v1alpha1
kind: Middleware
metadata:
  name: authentik
  namespace: traefik
spec:
  forwardAuth:
    address: http://authentik-proxy.authentik.svc.cluster.local:9000/outpost.goauthentik.io/auth/traefik
    trustForwardHeader: true
    authResponseHeaders:
      - X-Remote-Auth-User
      - X-Remote-Auth-Name
      - X-Remote-Auth-Groups
```

## RBAC

### Role Merging

When a user matches multiple role mappings (because they're in multiple groups), permissions are **merged with union semantics**:

- Boolean permissions: `true` if **any** matched mapping has it enabled
- Libraries: union of all matched mappings' library sets
- `EnableAllLibraries`: `true` if any mapping enables it
- `MaxParentalRating`: highest value across all matched mappings

### Default Role

If none of the user's groups match any role mapping, the **Default Role** (configured in General tab) is used as a fallback. Leave blank to deny access to unmatched users.

### AdminGroup Shortcut

If **Admin Group** is set and a user is a member of that group, they receive `IsAdministrator = true` regardless of role mappings.

## TV Client Compatibility

Smart TV and mobile clients authenticate via `/Users/AuthenticateByName` (standard Jellyfin API) — they are **not affected** by this plugin. Remote Auth only manages the web-based SSO flow via `/sso/RemoteAuth/Login`.

Users managed by this plugin have password login disabled. If you need to allow TV clients for SSO users, you can either:
- Set up the Jellyfin TV app with the session token from a web login (token sharing), or
- Leave standard auth enabled for a separate TV-only account.

## API Endpoints

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/sso/RemoteAuth/Login` | Secret header | Header-based login (called by proxy) |
| GET | `/sso/RemoteAuth/Config/Libraries` | Admin | List available libraries |
| GET | `/sso/RemoteAuth/Config/Status` | Admin | Plugin status |

## Building

### Requirements

- .NET 9.0 SDK **or** Docker

```bash
# SDK
make build

# Docker only
make docker-build

# Installable zip
make package
```

## Project Structure

```
Jellyfin.Plugin.RemoteAuth/
  RemoteAuthPlugin.cs              # Plugin entry point
  Configuration/
    PluginConfiguration.cs         # Config DTOs
    configPage.html                # Admin UI (embedded)
    remoteauth.js                  # Admin UI JS (embedded)
  Api/
    RemoteAuthController.cs        # /sso/RemoteAuth/Login
    ConfigController.cs            # Admin config API
  Auth/
    RemoteAuthProvider.cs          # Blocks password login for managed users
  Services/
    RbacService.cs                 # Group → permission mapping engine
    UserSyncService.cs             # User provisioning and sync
    ServiceRegistrator.cs          # DI registration
```

## License

GPLv3 (required by linking against Jellyfin's GPLv3 libraries)
