# Dynamic routing v7

Build the existing **M1_LiveRoomScan** scene. Its title is now **SpatialSense | Dynamic routing v7**. No scene recreation or Inspector assignments are required. Both Meta room scanning and App live scanning remain available under **Scan > Method**.

## What this build adds

The app checks live depth while using the selected scan's walking map. New obstacles appear as magenta floor tiles. Once repeated observations confirm a change, the path updates immediately. If no route exists, the blue line disappears and a blocked message appears. The destination remains selected, so observing an obstruction being removed can restore guidance without registering or finding the item again.

If a new obstacle covers an item's original standing point, the app searches for a different reachable point beside the same saved item. It does not move the saved item marker or edit the stored location.

Dynamic checks start automatically after a usable walking map is built, unless you previously switched them off. **Dynamic > Dynamic routing** changes that setting, which is remembered between launches. Turning checks off stops new observations; it keeps already confirmed obstacles. This avoids treating a monitoring pause as proof that furniture disappeared.

The **Dynamic** tab contains an obstacle-highlight toggle, a test marker that resets counters, the existing extra-margin setting, and a separately confirmed reset of live blocks. Hiding highlights does not change the route or obstacle data. Resetting live blocks leaves baseline furniture untouched. Rebuilding a map or switching scan methods starts a fresh live layer.

## One headset test

1. Load a room and wait for its floor map. Leave a clear corridor in the baseline scan.
2. Register an item at the far end and choose Find. Confirm that the blue path reaches it normally.
3. Open Dynamic and check that dynamic routing is ON. Choose **Mark test / reset counters**, then close the menu.
4. Add a stationary chair or box across the path after the baseline was captured. Look toward it from about 1–2 m away. Magenta tiles should appear and the path should detour if there is room.
5. Test a completely blocked corridor. The blue path should disappear and the app should report that the route is blocked. The item marker should remain in place.
6. Remove the added obstruction and look at the space it occupied. The system needs actual valid depth rays through the old obstacle volume to confirm removal; looking away or receiving no depth is insufficient. Once confirmed clear, guidance should recover.
7. Repeat with an obstruction covering the destination ring. For an item search, the app should find another reachable approach if available.
8. Repeat with the other scan method. Set the app scan's floor height and rebuild its map if required. Use Mark test to separate the runs.

Test the path while stationary first so head movement does not complicate comparisons. For removal tests, the sensor must see a real surface beyond the old obstacle; a missing depth result cannot establish that the space is empty.

## What the numbers mean

The Dynamic tab shows path changes during live map updates, transitions to a blocked route, recoveries, the last response time, and the most recent depth-sampling time and hit count. “New blocks” counts confirmed sampled floor cells, not separate chairs or other objects. Pending points are observations still awaiting confirmation.

The app records bounded local JSONL diagnostics in `Application.persistentDataPath/diagnostics/`. Existing diagnostics retain a limited number of session files. Events include:

- `dynamic_test_marked`: a new test ID and reset counters; this is a manual test marker, not ground truth for the exact moment furniture moved.
- `live_obstacle_change`: source method, obstacle revision, added/cleared evidence cells, valid rays, sampling time and observation-to-confirmation delay.
- `route_blocked` / `route_recovered`: route availability transitions.
- `live_route_response` / `live_route_no_path`: elapsed time from confirmed live change through map update and route response, including an asynchronous endpoint search when needed.
- Enable/disable and manual layer-reset events distinguish intervention from automatic recovery.

The event detail is a JSON string inside the existing diagnostics entry. Source method, test ID, obstacle revision and route generation allow related events to be grouped. A timing value of -1 means unavailable. Confirmation delay starts at the first app observation of that evidence cell; it excludes the time before the headset observes the object and the headset's internal depth latency. The reported batch value is the longest confirmation delay among newly confirmed cells. Route-change counters may also reflect wearer movement during a live update. These are development measurements, not yet a controlled user study or an end-to-end obstacle detection benchmark.

## How the checks work

Sampling rotates across a 12 by 8 field in front of the headset, with targeted rechecks of remembered evidence. Batches run about every 80 ms, up to 24 queries with a cooperative 3 ms ray-query budget. A single expensive SDK call can exceed that budget. Sampling resumes from its previous position so a budget overrun does not permanently omit part of the field. Valid samples are limited to 0.4–3.5 m from the headset; obstacle hits must be 0.2–1.9 m above the selected floor.

Two positive observation batches confirm an obstacle. Duplicate rays in one batch cannot provide temporal confirmation. Nearby hits at a similar height can corroborate a point despite small head movements. Unconfirmed noise expires after 20 uncorroborated batches. Confirmed evidence does not expire merely because it is out of view. Three positive batches through its old height/volume clear it; a new obstacle hit takes precedence over clearing in the same batch.

Confirmed samples within 22.5 cm at similar heights can be connected conservatively to prevent a path slipping between sparse hits on one furniture surface. This does not enlarge an isolated hit into a whole chair. The existing extra margin remains separately adjustable. Evidence is bounded to 1,024 cells; the HUD reports capacity instead of silently claiming complete monitoring.

Only the small changed-cell overlay is rebuilt for live feedback. The larger baseline floor meshes remain allocated. Item endpoint searches run on a snapshot outside the main Unity thread and discard stale results when the room, source, selection or obstacle revision changes.

## Current limits

This is dynamic navigation on a floor grid, not Unity NavMesh rebuilding. It observes a limited forward view and may need a deliberate look around furniture. It can add obstacles to the baseline and remove those live additions after positive evidence. **It does not automatically erase furniture already stored in the original Meta/app scan.** Rescan that baseline if such furniture moves. It does not reconstruct hidden sides or a complete new furniture mesh from sparse depth hits.

No sensor hit or sparse floor route establishes whole-body clearance. The selected thin extra margin remains a prototype setting. Headset performance, detection coverage and real removal reliability still require physical testing.

## Checks included

`Tools/Test-DynamicNavigation.ps1` exercises a detour around a new obstacle, continuity between close confirmed samples, a fully blocked corridor and its recovery, missing-data preservation, confirmation timing/counters, head-motion jitter, duplicate-ray rejection, an alternate item endpoint, unconfirmed-noise expiry and preservation of baseline walls. Existing navigation, native-room and item-standing tests remain applicable. Editor and Android C# compilation check API/type integration; they do not replace the headset checklist above.
