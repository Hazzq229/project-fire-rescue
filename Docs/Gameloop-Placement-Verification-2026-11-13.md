# Placement verification — 13 November 2026

## Behavior repaired

The previous placement code only raycast downward and wrote the interpolated and final position directly. It also dropped the item when the animation exited even if a placement could not safely complete.

Placement now checks active solid colliders attached to the held Rigidbody, chooses a supported target from the preferred forward position and nearby alternatives, and validates the complete lowering path with CarryMotionGuard. Five footprint support rays prevent selecting a destination with no support at its edges. The item's current rotation is preserved while lowering.

Only the first holder applies the pose and commits it at the actual Pickup_Grab animation event. Both the interpolation and final commit are guarded. StableCarryAttachment no longer independently writes placement positions.

No support means no placement starts. A new obstruction during lowering prevents unsafe release and retains the object. Animation cleanup restores movement and the original kinematic/spring settings. Retained cooperative objects re-align after both operators finish so cancellation does not leave a table permanently at a lowered height.

## Verified outcomes

Tests are EditMode coroutine tests entering Unity Editor Play Mode. Final outcomes from three focused runs cover **10 distinct passing checks**; this is an aggregate, not a single ten-test job.

| Check | Outcome |
| --- | --- |
| Computer near wall and floor | Real animation/event placement releases onto the floor; wall overlap 0; maximum measured floor overlap during lowering/settling about 0.000386 m; final bottom approximately 0. |
| Paired table near wall and floor | Both operators lower and release the same object at contact; wall overlap 0; floor overlap about 0.0000006 m; final bottom 0. |
| No floor in placement range | Object remains held and controls remain available; no unsupported forced drop. |
| New blocker during solo lowering | Object does not cross blocker/floor; ownership retained and controls recovered. |
| New blocker during table lowering | Both holders retain ownership, controls recover, table returns to its previous carry height/state, and visible hand-target contact recovers. Blocker/floor overlap 0 in this probe. |
| Small/medium/big demo placement, ownership, cancellation, throw and dash | Existing regression passes with nearby pickup fixtures and a floor-aware lowering assertion. The path is checked for floor penetration. |
| Heavy solo/team gait and directional stride | Existing focused regression passes. |
| Solo carried-object wall collision and sharp retreat | Existing focused regression passes. |
| Cooperative translation, turning and orbit near wall | Existing focused regression passes, including player capsules and hand contact. |
| Visible computer/table contacts and body lean | Existing strengthened destination regression passes; hand distances remain below 0.0004 m and computer backward chest lean is about 24 degrees at the controlled load. |

The old size-placement test originally expected at least 2 cm of lowering even when the item was already within millimeters of its supported destination. That assertion was corrected to respect available lowering distance and supplemented with actual floor penetration checks; final support, ownership/event timing, dynamic physics and no-throw assertions remain.

## Test evidence

- `4b015fcf739b422498266e8b3d1c2379`: all five destination placement checks passed; the old arbitrary lowering-distance assertion failed.
- `52cee54fb8be4e5da22e8ec854d5f8ac`: corrected small/medium/big placement regression passed.
- `90c49bdd3c76432bb3f79d761afb38b5`: four carry-motion/contact/lean regressions passed.

`Temp/Gameloop-Computer-Placed.png` was inspected and shows the released computer resting beside the wall. Tests use temporary support/wall/blocker geometry in a clear area of Gameloop_Demo during Play Mode; scene geometry is not saved by the tests.

## Coverage boundaries

These checks establish placement for representative stable single-carry props, cooperative furniture and the original size-varied demo objects. Generic SpringJoint cancellation restores its spring settings but was not separately exercised with a dedicated item. Exhaustive terrain edges, every prop shape, hardware input and standalone builds are not established by this run. Conservative footprint validation can refuse placement where adequate support or a clear lowering path cannot be found.
