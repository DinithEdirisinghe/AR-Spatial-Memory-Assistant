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
