> Loader repair, 18 September: server release 0.9.15-rc.1 uses numeric BepInEx version 0.9.15.1; UI release 0.8.6-rc.3 uses 0.8.6.3. Earlier suffixed loader versions are rejected by BepInEx 5. Both DLLs must be replaced with the game closed. Configuration/database impact: none.

# Install TALON Public UI

1. Install BepInEx 5 for Nuclear Option 0.34.2, then exit the game.
2. Back up the old UI DLL outside active plugin folders. Keep existing configuration
   and bindings; remove duplicate UI DLLs.
3. Extract this archive into the game directory so the DLL is at
   `BepInEx/plugins/KellysTALONPublicUI/KellysTALONPublicUI.dll`.
4. Start and verify `Loading [Kelly's TALON Public UI 0.8.6.3]` in the BepInEx log.
5. Join a server with TALON **0.9.14-rc.1 / protocol 15**. Press TAL on the maximized
   map or F7 for standalone controls.

Airframe-plus-weapons price quotes require this matching pair. Protocol 14 and older are rejected.
Install matching aircraft/weapon add-ons separately; they are not bundled.
A local listen host needs the separate server component too. Dedicated servers do
not need this UI. The UI cannot create wingmen by itself.

Bindings remain in `BepInEx/config/kelly.nuclearoption.talon.publicui.cfg`.
No new keys or database changes. To roll back, exit and restore the previous UI DLL;
use a server with its matching protocol.

This RC is not approved for public release. F-22 presets and flight acceptance are
outstanding; read README.md before testing.
