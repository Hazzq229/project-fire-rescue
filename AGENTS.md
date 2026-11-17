# AI Agent Guidelines & Workspace Directives

This document outlines the operational directives and repository conventions for AI coding assistants and automated tools working on this project.

### 1. Unity Project Scope
* Treat this repository primarily as a Unity project, maintaining Unity-centric workflows across C# scripts, scenes, prefabs, and assets.
* Rely on Unity MCP tools for inspection, validation, and editor-safe operations instead of using `.NET` CLI commands.
* Do not manually edit or create `.meta` files; allow Unity to automatically generate and manage them when assets or scripts are created.

### 2. Input System Standard (Mandatory)
* Exclusively utilize Unity's new Input System Package (`UnityEngine.InputSystem`) for all input logic.
* Avoid incorporating or depending on the legacy Input Manager (`UnityEngine.Input`) for new features.
* Align all new input-related code with the project's existing Input System patterns.

### 3. Self-Improvement Loop
* Log any user feedback or corrections into `tasks/lessons.md` to document lessons and pattern updates.
* Formulate internal rules to prevent repeating mistakes, iteratively refining guidelines until error rates drop.
* Review `tasks/lessons.md` at the beginning of each session relevant to the project.

### 4. Verification Before Done
* Do not consider a task complete without proving functional correctness through test execution, log inspection, and behavioral diffs against the main branch.
* Evaluate your implementation against staff-engineer standards before finalizing.
* Use Unity MCP tooling whenever necessary to verify modifications.

### 5. Demand Elegance (Balanced)
* Pause during non-trivial updates to evaluate whether a cleaner implementation exists, replacing temporary or hacky fixes with refined solutions.
* Avoid over-engineering simple fixes while maintaining thorough self-critique prior to presenting work.

### 6. Autonomous Bug Fixing
* Resolve bug reports and failing CI tests autonomously by examining logs and error traces without requiring user hand-holding or context switching.

### 7. Subagent Strategy
* Frequently spawn subagents to handle research, codebase exploration, and parallel analysis in order to keep the primary context window clear.
* Assign a single, focused task per subagent to scale computational effort effectively on complex problems.
