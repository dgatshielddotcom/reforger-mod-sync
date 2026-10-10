# Reforger Mod Sync

A simple Windows app to clear out mods you don't need for a server. Made for personal use, with JSOTF preselected; you can pick any server.

[Download ReforgerModSync.exe](https://raw.githubusercontent.com/dgatshielddotcom/reforger-mod-sync/refs/heads/main/downloads/ReforgerModSync.exe). Windows 10/11, .NET Framework 4.8. No installer.

## Use

1. Open it and use **Browse** to choose your `addons` folder.
2. Enter a server name and click **Find server**, or paste a ReforgerMods link/UUID.
3. Check the server and preview, then close Reforger and Workbench.
4. Click **Delete extra mods** and confirm.

## Options

- Deleting uses the Recycle Bin. **Delete permanently** frees space immediately and can't be undone.
- **Disable extras** keeps files in a separate folder. **Restore disabled** brings them back; disabling doesn't free space.
- **Import JSON** accepts a mod list or server config. Get the complete list, including dependencies, from the server admin; the app can't verify it. Confirm `[]` only for a known vanilla server.

Versions are ignored. Join the server in Reforger to get missing mods and the required versions. Unknown folders, including saves, stay put.

## Build / test

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1
```

[Server API](https://reforgermods.net/arma-reforger-mods-api/v2/) · [Config format](https://community.bistudio.com/wiki/Arma_Reforger:Server_Config)
