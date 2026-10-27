using System.Linq;
using SpatialSense.Bootstrap;
using SpatialSense.RoomScan;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LiveRoomScanSetup
{
    private const string Source = "Assets/_Project/Dinith_Navigation/Scenes/M1_Quest_Bootstrap.unity";
    private const string Destination = "Assets/_Project/Dinith_Navigation/Scenes/M1_LiveRoomScan.unity";

    [MenuItem("SpatialSense/Create Live Room Scan Lab")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(Destination) == null && !AssetDatabase.CopyAsset(Source, Destination))
            throw new System.InvalidOperationException("The working Quest bootstrap scene is missing.");
        var scene = EditorSceneManager.OpenScene(Destination);
        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        if (rigs.Length != 1) throw new System.InvalidOperationException("Expected one Quest camera rig.");
        // This lab has its own controller controls and does not load the saved Scene API room.
        foreach (var item in objects)
        {
            if (item == null) continue;
            if (item.name == "Sphere" || item.GetComponent<QuestControllerPointer>() != null)
                Undo.DestroyObjectImmediate(item.gameObject);
        }
        const string materialPath = "Assets/_Project/Dinith_Navigation/LiveDepthMesh.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new System.InvalidOperationException("URP Unlit shader is unavailable.");
            material = new Material(shader) { name = "LiveDepthMesh" };
            material.SetColor("_BaseColor", new Color(0.05f, 0.8f, 0.85f, 0.24f));
            material.SetFloat("_Surface", 1);
            material.SetFloat("_Blend", 0);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0);
            material.SetFloat("_Cull", 0);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = 3000;
            AssetDatabase.CreateAsset(material, materialPath);
        }
        var scanner = Object.FindAnyObjectByType<LiveRoomScan>();
        if (scanner == null)
        {
            var go = new GameObject("Live Room Scan Lab");
            Undo.RegisterCreatedObjectUndo(go, "Create scan lab");
            scanner = Undo.AddComponent<LiveRoomScan>(go);
        }
        Undo.RecordObject(scanner, "Configure scan lab");
        scanner.Configure(rigs[0], material, Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));
        EditorUtility.SetDirty(scanner);
        AssetDatabase.SaveAssets();
        EditorSceneManager.SaveScene(scene);
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(Destination, true) };
        Selection.activeGameObject = scanner.gameObject;
        Debug.Log("[SpatialSense] Find my items v5 ready. Build this scene to Quest 3. Scan/load the Meta room, then open Items. Register beside an item with right grip and wait for saving; Find chooses a reachable standing position. Found it confirms near the endpoint; Cancel stops guidance and Forget requires confirmation. A opens Meta setup; B saves the native room copy.", scanner);
    }
}
