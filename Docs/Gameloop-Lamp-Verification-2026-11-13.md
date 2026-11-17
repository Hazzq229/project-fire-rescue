# Gameloop lamp interaction — 13 November 2026

## Cause and repair

The saved lamp faced the wall. Its animation stand point was approximately `(11.354, .39, -.212)`, between the lamp and the wall collider `GameObject (5)`. The player's .32 m radius capsule did not fit in that gap. Physics displaced the character, and the subsequent approach cast correctly rejected movement back toward the obstructed anchor. Runtime casts hit both the wall and `LampVisual`; ignoring target colliders would not have solved the wall collision.

Rotated only the `Adaptive_Lamp` scene instance to face into the room. The stand point is now approximately `(11.354, .39, -1.172)`, facing +Z, with the switch on the accessible side. Added a lamp-specific .85 m interaction reach instead of the generic .45 m fallback. The existing capsule/ray obstruction checks and animation contact event remain in use. A carried lamp cannot accept a new switch reservation.

The scene file differs from its pre-fix hash only in the lamp's two quaternion overrides. Reconstructing those two original values in memory produced the exact previous SHA256 `0C220BDEF99005329FA137825FE6AE7BCB3C2966316C2510E8680FC69EA4F02B`. AdaptiveAnimation_Demo, MainMenu, Player.prefab and Controls.inputactions retain their pre-fix hashes.

## Play Mode verification

Unity 2022.3.62f2, using EditMode coroutine tests that enter actual Editor Play Mode and load the saved destination scene.

| Check | Result |
| --- | --- |
| Destination lamp contact toggles | Passed: Player 1 E and Player 2 Right Shift, off-axis approaches beyond .5 m, four total toggles, second toggle from the same settled position. Input reserves first; actual animation contact changes both IsOn and Light.enabled. Controls unlock after each action. |
| Capsule clearance | Passed: maximum measured player penetration into surrounding solid colliders was 0 m during alignment and switching. |
| Destination rejection cases | Passed: outside reach, a solid wall across the approach, and reservation while the lamp is held. Rejected input does not change light state or lock controls. |
| Existing source lamp regression | Passed: occupied hands, owner-only contact, actual clip event, duplicate contact, release and cancellation. |
| Destination doors | Passed: all seven doors open away from either side and close from the opener's position. |

Four distinct test methods passed across the final focused runs, not one single four-test job. Job `8459e2c92d2a409a88d5b1e358fb3537` passed the rejection and source lamp tests; its positive lamp probe initially failed because this EditMode runner requires `yield return null`, even after EnterPlayMode. The probe was corrected without weakening its assertions. Job `47baa64b1b684c82a890db7383d8cbd6` then passed both positive destination lamp and seven-door tests.

After reloading the saved destination scene, a separate Play Mode visual check switched the lamp off and back on from the same position. `IsOn` and `Light.enabled` agreed in both states, and the interaction lock cleared. Inspected captures: `Temp/Gameloop-Lamp-Off.png` and `Temp/Gameloop-Lamp-On.png`. The character partially occludes the lamp in these views; the captures confirm the room-side stance and lighting, not precise hand-to-switch contact. Console inspection after the focused runs and visual check found no errors or warnings. The Editor was left in Gameloop_Demo, outside Play Mode, with no unsaved scene changes.

The Editor initially retained a dirty flag after Play Mode. A diagnostic SaveScene copy in Temp was byte-identical to the saved destination scene (SHA256 `1CBE9EE7AAFB8D1721BB25F046C2814B527A2239A460473DF8DD1EBEE64D4606`). Reopening was guarded by a fresh copy-and-byte comparison, preserving the scene if any difference appeared; the reopened scene reported dirty=False.

Compared against `main`: the lamp and shared object dispatcher are later branch additions and have no equivalent implementation there. The direct before/after behavior on the current development setup is an unreachable wall-facing anchor/.45 m generic reach versus the saved room-facing anchor/.85 m lamp reach, with the same obstruction and contact-event rules. No clean-main runtime parity or standalone build is claimed.
