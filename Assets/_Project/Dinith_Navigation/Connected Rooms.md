# Connected rooms v10

**Current patch: Connected rooms v10.1.** The in-app MRUK depth scanner now retains small completed surface cells under its time budget instead of discarding a whole view and repeatedly falling back to Fast. It continues an unfinished view across capture calls, with every triangle sampled entirely within one call. The source selector still distinguishes App live scan (MRUK depth) from Meta room scan (Quest's full-screen setup). The latter action is now labeled **Open Meta room setup**.

Build the existing **M1_LiveRoomScan** scene once. The title is now **SpatialSense | Connected rooms v10.1**, with a **Rooms** tab. Existing saved item files and anchors are kept; you do not need to register working saved items again solely for this upgrade.

## Why this was needed

The previous application used one active room's floor map and treated other-room item anchors as unavailable. This version keeps a collection of room navigation models and a graph of explicitly taught doorway connections. It guides each room separately, rather than merging unrelated room coordinates or drawing a straight line through their walls.

An item's saved room UUID already exists in its record. Find uses that identity to travel toward the original room. The final item position is still obtained from its tracked spatial anchor after that room is located. A cached map is not treated as a tracked item position.

## Teach your first doorway once

1. Choose **Meta room scan** and load the saved Meta rooms. Make sure both physical rooms have been scanned on the headset. The Rooms panel shows the current room label and remembered navigation-map count.
2. In the first room, stand on an **ordinary green** floor square just inside the doorway. Stay clear of furniture and the wall edge. Choose **Rooms > Link doorway: side 1**.
3. Walk directly through that actual doorway. Keep the headset tracking and stay on the same floor level. Do not use Quest recenter or put the headset to sleep.
4. In the second room, wait for it and its walking map to locate. Stand on ordinary green floor just inside that side, open Rooms and choose **Finish doorway: side 2**.
5. Wait for the saved confirmation. That connection works in either direction and is retained after reopening the app.

The two points must be within 3 m of one another in the session's raw tracking coordinates, with no more than 4 m of walking, a 45 s teaching window and at most 15 cm floor-height difference. Tracking loss, a jump or crossing a third identified room invalidates teaching. This is for nearby, same-level doorways, not stairs, teleports or links between distant buildings.

The application generates persistent labels such as Room 1 and Room 2 from room identities. These may differ from the informal room numbers you have been using; check the current label on each side. For three or more rooms, repeat teaching for each physical doorway in the chain.

If the next saved room is not loaded automatically, use **Rooms > Load saved Meta rooms** while standing there, then wait for the map. A data reload can continue teaching if raw tracking remains stable; a real recenter/tracking interruption cancels it. If the teaching window expires, cancel and repeat after the rooms finish loading.

## Find an item from another room

1. Register or restore the item normally in its original room. Its slot will later say **Saved in Room N** when you move elsewhere.
2. Walk to another connected room and select that item under Items or Rooms. Choose **Find selected item**.
3. Follow the blue floor path to the linked doorway. Near the endpoint, the status asks you to check that the doorway is open and cross into the named room.
4. During the threshold crossing, Quest may briefly report no current room. The application hides unavailable floor guidance, keeps the Find intent and waits for the next room to locate. It does not invent floor geometry across that gap.
5. In the next room, guidance continues toward the next linked doorway, or starts the normal item-anchor localization and final item route if this is the destination room.
6. Confirm Found it as before when you identify the actual item.

If a doorway approach is blocked, the current room's live obstacle checks can block its floor route and the room planner can choose another linked route. If the doorway itself is closed or unsuitable beyond the endpoint, use **Rooms > Doorway blocked: try another**. That excludes the connection for this Find request, without deleting it permanently. The app does not detect or open doors automatically.

If there is no alternative, the application reports no connected/walkable route. A lost target anchor after arrival still needs localization/retry; being in the right room alone is not proof of the item's position. If Quest cannot locate the next room after the waiting interval, the journey stops and asks you to load/locate that room and choose Find again. Saved item data remains unchanged.

## Room maps and doorway data

Room models and links are stored in `Application.persistentDataPath/room-navigation/`:

- `room-<room UUID>.json`: semantic floor/obstacle navigation geometry and its floor coordinate transform, not a replacement export of the complete native global mesh.
- `connections-v1.json`: room aliases and doorway-side points expressed in each floor anchor's local coordinates.

The SDK can still display the current room's native mesh as before. Remembering several navigation models does not require displaying unlocalized remote meshes in physical space. Only the active room's checked floor route is drawn.

SDK-loaded models refresh the cache. Previously cached rooms can remain known when another room is active or after restarting, but their future legs are reference geometry until reached and checked locally. Live obstacle/furniture corrections remain session/current-map observations; this feature does not claim that remote rooms are being scanned live.

Doorway links are tied to each room ID, floor-anchor ID and a structural layout stamp based on its floor, walls and doorframes. A changed/rescanned structure can invalidate a link; teach it again rather than reuse an old wall crossing. Replacing a room identity can also require re-registering items under the new room model. Old data is not silently reassigned to a different room.

Use the Rooms tab to cycle saved connections and confirm forgetting only the selected link. This leaves saved item anchors and room scans intact. Room-map archives use the selected margin when rebuilt. The room graph checks connected intraroom floor paths, so an intermediate room with an impassable wall cannot be skipped merely because its room ID connects to two doors.

## Current scope

**Cross-room guidance currently uses Meta room maps.** The app's own scan remains available for local scanning/navigation and the existing comparisons; it does not silently substitute Meta geometry for an app-scan route. Collecting independent app-owned meshes per room is a separate extension.

The v9 Tests workflow remains a single-room evaluation protocol. It refuses to start during a room journey/doorway capture, and a room change during a timed single-room trial remains an abort. Use ordinary navigation for the multi-room pilot; do not mix its results into the existing single-room trial comparisons without a separate protocol.

This is route planning on room grids plus a doorway graph, not an automatically reconstructed whole-building NavMesh. Doorways are taught by a user walking the passage; same-looking or overlapping room meshes do not automatically establish a connection.

Doorway costs use checked grid paths and the taught crossing distance to reach the destination room. The final item approach is planned after its anchor localizes there; this is not a guarantee of the globally shortest complete path to the item.

## Headset checklist after one rebuild

1. Teach one doorway and find an item in the opposite room. Test both travel directions.
2. Add a third room and verify an intermediate doorway handoff.
3. Close/reopen, load the rooms, and verify saved links and item finding still work.
4. Block a doorway approach or mark its door unsuitable. Check rerouting when an alternative exists, and a no-route message otherwise.
5. Check that missing room tracking hides the line and that tracking loss/recenter cancels rather than using a stale pose.
6. Verify an ordinary same-room item search and a single-room evaluation trial still work.

`Tools/Test-MultiRoom.ps1` checks independent/overlapping coordinate frames without guessed links, two-room/reverse/multi-hop routes, floor/layout mismatches, blocked approaches and intermediate rooms, distance-aware alternatives, floor-plane transforms, cancellation, tracked teaching constraints and persistent room/link round trips. Source compilation verifies Editor and Android APIs. Physical room transitions, SDK localization, saved link alignment and menu usability still require the headset tests above.
