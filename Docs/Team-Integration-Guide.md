# SpatialSense shared application base

## Create and run

After scripts compile, use **SpatialSense > Create Master Integration Scene**. Save existing work if prompted. The command copies the working multi-object scene to `Assets/Scenes/SpatialSense_MasterIntegration.unity`, adds application services and input sources, saves it, and selects it for building. It preserves all earlier scenes, including the old placeholder `Master_Integration`. Rerunning the command configures the new shared scene while preserving an assigned custom guidance renderer.

Build a fresh Android APK and install on Quest. The menu title should be **SpatialSense | Team base**, with the existing three objects and a new **Simulated vision update (test input)** button.

## First integration test

1. Select **Wallet**. Its existing saved position is retained until a replacement succeeds.
2. Select **Simulated vision update (test input)**. The menu closes and the app displays an explicit simulated-input prompt.
3. Move the real wallet to a different location. Hold the right controller beside it, release and squeeze the **right grip** (the side button under the middle finger). This is separate from the index trigger used to click the menu.
4. Wait for the new anchored position to save. The marker must be labelled **SIMULATED vision sighting**.
5. Press right B, or reopen the object menu and select Find. The route must target the new Wallet location.
6. Check Keys and Remote: their saved positions must be unchanged. Restart the app, locate Wallet again, and confirm the new position and source label are retained.

This is not a camera detector. Controller position stands in for a vision sighting, allowing the team to test the data connection before a detector exists. Right A remains ordinary manual registration; it now uses the same observation connection in this scene. Earlier scenes retain their controller-based workflows.

## Component ownership

| Component | Responsibility |
| --- | --- |
| `SpatialApplicationHub` | Validates incoming observations, exposes the shared registry and application state, and connects inputs to anchor persistence. |
| `ManualObservationSource` | Turns controller registration into an observation. |
| `SimulatedVisionObservationSource` | Emits a clearly labelled test observation when armed and the right grip is pressed. |
| `PersistentObjectMemory` | Creates, saves, loads and tracks anchors for each named object. It remains the persistence implementation, not the detector's input API. |
| `WalkingRouteDemo` | Reads the selected last-seen point through the shared registry, builds/queries navigation and chooses a standing position. |
| `DefaultGuidanceRenderer` | Draws the route, arrows and destination ring through `IWayfindingRenderer`. |
| `MultiObjectMenu` | Current controller menu. It can be replaced independently of observation validation and persistence. |
| `ApplicationDiagnostics` | Writes bounded local diagnostic sessions for input rejection, anchor commits, localization, navigation and search outcomes. |

The scripts still compile in the project's existing Unity assemblies. These interfaces establish component responsibilities; this is not a claim that separate package assemblies or independently deployed services have been created.

## Vision teammate's input contract

Stable object IDs are `keys`, `wallet`, and `remote`. They identify enrolled object slots; the display names My keys/My wallet/My remote are not IDs. Class detection alone does not establish that the detected item is the correct personal instance.

Submit a `Project.Core.Integration.ObjectObservation` through **IObjectObservationSink.TrySubmitObservation** on the Unity main thread. Alternatively implement **IObjectObservationSource**, then register/unregister that source with the hub during its lifecycle.

Required data:

- A new observation UUID, the object ID, and the estimated point in **Unity world metres**.
- Separate identity and position confidence, each in [0, 1].
- The actual observation timestamp using `DateTimeKind.Utc`.
- The current room UUID and world-frame token from **TryGetObservationContext**.
- `ObservationOrigin.Vision` for a real detector. The supplied simulator uses `SimulatedVision`.

At frame capture, obtain `capturedContext` with `TryGetObservationContext` on the Unity main thread and retain it with the frame, pose and UTC capture time. Skip the frame if no context is available. After inference, deliver on the main thread using that retained context:

```csharp
{
    var observation = new ObjectObservation
    {
        ObservationId = Guid.NewGuid().ToString("D"),
        ObjectId = "wallet",
        WorldPosition = worldPointFromTheCapturedFrame,
        IdentityConfidence = instanceMatchConfidence,
        PositionConfidence = spatialEstimateConfidence,
        ObservedAtUtc = capturedAtUtc,
        RoomId = capturedContext.RoomId,
        WorldFrameId = capturedContext.WorldFrameId,
        Origin = ObservationOrigin.Vision
    };
    bool accepted = sink.TrySubmitObservation(observation, out string reason);
}
```

The sink checks whether the retained context still matches the current one; do not attach a new frame token to an old coordinate just to make validation pass. Convert camera pixels/depth into world coordinates using the camera calibration and pose associated with that captured frame. No conversion from camera-local, pixel, normalized-image or millimetre coordinates is performed by this sink.

The demo rejects unknown objects, invalid/nonfinite coordinates, missing UUIDs, confidence below 0.8 identity or 0.6 position, non-UTC times, observations older than 30 seconds, times more than 5 seconds in the future, mismatched room/frame tokens, duplicate observation IDs, and non-manual observations older than the currently recorded sighting. It also rejects positions outside the mapped room and requests while an anchor operation is busy. Room reloads start a new coordinate-frame token. Confidence thresholds and simulator values are prototype choices, not calibrated detector accuracy.

Accepted means that an anchor save was started, not that it has committed. Wait for registry readiness or an anchor-commit diagnostic. A failed save preserves the previous stored record. Background observations do not silently change the selected object or start a search. Submit stable, relevant sightings rather than persisting every camera frame; back-pressure is rejection with a reason, not an unbounded queue.

## Registry and state for navigation/UX

`ISpatialObservationRegistry.TryGetSnapshot(id, out snapshot)` provides per-object availability, provenance, timestamp, separate confidence and a located-position flag. `TryGetTargetCoordinates` and the existing `ISpatialRegistryService.TryGetItemRecord` succeed only while the saved anchor is currently located. Listen for both updated and invalidated events.

Coordinates returned after restart are the saved object point reprojected into the current Unity world by its tracked anchor. The original seen timestamp/source are retained; reopening the application is not a new sighting. Legacy saved records without provenance are labelled StoredAnchor and have unknown (zero) confidence metadata. Ready means the coordinate is usable, not proof that the physical object has not moved.

`IApplicationStateProvider` exposes phase, selected object and message. Phases include loading room, choosing an object, awaiting test input, locating, saving, searching, found, cancelled and unavailable. `SpatialApplicationHub.RequestFind(id, out reason)` is the high-level object-search request. It may first require localization; listen for readiness and retry the request.

`INavMeshService.TryCalculatePath` is a **low-level floor-point query** and returns only a complete route. Pass a reachable floor endpoint, not the object's tabletop coordinate. High-level Find chooses the standing position. `UpdateNavMeshRegion` currently requests a full current-room rebuild from the saved map; localized incremental updates and live obstacle detection are not implemented.

## UX teammate's renderer connection

Implement `IWayfindingRenderer.DisplayGuidancePath(Vector3[] waypoints)` and `ClearGuidancePath()` on a MonoBehaviour, then assign that component as **WalkingRouteDemo > Guidance Provider** before Play/build. The default renderer is supplied as a working reference. Waypoints are world-space floor positions. Clear must remove old guidance immediately. Changes to route styling should preserve path corners and avoid drawing smoothing that cuts through obstacles.

The object menu is a separate component and can use the hub's state/registry/search API. Its current primitive collider buttons use the existing right-controller pointer. The source/persistence connection does not depend on those particular buttons.

## Diagnostics and verification

Local JSONL files are stored under the application's persistent-data `diagnostics` directory. They retain up to five sessions, each capped at roughly one megabyte. They include local path/goal diagnostics where needed, but no camera images. Logging failure must not stop object finding. Native file access/export is a separate debugging step; logs are not uploaded by this application.

Computer checks: `Tools/Test-ObservationPolicy.ps1`, `Test-ObjectCatalog.ps1`, `Test-AnchorRecordStore.ps1` and `Test-RouteMath.ps1`, plus editor/Android source compilation. These establish contract/file/geometry behaviour, not real detector performance or full device integration.

Quest checks required before handing the base to teammates:

1. All previously tested menu, register, save, restore, find, cancel and forget flows still work.
2. The simulated Wallet update changes Wallet's marker and subsequent route; other objects remain unchanged.
3. The source label and seen timestamp survive restart. No marker/route appears for an unlocated anchor.
4. Opening the menu or reloading the room while the simulator is armed cancels that pending test input.
5. The separated renderer shows the same route/arrows/ring and clears them on cancel, selection change or loss of tracking.
6. Failure/state transitions appear in the diagnostics file. No new camera detector, clinical outcome or research novelty is established by this integration checkpoint.

## Native application endpoint — v5

The newer combined native room application runs in M1_LiveRoomScan. It creates NativeItemWorkflow at startup, available through LiveRoomScan.Items, implementing IObjectObservationSink, ISpatialObservationRegistry and IApplicationStateProvider. Use the typed observation contract above, but obtain its context from that workflow rather than the older SpatialApplicationHub instance. There is no real detector in this milestone. Existing shared/master demo scenes remain available as earlier checkpoints.

See Assets/_Project/Dinith_Navigation/Native Item Finding.md for native menu/controller actions, ready/commit semantics, data compatibility and the full device test. Manual item registration uses a controller-side-grip action after choosing Register; A opens Meta capture and B exports the native room. UI and vision teammates should integrate against the new workflow after startup rather than depend on the old manual-demo UI component.
# Dynamic navigation v7 integration note

The combined M1_LiveRoomScan application keeps both scan methods. LiveScanNavigation retains a destination and RouteOwner while temporarily blocked; RouteAvailable becomes false and Status explains the blockage. NativeItemWorkflow remains in Searching and forwards that message. Dynamic updates can choose another standing point for the same saved item, so UI consumers should read StandingPosition again rather than caching the original endpoint. Room/source/selection changes still cancel guidance. No item record or anchor moves because furniture blocks the path.

Presentation can read LiveSummary and LiveMeasurements and control ToggleLive, ToggleLiveOverlay and ResetLiveMeasurements. Monitoring, visualisation and manual block reset are separate actions. Do not automatically clear live blocks when hiding their overlay or turning monitoring off. Test/source/revision-grouped diagnostics describe observed confirmation and route response, not physical ground truth. The original observation sink, spatial registry and application-state contracts are unchanged. See Assets/_Project/Dinith_Navigation/Dynamic Routing.md for the practical test protocol and limits.

## Baseline furniture corrections v8

FurnitureSummary and FurnitureUpdatesEnabled describe the new verifier; ToggleFurnitureUpdates resets verification and restores original furniture restrictions before starting fresh when enabled. Blue floor tiles are navigation corrections, while the cyan source mesh remains historical geometry. Baseline and live obstacle changes share obstacleRevision and the existing route cancellation guards. Saved item records and shared observation/UI interfaces are unchanged.

The allowlist is TABLE/COUCH/BED semantic furniture. Walls, unknown or mixed-label surfaces, overlaps with permanent geometry and missing floor support stay protected. App scans use their own mesh/floor plus matching Meta labels for eligibility, not Meta floor substitution. Corrections are session-local and never alter the Meta room. See Assets/_Project/Dinith_Navigation/Moved Furniture Updates.md for the test and limits; do not present every blocked cell as automatically removable or current source meshes as reconstructed after a move.

## Evaluation workflow v9

LiveRoomScan.Evaluation controls the optional comparison workflow. Respect IsLocked while preparing or running: do not change scan sources, margins, target observations, routing settings or maps mid-trial. Unexpected room/tracking/configuration changes abort a trial; saved item records remain unchanged. Normal navigation preferences are restored after each outcome. Evaluation overrides are temporary and do not write routing preferences.

NavigationEvaluation exposes mode/scenario cycling, StartTrial, markers, reports, outcomes and CSV export. NavigationTrial and NavigationTrialStore are pure data/measurement/storage classes. NavigationTrialSignal carries observed navigation changes without replacing the shared item interfaces. Results distinguish manually reported success/errors and approximate change references from automatic observations. Event/summary exports include trial/session/source/configuration identifiers; no participant name or ground-truth recognition is inferred. See Evaluation Lab.md before using these development metrics as research results.

## Connected-room journeys v10

LiveRoomScan.RoomNavigation owns doorway teaching and inter-room Find intent. NativeItemWorkflow.Find dispatches a saved item in another room to it, then resumes normal local Find on arrival. A remote item snapshot still has HasLocatedPosition=false until its original room and anchor are tracked; do not expose cached coordinates as a located object. CurrentState can be Searching while the room journey is active. Normal room changes update the observation frame token without cancelling a remote intent; actual tracking loss, recenter, selection/save changes or user cancellation stop it.

SDK room navigation caches use each floor anchor's local coordinates and structural layout stamp. They are reference models for graph planning, not live remote-room sensor observations. Show only the current checked floor leg; crossing the explicitly taught doorway is a separate step while the next room locates. Cross-room Meta guidance and ordinary app-local scanning are distinct modes. Current evaluation trials remain single-room. Read Connected Rooms.md before extending the graph or interpreting room labels as user-supplied room names.

## Home workspace v11

The default HomeScanner builds one continuous same-floor workspace independently of MRUK room IDs. Use LiveRoomScan.Home.Active to distinguish it from the previous pipeline. Home owns its geometry, sparse walking graph and three home-local item records tied to a saved reference. Existing NativeItemWorkflow observation interfaces and trial metrics still describe the previous native-room pipeline; do not interpret them as Home state. A shared-interface Home adapter and multi-anchor drift correction are future integration tasks. Previous scan tools remain available explicitly, and their files are untouched. Home Scanning.md documents the capture, save/recovery behaviour and practical limits.
## Home navigation and live updates v12

HomeMap is a partial sparse-map class split between geometry/persistence, HomeLiveUpdates and HomeRoutePlanner. Live confirmation and floor-supported multi-height clearing are now active in the default Home mode, independent of a current Find action. Dynamic occupancy and reopened-state colors are magenta and blue. Floors and route checks use 7.5 cm cells; A* and checked smoothing share the configured explicit margin. Use BlockingRevision for obstacle-driven route validity checks and Revision for broader map changes. Do not convert missing depth into free space or count multiple samples from one acquisition as independent confirmations. Schema 2 checkpoints preserve the home anchor/items; v1 migration re-rasterizes the saved mesh instead of splitting coarse green cells. The shared native-room observation/trial adapters remain separate from Home state.