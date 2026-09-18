> F-22 update, 18 September: five screenshot presets added (Air to Air, Air to Air Stealth, Air to Ground AGM, Air to Ground PGM, Mixed). Requires installed Aryx F-22E 0.0.6 and Weapons Pack 1.1.0.1. All 11 station groups audited; armed presets retain the cannon, heater missiles and tailhook. Database/configuration impact: none.

> Loader repair, 18 September: server release 0.9.15-rc.1 uses numeric BepInEx version 0.9.15.1; UI release 0.8.6-rc.3 uses 0.8.6.3. Earlier suffixed loader versions are rejected by BepInEx 5. Both DLLs must be replaced with the game closed. Configuration/database impact: none.

# Kelly's TALON Public UI

**Tactical Airborne Leadership & Operations Network**

Public UI **0.8.6-rc.3** pairs with server **0.9.15-rc.1**, protocol **15**.
This is a release preparation candidate, not the approved public release.

The vanilla-styled TAL MFD panel appears beside the maximized map. F7 opens
standalone controls, including when no spare MFD button exists. Donate / Vehicles
also links to purchases. No other wingman mod is required.

REINFORCEMENTS selects aircraft, mission loadout and Workshop livery, displays
allocation/cost and orders/cancels purchases. Order before deployment or while flying.
Three active and queued wingmen share the limit. Prices now show **full airframe
value plus selected weapons/equipment**, quoted by the server. The airframe component
is four times the former testing price. Paired pylons, rack costs and ammunition are
included. Wait for the total after selecting a preset; quotes never debit allocation.
Protocol 14 and older are incompatible; update both server and client.

ORDERS shows individual callsigns/status and ALL/individual command scope:

- Form Up, Hold Here and Return to Base.
- Distributed attack: one munition per selected target across the scoped flight.
- Flight Salvo: one munition per aircraft per target.
- Alpha Strike: remaining compatible munitions across selected targets, excluding
  cannons, energy weapons, cargo and nukes. Native launch constraints still apply.
- Whole-flight formation cycling and directed JAM for Medusas with fitted pods.

Keyboard/HOTAS bindings include JAM, Alpha Strike and Toggle Attack Mode. KEY/BUTTON
capture and separate clearing are available. Escape cancels; capture times out
after 30 seconds. Saving a binding does not fire its command. Existing bindings in
`kelly.nuclearoption.talon.publicui.cfg` are preserved.

Current roster: 13 aircraft / 38 presets. Cricket, Vagrant, Brawler, Revoker, Shrike,
King Viper, Eclipse, Medusa, Cavalier, Compass, Ifrit, Vortex and F-22E. Install matching
aircraft/weapon packs separately. MiG-15, Knockout,
Chicane and empty local-test presets are absent.

Successful native RTB recovery/despawn refunds the original purchase payment once.
Crashes/arbitrary despawns do not. Receipts are mission-local and expire on reset
or plugin unload; confirmed refunds can wait for same-faction reconnect beforehand.

Release blockers remain: Shrike/Eclipse formation upset reports and incomplete
flight-wide defensive interceptor accounting. Automated checks are not flight acceptance.
For feedback include aircraft/preset, command/scope, time and both component versions.

Install only on clients; local hosts also need the separate server component.
See [installation](INSTALL.md). Database/configuration impact: none; no new keys.
The original public UI uses its own MIT license. Its archive includes no server AI,
game/content assemblies, configurations or credentials.

## Build and test

The client is independent of the server source. Install the .NET 8 SDK and use a
local Nuclear Option installation with BepInEx 5. Game libraries remain local and
are not redistributed in this repository.

```powershell
dotnet run --project tests/KellysTALONPublicUI.LogicTests.csproj -c Release
dotnet run --project tests/Bindings/Bindings.Tests.csproj -c Release
dotnet build KellysTALONPublicUI.csproj -c Release -p:GameDir="C:/path/to/Nuclear Option" -p:DebugType=None -p:DebugSymbols=false
```

See [launch preparation](LAUNCH-PREPARATION.md) for candidate compatibility and
remaining acceptance checks. This repository is private during preparation.
