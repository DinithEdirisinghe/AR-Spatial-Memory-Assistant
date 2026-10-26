using SpatialSense.Interaction;
using UnityEngine;
using UnityEngine.UI;

namespace SpatialSense.ManualDemo
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer), typeof(Collider))]
    public sealed class ManualObjectMarker : MonoBehaviour, IPointerTarget
    {
        private ManualObjectDemo owner;
        private Transform viewer;
        private Transform label;
        private Renderer markerRenderer;
        private MaterialPropertyBlock properties;
        private Text nameText;
        private string objectName = "My keys";
        private string evidenceLabel = "Manually registered location";
        public void SetEvidenceLabel(string value)
        {
            if (evidenceLabel == value) return;
            evidenceLabel = value;
            if (nameText != null) nameText.text = objectName + "\n" + evidenceLabel;
        }

        public void SetObjectName(string value)
        {
            objectName = value;
            if (nameText != null) nameText.text = objectName + "\n" + evidenceLabel;
        }

        public bool CanSelect => isActiveAndEnabled && owner != null && owner.CanConfirm;

        public void Initialize(ManualObjectDemo demo, Transform head, Font font)
        {
            owner = demo;
            viewer = head;
            markerRenderer = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            var labelObject = new GameObject("Object label", typeof(RectTransform), typeof(Canvas));
            label = labelObject.transform;
            label.SetParent(transform, false);
            // Counter the marker's scale so label dimensions remain in metres.
            label.localScale = new Vector3(1f / transform.lossyScale.x, 1f / transform.lossyScale.y, 1f / transform.lossyScale.z) * 0.001f;
            labelObject.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            labelObject.GetComponent<RectTransform>().sizeDelta = new Vector2(450, 90);
            var text = ManualObjectDemo.AddText(labelObject.transform, "Name", font, 26, new Vector2(450, 90), Vector2.zero);
            nameText = text;
            text.alignment = TextAnchor.MiddleCenter;
            SetObjectName(objectName);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
        }

        public void Show(Vector3 position, Color colour)
        {
            transform.position = position;
            markerRenderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", colour);
            markerRenderer.SetPropertyBlock(properties);
            gameObject.SetActive(true);
        }

        private void LateUpdate()
        {
            if (label == null || viewer == null) return;
            label.position = transform.position + Vector3.up * 0.18f;
            var facing = label.position - viewer.position;
            if (facing.sqrMagnitude > 0.0001f) label.rotation = Quaternion.LookRotation(facing);
        }

        public void Select()
        {
            if (CanSelect) owner.ConfirmFound();
        }
    }
}
