using System.Linq;
using SpatialSense.Bootstrap;
using SpatialSense.ManualDemo;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ManualObjectDemoSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_Quest_Bootstrap.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_ManualObjectDemo.unity";

    [MenuItem("SpatialSense/Create Manual Object Demo")]
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
            Debug.LogError("[SpatialSense] Could not copy the bootstrap scene. Check that it exists.");
            return;
        }
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var rig = rigs.Length == 1 ? rigs[0] : null;
        var pointer = objects.Select(item => item.GetComponent<QuestControllerPointer>()).FirstOrDefault(item => item != null);
        var target = objects.FirstOrDefault(item => item.GetComponent<ManualObjectMarker>() != null)
            ?? objects.FirstOrDefault(item => item.name == "Sphere");
        var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (rig == null || pointer == null || target == null || font == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "The copied scene needs a camera rig, controller pointer, Sphere and built-in font. Set up and save the working bootstrap scene first.", "OK");
            return;
        }

        var toggle = target.GetComponent<ColourToggleTarget>();
        if (toggle != null) Undo.DestroyObjectImmediate(toggle);
        Undo.RecordObject(target.gameObject, "Name manual marker");
        target.name = "My keys marker";
        Undo.RecordObject(target, "Size manual marker");
        target.localScale = Vector3.one * 0.12f;
        if (target.GetComponent<Collider>() == null) Undo.AddComponent<SphereCollider>(target.gameObject);
        var marker = target.GetComponent<ManualObjectMarker>() ?? Undo.AddComponent<ManualObjectMarker>(target.gameObject);
        var demo = objects.Select(item => item.GetComponent<ManualObjectDemo>()).FirstOrDefault(item => item != null);
        if (demo == null)
        {
            var app = new GameObject("Manual Object Demo");
            SceneManager.MoveGameObjectToScene(app, scene);
            Undo.RegisterCreatedObjectUndo(app, "Create manual demo");
            demo = Undo.AddComponent<ManualObjectDemo>(app);
        }
        Undo.RecordObject(demo, "Configure manual demo");
        demo.Configure(rig, marker, font);
        EditorUtility.SetDirty(demo);
        EditorSceneManager.SaveScene(scene);
        // Use only the demo for this build; the bootstrap asset stays available.
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = demo.gameObject;
        Debug.Log("[SpatialSense] Manual object demo created, saved and selected for building. Right controller: A registers beside your keys; B finds; aim at marker within 1 m and trigger confirms; thumbstick click cancels. No restart persistence or route planning yet.", demo);
    }
}
