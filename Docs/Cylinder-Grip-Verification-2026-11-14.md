# Cylinder grip in Gameloop_Demo

Implemented on 13 November and verified after reconnection on 14 November 2026. The existing `Prop_FireExtinguisher` opts into Cylinder grip directly. Decorative Cylinder meshes on the hose connections remain unchanged.

## Implementation

- `CylinderGripProvider` samples paired contacts on the readable visible mesh triangles at an explicit barrel level. The imported extinguisher's length axis is local Z, with grip height .35 along its bounds. The near side is selected toward the holder's torso, and anatomical hand ordering is preserved.
- Contact spacing grows with radius/scale and is limited to .14 m per side. Rays intersect actual facets instead of using box corners. Points are recomputed from the current mesh transform; normals use the inverse transpose for nonuniform scale.
- Grip frames use the existing convention: forward points into the surface, up follows the cylinder axis. `Player_CylinderGripRotation.asset` is a separate cylinder-side profile using the calibrated character corrections. The primary box-side profile remains unchanged. The grip consumer selects a matching additional profile and uses holder-specific frames when recording calibration.
- `AdaptiveAnimationManager` recognizes this provider as Cylinder and reads its visible mesh dimensions in object axes. The existing context continues to provide weight, role and interaction type. This single-holder APAR reports Role=None and CarryObject. Its new animation load is authored at 1 kg, matching its unchanged physical mass; weight data remains independent of Rigidbody.mass.
- Unity's model importer enabled Read/Write for `Prop_FireExtinguisher.fbx` so surface fitting also has readable geometry at runtime. This is the only importer flag changed.

The existing Liftable, dynamic Rigidbody, SpringJoint carry, input/events, collision, placement and HoseWaterController remain in use. The extinguisher is not passed through AdaptiveSceneProps or converted into a new carry root. Its tool references and off state are preserved; this work does not repair or validate firing with the originally unassigned Water FX/raycast origin.

## Verification

Tests enter actual Editor Play Mode through EditMode coroutines.

| Check | Coverage |
| --- | --- |
| Pickup, context and visible contacts | Both players at scale/load pairs (.8, 1 kg), (1, 10 kg), (1.4, 30 kg), with actual Pickup_Grab, active IK, visible hand-to-mesh distance below .025 m, target tracking below .04 m and palm-frame error below 1 degree. The largest pair also moves/turns. These are six configurations, not a full Cartesian parameter matrix. |
| Geometry | Three uniform scales plus one nonuniform scale, each at three yaw/tilt poses. Provider points lie within .001 m of the actual mesh; inward frames, hand ordering, growing/capped spacing and yaw-independent dimensions are checked. |
| Lifecycle | Provider disable/re-enable, ownership, original SpringJoint settings/mass, actual placement release, context cleanup and unchanged tool references/off state. |

The geometry and both-player contact tests passed in job `0e41834968d94a298dd297069ec258ba`. Their initial distance harness mishandled degenerate triangles, allowing NaN values to reset the accumulated minimum. Edge projection now handles degenerate faces without relaxing contact tolerances. The lifecycle harness also captured a local in a lambda across EnterPlayMode; Unity's domain reload lost its generated closure. An explicit joint loop preserves the same cleanup assertion and avoids that test-runner issue.

Final results after reconnection: **12 distinct test methods passed across two runs**. Job `733199b784be4a1e9acf50cdbb34335d` passed 10/10: Cylinder lifecycle, four shared-context checks, the source box size/weight matrix, carried-computer wall collision, destination computer/table contact and lean, hose contacts, and safe computer placement. Job `0a5e2c955ee64948a70be5dd5ffda28f` passed 2/2: Cylinder geometry and both-player contact/movement. The earlier regression job was lost when the MCP session restarted, so its outcome is not counted.

Original Box/SceneProp surface contacts remained below .0004 m with about 24 degrees visible computer lean. Solo wall overlap remained zero; placement wall overlap was zero and floor settling overlap about .000386 m. The hose regression recorded Unity's existing partial-convex-hull warning for mesh Sphere. Final Console inspection returned no errors or warnings. The Editor is in Gameloop_Demo, outside Play Mode, with no unsaved scene changes.

Fresh captures for both players were inspected and copied to `Docs/Images/Gameloop-Cylinder-P1.png` and `Docs/Images/Gameloop-Cylinder-P2.png`. The two character models and final poses are visually identical; separate player/input assertions establish that both slots were exercised. Numerical mesh-distance checks establish precise contact beyond what these overview captures resolve.

## Scope and preservation

The saved player prefab differs from its pre-Cylinder version only in the additional profile array entry. Removing that entry in memory reconstructs SHA256 `F7ADF66BF1F7F0FDF5B7CA8C4BAFC26D9C085FF4EB0E31C78389D127C5EB2952`. The primary box profile remains `10AC37A0DAACC3F8614BC7E297D42F08D5D2EC1E57D815741FFFE60197872C42`. AdaptiveAnimation_Demo, MainMenu and input bindings retain their prior hashes.

This implementation supports one explicitly configured readable mesh and axis. It does not auto-classify objects by name, add cooperative Cylinder carrying, establish the full proposal size/weight range, or implement Carry Survivor. Standalone builds and hardware gamepads remain separate validation.
