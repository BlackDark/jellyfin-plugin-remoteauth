# Security

Two layers. Both are required.

## Layer 1: Shared secret header

Every request to these endpoints must include the configured secret header:

- `/sso/RemoteAuth/Login`
- `/sso/RemoteAuth/QuickConnect`
- `/sso/RemoteAuth/QuickConnect/Authorize`

Wrong or missing secret → `401`. Blank **Secret Header Value** in config → `503` (plugin refuses all requests).

Comparison uses constant time (`CryptographicOperations.FixedTimeEquals`). Generate with `openssl rand -hex 32` (or equivalent).

## Layer 2: Network isolation (mandatory)

The secret alone is not enough. Anyone who can reach the endpoint and guess the secret can authenticate as any user.

Only your reverse proxy may reach the SSO endpoints above.

### Docker / Docker Compose

Put Jellyfin on an internal network. Only the proxy container shares that network. Do not publish port `8096` on the host.

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

### Kubernetes NetworkPolicy

Isolation for the Jellyfin pod (port `8096` is not path-scoped; the proxy still must inject the secret).

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
    - from:
        - namespaceSelector:
            matchLabels:
              kubernetes.io/metadata.name: traefik
          podSelector:
            matchLabels:
              app: traefik
      ports:
        - port: 8096
```

For Traefik Ingress + Middleware CRDs (headers and secret injection), see [proxy.md](proxy.md#kubernetes-ingress).
