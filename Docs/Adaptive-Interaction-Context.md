# Shared adaptive interaction context

Implemented in Gameloop_Demo on 13 November 2026. This layer gives the existing grip, lean and carry-pose components one view of the proposal's five parameters. Existing interaction controllers retain ownership of input, pickup/contact events, movement, collision, placement and release.

## Architecture and activation

`GameplayPlayerSpawner.enableAdaptiveInteractionContext` is enabled in Gameloop_Demo. After a successful player spawn, the spawner adds one `AdaptiveAnimationManager` to that player if absent. The flag defaults to false; the player prefab and AdaptiveAnimation_Demo do not require a manager.

The files under `Assets/Animations/AdaptiveAnimationSystem/Script` now provide:

- `InteractionContext`: an immutable per-player snapshot of parameter values, source references and interaction phase.
- `AdaptiveAnimationManager`: resolves those inputs from existing components, refreshes the snapshot and exposes Inspector diagnostics.
- `ProceduralLayerProcessor`: routes the snapshot to the existing grip provider and carry/hose pose selection.
- `AdaptationResult`: the selected provider, effective animation load, hose-pose flag and existing animation-state path.
- `Enums`: object type, interaction type and phase identifiers.

Since 15 November 2026, `Context`, `Result` and Update share one published context/result. Each read samples mutable inputs, but unchanged input values skip procedural processing and Inspector diagnostic writes. Component bindings are discovered once per source per frame, rather than repeated for every grip/lean/pose reader. `ContextRevision` counts publications and `BindingRefreshCount` counts binding discovery for verification; neither is a performance timing measurement.

Ownership, pickup completion, placement, role, live collider dimensions/scale, weight and existing component enable states remain visible on the same-frame read. IK coordinates are never cached here. Runtime code adding/replacing components or reparenting metadata must call `RefreshContext()` when it needs immediate same-frame discovery; otherwise binding discovery catches structural changes on the next frame. Disable/re-enable and source changes invalidate the bindings. The manager still uses execution order 160, and early consumers can safely read before its Update.

## Responsibility boundaries

| Component | Owns | Leaves to other components |
| --- | --- | --- |
| AdaptiveAnimationManager | Component bindings, live input sampling, changed-input detection, shared snapshot and diagnostics | Ownership, animation commands, pose application and physics |
| InteractionContext | Immutable parameter/phase snapshot, including pickup and provider-active flags | Querying live controllers or evaluating world hand coordinates |
| ProceduralLayerProcessor | Routing the supplied snapshot to existing grip, load and pose selections | Input/component discovery, Animator calls, bone/IK writes, movement and reservations |
| InteractionController / DoorInteractionController | Pickup/contact/release events, reservations and animation triggers | Procedural grip/lean calculation |
| CarryPoseLayer | Applying and blending existing carry/hose Animator layers | Pickup ownership and contact events |
| Grip and lean controllers | Live hand target fitting, calibrated load curves and rig application | Starting or completing gameplay interactions |

`AdaptationResult.AnimationStateHint` names the diagnostic animation route. It is not an Animator command; `BaseAnimationState` remains a compatibility alias. Processor decisions use the supplied pickup/provider flags, so routing a saved snapshot does not consult the actor's newer pickup state or provider enable state. This separates routing from execution without introducing a competing animation controller. The direct-reading path remains available to scenes with no enabled manager.

## Authoritative parameter sources

| Parameter | Source and interpretation |
| --- | --- |
| Object Type | Existing component/provider: hose grip/provider → Hose; VictimAI → Survivor; Box, Cooperative or SceneProp grip provider → the existing Box-style grip route. Other active sources report Unknown. |
| Object Size | Live root BoxCollider size times absolute scale, then adapted dimensions or supported collider geometry as fallback. Values use the shape's own axes rather than a yaw-dependent world AABB. |
| Object Weight | Enabled CarryAnimationLoad.WeightKg. ObjectWeightKg is the total animation load; EffectiveWeightKg uses CooperativeCarryObject.LoadPerPlayerKg once held, otherwise the total. HasWeight distinguishes absent/disabled data from a known zero load. Rigidbody.mass is not substituted or changed. |
| Role | InteractionController.CurrentCarryRole, derived from actual cooperative ownership or the hose station. Pending pickup has no assigned role; player number does not determine Front/Back. |
| Interaction Type | Active object reservation → ObjectInteraction; held/pending hose → FireHoseInteraction; recognized victim → CarrySurvivor classification; other held/pending items → CarryObject. Idle reports None. |

Source priority is active held object, pending pickup, then the active door/lamp target. For an adapted lamp, the original LampInteractable remains the interaction source/target while its parent AdaptiveSceneProp carry root supplies dimensions, load and provider metadata.

## Consumers and lifecycle

`HeldObjectGripController` reads the shared held source/provider. It still performs existing reach checks, blending, rotation calibration and provider queries. World grip coordinates are queried after attachment reach fitting, so the context cannot introduce stale hand targets.

`HeldObjectBodyLeanController` reads the shared pending/held source, effective load and role. Existing weight curves, exaggeration, anticipation, cooperative effort, hose pressure, aiming and recoil remain responsible for the final pose. `CarryPoseLayer` reads the held source and hose-pose flag while retaining its existing transition and layer-weight rules. Missing or disabled managers leave these consumers on their existing direct-reading path.

Preparing pickup exposes PendingObject but supplies no active holding grip before the actual grab event. Placement retains HeldObject and its grip until ownership releases; lean and pose-layer placement rules remain unchanged. The manager clears its snapshot when disabled or when no active source/controller remains. It does not cancel, reserve, grab, drop or move objects. AnimationStateHint records the existing route; animation controllers and clip events still initiate and complete actions.

This integration changes no physics settings, collision guards, calibrated curves, animation clips or input bindings.

## Coverage limits

Cylinder now has an explicit provider/classifier extension; see [Cylinder grip verification](Cylinder-Grip-Verification-2026-11-14.md) for its subsequent implementation and tests. Unknown objects keep their existing generic mechanics. Survivor detection classifies an existing VictimAI; the current result still uses generic CarryHold. It does not add a survivor-specific grip, base animation or synchronized rescue interaction. Box classification identifies the existing grip route, including adapted furniture, rather than asserting that every visual model is geometrically a box.

## Verification on 13 November 2026

Unity 2022.3.62f2, EditMode coroutine tests entering actual Editor Play Mode. Final focused results cover **23 distinct passing test methods across runs**, not a single 23/23 job.

| Group | Passing methods | Evidence |
| --- | ---: | --- |
| Shared context | 4 | Idle/pending/contact/release; live size/load updates; disabled load/provider/source; manager-disabled legacy fallback with the same 24-degree lean, full carry layer and visible contacts; paired 60-to-30 kg load sharing and reversed player roles; hose pressure pose; lamp parent metadata; object completion/cancellation. |
| Carry motion/collision | 3 | Slow gait, team yaw/orbit, solo wall stop, contact retention. |
| Destination migration | 9 | Menu join, independent keyboard players/dash, all seven doors, lamp both slots, hose pickup/contact/pressure/reel/extinguishing, computer/table visible surface contact and load pose. |
| Safe placement | 5 | Computer/table wall and floor clearance, unsupported placement rejection, late obstruction and retained cooperative pose recovery. |
| Source demo | 2 | Heavy pickup anticipation and three box sizes at 10/30/50/100 kg; legacy consumer path without a context manager. |

- Initial context-only job `1f7e8badbf5947c29caff924a98b2248`: 4/4 passed.
- Strengthened context plus regression job `5db225dc864a4dc583b79b0add3403c1`: 22 passed, one old source contact-fixture failure.
- Corrected source contact-matrix job `82133cd246944e59b7375cfc43c6d122`: 1/1 passed, including all twelve size/weight combinations and the original visible-contact/clearance assertions.

The old contact fixture privately claimed saved boxes 5-9 m from the actor. A separate source Play Mode diagnostic confirmed zero context managers: Medium remained about 6.70 m from CarryAnchor, grip weight was zero/out of reach, and its sweep toward the actor hit Big at about .00249 m. Collision protection correctly prevented the long teleport. The test now stages each box nearby on a temporary supported test floor; no product collision rule, IK threshold, curve, contact or clearance assertion was weakened. The source scene asset was not modified.

Representative destination metrics remained consistent: computer visible backward lean about 24 degrees, visible surface contact below .0004 m, solo wall overlap zero, team wall overlap about .000832 m, and cancelled table placement recovered its prior height. Computer and table captures were inspected; fire VFX partially occludes the table image, so precise table contact is established by the assertions rather than that capture. One hose test recorded Unity's existing partial-convex-hull warning for mesh Sphere; no context-related error was reported.

Only Gameloop_Demo's spawner opt-in line was added to scene serialization. Removing that line in memory reproduces its pre-integration hash `1CBE9EE7AAFB8D1721BB25F046C2814B527A2239A460473DF8DD1EBEE64D4606`; current hash is `6C7B16516E8C2A0F26275D602664514E22092A4847CA150CCA0E79A0C9BF8C07`. AdaptiveAnimation_Demo, MainMenu, Player.prefab and Controls.inputactions retain their prior hashes. The core files are later additions relative to main; this change fills their development-branch stubs and preserves the existing numerical consumer behavior. The manager-disabled comparison provides a direct enabled/legacy check without changing the physics mass.

These original integration results do not establish exhaustive parameter combinations, source destruction or unknown-type lifecycle tests, Survivor animation support, hardware gamepads, standalone builds or research respondent scores. Subsequent Cylinder results are recorded in the follow-up linked above.

## Context reuse verification — 15 November 2026

The context/routing refactor above passed **10 distinct focused methods across final runs**. All use Editor coroutine tests entering real Play Mode; this is not a full-project-suite or standalone-build result.

- Context job `fb5e713bfe1144888a092a078617070e`: 5/5 passed. The new reuse test performed 100 Context reads, 100 Result reads and 100 Update calls within a single frame without increasing ContextRevision or BindingRefreshCount. Input sampling still runs on these reads; procedural processing and diagnostic publication are reused. Load, shape size/scale, provider activation and pickup completion updated immediately. Explicit RefreshContext and next-frame binding discovery passed. Routing a saved pickup snapshot after the live actor completed pickup retained the original snapshot's route. Existing lifecycle/legacy fallback, shared load/roles, hose and object-interaction tests passed.
- `GameloopCarryMotionTests.HeavySoloAndTeamGaitMatchDisplacement`: passed through the Test Runner UI after connection recovery. Heavy solo actual speed was about .0508 m/s with rate .048; team turn actual speed about .663 m/s with rate .647.
- Regression job `9d6eabc95e1c4dffaef8723a04b58a5b`: 4/4 passed for saved Cylinder both-player contacts, visible hose contacts/clearance, destination furniture contacts/lean and blocked cooperative placement recovery. Computer/table visible surface distance stayed below .00036 m and computer backward lean about 24 degrees. Cancelled placement restored the previous carry height and reported zero blocker/floor overlap.

The earlier interrupted regression job `0e1a644032c74fa98911a2c265fd9ead` was unknown after the session changed and is not counted. The standard local MCP HTTP endpoint was used when the chat connector could not handshake; Test Runner UI supplied the heavy-motion result. Final Console inspection retained a plugin WebSocket warning and no errors; the hose test output also contains the previously observed partial-convex-hull warning for Sphere. Reuse counters establish eliminated repeated processing, not a measured frame-rate gain.

Task-start and final hashes match for Gameloop_Demo (`5FCDC2A2094E6801602728F08085299A87E377D690445CEF787E99CA6BF113C6`), AdaptiveAnimation_Demo (`033E02C533E3A76DB2689DF359C3C13517C10F0C2F0ED01110C6BCD6A67295FF`), Player.prefab (`66C0D68B9FCEFAF22130D8977E0D49A4ECC6B022BCE235807F707227A94A9A0C`) and Controls.inputactions (`C839A279ACEF264AA7AF616AF0AAE398AD51848E592D5DDDF13F47766458E63A`). These compare against the current development setup, which already includes prior grip/configuration work. The manager-disabled test provides behavioral parity for the supported legacy consumer path; no clean-main runtime comparison is claimed. Editor was restored to Gameloop_Demo outside Play Mode with no unsaved scene changes.
