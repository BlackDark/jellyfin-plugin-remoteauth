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

## AuthenticationProviderId / password login

Config: **Allow password login** (`AllowPasswordLogin`, default **on** = hybrid).

| Mode | Behavior |
|------|----------|
| **On (hybrid)** | Do not force Remote Auth provider. Users stuck on `RemoteAuthProvider` are migrated back to Default on next successful SSO. Infuse/`AuthenticateByName` works if the Jellyfin password is known. |
| **Off** | After successful RBAC, force `RemoteAuthProvider` — password login disabled. Use Quick Connect for native apps that support it. |

- Web SSO always uses `AuthenticateDirect` (no password check) either way.
- **New auto-created users** get a random unknown password — set one in Jellyfin (Dashboard → Users) before Infuse can log in.
- Users who already had a Jellyfin password before Remote Auth takeover usually keep that hash after migration back to Default.
- Hybrid password login **bypasses IdP MFA** — accepted tradeoff when the setting is on.

Native/TV apps with Quick Connect: `/sso/RemoteAuth/QuickConnect` (Jellyfin Quick Connect must be enabled). Infuse does **not** support Quick Connect — use hybrid password instead.

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
