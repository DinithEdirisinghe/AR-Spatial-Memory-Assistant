using System.Linq;
using Meta.XR.MRUtilityKit;
using SpatialSense.ManualDemo;
using SpatialSense.Navigation;
using SpatialSense.RoomDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WalkingRouteDemoSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_RoomUnderstandingDemo.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_WalkingRouteDemo.unity";

    [MenuItem("SpatialSense/Create Walking Route Demo")]
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
            Debug.LogError("[SpatialSense] Create and save the working room-understanding scene first.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var rooms = objects.Select(item => item.GetComponent<RoomUnderstandingDemo>()).Where(item => item != null).ToArray();
        var demos = objects.Select(item => item.GetComponent<ManualObjectDemo>()).Where(item => item != null).ToArray();
        var navigations = objects.Select(item => item.GetComponent<SceneNavigation>()).Where(item => item != null).ToArray();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Dinith_Navigation/PointerBeam.mat");
        if (rigs.Length != 1 || rooms.Length != 1 || demos.Length != 1 || navigations.Length > 1 || material == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "Expected one working room demo, object demo and camera rig, with its pointer material and at most one SceneNavigation.", "OK");
            return;
        }
        var app = objects.Select(item => item.GetComponent<WalkingRouteDemo>()).FirstOrDefault(item => item != null);
        if (app == null)
        {
            var appObject = new GameObject("Walking Route Demo");
            SceneManager.MoveGameObjectToScene(appObject, scene);
            Undo.RegisterCreatedObjectUndo(appObject, "Create walking route demo");
            app = Undo.AddComponent<WalkingRouteDemo>(appObject);
        }
        var nav = navigations.FirstOrDefault() ?? Undo.AddComponent<SceneNavigation>(app.gameObject);
        Undo.RecordObject(nav, "Configure floor navigation");
        nav.UseSceneData = true;
        nav.BuildOnSceneLoaded = MRUK.RoomFilter.None;
        // Calling the public build API explicitly avoids automatic room-event rebuilds.
        nav.enabled = false;
        nav.CustomAgent = true;
        nav.AgentRadius = 0.3f;
        nav.AgentHeight = 1.7f;
        nav.AgentClimb = 0.04f;
        nav.AgentMaxSlope = 8f;
        nav.NavigableSurfaces = MRUKAnchor.SceneLabels.FLOOR;
        nav.SceneObstacles = MRUKAnchor.SceneLabels.WALL_FACE | MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE
            | MRUKAnchor.SceneLabels.INNER_WALL_FACE | MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH
            | MRUKAnchor.SceneLabels.STORAGE | MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.OTHER
            | MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.LAMP;
        nav.Layers = ~0;
        nav.OverrideVoxelSize = true;
        nav.VoxelSize = 0.05f;
        nav.Agents = new System.Collections.Generic.List<UnityEngine.AI.NavMeshAgent>();
        Undo.RecordObject(app, "Configure walking route");
        app.Configure(rooms[0], demos[0], rigs[0], nav, material);
        Undo.RecordObject(demos[0], "Connect Find to walking route");
        demos[0].ConfigureNavigation(app);
        foreach (var pointer in objects.Select(item => item.GetComponent<SpatialSense.Bootstrap.QuestControllerPointer>()).Where(item => item != null))
        {
            Undo.RecordObject(pointer, "Distinguish controller pointer from floor route");
            pointer.SetIdleColour(Color.white);
            EditorUtility.SetDirty(pointer);
        }
        EditorUtility.SetDirty(nav);
        EditorUtility.SetDirty(app);
        EditorUtility.SetDirty(demos[0]);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = app.gameObject;
        Debug.Log("[SpatialSense] Walking route scene saved and selected for building. Register keys with right A, step away and press B. Blue = route; green ring = nearby standing position. Routes use the saved room scan.", app);
    }
}
