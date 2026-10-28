# Find my items — current v10

Current build title: **SpatialSense | Connected rooms v10.1**. See [Connected Rooms.md](Connected%20Rooms.md) for doorway teaching and finding items across rooms, [Evaluation Lab.md](Evaluation%20Lab.md) for single-room comparison trials and CSV results, and [Moved Furniture Updates.md](Moved%20Furniture%20Updates.md) for old furniture corrections. Both scan methods and existing item memory remain included. The v5 notes below describe the original combined workflow.

This milestone combines the native Meta room scanner, room navigation and persistent Keys/Wallet/Remote locations in the existing `M1_LiveRoomScan` scene. The app creates the additional components automatically at startup. No scene recreation, manual Inspector assignments or new package installation is required for an existing working native capture scene.

## One build and the main headset test

1. Build the existing `M1_LiveRoomScan` scene once. The title should be **SpatialSense | Connected rooms v10.1**.
2. On Scan, complete **Scan room with Meta**, or explicitly **Load previous Meta room**. Wait for the room to locate and its walking map to finish building.
3. Open **Items**, select **Keys**, and choose **Register / update**. The menu closes.
4. Hold the right controller beside the real keys. Release and squeeze the **right side grip**. This is not the index trigger or A/B. Wait for the saved confirmation.
5. Move away. Open Items and choose **Find selected item**. The app locates the item's anchor if needed and chooses a reachable floor standing point near it. Follow the blue route to the ring; the labelled sphere shows the last saved object location.
6. Near the ring/item, open Items and select **Found it**. The button is enabled only near that standing point and last saved position. Confirmation is manual; it records a user event and does not rewrite the saved position or create a new camera sighting.
7. Repeat registration and Find for Wallet and Remote. Confirm the other item records stay unchanged when you replace one location.
8. Close and reopen the app. Choose **Load previous Meta room**, then select the saved item and Find. The selected slot restores its native spatial anchor; it is not placed using a raw coordinate from the previous Unity session.
9. Test **Cancel item action** and right-thumbstick cancellation while registering/planning/searching. Cancellation keeps the stored record. An already running native save/restore/delete operation finishes; cancellation does not pretend to undo a completed save.
10. Test **Forget selected item** on just one slot. Press its confirmation within five seconds. After reopening, that slot should stay removed while the other two still restore.

The primary native controls remain: A opens Meta Space Setup; B exports a room/mesh copy; left stick opens/closes the menu; right pointer/index trigger selects menu buttons; X cycles mesh/map views; Y repositions the menu. Item registration uses the side grip after explicitly choosing Register. A/B do not register items in this combined scene.

## Item states and markers

The Items page lists each slot's state, such as Not saved, Saved, Locating, Ready, Waiting for room or Unavailable in this room. Only the selected item's marker is displayed. It is hidden until both the correct room and saved anchor are located, while an anchor operation is busy, while manual registration is armed, or when tracking/visibility is not trusted.

Yellow means a saved location is ready; cyan indicates an active item search; green indicates the user's Found confirmation for this session. The label always says **Last saved location**. A restored anchor is not evidence that the real item has stayed there. Registration is currently manual. Moving a real item requires a new registration or a later accepted recognition observation.

Changing selected items clears old guidance. Reopening the menu cancels an armed controller registration so a later grip cannot accidentally overwrite a record. Room/frame changes and loss of item localization cancel search guidance; a native record is not deleted by those events. Updating the selected item's location invalidates an old item search. Native room capture and JSON/OBJ exports remain separate from item storage.

## How item routing differs from a floor destination

An item's position can be on a table or inside the furniture's footprint. Find therefore does not project it directly onto the floor underneath. It runs a search over the current walking grid and considers only reachable green cells roughly 0.25–1.1 m horizontally from the item. Its score combines walking effort with distance to the item. It rejects unreachable nearby cells, respects red/orange cells, and does not cut diagonal blocked corners.

Planning uses an independent map snapshot on a worker thread. Cancellation stops the search. A changed room, different request or changed live-obstacle version prevents a stale result from being installed. The chosen endpoint is then updated using ordinary routing from the user's current position. A blocked route clears its line and reports the reason; it does not draw a straight fallback through furniture.

Found confirmation requires an active item destination, a located item anchor, a located room, and horizontal proximity to both the standing point (65 cm) and the saved object point (1.35 m). It intentionally does not use full head-to-object 3D distance, which would reject a floor item just because the user's head is higher. These are prototype interaction thresholds, not physical reach/safety guarantees or a measured recognition result. A user may confirm an item they actually see nearby even if the remaining route line is temporarily blocked.

## Storage, replacement and room identity

This reuses `PersistentObjectMemory`, `SavedObjectRecord`, `SavedObjectCatalog` and the atomic record writer. The existing legacy filenames remain:

- `spatialsense-my-keys-v1.json`
- `spatialsense-my-wallet-v1.json`
- `spatialsense-my-remote-v1.json`

Each holds that slot's native anchor UUID, room UUID, timestamps and provenance. The record is replaced only after native anchor persistence succeeds. A failed replacement keeps the previous record. Forget writes a removal marker before native erasure so an interrupted deletion cannot resurrect the slot. Failed native erasure can be retried through Forget.

Old records can be used if their room UUID matches the current Meta room. A fresh Space Setup may create a different room identity. In that case the app reports Unavailable in this room and asks you to use the original model or register again; it does not silently associate the old location with a different room. For the restart/restore test, load the previous room rather than doing a new scan first. Clearing app data, uninstalling, changing the application identifier or deleting native spatial data can affect restore availability.

## Teammate connections

`NativeItemWorkflow` is created at startup and available as `LiveRoomScan.Items`. It implements the existing `IObjectObservationSink`, `ISpatialObservationRegistry` and `IApplicationStateProvider` contracts. Runtime access should occur after this application's startup; these are not separately deployed services or separate Unity assemblies.

For a real detector, obtain `TryGetObservationContext` at image capture, retain that room/frame context with the capture time and pose, and submit an `ObjectObservation` on the Unity main thread after inference. IDs are `keys`, `wallet`, `remote`. Use UTC capture time and world metres; origin `Vision` is for a real detector, not the manual controller flow. Class detection alone does not prove which personal item was seen.

The existing policy checks IDs, finite coordinates, identity/position confidence, UTC/time bounds, room/frame tokens and UUIDs. The workflow also rejects duplicate observation IDs, out-of-room points, old non-manual sightings, busy memory and input while manual placement is armed. The current numeric confidence thresholds are prototype choices, not calibrated recognition accuracy. Submitting true starts a save; it is not a commit acknowledgment. Registry events/snapshots establish when an anchored location becomes available. Failures require a fresh observation ID when retrying. Background updates do not select another item or start a search automatically.

Stored observations retain their original timestamps and source on restore. Snapshot positions are usable only when HasLocatedPosition is true; coordinate-query methods fail when unavailable. Do not resubmit a stored anchor as though it were a new camera observation. UI teammates can consume CurrentState/StateChanged and replace presentation while preserving those distinctions.

The original controller/manual demo adapter is preserved for the earlier demo scenes. The shared memory component now selects either the old RoomUnderstandingDemo context or the new MetaRoomSource context. The native Items UI uses its own controller instructions, not the old demo's A/B shortcuts.

## Diagnostics and validation

The existing bounded ApplicationDiagnostics logger records item states, anchor commits, search starts/failures, cancellations and manual Found confirmations locally. It does not record camera images or upload logs. Confirmation is user feedback, not independent ground truth.

Checks for this milestone:

- `Tools/Test-ItemStandingRoute.ps1`: tabletop/floor targets, route points avoiding furniture, unreachable and off-map targets, blocked inspection neighbourhoods, map snapshot isolation and cancellation.
- `Tools/Test-ObservationPolicy.ps1`: known IDs, confidence/time/coordinate rules and stale room/frame rejection.
- `Tools/Test-ObjectCatalog.ps1`: unique slot files, legacy Keys compatibility and update/forget isolation.
- `Tools/Test-AnchorRecordStore.ps1`: atomic replacement, interrupted-write handling and failed-replacement preservation.
- Native room navigation and grid regression suites remain applicable.
- Editor/runtime and Android C# source compilation verify both the new adapter and references used by earlier scenes.

These checks do not establish real Quest anchor reliability, body clearance, UI readability, native save/restore success or a completed user study. The combined device checklist above remains required. In particular test tracking loss, a changed room scan, saving failure, old saved data, selecting another item during planning, an item with no reachable standing space, a failed restore, cancellation, near-item confirmation and forgetting only one slot.
# v6: complete guidance and scan comparison

The old item-standing score favoured a shorter walk and could stop around 1.1 m from the item. The planner now searches connected walkable cells, finds the closest reachable item neighbourhood, and uses walking distance only to choose between cells within half a grid cell of that closest distance. Open-floor item endpoints are roughly 25–34 cm away. Furniture still blocks walking: for an item on a table, walk to its reachable edge and look at the saved-location marker. The line is a floor walking route, not a line through the table to the object.

## Choose a scan method without rebuilding

Open **Scan > Method** to switch between **Meta room scan** and **App live scan**. Both meshes stay in memory when switching. Each method builds its own walking map in its own coordinate frame; switching clears guidance and rebuilds from the selected method. The two meshes are not mixed to make the app scan look more complete.

- **Meta room scan:** Scan room with Meta opens the native capture flow. Load previous Meta room explicitly loads the existing model. Save native mesh + room exports its copy. The walking floor and furniture come from Meta's room model.
- **App live scan:** Start/continue captures environment depth patches inside our app. Look around the floor and furniture, pause capture, then choose Set app floor height. Aim down at actual floor and press right grip. Use Route > Rebuild walking map. Only observed app-mesh floor contributes walkable cells. App tools contains detail, coverage and OBJ export controls. Save app mesh + floor retains the anchored mesh and selected floor height; Load saved app scan restores it after reopening.

App scanning and floor-destination routes work independently of a loaded Meta room. **Saved item memory still needs a Meta room identity**, for either walking-map method. Use **Options > Load previous Meta room** before registering/finding items if it has not been loaded. This identifies the room for saved object anchors; it does not supply floor geometry to the app-scan map. Keep the same Meta room when comparing both methods so existing item records remain comparable. A newly captured Meta room may have a new identity and require registering items again.

## Headset check after one rebuild

1. Load the same Meta room, register an item, move several metres away and Find. Check that the blue line reaches the item neighbourhood instead of ending around a metre early.
2. Repeat with an item on a table: route ends beside furniture, and the saved-location marker remains at the item.
3. Switch to App live scan, capture or load a saved app mesh, set its floor if needed, and rebuild its map. Find the same item using this map.
4. Switch back to Meta room scan. Its mesh should remain available and its map should use Meta geometry again. Save/load buttons write different files for the two methods.

The app-owned capture remains a sparse depth-patch reconstruction. This update restores it for comparison; it does not make it equivalent in completeness to Meta's native capture. Physical headset testing of these changes is still required.
