# Install TALON Public UI 0.8.7

1. Install BepInEx 5 for Nuclear Option 0.34.5 and close the game.
2. Back up the old TALON Public UI DLL outside active plugin folders. Remove duplicate active copies; keep your configuration and bindings.
3. Extract the client archive into the game folder. The DLL belongs at `BepInEx/plugins/KellysTALONPublicUI/KellysTALONPublicUI.dll`.
4. Start the game and check for `Loading [Kelly's TALON Public UI 0.8.7.0]` in the BepInEx log. Open TAL on the maximized map or use F7.
5. Join a server running TALON protocol 15. Aircraft and weapon add-ons are separate. The UI cannot create wingmen alone.

Locally hosted missions also need the server component. Dedicated servers do not need this client UI. To roll back, close the game and restore the old UI DLL. Bindings remain in `BepInEx/config/kelly.nuclearoption.talon.publicui.cfg`.
