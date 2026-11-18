# Gameloop migration verification — 13 November 2026

Unity 2022.3.62f2, `Gameloop_Demo`, Editor Play Mode entered by EditMode coroutine tests. These results concern the saved destination scene and spawned `Player.prefab`. No gameplay scripts, scene configuration, prefab configuration or input bindings were changed in this audit.

Lamp follow-up: the obstructed anchor and limited reach are repaired and verified in [Gameloop-Lamp-Verification-2026-11-13.md](Gameloop-Lamp-Verification-2026-11-13.md). The lamp failure below records the original migration audit.

## Results

Eight distinct checks were exercised across the initial run and targeted follow-up runs. Final observed outcomes: **six passed, two failed**. This is a combined outcome, not a claim that one eight-test run passed. Earlier harness failures involving an expected startup log and the temporary fire target were corrected before recording these outcomes.

| Check | Outcome | Evidence |
| --- | --- | --- |
| Main menu → keyboard joining → Gameloop | Passed | Actual keyboard join events register WASD and arrow slots; StartGame loads Gameloop with the same saved assignments. |
| Spawn, appearance, independent walking, stop and dash | Passed | Two players have replacement visuals and persistent fitted helmets. Each moves about 0.61 m over the 0.65 s movement probe while its partner remains still; walking clears after release. Both dash triggers start and finish. |
| Seven doors, both sides, same-position closing | Passed | All seven doors complete opening and closing; opening from the opposite side reverses the swing; closing does not require moving to another side. |
| Hose contact, geometry, span and movement | Passed | Existing visible-hand, baked-body/nozzle and supported-span checks pass on both operators. Team movement displaces the front operator and retains visible hand contact; stopping clears walking. |
| Hose pickup via input and animation events | Passed | F and RightCtrl start pickup; actual animation events attach the nozzle and rear station; the team becomes ready to spray. |
| Hose pressure, reel, fire target and release | Passed | Pressure reaches about 0.7, water starts, rear reel increases length, an unobstructed temporary Fire target extinguishes, pressure release stops water, and dropping releases both operators. |
| Level props adaptation / ownership / cooperative carry | Failed / blocked | Existing `AdaptiveScenePropsTests.ScenePropsOwnershipFormationAndInput` fails at `IsReady`. The saved adapter has a null `playerReference`; level root and speed profile are present. Subsequent >100-prop inventory, ownership and cooperative furniture checks are blocked by this initialization failure. |
| Adaptive_Lamp switch interaction | Failed | After settling at its authored point, Player1 is at about (11.35, 0.39, -0.27), while the anchor is (11.35, 0.39, -0.21). Lamp is available, but ApproachBlocked is true and TryInteract selects no target. Nearby colliders include LampVisual, LampSwitch and level geometry. Further diagnosis must identify the obstructing collider and provide a physically reachable approach. |

## Findings and next work

1. Connect the dynamically spawned player collider to `AdaptiveSceneProps` before its initialization, then rerun the existing full-level props test. Do not replace its coverage with demo-only props. Solo pickup, cooperative furniture, hand contact and load pose remain unverified in the destination scene until this passes.
2. Fix the lamp's approach/anchor after identifying the blocking geometry. Do not bypass checks against unrelated walls.
3. Rerun the affected checks after those fixes; then address the already logged low-speed carry gait, formation-turn velocity and carried-object environmental collision issues.

The adapter emits **an error**, not merely a warning: `AdaptiveSceneProps requires a level root, player collider and speed profile.` The menu-transition test expects this known error so that it can independently verify device pairing; its passing result does not mean scene initialization is clean. Remove that expectation when the adapter is fixed.

The saved hose initially faces `Collider/GameObject`, a solid wall about 0.23 m ahead of the muzzle in the probe. A fire placed behind this wall correctly receives no water. The final pressure/extinguishing test turns the operators toward the open road before placing the temporary fire. No permanent Fire was added.

There are no `PushableDoor` components in the saved destination scene. Contact-driven doors demonstrated in AdaptiveAnimation_Demo therefore are not covered as a migrated Gameloop feature.

## Coverage limits

- Real keyboard Input System state was injected; no gamepad was connected during the menu probe. Hardware gamepad behavior and a standalone build are not validated.
- The fire target exists only in Play Mode. This checks the existing Fire.TryExtinguish path, not an entire fire-spawning mission.
- Door checks cover signed opening and same-position closing. Opposite-side closing routes, dash against all obstacles and exhaustive animation blending are not a full acceptance result here.
- Body lean while holding ordinary level props remains blocked by the adapter failure.
- No new measured result establishes that the previously logged heavy-carry gait or object-wall clipping defects are fixed.

## Test evidence

- `16ab78100c5d4d209384b0f56051c584`: existing full-level props test fails at adapter readiness.
- `5601d80145134a3483da7b53f0ec1bc7`: doors, visible hose contacts/clearance, menu and locomotion pass; lamp and initial fire probe fail.
- `c20161fb40824c9c92a1e16bdd8b8384`: actual grab/animation pickup passes; wall obstruction and lamp failure diagnosed.
- `2fd9218091ff4108a79390205cd7d216`: pressure/reel/extinguishing/release passes in a clear firing direction; lamp failure reproduces with ApproachBlocked=true.

Regression checks are in `Assets/Tests/Editor/GameloopMigrationTests.cs`, alongside the existing full-level props test. Scene, prefab and input asset hashes are compared against their pre-test working-copy contents, preserving the user's uncommitted migration work.
