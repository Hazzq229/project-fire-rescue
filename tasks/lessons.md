# Project Lessons & Self-Improvement Log

This document records user feedback, architectural decisions, past corrections, and refined rules to prevent repeating mistakes and drive continuous improvement.

## Session Review Checklist
- [x] Review all entries in this log at the start of each development session.
- [x] Ensure any newly identified anti-patterns or mistakes are documented below with corrective directives.

---

## Log Entries

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
