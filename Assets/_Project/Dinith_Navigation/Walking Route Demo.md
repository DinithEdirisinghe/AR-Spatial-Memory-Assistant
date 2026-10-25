# Walking route checkpoint

## Version 2 corrections

Run **SpatialSense > Create Walking Route Demo** again and rebuild to apply the pointer colour and scene configuration. The lower panel identifies this build as **Walking route v2**.

- The controller pointer is white, becoming yellow over a selectable marker. The walking route is blue with arrows on the floor. A straight controller pointer is not a walking route.
- **Left index trigger** toggles a map preview: blue wire edges show the actual navigable floor triangles, and red outlines show obstacle clearance. Inspect this when a visually open gap is rejected or a route appears to cross furniture.
- Removed the additional 0.3 m obstacle expansion. Unity carving already uses the baked agent radius. Obstacles now get a 0.03 m scan buffer, while the agent radius remains 0.3 m. See Unity's official NavMeshObstacle carving reference: https://docs.unity.com/en-us/engine/6000.3/script-reference/unityengine/ai/navmeshobstacle/carving
- The independent path guard now checks circular body clearance around rectangle corners rather than using square expanded corners, with a small tolerance for voxel edges. Valid corner turns should no longer be rejected solely by that square approximation.
- Standing-position selection includes the projected object location, a nearest-floor proposal, denser rings and up to 2.2 m horizontal distance. Proposed goals may project onto nearby usable floor; the real starting position cannot snap across an obstacle.
- Failure messages distinguish an off-map start, a start inside clearance, no nearby standing space, walls blocking views, and disconnected routes. Logs include obstacle counts, candidate counts and path corners for diagnosis.

Validation: source compilation and geometry tests cover padding, corner clearance, a usable 0.9 m corridor, an insufficient-clearance corridor and collision invariants. These tests do not validate the captured geometry or real headset route. Compare the preview against the real room and retest after building.

## Create and build

Choose **SpatialSense > Create Walking Route Demo** after script compilation. Save existing work if asked. The command copies the saved room-understanding scene to `M1_WalkingRouteDemo`, configures navigation, saves it, and makes it the only enabled build scene. The original scenes remain available. Repeating the command configures the existing walking-route scene.

Build an Android APK in a fresh folder, for example `Builds/Android/WalkingRoute/SpatialSense_WalkingRoute.apk`, and install on Quest.

## Controls and expected result

1. Wait for the room to be ready. Check alignment with the real floor and furniture.
2. Hold the right controller beside your keys and press **A**.
3. Move to another clear part of the room, with mapped furniture between you and the keys, and press **B**.
4. A **blue line** should route around the mapped furniture toward a **green standing-position ring** near the keys. The yellow object marker remains at the registered position.
5. Walk toward the green ring while checking your surroundings. The path updates twice per second and reports remaining walking distance.
6. Within 0.55 m horizontally of the standing position, aim at the keys marker and press the right trigger to manually confirm Found. This scene uses arrival at the planned viewing spot instead of the older straight-line one-metre confirmation rule.
7. Click the right thumbstick to cancel. The route clears on cancellation, new registration and confirmation.

Left **X** still toggles room outlines, while left **Y** reloads the room and clears the registered position and route.

## How this works

- MRUK SceneNavigation builds a mesh from the current room's floor only. Walking on tabletops, climbing and room-to-room links are excluded from this checkpoint.
- The build uses a starting agent radius of 0.3 m, height 1.7 m, climb 0.04 m and slope 8 degrees.
- Saved furniture and wall bounds create carving obstacles. Elevated furniture bounds extend down to the floor, avoiding routes underneath tables. Horizontal bounds have a 0.03 m scan buffer on each side, with body clearance applied through the agent radius.
- After building, the component waits for Unity's obstacle carving to take effect. An additional segment/footprint check rejects paths that still cross reserved obstacle footprints.
- Nearby standing positions are sampled around the target. Only complete paths to floor points within 2.2 m horizontally of the object are accepted. Candidates behind a mapped wall relative to the object are rejected.
- The chosen standing position stays fixed while its route remains usable. The line follows NavMesh path corners without smoothing across obstacles. No automatic player movement is applied.
- A missing path, an off-map starting position or a missing nearby standing position shows No route available and removes the line. Partial paths are rejected.
- Reloading or losing the current room clears guidance; a changed room model triggers a rebuild.
- The SceneNavigation component's automatic lifecycle is disabled in this scene. WalkingRouteDemo calls its public build API explicitly after room readiness, then waits for obstacle carving. Keep WalkingRouteDemo enabled; the unchecked SceneNavigation component is intentional.

The map comes from the saved room scan. It cannot account for unscanned or subsequently moved obstacles. Object confirmation remains a user action; the app does not recognize the keys or verify that they physically remain at the saved position. The original manual and room demo scenes retain their earlier behaviour.

## Headset checks before expanding

- **Mapped obstacle:** place the target beyond a scanned table. The blue line must avoid the table's footprint and the green ring must sit on reachable floor, not on the tabletop.
- **Movement:** walk along part of the path, and then move to a different clear position. The route should update while retaining its destination.
- **No route:** register near an inaccessible mapped surface or stand outside the usable floor. A failed or partial path must show a message and leave no previous line visible.
- **Confirmation:** far from the destination, trigger must not confirm. Near the ring, pointing at the object marker and pressing trigger should confirm and clear the route. Confirmation never happens automatically on arrival.
- **Lifecycle:** cancel, re-register elsewhere and reload the room. Old lines/rings must disappear and the next search must use the current point and room.
- **Narrow space:** if no route exists with these clearance settings, inspect the saved room model and settings rather than assuming the physical passage was proven blocked.

Source compilation and footprint intersection tests can be checked on the computer. Real-room mesh quality, route correctness, line visibility and performance still require these Quest tests.
