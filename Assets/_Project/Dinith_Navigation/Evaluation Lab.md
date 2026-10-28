# Evaluation lab v9

**Current application is Connected rooms v10.** The single-room evaluation workflow below is retained. See [Connected Rooms.md](Connected%20Rooms.md) for ordinary cross-room item guidance. Multi-room journeys are outside this initial single-room evaluation protocol; do not start a trial while teaching or following a room journey.

Build **M1_LiveRoomScan** once. The panel title should be **SpatialSense | Evaluation lab v9**. The menu now has two rows of readable tabs, including **Tests**. Scanning, item memory and furniture updates remain available.

## First trial

1. Load the matching Meta room for item memory. Choose Meta room scan or App live scan, capture/load that scan, and set the app floor if required.
2. Register your test item under Items. If restoring an older item, choose Find once or use **Tests > Locate saved item**, then wait for its anchor to locate.
3. Open Tests. Choose a navigation mode, scenario and item. Restore the physical room to the same starting arrangement and stand at your chosen start point.
4. Choose **Start trial**. The app builds a fresh walking map and resets observation evidence. Preparation is outside the timed trial. Timing starts when the map is ready, and Find starts automatically.
5. During the trial, open Tests for change markers, false/missed-obstacle reports and outcomes. The right-stick click aborts a trial; it does not silently turn it into a failed task.
6. Near the saved item, visually confirm the actual item and choose **Found item: success**. This is manual confirmation, not automatic recognition. Alternatively choose **End: could not finish** or **Abort trial**.
7. Choose **Results / export CSV > Export CSV files**. Wait for the completion message before closing the app.

Navigation and scanning settings are locked in the menu during preparation and measurement. The app also checks source, room, map generation, extra margin, item anchor, target position and tracking. Changes that invalidate a comparison abort the timed trial instead of silently changing its condition. Live changes within the chosen mode remain allowed. Initially unavailable item routes retry automatically once per second so a later corrected corridor can become usable.

After a trial, normal navigation settings are restored and its map is rebuilt. Trial observations/corrections do not carry into the next trial. Saved item anchors and captured source scans are kept.

## Modes

| Mode | New live obstacles | Old classified furniture corrections |
| --- | --- | --- |
| Fixed map | Off | Off |
| Live obstacles | On | Off |
| Furniture updates | On | On |

Mode selection is temporary and does not overwrite your normal navigation preferences. Each mode starts with the same original selected scan, rather than inheriting another trial's learned blocks or reopened floor. Fixed-map guidance cannot respond to real obstacles added after scanning; report that as a missed obstacle rather than walking into it.

Furniture-updates trials require eligible classified furniture with captured floor support. Missing labels or unsupported floor prevent preparation and explain the problem; no timed trial is created. This prevents accidentally presenting an inactive furniture verifier as the full method.

## Scenarios

- **Clear path:** keep the room unchanged and find the item. This checks ordinary guidance and unnecessary restrictions.
- **Add obstacle:** baseline corridor is clear. Start the trial, mark Added/moved immediately before a helper adds an obstruction, then observe the result. Report a missed obstruction or false block when appropriate.
- **Remove obstacle:** baseline corridor is clear. During the trial, mark Added/moved and introduce a temporary obstruction. Give monitoring time to observe it, then mark Removed/cleared immediately before removing it. Fixed-map mode will not learn this new obstruction; that difference must be retained in the results.
- **Move scanned furniture:** source scan contains eligible furniture in its original location. Start, mark Added/moved immediately before moving it, then observe both the vacated space and the furniture's new location. Look down to verify exposed floor. If a second removal stage is included, mark it separately.

Use the same action sequence, start point, item, margin, source scan, highlight setting and observation directions across modes. The highlight setting is exported; keep it unchanged for a comparison. Reset the physical arrangement before every trial; the software cannot do that for you. For an initial pilot, run Clear path once for each of the three modes with each scanner: six trials. Then add the change scenarios and repetitions. Rotate condition order so practice is not always associated with the newest method.

Keep stationary during observation-latency tests. Use separate walking trials for task completion and route behavior where possible. Manually introduced waiting and menu interaction affect completion time; follow the same script across conditions.

## Manual reports and timing

Tests provides one Added/moved marker and one Removed/cleared marker per trial. Duplicate presses cannot overwrite them. These timestamps are manual references and include operator/action delay; they are not physical ground truth. If the app detected a change before its marker was pressed, the relevant post-marker delay may remain missing. Do not replace that with zero. Use independent video or another timestamped reference if precise physical onset latency is needed later.

False-obstacle and missed-obstacle buttons increment separate manual counters. They do not assert independent ground truth. Correct identification of the actual item is also manual. Automatic logs measure the navigation model's observations and responses.

## Files and download

Files stay in the headset application's persistent directory:

- `evaluation/trials/`: one JSON checkpoint per trial, including its event history.
- `evaluation/exports/summary-*.csv`: one row per recorded outcome.
- `evaluation/exports/events-*.csv`: timestamped events tied to trial IDs.
- `evaluation/exports/complete-*.txt`: names the matching completed CSV pair and lists any skipped records.

Exports are cumulative snapshots. Use one completed export pair for an analysis; combining repeated exports without deduplicating trial IDs will double-count results. CSVs use UTF-8 with a BOM, invariant decimal points and quoted text so Excel can open them consistently. Text that could be interpreted as a spreadsheet formula is escaped.

Connect the headset by USB, allow USB debugging, then run from the Unity project in Windows PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\Tools\Pull-EvaluationResults.ps1
```

The helper reads this project's Android package name and copies the evaluation folder to a new dated folder under **EvaluationResults**. It keeps the headset files. If ADB is elsewhere, pass `-AdbPath "C:\path\to\adb.exe"`; with multiple devices, pass `-DeviceSerial`. The downloaded default folder is excluded from Git. It includes raw trial records as well as exports so interrupted or invalid records remain inspectable.

## Meaning of the main columns

- **Outcome:** Success is user-confirmed near the saved item; Failed is an explicit inability to finish; Aborted is cancellation/interruption/configuration or tracking change. Keep these separate in analysis.
- **Elapsed seconds:** timed task from trial activation to outcome, including the initial checkpoint write, automatic Find/planning, menu interaction and waiting. Map preparation is excluded.
- **First route / route metres:** first observed available-route time, its floor-route length, and the last available-route length. Floor-route length ends at the standing point beside the item.
- **Head path metres:** horizontal headset movement sampled about 10 times/second; it includes leaning and is not an exact body-walking distance. Long gaps or large pose jumps are rejected and counted.
- **Route unavailable seconds:** integrated unavailable time, including initial planning. Transition sampling has approximately 0.1 s resolution.
- **No recent depth seconds:** time with no valid depth hit within the last three seconds while dynamic monitoring is required. It is unavailable, rather than zero, for fixed-map trials. A valid hit does not establish full scene coverage.
- **Manual marker-to-confirmation / recovery:** delay from a marker to the first matching map change or route recovery after it. Another observed change can influence these counters; the log does not independently associate every hit with the physical object being moved.
- **Response times:** observed confirmed map change through map update and route response, using the existing dynamic diagnostics. This excludes time before the headset observed the object and its internal depth processing.
- **Path changes, blocked events and recoveries:** navigation transitions during the trial. Path changes can also reflect wearer movement during an update.
- **Configuration:** mode, scenario, scanner, room/item/anchor IDs, Unity build identifier, floor coverage setting, map revisions, cell size, margin, initial walkable cells and eligible furniture count.

Missing measurements export as empty CSV cells. A trial with no available route must not be treated as zero planning time or zero route length. No participant names, camera images, source meshes or raw headset coordinates are added to these trial files. A session ID groups trials from one app launch; it is not an automatically assigned participant ID.

## Interrupted trials and verification

Records are checkpointed at start, on markers/reports, about every two seconds and at outcome. On a normal pause or close, an active trial is aborted. If the app is killed before a final save, its next launch recovers the last Running checkpoint as Aborted with `end_time_unknown=true`, an empty end UTC and the last checkpoint's elapsed time. That elapsed time is a lower bound, not an invented exact finish time. If saving fails, the result stays in memory and new trials are blocked until export retries the save. Closing the app while storage remains unavailable can still lose that unsaved result.

Corrupt, oversized or unsupported records are retained but skipped during export and counted in its completion file. Event histories are bounded to 5,000 entries per trial; dropped-event counts are explicit, while aggregate counters continue. The CSV pair is marked complete only after both files are written.

`Tools/Test-Evaluation.ps1` uses Unity's bundled .NET runtime and checks timing, outcomes, head-path rejection, manual markers, missing measurements, event bounds, CSV formatting, atomic checkpoint replacement, interruption recovery and export integrity. Editor/Android C# compilation checks integration. A physical headset pilot remains required before collecting research results; these controls are a measurement workflow, not evidence of novelty or completed validation by themselves.
