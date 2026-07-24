# Developing

## Releases

Automated with [release-please](https://github.com/googleapis/release-please) from [Conventional Commits](https://www.conventionalcommits.org/) on `main`.

| Commit prefix | Version bump |
|---|---|
| `fix:` | patch |
| `feat:` | minor |
| `feat!:` / `fix!:` / `BREAKING CHANGE:` | major |

Merge the Release PR → tag + GitHub release → `release.yml` uploads the zip and updates `manifest.json`, `build.yaml`, and `meta.json`.

Git tags: `v1.0.2`. Jellyfin plugin versions: `1.0.2.0` (workflow pads).

```bash
make validate-manifest   # verify manifest URLs/checksums before shipping
```

## Project structure

```
Jellyfin.Plugin.RemoteAuth/
  RemoteAuthPlugin.cs
  Configuration/          # PluginConfiguration, admin UI
  Api/                    # Login, Quick Connect, config API
  Auth/                   # RemoteAuthProvider (password block)
  Services/               # RBAC, user sync, Quick Connect state, DI
```
