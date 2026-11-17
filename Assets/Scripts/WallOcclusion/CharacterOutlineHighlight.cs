using System.Collections.Generic;
using UnityEngine;

namespace DisasterSquad.WallOcclusion
{
    /// <summary>
    /// Manages the outline-glow highlight on a focus-target character.
    /// Creates duplicate renderers (SkinnedMeshRenderer / MeshRenderer)
    /// with the outline shader for a clean back-face-extrusion outline.
    /// </summary>
    [DisallowMultipleComponent]
    public class CharacterOutlineHighlight : MonoBehaviour
    {
        // ── Configuration ────────────────────────────────────────────

        private Color _outlineColor = new Color(1f, 0.8f, 0f, 1f);
        private float _outlineWidth = 0.005f;
        private HighlightMode _highlightMode = HighlightMode.AlwaysOn;

        // ── Runtime state ────────────────────────────────────────────

        private Material _outlineMaterial;
        private readonly List<Renderer> _outlineRenderers = new List<Renderer>();
        private bool _isInitialized;
        private bool _isHighlightVisible;
        private bool _isOccluded;

        // Unique outline shader reference
        private static Shader s_outlineShader;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Initialize the highlight system.  Call once after the component is added.
        /// </summary>
        public void Initialize(Color color, float width, HighlightMode mode)
        {
            if (_isInitialized) return;

            _outlineColor  = color;
            _outlineWidth  = width;
            _highlightMode = mode;

            // Find the outline shader
            if (s_outlineShader == null)
                s_outlineShader = Shader.Find("DisasterSquad/CharacterOutline");

            if (s_outlineShader == null)
            {
                Debug.LogError("[CharacterOutlineHighlight] Cannot find 'DisasterSquad/CharacterOutline' shader!", this);
                enabled = false;
                return;
            }

            // Create outline material
            _outlineMaterial = new Material(s_outlineShader)
            {
                name = "Outline Material (Runtime)"
            };
            _outlineMaterial.SetColor("_OutlineColor", _outlineColor);
            _outlineMaterial.SetFloat("_OutlineWidth", _outlineWidth);

            // Build outline renderers from existing renderers
            BuildOutlineRenderers();

            _isInitialized = true;

            // Apply initial visibility
            UpdateVisibility();
        }

        /// <summary>Update outline appearance at runtime.</summary>
        public void UpdateConfig(Color color, float width, HighlightMode mode)
        {
            _outlineColor  = color;
            _outlineWidth  = width;
            _highlightMode = mode;

            if (_outlineMaterial != null)
            {
                _outlineMaterial.SetColor("_OutlineColor", _outlineColor);
                _outlineMaterial.SetFloat("_OutlineWidth", _outlineWidth);
            }

            UpdateVisibility();
        }

        /// <summary>
        /// Called by WallOcclusionController to notify whether this character
        /// is currently occluded by a wall.
        /// </summary>
        public void SetOccluded(bool occluded)
        {
            if (_isOccluded == occluded) return;
            _isOccluded = occluded;
            UpdateVisibility();
        }

        /// <summary>Whether the outline is currently being rendered.</summary>
        public bool IsHighlightVisible => _isHighlightVisible;

        // ── MonoBehaviour ────────────────────────────────────────────

        private void OnDestroy()
        {
            // Clean up outline renderers and material
            foreach (var r in _outlineRenderers)
            {
                if (r != null && r.gameObject != null)
                    Destroy(r.gameObject);
            }
            _outlineRenderers.Clear();

            if (_outlineMaterial != null)
                Destroy(_outlineMaterial);
        }

        // ── Internal ─────────────────────────────────────────────────

        private void BuildOutlineRenderers()
        {
            // Find all renderers on the character (including children)
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);

            foreach (Renderer rend in renderers)
            {
                // Skip renderers that are already outline renderers
                if (rend.gameObject.name.EndsWith("(Outline)"))
                    continue;

                // Skip particle systems, trail renderers, etc.
                if (rend is ParticleSystemRenderer || rend is TrailRenderer ||
                    rend is LineRenderer)
                    continue;

                Renderer outlineRenderer = null;

                if (rend is SkinnedMeshRenderer smr)
                {
                    outlineRenderer = CreateSkinnedOutline(smr);
                }
                else if (rend is MeshRenderer mr)
                {
                    MeshFilter mf = mr.GetComponent<MeshFilter>();
                    if (mf != null && mf.sharedMesh != null)
                        outlineRenderer = CreateMeshOutline(mr, mf);
                }

                if (outlineRenderer != null)
                    _outlineRenderers.Add(outlineRenderer);
            }
        }

        private Renderer CreateSkinnedOutline(SkinnedMeshRenderer source)
        {
            GameObject outlineObj = new GameObject($"{source.gameObject.name} (Outline)");
            outlineObj.transform.SetParent(source.transform, false);
            outlineObj.transform.localPosition = Vector3.zero;
            outlineObj.transform.localRotation = Quaternion.identity;
            outlineObj.transform.localScale    = Vector3.one;

            var outlineSMR = outlineObj.AddComponent<SkinnedMeshRenderer>();
            outlineSMR.sharedMesh          = source.sharedMesh;
            outlineSMR.bones               = source.bones;
            outlineSMR.rootBone            = source.rootBone;
            outlineSMR.quality             = source.quality;
            outlineSMR.updateWhenOffscreen = source.updateWhenOffscreen;

            // Assign outline material — one for each sub-mesh
            int subMeshCount = source.sharedMesh != null ? source.sharedMesh.subMeshCount : 1;
            Material[] mats  = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                mats[i] = _outlineMaterial;
            outlineSMR.sharedMaterials = mats;

            outlineSMR.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            outlineSMR.receiveShadows       = false;
            outlineSMR.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
            outlineSMR.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            outlineObj.SetActive(false);
            return outlineSMR;
        }

        private Renderer CreateMeshOutline(MeshRenderer source, MeshFilter sourceMF)
        {
            GameObject outlineObj = new GameObject($"{source.gameObject.name} (Outline)");
            outlineObj.transform.SetParent(source.transform, false);
            outlineObj.transform.localPosition = Vector3.zero;
            outlineObj.transform.localRotation = Quaternion.identity;
            outlineObj.transform.localScale    = Vector3.one;

            var mf = outlineObj.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMF.sharedMesh;

            var mr = outlineObj.AddComponent<MeshRenderer>();

            int subMeshCount = sourceMF.sharedMesh != null ? sourceMF.sharedMesh.subMeshCount : 1;
            Material[] mats  = new Material[subMeshCount];
            for (int i = 0; i < subMeshCount; i++)
                mats[i] = _outlineMaterial;
            mr.sharedMaterials = mats;

            mr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows       = false;
            mr.lightProbeUsage      = UnityEngine.Rendering.LightProbeUsage.Off;
            mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

            outlineObj.SetActive(false);
            return mr;
        }

        private void UpdateVisibility()
        {
            bool shouldShow;

            switch (_highlightMode)
            {
                case HighlightMode.AlwaysOn:
                    shouldShow = true;
                    break;
                case HighlightMode.OnlyWhenOccluded:
                    shouldShow = _isOccluded;
                    break;
                default:
                    shouldShow = false;
                    break;
            }

            if (_isHighlightVisible == shouldShow) return;
            _isHighlightVisible = shouldShow;

            foreach (var r in _outlineRenderers)
            {
                if (r != null && r.gameObject != null)
                    r.gameObject.SetActive(shouldShow);
            }
        }

#if UNITY_EDITOR
        /// <summary>Editor gizmo — draws a diamond icon above tracked targets.</summary>
        private void OnDrawGizmos()
        {
            Gizmos.color = _isInitialized ? _outlineColor : Color.yellow;
            Vector3 pos = transform.position + Vector3.up * 2.2f;
            Gizmos.DrawWireSphere(pos, 0.15f);
            Gizmos.DrawLine(transform.position + Vector3.up * 2f, pos);
        }
#endif
    }
}

