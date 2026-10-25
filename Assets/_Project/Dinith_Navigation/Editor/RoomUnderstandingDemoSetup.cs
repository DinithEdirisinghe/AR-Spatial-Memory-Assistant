using System.Linq;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using SpatialSense.RoomDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RoomUnderstandingDemoSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_ManualObjectDemo.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_RoomUnderstandingDemo.unity";

    [MenuItem("SpatialSense/Create Room Understanding Demo")]
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
            Debug.LogError("[SpatialSense] Could not copy the working manual object demo. Create and save it first.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var kits = objects.Select(item => item.GetComponent<MRUK>()).Where(item => item != null).ToArray();
        var demo = objects.Select(item => item.GetComponent<ManualObjectDemo>()).FirstOrDefault(item => item != null);
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Dinith_Navigation/PointerBeam.mat");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (rigs.Length != 1 || kits.Length > 1 || demo == null || material == null || font == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "Expected one camera rig, the manual demo, its pointer material and at most one MRUK component.", "OK");
            return;
        }
        var kit = kits.FirstOrDefault();
        if (kit == null)
        {
            var roomObject = new GameObject("MRUK Room Data");
            SceneManager.MoveGameObjectToScene(roomObject, scene);
            Undo.RegisterCreatedObjectUndo(roomObject, "Create MRUK");
            kit = Undo.AddComponent<MRUK>(roomObject);
        }
        Undo.RecordObject(kit, "Configure room loading");
        kit.SceneSettings = new MRUK.MRUKSettings { DataSource = MRUK.SceneDataSource.Device, LoadSceneOnStartup = false };
        kit.EnableWorldLock = true;
        var manager = rigs[0].GetComponent<OVRManager>();
        if (manager != null)
        {
            Undo.RecordObject(manager, "Use room demo permission handling");
            var serializedManager = new SerializedObject(manager);
            var startupPermission = serializedManager.FindProperty("requestScenePermissionOnStartup");
            if (startupPermission != null)
            {
                startupPermission.boolValue = false;
                serializedManager.ApplyModifiedProperties();
            }
            EditorUtility.SetDirty(manager);
        }
        var roomDemo = objects.Select(item => item.GetComponent<RoomUnderstandingDemo>()).FirstOrDefault(item => item != null);
        if (roomDemo == null)
        {
            var roomApp = new GameObject("Room Understanding Demo");
            SceneManager.MoveGameObjectToScene(roomApp, scene);
            Undo.RegisterCreatedObjectUndo(roomApp, "Create room demo");
            roomDemo = Undo.AddComponent<RoomUnderstandingDemo>(roomApp);
        }
        Undo.RecordObject(roomDemo, "Configure room demo");
        roomDemo.Configure(kit, rigs[0], demo, material, font);
        Undo.RecordObject(demo, "Wait for room readiness");
        demo.ConfigureRoomLoading(roomDemo);
        EditorUtility.SetDirty(kit);
        EditorUtility.SetDirty(roomDemo);
        EditorUtility.SetDirty(demo);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = roomDemo.gameObject;
        Debug.Log("[SpatialSense] Room understanding demo created and selected for building. Allow scene access in the Quest. Left X toggles outlines; left Y retries or offers setup when no room exists. Right-controller object controls are available once a floor is loaded.", roomDemo);
    }
}
