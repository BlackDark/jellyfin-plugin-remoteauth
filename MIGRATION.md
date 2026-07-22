# Migration Notes — Remote Auth

Behavior when enabling Remote Auth for existing Jellyfin users, or upgrading after stability fixes.

## Username match

SSO username comes from the configured **Username Header** (default `X-Remote-Auth-User`). It must match the Jellyfin username **exactly** (case-sensitive Jellyfin lookup).

- Existing local/LDAP users with the same username are **taken over** by Remote Auth on first successful SSO login.
- No rename/alias mapping — change the IdP username or Jellyfin username to align.

## Permission overwrite

Every successful login re-applies RBAC from matched role mappings (and Default Role / Admin Group when used).

- Manual permission/library edits in Jellyfin are overwritten on next SSO login.
- Put intended access in **Role Mappings**, not only in the Jellyfin user editor.

## AuthenticationProviderId takeover

On sync, the plugin sets `AuthenticationProviderId` to the Remote Auth provider.

- Password login for that user is **disabled** (`RemoteAuthProvider` rejects `AuthenticateByName`).
- Web SSO: `/sso/RemoteAuth/Login` (proxy headers).
- Native/TV apps: Quick Connect via `/sso/RemoteAuth/QuickConnect` (Jellyfin Quick Connect must be enabled).

To restore password login for a user, clear/change their auth provider in Jellyfin (or remove Remote Auth) — this plugin always re-forces the provider on successful sync.

## Disabled users

On a **successful** mapped login, RBAC sets `IsDisabled = false`.

- Admins who disabled a user in the dashboard will see them re-enabled after the next successful SSO with a matching role.
- Denied logins (no mapping / no Default Role / Admin Group miss) never issue a session and do not re-enable.

## Deny until matched

If the user has **no** matching role mapping, **no** usable Default Role, and does not get access via Admin Group:

- Login returns **403** — no session.
- If **Admin Group** is configured and the user is not in it, admin/folder access from that shortcut is revoked before the 403.
- If Admin Group is blank, existing Jellyfin permissions are left unchanged in the DB (still no session until a mapping matches).
- Access **auto-heals** on the next login after the IdP grants a group that matches a Role Mapping (or Default Role / Admin Group applies).

Leave **Default Role** blank for strict deny. Set a Default Role only when unmatched users should still get a fallback mapping.
