# Launch preparation

Candidate UI: **0.8.6-rc.3** (numeric BepInEx version **0.8.6.3**).
Matching server candidate: **0.9.15-rc.1**, protocol **15**.
This is private preparation; no public release or server activation is implied.

The canonical pair uses full airframe plus selected stores pricing. The separate
protocol-14 local testing UI is incompatible and is not included here. UI source
matches the audited 0.8.6-rc.3 source archive; repository documentation is updated
for the newer compatible server. The server implementation is not included.

## Before public launch

- Confirm TAL MFD, F7 fallback, bindings, quotes/purchases and orders in game.
- Test three Shrikes/Eclipses through spawn, turns, descent and formation changes
  with the 0.9.15 server: custom flight restrictions now end after spawning.
- Resolve and verify the server RTB-abort handoff: native landing can abort into
  combat while TALON retains an RTB flag. This server issue remains open.
- Verify flight-wide interceptor accounting: one incoming missile must not draw
  unnecessary duplicate interceptors from separate wingmen.
- Verify the five F22 presets in game with the required aircraft/weapon content.
- Complete the separately approved server activation and client compatibility
  check before making this repository public or announcing availability.

Automated tests are not proof of in-game flight/landing behaviour.
Database impact: none. Configuration impact: none; existing bindings are preserved.
