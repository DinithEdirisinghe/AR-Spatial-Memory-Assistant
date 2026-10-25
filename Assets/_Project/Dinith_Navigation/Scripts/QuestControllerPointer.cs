using UnityEngine;
using UnityEngine.Rendering;

namespace SpatialSense.Bootstrap
{
    [DisallowMultipleComponent]
    public sealed class QuestControllerPointer : MonoBehaviour
    {
        [SerializeField] private OVRCameraRig cameraRig;
        [SerializeField] private Material pointerMaterial;
        [SerializeField] private Color idleColour = Color.cyan;
        [SerializeField, Min(0.1f)] private float reach = 5f;
        [SerializeField] private LayerMask hitLayers = ~0;
        private LineRenderer beam;
        private Material beamMaterial;

        public void Configure(OVRCameraRig rig, Material material)
        {
            cameraRig = rig;
            pointerMaterial = material;
        }

        public void SetIdleColour(Color colour) => idleColour = colour;

        private void Awake()
        {
            if (cameraRig == null || pointerMaterial == null)
            {
                Debug.LogError("[SpatialSense] Pointer needs a camera rig and material. Run SpatialSense > Set Up Controller Interaction.", this);
                enabled = false;
                return;
            }
            beamMaterial = new Material(pointerMaterial);
            beam = gameObject.AddComponent<LineRenderer>();
            beam.sharedMaterial = beamMaterial;
            beam.useWorldSpace = true;
            beam.positionCount = 2;
            beam.startWidth = 0.006f;
            beam.endWidth = 0.003f;
            beam.shadowCastingMode = ShadowCastingMode.Off;
            beam.receiveShadows = false;
            beam.enabled = false;
        }

        private void LateUpdate()
        {
            var tracked = OVRInput.IsControllerConnected(OVRInput.Controller.RTouch)
                && OVRInput.GetControllerPositionTracked(OVRInput.Controller.RTouch)
                && OVRInput.GetControllerOrientationTracked(OVRInput.Controller.RTouch);
            var origin = cameraRig.rightControllerAnchor;
            beam.enabled = tracked && origin != null;
            if (!beam.enabled) return;

            var ray = new Ray(origin.position, origin.forward);
            var endpoint = ray.GetPoint(reach);
            SpatialSense.Interaction.IPointerTarget target = null;
            if (Physics.Raycast(ray, out var hit, reach, hitLayers, QueryTriggerInteraction.Ignore))
            {
                endpoint = hit.point;
                target = hit.collider.GetComponentInParent<SpatialSense.Interaction.IPointerTarget>();
                if (target != null && !target.CanSelect) target = null;
            }
            beam.SetPosition(0, ray.GetPoint(0.04f));
            beam.SetPosition(1, endpoint);
            var colour = target != null ? Color.yellow : idleColour;
            beam.startColor = colour;
            beam.endColor = colour;
            beamMaterial.SetColor("_BaseColor", colour);

            if (target != null && OVRInput.GetDown(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
                target.Select();
        }

        private void OnDisable()
        {
            if (beam != null) beam.enabled = false;
        }

        private void OnDestroy()
        {
            if (beamMaterial != null) Destroy(beamMaterial);
        }
    }
}
