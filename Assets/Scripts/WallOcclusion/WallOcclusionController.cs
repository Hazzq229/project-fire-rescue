using System.Collections.Generic;
using UnityEngine;

namespace DisasterSquad.WallOcclusion
{
    /// <summary>
    /// Main runtime controller for the Wall Occlusion system.
    /// Attach to any GameObject with a Camera, OR to a parent/manager object
    /// and manually assign the camera reference.
    /// Supports per-player split-screen.
    ///
    /// Reads shared settings from a <see cref="WallOcclusionProfile"/>
    /// and holds scene-specific references (focus targets, manual walls).
    /// </summary>
    [DisallowMultipleComponent]
    public class WallOcclusionController : MonoBehaviour
    {
        // ── Inspector fields ─────────────────────────────────────────

        [Header("Profile")]
        [Tooltip("Shared settings profile (fade speed, highlight, tags, etc.).")]
        [SerializeField] private WallOcclusionProfile _profile;

        [Header("Camera")]
        [Tooltip("The camera used for raycasting. If left empty, will try GetComponent<Camera>(), " +
                 "then GetComponentInChildren<Camera>(), then Camera.main.")]
        [SerializeField] private Camera _cameraOverride;

        [Header("Focus Targets")]
        [Tooltip("Characters whose visibility must be preserved. " +
                 "Raycast goes from this camera to each target.")]
        [SerializeField] private List<Transform> _focusTargets = new List<Transform>();

        [Header("Walls")]
        [Tooltip("Specific wall Renderers that can become transparent (manual list).")]
        [SerializeField] private List<Renderer> _manualWalls = new List<Renderer>();

        [Tooltip("Also consider walls found via tags defined in the profile.")]
        [SerializeField] private bool _autoDetectByTag = true;

        // ── Runtime state ────────────────────────────────────────────

        private Camera _cam;
        private int    _camId;

        // All known wall renderers (manual + tag-based), cached
        private readonly HashSet<Renderer> _allWalls = new HashSet<Renderer>();

        // Walls currently fading this frame
        private readonly HashSet<Renderer> _currentlyOccluding = new HashSet<Renderer>();

        // Previous frame's occluding set (for change detection)
        private readonly HashSet<Renderer> _previouslyOccluding = new HashSet<Renderer>();

        // Cached WallFadeable components (created / fetched lazily)
        private readonly Dictionary<Renderer, WallFadeable> _fadeableCache
            = new Dictionary<Renderer, WallFadeable>();

        // Cached highlight components
        private readonly Dictionary<Transform, CharacterOutlineHighlight> _highlightCache
            = new Dictionary<Transform, CharacterOutlineHighlight>();

        // Targets that are occluded this frame
        private readonly HashSet<Transform> _occludedTargets = new HashSet<Transform>();

        // Fallback shader (resolved once)
        private Shader _resolvedFallbackShader;

        // Ray hit buffer (non-alloc)
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[64];

        // Tag-detected walls refresh timer
        private float _tagRefreshTimer;
        private const float TAG_REFRESH_INTERVAL = 1f; // seconds

        // ── Public API ───────────────────────────────────────────────

        /// <summary>The profile driving this controller.</summary>
        public WallOcclusionProfile Profile
        {
            get => _profile;
            set => _profile = value;
        }

        /// <summary>Live list of focus targets. Modify freely at runtime.</summary>
        public List<Transform> FocusTargets => _focusTargets;

        /// <summary>Live list of manually-assigned walls.</summary>
        public List<Renderer> ManualWalls => _manualWalls;

        /// <summary>Add a focus target at runtime.</summary>
        public void AddFocusTarget(Transform target)
        {
            if (target != null && !_focusTargets.Contains(target))
            {
                _focusTargets.Add(target);
                EnsureHighlight(target);
            }
        }

        /// <summary>Remove a focus target at runtime.</summary>
        public void RemoveFocusTarget(Transform target)
        {
            _focusTargets.Remove(target);
            if (_highlightCache.TryGetValue(target, out var hl))
            {
                if (hl != null) Destroy(hl);
                _highlightCache.Remove(target);
            }
        }

        /// <summary>Add a wall at runtime.</summary>
        public void AddWall(Renderer wall)
        {
            if (wall != null)
            {
                if (!_manualWalls.Contains(wall))
                    _manualWalls.Add(wall);
                _allWalls.Add(wall);
            }
        }

        /// <summary>Remove a wall at runtime.</summary>
        public void RemoveWall(Renderer wall)
        {
            _manualWalls.Remove(wall);
            _allWalls.Remove(wall);
            ReleaseFadeable(wall);
        }

        // ── MonoBehaviour ────────────────────────────────────────────

        private void Awake()
        {
            ResolveCamera();
            _camId = GetInstanceID();
        }

        /// <summary>Finds the camera to use for raycasting.</summary>
        private void ResolveCamera()
        {
            if (_cameraOverride != null)
            {
                _cam = _cameraOverride;
                return;
            }

            _cam = GetComponent<Camera>();
            if (_cam != null) return;

            _cam = GetComponentInChildren<Camera>();
            if (_cam != null) return;

            _cam = Camera.main;

            if (_cam == null)
                Debug.LogError("[WallOcclusion] No Camera found! Assign one in the inspector.", this);
        }

        private void Start()
        {
            ResolveFallbackShader();
            RebuildWallSet();
            InitializeHighlights();
        }

        private void LateUpdate()
        {
            if (_profile == null || _cam == null || _focusTargets.Count == 0) return;

            // Periodically refresh tag-based walls
            _tagRefreshTimer -= Time.deltaTime;
            if (_tagRefreshTimer <= 0f)
            {
                _tagRefreshTimer = TAG_REFRESH_INTERVAL;
                RebuildWallSet();
            }

            // ── Detect occlusions ────────────────────────────────────
            _currentlyOccluding.Clear();
            _occludedTargets.Clear();

            Vector3 camPos = _cam.transform.position;

            foreach (Transform target in _focusTargets)
            {
                if (target == null) continue;

                bool targetOccluded = false;

                Vector3 targetPos = target.position + _profile.targetOffset;
                Vector3 direction = targetPos - camPos;
                float   distance  = direction.magnitude;

                if (distance < 0.01f) continue;

                // Centre ray
                CastAndCollect(camPos, direction.normalized, distance, ref targetOccluded);

                // Extra rays for better coverage
                if (_profile.extraRays > 0)
                {
                    CastExtraRays(camPos, targetPos, distance, ref targetOccluded);
                }

                if (targetOccluded)
                    _occludedTargets.Add(target);
            }

            // ── Fade walls ───────────────────────────────────────────
            // Release walls that are no longer occluding
            foreach (Renderer wall in _previouslyOccluding)
            {
                if (!_currentlyOccluding.Contains(wall))
                {
                    if (_fadeableCache.TryGetValue(wall, out var fadeable) && fadeable != null)
                        fadeable.ReleaseFade(_camId);
                }
            }

            // Request fade on newly occluding walls
            foreach (Renderer wall in _currentlyOccluding)
            {
                WallFadeable fadeable = GetOrCreateFadeable(wall);
                if (fadeable != null)
                    fadeable.RequestFade(_camId);
            }

            // Swap sets
            _previouslyOccluding.Clear();
            foreach (var w in _currentlyOccluding)
                _previouslyOccluding.Add(w);

            // ── Update highlights ────────────────────────────────────
            UpdateHighlights();
        }

        private void OnDisable()
        {
            // Release all fades when the controller is disabled
            foreach (Renderer wall in _previouslyOccluding)
            {
                if (_fadeableCache.TryGetValue(wall, out var fadeable) && fadeable != null)
                    fadeable.ReleaseFade(_camId);
            }
            _previouslyOccluding.Clear();
        }

        // ── Raycasting / Occlusion Detection ─────────────────────────

        private void CastAndCollect(Vector3 origin, Vector3 direction, float maxDist,
                                    ref bool targetOccluded)
        {
            if (_profile != null && _profile.detectionMethod == DetectionMethod.PhysicsRaycast)
            {
                int hitCount = Physics.RaycastNonAlloc(
                    origin, direction, _hitBuffer, maxDist, _profile.raycastLayerMask,
                    QueryTriggerInteraction.Ignore);

                for (int i = 0; i < hitCount; i++)
                {
                    Renderer hitRenderer = _hitBuffer[i].collider.GetComponentInParent<Renderer>();

                    if (hitRenderer == null) continue;
                    if (!_allWalls.Contains(hitRenderer)) continue;

                    // This wall is occluding
                    _currentlyOccluding.Add(hitRenderer);
                    targetOccluded = true;
                }
            }
            else
            {
                // VisualBounds detection — NO COLLIDER NEEDED!
                // Checks directly whether the view ray intersects the wall's visual bounding box.
                Ray ray = new Ray(origin, direction);

                foreach (Renderer wall in _allWalls)
                {
                    if (wall == null || !wall.gameObject.activeInHierarchy) continue;

                    Bounds bounds = wall.bounds;

                    // If camera is inside the bounding box
                    if (bounds.Contains(origin))
                    {
                        _currentlyOccluding.Add(wall);
                        targetOccluded = true;
                        continue;
                    }

                    // Check if ray intersects the bounding box between camera and target
                    if (bounds.IntersectRay(ray, out float hitDist))
                    {
                        if (hitDist > 0.01f && hitDist < maxDist - 0.05f)
                        {
                            _currentlyOccluding.Add(wall);
                            targetOccluded = true;
                        }
                    }
                }
            }
        }

        private void CastExtraRays(Vector3 camPos, Vector3 targetPos, float distance,
                                   ref bool targetOccluded)
        {
            float spread = _profile.extraRaySpread;
            int   count  = _profile.extraRays;

            // Distribute extra rays in a circle around the target
            Vector3 camForward = (targetPos - camPos).normalized;
            Vector3 camRight   = Vector3.Cross(Vector3.up, camForward).normalized;
            Vector3 camUp      = Vector3.Cross(camForward, camRight).normalized;

            for (int i = 0; i < count; i++)
            {
                float angle    = (360f / count) * i * Mathf.Deg2Rad;
                Vector3 offset = (camRight * Mathf.Cos(angle) + camUp * Mathf.Sin(angle)) * spread;
                Vector3 point  = targetPos + offset;
                Vector3 dir    = (point - camPos).normalized;
                float   dist   = Vector3.Distance(camPos, point);

                CastAndCollect(camPos, dir, dist, ref targetOccluded);
            }
        }

        // ── Wall management ──────────────────────────────────────────

        private void RebuildWallSet()
        {
            _allWalls.Clear();

            // Add manual walls
            foreach (var wall in _manualWalls)
            {
                if (wall != null)
                {
                    _allWalls.Add(wall);

                    // Also check for any child renderers in case a parent object was referenced
                    Renderer[] childRends = wall.GetComponentsInChildren<Renderer>();
                    foreach (var cr in childRends)
                    {
                        if (cr != null) _allWalls.Add(cr);
                    }
                }
            }

            // Add tag-based walls
            if (_autoDetectByTag && _profile != null)
            {
                foreach (string tag in _profile.wallTags)
                {
                    if (string.IsNullOrEmpty(tag)) continue;

                    GameObject[] tagged;
                    try
                    {
                        tagged = GameObject.FindGameObjectsWithTag(tag);
                    }
                    catch (UnityException)
                    {
                        Debug.LogWarning(
                            $"[WallOcclusion] Tag '{tag}' is not defined. " +
                            "Add it in Project Settings → Tags & Layers.", this);
                        continue;
                    }

                    foreach (var go in tagged)
                    {
                        Renderer rend = go.GetComponent<Renderer>();
                        if (rend != null)
                            _allWalls.Add(rend);

                        // Also grab child renderers (for composite walls)
                        Renderer[] childRends = go.GetComponentsInChildren<Renderer>();
                        foreach (var cr in childRends)
                            _allWalls.Add(cr);
                    }
                }
            }
        }

        private WallFadeable GetOrCreateFadeable(Renderer wall)
        {
            if (_fadeableCache.TryGetValue(wall, out var existing) && existing != null)
                return existing;

            WallFadeable fadeable = wall.GetComponent<WallFadeable>();
            if (fadeable == null)
                fadeable = wall.gameObject.AddComponent<WallFadeable>();

            fadeable.Initialize(_profile.fadeAlpha, _profile.fadeSpeed, _resolvedFallbackShader);
            _fadeableCache[wall] = fadeable;
            return fadeable;
        }

        private void ReleaseFadeable(Renderer wall)
        {
            if (_fadeableCache.TryGetValue(wall, out var fadeable))
            {
                if (fadeable != null) fadeable.ReleaseFade(_camId);
                _fadeableCache.Remove(wall);
            }
        }

        // ── Highlight management ─────────────────────────────────────

        private void InitializeHighlights()
        {
            if (_profile == null || !_profile.enableHighlight) return;

            foreach (Transform target in _focusTargets)
            {
                if (target != null) EnsureHighlight(target);
            }
        }

        private void EnsureHighlight(Transform target)
        {
            if (!_profile.enableHighlight) return;
            if (_highlightCache.ContainsKey(target)) return;

            var hl = target.GetComponent<CharacterOutlineHighlight>();
            if (hl == null)
                hl = target.gameObject.AddComponent<CharacterOutlineHighlight>();

            hl.Initialize(_profile.highlightColor, _profile.highlightWidth,
                          _profile.highlightMode);

            _highlightCache[target] = hl;
        }

        private void UpdateHighlights()
        {
            if (_profile == null || !_profile.enableHighlight) return;

            foreach (Transform target in _focusTargets)
            {
                if (target == null) continue;

                EnsureHighlight(target);

                if (_highlightCache.TryGetValue(target, out var hl) && hl != null)
                {
                    bool isOccluded = _occludedTargets.Contains(target);
                    hl.SetOccluded(isOccluded);
                    hl.UpdateConfig(_profile.highlightColor, _profile.highlightWidth,
                                   _profile.highlightMode);
                }
            }
        }

        // ── Utility ──────────────────────────────────────────────────

        private void ResolveFallbackShader()
        {
            if (_profile != null && _profile.fallbackFadeShader != null)
            {
                _resolvedFallbackShader = _profile.fallbackFadeShader;
            }
            else
            {
                _resolvedFallbackShader = Shader.Find("DisasterSquad/WallFade");
            }

            if (_resolvedFallbackShader == null)
                Debug.LogWarning("[WallOcclusion] Fallback shader 'DisasterSquad/WallFade' not found. " +
                                 "Walls with non-URP shaders may not fade correctly.", this);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_profile == null) return;

            // Resolve camera for editor gizmos
            Camera gizmoCam = _cameraOverride != null ? _cameraOverride
                : GetComponent<Camera>() ?? GetComponentInChildren<Camera>();
            if (gizmoCam == null) return;

            Vector3 camPos = gizmoCam.transform.position;

            foreach (Transform target in _focusTargets)
            {
                if (target == null) continue;

                Vector3 targetPos = target.position + _profile.targetOffset;
                bool isOccluded = _occludedTargets.Contains(target);

                // Draw ray line
                Gizmos.color = isOccluded
                    ? new Color(1f, 0.2f, 0.2f, 0.8f)   // red = occluded
                    : new Color(0.2f, 1f, 0.2f, 0.4f);   // green = clear

                Gizmos.DrawLine(camPos, targetPos);
                Gizmos.DrawWireSphere(targetPos, 0.1f);
            }

            // Draw wall bounds
            Gizmos.color = new Color(1f, 0.5f, 0f, 0.25f);
            foreach (Renderer wall in _allWalls)
            {
                if (wall != null)
                    Gizmos.DrawWireCube(wall.bounds.center, wall.bounds.size);
            }
        }
#endif
    }
}

