using System.Linq;
using Project.Core.Integration;
using SpatialSense.ManualDemo;
using SpatialSense.Navigation;
using SpatialSense.ObjectMenu;
using SpatialSense.Persistence;
using SpatialSense.RoomDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class MasterIntegrationSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_MultiObjectDemo.unity";
    private const string Destination = "Assets/Scenes/SpatialSense_MasterIntegration.unity";

    [MenuItem("SpatialSense/Create Master Integration Scene")]
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
            Debug.LogError("[SpatialSense] Create and save the working multi-object scene first.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var memories = objects.Select(item => item.GetComponent<PersistentObjectMemory>()).Where(item => item != null).ToArray();
        var rooms = objects.Select(item => item.GetComponent<RoomUnderstandingDemo>()).Where(item => item != null).ToArray();
        var flows = objects.Select(item => item.GetComponent<ManualObjectDemo>()).Where(item => item != null).ToArray();
        var routes = objects.Select(item => item.GetComponent<WalkingRouteDemo>()).Where(item => item != null).ToArray();
        var menus = objects.Select(item => item.GetComponent<MultiObjectMenu>()).Where(item => item != null).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Dinith_Navigation/PointerBeam.mat");
        if (memories.Length != 3 || rooms.Length != 1 || flows.Length != 1 || routes.Length != 1 || menus.Length != 1 || rigs.Length != 1 || material == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "Expected the tested three-object scene with one room loader, flow, route, menu and camera rig.", "OK");
            return;
        }
        var hub = objects.Select(item => item.GetComponent<SpatialApplicationHub>()).FirstOrDefault(item => item != null);
        if (hub == null)
        {
            var root = new GameObject("SpatialSense Application Services");
            Undo.RegisterCreatedObjectUndo(root, "Create application services");
            hub = Undo.AddComponent<SpatialApplicationHub>(root);
        }
        var logs = hub.GetComponent<ApplicationDiagnostics>() ?? Undo.AddComponent<ApplicationDiagnostics>(hub.gameObject);
        var manual = hub.GetComponent<ManualObservationSource>() ?? Undo.AddComponent<ManualObservationSource>(hub.gameObject);
        var simulated = hub.GetComponent<SimulatedVisionObservationSource>() ?? Undo.AddComponent<SimulatedVisionObservationSource>(hub.gameObject);
        Undo.RecordObject(hub, "Configure application hub");
        hub.Configure(memories, rooms[0], flows[0], routes[0], menus[0], manual, simulated, logs);
        Undo.RecordObject(manual, "Configure controller observation source");
        manual.Configure(hub);
        Undo.RecordObject(simulated, "Configure simulated vision source");
        simulated.Configure(hub, flows[0], rigs[0]);
        Undo.RecordObject(flows[0], "Connect observation sources");
        flows[0].ConfigureObservationSources(manual, simulated);
        Undo.RecordObject(menus[0], "Add simulated input action");
        menus[0].ConfigureIntegration(simulated);
        Undo.RecordObject(routes[0], "Connect shared spatial registry");
        routes[0].ConfigureRegistry(hub);
        EditorUtility.SetDirty(routes[0]);
        var provider = routes[0].GuidanceProvider;
        if (provider == null)
        {
            var renderer = routes[0].GetComponent<DefaultGuidanceRenderer>() ?? Undo.AddComponent<DefaultGuidanceRenderer>(routes[0].gameObject);
            Undo.RecordObject(renderer, "Configure default guidance renderer");
            renderer.Configure(material);
            Undo.RecordObject(routes[0], "Assign guidance renderer");
            routes[0].ConfigureGuidance(renderer);
            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(routes[0]);
        }
        foreach (var component in new Component[] { hub, manual, simulated, logs, flows[0], menus[0] }) EditorUtility.SetDirty(component);
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = hub.gameObject;
        Debug.Log("[SpatialSense] Team base configured. Build SpatialSense_MasterIntegration. Select Wallet > Simulated vision update, place the right controller at its new location, and squeeze right GRIP. Wait for saving, then Find. This is simulated input, not camera detection.", hub);
    }
}
