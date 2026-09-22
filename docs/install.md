# Other install paths

Catalog install (recommended) lives in the [README](../README.md#installation).

## From GitHub release

1. Download the latest `.zip` from [Releases](https://github.com/BlackDark/jellyfin-plugin-remoteauth/releases)
2. Extract into the Jellyfin plugins directory:

   ```bash
   # Linux
   unzip remote-auth_*.zip -d /var/lib/jellyfin/plugins/RemoteAuth/

   # Docker
   unzip remote-auth_*.zip -d /config/plugins/RemoteAuth/
   ```

3. Restart Jellyfin

## From source

Requires .NET 10 SDK **or** Docker (Jellyfin 12):

```bash
make build          # or: make docker-build
sudo cp dist/*.dll dist/meta.json /var/lib/jellyfin/plugins/RemoteAuth/
sudo systemctl restart jellyfin
```

`make package` builds an installable zip.
