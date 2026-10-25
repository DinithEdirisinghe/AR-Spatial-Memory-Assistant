# Room understanding checkpoint

This checkpoint extends the working manual-object demo with the saved Quest room model. It draws outlines, reports load failures and keeps object registration unavailable until the current room has a floor. It does not create a walking path yet.

## Create and build

1. Wait for Unity to import the scripts. Choose **SpatialSense > Create Room Understanding Demo**.
2. Save existing work if asked. The command copies the saved manual-object scene to `M1_RoomUnderstandingDemo`, configures it, saves it, and makes it the only enabled build scene. Existing source scenes remain available. Running the command again configures the existing room-demo scene.
3. Build an Android APK in a fresh output folder, for example `Builds/Android/RoomDemo/SpatialSense_RoomDemo.apk`.
4. Install and launch on Quest. Approve room/spatial-data access if prompted.

The project already declares `com.oculus.permission.USE_SCENE` in its Android manifest and has Scene Support enabled. The room demo handles the runtime permission request itself. OVRManager's automatic startup scene-permission request is disabled in this new scene to avoid competing prompts; do not enable it with Apply All for this demo.

## What to see

- The upper panel first reports loading or asks for room access.
- A ready room shows a count of outlined surfaces. The current room's floor is cyan, walls and openings are white, and other mapped objects are amber.
- The existing lower panel still operates My keys. It waits for room readiness before accepting registration.
- Outlines represent the saved room scan, including furniture only if that scan contains it. They are not continuous detection of every object or a live obstacle map.

## Controls

Left controller:

- **X:** hide/show outlines.
- **Y:** retry loading. If the previous attempt found no room, this also requests the headset's room-setup flow. Complete that flow. If unavailable or cancelled, use Space Setup in headset settings and press Y again.

Right controller, after the room is ready:

- **A:** register My keys at the controller position.
- **B:** find the registered point.
- **Trigger while pointing at the marker within one metre:** user confirms Found.
- **Thumbstick click:** cancel the search.

Reloading the room or changing/losing the current room clears object registration. Register again after the room is ready. The object position is still for the current application session only.

## Headset verification

1. With a completed room scan, launch and approve permission. Verify the status reaches ready and the floor outline matches the physical floor.
2. Compare walls, desk and other scanned furniture with the real room. Check alignment at several points while moving your head. Do not proceed to navigation if alignment is wrong.
3. Press X twice: outlines should hide and return, while the object workflow remains available.
4. Register an object, find and confirm it. Check that the outlines do not block the controller pointer; they have no physics colliders.
5. Press Y after registration: the point must clear and the room must load again without duplicate outlines.
6. If permission is denied, the room panel should explain the problem and registration must stay unavailable. Approve scene/spatial-data access in the headset app permissions and press Y to retry.
7. If there is no saved room scan, the app must explain that setup is needed, without pretending to have loaded a room. Use Y or headset Space Setup, then retry.
8. With room data loaded but the current room not located, look around in good lighting. The app must wait rather than drawing an unrelated loaded room as the current one.
9. Run the original manual-object scene separately if needed: it should still work without requiring a room.

## Implementation boundaries

The scene contains one MRUK instance with device data and automatic startup loading disabled. The room demo requests permission, then calls MRUK's documented device-loading API. The first load does not automatically launch setup; a subsequent explicit Y action can request it after a missing-room result. Loading is guarded against repeated concurrent requests.

Outline lines use anchor-local plane boundaries or volume bounds and follow updated anchor poses. They add no colliders or solid room meshes. The demo listens for room-data changes and redraws as needed. A missing floor, failed permission or unresolved current room keeps registration unavailable.

This does not establish obstacle avoidance, persistent object positions, safe navigation or research novelty. The next checkpoint is a built-in navigation mesh and a reachable standing position near the target. Test that separately after room alignment is confirmed.
