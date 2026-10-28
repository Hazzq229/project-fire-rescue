using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DisasterSquad.WallOcclusion
{
    /// <summary>
    /// Attach to (or auto-added on) any wall that should be able to fade when
    /// occluding a focus target.  Handles material cloning, transparent-mode
    /// switching, smooth alpha animation and multi-camera reference counting.
    /// </summary>
    [DisallowMultipleComponent]
    public class WallFadeable : MonoBehaviour
    {
        // ── Runtime state ────────────────────────────────────────────

        private Renderer   _renderer;
        private Material[] _originalMaterials;   // shared-material snapshot
        private Material[] _fadeMaterials;       // per-instance clones
        private bool       _isInitialized;
        private bool       _isFading;
        private float      _currentAlpha = 1f;
        private float      _targetAlpha  = 1f;

        // Reference counting — each camera that needs this wall transparent
        // registers with its own unique id (GetInstanceID).
        private readonly HashSet<int> _requestingCameras = new HashSet<int>();

        // Config (set once via Initialize)
        private float  _fadeAlpha;
        private float  _fadeSpeed;
        private Shader _fallbackShader;

        // Cached data for restoring URP material state
        private struct MaterialSnapshot
        {
            public float       surface;
            public float       srcBlend;
            public float       dstBlend;
            public float       srcBlendAlpha;
            public float       dstBlendAlpha;
            public float       zWrite;
            public int         renderQueue;
            public string      renderType;
            public Color       baseColor;
            public bool        hadSurfaceProp;
            public Shader      originalShader;
        }
        private MaterialSnapshot[] _snapshots;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Must be called once before the component is used.</summary>
        public void Initialize(float fadeAlpha, float fadeSpeed, Shader fallbackShader)
        {
            if (_isInitialized) return;

            _fadeAlpha      = fadeAlpha;
            _fadeSpeed      = fadeSpeed;
            _fallbackShader = fallbackShader;

            _renderer = GetComponent<Renderer>();
            if (_renderer == null)
            {
                Debug.LogWarning($"[WallFadeable] No Renderer on '{name}'. Disabling.", this);
                enabled = false;
                return;
            }

            // Snapshot the original shared materials
            _originalMaterials = _renderer.sharedMaterials;

            // Create per-instance clones for fading
            _fadeMaterials = new Material[_originalMaterials.Length];
            _snapshots     = new MaterialSnapshot[_originalMaterials.Length];

            for (int i = 0; i < _originalMaterials.Length; i++)
            {
                Material orig = _originalMaterials[i];
                if (orig == null) continue;

                Material clone = new Material(orig);
                clone.name = $"{orig.name} (Fade)";
                _fadeMaterials[i] = clone;

                // Take a snapshot so we can restore if needed
                _snapshots[i] = TakeSnapshot(clone);
            }

            _isInitialized = true;
        }

        /// <summary>Update configuration at runtime (e.g. when profile changes).</summary>
        public void UpdateConfig(float fadeAlpha, float fadeSpeed)
        {
            _fadeAlpha = fadeAlpha;
            _fadeSpeed = fadeSpeed;
        }

        /// <summary>
        /// A camera requests this wall to be transparent.
        /// Call every frame from the controller while the wall is occluding.
        /// </summary>
        public void RequestFade(int cameraId)
        {
            if (!_isInitialized) return;
            _requestingCameras.Add(cameraId);
            _targetAlpha = _fadeAlpha;
        }

        /// <summary>
        /// A camera no longer needs this wall to be transparent.
        /// </summary>
        public void ReleaseFade(int cameraId)
        {
            if (!_isInitialized) return;
            _requestingCameras.Remove(cameraId);

            // Only restore if NO camera needs it faded
            if (_requestingCameras.Count == 0)
                _targetAlpha = 1f;
        }

        /// <summary>True while the wall has any transparency applied.</summary>
        public bool IsFading => _isFading;

        /// <summary>Current visual alpha (0–1).</summary>
        public float CurrentAlpha => _currentAlpha;

        // ── MonoBehaviour ────────────────────────────────────────────

        private void Update()
        {
            if (!_isInitialized) return;

            // Lerp towards target
            if (!Mathf.Approximately(_currentAlpha, _targetAlpha))
            {
                _currentAlpha = Mathf.MoveTowards(
                    _currentAlpha, _targetAlpha, _fadeSpeed * Time.deltaTime);

                if (_currentAlpha < 0.999f)
                {
                    if (!_isFading) EnableFadeMode();
                    ApplyAlpha(_currentAlpha);
                }
                else
                {
                    _currentAlpha = 1f;
                    if (_isFading) DisableFadeMode();
                }
            }
        }

        private void OnDestroy()
        {
            // Clean up cloned materials
            if (_fadeMaterials == null) return;
            foreach (var mat in _fadeMaterials)
                if (mat != null) Destroy(mat);
        }

        // ── Internal ─────────────────────────────────────────────────

        private void EnableFadeMode()
        {
            _isFading = true;

            for (int i = 0; i < _fadeMaterials.Length; i++)
            {
                Material mat = _fadeMaterials[i];
                if (mat == null) continue;

                if (_snapshots[i].hadSurfaceProp)
                {
                    // URP Lit / SimpleLit — switch surface type to Transparent
                    SetMaterialTransparent(mat);
                }
                else
                {
                    // Shader doesn't have _Surface (e.g. custom Shader Graph).
                    // Swap to fallback shader if available.
                    if (_fallbackShader != null)
                    {
                        // Copy main texture before swapping
                        Texture mainTex = mat.HasProperty("_BaseMap")
                            ? mat.GetTexture("_BaseMap")
                            : mat.HasProperty("_MainTex")
                                ? mat.GetTexture("_MainTex")
                                : null;

                        Color col = mat.HasProperty("_BaseColor")
                            ? mat.GetColor("_BaseColor")
                            : mat.HasProperty("_Color")
                                ? mat.GetColor("_Color")
                                : Color.white;

                        mat.shader = _fallbackShader;

                        if (mainTex != null)
                            mat.SetTexture("_BaseMap", mainTex);
                        mat.SetColor("_BaseColor", col);
                    }
                }
            }

            _renderer.materials = _fadeMaterials;
        }

        private void DisableFadeMode()
        {
            _isFading = false;

            // Restore original shared materials
            _renderer.sharedMaterials = _originalMaterials;

            // Reset the clones so they're ready for the next fade
            for (int i = 0; i < _fadeMaterials.Length; i++)
            {
                Material mat = _fadeMaterials[i];
                if (mat == null) continue;

                if (_snapshots[i].hadSurfaceProp)
                {
                    RestoreSnapshot(mat, _snapshots[i]);
                }
                else if (mat.shader != _snapshots[i].originalShader)
                {
                    // Restore original shader on the clone
                    mat.shader = _snapshots[i].originalShader;
                }
            }
        }

        private void ApplyAlpha(float alpha)
        {
            foreach (var mat in _fadeMaterials)
            {
                if (mat == null) continue;

                if (mat.HasProperty("_FadeAlpha"))
                {
                    // Our WallFade shader
                    mat.SetFloat("_FadeAlpha", alpha);
                }

                if (mat.HasProperty("_BaseColor"))
                {
                    Color c = mat.GetColor("_BaseColor");
                    c.a = alpha;
                    mat.SetColor("_BaseColor", c);
                }
                else if (mat.HasProperty("_Color"))
                {
                    Color c = mat.GetColor("_Color");
                    c.a = alpha;
                    mat.SetColor("_Color", c);
                }
            }
        }

        // ── URP material helpers ─────────────────────────────────────

        private static MaterialSnapshot TakeSnapshot(Material mat)
        {
            bool hasSurface = mat.HasProperty("_Surface");
            return new MaterialSnapshot
            {
                hadSurfaceProp = hasSurface,
                surface        = hasSurface ? mat.GetFloat("_Surface") : 0f,
                srcBlend       = mat.HasProperty("_SrcBlend")      ? mat.GetFloat("_SrcBlend")      : 1f,
                dstBlend       = mat.HasProperty("_DstBlend")      ? mat.GetFloat("_DstBlend")      : 0f,
                srcBlendAlpha  = mat.HasProperty("_SrcBlendAlpha") ? mat.GetFloat("_SrcBlendAlpha") : 1f,
                dstBlendAlpha  = mat.HasProperty("_DstBlendAlpha") ? mat.GetFloat("_DstBlendAlpha") : 0f,
                zWrite         = mat.HasProperty("_ZWrite")        ? mat.GetFloat("_ZWrite")        : 1f,
                renderQueue    = mat.renderQueue,
                renderType     = mat.GetTag("RenderType", false, "Opaque"),
                baseColor      = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor")
                               : mat.HasProperty("_Color")     ? mat.GetColor("_Color")
                               : Color.white,
                originalShader = mat.shader
            };
        }

        private static void RestoreSnapshot(Material mat, in MaterialSnapshot snap)
        {
            mat.SetFloat("_Surface", snap.surface);
            if (mat.HasProperty("_SrcBlend"))      mat.SetFloat("_SrcBlend",      snap.srcBlend);
            if (mat.HasProperty("_DstBlend"))      mat.SetFloat("_DstBlend",      snap.dstBlend);
            if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", snap.srcBlendAlpha);
            if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", snap.dstBlendAlpha);
            if (mat.HasProperty("_ZWrite"))        mat.SetFloat("_ZWrite",        snap.zWrite);
            mat.renderQueue = snap.renderQueue;
            mat.SetOverrideTag("RenderType", snap.renderType);

            if (snap.surface < 0.5f)
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");

            // Restore base color with original alpha
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", snap.baseColor);
            else if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", snap.baseColor);
        }

        private static void SetMaterialTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_SrcBlend"))
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend"))
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_SrcBlendAlpha"))
                mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
            if (mat.HasProperty("_DstBlendAlpha"))
                mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetOverrideTag("RenderType", "Transparent");
        }
    }
}

