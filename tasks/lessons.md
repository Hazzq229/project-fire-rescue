# Project Lessons & Self-Improvement Log

This document records user feedback, architectural decisions, past corrections, and refined rules to prevent repeating mistakes and drive continuous improvement.

## Session Review Checklist
- [x] Review all entries in this log at the start of each development session.
- [x] Ensure any newly identified anti-patterns or mistakes are documented below with corrective directives.

---

## Log Entries

### [2026-11-18] Fire Hose Discrete Reel Step (Spam Input Mechanic)
- **Context**: Replaced the automatic continuous toggle reel with a discrete step-per-press mechanic so players spam buttons (N/M) to extend/retract.
- **Lessons & Patterns**:
  - Replaced continuous motor toggle with discrete `ApplyReelStep(actor, direction)` triggered by `HoseExtendPressed` / `HoseRetractPressed` (`WasPressedThisFrame()`).
  - Added configurable serialized field `reelStep` (default: 0.5m) in `CooperativeFireHose`, allowing designers to fine-tune how much each key press extends or shortens the hose.
  - Retained `ToggleReel` and continuous `StepReel(float dt)` internally to preserve unit test backward compatibility.
  - Clamped shortening so it cannot retract past the operator's current distance to the hydrant (`rearDist + separation + 0.25f`), preventing sudden physics flings or snapping.

### [2026-11-16] Fire Hose Hydrant Cable Constraint Relocated from Player 1 to Player 2
- **Context**: Player 1 was being artificially stopped by the hydrant cable distance check even though Player 1's physical connection is solely to Player 2.
- **Lessons & Patterns**:
  - Removed hydrant cable length check (`cable.CableLength`) from Player 1 (`FrontPlayer`). Player 1 is bounded exclusively by the mutual tether to Player 2 (`maxTether = separation * 1.35f`).
  - Added hydrant cable length constraint to Player 2 (`RearPlayer`), capping distance from hydrant at `Mathf.Max(0.5f, cable.CableLength - separation)`. Since Player 2 holds the hose directly coming from the hydrant, Player 2 is the actual physical stop.
  - When both players move and Player 2 hits the hydrant cable limit, Player 2 cannot move further away, and the tether clamp absorbs 100% of Player 1's excess forward movement, naturally holding Player 1 at `maxTether` from Player 2.
  - Updated `StepReel` to calculate minimum reel retraction against `rearGrip` position + `separation` + 0.25m rather than `frontGrip`.

### [2026-11-16] Fire Hose Anchor System & Unrestricted Proximity (Carry Object Analogy)
- **Context**: Adapted the two-player anchor system from `CooperativeCarryObject` into `CooperativeFireHose` without a minimum distance floor, allowing players to approach freely.
- **Lessons & Patterns**:
  - Mutual anchor state: If Player 2 is idle, Player 2 is an anchor (rigid pivot) and Player 1 can approach freely, orbit, or move away up to `maxTether` (`separation * 1.35f` ~1.42m). If Player 1 is idle, Player 1 is an anchor and Player 2 can approach freely, orbit, or move away up to `maxTether`.
  - Unlike carrying a box/crate, there is no solid object between fire hose operators: remove artificial `minDistance` clamps so players can step completely close to each other without being pushed away.
  - When both players are active simultaneously, allow independent movement and clamp only the divergent separation vector at `maxTether`, preventing stutter or half-speed movement when running together.

### [2026-11-16] Fire Hose Solution 2: Fixed Front Distance & Manual-Only Player 2 Movement
- **Context**: Solved hose ballooning during reel and removed passive auto-pull so Player 2 must actively move with input.
- **Lessons & Patterns**:
  - In a two-operator fire hose, the distance between Player 1 (nozzle) and Player 2 (backup grip) must remain constant (`fixedFrontDistance = separation` ~1.05m across `fixedFrontConnectors = 3`, giving 0.35m per segment).
  - In `PhysicCable.SetCableLength`, assign distances individually via `SetJointDistance`: lock the front 3 segments to `fixedFrontDistance / fixedFrontConnectors`, and allocate all remaining length (`metres - fixedFrontDistance`) exclusively to the rear segments between Player 2 and the wall hydrant.
  - Do not auto-drag an idle Player 2 when Player 1 moves (`rearDelta += pull` removed). Player 2 must actively press movement keys.
  - In `MoveTeam`, clamp Player 1's movement against Player 2's position at `maxTether` (`separation * 1.35f`). If Player 2 is idle and doesn't walk, Player 1 is physically anchored by the taut hose and cannot move further away until Player 2 actively follows.

### [2026-11-13] Fire Hose 3-Connector Cable Grip & Unrestricted Cooperative Spray
- **Context**: Refactored `CooperativeFireHose.cs` and `PhysicCable.cs` so Player 2 grips directly on the physical hose 3 connectors back from Player 1, and removed rigid straight-line spray restrictions.
- **Lessons & Patterns**:
  - Expose cable segment connectors (`IReadOnlyList<Transform> Connectors`) from `PhysicCable` so external interactables can anchor dynamically to cable segments.
  - When idle/unheld, anchor `rearGrip` to the 3rd connector (`cable.Connectors[Count - 3]`) so Player 2's grab trigger is located physically on the hose. Detach `rearGrip` from `frontGrip`'s hierarchy at runtime (`SetParent(null, true)`) to prevent parent transform rotations from swinging the grip in an arc.
  - When Player 2 holds `rearGrip`, pin the 3rd cable joint (`cable.Points[Count - 1 - 3]`) to Player 2's hands (`isKinematic = true` during hold, restored to `false` on drop), allowing the 3 hose segments between Player 1 and Player 2 to naturally hang, sag, and flex in 3D physics.
  - Disable artificial `supportSegment` LineRenderer since real physical cable connectors now bridge the distance between Player 1 and Player 2.
  - Remove straight-line stance checks (`Vector3.Distance(RearPlayer.position, RearStance) < 0.18f`) from `ReadyToSpray`. Rule: as long as both operators hold their grips (`HasTeam && !FrontPlayer.IsPickingUp && !RearPlayer.IsPickingUp`), water spraying is always permitted even around corners or obstacles.
  - In `MoveTeam`, sweep test obstacles only for the active leader (`SafeDelta(leader.GetComponent<Rigidbody>(), delta)`), allowing RearPlayer to independently maneuver around obstacles and corners without ghost collision blocks.

### [2026-11-13] Camera-Relative Movement Restoration & Adaptive Split-Screen Support
- **Context**: Re-implemented camera-relative movement in `ThirdPersonPlayerController.cs` and `CooperativeFireHose.cs` after past refactor inadvertently reverted it to world coordinates.
- **Lessons & Patterns**:
  - Always project horizontal input vectors against the camera's flattened forward and right vectors (`forward.y = 0; right.y = 0; forward.Normalize(); right.Normalize()`) to preserve intuitive isometric controls.
  - Dynamically resolve the active camera between shared camera (`Camera.main`) and player-specific split cameras (`SplitScreenManager.player1Camera` / `player2Camera`) based on `IsSplitActive` and player index.
  - When no camera is active/found (such as in headless or isolated unit tests), gracefully fall back to world-space vectors so tests and editor setups without cameras do not throw null reference exceptions.
  - Ensure `RequestedMove` exposed by `ThirdPersonPlayerController` returns camera-relative directions so that cooperative mechanics (`CooperativeCarryObject`, `CooperativeFireHose`) also move along the camera view direction.

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
