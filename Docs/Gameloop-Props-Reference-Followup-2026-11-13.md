# Gameloop prop reference follow-up — 13 November 2026

## Subsequent surface grip repair

The contact defect described in the original reference audit below is now resolved for `Props_Computer_01` and `Props_Table_01` instances. ScenePropGripProvider caches paired end contacts on the readable visible mesh triangles. Solo carry placement uses the resulting midpoint; cooperative carry uses the fitted height so hand reach and lean remain stable. Read/Write was enabled through Unity importers for those two FBX assets. Other prop families retain their prior grip behavior.

Final job `c11949613a374369b112bbd2d67921f4` passed 3/3: full-level ownership/formation/input, the Adaptive demo regression, and destination pickup/visible contacts/body lean. The surface threshold was tightened from 0.06 m to 0.025 m, with measurements both at rest and after movement. Computer hand distances were about 0.000176–0.000360 m; table distances on both operators were about 0.000038–0.000171 m. The visible computer chest still leans backward about 24 degrees at the controlled 50 kg load; table load sharing, 16-degree stylized lean and release recovery pass.

The earlier failing job and larger distances below are historical evidence before this repair. Updated captures remain at Temp/Gameloop-Computer-Carry.png and Temp/Gameloop-Table-Carry.png. Tests use representative props in a clear Play Mode test area; no result here establishes exhaustive wall-collision behavior or a standalone build.

## Change

GameplayPlayerSpawner now binds the first successfully spawned player capsule to AdaptiveSceneProps instances in the same scene during Awake. The adapter accepts the reference only when it has no authored player reference. Binding occurs before adapter Start and before camera/wall-occlusion target assignment. No scene or prefab serialization changes are needed for a runtime-spawned player.

The existing full-level test now reads actions from PlayerInputComponent.actions, matching the current LocalPlayerInput API. Its >100-prop inventory and ownership/formation coverage remain intact. The menu transition test no longer expects the old adapter error and now requires the adapter to become ready.

## Play Mode results

| Check | Result |
| --- | --- |
| Full-level adapter readiness and inventory | Pass: 207 active adapted props recorded in Temp/AdaptiveSceneProps-inventory.csv. |
| Book ownership/release, computer source IK, cooperative table ownership and release, current input bindings | Pass in the existing full-level test. |
| Actual keyboard join from MainMenu and adapter readiness after scene transition | Pass with no old adapter-reference error. |
| Computer pickup animation event | Pass: actual Pickup_Grab attaches the adapted level computer. |
| Cooperative furniture movement | Pass: two players hold the table, horizontal team input moves it, releasing one releases the partner and restores dynamic physics. |
| Body lean | Pass in controlled poses: 50 kg computer gives about 24 degrees of visible backward chest rotation and raised head. A 60 kg table gives 30 kg per holder and 16 degrees stylized lean; visible chest rotation exceeds 10 degrees for both. Lean returns to neutral after release. |
| Visible hands tracking IK targets | Pass within 0.045 m at rest, and after cooperative movement. |
| Hands contacting the actual visible prop surface | **Fail**: computer hands are about 0.175/0.179 m from the nearest mesh triangle; the four table hand distances are about 0.204 m. |

The stronger surface check uses world-space visible hand positions and the prop's enabled MeshRenderer geometry. Nearest points are calculated on mesh triangles, not collider bounds or IK targets. It reveals a separate grip-placement defect in ScenePropGripProvider, which still selects points on a bounding-box end face rather than fitting each target to real geometry.

The reference fix is verified, but physical furniture hand contact must not be marked complete. The final strengthened test deliberately remains failing to capture this defect; no grip or animation geometry was changed as part of the reference repair.

## Evidence and limits

- Job `d2611d6df6bb46bf8dcc874f762f9647`: full-level test and menu transition test, 2/2 passed.
- Job `2f3b6bb019974f60b76ac23dddce6cc0`: pickup, target tracking, movement and lean passed before the stronger mesh-surface check was added.
- Job `291265ee15074ab1afaae8e00456f388`: strengthened test fails only at its collected actual-surface contact assertions; previous pickup, lean, movement and release assertions completed.
- Captures: Temp/Gameloop-Computer-Carry.png and Temp/Gameloop-Table-Carry.png, inspected after testing.

Representative props were positioned in an unobstructed test area during Play Mode to measure pickup/contact/lean independently of level obstacles. This does not verify carried-object wall collision, exhaustive prop types, gamepad hardware or a standalone build. Those remain separate work, as does the previously diagnosed lamp approach.
