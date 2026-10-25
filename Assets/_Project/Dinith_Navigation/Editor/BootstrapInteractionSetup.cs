using System.Linq;
using SpatialSense.Bootstrap;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class BootstrapInteractionSetup
{
    [MenuItem("SpatialSense/Set Up Controller Interaction")]
    private static void SetUp()
    {
        var scene = SceneManager.GetActiveScene();
        if (scene.name != "M1_Quest_Bootstrap" || EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("SpatialSense", "Open M1_Quest_Bootstrap and leave Play mode before running this setup.", "OK");
            return;
        }

        var objects = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
        var rigs = objects.Select(item => item.GetComponent<OVRCameraRig>()).Where(item => item != null).ToArray();
        var sphere = objects.FirstOrDefault(item => item.name == "Sphere" && item.GetComponent<Renderer>() != null);
        if (rigs.Length != 1 || sphere == null)
        {
            EditorUtility.DisplayDialog("SpatialSense", "This setup expects one OVRCameraRig and a rendered object named Sphere in the active scene.", "OK");
            return;
        }

        const string materialPath = "Assets/_Project/Dinith_Navigation/PointerBeam.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("SpatialSense", "The Universal Render Pipeline Unlit shader could not be found.", "OK");
                return;
            }
            material = new Material(shader) { name = "PointerBeam" };
            AssetDatabase.CreateAsset(material, materialPath);
            AssetDatabase.SaveAssets();
        }

        Undo.SetCurrentGroupName("Set up SpatialSense controller interaction");
        if (sphere.GetComponent<Collider>() == null) Undo.AddComponent<SphereCollider>(sphere.gameObject);
        if (sphere.GetComponent<ColourToggleTarget>() == null) Undo.AddComponent<ColourToggleTarget>(sphere.gameObject);
        var pointer = objects.Select(item => item.GetComponent<QuestControllerPointer>()).FirstOrDefault(item => item != null);
        if (pointer == null)
        {
            var pointerObject = new GameObject("Right Controller Pointer");
            SceneManager.MoveGameObjectToScene(pointerObject, scene);
            Undo.RegisterCreatedObjectUndo(pointerObject, "Create right controller pointer");
            pointer = Undo.AddComponent<QuestControllerPointer>(pointerObject);
        }
        Undo.RecordObject(pointer, "Assign camera rig");
        pointer.Configure(rigs[0], material);
        EditorUtility.SetDirty(pointer);
        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = sphere.gameObject;
        Debug.Log("[SpatialSense] Interaction ready. Save the scene, build to Quest, aim the right controller at the ball and press the index trigger. Cyan pointer = no target; yellow = target. Trigger toggles the original colour and blue.", sphere);
    }
}
