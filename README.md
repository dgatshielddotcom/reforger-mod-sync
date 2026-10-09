# Reforger Mod Sync

A small Windows app that removes local Arma Reforger addons absent from a selected server's mod list. Search public servers through ReforgerMods, paste a server link or UUID, or import a complete JSON mod list for a private server.

Membership is compared by mod GUID. **Versions are ignored**, so the game can choose the versions required by the server, including downgrades.

## Download and use

Download [ReforgerModSync.exe](downloads/ReforgerModSync.exe?raw=true). The app runs on Windows 10/11 with .NET Framework 4.8. No installer, API key, or administrator access is required. 

1. Open the app and select the correct local `addons` folder. The starting path is your Windows Documents folder followed by `My Games\ArmaReforger\addons`; use **Browse** if your game uses another location.
2. Enter a server name and click **Find server**. Select the correct result, then verify its name/address and the preview.
3. Close Arma Reforger, its server, and Workbench.
4. Click **Delete extra mods** and review the final confirmation.

Deletion sends extra addon folders to the Windows Recycle Bin by default. **Delete permanently** removes them without recovery through this app. Emptying the Recycle Bin is also permanent. Nothing is removed automatically.

The original JSOTF Public Operation Server 1 is preselected for convenience (this was made for personal use) you can choose another server at any time. The live list is fetched again before each cleanup.

Supported direct inputs:

```text
https://reforgermods.net/servers/<server-UUID>
https://api.reforgermods.net/v2/servers/<server-UUID>
<server-UUID>
```

Search returns up to 100 results and can match scenario names. Use a specific server name and select the result yourself. IP-address search and arbitrary BattleMetrics URLs are unavailable; search by server name instead. The original BattleMetrics server link is recognized as a shortcut.

## Private servers and manual lists

Click **Import mod list...** and select a JSON export or server configuration containing the complete required list, including dependencies:

```json
[
  {"modId": "65AD7D0D9941A380", "name": "ACE Core Dev"},
  {"modId": "595F2BF2F44836FB", "name": "RHS - Status Quo"}
]
```

Supported formats include arrays of 16-character IDs, arrays of objects with `modId` or `id`, `{"mods":[...]}`, server configs with `game.mods`, and API exports with `server.mods`. Names are optional; versions have no effect.

For a known vanilla server, import `[]` and explicitly confirm that it is the complete empty list. All recognized local addons then count as extras. Automatic zero-mod API responses are rejected because they do not prove a server is vanilla.

Imported files are reread before cleanup. Obtain them from the server administrator or a reliable complete export: the app cannot verify that a manual list matches a live server. Imported files and local addons are not uploaded.

## Disable, restore, and protected folders

**Disable extras** moves extra folders into the sibling `addons-disabled-by-ReforgerSync` folder without deleting files or reclaiming space. It isolates folders instead of changing the game's native enabled flag. Required addons already isolated by this app are restored during cleanup.

**Restore all disabled** works offline while the game is closed and skips collisions. It restores isolated folders only; recover recycled folders through Windows Recycle Bin. Permanent deletion cannot be undone.

Unknown folders, including `addons\saves`, conflicting IDs, and folders containing links/junctions are left unchanged. The app rejects changed previews and never overwrites an existing destination. Operations are journaled in the sibling folder so interrupted runs can be inspected; successful permanent deletions cannot be rolled back.

Missing addons are displayed for reference. Join the server through Arma Reforger to download required addons; the app does not download, update, or downgrade them. Only the selected `addons` directory is managed; mods loaded through other `-addonsDir` paths are outside its scope.

## Build

From a checkout on Windows, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\src\Build.ps1
```

The script uses the installed Windows .NET Framework C# compiler and creates `ReforgerModSync.exe` in the repository root. No package restore or external SDK is required. Source is C# 5 with Windows Forms and .NET Framework libraries.

## Test

Close Arma Reforger before running engine tests. They create random temporary fixture directories and never access a real addon directory.

```powershell
# Offline tests: filesystem safeguards, manual import, and public API fixtures.
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1

# Optional live provider requests and Windows Recycle Bin integration.
powershell -NoProfile -ExecutionPolicy Bypass -File .\tests\Run-Tests.ps1 -Live -Recycle
```

The optional recycle test sends one small generated fixture addon to your Recycle Bin. Test executables stay in ignored `work\test-binaries`. GitHub Actions builds the app, runs offline tests, and uploads a downloadable build artifact; it does not publish releases.

## References

- [ReforgerMods API](https://reforgermods.net/arma-reforger-mods-api/v2/)
- [Arma Reforger server configuration](https://community.bistudio.com/wiki/Arma_Reforger:Server_Config)
- [Windows file-operation flags](https://learn.microsoft.com/en-us/windows/win32/api/shobjidl_core/nf-shobjidl_core-ifileoperation-setoperationflags)
