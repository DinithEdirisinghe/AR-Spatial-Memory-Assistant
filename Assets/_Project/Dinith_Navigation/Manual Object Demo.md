# Manual object registration demo

Purpose: prove register -> find -> user confirms, on Quest 3, with a manually supplied object location.

## Create and build

1. In Unity, wait for script compilation, then choose **SpatialSense > Create Manual Object Demo**.
2. Save any work if Unity asks. The tool copies the saved working bootstrap scene to `M1_ManualObjectDemo` and opens it. Repeating the command configures the existing demo instead of overwriting it from the bootstrap.
3. The tool saves the demo and makes it the only enabled build scene. The original bootstrap remains available as an asset.
4. Build to Android as usual, with the Quest selected. Use a new output folder/name such as `Builds/Android/ManualDemo/SpatialSense_ManualDemo.apk`.

## Headset controls (right controller)

- **A:** store the current right-controller position as the location of My keys. Hold the controller beside the real object before pressing. This replaces the previous registered position.
- **B:** begin finding the registered position. Pressing B before registration explains what to do first.
- **Index trigger while pointing at the marker:** confirm Found, when your headset is within one metre of the registered point. The pointer is yellow when confirmation is available. Confirmation changes the marker to cyan.
- **Click the right thumbstick:** cancel an active search while keeping the registered point.

The instruction panel follows your view. A green marker means registered, yellow means searching, and cyan means confirmed. The marker starts hidden until A is pressed. Its floating label says My keys and identifies the location as manually registered.

## Verify on the headset

1. Start fresh and press B: it should ask for registration, without inventing a location.
2. Hold the right controller beside keys on a table and press A: the green marker should appear there.
3. Move away safely, press B, and turn your head: the distance and left/right/ahead/behind cue should follow the registered point.
4. Point and trigger from more than one metre away: it must not confirm.
5. Move within one metre, aim at the marker, and press the trigger: it should report user-confirmed Found.
6. Press B, then click the thumbstick: it should cancel. Press B again: it should reuse the location.
7. Move the keys and re-register with A: the previous point should be replaced.
8. Put the controller out of tracking view: registration must pause and show the tracking message. Resume once tracking returns.
9. Close and relaunch the application: it should start with no registered object.
10. Reopen and build the original bootstrap scene separately if needed: its ball colour interaction should still work.

## Boundaries and next work

This is one manually named object in one running app session. It does not detect keys, track physical object movements, plan a walking route, or preserve spatial alignment across app restarts. Distance is straight-line distance to the registered controller position. Direction cues do not account for obstacles. Re-register after recentering or changing the physical setup.

Next steps are room loading and reachable navigation positions, followed by persistent spatial references. Later, a vision component can supply sightings in place of controller registration, while the search flow and display continue to be reused.

## Components

- `ManualObjectDemo`: right-controller commands, session state and instruction panel.
- `ManualObjectMarker`: labelled target and guarded confirmation interaction.
- `QuestControllerPointer`: shared pointing and trigger input. It now works with the `IPointerTarget` interface, including the original colour-toggle target.
- `ManualObjectDemoSetup`: editor-only scene creation/configuration command.

Source compilation is checked separately. The headset checklist above is required to validate readability, controller behaviour and physical placement.
