# Reverse proxy

Every setup must:

1. Authenticate the user at the IdP
2. Inject identity headers plus the shared secret on requests to `/sso/RemoteAuth/*`
3. Send the browser to `/sso/RemoteAuth/Login` after auth ([branding button](branding.md) or a redirect)

Default recipe below: **Authentik + Traefik**. Caddy, Authelia, and Kubernetes Ingress variants follow.

Also apply [network isolation](security.md#layer-2-network-isolation-mandatory).

## Authentik and Traefik (default)

In Authentik (**Providers → your provider → Advanced**), add headers:

```
X-Remote-Auth-User: %(username)s
X-Remote-Auth-Name: %(name)s
X-Remote-Auth-Groups: %(groups | join("|"))s
```

Put users in an IdP group that matches a Role Mapping name (for example `jellyfin-users`).

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
    add-ra-secret:
      headers:
        customRequestHeaders:
          X-Remote-Auth-Secret: "your-long-random-secret"
```

Jellyfin labels (both middlewares; order matters: Authentik first, then secret):

```yaml
labels:
  - "traefik.http.routers.jellyfin.middlewares=authentik@file,add-ra-secret@file"
```

Use the same secret string in the plugin **Secret Header Value** field.

## Caddy

Configure Authentik (or your IdP) to emit the same identity headers as in [Authentik and Traefik](#authentik-and-traefik-default), then:

```caddy
jellyfin.example.com {
    forward_auth authentik:9000 {
        uri /outpost.goauthentik.io/auth/caddy
        copy_headers X-Remote-Auth-User X-Remote-Auth-Name X-Remote-Auth-Groups
    }

    header X-Remote-Auth-Secret "your-long-random-secret"

    reverse_proxy jellyfin:8096
}
```

Note: this `header` directive adds the secret on all upstream requests to Jellyfin, not only SSO paths.

## Authelia

Authelia typically emits `Remote-User` / `Remote-Groups`. Point the plugin header fields at those names (or rename in the proxy).

Plugin General tab (example):

| Field | Value |
|-------|-------|
| Username Header | `Remote-User` |
| Groups Header | `Remote-Groups` |
| Groups Delimiter | `,` (Authelia's default) or match your Authelia config |

```yaml
# authelia configuration.yml (sketch)
access_control:
  rules:
    - domain: jellyfin.example.com
      policy: one_factor
```

Traefik ForwardAuth example (inject secret + forward Authelia headers):

```yaml
http:
  middlewares:
    authelia:
      forwardAuth:
        address: "http://authelia:9091/api/authz/forward-auth"
        trustForwardHeader: true
        authResponseHeaders:
          - Remote-User
          - Remote-Groups
    add-ra-secret:
      headers:
        customRequestHeaders:
          X-Remote-Auth-Secret: "your-long-random-secret"
```

Wire both middlewares on the Jellyfin router. Set **Secret Header Value** in the plugin to the same string.

## Kubernetes Ingress

Same rules as Docker Traefik: forward-auth headers **and** secret injection. Pair with the [NetworkPolicy](security.md#kubernetes-networkpolicy).

```yaml
apiVersion: networking.k8s.io/v1
kind: Ingress
metadata:
  name: jellyfin
  annotations:
    traefik.ingress.kubernetes.io/router.middlewares: "traefik-authentik@kubernetescrd,traefik-add-ra-secret@kubernetescrd"
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
---
apiVersion: traefik.io/v1alpha1
kind: Middleware
metadata:
  name: add-ra-secret
  namespace: traefik
spec:
  headers:
    customRequestHeaders:
      X-Remote-Auth-Secret: "your-long-random-secret"
```
