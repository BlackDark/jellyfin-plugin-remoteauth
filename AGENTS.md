# jellyfin-plugin-remoteauth

Jellyfin plugin for trusted-header SSO with role-based library access control.

## Verify

- `mise run check`: restore, build, and test with the pinned .NET 11 SDK; tests roll net10 forward to its runtime.
- `bash scripts/validate-manifest.sh` after changing plugin metadata.
- CI also builds the JPRM artifact and runs actionlint, zizmor, and strict Renovate validation.

The required gate is `check`. The existing tag-driven package and manifest update process remains specialized.

## Stack

Follows https://github.com/BlackDark/agent-presets/blob/main/stack/core.md where applicable. Keep the Jellyfin plugin SDK and JPRM packaging conventions.

## Map

- `Jellyfin.Plugin.RemoteAuth/`: plugin, authentication, and library-access rules.
- `scripts/validate-manifest.sh`: manifest source URL validation.
- `build.yaml`, `manifest.json`, `meta.json`: package metadata updated by the release workflow.

## Git

- Conventional Commits, one topic per PR.
- Squash unless commits need separate review.
- Force-push only with `--force-with-lease` on own branches.

## Boundaries

- Do not publish plugin packages, create release tags, or update the public manifest from a local session.
- Do not weaken trusted-header validation to silence test failures.
