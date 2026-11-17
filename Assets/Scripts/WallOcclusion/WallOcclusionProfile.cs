using System.Collections.Generic;
using UnityEngine;

namespace DisasterSquad.WallOcclusion
{
    /// <summary>
    /// Reusable configuration profile for the Wall Occlusion system.
    /// Create via Assets → Create → Wall Occlusion → Profile.
    /// Stores all settings that can be shared across scenes.
    /// Scene-specific references (targets, walls) live on WallOcclusionController.
    /// </summary>
    [CreateAssetMenu(fileName = "WallOcclusionProfile", menuName = "Wall Occlusion/Profile")]
    public class WallOcclusionProfile : ScriptableObject
    {
        // ── Fade Settings ────────────────────────────────────────────

        [Header("Fade Settings")]
        [Tooltip("Target alpha when a wall is faded (0 = fully invisible, 1 = opaque).")]
        [Range(0f, 1f)]
        public float fadeAlpha = 0.2f;

        [Tooltip("Speed of the fade transition. Higher = faster.")]
        [Min(0.1f)]
        public float fadeSpeed = 3f;

        // ── Detection ────────────────────────────────────────────────

        [Header("Detection")]
        [Tooltip("Detection method: VisualBounds (No Collider needed! Recommended) or PhysicsRaycast (Requires Colliders).")]
        public DetectionMethod detectionMethod = DetectionMethod.VisualBounds;

        [Tooltip("Tags used to auto-detect wall objects in the scene. " +
                 "Any Renderer whose GameObject carries one of these tags is treated as a wall candidate.")]
        public List<string> wallTags = new List<string>();

        [Tooltip("Physics layers included in the occlusion raycast (Only used if Detection Method is PhysicsRaycast).")]
        public LayerMask raycastLayerMask = ~0;

        [Tooltip("Offset added to the focus-target position before casting the ray " +
                 "(useful to aim at the character's chest instead of pivot/feet).")]
        public Vector3 targetOffset = new Vector3(0f, 1f, 0f);

        // ── Highlight ────────────────────────────────────────────────

        [Header("Highlight")]
        [Tooltip("Enable the outline-glow highlight on focus targets.")]
        public bool enableHighlight = true;

        [Tooltip("When should the highlight be visible?")]
        public HighlightMode highlightMode = HighlightMode.AlwaysOn;

        [Tooltip("Outline color.")]
        public Color highlightColor = new Color(1f, 0.8f, 0f, 1f);

        [Tooltip("Outline thickness (world-space extrusion).")]
        [Range(0.001f, 0.05f)]
        public float highlightWidth = 0.005f;

        // ── Advanced ─────────────────────────────────────────────────

        [Header("Advanced")]
        [Tooltip("Shader used for walls whose original shader cannot switch to transparent mode. " +
                 "Leave empty to auto-locate 'DisasterSquad/WallFade'.")]
        public Shader fallbackFadeShader;

        [Tooltip("Number of extra rays spread around the target for better coverage. " +
                 "0 = single centre ray only.")]
        [Range(0, 8)]
        public int extraRays = 0;

        [Tooltip("Spread radius (world units) of the extra rays around the target.")]
        [Min(0f)]
        public float extraRaySpread = 0.3f;
    }

    /// <summary>Controls when the character outline highlight is shown.</summary>
    public enum HighlightMode
    {
        /// <summary>Highlight is always visible on focus targets.</summary>
        AlwaysOn,
        /// <summary>Highlight only appears when the target is occluded by a wall.</summary>
        OnlyWhenOccluded
    }

    /// <summary>Controls how walls are detected.</summary>
    public enum DetectionMethod
    {
        /// <summary>Checks wall visual bounding boxes directly. No Colliders required.</summary>
        VisualBounds,
        /// <summary>Uses Unity Physics Raycast. Requires walls to have Colliders.</summary>
        PhysicsRaycast
    }
}

