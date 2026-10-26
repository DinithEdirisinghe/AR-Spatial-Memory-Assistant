# Unity development handoff - 26 October 2026

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

## Controls

Right A registers My keys at the controller position; B starts finding; index trigger confirms while aiming at the object marker near the standing position; thumbstick click cancels. Left X toggles room outlines; Y reloads the room; index trigger toggles the walking-map preview.

Earlier demos retain their checkpoint flows. Persistent scenes restore local anchored records after room loading and localization. Room reload clears the active search, and a different room scan prevents restoration. Geometry comes from the saved headset scan. Automatic object recognition, continuous obstacle updates and multi-room routes are future work.

## Build notes

Scene creation/update commands are under SpatialSense. Keep earlier scenes as tested checkpoints. Per-demo instructions are beside the navigation assets. Run the portable geometry checks using `Tools/Test-RouteMath.ps1` in PowerShell.

If Quest disappears from Run Device while file browsing works, approve USB debugging inside the headset. ADB reports unauthorized until approval.

Unity sometimes generates the APK and then fails copying its IL2CPP backup folder. Installing that generated APK directly using Unity's bundled Android tools has worked. Generated builds, intermediates and recovery/simulator backups are excluded from Git.

The application ID is still the Unity template ID `com.UnityTechnologies.com.unity.template.urpblank`; change it deliberately before distribution.
