# Config file (backup and automation)

Settings live as XML on the Jellyfin server (not in this repository). The admin UI reads and writes that file.

| Install | Path |
|---------|------|
| Linux (package) | `/var/lib/jellyfin/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml` |
| Docker | `/config/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml` |
| macOS | `~/Library/Application Support/jellyfin/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml` |
| Windows (tray) | `%ProgramData%\Jellyfin\Server\plugins\configurations\Jellyfin.Plugin.RemoteAuth.xml` |

Created on first save in the dashboard. Until then, built-in defaults apply.

```bash
# Backup (Linux)
sudo cp /var/lib/jellyfin/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml{,.bak}

# Backup (Docker)
docker cp jellyfin:/config/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml ./remote-auth-config.xml.bak
```

Jellyfin keeps this file across plugin updates if you do not delete it. File edits do not hot-reload. Restart Jellyfin after copying config.

```bash
# Deploy pre-built config (Docker example)
install -d -m 755 /config/plugins/configurations
install -m 600 remote-auth-config.xml /config/plugins/configurations/Jellyfin.Plugin.RemoteAuth.xml
```

Example structure (adjust values; UI labels map to these XML names):

```xml
<?xml version="1.0" encoding="utf-8"?>
<PluginConfiguration>
  <Enabled>true</Enabled>
  <SecretHeaderName>X-Remote-Auth-Secret</SecretHeaderName>
  <SecretHeaderValue>your-shared-secret</SecretHeaderValue>
  <UserHeader>X-Remote-Auth-User</UserHeader>
  <EmailHeader>X-Remote-Auth-Email</EmailHeader>
  <DisplayNameHeader>X-Remote-Auth-Name</DisplayNameHeader>
  <GroupsHeader>X-Remote-Auth-Groups</GroupsHeader>
  <GroupsDelimiter>|</GroupsDelimiter>
  <AdminGroup>jellyfin-admins</AdminGroup>
  <AutoCreateUsers>true</AutoCreateUsers>
  <AllowPasswordLogin>true</AllowPasswordLogin>
  <DefaultRoleName></DefaultRoleName>
  <RoleMappings>
    <RoleMapping>
      <RoleName>jellyfin-users</RoleName>
      <IsAdmin>false</IsAdmin>
      <EnableAllLibraries>true</EnableAllLibraries>
      <LibraryIds />
      <LibraryNames />
      <EnableMediaPlayback>true</EnableMediaPlayback>
      <EnableRemoteAccess>true</EnableRemoteAccess>
      <EnableTranscoding>true</EnableTranscoding>
      <Priority>0</Priority>
    </RoleMapping>
  </RoleMappings>
</PluginConfiguration>
```

Library GUIDs for `LibraryIds`: **Admin → Plugins → Remote Auth**, or `GET /sso/RemoteAuth/Config/Libraries` as an admin.

`SecretHeaderValue` is plain text. Use `chmod 600` (or equivalent) and treat the XML as a secret.

UI field reference: [README Configuration](../README.md#configuration).
