# Multiple named objects checkpoint

## Create and build

After Unity compiles, choose **SpatialSense > Create Multi Object Demo**. Save your work if prompted. This copies the saved-object scene to `M1_MultiObjectDemo`, configures three independent memory slots and a controller-operated menu, saves it, and selects it for building. Earlier scenes remain available. Repeating the command configures the existing multi-object scene.

Build a fresh Android APK, for example `Builds/Android/MultiObject/SpatialSense_MultiObject.apk`. On Quest, the new panel should say **SpatialSense | My objects** and show **Keys, Wallet and Remote**.

## Menu controls

- Point the right controller at a row or button and press the index trigger to select it.
- Select **Keys**, **Wallet** or **Remote**. The selected object is highlighted. Changing selection cancels the previous search and changes the marker label and navigation target.
- **Register / update** closes the menu. Hold the controller beside the selected real object and press right **A** to register and save. It does not save the position of the hand while pointing at the menu.
- **Find** starts a route to the selected object and closes the menu. If its saved anchor is not ready, the button says **Locate / retry** and attempts localization first. Wait until the row says Ready, then select Find.
- **Forget** asks for a second **Confirm forget** click. **Cancel**, selecting another row or closing the menu cancels that confirmation. No other object is removed.
- **Close** returns to the selected object's view without starting a search.
- Click the **left thumbstick** to open/close the menu. Opening places it in front of your current viewpoint, then it stays there so you can point at it. This also repositions a menu you walked away from.

While the menu is open, the other instruction panels and object marker are hidden so they do not obscure or intercept its buttons. The selected object's marker and route are shown only in the object view when appropriate.

In object view, the previous controls remain: right A registers/saves, B finds or retries, trigger confirms, and right thumbstick cancels search. Left X toggles room outlines, Y reloads the room, and trigger shows the walking-map preview.

## Saved object behaviour

Each object has its own file and anchor UUID. The original `spatialsense-my-keys-v1.json` is reused, preserving the Keys position saved in the single-object demo. Wallet and Remote have distinct filenames. The single-object scene and multi-object scene therefore share the Keys slot on the same installed app.

Only the selected object's anchor is initially loaded/localized. Other rows show **Saved - select to locate** rather than claiming tracking is ready. Already loaded anchors can remain tracked when another object is selected. Ready, locating, unavailable, not-saved and cleanup-pending states are reflected in the list.

Selection and new actions are disabled during a save/load/remove operation. The menu can still be closed with the Close button or left thumbstick. This prevents a delayed operation from applying one object's point to a newly selected object.

Updating one object retains its previous record if the replacement fails. Forget first commits a removal marker, then erases the native anchor. An interrupted deletion cannot revive the old entry on restart. If native cleanup fails, the entry remains removed and the menu offers Forget again to retry. Normal record loading never restores the diagnostic backup file.

The room UUID check, tracked-anchor requirement, local-only persistence and 0.25 m navigation-radius preset remain. There is still one visible selected-object marker and one active route, not three simultaneous routes. The app does not detect objects or update positions when they move physically.

## Required Quest checks

1. Existing Keys data should appear without re-registration.
2. Register Wallet and Remote at different locations. Select and find each; marker name, location and route must match that object.
3. Switch objects during a search. The previous route must disappear, and no previous object can be accidentally confirmed.
4. Update only Wallet. Keys and Remote must keep their prior locations.
5. Restart the app, select each row and wait for localization. All three should restore their own positions.
6. Press Forget, then Cancel: the entry must remain. Select another row after requesting deletion: the confirmation must be cancelled.
7. Confirm Forget for one object. It must remain absent after restart while the other two still restore. Re-register the forgotten object to use it again.
8. Test tracking loss and a different room scan. Find must remain unavailable for unlocated anchors. Room/permission messages must remain visible in the menu when setup is incomplete.
9. Open/close and reposition the menu with the left thumbstick. Pointing at buttons must be distinguishable from the floor route, and the hidden marker must not block menu selection.

Computer checks: `Tools/Test-ObjectCatalog.ps1` covers filename uniqueness, legacy Keys compatibility, record/removal validation, and update/forget storage isolation. The earlier storage and route tests remain applicable. Menu readability and multi-anchor save/restart behaviour require the actual headset checks above.
