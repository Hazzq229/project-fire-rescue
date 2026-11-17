using UnityEditor;
using UnityEngine;
using System.Linq;
using DisasterSquad.WallOcclusion;

namespace DisasterSquad.WallOcclusion.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="WallOcclusionProfile"/>.
    /// Provides a polished UI with tag picker dropdown and section headers.
    /// </summary>
    [CustomEditor(typeof(WallOcclusionProfile))]
    public class WallOcclusionProfileEditor : UnityEditor.Editor
    {
        // Serialized properties
        private SerializedProperty _fadeAlpha;
        private SerializedProperty _fadeSpeed;
        private SerializedProperty _detectionMethod;
        private SerializedProperty _wallTags;
        private SerializedProperty _raycastLayerMask;
        private SerializedProperty _targetOffset;
        private SerializedProperty _enableHighlight;
        private SerializedProperty _highlightMode;
        private SerializedProperty _highlightColor;
        private SerializedProperty _highlightWidth;
        private SerializedProperty _fallbackFadeShader;
        private SerializedProperty _extraRays;
        private SerializedProperty _extraRaySpread;

        // Foldout states
        private bool _fadeFoldout    = true;
        private bool _detectFoldout  = true;
        private bool _hlFoldout      = true;
        private bool _advancedFoldout = false;

        // Tag selector
        private int _selectedTagIndex;

        // Styles
        private static GUIStyle s_headerStyle;
        private static GUIStyle s_boxStyle;

        private void OnEnable()
        {
            _fadeAlpha          = serializedObject.FindProperty("fadeAlpha");
            _fadeSpeed          = serializedObject.FindProperty("fadeSpeed");
            _detectionMethod    = serializedObject.FindProperty("detectionMethod");
            _wallTags           = serializedObject.FindProperty("wallTags");
            _raycastLayerMask   = serializedObject.FindProperty("raycastLayerMask");
            _targetOffset       = serializedObject.FindProperty("targetOffset");
            _enableHighlight    = serializedObject.FindProperty("enableHighlight");
            _highlightMode      = serializedObject.FindProperty("highlightMode");
            _highlightColor     = serializedObject.FindProperty("highlightColor");
            _highlightWidth     = serializedObject.FindProperty("highlightWidth");
            _fallbackFadeShader = serializedObject.FindProperty("fallbackFadeShader");
            _extraRays          = serializedObject.FindProperty("extraRays");
            _extraRaySpread     = serializedObject.FindProperty("extraRaySpread");
        }

        public override void OnInspectorGUI()
        {
            InitStyles();
            serializedObject.Update();

            EditorGUILayout.Space(4);
            DrawHeader("🧱  Wall Occlusion Profile");
            EditorGUILayout.Space(8);

            // ── Fade Settings ────────────────────────────────────────
            _fadeFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_fadeFoldout, "Fade Settings");
            if (_fadeFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    EditorGUILayout.PropertyField(_fadeAlpha,
                        new GUIContent("Target Alpha", "How transparent the wall becomes (0 = invisible)."));

                    EditorGUILayout.PropertyField(_fadeSpeed,
                        new GUIContent("Fade Speed", "Transition speed. Higher = faster."));

                    // Preview bar
                    EditorGUILayout.Space(4);
                    Rect previewRect = EditorGUILayout.GetControlRect(false, 20);
                    float alpha = _fadeAlpha.floatValue;
                    EditorGUI.DrawRect(previewRect, new Color(0.3f, 0.3f, 0.3f, 1f));
                    Rect fillRect = new Rect(previewRect.x, previewRect.y,
                        previewRect.width * (1f - alpha), previewRect.height);
                    EditorGUI.DrawRect(fillRect, new Color(0.2f, 0.6f, 1f, 0.8f));
                    EditorGUI.LabelField(previewRect,
                        $"  Opacity: {alpha:P0} → Wall will be {(1f - alpha):P0} see-through",
                        EditorStyles.miniLabel);
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── Detection ────────────────────────────────────────────
            _detectFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_detectFoldout, "Detection");
            if (_detectFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    EditorGUILayout.PropertyField(_detectionMethod,
                        new GUIContent("Detection Method", "VisualBounds (No Collider needed!) or PhysicsRaycast (Requires Colliders)."));

                    if (_detectionMethod.enumValueIndex == (int)DetectionMethod.VisualBounds)
                    {
                        EditorGUILayout.HelpBox(
                            "VisualBounds Mode: Directly detects wall visual meshes.\n" +
                            "NO COLLIDER NEEDED on walls!",
                            MessageType.None);
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(_raycastLayerMask,
                            new GUIContent("Raycast Layers", "Physics layers to include in the raycast."));
                    }

                    EditorGUILayout.PropertyField(_targetOffset,
                        new GUIContent("Target Offset", "Offset from character pivot for raycast (aim at chest)."));

                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("Wall Tags", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(
                        "Objects with any of these tags will automatically be treated as wall candidates.",
                        MessageType.Info);

                    // Tag list
                    for (int i = 0; i < _wallTags.arraySize; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            SerializedProperty tagProp = _wallTags.GetArrayElementAtIndex(i);

                            // Tag dropdown
                            string currentTag = tagProp.stringValue;
                            string[] allTags  = UnityEditorInternal.InternalEditorUtility.tags;
                            int idx           = System.Array.IndexOf(allTags, currentTag);
                            if (idx < 0) idx = 0;

                            int newIdx = EditorGUILayout.Popup(idx, allTags);
                            if (newIdx >= 0 && newIdx < allTags.Length)
                                tagProp.stringValue = allTags[newIdx];

                            // Remove button
                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            {
                                _wallTags.DeleteArrayElementAtIndex(i);
                                i--;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+ Add Tag", GUILayout.Width(100)))
                    {
                        _wallTags.arraySize++;
                        _wallTags.GetArrayElementAtIndex(_wallTags.arraySize - 1).stringValue = "Untagged";
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── Highlight ────────────────────────────────────────────
            _hlFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_hlFoldout, "Character Highlight");
            if (_hlFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    EditorGUILayout.PropertyField(_enableHighlight,
                        new GUIContent("Enable Highlight", "Show outline glow on focus targets."));

                    if (_enableHighlight.boolValue)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(_highlightMode,
                            new GUIContent("Mode"));
                        EditorGUILayout.PropertyField(_highlightColor,
                            new GUIContent("Outline Color"));
                        EditorGUILayout.PropertyField(_highlightWidth,
                            new GUIContent("Outline Width"));
                        EditorGUI.indentLevel--;

                        // Color preview
                        EditorGUILayout.Space(2);
                        Rect colorPreview = EditorGUILayout.GetControlRect(false, 6);
                        EditorGUI.DrawRect(colorPreview, _highlightColor.colorValue);
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── Advanced ─────────────────────────────────────────────
            _advancedFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_advancedFoldout, "Advanced");
            if (_advancedFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    EditorGUILayout.PropertyField(_fallbackFadeShader,
                        new GUIContent("Fallback Shader",
                            "Shader used when the wall's original shader doesn't support transparency. " +
                            "Leave empty to auto-find 'DisasterSquad/WallFade'."));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.PropertyField(_extraRays,
                        new GUIContent("Extra Rays",
                            "Additional rays around the target for wider occlusion coverage."));

                    if (_extraRays.intValue > 0)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(_extraRaySpread,
                            new GUIContent("Spread Radius",
                                "World-unit radius of the extra ray circle around the target."));
                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            serializedObject.ApplyModifiedProperties();
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static void DrawHeader(string text)
        {
            EditorGUILayout.LabelField(text, s_headerStyle);
        }

        private static void InitStyles()
        {
            if (s_headerStyle != null) return;

            s_headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleLeft
            };

            s_boxStyle = new GUIStyle("helpbox")
            {
                padding = new RectOffset(8, 8, 6, 6)
            };
        }
    }
}

