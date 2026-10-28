# Room scan lab — current v10, native capture history

**v10.1 MRUK capture fix:** App live scan previously discarded all samples when the full grid exceeded 8 ms, potentially making no progress even on Fast. It now completes 2x2 surface cells within each capture call, retains valid triangles and rotates through remaining cells across calls. A view can finish while you hold still; no triangle joins measurements from different calls. A completely missing-data view retries so depth warmup cannot permanently freeze capture. The budget is cooperative: an indivisible four-ray cell can exceed it if an SDK call is slow. Detail stays at your selected setting. Statistics show cells per batch and sampling time. Geometry updates now explain that the walking map needs rebuilding rather than repeatedly reporting Route cleared.

Use **Scan > Method: App live scan > Start / continue MRUK scan** for in-app depth capture. Use **Method: Meta room scan > Open Meta room setup** for the headset's full-screen capture. They are separate paths even though both use Meta SDK facilities. After a slow in-app batch, hold briefly to let the view fill, then pan slowly. Capture still requires valid depth and nearby geometry; missing depth and discontinuities are not converted into floor. Tools/Test-DepthPatchSampler.ps1 covers the budget-starvation regression and triangle validity rules. A Quest capture retest is required.

**Current panel title: SpatialSense | Connected rooms v10.** See [Connected Rooms.md](Connected%20Rooms.md) for the Rooms tab, persistent doorway links and item finding between rooms. App scanning and the single-room evaluation workflow remain available with the scope described in that guide.

**Current panel title: SpatialSense | Evaluation lab v9.** See [Evaluation Lab.md](Evaluation%20Lab.md) for the new Tests tab, comparison modes, trial outcomes and CSV exports. The scanning and furniture behavior below is retained outside evaluation trials.

**Current build: SpatialSense | Furniture updates v8.** The Scan tab retains Meta room scan and App live scan, each with its own mesh and walking map. App tools contains live scan detail, floor selection, coverage and OBJ export controls. See [Moved Furniture Updates.md](Moved%20Furniture%20Updates.md) for classified baseline-furniture corrections, [Dynamic Routing.md](Dynamic%20Routing.md) for live obstacles and [Native Item Finding.md](Native%20Item%20Finding.md) for item finding. The native-only v4.1 instructions below are historical; no scene recreation is required.

## The corrected goal

The user wants a fresh room scan using Meta's own capture process and the resulting mesh displayed in our application. This is different from quietly loading an old room's floor as the primary experience. V4 makes **Scan room with Meta** the main action.

The app requests Meta's native **Space Setup** flow through `OVRScene.RequestSpaceSetup()`. Meta temporarily controls the scanning screen. After the user completes it and returns, the app reloads the available room data and directly queries `MRUKAnchor.LoadGlobalMeshTriangles()` for the native scene mesh. That mesh is shown in cyan. The old sparse ray-grid mesh is not the primary capture path and is not used to replace Meta's returned mesh.

The app does not silently load a previously scanned room at first launch. **Load previous Meta room** remains a separate, explicit option. Tracking recovery can reload the room after a capture/load session has already begun.

## One build and one main test

1. Rebuild the existing `M1_LiveRoomScan` scene. Its title should be **SpatialSense | Native room capture v4.1**. Existing configured scenes do not need recreation.
2. Choose **Scan > Scan room with Meta**, or press right **A**.
3. Allow spatial-data access if prompted, then choose Scan again.
4. Complete the headset's native Space Setup. If the system shows existing setup data, choose its update/rescan option to perform a new capture. Meta controls the available steps.
5. Return to our application. It reloads the room and fetches the mesh explicitly instead of relying on the managed GlobalMesh cache.
6. Inspect the cyan native mesh. The default view shows the native mesh; the walking-grid overlay is optional.
7. Press right **B** or choose **Save native mesh + room**. Wait for confirmation. This saves JSON and OBJ copies locally; Meta also retains the room model produced by Space Setup.
8. Close and reopen. To inspect the last room without scanning again, explicitly choose **Load previous Meta room**. To capture again, use Scan room with Meta.

A return from the native flow is not proof that a fresh scan was completed. The SDK can report success even when the user cancels Space Setup. In that case the available model can be the previous scan; the UI says so. A failed request does not intentionally load old data as if a new capture succeeded.

## Controls

| Control | Action |
| --- | --- |
| Right A | Open Meta's native Space Setup. |
| Right B | Save the loaded native room JSON and scene mesh OBJ copies. |
| Left thumbstick click | Open/close the app menu. |
| Right pointer + index trigger | Select app menu buttons. |
| Left X | Cycle native mesh, floor map, both, hidden. |
| Left Y | Open/reposition the menu. |
| Right side grip | Confirm a floor destination while the destination pointer is green. |
| Right thumbstick click | Cancel destination selection. |

The old handmade scan controls (A pause/continue and trigger load) are no longer the primary UI. The earlier implementation is retained in source for development, but native capture does not depend on it.

## Native mesh, room floor and route map are separate

- **Native mesh:** the mesh Meta exposes after Space Setup. The app shows its actual vertices/triangles, without re-meshing them using our former ray sampler.
- **Native floor and furniture:** Meta's semantic floor polygon, walls and furniture bounds provide the walking map. The floor does not depend on patch coverage or a manually chosen floor height.
- **Walking map:** our 7.5 cm navigation grid with selectable 0/3/5/10/20 cm extra obstacle margin (3 cm default). It is built in the background from the native model after load. To display/rebuild it, choose **Route > Rebuild room map**. The mesh view is not automatically covered by the grid.
- **Destination:** choose **Route > Choose destination**, point at a green floor cell and squeeze the right side grip. The pointer intersects the native floor plane, so destination selection does not depend on a live depth hit.

Only the semantic floor polygon creates floor evidence. Furniture bottoms and flat non-floor geometry cannot silently extend the room floor. Furniture boxes and wall planes restrict routes. The workspace supports one reasonably level floor; it does not implement stairs or multi-level routing.

## Optional live obstacle checks

Live checks are OFF initially, so the native capture/mesh can be tested independently. Enable them on the Route or Options page after a walking map is ready.

This layer uses up to 24 environment rays at most four times a second, with a cooperative 4 ms sampling check and a 4 m ray range. Hits 20 cm–1.9 m above the native floor can restrict previously open room cells after positive observations in two batches. Missing observations never erase the native floor. Live samples are not the room reconstruction algorithm.

Confirmed live blocks remain when they leave the camera view. Three batches with rays actually traversing the old block's volume can clear a live block. Rays passing below the old obstacle do not clear it. The layer tracks at most 256 cells and is reset on map/room changes; it is not persisted. **Clear live blocks** resets this additional layer explicitly. Meta's stored furniture is never removed by that action or by live clearing. Move furniture in the base model by updating Meta's room scan.

Depth is limited to the current view and may miss small or thin objects. Temporal thresholds are prototype choices, not calibrated accuracy guarantees. This is a route experiment, not an independent safety/boundary replacement.

## Native files and ownership

Under `Application.persistentDataPath` on the headset:

- `spatialsense-meta-room.json`: MRUK's serialized room data, with global mesh requested when available.
- `spatialsense-native-room.obj`: the native mesh transformed into an application-owned Y-up floor frame, in metres. OBJ uses right-handed coordinates (X is flipped and triangle winding reversed).

The JSON and OBJ are separate atomic file writes, each with its own backup. They are inspection/export files, not a transactionally versioned two-file bundle. Reopening the application loads the system's model and its spatial localization; it does not treat the OBJ as an automatically aligned room.

MRUK owns its room/anchor objects. The application adds its own child frame and mesh renderer without editing their poses. Its explicitly queried native Mesh instance and display material are destroyed on reload. The SDK-owned cached GlobalMesh is not destroyed or modified.

If Meta supplies floor/furniture but no global scene mesh, the app reports that limitation and does not substitute the old handmade mesh. Run/complete Space Setup and check the native room data. If native loading fails or localization is unavailable, the app does not display a route at an arbitrary world origin.

## Platform limits

The public integration is **Meta Space Setup and Scene Mesh**, not an embedded copy of the private boundary scanner. The safety/play-area boundary experience and Space Setup are related physical-space features but are not interchangeable APIs. We cannot promise that the exported Scene Mesh has identical detail to every internal boundary visualization. Meta documents the public GlobalMesh as low-fidelity/high-coverage geometry created during Space Setup.

The scanner's system screen is not embedded in a Unity canvas, and this flow does not stream its incremental meshing progress into Unity while the system screen is active. The resulting mesh appears after return. Fresh capture must be completed on the headset, not through Link.

## Checks and device validation

C# runtime/editor and Android source compilation are checked against the installed SDK. Deterministic checks:

- `Test-ScanNavigation.ps1`: floor routing, thin walls, gaps, clearance, bounds, diagonal blocking and coverage regressions.
- `Test-NativeRoomNavigation.ps1`: authoritative semantic floor, prevention of false floor from other geometry, nonfragmented native floor, temporal live blocks, preservation on missing depth and height-aware free evidence.
- Existing mesh record/cleanup tests remain for the earlier handmade scanner, which is now secondary source code.

These checks do not establish that the system capture screen opens on this device, that the returned native mesh is adequate, or that exports and alignment work on Quest. Test the main capture sequence above, then compare the optional walking map and live checks. Also test cancellation, no saved scene, denied permission, tracking loss/recenter, returning from the native UI, repeated capture, export and loading the previous model.

## Official references

- [Manage Meta scene data and request Space Setup](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-manage-scene-data/)
- [Scene overview](https://developers.meta.com/vr/documentation/unity/unity-scene-overview/)
- [Live environment raycasting](https://developers.meta.com/vr/documentation/unity/unity-mr-utility-kit-environment-raycast/)

Installed MRUK/Core SDK: 207.0.1. The local OVRScene and MRUK sources were used to verify the actual request, load and mesh APIs. Archived High-Fidelity/Scene 2.0 features are not required by this implementation.

## Thin margins — v4.1

The user reported overly large obstacle buffers. The floor grid is now 7.5 cm instead of 15 cm. Its maximum side increases from 128 to 256 cells to preserve the previous approximate 19 m room span. This increases cell count and possible routing/rendering cost; Quest performance needs retesting.

Route/Options > **Extra margin** cycles **0, 3, 5, 10, 20 cm**, with 3 cm as the new default. The choice is saved in PlayerPrefs and remembered after reopening. Changing it rebuilds the map automatically inside the app; it clears the selected route/live checks, so choose a destination and re-enable live checks afterward. It does not require another APK rebuild or room rescan.

Red now means captured obstacle cells; orange means only the additional obstacle buffer. Green is route area, purple is an incomplete room-floor boundary, and grey is unknown. This distinction helps identify when the broad blocked shape is Meta's furniture box itself, rather than our padding.

Margins are still rasterized on a 7.5 cm grid. Zero and 3 cm can look similar in some places, and a boundary-aligned thin wall can occupy neighbouring cells. The value is additional map padding, not an exact physical body clearance. Red captured obstacles remain blocked at zero margin; there is no change to the native mesh, no diagonal corner cutting, and unknown floor is not made traversable.

Test once using the same native room: compare 3, 0 and 10 cm inside the app, inspect red versus orange, and verify that routes still go around furniture. Check the remembered setting after restart and compare frame smoothness with the previous grid.

## Current combined app — v5

The existing scene now adds an Items tab and persistent Keys/Wallet/Remote finding. The title is Find my items v5. Native capture/export, the thin-margin room map and live checks remain available. Follow [Native Item Finding](Native%20Item%20Finding.md) for the one-build register/save/find/confirm/cancel/restore/forget test. Load the previous Meta room after restarting to restore items in the same room identity; doing a new scan may require re-registration.
