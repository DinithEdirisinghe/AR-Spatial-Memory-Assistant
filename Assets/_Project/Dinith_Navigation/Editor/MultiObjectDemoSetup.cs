using System.Linq;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using SpatialSense.ObjectMenu;
using SpatialSense.Persistence;
using SpatialSense.RoomDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MultiObjectDemoSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_SavedObjectDemo.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_MultiObjectDemo.unity";

    [MenuItem("SpatialSense/Create Multi Object Demo")]
    private static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("SpatialSense", "Leave Play mode first.", "OK");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Destination) == null && !AssetDatabase.CopyAsset(Source, Destination))
        {
            Debug.LogError("[SpatialSense] Create and save the working saved-object scene first.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rooms = objects.Select(item => item.GetComponent<RoomUnderstandingDemo>()).Where(item => item != null).ToArray();
        var demos = objects.Select(item => item.GetComponent<ManualObjectDemo>()).Where(item => item != null).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (rooms.Length != 1 || demos.Length != 1 || rigs.Length != 1 || font == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "The source needs one working saved-object flow, room loader and camera rig.", "OK");
            return;
        }
        var existing = objects.Select(item => item.GetComponent<PersistentObjectMemory>()).Where(item => item != null).ToArray();
        if (existing.Any(memory => !SavedObjectCatalog.IsKnownName(memory.ObjectName)))
        {
            EditorUtility.DisplayDialog("SpatialSense", "An existing memory component has an unsupported object name. Use My keys, My wallet or My remote.", "OK");
            return;
        }
        var slots = new PersistentObjectMemory[3];
        for (var i = 0; i < slots.Length; i++)
        {
            var name = SavedObjectCatalog.Names[i];
            var matches = existing.Where(memory => memory.ObjectName == name).ToArray();
            if (matches.Length > 1)
            {
                EditorUtility.DisplayDialog("SpatialSense", "Duplicate memory components for " + name + ". Remove the duplicate before setup.", "OK");
                return;
            }
            var memory = matches.FirstOrDefault();
            if (memory == null)
            {
                var slotObject = new GameObject("Memory - " + name);
                SceneManager.MoveGameObjectToScene(slotObject, scene);
                Undo.RegisterCreatedObjectUndo(slotObject, "Create object memory slot");
                memory = Undo.AddComponent<PersistentObjectMemory>(slotObject);
            }
            Undo.RecordObject(memory, "Configure named memory slot");
            memory.Configure(rooms[0], demos[0]);
            memory.ConfigureSlot(name, i == 0);
            EditorUtility.SetDirty(memory);
            slots[i] = memory;
        }
        var menu = objects.Select(item => item.GetComponent<MultiObjectMenu>()).FirstOrDefault(item => item != null);
        if (menu == null)
        {
            var menuObject = new GameObject("My Objects Menu");
            SceneManager.MoveGameObjectToScene(menuObject, scene);
            Undo.RegisterCreatedObjectUndo(menuObject, "Create object menu");
            menu = Undo.AddComponent<MultiObjectMenu>(menuObject);
        }
        Undo.RecordObject(menu, "Configure object menu");
        menu.Configure(slots, demos[0], rooms[0], rigs[0], font);
        Undo.RecordObject(demos[0], "Select initial keys slot");
        demos[0].ConfigurePersistence(slots[0]);
        var navigation = objects.Select(item => item.GetComponent<SceneNavigation>()).FirstOrDefault(item => item != null);
        if (navigation != null)
        {
            Undo.RecordObject(navigation, "Keep tuned navigation clearance");
            navigation.AgentRadius = 0.25f;
            EditorUtility.SetDirty(navigation);
        }
        EditorUtility.SetDirty(menu);
        EditorUtility.SetDirty(demos[0]);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = menu.gameObject;
        Debug.Log("[SpatialSense] Multi-object scene selected for building. Right pointer/trigger operates the menu. Select Keys, Wallet or Remote. Register/update closes the menu; place the controller by the object and press A. Find starts its route. Left thumbstick opens the menu again. Existing Keys data is reused.", menu);
    }
}
