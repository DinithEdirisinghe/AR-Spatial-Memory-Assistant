using System.Linq;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using SpatialSense.Persistence;
using SpatialSense.RoomDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class SavedObjectDemoSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_WalkingRouteDemo.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_SavedObjectDemo.unity";

    [MenuItem("SpatialSense/Create Saved Object Demo")]
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
            Debug.LogError("[SpatialSense] Create and save the working walking-route scene first.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rooms = objects.Select(item => item.GetComponent<RoomUnderstandingDemo>()).Where(item => item != null).ToArray();
        var demos = objects.Select(item => item.GetComponent<ManualObjectDemo>()).Where(item => item != null).ToArray();
        var navigations = objects.Select(item => item.GetComponent<SceneNavigation>()).Where(item => item != null).ToArray();
        if (rooms.Length != 1 || demos.Length != 1 || navigations.Length != 1)
        {
            EditorUtility.DisplayDialog("SpatialSense", "The source scene must contain the working room, object and walking-route setup.", "OK");
            return;
        }
        var memory = objects.Select(item => item.GetComponent<PersistentObjectMemory>()).FirstOrDefault(item => item != null);
        if (memory == null)
        {
            var memoryObject = new GameObject("Persistent Object Memory");
            SceneManager.MoveGameObjectToScene(memoryObject, scene);
            Undo.RegisterCreatedObjectUndo(memoryObject, "Create persistent memory");
            memory = Undo.AddComponent<PersistentObjectMemory>(memoryObject);
        }
        Undo.RecordObject(memory, "Configure persistent memory");
        memory.Configure(rooms[0], demos[0]);
        Undo.RecordObject(demos[0], "Connect persistent registration");
        demos[0].ConfigurePersistence(memory);
        Undo.RecordObject(navigations[0], "Apply latest walking clearance");
        navigations[0].AgentRadius = 0.25f;
        EditorUtility.SetDirty(memory);
        EditorUtility.SetDirty(demos[0]);
        EditorUtility.SetDirty(navigations[0]);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = memory.gameObject;
        Debug.Log("[SpatialSense] Saved object demo selected for building. A registers and saves locally. Wait for Saved location ready, restart the app, let the anchor localize, then press B to find. Earlier demo scenes stay available.", memory);
    }
}
