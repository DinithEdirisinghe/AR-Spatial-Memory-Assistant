# Moved furniture updates v8

**Current application: Evaluation lab v9.** The furniture behavior below is retained. See [Evaluation Lab.md](Evaluation%20Lab.md) for trial modes, measurements, outcomes and CSV download. The current panel title is SpatialSense | Evaluation lab v9; the v8 steps remain useful for ordinary navigation outside a trial.

Build the existing **M1_LiveRoomScan** scene once. The title should read **SpatialSense | Furniture updates v8**. Both scanning methods, item memory and dynamic obstacle detection remain available. No scene recreation is required.

## What changed

Previously, every obstacle in the original room map stayed blocked forever. This version retains which triangles belong to classified furniture and which belong to walls or other protected geometry. Live observations can reopen parts of an old furniture footprint after verifying the space and its floor. The route then updates using the corrected map.

**Blue floor tiles** show locations that were blocked in the original map and are now walkable. **Magenta tiles** still show new live obstacles. The original cyan scene mesh is a reference capture and can still show the old furniture. Choose **Furniture > Show walking floor only** to inspect the current navigation map without that old mesh.

These corrections are local to our navigation map. They do not erase Meta room anchors, change Meta's saved scene, or move saved item locations.

## First headset test

1. Load the room captured while a movable table, couch or bed was in its original position. A semantic furniture object must be present in that room model; an arbitrary chair visible only in the global mesh may have no usable furniture label.
2. Open Furniture. Confirm **Furniture updates: ON** and **Dynamic routing: ON**. Check that the candidate-cell count is greater than zero.
3. Move that furniture away from its old location. Look through the space it occupied and down at the newly exposed floor, from roughly 1–2 m away. Give the app time to observe the different parts of the old footprint; cells reopen gradually rather than releasing a whole object from one ray.
4. Choose Show walking floor only. Blue tiles should appear where the old footprint has been verified, and an active route can use those tiles when a connected path is available. If Find failed before the corridor opened, choose Find again after it opens.
5. Look toward the furniture's new location. The existing live layer should mark its new position magenta and route around it when it covers previously walkable floor.
6. Put an obstacle back on a blue location. A valid positive obstacle hit should reblock that corrected cell. A wall should remain blocked throughout the test.

The Furniture tab also offers reset to the scanned map, the existing margin control, test-counter reset and view controls. Switching furniture updates OFF restores original furniture restrictions. Turning it ON starts fresh verification. Turning only dynamic monitoring OFF pauses observations and retains confirmed corrections. Rebuilding a map, switching methods, restarting, losing room context or loading another room requires fresh verification.

## What qualifies for updating

The initial automatic allowlist is Meta **TABLE, COUCH and BED** semantic labels. Mixed labels, storage, screens, plants, walls, doors, windows and unclassified geometry are protected. This is a conservative initial scope, not automatic recognition of every movable object. Poor or missing scene labels can leave an object protected.

With **Meta room scan**, those classifications accompany the existing semantic floor/furniture geometry. With **App live scan**, the walking floor and obstacle geometry still come from the app mesh. Loading the matching Meta room supplies furniture classification: an app triangle must fit inside the same recognized furniture region and avoid protected semantic surfaces. Unmatched triangles remain protected. Loading or changing that semantic room context rebuilds the app map to prevent using stale classifications.

The app scan must already contain floor support under a candidate location. This build does not invent missing floor beneath a previously occluded object. If that support is absent, the location stays blocked; capture the newly visible floor and rebuild. Meta's semantic floor commonly includes those locations, so the first test is simplest with the Meta scan method.

## Evidence required before reopening

Each eligible 7.5 cm column tracks samples at the heights occupied by old furniture, spaced up to 30 cm apart. It also requires a current floor hit near the selected floor height with an upward surface normal. A valid depth ray must pass through and beyond an old occupied volume to count as free evidence. A ray that stops in front of it, a ray below an old tabletop, or a missing result does not verify that old surface.

All required height samples and floor evidence need at least three positive observation batches, spanning at least 0.6 seconds, with evidence no more than three seconds old. Many rays in one batch count only once for a sample. Long gaps reset incomplete evidence. An obstacle hit takes precedence over free observations in the same batch and immediately restores a previously cleared baseline cell. Walls and unknown obstacles cannot be cleared through this layer, including where their projected cells overlap furniture.

Only after verification does the map remove that furniture restriction and recalculate its extra margin. Unknown floor, floor-coverage restrictions and permanent structures remain intact. New live obstacles are then applied on top, so reopening old furniture cannot override a newly detected obstruction. Pending asynchronous item routes discard results when the obstacle revision changes.

These observations establish a correction to the sampled navigation model, not exhaustive knowledge of every hidden surface or proof of whole-body clearance. Thin legs, incomplete views, depth errors, inaccurate labels and alignment can still affect results. Physical tests determine practical reliability.

## Measurements and checks

The Furniture panel shows eligible and verified cell counts. The existing local diagnostics add furniture candidate, reopened and restored counts to source/test/revision-tagged entries. `baseline_furniture_changed` identifies a correction, alongside the existing route response events. Nothing is uploaded. These are session-local corrections and require fresh evidence after restart.

`Tools/Test-BaselineFurniture.ps1` checks complete volume-plus-floor proof, valid floor normals, repeated/time-separated observations, missing/stale/under-table evidence, route shortening, reoccupation, new live obstacles, protected walls and unknown surfaces, wall/furniture overlap, immutable original states, worker snapshots, missing floor support and negative anchor-local floor heights. Existing dynamic, native-room, grid and item-standing suites cover regressions. Editor and Android C# compilation checks types and SDK references; the headset test above remains required.
