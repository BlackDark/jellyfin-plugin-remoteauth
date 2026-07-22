# Remote Auth Stability Fixes — Design

**Date:** 2026-07-22  
**Status:** Approved (design conversation)  
**Repo:** jellyfin-plugin-remoteauth

## Problem

Trusted-header SSO works at the HTTP layer, but Jellyfin 10.11 silently drops permission/preference writes made via `UpdateUserAsync`. Sibling plugin jellyfin-plugin-oidc already fixed this with `UpdatePolicyAsync`. Remote Auth also lacks deny-on-no-match (README claims it), base-path session HTML, create-user concurrency retry, Quick Connect for native apps, automated tests, and `meta.json` sync on release.

## Goals

1. RBAC permissions and library access persist correctly on Jellyfin 10.11.
2. Unmatched users (no role mapping, no Default Role, AdminGroup not granting access) get **403** and no session; access auto-heals on next login after IdP group membership is granted.
3. Session HTML honors Jellyfin Base URL.
4. New-user provisioning survives DbUpdateConcurrencyException.
5. Native/TV apps can complete SSO via Quick Connect.
6. Unit tests runnable via mise + Makefile.
7. Release workflow updates both `manifest.json` and plugin `meta.json`.

## Non-goals

- Full OIDC inside this plugin (proxy/IdP remains external).
- Changing Priority from union-merge to exclusive precedence.
- Profile-image sync (no reliable picture URL in typical forward-auth headers).
- Applying email to Jellyfin user entities (API does not expose a simple email write path we will invent).

## Architecture

```
Proxy (IdP groups) → GET /sso/RemoteAuth/Login
                   → secret check → parse headers
                   → UserSyncService (create/retry + provider id)
                   → RbacService (match / DefaultRole / AdminGroup / deny)
                   → AuthenticateDirect OR 403
                   → HTML (basePath-aware localStorage) OR Quick Connect UI
```

Quick Connect path:

```
GET  /sso/RemoteAuth/QuickConnect          → same header auth + sync + state token → QC HTML
POST /sso/RemoteAuth/QuickConnect/Authorize → validate state token + code → IQuickConnect.AuthorizeRequest
```

## Behavior details

### RBAC persistence

- Load policy via `_userManager.GetUserDto(user).Policy`.
- Mutate policy fields (admin, playback, libraries, parental rating, `IsDisabled=false`).
- Persist with `_userManager.UpdatePolicyAsync(userId, policy)`.
- If `merged.IsAdmin || merged.EnableAllLibraries` → `EnableAllFolders=true`, clear `EnabledFolders`.
- AdminGroup: membership still forces admin (and thus all folders) when resolving matched or AdminGroup-only paths.

### Deny unmatched (option A)

After resolving Default Role fallback and AdminGroup shortcut:

| Condition | Result |
|-----------|--------|
| ≥1 matched RoleMapping | Apply merged policy; continue login |
| 0 matches, DefaultRoleName maps to a RoleMapping | Apply that mapping; continue |
| 0 matches, AdminGroup set and user in AdminGroup | Admin + all folders; continue |
| 0 matches, AdminGroup set and user **not** in it | Strip admin + folders (existing revoke path), then **403** |
| 0 matches, AdminGroup blank | **403** — do not leave sticky perms and login |

Throw `InvalidOperationException` with a clear message (e.g. `No role mapping matched for user '…'`). Controller already maps that to 403.

**Auto-heal:** Next request with a matching group header applies RBAC and issues a session. No manual Jellyfin re-enable.

### User sync

- New users: create → random password → set `AuthenticationProviderId` with resilient retry (3 attempts on `DbUpdateConcurrencyException`, re-fetch each time).
- Existing users: keep forcing `AuthenticationProviderId` to RemoteAuth provider (password login blocked).
- `displayName` parameter: apply if Jellyfin user has a writable display-name field without inventing APIs; otherwise document as unused and stop reading dead email header noise in README (keep config fields for proxy compatibility; mark unused in docs).
- Re-enable via policy `IsDisabled=false` inside RBAC success path only (deny path never issues session).

### Base path HTML

Derive `basePath` from `Request.PathBase` server-side (preferred) and/or client pathname replace of `/sso/RemoteAuth/Login`.

- `ManualAddress = origin + basePath`
- Redirect = `basePath + '/'`

### Quick Connect

- Requires Jellyfin Quick Connect enabled.
- `GET QuickConnect`: identical secret + header validation as Login; sync + RBAC (deny → 403); store short-lived authorized session (username/roles already applied) in an in-memory `StateManager`; return QC HTML.
- `POST QuickConnect/Authorize`: body `{ Token, Code }`; peek session; `AuthorizeRequest(userId, code)`; invalidate token on success; allow retry on bad code.
- Adapt OIDC UI; strip providerId from routes (single RemoteAuth “provider”).

### Priority

Keep sorting by Priority for deterministic merge order. Document: under union merge, Priority does not exclude lower roles.

### Tests

- New project `tests/Jellyfin.Plugin.RemoteAuth.Tests` (xUnit), referenced from solution.
- Prefer pure helpers extracted for: constant-time secret compare, role match/deny resolution, merge mappings, basePath derivation.
- Mock `IUserManager` / `ILibraryManager` only where unavoidable for RbacService.
- `.mise.toml`: pin `dotnet` 9.x; tasks `test`, `build`.
- Makefile: `test` target → `dotnet test`.

### Release meta.json

Extend `update-manifest` job (or sibling step) so that for version `V`:

1. Prepend version entry to `manifest.json` (existing).
2. Update `build.yaml` version (existing).
3. Update `Jellyfin.Plugin.RemoteAuth/meta.json`:
   - Set top-level / versions[0] to `V` with changelog `Release ${TAG}`, `targetAbi` `10.11.0.0`, timestamp UTC.
   - Keep prior version history entries (prepend like manifest).
4. Commit `manifest.json`, `build.yaml`, and `meta.json` together.

Prefer direct commit to `main` (match current release workflow), not a PR, unless token permissions force a PR.

### Docs

- README: fix Default Role deny wording; document auto-heal; Priority truth; Quick Connect; base URL; unused email/displayName.
- Add `MIGRATION.md`: username match, permission overwrite, password provider takeover, disabled users re-enabled on successful mapped login.

## File map

| Path | Change |
|------|--------|
| `Services/RbacService.cs` | UpdatePolicyAsync, admin folders, deny throw |
| `Services/UserSyncService.cs` | Resilient update; call RBAC; surface deny |
| `Services/StateManager.cs` | New — QC session tokens |
| `Services/ServiceRegistrator.cs` | Register StateManager, IQuickConnect already in host |
| `Api/RemoteAuthController.cs` | Base path HTML; QC endpoints |
| `Auth/RemoteAuthProvider.cs` | Unchanged behavior |
| `tests/...` | New test project |
| `.mise.toml` | dotnet + tasks |
| `Makefile` | `test` |
| `.github/workflows/release.yml` | Sync meta.json |
| `README.md`, `MIGRATION.md` | Docs |
| `meta.json` | Kept in sync by release |

## Success criteria

1. Unit tests cover: merge union, Default Role fallback, deny when unmatched, AdminGroup grant, secret compare.
2. `mise run test` / `make test` passes.
3. Manual/proxy: unmatched → 403; add group → next Login succeeds with mapped libs.
4. Tag release updates `meta.json` in same commit as manifest.
5. Base URL deploy: redirect lands under prefix, credentials ManualAddress includes prefix.

## Out of scope follow-ups

- Rate limiting Login
- Optional “do not overwrite AuthenticationProviderId on existing users”
- Profile image header
