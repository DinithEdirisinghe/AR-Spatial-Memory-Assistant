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

**Latest navigation accuracy still needs a headset retest.** Resume by running **SpatialSense > Create Walking Route Demo**, building a fresh APK and checking that the lower panel says **Walking route v2**. Test a clear direct route and a route around a scanned table. Use the left trigger to inspect blue navigable floor edges and red clearance outlines. Record specific failure messages and `[SpatialSense Navigation]` Unity logs if rejection persists.

## Controls

Right A registers My keys at the controller position; B starts finding; index trigger confirms while aiming at the object marker near the standing position; thumbstick click cancels. Left X toggles room outlines; Y reloads the room; index trigger toggles the walking-map preview.

Object registration lasts for the current app session. Room reload/change clears it. Geometry comes from the saved headset scan. Automatic object recognition, continuous obstacle updates, cross-session object anchors and multi-room routes are future work.

## Build notes

Scene creation/update commands are under SpatialSense. Keep earlier scenes as tested checkpoints. Per-demo instructions are beside the navigation assets. Run the portable geometry checks using `Tools/Test-RouteMath.ps1` in PowerShell.

If Quest disappears from Run Device while file browsing works, approve USB debugging inside the headset. ADB reports unauthorized until approval.

Unity sometimes generates the APK and then fails copying its IL2CPP backup folder. Installing that generated APK directly using Unity's bundled Android tools has worked. Generated builds, intermediates and recovery/simulator backups are excluded from Git.

The application ID is still the Unity template ID `com.UnityTechnologies.com.unity.template.urpblank`; change it deliberately before distribution.
