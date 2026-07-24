# RBAC

Role Mappings map IdP group names to Jellyfin permissions. Matching on group/role name is **case-insensitive**. Multiple matches merge with **union** semantics (most permissive wins).

**Priority** only sorts merge order. It does not exclude lower-priority roles.

Manual library/permission edits in the Jellyfin user editor are overwritten on the next successful SSO. Put intended access in Role Mappings. See [migration.md](migration.md).

## Role merging

When a user matches multiple role mappings:

- Boolean permissions: `true` if any matched mapping enables it
- Libraries: union of matched library sets
- `EnableAllLibraries`: `true` if any mapping enables it
- `MaxParentalRating`: highest value among matches

## Default Role

If no group matches a Role Mapping, **Default Role** (General tab) is used when it names an existing mapping.

- Blank Default Role → unmatched users get **403** and no session
- Access auto-heals on the next login after the IdP group matches (or Default Role / Admin Group applies)

## Admin Group shortcut

If **Admin Group** is set and the user is in that group, they get `IsAdministrator = true` and all libraries even with no other mappings.

If Admin Group is set and the user is **not** in it, and nothing else matches (no Role Mapping / Default Role), login is **403**. On that deny path the plugin also revokes the Admin Group admin shortcut before returning 403. See [migration.md](migration.md).

## Base URL

Login and Quick Connect HTML honor Jellyfin's **Base URL** / path prefix (`Request.PathBase`):

- Session `ManualAddress` = `origin + basePath`
- Redirect after login = `basePath + '/'`

Subpath deploys (for example `https://example.com/jellyfin`) work without client-side path hacks. Branding button form action must include the same prefix; see [branding.md](branding.md).
