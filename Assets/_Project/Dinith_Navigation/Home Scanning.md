# Live home scanning v12

## What to do on the Quest

Build and run the existing **M1_LiveRoomScan** scene. No scene recreation or new component setup is needed. The opening screen says **Live home v12.1**.

The Android app declares Meta's **Boundaryless** capability and requires passthrough. After rebuilding and installing the APK directly on the Quest, the app can operate beyond the drawn room-scale boundary on supported Horizon OS versions. This declaration does not apply to Unity Play mode over Quest Link. Keep passthrough active throughout the experience. The system still controls tracking and system dialogs; this setting does not remove map capacity or tracking limits.

This uses the app-wide `com.oculus.feature.BOUNDARYLESS_APP` manifest feature. The separate contextual Boundary Visibility API is intentionally left disabled: Meta documents these as mutually exclusive approaches. The rig already uses Floor Level tracking, and home geometry is attached to its saved spatial anchor rather than a room-scale boundary's Stage coordinates. Reference: [Meta Boundaryless documentation](https://developers.meta.com/vr/documentation/unity/unity-boundaryless/).

1. Choose **Start scanning your home**. Allow spatial-data access if asked.
2. Look down at nearby open floor for a few seconds. The app checks the floor height automatically against the headset's floor reference.
3. Walk slowly into the next room, looking around and down through the doorway. Continue the same scan. Do not create rooms or link doorways.
4. Smaller **7.5 cm** green squares show observed walking floor. Red squares show scanned obstacles; magenta shows new confirmed obstacles; blue shows positively verified reopened floor. Blank floor needs more observation. Connections appear when observed walking floor joins across the doorway.
5. Press **B**, or choose **Finish scanning & save**. Wait until the menu says progress is saved before closing the app.
6. Open **Items**, select Keys, Wallet or Remote, and choose **Register / update**. Hold the right controller beside the item and press its side grip.
7. Walk to another scanned room and choose **Find selected item**. The blue line follows connected observed floor toward the item; its blue sphere marks the saved location.

Live checks continue after capture is paused, including when no Find action is active. Put a chair into an observed walking area: repeated observations should block its new position and hide/replan any affected route. Move it away and look at its old position **and the floor** for a few seconds: the old cells can turn blue after the app verifies empty space through their old height bands and confirms floor support. Seeing only the floor, losing sight of the chair, or waiting is insufficient.

The **Route** tab controls obstacle checks, furniture clearing and extra clearance. The initial extra margin is **1.5 cm**, adjustable through 0, 1.5, 3, 5 and 10 cm. This is additional map clearance, not a model of the user's body width. Square size is the map resolution, not a guaranteed physical measurement accuracy.

The app checkpoints changes about every 15 seconds, and also saves on finishing capture, item registration and tracking interruption. On reopening, **Start scanning your home** restores the saved reference and continues. **Recover saved home** restores without immediately scanning. If localization fails, return near the original starting area and look around.

For a first test, scan two connected rooms and their doorway, register an item, find it from the other room, then close/reopen and repeat. Also cover the headset cameras briefly: guidance should disappear instead of sliding to an incorrect position. Recover the home reference before continuing.

## What changed

Home mode no longer depends on separate MRUK room IDs or manually taught doorway links. One sparse floor graph covers all observed connected spaces in the home reference. Geometry is held in small surface sections, and only nearby sections are drawn.

The primary capture path reads a **48 by 48** live Environment Depth image asynchronously, increased from 32 by 32. It records the matching camera-to-home transform and ray origin at capture time. Completed depth triangles are processed in small batches. Public MRUK raycasts cross-check the reconstructed positions. If dense capture is unsupported, errors, or repeatedly disagrees, a finer **20 by 16** budgeted ray sampler remains available automatically. Preview thinning is now 5 cm rather than 8 cm; navigation processes accepted observations independently of preview thinning.

Home routing now uses eight-neighbour A* with measured diagonal costs, corner checks and observed-floor line smoothing. The item approach favours a close reachable position and rejects an approach through a high blocking surface. Smoothed segments respect the selected margin. A completed worker result is checked against current obstacles, then connected to the user's current location. Floor updates do not discard otherwise valid route results. New occupied-space evidence revokes an affected route and triggers replanning.

In v12.1, the blue Home guidance line uses live **soft depth occlusion**. Real furniture and walls can hide parts of the line behind them. This is a rendering effect separate from route replanning. The depth feed is kept active even if dense mesh capture falls back to MRUK ray samples. Outside sensed depth coverage, or when depth is unavailable, the path renders normally. A small relative depth tolerance helps prevent floor noise from hiding a line placed just above the floor. The material is attached only to the path; the item locator retains its existing appearance. Test with a real chair between your eyes and a farther section of the route, and move your head to reveal the line again.

Older Home saves upgrade in place to schema 2 while keeping their spatial reference and item coordinates. The 7.5 cm floor is rebuilt from the saved measured mesh, rather than painting four fine cells for every old 15 cm square. Some old map coverage can therefore become unknown until you rescan those gaps. The checkpoint filename remains `home-v1.json` for continuity; its internal version and cell size describe the new format.

The app saves its own observed geometry. It does not import a pre-scanned Meta room or reproduce Meta's private Space Setup reconstruction algorithm. Floor height uses the headset's Floor Level/Stage tracking origin, corroborated by actual depth surfaces; it does not use the Meta room's floor polygon.

## Important limits of this first home version

- **One floor/level.** Stairs, ramps and multiple storeys are not supported. Floor Level or Stage must be the rig's tracking origin; the existing setup normally already uses this.
- **One saved spatial reference.** This is not a multi-anchor SLAM/loop-closure system. Alignment across a large house and recovery far from the starting area need physical headset testing. The app detects tracking interruptions and abrupt pose jumps, but cannot guarantee detection/correction of slow drift.
- **Observed paths only.** Walking somewhere is not treated as proof of an empty corridor. The floor must actually be visible to the depth camera. Glass, reflective surfaces, dark/textureless surfaces and missed doorway floor can leave gaps. Look down and rescan those gaps.
- **Furniture updates require visible evidence.** New nonstructural obstacles need two distinct acquisition batches. A previously cleared obstacle reblocks immediately on positive reoccupation. Clearing requires three positive observations of every old height band and its floor, spanning at least 0.6 seconds, with evidence no older than 3 seconds. Hit evidence wins within a batch. Walls identified by tall vertical observations and obstacles over 1.6 m remain protected. Home uses conservative height/geometry eligibility rather than semantic furniture labels; it cannot promise automatic removal for every furniture type. Targeted ray checks supplement the depth image when you look at an old occupied position. Cleared navigation space does not rewrite the historical 3D preview mesh.
- **Home items are separate.** Re-register the three items once in Home mode. Their coordinates are saved relative to the persisted home reference. Previous native-room item anchors and files remain unchanged. This home item workflow is not yet the shared observation-interface adapter used by the original native workflow.
- **Bounded storage.** At most 48 surface preview sections, 120,000 mesh vertices, 600,000 mesh indices and 320,000 observed floor cells. Each section also uses the existing mesh accumulator limits. A full preview section stops accepting display geometry; floor mapping and live checks continue. Filling the walking-map budget pauses expansion and retains guidance/updates. Live verification has an 8,192-cell evidence budget and expires old temporary evidence without erasing confirmed obstacles. The coordinate range is bounded to about 60 metres from the reference; this is a file-validation limit, not a demonstrated tracking range.
- **Not a navigation safety guarantee.** A 7.5 cm grid represents observed evidence, not a person's full body clearance. The route is a research prototype; the user still checks the real environment.
- **Checkpointing is not a shutdown guarantee.** An abrupt process kill can lose changes since the last completed save. Pause and wait for the saved indication before quitting.

Use **Previous scan tools** for the earlier native Meta scanner, app scanner, dynamic furniture experiments, evaluation trials and manually linked room workflow. Their files are separate and are not migrated or deleted. Home mode is not part of the old single-room evaluation protocol.

## Implementation map

- `HomeScanner.cs`: one-button session flow, anchor persistence/recovery, chunk previews, checkpoints and home items.
- `HomeDepthCapture.cs` and `Resources/HomeDepthReadback.compute`: asynchronous dense depth acquisition, with transforms captured before readback and a bounded in-flight request.
- `Resources/HomeRouteOcclusion.shader` and `.mat`: stereo-aware URP path material using the installed Meta depth-occlusion helpers; explicitly referenced through Resources so it is included in the Android build.
- `HomeMap.cs`: sparse observed-floor graph, obstacle rasterization, schema validation and legacy migration. Doorways need no special room-link representation.
- `HomeLiveUpdates.cs`: temporal obstacle confirmation, multi-height free-volume/floor verification, reoccupation and targeted probes.
- `HomeRoutePlanner.cs`: eight-neighbour A*, configurable clearance, checked smoothing and route validity checks.
- `RoomScanMenu.cs`: default Home tab and mode-specific item controls.

Persistence: `Application.persistentDataPath/home-scan/home-v1.json`, with atomic replacement and a previous-file backup. Choosing **Start a different home** requires a second confirmation and replaces the active home checkpoint after the new reference is ready. Existing Meta/app scan files are untouched.

The mesh preview is spatially thinned to reduce repeated overlapping surface triangles. Navigation still receives all accepted depth triangles, independently of that display thinning. Floor cells require centre and four inset-corner observations; no missing measurement becomes free space. Vertical surfaces are conservatively rasterized even when their top-down area is almost zero. A high vertical surface also prevents selecting an item approach through a wall. Routes do not snap an unobserved starting position across a gap.

## Validation

Run `Tools/Test-HomeMap.ps1` with Windows PowerShell and the Unity bundled runtime. It checks automatic doorway connections, disconnected rooms, thin obstacles, temporal live blocks, moved/reoccupied furniture, protected surfaces, occlusion, missing floor, hit precedence, saved-map reconstruction, legacy migration, a 48 m synthetic corridor, diagonal smoothing, obstacle detours, extra clearance, unknown start positions, tables, diagonal corner gaps, invalid checkpoints and cancellation. `Tools/Test-DepthPatchSampler.ps1` checks the shared triangle sampler and missing-depth handling.

C# source compilation and synthetic geometry tests cannot verify a Quest GPU shader, actual camera calibration, physical mapping quality, anchor persistence, UI readability or sustained frame rate. Validate those on the headset before using Home mode for research trials or claiming whole-house robustness.
