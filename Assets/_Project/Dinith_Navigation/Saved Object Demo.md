# Saved object checkpoint

## Create and build

Wait for Unity to compile, then choose **SpatialSense > Create Saved Object Demo**. Save your work if prompted. This copies the saved walking-route scene to `M1_SavedObjectDemo`, adds persistent memory, applies the 0.25 m navigation radius, saves the scene, and selects it for building. Earlier scenes remain available. Repeating the command configures the existing saved-object scene.

Build an Android APK in a fresh folder, for example `Builds/Android/SavedObject/SpatialSense_SavedObject.apk`. The lower panel should say **SpatialSense | Saved object demo**.

## First use

1. Wait for the room scan to load.
2. Hold the right controller beside your keys and press **A**.
3. The app creates a spatial anchor and saves it through the Meta SDK, then commits its local record. Wait until **Saved location ready** appears before closing the app.
4. Press **B** to find the keys using the existing route and standing-position ring.
5. Near the ring, aim at the object marker and press the right trigger to manually confirm Found. Thumbstick click still cancels a search.

## Restart test

1. Leave the real keys in place and close the app fully.
2. Reopen the installed application without uninstalling or clearing its data.
3. Let the room load. The app reads the saved anchor ID and shows **Locating saved position** while the SDK localizes and tracks it.
4. Only after a tracked pose is available should the marker appear and Find become available.
5. Look around in good lighting if localization takes time. Press **B** to retry after a failure. **A** explicitly registers and saves a replacement.

Existing left-controller controls remain: X toggles room outlines; Y reloads the room; index trigger toggles the walking-map preview.

## Failure behaviour

- The saved record contains a version, object name, anchor UUID, room UUID and timestamp. It contains no raw Unity world coordinates. The object point is the spatial anchor's origin.
- Scene access and a ready room are required. A saved point associated with a different room scan is not shown as a valid point in the current room.
- Loading uses the SDK's unbound-anchor API, localization and tracked-pose checks before binding a new component. A missing or untracked pose never falls back to the world origin.
- Marker position follows the live anchor transform. If tracking is lost, the marker and route are withheld. When tracking returns the point becomes registered again; press B to resume finding.
- Replacing the point saves the new native anchor first. Only a successful local metadata commit activates it. A failed replacement keeps the previous record, and B can explicitly restore that record. Old-anchor cleanup happens only after the replacement commit.
- Local metadata uses a flushed staging file and atomic replacement. The previous metadata file may exist as a diagnostic `.bak`; it is not a second supported object slot, and its anchor may have been cleaned up after replacement.
- A corrupt record produces an explanatory message. A registers a new record; the old file is not overwritten until a new save succeeds.
- Room reload clears the active search, but persistent memory can restore the tracked object point when the same room is ready again. Actual object movement is not detected; re-register with A when the object moves.
- Anchor operations that take more than 30 seconds show a slow-operation message. SDK load/save calls are not forcibly cancelled mid-operation; restart the app if an operation remains stuck.

This is local persistence for one named object on one headset. It does not share anchors, upload object data, recognize keys or establish zero drift. The nearby standing-point and route behaviour remains based on the saved room model.

## Verification

Computer checks: `Tools/Test-AnchorRecordStore.ps1` validates record fields and file replacement failure cases. `Tools/Test-RouteMath.ps1` covers route geometry. Editor and Android code-path compilation is checked separately.

Required headset checks: first save; full app restart and restore; moved-head tracking updates; temporary anchor tracking loss; B retry; a different room scan; replacement save; room reload; and repeated replacement without stale markers. Native persistence and physical alignment cannot be established by source compilation or desktop file tests alone.
