# Project Lessons & Self-Improvement Log

This document records user feedback, architectural decisions, past corrections, and refined rules to prevent repeating mistakes and drive continuous improvement.

## Session Review Checklist
- [x] Review all entries in this log at the start of each development session.
- [x] Ensure any newly identified anti-patterns or mistakes are documented below with corrective directives.

---

## Log Entries

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
