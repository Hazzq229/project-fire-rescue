# Project Lessons & Self-Improvement Log

### [2026-11-14] Free Walking Cadence and Rigid Head Geometry
- **Feedback**: Free walking felt too slow, its loop felt discontinuous, and the head stretched during movement. Repair only these presentation/movement issues and preserve the other mechanics.
- **Lesson**: Increase unladen movement through a separate multiplier rather than changing BaseMoveSpeed, which existing carry/load calculations consume. Let playback follow the resulting measured displacement while retaining the loaded playback limits, input, dash and load curves.
- **Lesson**: Test the active controller's Running clip, including endpoint velocity continuity. Equal first/last poses alone do not prevent a visible hitch. Fit endpoint tangents while preserving poses, timing and interior keys; native Unity serialization can produce a large textual diff without a correspondingly large behavioral change.
- **Lesson**: Mixed shoulder/neck/head skin weights plus a nonuniformly scaled armature can change skull shape when bones rotate. Measure world geometry after removing only rigid pose; inverse-transforming through the same scale can hide the defect. Stabilize the whole face, ears and helmet together using persistent meshes and a uniform head follower after the animator mirror; leave body bones and IK intact.
- **Lesson**: Verify BakeMesh scale behavior on the actual imported rig. Here BakeMesh(useScale: true) plus renderer transforms matches rendered geometry. SkinnedMeshRenderer culling bounds must be fitted in the replacement root bone's space and checked against actual baked vertices. Outline clones should retain the same geometry and bone binding.
- **Result**: Twelve distinct methods passed across final focused runs: four walking/head checks and eight regressions. Both players walked about 1.50 m/s, stopped cleanly and restarted; the 30 kg computer retained its existing speed. Maximum sampled head/face drift was below 0.000009 m during walking and turning. Carry contacts, body lean, hose, Cylinder and dash regressions passed; Console had no errors/warnings. Details: `Docs/Walking-Head-Verification-2026-11-14.md`.
- **Rule**: Keep head meshes as saved assets without runtime vertex reads. If model replacement is reapplied or the source model changes, regenerate and rebind this saved geometry. Stage recovery tests clear of a released solid object; do not interpret an actual collision as a speed defect. Preserve scene hashes and distinguish focused Play Mode evidence from a full suite or player-build claim.

### [2026-11-14] Curved Cylinder Grip and Preserved Tool Carry
- **Feedback**: Add Cylinder grip in Gameloop_Demo, connect it to the five-parameter context, and preserve existing mechanics.
- **Lesson**: Opt the existing APAR into a dedicated provider. Objects named Cylinder on the hose are connector visuals, not new carry targets. Keep the extinguisher's existing Liftable, Rigidbody, SpringJoint and tool component; do not run it through an adapter that replaces them.
- **Lesson**: Fit paired contacts to the actual readable mesh facets on an explicitly configured body axis. Imported APAR length is local Z. Select the near side at torso height, cap the reachable hand span, and transform surface normals by the inverse transpose under nonuniform scale.
- **Lesson**: Use a separate cylinder-side rotation profile and retain the calibrated box-side profile. Both normal IK queries and calibration recording must use holder-specific frames when grip orientation depends on the actor.
- **Lesson**: Degenerate triangles can return NaN from closest-point formulas and reset a minimum-distance accumulator. Handle their edges before barycentric division while retaining strict contact tolerances. Avoid captured iterator locals across EnterPlayMode: domain reload can lose the compiler's closure and fail the first post-reload assignment; an explicit loop preserves the assertion safely.
- **Result**: After reconnecting MCP, 12 distinct methods passed across final runs: three Cylinder checks and nine related regressions. Both player slots passed six paired size/load configurations and movement; geometry also passed yaw, tilt and nonuniform scale. Spring settings, mass, ownership and placement cleanup remained intact. Details: `Docs/Cylinder-Grip-Verification-2026-11-14.md`.
- **Rule**: Describe tested configurations precisely. These paired size/load cases are not a full Cartesian matrix, and preserved tool references do not prove firing with unassigned VFX/origin settings.

### [2026-11-13] Shared Five-Parameter Interaction Context
- **Feedback**: Integrate object type, size, weight, role and interaction type in Gameloop_Demo using the existing components and preserve mechanics that already work.
- **Lesson**: Publish one fresh per-player context and route its held source, provider, effective load and pose category to existing consumers. Keep calibrated curves, IK reach fitting, animation events, ownership, input and physics in their existing components; retain the direct-reading path when the manager is absent or disabled.
- **Lesson**: Pending pickup is for anticipation and must not activate holding IK. Placement retains the held grip until contact/release, while its existing clip controls lean. Read cooperative roles from ownership and split load only after the actor holds the object; preserve hose pressure, aim and recoil overrides.
- **Lesson**: Read dimensions in object axes from live collider size/scale. Adapted lamps keep their interaction component on a visual child but their load/size/provider metadata on the normalized parent. Snapshot provider handles, then query grip coordinates after reach fitting to avoid stale contacts.
- **Lesson**: Metadata classification does not implement a new animation. Survivor classification still uses existing generic carry; Cylinder support and a dedicated rescue pose remain separate work.
- **Result**: Four new context checks and nineteen related regressions passed as 23 distinct methods across final focused runs. The source 3-size x 4-weight matrix required nearby supported fixtures: the old private pickup was blocked by another box while the held source remained 6.7 m from the hand anchor. Contact/clearance assertions and collision rules were retained. Details: `Docs/Adaptive-Interaction-Context.md`.
- **Rule**: Enable migration through an explicit scene spawner opt-in, and verify manager-disabled parity plus scene/prefab/input hashes before declaring the integration behavior-preserving.

### [2026-11-13] Reachable Lamp Switching in Gameloop
- **Feedback**: Repair the obstructed lamp interaction in Gameloop_Demo so players can switch it on and off easily, then test in Play Mode.
- **Lesson**: Inspect the settled capsule, actual cast hits and authored orientation together. The wall-facing lamp anchor left less room than the player's capsule diameter; ignoring lamp self-colliders would still force the character into a real wall. Face the scene instance into clear space instead.
- **Lesson**: Give the lamp its own forgiving reach while preserving capsule/ray obstruction checks, owner reservations and animation-contact toggling. A lamp adapted into a carry prop must reject new switch reservations while held.
- **Result**: Both keyboard players completed four contact-driven toggles from off-axis approaches and could switch back from the same position. Maximum sampled player penetration was zero. Blocked/out-of-range/held rejection, source lamp ownership/cancellation and all seven destination doors passed across four distinct focused test methods. Details: `Docs/Gameloop-Lamp-Verification-2026-11-13.md`.
- **Rule**: These EditMode coroutine tests enter Play Mode but still require `yield return null` for frame sampling; do not use WaitForFixedUpdate in this runner.

### [2026-11-13] Collision-Safe Ground Placement
- **Feedback**: Check and repair object placement in Gameloop_Demo so lowering/release cannot force objects through walls or floors.
- **Lesson**: Validate the entire solid compound shape and lowering path, not only a downward center ray. Use enabled colliders attached to the item's Rigidbody, sample footprint support, preserve the placement rotation, and choose one shared target before starting either operator's animation.
- **Lesson**: Only the first holder may interpolate and commit a guarded placement pose. Attachment Update must not overwrite that pose, and the final animation contact must repeat the guard rather than teleport to the target.
- **Lesson**: Refuse unsupported placement or retain ownership if a blocker appears during lowering. Cancellation must restore kinematic/joint settings and both players' movement; cooperative carry must re-align and recover its original hand height after the last operator exits the animation.
- **Lesson**: An object already at floor height may have less than 2 cm available to lower. Test actual floor penetration, ownership until contact and final resting pose rather than forcing an arbitrary minimum downward travel.
- **Result**: Five destination placement checks and five related regression checks passed across the final focused runs. Computer/table wall overlap was zero in the near-wall probes; maximum computer floor settling overlap was about 0.000386 m. No-support and late-blocker cases retained the object, and cancelled table placement recovered both operators' carry pose and contacts. Details: `Docs/Gameloop-Placement-Verification-2026-11-13.md`.

### [2026-11-13] Heavy Carry Gait and Whole-Formation Collision
- **Feedback**: Recheck slow heavy-carry gait and carried-object environmental collision in Gameloop_Demo; fix remaining issues and verify in Play Mode.
- **Lesson**: Read completed per-player Rigidbody displacement for cooperative gait, including idle turns and anchor/orbit motion. A 0.08 m/s walking threshold and 0.12 playback floor hide or overplay a legitimate 0.048 m/s solo load. Use low-motion hysteresis and proportional cadence, and share backward/strafe classification with the visible model.
- **Lesson**: Limit formation turning by each operator's loaded linear speed. Large objects otherwise make operators orbit much faster than their walking animation even when reported velocities are correct.
- **Lesson**: Guard the actual solid compound shapes of object and holders with one allowed motion fraction, including translation, rotation, leveling and reach corrections. Sample swept poses and exclude the guarded bodies; checking only the player capsule or only translation leaves wall clipping paths.
- **Lesson**: Parented solo objects follow a pivot arc during yaw, and supported-edge pickup follows an arc above the floor. Testing a straight chord can either miss an actual sharp-turn collision or incorrectly block a valid lift. Keep a safe translation-only retreat when yaw is obstructed.
- **Lesson**: Keep holders turning toward the carry stance while idle. Replacing movement branches with guarded movement must not omit this orientation update.
- **Result**: Final focused run passed 8/8. Heavy solo motion was about 0.05 m/s with playback about 0.048; team turn motion about 0.66 m/s with playback about 0.647. Solo wall approach had zero measured overlap; team wall translation/turn overlap stayed below 0.001 m. Hand contact, load lean, hose cadence, normal movement and dash regressions passed.
- **Rule**: A test must not expect an object to teleport through walls from across the level. Stage ownership/contact fixtures within reach while retaining full-level inventory coverage. Ground-placement path collision remains separate from these carrying-motion checks.

### [2026-11-13] Computer and Table Surface Grip Repair
- **Feedback**: Fit computer and table grips to actual object surfaces in Gameloop_Demo, then verify visible hands and body lean in Play Mode.
- **Lesson**: Cache paired contacts on the original mesh triangles, searching near the intended hand height and rejecting deep interior end-face hits. Convex collision hulls and bounding boxes can create contacts in empty space beside a keyboard or between table legs.
- **Lesson**: Reposition solo carry from the fitted grip midpoint and use the fitted grip height for cooperative carry. Moving targets alone can make them unreachable or change load pose; preserve reachable hand height while moving the object.
- **Result**: Props_Computer_01 and Props_Table_01 instances now use fitted surface grips. Unity importer Read/Write was enabled for these two meshes. Three focused Play Mode checks passed, including the full-level inventory and a stricter 0.025 m surface tolerance; sampled visible-hand distances were below 0.0004 m at rest and after movement. Heavy computer lean remained about 24 degrees, and table lean/load sharing and release recovery passed.
- **Rule**: Scope surface fitting to prepared mesh families; do not silently disable grip providers on other unreadable props. Keep actual-surface tests alongside IK target tracking tests.

### [2026-11-13] Spawned Prop References and Actual Surface Contact
- **Feedback**: Fix the Gameloop adapter reference, then retest pickup, furniture carry, visible hand contact and body lean in Play Mode.
- **Lesson**: Bind a successfully spawned player's capsule to same-scene adapters in the spawner's Awake, before adapter Start. Preserve authored references for scenes with static players; do not serialize a runtime instance back into the scene.
- **Result**: Full-level inventory (207 active props), ownership, cooperative furniture and menu-to-gameplay readiness pass. The spawned model shows about 24 degrees of backward chest lean at the controlled 50 kg computer load; the 60 kg table splits the load between operators and returns lean to neutral after release.
- **Findings (open)**: Visible hands can be within IK target tolerance while floating away from the prop mesh. Triangle-surface measurements found about 0.175-0.179 m for computer hands and 0.204 m for table hands. ScenePropGripProvider's bounding-box end targets must be fitted to real surfaces; do not claim physical contact from target tracking alone.

### [2026-11-13] Gameloop Migration Verification
- **Context**: Tested the migrated destination scene in Play Mode, including keyboard joining from the menu. Six distinct checks passed; level-prop adaptation and lamp interaction failed. Details are in `Docs/Gameloop-Migration-Verification-2026-11-13.md`.
- **Lesson**: Dynamic player spawning invalidates serialized references to old scene players. The level adapter still has its root and speed profile, but its null player collider prevents initialization. This emits an error, not merely a warning; verify readiness and full-level prop coverage before claiming carry migration complete.
- **Lesson**: A lamp anchor can be physically displaced by nearby collisions while approach sweeps reject the resulting short movement. Verify the settled player position and actual blocked path, not only valid lamp references or an exact teleport to the point.
- **Lesson**: Test extinguishing with an unobstructed trajectory. The saved hose faces a level wall; a target behind it remaining lit is correct obstruction behavior, not evidence of broken water logic.
- **Lesson**: Keep menu-to-gameplay pairing tests separate from direct-scene Editor fallback. Keyboard passes do not establish hardware gamepad or standalone-build correctness.

### [2026-11-12] Persistent Helmet Mesh on Spawned Players
- **Feedback**: The character's helmet disappeared after migration to `Gameloop_Demo`.
- **Lesson**: A prefab must not serialize a null helmet mesh while relying on a temporary `HideAndDontSave` fitted mesh. Bake the fitted mesh as a persistent Unity asset and assign it to the spawned player prefab; test both spawned players after entering Play Mode and verify visible geometry as well as the mesh reference.
- **Lesson**: The original FBX mesh is not marked readable for runtime builds. Editor access to its vertices alone does not prove that runtime mesh fitting will work in a player build.

### [2026-11-12] Gameloop_Demo Migration & Spawned Prefab Integration
- **Context**: Ported verified mechanics (character replacement model Rubah_P1, Animator mirror, hose carry parameters, body lean, and input dash) into Gameloop_Demo and Assets/Prefabs/Player.prefab without affecting camera systems or existing controls.
- **Rules**:
  - Gameloop_Demo spawns players dynamically via GameplayPlayerSpawner from Assets/Prefabs/Player.prefab. To migrate visual and control enhancements, apply the model replacement and calibrated controller parameters directly to Player.prefab so instantiated players inherit CharacterAppearance, Rubah_P1, CharacterModelAnimatorMirror, and CharacterHeadgearFit.
  - For the new dash mechanic on gamepad/joystick, bind an unused input (<Gamepad>/buttonSouth [A/Cross] and <Gamepad>/leftTrigger) while preserving existing bindings (uttonEast for Run/legacy, uttonWest for Grab/Door, uttonNorth for Throw, etc.).
  - Never alter camera hierarchies, Cinemachine cameras, or SplitScreenManager in Gameloop_Demo.
  - In Gameloop_Demo, calibrate CooperativeFireHose with earSideOffset: 0.35, handHeight: 0.34, handForward: 0.18, earHandForward: 0.3, movementSpeed: 0.75, maximumUpwardAim: 30, and ensure Hose_RearGrip has a kinematic Rigidbody.
### [2026-11-11] Adaptive Demo Follow-Up Audit
- **Context**: After the hose changes, review what remains in `AdaptiveAnimation_Demo` while leaving `Gameloop_Demo` unchanged. The 23 selected active-demo tests passed, but they do not cover every visual and gameplay condition.
- **Findings (open)**: A heavy object carried solo moved at about 0.048 m/s while the walking state remained off. During a controlled cooperative turn, an operator moved with reported animation velocity at zero. Test both players' measured movement and gait at low carry speeds, during turns, and in backward and sideways travel.
- **Findings (open)**: In a controlled simulation, a solo-carried large object penetrated a wall while the player's capsule stayed clear. Test the carried object's full shape against environmental obstacles; checking the player alone is insufficient. Cooperative rotation near obstacles also needs coverage.
- **Lesson**: Passing source-rig contact tests does not establish contact or clearance on the visible replacement model. Extend visual checks to representative carried objects, door handles and lamps, including movement, before treating those interactions as verified.
- **Findings (open)**: The demo has no permanent fire target, and cylinder grip, survivor carrying, and the proposed unified adaptive context remain incomplete. Proposal evaluation still needs a parameter matrix, a manual baseline and naturalness/smoothness measures.
- **Lesson**: Validate the animation clip actually used by the active controller: the existing loop test samples `Walking.anim`, while the current walking state uses `Running.anim`. Keep control and speed documentation aligned with the implemented scene.

### [2026-11-11] Straight Hose Span and Compliant Carry Pose
- **Feedback**: The supported hose between operators still looked bent, and the holding pose felt too rigid.
- **Rules**: Keep the long inter-player span aligned beside the front operator and place the short nozzle lead ahead of the torso. A guide behind the front operator creates a visible elbow; removing the guide entirely routes the cable through the body.
- **Rules**: Use one damped carry offset for both grips and the real cable supports, with bounded gait bob, breathing and turn response. Keep hand targets attached to these grips, add only small torso effort, and reduce compliance as hose pressure rises. Verify straightness, actual bounded motion, visible hand contact and body clearance together.
- **Rules**: Include capsules at the rendered hose radius for all supported spans in baked-mesh penetration checks on both operators. Nozzle-only checks miss the short cable lead crossing a sleeve. Moving the cable center ahead while retaining the hand grip center can preserve reach and remove that overlap.

### [2026-11-11] Physical Hose Support and Locomotion
- **Feedback**: Hose operators slid while walking, the rear operator held a floating station instead of the cable, and the nozzle/holding pose intersected the character. Keep Gameloop_Demo unchanged.
- **Rules**: Match both operators' animation rate to actual displacement, including the rear operator's formation corrections. Reverse the cycle for backward motion and orient the visible stride plane for sideways travel; avoid double damping playback.
- **Rules**: Anchor the actual cable bodies at the rear hand station, route its adjacent spans outside both torsos, and restore body states on release. Support must also work when the rear role is picked up first. Account for the held span when reeling and distribute spring rest lengths within the same total cable budget.
- **Rules**: Pivot nozzle aiming around the hand contacts, fit its scene instance geometry to the visible model, and include head aiming clearance. Verify baked visible meshes with enabled, cooked probe colliders; disabled MeshColliders can produce false-negative penetration queries.
- **Rules**: Test real Input System movement, sustained turning, stopping, visible hand contact, and nozzle/body clearance. EnterPlayMode editor tests must yield null rather than WaitForEndOfFrame, and iterator locals captured by lambdas can be lost across the test domain reload.
- **Rules**: Notify the test player loop of Game View focus when the test runner's progress window owns focus. Flush synthetic key releases through InputSystem.Update before exiting Play Mode so later tests cannot inherit a held aim key.

### [2026-11-10] Replacement Geometry and Visible Hose IK
- **Feedback**: Replacement scale and helmet fit looked wrong; validate geometry, pickup, and hose IK in AdaptiveAnimation_Demo only.
- **Lesson**: Use BakeMesh with useScale=true and renderer transforms to measure actual geometry on this imported rig. Renderer bounds overstated skull size; a small instance-only helmet fit fixed the forehead clearance without rescaling the skull or editing the FBX.
- **Lesson**: Validate IK on the replacement model's actual hands as well as the gameplay rig. The replacement's shorter reach required a lower, nearer hose carry offset; stretching bones would hide the mismatch.
- **Lesson**: Register hierarchy changes with Undo.SetTransformParent and isolate test undo groups. Verify undo leaves no root-level orphan appearances. Fitted meshes must be rebuilt from serialized source references after scene reload and disposed when disabled.

### [2026-11-10] Bidirectional Door Swing and Stable Closing Position
- **Feedback**: Close from the same position used to open; open away from either doorway side; reposition an opposite-side closer around the obstructing leaf.
- **Lesson**: Classify the actor against stationary doorway anchors. Keep the opener's closing anchor unchanged and choose the signed opening rotation from the actor's side.
- **Lesson**: Place an opposite-side closing anchor beyond the maximum horizontal leaf radius and route alignment around the tip on the actor's current side of the leaf. Check both route segments against external obstacles with capsule casts and a line check.
- **Lesson**: Tests must open and close without teleporting between actions, cover both signed swings and opposite-side lateral approaches, and inspect capsule penetration throughout alignment and closing.

### [2026-11-10] Flexible Door Reach and Contact-Driven Doors
- **Feedback**: Door interaction required standing on a precise point; some doors should open from player contact without an interaction button.
- **Lesson**: Measure door reach against the leaf and choose the player's nearest side, while retaining authored points for animation alignment. Ignore the target leaf in approach sweeps, but reject other walls with both capsule sweeps and a line check.
- **Lesson**: Open interaction points must follow the hinge. Scene prefab instances must be unpacked locally before reparenting existing prefab children; inspect warnings rather than assuming SetParent succeeded.
- **Lesson**: Validate pushable doors through real Rigidbody contact at each player's normal walking speed, from both sides, with hinge limits and without reserving a manual interaction.

This document records user feedback, architectural decisions, past corrections, and refined rules to prevent repeating mistakes and drive continuous improvement.

## Session Review Checklist
- [x] Review all entries in this log at the start of each development session.
- [x] Ensure any newly identified anti-patterns or mistakes are documented below with corrective directives.

---

## Log Entries

### [2026-11-07] Large Shelf Carry and Two-Player Locomotion
- **Feedback**: The large shelf still clipped the character, hand spacing was too wide, carrying remained too fast, Player 2 could not move, and walking looked abrupt.
- **Rules**:
  - For tall carried meshes, place real surface grips near the lower rail and pitch around the grip midpoint; verify reach and head/pelvis clearance together across weights.
  - In a direct two-player demo scene, assign both PlayerInput instances an action map and distinct keyboard control schemes; pair the shared keyboard explicitly.
  - Calibrate movement penalties and animation clip speed together, and prevent sprinting while carrying so gait and velocity stay aligned.
  - Derive cooperative movement test expectations from the active speed profile instead of stale fixed numbers.

### [2026-11-06] Visible Carry Geometry and Stylized Weight
- **Feedback**: Large objects still intersected the character; heavy-load lean did not read as leaning back/looking up. Animation direction should incorporate the twelve animation principles, starting with exaggeration and anticipation.
- **Rules**:
  - Validate contact against enabled visible mesh geometry, not just a hidden proxy collider or IK target.
  - Keep bulky objects outside the player's body while finding reachable contacts on their near face; preserve the visual object's authored size.
  - Verify lean direction on the visible rig in world space. Numeric local-X angles alone do not establish readable weight.
  - Use configurable exaggeration and a preparatory motion before pickup contact; verify that anticipation precedes attachment and that hand contact survives the resulting pose.

### [2026-11-05] Character Model Slot — Flexible Rig Replacement
- **Context**: A replacement character used a compatible set of bone names but differed in export orientation, non-uniform scale, and rest transform data.
- **Lessons & Patterns**:
  - Character appearance replacement must not reject a compatible model solely because its rest pose or DCC export transforms differ.
  - Map bones by hierarchy when available and fall back to unique bone names; preserve the gameplay skeleton, Animator, and IK as the controlling rig.
  - Rebuild copied mesh bind poses for the target skeleton and normalize the generated visual against the original renderer bounds before hiding the original model.
  - Verify the result in the actual target scene and Play Mode; scene-specific tests that assume a missing object are not evidence of a model-slot regression.

### [2026-11-05] Character Model Slot — Animation Mirror Serialization Fix
- **Context**: After replacing the character model, the replacement visuals appeared correctly but animations did not play. Root causes: (1) `CharacterModelAnimatorMirror` stored `sourceAnimator` and `appearanceAnimator` in non-serialized private fields that became `null` after domain reload / play mode entry; (2) the appearance Animator was created without copying the source's avatar, so it couldn't resolve bone paths.
- **Lessons & Patterns**:
  - Runtime MonoBehaviour fields that must survive domain reloads (e.g. editor-configured references) **must** use `[SerializeField]`; plain private fields are zeroed on recompile and play-mode transitions.
  - When cloning an Animator for a replacement skeleton, always copy `avatar`, `cullingMode`, and `updateMode` from the source — not just the `runtimeAnimatorController`. Without a valid avatar the Animator cannot map animation curves to bones.
  - Non-serialized arrays (like `lastStates`) need reconstruction in `Awake()` from serialized state, since they will be `null` after deserialization.
  - Both original (IDLE.fbx) and replacement (Rubah_P1.fbx) use Generic animation type with identical 50-bone skeletons — they share the same avatar and controller successfully.

### [2026-11-05] Character Model Slot — Play Mode Offset & Oval Shape Distortion Fix
- **Context**: When entering Play Mode, the replacement model jumped significantly to the right (+16 units offset) and appeared oval/stretched (lonjong).
- **Lessons & Patterns**:
  - **Root Cause of Offset (+16 in X)**: The generic animation clip (`Idle`) on `Mulyono` controller has animated position curves on the armature node (`metarig.009` with `localPosition.x = 15.975`). In the original FBX, `IDLE` container was placed at `(-7.89, -0.14, 0)` with scale `0.491` to compensate for this 16-unit shift. When `CharacterAppearance` was parented under `IDLE` and renamed `metarig.010` to `metarig.009`, the animation forced the replacement armature to jump 16 units to the right at runtime, compounding with the bounds offset calculated in Edit Mode.
  - **Root Cause of Oval Distortion (Lonjong)**: The replacement model FBX (`Rubah_P1`) had unapplied DCC transforms on its armature (`metarig.010` export scale was non-uniform: `72.856, 54.544, 56.357`). When the animation clip evaluated, it overwrote `metarig.009` scale to uniform `(100, 100, 100)`. This un-squashed the bones by `(1.37x, 1.83x, 1.77x)`, stretching the character vertically and making it oval.
  - **Solution**:
    1. Parent `CharacterAppearance` directly to the player (`slot.transform`), decoupling it from the old `IDLE` container's internal offsets.
    2. Do NOT rename the replacement model's armature (`metarig.010`), preserving its unapplied DCC rest scale and position so the mesh skinning matches the creator's intended proportions (aspect ratio perfectly preserved at 0.804).
    3. Use direct bone rotation synchronization in [`CharacterModelAnimatorMirror`](file:///d:/Github/project-fire-rescue/Assets/Scripts/CharacterModel/CharacterModelAnimatorMirror.cs) via mapped bone pairs (`source.localRotation` -> `target.localRotation`) in `LateUpdate()`.
    4. Normalize height against original renderers using a uniform scale (`scale = refBounds.size.y / importedBounds.size.y`) and align feet (`min.y`) and center (`x, z`).

### [2026-10-22] Cooperative Carry Backward Slack & Distance Limits
- **Context**: Added backward slack room (`backwardRoom`) to cooperative carry (`CooperativeCarryObject`) so players can step backward away from the box without moving closer than the base stance.
- **Lessons & Patterns**:
  - Distance leeway (slack) must strictly be one-directional relative to base stance: players may step backward away from the object up to `[stanceGap, stanceGap + backwardRoom]`, but can never step forward closer than `stanceGap` (preventing cramped meshes and visual clipping).
  - The maximum reach ratio must align with IK arm reach (`HeldObjectGripController` has `maximumReachRatio = 0.98`), keeping `stanceGap + backwardRoom` (~0.45m-0.50m) comfortably within the 0.55m-0.65m arm reach limit.
  - When partner is an anchor, stepping backward expands the active player's gap until `stanceGap + backwardRoom`; once taut, pulling against the anchor is blocked and triggers effort strain animation without dragging the anchor or object.
  - During carrying state, bypass redundant alignment sweep tests in `FixedUpdate` so dynamic slack movement does not trigger drift realignment pauses.

### [2026-10-22] Cooperative Carry Physics & Anchor Pivot Mechanics
- **Context**: Refactored two-player cooperative carry (`CooperativeCarryObject`) to support independent movement, tethering, and idle-player anchor/pivot mechanics (inspired by *Carry the Glass* style).
- **Lessons & Patterns**:
  - When one player is idle during 2-player carry, treating the idle player as a rigid anchor pivot prevents linear dragging while allowing smooth tangential orbiting around the stationary player.
  - To prevent idle players from playing locomotion animations while acting as anchors, supply per-player velocity (`GetPlayerVelocity(InteractionController)`) to `ThirdPersonPlayerController` rather than a unified `GroupVelocity`.
  - Maintain the physical distance constraint ($L = \text{totalSpan}$) between players across both translation and rotation to keep carried objects from stretching or clipping into player models.
  - Preserve `SafeDelta` sweep tests across both players and the carried object to stop rotation and translation before penetrating environmental geometry.

### [2026-10-14] Camera System & Split Screen Refactor
- **Context**: Fixed adaptive split screen and zoom camera mode toggle in `Gameloop_Demo`.
- **Lessons & Patterns**:
  - Distance-based camera splitting must include a hysteresis margin to prevent rapid flickering around threshold boundaries.
  - When switching camera modes, ensure inactive camera components/VCams are disabled to prevent unnecessary GPU draw calls.
  - Runtime camera toggle controls must utilize `UnityEngine.InputSystem` rather than legacy `Input.GetKeyDown`.
  - Maintain field name compatibility with serialized scene assets so references aren't lost on recompile.
  - Strict preservation of gameplay, interaction, and animation subsystems when working on camera features.

### [2026-10-14] Initialization & Core Rule Adoption
- **Context**: Initialized project guidelines from `AgentRules.md`.
- **Directives Established**:
  - Exclusively use Unity's new Input System Package (`UnityEngine.InputSystem`), avoiding legacy `UnityEngine.Input`.
  - Rely on Unity MCP tools for inspection, scene management, code execution, and editor operations.
  - Never manually create or edit `.meta` files (leave to Unity asset pipeline).
  - Enforce verification before marking tasks complete (functional checks, console log clean).
  - Autonomous bug fixing from error traces and logs.
  - Spawn subagents for parallel exploration to keep primary context clean.

### [2026-11-07] Locomotion Loop and Stop Response
- **Feedback**: Walking looked jerky at the loop and sometimes continued after the player stopped.
- **Lesson**: Use measured horizontal velocity for Animator walking/running state changes; use damped velocity only for playback-rate blending. Otherwise the damping tail delays the idle transition after motion has ended.
- **Lesson**: Measure the first and last skeleton poses of a looping clip. The Walking clip's matching stride pose was at 1.625 seconds, while its old 2.75-second endpoint was mid-stride. Validate the visible replacement model's foot travel against actual scene movement speed when calibrating Animator state playback rates.

### [2026-11-07] Walk Feel and Dash Replacement
- **Feedback**: The walking motion still lacked the intended game feel. Use the former running gait for walking and replace held running with a short dash pose.
- **Lesson**: Calibrate gait playback against a fixed world-speed reference across players with different movement speeds, including weight penalties. Use a press edge for dash input, a short duration and cooldown, and block the dash while carry and interaction rigs need stable hand contact.
- **Lesson**: Build the dash silhouette on the gameplay skeleton so the replacement model mirror inherits the same forward torso lean and trailing hands and feet.

### [2026-11-07] Walking Gait Cadence Calibration
- **Feedback**: The reused running clip still played too quickly and felt disconnected from walking motion.
- **Lesson**: A single foot-plant sample can pass while the full stride cadence still reads as running. Check the clip's repeating gait period in real seconds as well as planted-foot travel, then tune clip playback and player movement speed together so slowing the animation does not create foot sliding.

### [2026-11-07] Active Demo Carry, Props, and Hose Regression
- **Feedback**: Fix the first four failing tests in `AdaptiveAnimation_Demo` while leaving `Gameloop_Demo` unchanged.
- **Lesson**: Paired carry translation uses the average of both players' speed after penalty; tests must not assume Player 1's speed when the players have different base speeds. Differential speed may rotate the formation while preserving its spacing.
- **Lesson**: Copy a configured hose scene instance with its full cable hierarchy when the base prefab lacks scene-added grips, water, and VFX. Clear references outside the copied hierarchy, and save only the destination scene.
- **Lesson**: A level-wide props test cannot be treated as passing by replacing its >100-prop coverage with a three-prop demo. Keep the level test intact and add a focused active-scene test for representative ownership, formation, and hand contact.

### [2026-11-12] Selective Animation Principle Mapping
- Feedback: Do not assign all twelve animation principles to players by default. Mark only principles with a concrete planned use and explain each character's selection.
- Rule: Separate proposed animation uses from verified implementation; keep player selections consistent and base NPC differences on specified motion rather than species alone.

