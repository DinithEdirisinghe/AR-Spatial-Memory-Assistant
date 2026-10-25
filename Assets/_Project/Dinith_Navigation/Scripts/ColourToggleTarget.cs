using UnityEngine;

namespace SpatialSense.Bootstrap
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Renderer))]
    public sealed class ColourToggleTarget : MonoBehaviour, SpatialSense.Interaction.IPointerTarget
    {
        [SerializeField] private Color selectedColour = new Color(0.1f, 0.45f, 1f);
        private Renderer targetRenderer;
        private MaterialPropertyBlock properties;
        private Color originalColour;
        private int colourProperty;
        private bool selected;
        public bool CanSelect => isActiveAndEnabled;

        private void Awake()
        {
            targetRenderer = GetComponent<Renderer>();
            properties = new MaterialPropertyBlock();
            var material = targetRenderer.sharedMaterial;
            if (material == null) { enabled = false; return; }
            colourProperty = Shader.PropertyToID(material.HasProperty("_BaseColor") ? "_BaseColor" : "_Color");
            originalColour = material.GetColor(colourProperty);
        }

        public void Select()
        {
            if (!enabled || properties == null) return;
            selected = !selected;
            targetRenderer.GetPropertyBlock(properties);
            properties.SetColor(colourProperty, selected ? selectedColour : originalColour);
            targetRenderer.SetPropertyBlock(properties);
            Debug.Log($"[SpatialSense] Selected {name}: {(selected ? "blue" : "original colour")}", this);
        }
    }
}
