using System.Collections.Generic;
using Project.Core.Interfaces;
using UnityEngine;
using UnityEngine.Rendering;

namespace Project.Core.Integration
{
    public sealed class DefaultGuidanceRenderer : MonoBehaviour, IWayfindingRenderer
    {
        [SerializeField] private Material material;
        private LineRenderer line;
        private LineRenderer ring;
        private GameObject arrows;
        private Mesh arrowMesh;
        public void Configure(Material guidanceMaterial) => material = guidanceMaterial;

        public void DisplayGuidancePath(Vector3[] waypoints)
        {
            if (waypoints == null || waypoints.Length == 0 || !EnsureObjects()) { ClearGuidancePath(); return; }
            foreach (var point in waypoints)
                if (float.IsNaN(point.x) || float.IsNaN(point.y) || float.IsNaN(point.z)
                    || float.IsInfinity(point.x) || float.IsInfinity(point.y) || float.IsInfinity(point.z))
                { ClearGuidancePath(); return; }
            line.positionCount = waypoints.Length;
            for (var i = 0; i < waypoints.Length; i++) line.SetPosition(i, waypoints[i] + Vector3.up * 0.035f);
            line.enabled = waypoints.Length > 1;
            var goal = waypoints[waypoints.Length - 1];
            ring.positionCount = 32;
            for (var i = 0; i < 32; i++)
            {
                var angle = i * Mathf.PI * 2f / 32f;
                ring.SetPosition(i, goal + new Vector3(Mathf.Cos(angle) * 0.15f, 0.04f, Mathf.Sin(angle) * 0.15f));
            }
            ring.enabled = true;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            var travelled = 0f;
            var nextArrow = 0.35f;
            for (var i = 1; i < waypoints.Length; i++)
            {
                var delta = waypoints[i] - waypoints[i - 1];
                var length = delta.magnitude;
                var forward = Vector3.ProjectOnPlane(delta, Vector3.up).normalized;
                var right = Vector3.Cross(Vector3.up, forward);
                while (nextArrow <= travelled + length && length > 0.001f)
                {
                    var onSegment = nextArrow - travelled;
                    if (onSegment > 0.15f && onSegment < length - 0.15f)
                    {
                        var center = waypoints[i - 1] + delta * (onSegment / length) + Vector3.up * 0.055f;
                        var index = vertices.Count;
                        vertices.Add(arrows.transform.InverseTransformPoint(center + forward * 0.12f));
                        vertices.Add(arrows.transform.InverseTransformPoint(center - forward * 0.07f - right * 0.07f));
                        vertices.Add(arrows.transform.InverseTransformPoint(center - forward * 0.07f + right * 0.07f));
                        triangles.Add(index); triangles.Add(index + 2); triangles.Add(index + 1);
                    }
                    nextArrow += 0.6f;
                }
                travelled += length;
            }
            arrowMesh.Clear();
            arrowMesh.SetVertices(vertices);
            arrowMesh.SetTriangles(triangles, 0);
            arrowMesh.RecalculateBounds();
            arrows.SetActive(vertices.Count > 0);
        }

        private bool EnsureObjects()
        {
            if (material == null) { Debug.LogError("[SpatialSense Guidance] No material assigned.", this); return false; }
            if (line != null) return true;
            line = MakeLine("Walking route", new Color(0.05f, 0.55f, 1f), 0.025f);
            ring = MakeLine("Standing position", Color.green, 0.018f);
            ring.loop = true;
            arrows = new GameObject("Route arrows", typeof(MeshFilter), typeof(MeshRenderer));
            arrows.transform.SetParent(transform, false);
            var renderer = arrows.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", new Color(0.05f, 0.55f, 1f));
            renderer.SetPropertyBlock(properties);
            arrowMesh = new Mesh { name = "Route arrow geometry" };
            arrows.GetComponent<MeshFilter>().sharedMesh = arrowMesh;
            return true;
        }

        private LineRenderer MakeLine(string name, Color colour, float width)
        {
            var result = new GameObject(name).AddComponent<LineRenderer>();
            result.transform.SetParent(transform, false);
            result.sharedMaterial = material;
            result.useWorldSpace = true;
            result.widthMultiplier = width;
            result.shadowCastingMode = ShadowCastingMode.Off;
            result.receiveShadows = false;
            var properties = new MaterialPropertyBlock();
            properties.SetColor("_BaseColor", colour);
            result.SetPropertyBlock(properties);
            result.enabled = false;
            return result;
        }

        public void ClearGuidancePath()
        {
            if (line != null) line.enabled = false;
            if (ring != null) ring.enabled = false;
            if (arrows != null) arrows.SetActive(false);
        }

        private void OnDisable() => ClearGuidancePath();
        private void OnDestroy() { if (arrowMesh != null) Destroy(arrowMesh); }
    }
}
