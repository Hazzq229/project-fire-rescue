using UnityEditor;
using UnityEngine;
using DisasterSquad.WallOcclusion;

namespace DisasterSquad.WallOcclusion.Editor
{
    /// <summary>
    /// Custom inspector for <see cref="WallOcclusionController"/>.
    /// Provides drag-and-drop areas, runtime status, and scene gizmo controls.
    /// </summary>
    [CustomEditor(typeof(WallOcclusionController))]
    public class WallOcclusionControllerEditor : UnityEditor.Editor
    {
        // Serialized properties
        private SerializedProperty _profile;
        private SerializedProperty _focusTargets;
        private SerializedProperty _manualWalls;
        private SerializedProperty _autoDetectByTag;

        // Foldout states
        private bool _targetsFoldout = true;
        private bool _wallsFoldout   = true;
        private bool _statusFoldout  = true;

        // Styles
        private static GUIStyle s_headerStyle;
        private static GUIStyle s_boxStyle;
        private static GUIStyle s_dropAreaStyle;

        private void OnEnable()
        {
            _profile         = serializedObject.FindProperty("_profile");
            _focusTargets    = serializedObject.FindProperty("_focusTargets");
            _manualWalls     = serializedObject.FindProperty("_manualWalls");
            _autoDetectByTag = serializedObject.FindProperty("_autoDetectByTag");
        }

        public override void OnInspectorGUI()
        {
            InitStyles();
            serializedObject.Update();

            WallOcclusionController controller = (WallOcclusionController)target;

            EditorGUILayout.Space(4);
            DrawHeader("🎯  Wall Occlusion Controller");
            EditorGUILayout.Space(8);

            // ── Profile ──────────────────────────────────────────────
            EditorGUILayout.BeginVertical(s_boxStyle);
            {
                EditorGUILayout.PropertyField(_profile,
                    new GUIContent("Profile", "The WallOcclusionProfile asset with shared settings."));

                if (_profile.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "Assign a WallOcclusionProfile to configure fade, detection, and highlight settings.\n" +
                        "Create one via: Assets → Create → Wall Occlusion → Profile",
                        MessageType.Warning);
                }
                else
                {
                    EditorGUILayout.Space(2);
                    if (GUILayout.Button("Open Profile Inspector"))
                    {
                        Selection.activeObject = _profile.objectReferenceValue;
                    }
                }
            }
            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(6);

            // ── Focus Targets ────────────────────────────────────────
            _targetsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_targetsFoldout,
                $"Focus Targets ({_focusTargets.arraySize})");
            if (_targetsFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    // List
                    for (int i = 0; i < _focusTargets.arraySize; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.PropertyField(
                                _focusTargets.GetArrayElementAtIndex(i), GUIContent.none);

                            // Highlight indicator (editor only)
                            Transform t = (Transform)_focusTargets.GetArrayElementAtIndex(i)
                                .objectReferenceValue;
                            if (t != null)
                            {
                                GUILayout.Label("🎯", GUILayout.Width(20));
                            }

                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            {
                                _focusTargets.DeleteArrayElementAtIndex(i);
                                // DeleteArrayElement on object references may leave a null entry
                                if (i < _focusTargets.arraySize &&
                                    _focusTargets.GetArrayElementAtIndex(i).objectReferenceValue == null)
                                    _focusTargets.DeleteArrayElementAtIndex(i);
                                i--;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    // Drop area
                    DrawDropArea("Drag & Drop Character GameObjects here",
                        droppedObj =>
                        {
                            if (droppedObj is GameObject go)
                            {
                                // Check if already in list
                                for (int i = 0; i < _focusTargets.arraySize; i++)
                                {
                                    var existing = (Transform)_focusTargets
                                        .GetArrayElementAtIndex(i).objectReferenceValue;
                                    if (existing == go.transform) return;
                                }
                                _focusTargets.arraySize++;
                                _focusTargets.GetArrayElementAtIndex(_focusTargets.arraySize - 1)
                                    .objectReferenceValue = go.transform;
                            }
                        });

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+ Add Slot", GUILayout.Width(100)))
                    {
                        _focusTargets.arraySize++;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            EditorGUILayout.Space(4);

            // ── Walls ────────────────────────────────────────────────
            _wallsFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_wallsFoldout,
                $"Walls ({_manualWalls.arraySize} manual)");
            if (_wallsFoldout)
            {
                EditorGUILayout.BeginVertical(s_boxStyle);
                {
                    EditorGUILayout.PropertyField(_autoDetectByTag,
                        new GUIContent("Auto-Detect by Tag",
                            "Also detect walls using tags defined in the profile."));

                    EditorGUILayout.Space(4);
                    EditorGUILayout.LabelField("Manual Walls", EditorStyles.boldLabel);

                    // List
                    for (int i = 0; i < _manualWalls.arraySize; i++)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            EditorGUILayout.PropertyField(
                                _manualWalls.GetArrayElementAtIndex(i), GUIContent.none);

                            Renderer r = (Renderer)_manualWalls.GetArrayElementAtIndex(i)
                                .objectReferenceValue;
                            if (r != null)
                            {
                                // Show fading status in play mode
                                if (Application.isPlaying)
                                {
                                    var fadeable = r.GetComponent<WallFadeable>();
                                    if (fadeable != null && fadeable.IsFading)
                                    {
                                        GUILayout.Label(
                                            $"α:{fadeable.CurrentAlpha:F2}",
                                            EditorStyles.miniLabel, GUILayout.Width(40));
                                    }
                                }

                                GUILayout.Label("🧱", GUILayout.Width(20));
                            }

                            if (GUILayout.Button("✕", GUILayout.Width(24)))
                            {
                                _manualWalls.DeleteArrayElementAtIndex(i);
                                if (i < _manualWalls.arraySize &&
                                    _manualWalls.GetArrayElementAtIndex(i).objectReferenceValue == null)
                                    _manualWalls.DeleteArrayElementAtIndex(i);
                                i--;
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }

                    // Drop area
                    DrawDropArea("Drag & Drop Wall GameObjects / Renderers here",
                        droppedObj =>
                        {
                            Renderer[] renderers = null;

                            if (droppedObj is GameObject go)
                                renderers = go.GetComponentsInChildren<Renderer>();
                            else if (droppedObj is Renderer r)
                                renderers = new Renderer[] { r };

                            if (renderers == null || renderers.Length == 0)
                            {
                                Debug.LogWarning(
                                    $"[WallOcclusion] Dropped object '{droppedObj.name}' has no Renderer on itself or children.");
                                return;
                            }

                            foreach (var rend in renderers)
                            {
                                if (rend == null) continue;

                                // Check duplicates
                                bool exists = false;
                                for (int i = 0; i < _manualWalls.arraySize; i++)
                                {
                                    var existing = (Renderer)_manualWalls
                                        .GetArrayElementAtIndex(i).objectReferenceValue;
                                    if (existing == rend)
                                    {
                                        exists = true;
                                        break;
                                    }
                                }

                                if (!exists)
                                {
                                    _manualWalls.arraySize++;
                                    _manualWalls.GetArrayElementAtIndex(_manualWalls.arraySize - 1)
                                        .objectReferenceValue = rend;
                                }
                            }
                        });

                    EditorGUILayout.BeginHorizontal();
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("+ Add Slot", GUILayout.Width(100)))
                    {
                        _manualWalls.arraySize++;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndFoldoutHeaderGroup();

            // ── Runtime Status (play mode only) ──────────────────────
            if (Application.isPlaying)
            {
                EditorGUILayout.Space(6);
                _statusFoldout = EditorGUILayout.BeginFoldoutHeaderGroup(_statusFoldout,
                    "Runtime Status");
                if (_statusFoldout)
                {
                    EditorGUILayout.BeginVertical(s_boxStyle);
                    {
                        EditorGUILayout.LabelField("Status", "Active ✓",
                            EditorStyles.boldLabel);

                        EditorGUILayout.LabelField("Focus Targets",
                            _focusTargets.arraySize.ToString());

                        EditorGUILayout.LabelField("Total Walls Tracked",
                            _manualWalls.arraySize.ToString());

                        EditorGUILayout.Space(4);
                        EditorGUILayout.HelpBox(
                            "Select this object and look at the Scene View to see " +
                            "raycast gizmos (green = clear, red = occluded).",
                            MessageType.Info);
                    }
                    EditorGUILayout.EndVertical();
                }
                EditorGUILayout.EndFoldoutHeaderGroup();

                // Force repaint for live status
                Repaint();
            }

            serializedObject.ApplyModifiedProperties();
        }

        // ── Drag & Drop Area ─────────────────────────────────────────

        private void DrawDropArea(string label, System.Action<Object> onDrop)
        {
            EditorGUILayout.Space(4);
            Rect dropRect = GUILayoutUtility.GetRect(0, 36, GUILayout.ExpandWidth(true));

            // Draw background
            Color bgColor = EditorGUIUtility.isProSkin
                ? new Color(0.22f, 0.22f, 0.22f)
                : new Color(0.85f, 0.85f, 0.85f);

            GUI.color = bgColor;
            GUI.Box(dropRect, GUIContent.none, s_dropAreaStyle);
            GUI.color = Color.white;

            // Draw label
            GUI.Label(dropRect, label, new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = EditorGUIUtility.isProSkin
                    ? new Color(0.7f, 0.7f, 0.7f) : new Color(0.4f, 0.4f, 0.4f) }
            });

            // Handle drag events
            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropRect.Contains(evt.mousePosition)) break;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();
                        foreach (Object obj in DragAndDrop.objectReferences)
                            onDrop?.Invoke(obj);
                    }
                    evt.Use();
                    break;
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static void DrawHeader(string text)
        {
            if (s_headerStyle == null)
                InitStyles();
            EditorGUILayout.LabelField(text, s_headerStyle);
        }

        private static void InitStyles()
        {
            s_headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize  = 14,
                alignment = TextAnchor.MiddleLeft
            };

            s_boxStyle = new GUIStyle("helpbox")
            {
                padding = new RectOffset(8, 8, 6, 6)
            };

            s_dropAreaStyle = new GUIStyle("box")
            {
                border  = new RectOffset(2, 2, 2, 2),
                margin  = new RectOffset(4, 4, 2, 2),
                padding = new RectOffset(4, 4, 4, 4)
            };
        }
    }
}

