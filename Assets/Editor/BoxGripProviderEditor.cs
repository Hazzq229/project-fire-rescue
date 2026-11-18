using TA.AdaptiveAnimation;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(BoxGripProvider)), CanEditMultipleObjects]
public sealed class BoxGripProviderEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        EditorGUILayout.HelpBox("Manual memakai Grip Height/Depth pada bounds collider. Auto Fit mencari kontak mesh dan menyimpan nilai manual tanpa menimpanya. Fitting collider untuk tabrakan tetap aktif pada kedua mode.", MessageType.Info);
        if (!Application.isPlaying || targets.Length != 1) return;
        var provider = (BoxGripProvider)target;
        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Effective Grip Height", provider.EffectiveGripHeight);
            EditorGUILayout.FloatField("Effective Grip Depth", provider.EffectiveGripDepth);
        }
    }
}
