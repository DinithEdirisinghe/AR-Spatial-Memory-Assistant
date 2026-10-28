# Unity development handoff - 26 October 2026

## Beginner implementation reference

The current plain-language implementation guide is saved outside this Unity repository at `D:\Research Project Files\SpatialSense Implementation Guide.md` (reviewed 28 October 2026, Home v12.1). Update its current behaviour, file map, settings, limitations, verification status, and change log after meaningful milestones. This handoff remains the chronological technical history; early entries below are historical. Keep the root guide outside the Unity-only Git repository, consistent with the user's document/source separation.

Unity 6000.3.25f1, Meta XR Core/MRUK 207.0.1, AI Navigation 2.0.14. Target: Quest 3 Android standalone with passthrough and controllers.

## Working checkpoints

- Passthrough and green ball: tested on Quest.
- Right-controller pointing and trigger colour toggle: tested on Quest.
- Manual My keys registration/search/confirmation: tested on Quest.
- Loading the headset's saved room and drawing outlines: tested on Quest.
- Walking route prototype: displays routes, but the initial version rejected some usable positions. The user confirmed furniture outlines were aligned.

## Latest changes and next test

Walking route v2 removes duplicate obstacle clearance, uses rounded corner-clearance checks, expands the standing-position search, adds floor arrows, distinguishes the white controller pointer, and adds a left-trigger walking-map preview. Editor and Android source compilation passed. Geometry tests passed: 17 collision cases, 3 padding/corridor regressions and 4000 symmetry/translation checks.

The user retested navigation v2 and reported improvement, requesting less clearance around objects. The setup preset is now 0.25 m agent radius (previously 0.30 m), keeping the 0.03 m scan buffer. Run **SpatialSense > Create Walking Route Demo** again and rebuild to apply it. The reduced-clearance preset still needs a headset retest; overall navigation accuracy has not been formally measured. Use the left-trigger map preview to inspect failures.

The saved-object milestone is now implemented in source. Use **SpatialSense > Create Saved Object Demo** to create/update `M1_SavedObjectDemo`. A registers and saves a native spatial anchor; the app commits its UUID, room UUID and timestamp locally only after native saving succeeds. On restart it loads, localizes and checks tracking before showing the marker or enabling Find. B retries a failed restore. Replacements preserve the previous record on failure. The anchor origin is the object point; no raw Unity world coordinate is persisted.

The user tested the saved-object demo on Quest and confirmed that the object restored at the same physical place after reopening. This establishes a working basic persistence checkpoint, not a formal reliability measurement.

The user tested the multi-object menu and reported it working. It offers Keys, Wallet and Remote with separate files/anchors, retains the existing Keys record, and uses right-controller pointer/trigger selection. Register/update closes the menu and asks for right A beside the real object. Find selects that object's route; Forget requires confirmation. Left thumbstick opens/closes and repositions the menu. Only the selected slot loads initially, and selection is locked during native operations. This is a basic user-confirmed checkpoint, not an exhaustive reliability test; see `Multi Object Demo.md`.

The latest source milestone is the shared team application base. Run **SpatialSense > Create Master Integration Scene**, which creates/configures `Assets/Scenes/SpatialSense_MasterIntegration.unity` from the tested multi-object scene and selects it for building. Earlier scenes and the old master placeholder are preserved. This adds shared observation, registry and application-state contracts, controller and simulated-vision sources, bounded local diagnostics, and a separately replaceable guidance renderer. The current menu is optional to the hub's search API. No real camera detector is supplied.

Editor and Android source compilation passed. Observation validation, catalog/isolation, atomic record storage and route geometry checks passed using PowerShell 7. The new master scene still needs a headset test: select Wallet, arm **Simulated vision update (test input)**, place the right controller at a new location and squeeze its side grip. Wait for saving, Find Wallet, check other slots, and restart to verify the new location and simulated source label. Also check cancel and tracking-loss clearing after the renderer extraction. See `Team-Integration-Guide.md` for teammate contracts and the full device checklist.

Storage and route tests remain under Tools. `Test-ObjectCatalog.ps1` adds slot isolation, legacy filename and persisted removal-marker checks. No research reports belong in this Unity repository.

## Live depth capture checkpoint — 27 October 2026

`LiveRoomScan`, `DepthMeshRecord` and `LiveRoomScanSetup` add an isolated first live-depth experiment. Run **SpatialSense > Create Live Room Scan Lab** in the normal Unity editor to create/select `M1_LiveRoomScan`. This uses live environment raycasts, not the saved MRUK room. Right A starts sparse surface-patch capture, B freezes/saves anchor-local geometry and a native anchor, right trigger restores, left X toggles visibility and left Y moves the panel. See `Assets/_Project/Dinith_Navigation/Live Room Scan Lab.md` for limits and device tests.

Geometry/record validation checks passed, as did runtime/editor and Android source compilation against the installed project assemblies. Background Unity scene generation exited before running the setup, so scene creation and actual Quest behaviour remain unverified. This is not fused reconstruction or live navigation: patches can overlap, dynamic objects leave old surfaces, no completeness is asserted, and the existing navigation source is unchanged.

The user subsequently created the lab and confirmed capture, save and restore at the same physical location on Quest. Cleanup v2 now uses `SpatialMeshAccumulator` to weld points within 2 cm with face-normal agreement within 30 degrees, reject collapsed/inverted triangles and remove duplicate indexed faces. Mesh capacity is checked before committing each face. The stored format remains compatible with earlier scans; new records add optional processing and capture-view metadata. Different triangulations can still overlap, and old surfaces are not removed dynamically. Run `Tools/Test-MeshAccumulator.ps1`; the new cleanup still needs a Quest retest. Rebuild the existing lab scene; its panel title is now **Room scan cleanup v2**.

## Scan + route v3 bundle — 27 October 2026

The next deliverable groups multiple changes in the existing `M1_LiveRoomScan` scene: runtime Scan/Route/Options menu, pause/continue (including a restored scan), three sampling-detail presets, automatic preset reduction after repeated sampling overruns, floor selection and saved floor metadata, an observed-floor grid with obstacle/unknown clearance, a destination pointer and A* route test, coloured map overlays, local OBJ export, and diagnostics inside Options. Components and menu controls are wired at runtime; rebuilding the existing lab scene is enough. No scene recreation is required for a scene that already has its configured LiveRoomScan component.

Controls now use right index trigger for menu selection; load is under Scan. A toggles capture, B saves, left stick opens/closes the menu, X cycles geometry views and Y opens/repositions the menu. Floor/destination selection uses right grip; right stick cancels. Route testing requires a paused scan, a human-selected floor and an in-app map build. Resuming/changing the scan clears its old map/route. Runtime detail and clearance can be changed without rebuilding the APK.

The map deliberately excludes unknown floor, prevents diagonal corner cutting and conservatively handles thin walls at grid boundaries. It is a static snapshot test, not continuous obstacle detection or guaranteed safe navigation. It does not yet connect named-object memory or recognition to the live grid. Follow `Live Room Scan Lab.md` for the single combined device checklist and full limits. C# source checks and deterministic mesh/navigation tests do not verify headset UI/depth/performance; v3 needs a device test.

## Earlier checkpoint controls

Right A registers My keys at the controller position; B starts finding; index trigger confirms while aiming at the object marker near the standing position; thumbstick click cancels. Left X toggles room outlines; Y reloads the room; index trigger toggles the walking-map preview.

Earlier demos retain their checkpoint flows. Persistent scenes restore local anchored records after room loading and localization. Room reload clears the active search, and a different room scan prevents restoration. Geometry comes from the saved headset scan. Automatic object recognition, continuous obstacle updates and multi-room routes are future work.

## Build notes

Scene creation/update commands are under SpatialSense. Keep earlier scenes as tested checkpoints. Per-demo instructions are beside the navigation assets. Run the portable geometry checks using `Tools/Test-RouteMath.ps1` in PowerShell.

If Quest disappears from Run Device while file browsing works, approve USB debugging inside the headset. ADB reports unauthorized until approval.

Unity sometimes generates the APK and then fails copying its IL2CPP backup folder. Installing that generated APK directly using Unity's bundled Android tools has worked. Generated builds, intermediates and recovery/simulator backups are excluded from Git.

The application ID is still the Unity template ID `com.UnityTechnologies.com.unity.template.urpblank`; change it deliberately before distribution.

## Floor rejection correction — v3.1

The user reported mostly red cells on an apparently level captured floor. Inspection found that v3 required all five floor samples in every neighbouring cell and measured clearance from whole route-cell bounds. Small coverage gaps therefore excluded a broad area and were painted the same red as physical obstacles. The actual headset scan was not available over USB during diagnosis, so its dominant rejection cause is still unconfirmed.

V3.1 measures clearance from the route centre, distinguishes incomplete support (purple) from obstacle/clearance (red), and adds Normal/Strict coverage as a runtime setting. Normal requires a directly observed centre and three corners; Strict requires all five samples. Unknown centres, large gaps, thin walls and blocked diagonal corners remain excluded. The map reports green/red/purple counts and separate obstacle-evidence counts under Options statistics. Existing saved meshes can be reused; rebuild the map after changing coverage mode. Source compilation and regression checks passed; the effect on this room still needs a headset retest.

## Native capture v4 — clarified user intent

The user clarified that the goal is fresh capture through Meta's native scanner, not automatically loading an old room floor. V4's primary action is now **Scan room with Meta** (`OVRScene.RequestSpaceSetup`). After the native flow returns, it loads the available system model and explicitly queries fresh global-mesh triangles for display. Starting the app no longer implicitly loads a pre-existing room. Loading previous data is an explicit separate action. An OS-cancelled setup can still leave prior data available; the app does not claim that every successful API return proves a new capture.

The default app view shows the native mesh. Saving creates local native-room JSON and native-mesh OBJ copies. The optional walking map uses Meta's semantic floor/walls/furniture, independent of the former sparse mesh and its coverage heuristics. Optional live depth checks restrict the base map using temporal obstacle evidence; they do not reconstruct the floor or remove system furniture. See the rewritten Live Room Scan Lab guide for controls and limitations. The public flow is Meta Space Setup, not an embedded private boundary-scanning algorithm or an incremental native mesh stream inside Unity.

## Extra obstacle margin v4.1 — 27 October 2026

Reduced the grid cell size from 15 cm to 7.5 cm and kept the approximate room extent by increasing the side limit to 256 cells. Extra margin now defaults to 3 cm and cycles 0/3/5/10/20 cm inside Route/Options. It is remembered across sessions. Changing it automatically rebuilds the map; route/live monitoring reset and must be selected/enabled again. Static and live obstacles share the same margin convention, and live clearing volumes use the actual grid cell size.

Split captured obstacle cells (red) from extra-margin cells (orange), keeping route/unknown support states separate. Tests verify increased route area with thin margins, unchanged obstacle evidence, actual walls blocked at zero margin and wall detours. Editor/Android source compilation and navigation/live-layer regressions pass; the finer grid and thin margin still need a Quest performance/alignment retest. A thin point-route margin does not establish human body clearance. Next coherent application milestone: combine this native capture/navigation scene with the existing saved-item workflow before adding real recognition.

## Find my items v5 — current combined application

The native capture, native-room walking map and persistent three-item workflow now run in the existing M1_LiveRoomScan scene. Startup creates NativeItemWorkflow and three PersistentObjectMemory instances using a new native-room adapter. Earlier manual/demo adapters and the original three filenames remain supported. The UI has Scan/Items/Route/Options tabs. Items supports per-slot register/update, Find, near-endpoint manual Found confirmation, Cancel and a five-second confirmed Forget. Controller registration is armed explicitly, uses the side grip and shares the typed observation connection; A/B retain their native capture/export meanings.

Find restores the item's anchor, computes a reachable standing point near its projected location on a worker snapshot, and then follows the normal native-floor route. It never substitutes a tabletop coordinate as a floor goal or draws an unchecked straight-line fallback. Tracking/context changes, item updates, another route/selection and cancellation invalidate old guidance/plans. The labelled marker is the selected last saved location, not automatic recognition. New scans with another room UUID require re-registration.

NativeItemWorkflow exposes the existing observation sink, spatial registry and application-state interfaces for teammates. Future detector submissions use captured room/frame context and UTC capture time on the main thread; commits/ready snapshots acknowledge persistence. Diagnostics are local and bounded. See Assets/_Project/Dinith_Navigation/Native Item Finding.md for one combined headset checklist, contracts, storage compatibility and limitations. Source compilation and deterministic storage/policy/navigation checks are not a native Quest integration test; the v5 device retest remains required.
# 2026-10-27: v6 guidance endpoint and scan-method comparison

Fixed item endpoint selection in ScanNavigationGrid: search connected cells, prefer closest reachable item distance (half-cell tolerance), then shortest walk. The former travel + 0.35 * distance objective chose a point about a metre early. Regression tests now check floor-item endpoints from five approaches and tabletop-edge endpoints.

Restored both capture methods in RoomScanMenu with a Method button, plus App tools. LiveScanNavigation selects the correct frame and geometry source; app scans use captured triangles and an explicitly chosen floor height, while Meta maps use semantic floor/furniture snapshots. No geometry blending. Source changes cancel routes/workflows, retain both captured meshes, and rebuild the selected map. App floor metadata saves/restores with the existing anchored mesh file. Native item records still use the Meta room UUID for identity with either map source. See Native Item Finding.md for headset comparison steps and this limitation.

## 2026-10-27: dynamic routing v7

LiveScanNavigation now samples a budgeted rotating 12x8 field plus evidence-targeted rechecks. LiveObstacleLayer tolerates nearby depth jitter, expires only unconfirmed noise, confirms over two batches, and clears confirmed evidence only after three positive free-volume batches. Nearby confirmed hits can span at most 22.5 cm at similar heights to stop routing between sparse furniture-face samples. Evidence is bounded to 1,024 cells. A magenta changed-cell mesh is updated independently of the larger baseline floor meshes.

Live changes replan immediately, clear invalid lines, retain blocked destinations, and recover after removal. If an item approach becomes unavailable, a cancellable worker searches for another reachable point beside the same saved target. Room/source/selection/obstacle revisions guard against stale results. Initial item planning retries up to twice if live changes invalidate its snapshot. Native item UI prioritises blocked messages and keeps the saved object location unchanged.

Dynamic checks default ON after a usable map build and retain the user's preference. The new Dynamic tab exposes monitoring, overlay visibility, test counters/marker, margin and confirmed manual layer reset. Checks OFF keeps confirmed evidence; source switches/map rebuilds reset the live layer. Bounded diagnostics record grouped test/source/revision identifiers, observed-to-confirmed and confirmed-change-to-response timings, path changes and blocked/recovered events. Baseline scan furniture cannot be erased by live free-space rays; rescan for that. See Dynamic Routing.md for the device checklist, precise measurement meanings and sensor limits. Automated pure-geometry tests and source compilation do not establish physical headset detection performance.

## Furniture updates v8 — current implementation

MetaRoomSource.Snapshot now optionally supplies per-triangle movable classification. The initial allowlist is TABLE/COUCH/BED with no mixed/unknown labels. App scan triangles are eligible only when wholly matched to those loaded semantic regions and not near protected semantic geometry; app floor/obstacle geometry remains its own. Changed semantic revision rebuilds an app map to invalidate stale classification.

ScanNavigationGrid retains original cell state, floor support, raw permanent/furniture provenance and furniture height intervals. A protected obstacle wins in overlap cells. RebuildFurnitureBaseline removes only verified furniture, recalculates the selected margin and preserves unknown/coverage restrictions. Live obstacles apply afterward. Route snapshots copy effective cells and remain independent of subsequent corrections.

BaselineFurnitureLayer requires normal-verified current floor hits and positive free-volume observations at old occupied heights, three batches per required sample over at least 0.6 s, with a 3 s evidence freshness window. Occluded/missing/below-surface evidence does not open a cell; positive reoccupation restores the restriction. Candidate storage is bounded to 4,096 cells. Corrections are session-local, reset by map/source/context rebuild, and never modify Meta data or saved item anchors. Sparse app scans without floor support beneath furniture remain blocked.

Sampling rotates query roles across batches to avoid budget starvation and focuses on small candidate groups to revisit evidence within its freshness window. Blue tiles show reopened navigation floor; cyan source mesh remains the original capture, so Furniture > Show walking floor only is provided. The Furniture tab exposes monitoring, verification counters, baseline reset and existing controls. Diagnostics add baseline_furniture_changed and candidate/reopened/restored counts using the existing source/test/revision grouping. See Moved Furniture Updates.md and Test-BaselineFurniture.ps1 for the protocol, conservative limits and checks. Do not describe this as complete mesh reconstruction or automatic movable-object classification from app depth alone.

## Evaluation lab v9 — current application

LiveRoomScan now creates NavigationEvaluation after the item workflow. The Tests tab provides FixedMap / LiveObstacles / FurnitureUpdates modes, four scenario labels, scanner/item selection, clean-map preparation, automatic Find, manual added/moved and removed/cleared markers, false/missed obstacle reports and Success/Failed/Aborted outcomes. Success requires the existing near-item manual confirmation. Preparing/active trials lock ordinary menu actions and A/B scanning/export shortcuts; source, room, target anchor/pose, mode, margin, map and tracking checks abort invalidated timed trials. Navigation changes use lightweight TrialSignal events; initial blocked searches retry once per second.

NavigationTrial is pure measurement/data logic. It samples horizontal headset distance and route availability at about 10 Hz, rejects pose jumps, retains missing values, distinguishes approximate manual references from observed map confirmation and route response, and caps event history with explicit drop counts. NavigationTrialStore atomically checkpoints JSON, recovers unfinished Running records as Aborted with unknown exact end time, and exports a cumulative UTF-8 summary/events CSV pair with a completion file and skipped-record accounting. Normal preferences are restored after trials; source scans/item memory remain intact. Downloaded EvaluationResults are ignored by Git.

Use Tools/Test-Evaluation.ps1 with Unity's bundled .NET runtime for deterministic timing/outcome/CSV/checkpoint tests. Tools/Pull-EvaluationResults.ps1 reads the current Android package and copies data via authorised USB ADB into a new dated folder without removing headset data. Evaluation Lab.md contains the pilot steps, metric definitions, comparison protocol and data limitations. C# source compilation does not replace a v9 headset pilot. Existing v8 furniture and v7 dynamic navigation remain available outside trial mode.

## Connected rooms v10 - current application

LiveRoomScan creates MultiRoomNavigation after the native item workflow. It caches semantic room navigation models independently of the current rendered mesh, persists floor-local doorway endpoints and aliases, and performs cost-aware room/doorway graph planning using per-room grid routes. It does not concatenate separate room coordinate systems or infer adjacency from overlapping meshes. The Rooms tab teaches one doorway side inside each room, supports a verified tracked walk to the second side, forgetting links and excluding an unsuitable doorway for the current search.

MetaRoomSource exposes DataRevision for model/cache changes and TrackingRevision for actual recenter/tracking/pause invalidation. Normal room switches retain Find intent. SDK data reloads rebuild cache maps; real tracking changes cancel the journey. Structural layout stamps bind connections to floor/wall/doorframe geometry and IDs, with stamps cached per model revision to avoid per-frame drift churn. The current room's live routing snapshot replaces the reference cache when choosing the next leg. Doorway goals are checked against the current map again before drawing. No line is invented across the threshold while its next room is unlocated.

NativeItemWorkflow uses the room UUID already in each saved observation to start a remote-room journey. It does not ask PersistentObjectMemory to localize foreign-room anchors or relax its room/tracking checks. Slot labels show the saved room. On arrival the manager waits for room tracking/map readiness, ends the doorway leg, and invokes normal local Find/anchor restore. Selected item changes, new saved observations and cancellation clear the journey without deleting saved item data. Old item files need no migration.

Room navigation archives and links reside in persistent room-navigation/, using atomic writes and bounded validation. They are navigation geometry, not globally localized remote meshes or live remote observations. Cross-room routing initially uses Meta room maps; app-owned scanning stays local. The v9 trial protocol remains single-room and refuses preparation during a journey/teaching/cache build. See Connected Rooms.md and Tools/Test-MultiRoom.ps1 for setup, pure graph/traversal/persistence checks and the required physical headset pilot. SDK room availability/localization and saved doorway alignment are not established by source compilation.

## MRUK capture budget fix v10.1

The reported Sampling was slow / incomplete view discarded / Current detail Fast string came exclusively from App live scan's EnvironmentRaycastManager path, not the native Meta Space Setup flow. Replaced whole-grid discard with DepthPatchSampler: complete one 2x2 cell, retain accepted triangles, and stop between cells at the cooperative 8 ms budget. Each cell is measured in one call/frame; later calls continue spatial coverage without mixing triangle samples across frames. Camera/profile changes restart view completion while preserving the rotating cell cursor, and missing-data sweeps retry after warmup. Removed automatic detail degradation because per-cell scheduling bounds work independently of the chosen profile. Existing anchor-local fusion, edge rejection and persistence stay in use.

HUD/source actions distinguish App live scan (MRUK) and Open Meta room setup. App geometry invalidation now gives capture/map rebuild guidance. Fixed MultiRoomNavigation.Cancel incorrectly clearing a null-owned route when no journey existed. Test-DepthPatchSampler.ps1 simulates a device whose very first cell exceeds the budget and verifies retained geometry, advancing full-view coverage, profile bounds and missing-depth/discontinuity/cross-call rejection. Sampling performance and coverage still need the device retest.

## Continuous home scanning v11

Boundaryless configuration: the custom Android manifest declares BOUNDARYLESS_APP and required PASSTHROUGH. OculusProjectConfig.insightPassthroughSupport is Required so SDK manifest processing preserves the required passthrough capability. Contextual boundaryVisibilitySupport stays None and runtime shouldBoundaryVisibilityBeSuppressed stays false, because Meta treats the app-wide and contextual approaches as mutually exclusive. The current scene already uses Floor Level tracking and an active passthrough layer; no scene rewrite was needed. Rebuild/install a standalone APK to test crossing the former room-scale edge. Link Play mode does not exercise the manifest declaration.

Home is now the opening mode in M1_LiveRoomScan. LiveRoomScan creates HomeScanner at runtime; the scene was not rewritten for this feature. Home mode suspends legacy navigation/UI updates and MRUK world locking, leaving the previous Meta/app scan tools accessible separately. Home has one start/continue action, automatic floor verification, checkpoint saving, source-independent home item registration and continuous routes across observed doorway floor. It does not require room identities or teaching doorway pairs.

HomeDepthCapture uses EnvironmentDepthManager public shader globals, a 32x32 compute readback, capture-time inverse reprojection into the home anchor, one in-flight request, one-second readback-age rejection and independent MRUK raycast cross-checks. HomeScanner processes a bounded triangle queue and falls back to the budgeted ray sampler on unavailable/mismatched dense data. HomeMap uses sparse 15 cm cells with five-point accumulated observed-floor coverage, conservative obstacle intersection and cardinal-neighbour search. Display meshes are thinned and divided into 6 m sections; navigation observes accepted triangles independently of display thinning.

home-scan/home-v1.json stores validated geometry, floor evidence and three home-local items tied to one saved OVRSpatialAnchor. Atomic saves run approximately every 15 seconds and on explicit finish/item updates/interruption. Pending save requests are coalesced. Tracking interruptions hide the map and require reference recovery. Large-house drift/loop closure is not solved by this single-reference prototype; do not claim a multi-anchor SLAM system. Home obstacle removal, legacy observation-interface adaptation and research trial integration remain separate follow-up work. Previous scan files and native item anchors are not migrated or removed. Read Home Scanning.md for the physical pilot and constraints.

## Live home v12 - current Home workflow

Home was missing the earlier semantic-grid live correction layer, used a 15 cm graph and cardinal BFS, and only sampled while scanning/finding. HomeMap now uses 7.5 cm sparse cells and a 320,000-cell cap, retaining the same geometric area budget. HomeRoutePlanner adds eight-neighbour A*, diagonal corner guards, explicit extra clearance (default 1.5 cm), checked line smoothing, a close reachable item approach and route revalidation. A new obstacle or an obstructed item approach revokes affected guidance; unrelated floor updates no longer invalidate completed routes or every in-flight plan.

HomeLiveUpdates ports the earlier evidence principles to the sparse home graph: two distinct positive batches confirm new occupancy; repeated free rays through all old height bands plus confirmed floor are needed to reopen old space. Freshness is 3 seconds, three votes per band/floor and at least 0.6 seconds of evidence. Hit evidence wins; occluded/missing rays do not open cells. Tall vertical structural surfaces and obstacles above 1.6 m remain protected. Geometry-based eligibility is intentionally not claimed as semantic furniture recognition. Positive reoccupation immediately reblocks a previously cleared cell. Counters and magenta/blue overlays show new blocks/reopened floor, and cleared navigation state is checkpointed.

HomeScanner keeps checks running when capture is paused or no Find is active. Dense sampling increased from 32x32 to 48x48; fallback ray quads from 14x12 to 20x16. Each dense frame retains its acquisition origin. Targeted old-volume/floor probes use a cooperative budget and return unused cursor slots so slow devices cannot repeatedly skip the same targets. Preview thinning changed from 8 cm to 5 cm. Full preview chunks stop accumulating display geometry while floor mapping and live updates continue. New sources HomeLiveUpdates.cs and HomeRoutePlanner.cs require no scene recreation.

HomeRecord schema 2 has explicit cell size and occupied height ranges; the existing home-v1.json path is retained. Version 1 loads are rebuilt from genuine saved mesh at the finer scale, preserving anchor and item records without inventing fine floor from coarse cells. Some gaps require rescanning. Home still uses a single spatial reference, lacks large-house drift/loop closure and is separate from the old trial/observation adapters. See Home Scanning.md and Tools/Test-HomeMap.ps1 for the physical pilot and deterministic tests.
## Home route soft occlusion v12.1

The Home path has its own Resources material and URP shader using Meta's installed EnvironmentOcclusionURP helpers, global hard/soft/no-depth variants and stereo eye macros. It renders transparently without writing virtual depth, and uses a small relative depth bias to reduce floor noise. Pixels outside the depth-camera view keep normal visibility. Only HomeScanner's line renderer uses this material; the existing item locator remains separate. Material/resource references retain the shader in the APK, with the previous blue line as fallback if unavailable.

HomeDepthCapture now starts/enables EnvironmentDepthManager and SoftOcclusion independently of whether dense GPU readback is supported or has failed. Its existing capture-time reprojection and ray fallback remain in use. Header v12.1 identifies this rendering change. Verify on Quest by hiding a farther route segment behind real furniture and revealing it by moving the head. C# source checks do not verify physical depth accuracy or stereo rendering.
