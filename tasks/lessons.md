# Project Lessons & Self-Improvement Log

This document records user feedback, architectural decisions, past corrections, and refined rules to prevent repeating mistakes and drive continuous improvement.

## Session Review Checklist
- [x] Review all entries in this log at the start of each development session.
- [x] Ensure any newly identified anti-patterns or mistakes are documented below with corrective directives.

---

## Log Entries

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
